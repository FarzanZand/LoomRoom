using UnityEngine;

// A clear line between two points: a ray (or a thin sphere) that only the given roots'
// own colliders may cross. Triggers never block, and a full hit buffer counts as blocked.
// Used by Perception (sight), EnemyAttackRunner (arrows) and EnemyVoice.
public static class LineOfSight
{
    static readonly RaycastHit[] hits = new RaycastHit[32];

    public static bool Clear(Vector3 from, Vector3 to, int mask, Transform ignore, Transform ignoreToo = null, float radius = 0f)
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
            return false;
        }
        return true;
    }
}
