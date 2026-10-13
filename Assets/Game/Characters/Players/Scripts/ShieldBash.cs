using System.Collections.Generic;
using UnityEngine;

// Heathen's Holy Diver (CombatManager > Player > Shield bash): with the shield up, attack thrusts it forward and the
// player dashes ahead until the first enemy in the way, hits it with Holy damage (more for more armour) and stuns it.
// One use per cooldown; while it cools down, block and attack swing as usual (PlayerCombat asks TryStart first).
// The arms play Shield_Bash on the LeftArm layer (trigger ShieldBash, state tag ShieldBash), the shield glows and gold
// streaks rush past the view while it dashes.
public class ShieldBash : MonoBehaviour
{
    [SerializeField] string bashTrigger = "ShieldBash";

    enum Phase { None, Windup, Dash, Recovery }
    Phase phase;
    float phaseEnds, readyAt = -10f, travelled, glow;
    Vector3 direction, lastPosition;

    Player player;
    PlayerMotor motor;
    Animator arms;
    ParticleSystem streaks;
    readonly List<Renderer> shieldRenderers = new();
    readonly List<MaterialPropertyBlock> shieldOriginals = new();
    MaterialPropertyBlock glowBlock;
    // The hit flash's way (CharacterFX): an HDR tint on the base colour works on Pixel Lit and URP Lit alike (an emission
    // keyword can't be switched on through a property block). A small gold light on the shield lights what it passes.
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
    Light glowLight;

    static CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;

