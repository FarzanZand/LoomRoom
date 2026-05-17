using UnityEngine;

public class CharacterFX : MonoBehaviour
{
    [Header("On Hit Landed")]
    [SerializeField] AudioClip[] onHitSounds;
    [SerializeField, Range(0f, 1f)] float onHitVolume = 1f;
    [SerializeField, Range(0f, 0.5f)] float onHitPitchVariance = 0.05f;
    [SerializeField] bool enableHitStop = true;
    [SerializeField] float hitStopDuration = 0.07f;
    [SerializeField] bool enableKnockback = true;
    [SerializeField] float knockbackForce = 3f;

    [Header("On Hurt Received")]
    [SerializeField] AudioClip[] onHurtSounds;
    [SerializeField, Range(0f, 1f)] float onHurtVolume = 1f;
    [SerializeField, Range(0f, 0.5f)] float onHurtPitchVariance = 0.05f;

    public bool  KnockbackEnabled => enableKnockback;
    public float KnockbackForce   => knockbackForce;

    public virtual void NotifyHitLanded(Vector3 contactPoint)
    {
        if (enableHitStop)
            HitStopManager.Instance?.Trigger(hitStopDuration);

        AudioManager.Instance?.PlaySFXRandom(onHitSounds, contactPoint, onHitVolume, onHitPitchVariance);
    }

    public virtual void NotifyHurtReceived(float amount, Vector3 direction)
    {
        AudioManager.Instance?.PlaySFXRandom(onHurtSounds, transform.position, onHurtVolume, onHurtPitchVariance);
    }
}
