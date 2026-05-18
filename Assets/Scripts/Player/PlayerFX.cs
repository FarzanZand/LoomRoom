using Unity.Cinemachine;
using UnityEngine;

public class PlayerFX : CharacterFX
{
    [Header("Received Knockback")]
    [SerializeField] float receivedKnockbackForce = 3f;
    public float ReceivedKnockbackForce => receivedKnockbackForce;

    [Header("Hit Stop")]
    [SerializeField] bool  enableHitStop   = true;
    [SerializeField] float hitStopDuration = 0.07f;

    [Header("Camera")]
    [SerializeField] CinemachineImpulseSource onHitImpulse;
    [SerializeField] CinemachineImpulseSource onHurtImpulse;

    public override void NotifyHitLanded(Vector3 contactPoint)
    {
        base.NotifyHitLanded(contactPoint);
        if (enableHitStop)
            HitStopManager.Instance?.Trigger(hitStopDuration);
        onHitImpulse?.GenerateImpulse();
    }

    public override void NotifyHurtReceived(float amount, Vector3 direction)
    {
        base.NotifyHurtReceived(amount, direction);
        onHurtImpulse?.GenerateImpulse();
    }
}
