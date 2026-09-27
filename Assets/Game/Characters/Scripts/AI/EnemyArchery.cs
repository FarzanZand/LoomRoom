using System;
using Sirenix.OdinInspector;
using UnityEngine;

// A bow on top of (or instead of) an enemy's melee attacks. Switched on with EnemyData.archer.
// The brain treats the shot as one more attack: it keeps its distance, draws while aiming,
// and the release clip's OnAttackHit event looses an ArrowProjectile at the target.
[Serializable]
public class EnemyArchery
{
    [Title("Bow")]
    [Tooltip("Held in the left hand (grip: Characters/Resources/Synty humanoid bow grip). Art only; colliders are switched off.")]
    [AssetsOnly] public GameObject bow;
    [Tooltip("Flies at the target. Also shown in the right hand while the bow is drawn.")]
    [AssetsOnly] public ArrowProjectile arrow;
    [Tooltip("Animator trigger for the draw. The draw and release states carry the 'Attack' tag.")]
    public string animatorTrigger = "BowShot";

    [Title("Range and timing")]
    [Tooltip("Too close and it backs off (or uses its melee attacks instead).")]
    public float minRange = 3f;
    public float maxRange = 14f;
    [Tooltip("Seconds between shots, counted from the end of the release.")]
    public float cooldown = 2.2f;
    [Tooltip("Degrees per second the archer keeps turning toward the target while drawing.")]
    public float aimTrackSpeed = 300f;
    [Tooltip("Fires anyway this many seconds after the draw starts if the clip has no OnAttackHit event.")]
    public float fallbackReleaseTime = 1.3f;
    [Tooltip("Largest height difference to the target the archer will shoot across.")]
    public float maxHeightDifference = 4f;

    [Title("Arrow")]
    public float arrowSpeed = 22f;
    [Tooltip("Random aim error in degrees. 0 never misses a standing target.")]
    [Range(0f, 15f)] public float spread = 2.5f;
    [Tooltip("Aims ahead of a moving target by this fraction of its travel during the flight.")]
    [Range(0f, 1f)] public float leadTarget = 0.5f;
    [HideLabel, InlineProperty, FoldoutGroup("On hit")]
    public HitProfile hit = new HitProfile { useDefaultEffects = true, damageMultiplier = 1f, knockbackForce = 1.5f, hitStopScale = 0.5f };

    [Title("Audio")]
    public AudioData pullAudio;
    public AudioData fireAudio;

    // The brain's attack list entry for the shot, built on first use.
    [NonSerialized] EnemyAttack shot;
    public EnemyAttack Shot
    {
        get
        {
            shot ??= new EnemyAttack { name = "Bow shot", bowShot = this, facingAngle = 12f, fallbackHitDelay = -1f };
            shot.animatorTrigger = animatorTrigger;
            shot.minRange = minRange;
            shot.maxRange = maxRange;
            shot.cooldown = cooldown;
            shot.hit = hit;
            return shot;
        }
    }
}
