using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// Central toggles and tuning for combat feel. Hit stop lives here too, so there is
// exactly one entry point and a hit can never freeze the game twice.
public class CombatManager : Singleton<CombatManager>
{
    [Header("Melee hit tolerance")]
    [Min(1f), Tooltip("Reach multiplier for player/enemy melee queries and enemy attack selection.")]
    public float meleeReachMultiplier = 1.2f;
    [Min(1f), Tooltip("Thickness multiplier for melee hit queries; also widens fallback enemy hit arcs.")]
    public float meleeWidthMultiplier = 1.25f;

    [Header("Hit Stop")]
    public bool hitStopEnabled = true;
    [FormerlySerializedAs("hitStopDuration"), Min(0), Tooltip("Seconds the game slows when a light attack lands.")]
    public float lightHitStopDuration = 0.06f;
    [FormerlySerializedAs("hitStopTimeScale"), Range(0f, 0.5f)]
    [Tooltip("Time scale during a light hit stop. A hair above zero keeps animations creeping, which reads better than a hard freeze.")]
    public float lightHitStopTimeScale = 0.05f;
    [Min(0), Tooltip("Seconds the game slows when a heavy (charged) attack lands.")]
    public float heavyHitStopDuration = 0.11f;
    [Range(0f, 0.5f), Tooltip("Time scale during a heavy hit stop.")]
    public float heavyHitStopTimeScale = 0.02f;
    [Min(0), Tooltip("Seconds time takes to ramp back to full speed after the stop, instead of snapping. Softens the release.")]
    public float hitStopRecovery = 0.06f;
    [Min(0), Tooltip("How far the struck character's model shakes during the stop, in metres at its own scale. Heavy hits shake twice as far.")]
    public float hitStopShake = 0.035f;
    [Tooltip("Also hit-stop when an enemy lands a hit on the player.")]
    public bool hitStopOnPlayerHurt = false;

    [Header("Critical hits and backstabs (player attacks)")]
    public bool criticalHitsEnabled = true;
    [Range(0f, 1f)] public float critChance = .08f;
    [Min(1f)] public float critMultiplier = 1.75f;
    [Min(1f), Tooltip("Hit stop length multiplier for a critical hit.")]
    public float critHitStopScale = 2.5f;
    public AudioData critAudio;
    [Tooltip("Hitting an enemy that is not chasing or attacking, or one facing away, is a backstab.")]
    public bool backstabsEnabled = true;
    [Min(1f)] public float backstabMultiplier = 2.5f;
    [Range(-1f, 1f), Tooltip("A hit counts as from behind when dot(victim forward, attack direction) is above this.")]
    public float backstabBehindDot = .35f;
    [Tooltip("Also backstab alert enemies when struck from behind. Off: only unaware enemies.")]
    public bool backstabAlertFromBehind = true;
    [Min(1f)] public float backstabHitStopScale = 3f;
    public AudioData backstabAudio;
    [Min(1f), Tooltip("Size multiplier of the damage number for critical hits and backstabs.")]
    public float critNumberScale = 1.6f;

    [Header("Stealth")]
    [SerializeField, Range(0, 1), Tooltip("How close the player can get behind an enemy before it notices, as a share of its close radius.")] float unseenCloseScale = .6f;
    [SerializeField, Range(0, 1), Tooltip("The same while sneaking.")] float sneakCloseScale = .2f;
    [SerializeField, Range(0, 1), Tooltip("Enemy sight range while the player sneaks, as a share of normal.")] float sneakSightScale = .6f;

    [Header("Death")]
    [Tooltip("Enemies collapse as physics ragdolls. Off plays the death animation instead.")]
    public bool ragdollDeath = true;
    [Tooltip("Impulse applied along the killing blow's direction.")]
    [Min(0)] public float ragdollImpulse = 4.5f;
    [Min(0)] public float ragdollUpwardImpulse = 1.5f;
    [Tooltip("Enemy loot stays on the corpse until the player searches it. Off scatters it on death.")]
    public bool lootableCorpses = true;
    [Tooltip("Seconds before a corpse is removed. 0 keeps corpses for the whole floor.")]
    [Min(0)] public float corpseLifetime = 0f;

