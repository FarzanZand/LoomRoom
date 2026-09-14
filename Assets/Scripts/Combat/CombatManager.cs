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

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (hitStopRoutine != null) Time.timeScale = 1f;
    }
}
