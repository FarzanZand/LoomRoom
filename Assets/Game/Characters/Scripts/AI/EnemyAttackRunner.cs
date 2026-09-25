using System.Collections.Generic;
using UnityEngine;

// One enemy's attack lifecycle: cooldowns, picking an attack, the swing itself (windup
// tracking, commit, whoosh, hit timing) and the recovery that follows. Plain C# owned by
// EnemyBrain, so every serialized field stays on the brain and prefabs are untouched.
public class EnemyAttackRunner
{
    readonly EnemyBrain brain;
    readonly List<float> cooldowns = new();

    EnemyAttack current;
    float   attackStartedAt;
    bool    attackWasPlaying;
    bool    swingAudioPlayed;
    Vector3 committedForward;
    bool    directionCommitted;
    bool    fallbackHitPending;
    float   fallbackHitAt;
    float   pendingStagger;

    // An EnableHitbox event that arrived before the minimum windup, replayed once it has passed.
    bool  hitboxOpenPending;
    int   pendingHitboxIndex;
    float hitboxRequestedAt;
    float pendingHitboxWindow = -1f;
    float hitboxCloseAt = -1f;

    public EnemyAttackRunner(EnemyBrain brain) { this.brain = brain; }

    Character  Character  => brain.Character;
    EnemyMotor Motor      => brain.Motor;
    Perception Perception => brain.Perception;
    List<EnemyAttack> Attacks => brain.Attacks;

    static CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;
    public static float MinWindup => Tuning != null ? Tuning.enemyMinimumWindup : 0.3f;
    static float CommitTime   => Tuning != null ? Tuning.enemyAttackCommitTime : 0.4f;
    static float RecoveryTime => Tuning != null ? Tuning.enemyRecoveryTime : 0.18f;

    public bool IsSwinging => current != null;

    bool SwingLive => brain.isActiveAndEnabled && Character.IsAlive && brain.State == EnemyState.Attack && current != null
        && (!GameManager.HasInstance || GameManager.Instance.SimulationActive);
    bool PastMinWindup => Time.time >= attackStartedAt + MinWindup;

    public bool CanOpenHitbox => SwingLive && PastMinWindup;

    // ── Lifecycle ─────────────────────────────────────────────────────

    public void ResetCooldowns()
    {
        cooldowns.Clear();
        for (int i = 0; i < Attacks.Count; i++) cooldowns.Add(0f);
    }

    // Once per frame, before the state handler.
    public void Tick()
    {
        for (int i = 0; i < cooldowns.Count; i++) cooldowns[i] -= Time.deltaTime;

        if (fallbackHitPending && Time.time >= fallbackHitAt) DealFallbackHit();

        if (hitboxOpenPending && PastMinWindup)
        {
            hitboxOpenPending = false;
            var relay = brain.AttackRelay;
            if (relay != null && CanOpenHitbox)
            {
                relay.EnableHitboxAt(pendingHitboxIndex);
                // The clip already closed its window: keep the same length, shifted later.
                if (pendingHitboxWindow >= 0f) hitboxCloseAt = Time.time + pendingHitboxWindow;
            }
        }
        if (hitboxCloseAt >= 0f && Time.time >= hitboxCloseAt)
        {
            hitboxCloseAt = -1f;
            if (brain.AttackRelay != null) brain.AttackRelay.DisableHitbox();
        }
    }

    public void Cancel()
    {
        if (Character != null && Character.Animator != null)
            foreach (var attack in Attacks)
                if (attack != null && Character.HasParameter(Character.Animator, attack.animatorTrigger, AnimatorControllerParameterType.Trigger))
                    Character.Animator.ResetTrigger(attack.animatorTrigger);
        fallbackHitPending = false; current = null; attackWasPlaying = false;
        if (brain.AttackRelay != null) brain.AttackRelay.DisableHitbox();
        hitboxOpenPending = false; hitboxCloseAt = -1f; pendingHitboxWindow = -1f;
        pendingStagger = 0f;
        if (Motor != null) Motor.SuppressKnockback = false;
    }

