using UnityEngine;

// Testing from an eval: WatchTeammate.Run(seconds, metres) keeps the table player looking at the first teammate on the
// table and within reach of them (stepping in along the NavMesh when they get further than metres plus 1.5), so a
// recording shows how their body moves wherever they go.
public class WatchTeammate : MonoBehaviour
{
    float until, distance;

    public static void Run(float seconds, float metres)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null) return;
        var watch = player.GetComponent<WatchTeammate>();
        if (watch == null) watch = player.gameObject.AddComponent<WatchTeammate>();
        watch.until = Time.time + seconds;
        watch.distance = metres;
    }

    // A spot about distance away with a clear line to the teammate, on their floor; the nearest such spot to here.
    void Place(Player me, CoopPlayer other, Vector3 target)
    {
        int mask = ~((1 << me.gameObject.layer) | (1 << other.gameObject.layer));
        Vector3 best = Vector3.zero; float bestD = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            var dir = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
            var at = target + dir * distance;
            if (!UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, .8f, UnityEngine.AI.NavMesh.AllAreas) || Mathf.Abs(hit.position.y - target.y) > .5f) continue;
            if (Blocked(hit.position + Vector3.up * 1.5f, target + Vector3.up * 1.2f, mask)) continue;
            float d = Vector3.Distance(hit.position, me.transform.position);
            if (d < bestD) { bestD = d; best = hit.position; }
        }
        if (bestD < float.MaxValue) me.Warp(best);
    }

    // Walls between two points, stopping short of the teammate (their body is not under their player object).
    static bool Blocked(Vector3 from, Vector3 to, int mask)
    {
        var d = to - from; float len = d.magnitude;
        return len > .8f && Physics.Raycast(from, d / len, len - .8f, mask, QueryTriggerInteraction.Ignore);
    }

    void LateUpdate()
    {
        if (Time.time > until) { Destroy(this); return; }
        var me = GetComponent<Player>();
        CoopPlayer other = null;
        if (MultiplayerManager.HasInstance)
            foreach (var p in MultiplayerManager.Instance.Players) if (p != null && !p.IsOwner && p.Current.place == CoopPlace.Table) other = p;
        if (me == null || other == null || me.Look == null) return;
        var target = other.transform.position;
        var eye = target + Vector3.up * 1.2f;
        var flat = me.transform.position - target; flat.y = 0;
        bool seen = !Blocked(me.Look.PitchTransform.position, eye, ~((1 << me.gameObject.layer) | (1 << other.gameObject.layer)));
        if (!seen || flat.magnitude > distance + 1.5f || flat.magnitude < 1.2f) Place(me, other, target);
        CameraEase.YawPitchTo(me.Look, target + Vector3.up * .9f, out float yaw, out float pitch);
        me.Look.SetYaw(yaw); me.Look.SetPitch(pitch);
    }
}
