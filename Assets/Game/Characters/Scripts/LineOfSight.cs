using UnityEngine;

// A clear line between two points: a ray (or a thin sphere) that only the given roots'
// own colliders and dead bodies may cross. Triggers never block, and a full hit buffer counts as blocked.
// Used by Perception (sight), EnemyAttackRunner (arrows) and EnemyVoice.
public static class LineOfSight
{
    static readonly RaycastHit[] hits = new RaycastHit[32];

    // charactersBlock off: only the world blocks (a living character in the way is not cover from the eye).
    public static bool Clear(Vector3 from, Vector3 to, int mask, Transform ignore, Transform ignoreToo = null, float radius = 0f, bool charactersBlock = true)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < .0001f) return true;
        Vector3 direction = delta / distance;
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(from, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var t = hits[i].transform;
            if ((ignore != null && t.IsChildOf(ignore)) || (ignoreToo != null && t.IsChildOf(ignoreToo))) continue;
            // Corpses never block: a dead body lying between two enemies doesn't hide the player.
            if (IsCorpse(hits[i].collider)) continue;
            if (!charactersBlock && hits[i].collider.GetComponentInParent<Character>() != null) continue;
            return false;
        }
        return true;
    }

    // A piece of a dead body, or a collider still on one (one that never started reads 0 health but stands).
    static bool IsCorpse(Collider c)
    {
        if (DeathPhysics.IsCorpse(c)) return true;
        var owner = c.GetComponentInParent<Character>();
        return owner != null && owner.Stats != null && owner.Stats.Initialised && !owner.Stats.IsAlive;
    }
}
