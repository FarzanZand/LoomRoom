using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What DeathBurst and EnemyRagdoll share once a body turns into physics pieces: stop
// everything that poses the skeleton, keep the pieces out of the players' way, throw
// them along the killing blow and freeze them once they have settled.
public static class DeathPhysics
{
    // Anything still posing the skeleton would fight the physics.
    public static void StopPosing(Component root, Animator anim)
    {
        if (anim != null) anim.enabled = false;
        foreach (var hr in root.GetComponentsInChildren<HitReactionController>()) hr.enabled = false;
    }

    // The killing blow's direction (not normalised), or backwards when the hit had none.
    public static Vector3 HitDirection(DamageInfo lastHit, Transform body) =>
        lastHit.Direction.sqrMagnitude > .001f ? lastHit.Direction : -body.forward;

    public static void IgnorePlayers(List<Rigidbody> bodies)
    {
        if (!PlayerManager.HasInstance) return;
        var pieces = new List<Collider>();
        foreach (var rb in bodies)
            if (rb != null)
                foreach (var c in rb.GetComponents<Collider>())
                    if (c.enabled) pieces.Add(c);
        foreach (var context in PlayerManager.Instance.players)
        {
            if (context == null || context.player == null) continue;
            foreach (var pc in context.player.GetComponentsInChildren<Collider>(true))
                foreach (var c in pieces)
                    Physics.IgnoreCollision(pc, c, true);
        }
    }

    // Freezes the pieces where they lie (saves physics time).
    public static IEnumerator Settle(List<Rigidbody> bodies, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        foreach (var rb in bodies)
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }
}
