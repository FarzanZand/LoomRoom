using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Central toggles and tuning for combat feel. Hit stop lives here too, so there is
// exactly one entry point and a hit can never freeze the game twice.
public class CombatManager : Singleton<CombatManager>
{
    [Header("Hit Stop")]
    public bool hitStopEnabled = true;
    [Tooltip("Seconds the game slows when a hit lands.")]
    public float hitStopDuration = 0.05f;
    [Tooltip("Time scale during hit stop. A hair above zero keeps animations creeping, which reads better than a hard freeze.")]
    [Range(0f, 0.5f)] public float hitStopTimeScale = 0.05f;
    [Tooltip("Also hit-stop when an enemy lands a hit on the player.")]
    public bool hitStopOnPlayerHurt = false;

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
    [Range(0f, 1f)] public float blockDamageReduction = 1f;
    [Tooltip("Dot product threshold: a hit is frontal (blockable) when dot(facing, hitDir) is below this.")]
    [Range(-1f, 1f)] public float blockFrontalDot = -0.3f;
    [Tooltip("Stamina spent per blocked hit. 0 = free.")]
    public float blockStaminaCost = 0f;

    [Header("Attack responsiveness")]
    [Range(.5f,2f)] public float playerAttackSpeed = 1.12f;
    [Min(0)] public float attackInputBuffer = .22f;
    [Range(1,16)] public int weaponSweepSteps = 6;

    [Header("Animation pacing")]
    [Min(.1f)] public float windupSpeed = 1.2f;
    [Min(.1f)] public float releaseSpeed = 1.25f;
    [Min(.1f)] public float heavyReleaseSpeed = .95f;
    [Min(.1f)] public float enemyHurtAnimationSpeed = 1.3f;
    [Min(0)] public float timedBlockWindow = .18f;

    [Header("Charged strike")]
    [Min(.1f)] public float heavyChargeTime = .7f;
    [Min(1)] public float heavyDamageMultiplier = 1.65f;
    [Min(1)] public float heavyKnockbackMultiplier = 1.35f;
    [Min(0)] public float heavyStaminaCost = 1f;

    [Header("Enemy rhythm")]
    [Min(.1f)] public float enemyAttackCommitTime = .65f;
    [Min(0)] public float enemyRecoveryTime = .55f;
    [Min(.1f)] public float enemyCooldownMultiplier = 1.15f;
    [Min(0)] public float enemyMinimumWindup = .35f;
    [Range(10,100)] public float enemyHitFacingAngle = 55f;

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
    public bool HasMeleeLineOfSight(Character source,Character victim,Vector3 contact)
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
            if(t.IsChildOf(source.transform) || (victim!=null && t.IsChildOf(victim.transform)))continue;
            return false;
        }
        return true;
    }

    public void PresentImpact(Character victim, DamageInfo info)
    {
        var profile = info.Profile;
        bool defaults = profile == null || profile.useDefaultEffects;
        AudioData audio = info.Blocked ? blockAudio : victim is Player && playerHurtAudio != null ? playerHurtAudio :
            defaults ? defaultHitAudio : profile.hitAudio;
        if(audio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(audio,info.HitPoint);
        GameObject prefab = info.Blocked ? blockParticlePrefab : defaults ? GetRandomHitParticle() : profile.hitParticle;
        if(prefab != null)
        {
            Vector3 direction = info.Direction.sqrMagnitude > .001f ? -info.Direction : Vector3.up;
            var effect = Instantiate(prefab,info.HitPoint,Quaternion.LookRotation(direction));
            Destroy(effect,impactParticleLifetime);
        }
        if(!(victim is Player) || hitStopOnPlayerHurt)
            RequestHitStop((profile != null ? profile.hitStopScale : 1f) * (info.Blocked ? blockHitStopScale : 1f));
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

    public bool HitStopActive => hitStopRoutine != null;

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