    // Winding up, dashing or recovering: the player neither swings nor guards.
    public bool Busy => phase != Phase.None;
    public bool Dashing => phase == Phase.Dash;
    public float CooldownLeft => Mathf.Max(0f, readyAt - Time.time);
    bool ShieldHeld => player.Equipment != null && player.Equipment.Get(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;

    void Awake()
    {
        player = GetComponent<Player>();
        motor = GetComponent<PlayerMotor>();
        var relay = GetComponentInChildren<WeaponAnimationRelay>(true);
        if (relay != null) arms = relay.GetComponent<Animator>();
    }

    void OnDisable() => Stop();

    // Block held and attack pressed: bash if it is switched on, ready, and a shield is in hand.
    public bool TryStart()
    {
        var t = Tuning;
        if (t == null || !t.enableShieldBash || Busy || Time.time < readyAt || !player.IsActive || !ShieldHeld) return false;
        if (motor != null && !motor.IsGrounded) return false;
        var stats = player.Stats;
        if (stats != null && stats.HasStat(StatType.MaxStamina) && t.shieldBashStaminaCost > 0f &&
            !stats.TryUseStamina(t.shieldBashStaminaCost, player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f))
            return false;   // TryUseStamina reports it
        readyAt = Time.time + t.shieldBashCooldown;
        var yaw = player.Look != null ? player.Look.YawTransform : transform;
        direction = yaw.forward; direction.y = 0f;
        direction = direction.sqrMagnitude > .001f ? direction.normalized : transform.forward;
        phase = Phase.Windup;
        phaseEnds = Time.time + t.shieldBashWindup;
        travelled = 0f;
        glow = 1f;
        AnimatorHelper.SetTrigger(arms, bashTrigger);
        if (t.shieldBashAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(t.shieldBashAudio);
        CollectShield();
        return true;
    }

    void Update()
    {
        var t = Tuning;
        if (phase == Phase.None || t == null) { FadeGlow(); return; }
        // A menu, a cutscene or death ends it where it is.
        if (!player.IsActive || !player.IsAlive || (GameManager.HasInstance && !GameManager.Instance.GameplayActive)) { Stop(); return; }
        switch (phase)
        {
            case Phase.Windup:
                if (Time.time >= phaseEnds) BeginDash();
                break;
            case Phase.Dash:
                UpdateDash(t);
                break;
            case Phase.Recovery:
                if (Time.time >= phaseEnds) phase = Phase.None;
                break;
        }
        FadeGlow();
    }

    void BeginDash()
    {
        phase = Phase.Dash;
        lastPosition = transform.position;
        Streaks(true);
    }

    void UpdateDash(CombatManager t)
    {
        // Measured, not assumed: a wall or a ledge stops the dash short.
        var moved = transform.position - lastPosition; moved.y = 0f;
        float step = t.shieldBashSpeed * Time.deltaTime;
        if (travelled > .3f && moved.magnitude < step * .25f) { EndDash(t); return; }
        travelled += moved.magnitude;
        lastPosition = transform.position;

        var target = FindTarget(t, step);
        if (target != null) { Hit(target, t); EndDash(t); return; }
        if (travelled >= t.shieldBashDistance) { EndDash(t); return; }
        if (motor != null) motor.DashVelocity = direction * t.shieldBashSpeed;
    }

    // The nearest living enemy ahead within reach of this frame's step, in the dash's cone and in sight.
    EnemyBrain FindTarget(CombatManager t, float step)
    {
        float reach = t.personalSpace + .45f + step;
        float cos = Mathf.Cos(t.shieldBashAngle * Mathf.Deg2Rad);
        EnemyBrain best = null; float bestDistance = float.MaxValue;
        foreach (var enemy in EnemyBrain.Active)
        {
            if (enemy == null || enemy.State == EnemyState.Dead || enemy.Character == null || !enemy.Character.IsAlive) continue;
            if (!FactionRules.IsHostile(player.Faction, enemy.Character.Faction)) continue;
            var to = enemy.transform.position - transform.position;
            if (Mathf.Abs(to.y) > 2f) continue;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > reach || distance >= bestDistance) continue;
            if (distance > .1f && Vector3.Dot(to / distance, direction) < cos) continue;
            if (!t.HasMeleeLineOfSight(player, enemy.Character, Chest(enemy.Character))) continue;
            best = enemy; bestDistance = distance;
        }
        return best;
    }

    static Vector3 Chest(Character c)
    {
        var col = c.GetComponent<Collider>();
        return col != null ? col.bounds.center : c.transform.position + Vector3.up;
    }

    void Hit(EnemyBrain enemy, CombatManager t)
    {
        var target = enemy.Character;
        // Stunned first, so the hit's hurt reaction plays (a swing in progress would swallow it).
        enemy.Stun(t.shieldBashStun);
        float armor = player.Stats != null ? player.Stats.GetFinal(StatType.Armor) : 0f;
        var chest = Chest(target);
        var col = target.GetComponent<Collider>();
        var info = new DamageInfo
        {
            Amount = t.shieldBashDamage + Mathf.Max(0f, armor) * t.shieldBashDamagePerArmor,
            Source = player,
            Weapon = player.Equipment != null ? player.Equipment.Get(EquipmentSlot.LeftHand) : null,
            HitPoint = col != null ? col.ClosestPoint(transform.position + Vector3.up * (chest.y - transform.position.y)) : chest,
            Direction = direction,
            SwingDirection = direction,
            KnockbackForce = t.ScaleKnockback(t.shieldBashKnockback),
            Magic = true,   // Holy: no armour taken off, no shield blocks it
        };
        var damageable = target.GetComponent<IDamageable>() ?? target.GetComponentInChildren<IDamageable>();
        damageable?.TakeDamage(info);
        if (t.shieldBashHitAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(t.shieldBashHitAudio, info.HitPoint);
        NoiseEvents.Report(transform.position, t.fightNoiseRadius, player);
    }

    void EndDash(CombatManager t)
    {
        if (motor != null) motor.DashVelocity = Vector3.zero;
        Streaks(false);
        phase = Phase.Recovery;
        phaseEnds = Time.time + t.shieldBashRecovery;
    }

    void Stop()
    {
        if (motor != null) motor.DashVelocity = Vector3.zero;
        Streaks(false);
        phase = Phase.None;
        glow = 0f;
        FadeGlow();
    }

    // ── Look ──────────────────────────────────────────────────────────

    void Streaks(bool on)
    {
        var t = Tuning;
        if (on && streaks == null && t != null && t.shieldBashStreaks != null && player.CameraRig != null && player.CameraRig.Camera != null)
        {
            var go = Instantiate(t.shieldBashStreaks, player.CameraRig.Camera.transform);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            streaks = go.GetComponentInChildren<ParticleSystem>();
        }
        if (streaks == null) return;
        if (on) streaks.Play(true);
        else streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void CollectShield()
    {
        RestoreShield();
        var held = player.Equipment != null ? player.Equipment.HeldObject(EquipmentSlot.LeftHand) : null;
        if (held == null) return;
        held.GetComponentsInChildren(true, shieldRenderers);
        if (glowLight == null)
        {
            glowLight = new GameObject("Shield bash glow").AddComponent<Light>();
            glowLight.type = LightType.Point; glowLight.range = 4f; glowLight.shadows = LightShadows.None;
        }
        glowLight.transform.SetParent(held.transform, false);
        glowLight.transform.localPosition = Vector3.zero;
        foreach (var r in shieldRenderers)
        {
            var original = new MaterialPropertyBlock();
            r.GetPropertyBlock(original);
            shieldOriginals.Add(original);
        }
    }

    // Full through the windup and the dash, then gone over the recovery.
    void FadeGlow()
    {
        var t = Tuning;
        if (shieldRenderers.Count == 0) return;
        if (phase == Phase.Recovery && t != null) glow = Mathf.MoveTowards(glow, 0f, Time.deltaTime / Mathf.Max(.05f, t.shieldBashRecovery));
        else if (phase == Phase.None) glow = 0f;
        if (glow <= 0f) { RestoreShield(); return; }
        glowBlock ??= new MaterialPropertyBlock();
        var gold = t != null ? t.shieldBashGlow : Color.white;
        var colour = Color.Lerp(Color.white, gold, glow);
        for (int i = 0; i < shieldRenderers.Count; i++)
        {
            var r = shieldRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(glowBlock);
            glowBlock.SetColor(BaseColorId, colour);
            glowBlock.SetColor(ColorId, colour);
            r.SetPropertyBlock(glowBlock);
        }
        if (glowLight != null)
        {
            glowLight.enabled = true;
            glowLight.color = new Color(gold.r, gold.g, gold.b) / Mathf.Max(.01f, gold.maxColorComponent);
            glowLight.intensity = 5f * glow;
        }
    }

    void RestoreShield()
    {
        if (glowLight != null) glowLight.enabled = false;
        for (int i = 0; i < shieldRenderers.Count && i < shieldOriginals.Count; i++)
            if (shieldRenderers[i] != null) shieldRenderers[i].SetPropertyBlock(shieldOriginals[i]);
        shieldRenderers.Clear(); shieldOriginals.Clear();
    }
}
