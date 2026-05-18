using Unity.Cinemachine;
using UnityEngine;

public class PlayerFX : CharacterFX
{
    [Header("Received Knockback")]
    [SerializeField] float receivedKnockbackForce = 3f;
    public float ReceivedKnockbackForce => receivedKnockbackForce;

    [Header("Camera")]
    [SerializeField] CinemachineImpulseSource onHitImpulse;
    [SerializeField] CinemachineImpulseSource onHurtImpulse;

    public override void NotifyHitLanded(Vector3 contactPoint)
    {
        base.NotifyHitLanded(contactPoint);
        onHitImpulse?.GenerateImpulse();
    }

    public override void NotifyHurtReceived(float amount, Vector3 direction)
    {
        base.NotifyHurtReceived(amount, direction);
        onHurtImpulse?.GenerateImpulse();
    }
}
