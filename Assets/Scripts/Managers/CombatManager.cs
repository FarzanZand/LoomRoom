using UnityEngine;

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

    [Header("Hit Flash")]
    public bool hitFlashEnabled = true;
    public Color hitFlashColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Seconds the flash tint stays on the character's renderers.")]
    public float hitFlashDuration = 0.1f;

    public void RequestHitStop()
    {
        if (hitStopEnabled && HitStopManager.Instance != null)
            HitStopManager.Instance.Trigger(hitStopDuration);
    }

    public float ScaleKnockback(float baseForce) =>
        knockbackEnabled ? baseForce * knockbackForceMultiplier : 0f;
}