    [Header("Knockback")]
    public bool knockbackEnabled = true;
    [Tooltip("Off: the player's hits never push enemies back. Their hits on the player still do.")]
    public bool enemiesKnockedBack = true;
    [Min(0), Tooltip("Scales the push enemies take from the player's hits (on top of Knockback Force Multiplier). Small, so a hit jolts them without carrying them out of their attack range.")]
    public float enemyKnockbackScale = .25f;
    [Tooltip("Scales every knockback force in the game.")]
    public float knockbackForceMultiplier = 1f;
    [Tooltip("Seconds a knocked-back NavMesh character slides before regaining control.")]
    public float knockbackDuration = 0.25f;

    [Header("Player knockback safety")]
    [Tooltip("Maximum horizontal knockback speed, in world units per second. Never changes vertical velocity.")]
    [Min(0)] public float playerKnockbackSpeedLimit = 2.5f;
    [Tooltip("How quickly player knockback loses speed. A 2.5 speed kick at 12 decay travels about 0.26 units.")]
    [Min(.1f)] public float playerKnockbackDecay = 12f;

    [Header("Default Hit Effects")]
    [Tooltip("Pool of hit particle prefabs. One is chosen at random when the weapon uses default effects.")]
    public List<GameObject> hitParticlePrefabs = new();
    [Tooltip("Hit audio used when the weapon uses default effects.")]
    public AudioData defaultHitAudio;
    [Tooltip("Swing audio used when the weapon uses default effects.")]
    public AudioData defaultSwingAudio;
    [Tooltip("Heavy swing audio used by weapons with default effects.")]
    public AudioData defaultHeavySwingAudio;

    [Header("Hit Reaction")]
    public bool hitReactionEnabled = true;
    [Tooltip("Peak rotation angle (degrees) applied to the bone closest to the hit.")]
    public float hitReactionAngle = 20f;
    [Tooltip("How quickly the bones move into the reaction pose. Lower = smoother snap-in.")]
    public float hitReactionAttackSpeed = 20f;
    [Tooltip("How quickly the offset decays back to the animated pose.")]
    public float hitReactionDamping = 8f;
    [Tooltip("How many parent bones above the hit bone also receive an offset.")]
    public int hitReactionInfluenceDepth = 3;
    [Tooltip("Fraction of strength passed to each successive parent bone.")]
    [Range(0f, 1f)] public float hitReactionParentFalloff = 0.45f;

    [Header("Hit Flash")]
    public bool hitFlashEnabled = true;
    [ColorUsage(false, true), Tooltip("Tint every character flashes when hit. Values above 1 glow.")]
    public Color hitFlashColor = new Color(2.4f, 1.9f, 1.45f);
    [Tooltip("Seconds the flash tint stays on the character's renderers.")]
    public float hitFlashDuration = 0.1f;

    [Header("Armor")]
    [Tooltip("Barony's armor effectiveness: armor is taken off this share of a hit, the rest always lands, so armor never makes anyone untouchable. A successful block uses the full hit (1).")]
    [Range(0f, 1f)] public float armorEffectiveness = .75f;

    [Header("Spacing")]
    [Min(0), Tooltip("No one stands closer than this to an enemy (centre to centre): the player cannot walk further in, and an enemy that is not swinging steps back out.")]
    public float personalSpace = 1.1f;

    [Header("Block")]
    [Tooltip("Seconds after attacking that block is locked out. Set to just under your attack windup length.")]
    public float blockCancelWindow = 0.5f;
    [Tooltip("Dot product threshold: a hit is frontal (blockable) when dot(facing, hitDir) is below this.")]
    [Range(-1f, 1f)] public float blockFrontalDot = -0.3f;
    [Min(0), Tooltip("Stamina spent on each successfully blocked hit.")]
    public float blockStaminaCost = 6f;
    [Min(0), Tooltip("Stamina drained each second while holding the shield up.")]
    public float shieldStaminaPerSecond = 2f;

    [Header("Attack responsiveness")]
    [Range(.5f,2f)] public float playerAttackSpeed = 1.12f;
    [Min(0)] public float attackInputBuffer = .22f;
    [Range(1,16)] public int weaponSweepSteps = 6;

