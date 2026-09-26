using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Seconds the game slows when a hit lands.")]
    public float hitStopDuration = 0.05f;
    [Tooltip("Time scale during hit stop. A hair above zero keeps animations creeping, which reads better than a hard freeze.")]
    [Range(0f, 0.5f)] public float hitStopTimeScale = 0.05f;
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
    public Color hitFlashColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Seconds the flash tint stays on the character's renderers.")]
    public float hitFlashDuration = 0.1f;

    [Header("Block")]
    [Tooltip("Seconds after attacking that block is locked out. Set to just under your attack windup length.")]
    public float blockCancelWindow = 0.5f;
    [Tooltip("Fraction of incoming damage removed when guarding frontally. 1 = full block.")]
    [Range(0f, 1f)] public float blockDamageReduction = .5f;
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
    [HideInInspector] public float timedBlockWindow; // Legacy serialized field; timed parries are disabled.

    [Header("Charged strike")]
    [Min(.1f)] public float heavyChargeTime = .7f;
    [Min(1)] public float heavyDamageMultiplier = 1.65f;
    [Min(1)] public float heavyKnockbackMultiplier = 1.35f;
    [Min(0), Tooltip("Stamina spent when releasing a fully charged heavy attack. Insufficient stamina releases a light attack instead.")] public float heavyStaminaCost = 8f;
    [Tooltip("Extra recovery after a heavy hit. Does not interrupt an already committed enemy swing.")]
    [Min(0)] public float heavyStaggerDuration = .45f;
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
    [Tooltip("Hit stop scale for a heavy hit that lands.")]
    [Min(1)] public float heavyHitStopScale = 2f;
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
        if (!info.Blocked && defaults && victim.data != null && victim.data.bodyImpactAudio != null)
            audio = victim.data.bodyImpactAudio;
        if(audio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(audio,info.HitPoint);
        var special = info.Blocked ? null : info.Backstab ? backstabAudio : info.Critical ? critAudio : null;
        if(special != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(special,info.HitPoint);
        GameObject prefab = info.Blocked ? blockParticlePrefab : defaults ? GetRandomHitParticle() : profile.hitParticle;
        if(prefab != null)
        {
            Vector3 direction = info.Direction.sqrMagnitude > .001f ? -info.Direction : Vector3.up;
            PoolManager.SpawnOrInstantiate(prefab,info.HitPoint,Quaternion.LookRotation(direction),impactParticleLifetime);
        }
        if(!(victim is Player) || hitStopOnPlayerHurt)
            RequestHitStop((profile != null ? profile.hitStopScale : 1f) * (info.Blocked ? blockHitStopScale
                : info.Backstab ? backstabHitStopScale : info.Critical ? critHitStopScale : 1f));
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
    float hitStopEndsAt;

    // Scale lets a heavy weapon ask for a longer stop. While a stop is active, a new
    // request only extends it — it never re-freezes or shortens it.
    public void RequestHitStop(float scale = 1f)
    {
        if (!hitStopEnabled) return;
        float duration = hitStopDuration * Mathf.Max(0f, scale);
        if (duration <= 0f) return;

        float end = Time.unscaledTime + duration;
        if (hitStopRoutine != null)
        {
            hitStopEndsAt = Mathf.Max(hitStopEndsAt, end);
            return;
        }
        hitStopEndsAt = end;
        hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    IEnumerator HitStopRoutine()
    {
        Time.timeScale = hitStopTimeScale;
        while (Time.unscaledTime < hitStopEndsAt)
            yield return null;
        // Restore to 1 explicitly: capturing the previous value would freeze the game
        // permanently if a request fired mid-stop and captured the slowed value.
        Time.timeScale = 1f;
        hitStopRoutine = null;
    }

    void OnDisable()
    {
        if(hitStopRoutine != null) { StopCoroutine(hitStopRoutine); hitStopRoutine=null; Time.timeScale=1f; }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (hitStopRoutine != null) Time.timeScale = 1f;
    }
}
