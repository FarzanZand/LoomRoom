using UnityEngine;

// A piece of knocked-down wall decor (KnockDownDecor): knocks as it lands, then lies still for good.
public class KnockDownPiece : MonoBehaviour
{
    KnockDownDecor owner;
    Rigidbody body;
    float since;
    int knocks;

    public void Init(KnockDownDecor from) { owner = from; body = GetComponent<Rigidbody>(); }

    void OnCollisionEnter(Collision c) { if (owner != null && knocks++ < 3 && c.contactCount > 0) owner.Landed(c.GetContact(0).point, c.relativeVelocity.magnitude); }

    void FixedUpdate()
    {
        if (body == null || body.isKinematic) return;
        since += Time.fixedDeltaTime;
        if (since > 1f && body.linearVelocity.sqrMagnitude < .01f && body.angularVelocity.sqrMagnitude < .05f) body.isKinematic = true;
    }
}