    // A stagger taken mid-swing waits for the swing to finish: committed swings are never interrupted.
    public void QueueStagger(float seconds) => pendingStagger = Mathf.Max(pendingStagger, seconds);

    // ── Picking and starting ──────────────────────────────────────────

    public EnemyAttack PickAttack(float dist)
    {
        var attacks = Attacks;
        float totalWeight = 0f;
        for (int i = 0; i < attacks.Count; i++)
            if (Usable(i, dist)) totalWeight += attacks[i].weight;
        if (totalWeight <= 0f) return null;

        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < attacks.Count; i++)
        {
            if (!Usable(i, dist)) continue;
            roll -= attacks[i].weight;
            if (roll <= 0f) return attacks[i];
        }
        return null;
    }

    bool Usable(int i, float dist)
    {
        var a = Attacks[i];
        return a != null && dist >= a.minRange && dist <= a.EffectiveMaxRange && i < cooldowns.Count && cooldowns[i] <= 0f;
    }

    public void StartAttack(EnemyAttack attack)
    {
        current = attack;
        attackStartedAt = Time.time;
        committedForward = brain.transform.forward;
        directionCommitted = false;
        attackWasPlaying = false;
        swingAudioPlayed = false;
        var hitbox = brain.AttackRelay != null ? brain.AttackRelay.GetHitbox(attack.hitboxIndex) : null;
        if (hitbox != null) hitbox.SetProfile(attack.hit);

        brain.SetState(EnemyState.Attack);
        Motor.SuppressKnockback = true;
        // Clear a flinch queued earlier this frame before it can override this swing.
        if (Character.data != null && Character.HasParameter(Character.Animator, Character.data.hurtTrigger, AnimatorControllerParameterType.Trigger))
            Character.Animator.ResetTrigger(Character.data.hurtTrigger);
        Character.TriggerAnimation(attack.animatorTrigger);

        // Without a hitbox the hit lands either on the clip's OnAttackHit animation event
        // or, if the clip has none, after fallbackHitDelay seconds.
        if (hitbox == null)
        {
            fallbackHitPending = true;
            fallbackHitAt = attack.fallbackHitDelay >= 0f ? Time.time + Mathf.Max(attack.fallbackHitDelay, MinWindup) : float.MaxValue;
        }
    }

    // ── The swing ─────────────────────────────────────────────────────

    // EnemyState.Attack handler.
    public void HandleAttackState()
    {
        var target = Perception.Target;
        if (target == null) { Cancel(); brain.SetState(brain.Profile.defaultState); return; }
        if (current == null) { brain.SetState(EnemyState.Chase); return; }

        Motor.Stop();

        // Windup: keep tracking the target so circling does not trivially dodge. Then commit.
        float sinceStart = Time.time - attackStartedAt;
        if (!directionCommitted && sinceStart < CommitTime && brain.Profile.windupTrackSpeed > 0f)
        {
            Motor.LookAt(target.transform.position, brain.Profile.windupTrackSpeed);
        }
        else
        {
            CommitDirection();
            if (!swingAudioPlayed) { swingAudioPlayed = true; PlaySwingAudio(); }
        }

        if (IsPlayingAttack()) { attackWasPlaying = true; return; }

        // Swing finished (or never started because the animator has no such state).
        float startupTimeout = Mathf.Max(0.5f, current.fallbackHitDelay + 0.1f);
        if (attackWasPlaying || sinceStart > startupTimeout)
        {
            // Cooldown counts from the END of the swing, so there is always an opening
            // between attacks where the enemy spaces and circles instead of chaining swings.
            int index = Attacks.IndexOf(current);
            if (index >= 0 && index < cooldowns.Count)
                cooldowns[index] = current.cooldown * (Tuning != null ? Tuning.enemyCooldownMultiplier : 1f);

            float recovery = Mathf.Max(RecoveryTime, pendingStagger);
            Cancel();
            brain.BeginRecovery(recovery);
            brain.SetState(EnemyState.Chase);
        }
    }

    public void CommitDirection()
    {
        if (current == null || directionCommitted) return;
        directionCommitted = true;
        committedForward = brain.transform.forward;
        Motor.ClearLookTarget();
    }

    // The whoosh plays when the windup commits, just before the hit event.
    void PlaySwingAudio()
    {
        if (current == null || !AudioManager.HasInstance) return;
        var hit = current.hit;
        AudioData audio = hit.useDefaultEffects ? (Tuning != null ? Tuning.defaultSwingAudio : null) : hit.swingAudio;
        if (audio != null) AudioManager.Instance.PlaySFXData(audio, brain.transform.position + Vector3.up);
    }

    public bool IsPlayingAttack()
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).IsTag("Attack")) return true;
            if (anim.IsInTransition(layer) && anim.GetNextAnimatorStateInfo(layer).IsTag("Attack")) return true;
        }
        return false;
    }

    // ── Hit timing ────────────────────────────────────────────────────

    // Animation event: the moment the swing connects. Too early and it waits for the minimum windup.
    public void OnAttackHit()
    {
        if (!fallbackHitPending) return;
        if (PastMinWindup) DealFallbackHit();
        else fallbackHitAt = Mathf.Min(fallbackHitAt, attackStartedAt + MinWindup);
    }

    // An EnableHitbox event before the minimum windup is deferred rather than dropped.
    public bool DeferHitboxIfEarly(int index)
    {
        if (!SwingLive || PastMinWindup) return false;
        hitboxOpenPending = true;
        pendingHitboxIndex = index;
        hitboxRequestedAt = Time.time;
        pendingHitboxWindow = -1f;
        return true;
    }

    public void NotifyHitboxClosed()
    {
        if (hitboxOpenPending && pendingHitboxWindow < 0f) pendingHitboxWindow = Time.time - hitboxRequestedAt;
    }

    // Direct hit for enemies without a Hitbox.
    void DealFallbackHit()
    {
        fallbackHitPending = false;
        var target = Perception.Target;
        if (!CanOpenHitbox || target == null || !target.IsAlive || !FactionRules.IsHostile(Character.Faction, target.Faction)) return;
        CommitDirection();

        // Facing was required to start the swing and tracking ran through the windup; at
        // impact only distance and a generous arc around the committed direction count.
        Vector3 self = brain.transform.position;
        float dist = Perception.HorizontalDist(self, target.transform.position);
        if (dist > current.EffectiveMaxRange * 1.1f || Mathf.Abs(target.transform.position.y - self.y) > 1f) return;
        Vector3 toward = target.transform.position - self; toward.y = 0f;
        float arc = Tuning != null ? Mathf.Min(180f, Tuning.enemyHitFacingAngle * Tuning.meleeWidthMultiplier) : 60f;
        if (Vector3.Angle(committedForward, toward) > arc) return;

        Vector3 dir = toward.sqrMagnitude > 0.0001f ? toward.normalized : brain.transform.forward;

        float force = current.hit.knockbackForce >= 0f ? current.hit.knockbackForce
                    : (Character.FX != null ? Character.FX.KnockbackForce : 1f);
        if (CombatManager.HasInstance) force = CombatManager.Instance.ScaleKnockback(force);

        float damage = Character.Stats != null ? Character.Stats.GetFinal(StatType.AttackDamage) : 0f;
        var info = new DamageInfo
        {
            Profile        = current.hit,
            Amount         = damage * current.hit.damageMultiplier,
            Source         = Character,
            HitPoint       = target.transform.position + Vector3.up * 1f,
            Direction      = dir,
            KnockbackForce = force,
        };

        var damageable = target.GetComponent<IDamageable>();
        if (damageable == null) return;
        if (Tuning != null && !Tuning.HasMeleeLineOfSight(Character, target, info.HitPoint)) return;
        damageable.TakeDamage(info);
    }
}