    [Header("Animation pacing")]
    [Min(.1f)] public float windupSpeed = 1.2f;
    [Min(.1f)] public float releaseSpeed = 1.25f;
    [Min(.1f)] public float heavyReleaseSpeed = .95f;
    [Min(.1f)] public float enemyHurtAnimationSpeed = 1.3f;

    [Header("Charged strike")]
    [Min(.1f)] public float heavyChargeTime = .7f;
    [Min(1)] public float heavyDamageMultiplier = 1.65f;
    [Min(1)] public float heavyKnockbackMultiplier = 1.35f;
    [Min(0), Tooltip("Stamina spent when releasing a fully charged heavy attack. Insufficient stamina releases a light attack instead.")] public float heavyStaminaCost = 8f;
    [Tooltip("Extra recovery after a heavy hit. Does not interrupt an already committed enemy swing.")]
    [Min(0)] public float heavyStaggerDuration = .45f;
    [Min(0), Tooltip("After a stagger, seconds the enemy can't be staggered again, so charged hits can't chain it into never attacking.")]
    public float enemyStaggerImmunity = 1.5f;
    [Min(0), Tooltip("Seconds an enemy stands still after a light hit. 0: the hurt reaction plays but never delays its next attack, so spamming light hits can't stunlock it.")]
    public float lightFlinchDuration = .15f;
    [Min(1)] public float heavyRecoilMultiplier = 1.6f;
    [Min(1)] public float heavyRecoilDurationMultiplier = 1.5f;

    [Header("Enemy rhythm")]
    [Tooltip("Seconds after the swing starts during which the enemy still turns to track the target (windupTrackSpeed). After this the swing direction is committed.")]
    [Min(.1f)] public float enemyAttackCommitTime = .4f;
    [Tooltip("Seconds the enemy stands after a swing before it moves again.")]
    [Min(0)] public float enemyRecoveryTime = .18f;
    [Tooltip("Multiplier on every enemy attack cooldown.")]
    [Min(.1f)] public float enemyCooldownMultiplier = 1f;
    [Tooltip("The hit can never land sooner than this after the swing starts, even if the clip's event is earlier.")]
    [Min(0)] public float enemyMinimumWindup = .3f;
    [Tooltip("Max angle between where the enemy committed to swing and the target for the hit to land.")]
    [Range(10,100)] public float enemyHitFacingAngle = 60f;
    [Tooltip("Playback speed of enemy attack animations (the AttackSpeed animator parameter).")]
    [Range(.5f,2.5f)] public float enemyAttackAnimationSpeed = 1.35f;
    [Min(0), Tooltip("How far the sound of a landed hit carries: enemies within this (plus their hearing) come to look, so fights draw in the next room. 0: fights are silent.")]
    public float fightNoiseRadius = 8f;
    [Range(0, 1), Tooltip("Share of a sound's reach that carries through a wall or closed door. Low keeps enemies in other rooms from hearing fights and footsteps through stone.")]
    public float wallMuffle = .3f;

