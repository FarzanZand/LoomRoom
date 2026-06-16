using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct WeaponAudioEntry
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume;
    [Range(0f, 1f)] public float pitchVariance;
}

// Central toggles and tuning for combat feel effects.
public class CombatManager : Singleton<CombatManager>
{
    [Header("Hit Stop")]
    public bool hitStopEnabled = true;
    [Tooltip("Seconds the game freezes when a hit lands.")]
    public float hitStopDuration = 0.05f;

    [Header("Knockback")]
    public bool knockbackEnabled = true;
    [Tooltip("Scales every knockback force in the game.")]
    public float knockbackForceMultiplier = 1f;

    [Header("Default Hit Effects")]
    [Tooltip("Pool of hit particle prefabs. One is chosen at random when playDefaultEffects is true on the weapon.")]
    public List<GameObject> hitParticlePrefabs = new List<GameObject>();
    [Tooltip("Pool of hit audio clips. One is chosen at random when playDefaultEffects is true on the weapon.")]
    public List<WeaponAudioEntry> hitAudioClips = new List<WeaponAudioEntry>();
    [Tooltip("Pool of swing audio clips. One is chosen at random when playDefaultEffects is true on the weapon.")]
    public List<WeaponAudioEntry> swingAudioClips = new List<WeaponAudioEntry>();

    public GameObject GetRandomHitParticle()
    {
        if (hitParticlePrefabs == null || hitParticlePrefabs.Count == 0) return null;
        return hitParticlePrefabs[Random.Range(0, hitParticlePrefabs.Count)];
    }

    public bool TryGetRandomHitAudio(out WeaponAudioEntry entry)
    {
        if (hitAudioClips != null && hitAudioClips.Count > 0)
        {
            entry = hitAudioClips[Random.Range(0, hitAudioClips.Count)];
            return true;
        }
        entry = default;
        return false;
    }

    public bool TryGetRandomSwingAudio(out WeaponAudioEntry entry)
    {
        if (swingAudioClips != null && swingAudioClips.Count > 0)
        {
            entry = swingAudioClips[Random.Range(0, swingAudioClips.Count)];
            return true;
        }
        entry = default;
        return false;
    }

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

    [Header("Block Cancel")]
    [Tooltip("Seconds after attacking that block is locked out. Set this to just under your attack windup clip length.")]
    public float blockCancelWindow = 0.5f;

    [Header("Block Damage Reduction")]
    [Tooltip("Fraction of incoming damage blocked when guarding frontally. 1 = full block, 0 = no mitigation.")]
    [Range(0f, 1f)]
    public float blockDamageReduction = 1f;

    public void RequestHitStop()
    {
        if (hitStopEnabled && HitStopManager.Instance != null)
            HitStopManager.Instance.Trigger(hitStopDuration);
    }

    public float ScaleKnockback(float baseForce) =>
        knockbackEnabled ? baseForce * knockbackForceMultiplier : 0f;
}