    [Header("Swing feel")]
    public bool swingCameraMotionEnabled = true;
    [Tooltip("Local pitch, yaw and roll in degrees for each authored swing.")]
    public Vector3 lightSwingCameraRotation = new Vector3(.3f, .8f, -.35f);
    public Vector3 alternateSwingCameraRotation = new Vector3(.5f, .8f, -.55f);
    public Vector3 heavySwingCameraRotation = new Vector3(2f, .4f, -.65f);
    [Tooltip("Camera motion over normalized release animation time, returning to zero at the end.")]
    public AnimationCurve swingCameraCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.28f, 1f), new Keyframe(1f, 0f));
    [Tooltip("Heavy release: anticipation, strike, then a slower recovery.")]
    public AnimationCurve heavySwingCameraCurve = new AnimationCurve(new Keyframe(0f, 0f),
        new Keyframe(.18f, -.03f), new Keyframe(.4f, 1f), new Keyframe(.47f, 1f), new Keyframe(1f, 0f));
    [Tooltip("How quickly camera motion follows the animation and returns to neutral.")]
    [Min(1)] public float swingCameraBlendSpeed = 18f;
    [Tooltip("Speed multiplier over the light release by normalized clip time: fast in, slower follow-through.")]
    public AnimationCurve releaseSpeedCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(.45f, 1.15f), new Keyframe(1f, .7f));
    [Tooltip("Speed multiplier over the heavy release. The heavy swing clip already carries its coil / strike / hang / recovery timing, so keep this near 1.")]
    public AnimationCurve heavyReleaseSpeedCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.4f, 1.1f), new Keyframe(1f, .9f));
    [Tooltip("Tension-layer spike when a heavy hit lands, read as the arm shuddering from the impact.")]
    [Range(0,1)] public float heavyImpactShudder = .7f;

    [Header("Charge feel")]
    [Tooltip("Seconds an attack must be held before charge zoom and shake begin. Capped below the full-charge time.")]
    [Min(0f)] public float chargeCameraDelay = .2f;
    [Tooltip("Normalized heavy release time when charge zoom starts returning. Matches the end of anticipation.")]
    [Range(0f, .95f)] public float heavyZoomReturnStart = .18f;
    [Tooltip("Normalized heavy release time when charge zoom reaches normal. Scales with attack animation speed.")]
    [Range(.01f, 1f)] public float heavyZoomReturnEnd = .58f;
    [Tooltip("Zoom smoothing time for a cancelled charge or light release, in seconds.")]
    [Min(.01f)] public float chargeZoomReturnSmoothing = .12f;
    [Tooltip("Hold-loop speed at full charge: the arm settles into tension instead of breathing normally.")]
    [Range(.05f,1f)] public float holdSpeedAtFullCharge = .3f;
    [Tooltip("Weight of the ChargeAdditive layer at full charge (arm pulled further back, trembling).")]
    [Range(0,1)] public float chargeTension = 1f;
    [Tooltip("Field-of-view narrowing at full charge, in degrees.")]
    [Range(0,15)] public float chargeFovPull = 4f;
    [Tooltip("Extra camera noise amplitude at full charge.")]
    [Range(0,3)] public float chargeShake = .45f;
    [Tooltip("Camera kick the moment the charge completes.")]
    [Range(0,2)] public float chargeReadyKick = .3f;
    public AudioData chargeReadyAudio;

    [Header("Player impact feedback")]
    [Range(0,4)] public float landedCameraKick = .65f;
    [Range(0,6)] public float hurtCameraKick = 2f;
    [Range(0,4)] public float blockCameraKick = .8f;
    [Range(0,1)] public float hurtScreenAlpha = .22f;
    [Min(.05f)] public float hurtScreenDuration = .28f;
    public Color hurtScreenColor = new(.8f,.12f,.1f,1);
    public Color blockScreenColor = new(.55f,.7f,.8f,1);
    public AudioData blockAudio;
    public AudioData playerHurtAudio;
    public GameObject blockParticlePrefab;
    [Min(.1f)] public float impactParticleLifetime = 2f;
    [Min(0)] public float blockHitStopScale = .65f;

    readonly RaycastHit[] obstructionHits = new RaycastHit[64];
    public bool HasMeleeLineOfSight(Character source,Character victim,Vector3 contact) =>
        HasMeleeLineOfSight(source,victim!=null ? victim.transform : null,contact);

    // victim is the root of whatever is being hit (a Character, or the Component behind a prop's IDamageable);
    // its own colliders never count as obstructions.
    public bool HasMeleeLineOfSight(Character source,Transform victim,Vector3 contact)
    {
        if(source==null)return true;
        Vector3 origin=source.transform.position+Vector3.up*.9f;
        Vector3 delta=contact-origin;
        if(delta.sqrMagnitude<.001f)return true;
        int count=Physics.RaycastNonAlloc(origin,delta.normalized,obstructionHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        if(count==obstructionHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var t=obstructionHits[i].transform;
            if(t.IsChildOf(source.transform) || (victim!=null && t.IsChildOf(victim)))continue;
            return false;
        }
        return true;
    }

    // How close an enemy notices the player without looking at them, and how far it can see,
    // as shares of its profile's radii. Sneaking (crouching) and the Stealth skill shrink both.
    public void StealthScales(Transform target, out float close, out float sight)
    {
        close = 1; sight = 1;
        if (target == null) return;
        if (target != stealthTarget) { stealthTarget = target; stealthPlayer = target.GetComponent<Player>(); }
        if (stealthPlayer == null) return;
        bool sneaking = stealthPlayer.Motor != null && stealthPlayer.Motor.IsCrouching;
        float skill = sneaking && stealthPlayer.TryGetComponent<AdventurerProgress>(out var progress) && progress.InRun ? progress.Bonus(AdventureSkill.Stealth) : 0;
        close = (sneaking ? sneakCloseScale : unseenCloseScale) * Mathf.Clamp01(1 - skill);
        sight = sneaking ? sneakSightScale * Mathf.Clamp01(1 - skill * .5f) : 1;
    }
    Transform stealthTarget;
    Player stealthPlayer;

    public void RollCriticalOrBackstab(ref DamageInfo info, Character target)
    {
        if (target == null || info.Amount <= 0f) return;
        var brain = target.GetComponent<EnemyBrain>();
        if (backstabsEnabled && brain != null)
        {
            bool behind = info.Direction.sqrMagnitude > .001f && Vector3.Dot(target.transform.forward, info.Direction) > backstabBehindDot;
            if (!brain.IsAlerted || (backstabAlertFromBehind && behind))
            {
                info.Backstab = true;
                info.Amount *= backstabMultiplier;
                // Legendary Stealth doubles backstabs.
                if (info.Source != null && info.Source.TryGetComponent<AdventurerProgress>(out var progress) && progress.InRun && progress.Rank(AdventureSkill.Stealth) >= 100) info.Amount *= 2;
                return;
            }
        }
        if (criticalHitsEnabled && Random.value < critChance)
        {
            info.Critical = true;
            info.Amount *= critMultiplier;
        }
    }

    public void PresentImpact(Character victim, DamageInfo info)
    {
        var profile = info.Profile;
        bool defaults = profile == null || profile.useDefaultEffects;
        AudioData audio = info.Blocked ? blockAudio : victim is Player && playerHurtAudio != null ? playerHurtAudio :
            defaults ? defaultHitAudio : profile.hitAudio;
        var body = !info.Blocked && defaults && victim.data != null ? victim.data.audio : null;
        if (body != null && body.impact != null && body.impact.Length > 0) body.PlayAt(body.impact, info.HitPoint, body.impactVolume);
        else if(audio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(audio,info.HitPoint);
        var special = info.Blocked ? null : info.Backstab ? backstabAudio : info.Critical ? critAudio : null;
        if(special != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(special,info.HitPoint);
        GameObject prefab = info.Blocked ? blockParticlePrefab : defaults ? GetRandomHitParticle() : profile.hitParticle;
        if(prefab != null)
        {
            Vector3 direction = info.Direction.sqrMagnitude > .001f ? -info.Direction : Vector3.up;
            PoolManager.SpawnOrInstantiate(prefab,info.HitPoint,Quaternion.LookRotation(direction),impactParticleLifetime);
        }
        if(!(victim is Player) || hitStopOnPlayerHurt)
            RequestHitStop(info.Heavy, (profile != null ? profile.hitStopScale : 1f) * (info.Blocked ? blockHitStopScale
                : info.Backstab ? backstabHitStopScale : info.Critical ? critHitStopScale : 1f), victim);
    }

    public GameObject GetRandomHitParticle()
    {
        if (hitParticlePrefabs == null || hitParticlePrefabs.Count == 0) return null;
        return hitParticlePrefabs[Random.Range(0, hitParticlePrefabs.Count)];
    }

    public float ScaleKnockback(float baseForce) =>
        knockbackEnabled ? baseForce * knockbackForceMultiplier : 0f;

    // ── Hit stop ──────────────────────────────────────────────────────

    Coroutine hitStopRoutine;
    float hitStopEndsAt, hitStopScaleNow, shakeAmount;
    readonly List<(Transform model, Vector3 rest)> shaking = new();

    // Light and heavy attacks have their own length and depth; scale stretches the length (weapon
    // profile, crit, backstab, block). While a stop is active, a new request only extends or deepens
    // it — it never re-freezes or shortens it.
    public void RequestHitStop(bool heavy, float scale = 1f, Character victim = null)
    {
        if (!hitStopEnabled) return;
        float duration = (heavy ? heavyHitStopDuration : lightHitStopDuration) * Mathf.Max(0f, scale);
        if (duration <= 0f) return;
        float timeScale = heavy ? heavyHitStopTimeScale : lightHitStopTimeScale;

        float end = Time.unscaledTime + duration;
        AddShake(victim, heavy);
        if (hitStopRoutine != null)
        {
            hitStopEndsAt = Mathf.Max(hitStopEndsAt, end);
            hitStopScaleNow = Mathf.Min(hitStopScaleNow, timeScale);
            Time.timeScale = hitStopScaleNow;
            return;
        }
        hitStopEndsAt = end;
        hitStopScaleNow = timeScale;
        hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    // The pause menu owns Time.timeScale while it is open (it saves and restores the hit stop's value),
    // so the stop holds still and never writes the time scale until the game is unpaused.
    static bool Paused => GameManager.HasInstance && GameManager.Instance.State == GameState.Paused;

    IEnumerator HitStopRoutine()
    {
        while (true)
        {
            Time.timeScale = hitStopScaleNow;
            float start = Time.unscaledTime, length = Mathf.Max(.001f, hitStopEndsAt - start);
            while (Time.unscaledTime < hitStopEndsAt)
            {
                if (Paused) { hitStopEndsAt += Time.unscaledDeltaTime; start += Time.unscaledDeltaTime; yield return null; continue; }
                Shake(1f - (Time.unscaledTime - start) / length);
                yield return null;
            }
            EndShake();

            // Ease back to full speed; a new hit during the ease drops straight back into the stop.
            bool again = false;
            for (float t = 0f; t < hitStopRecovery; )
            {
                if (Paused) { yield return null; continue; }
                if (Time.unscaledTime < hitStopEndsAt) { again = true; break; }
                float k = t / hitStopRecovery;
                Time.timeScale = Mathf.Lerp(hitStopScaleNow, 1f, k * k);
                yield return null;
                if (!Paused) t += Time.unscaledDeltaTime;
            }
            if (!again) break;
        }
        // Restore to 1 explicitly: capturing the previous value would freeze the game
        // permanently if a request fired mid-stop and captured the slowed value.
        Time.timeScale = 1f;
        hitStopRoutine = null;
    }

    // The struck character's model (its "Model" child, never the root, so AI and physics are untouched)
    // jitters while the world is stopped.
    void AddShake(Character victim, bool heavy)
    {
        if (victim == null || victim is Player || hitStopShake <= 0f) return;
        var model = victim.transform.Find("Model");
        if (model == null) return;
        shakeAmount = Mathf.Max(shakeAmount, hitStopShake * (heavy ? 2f : 1f));
        foreach (var s in shaking) if (s.model == model) return;
        shaking.Add((model, model.localPosition));
    }

    void Shake(float strength)
    {
        int frame = Time.frameCount;
        foreach (var (model, rest) in shaking)
        {
            if (model == null || model.parent == null) continue;
            // Alternate sides every frame for a crisp shudder, with a little randomness, fading out.
            Vector3 dir = new Vector3(frame % 2 == 0 ? 1f : -1f, Random.Range(-.4f, .4f), Random.Range(-.4f, .4f));
            float local = shakeAmount * strength / Mathf.Max(.0001f, model.parent.lossyScale.x);
            model.localPosition = rest + model.parent.InverseTransformDirection(Camera.main != null
                ? Camera.main.transform.TransformDirection(dir) : dir) * local;
        }
    }

    void EndShake()
    {
        foreach (var (model, rest) in shaking) if (model != null) model.localPosition = rest;
        shaking.Clear();
        shakeAmount = 0f;
    }

    void OnDisable()
    {
        EndShake();
        if(hitStopRoutine != null) { StopCoroutine(hitStopRoutine); hitStopRoutine=null; Time.timeScale=1f; }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (hitStopRoutine != null) Time.timeScale = 1f;
    }
}
