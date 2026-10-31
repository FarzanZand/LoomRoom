using System.Collections.Generic;
using UnityEngine;

// The bones a burst skeleton left on the floor (DeathBurst), kept so Raise the Fallen can pull them back together.
// Each piece remembers the bone it came from. A heap goes once it is raised, or when its pieces are gone (the floor
// unloaded).
public class BoneHeap
{
    public struct Piece { public Transform transform; public string bone; }

    public readonly List<Piece> pieces = new();
    public string body;
    // The kind of skeleton the bones came from (its EnemyData knows its prefab), unless it was a boss.
    public EnemyData kind;
    public Transform parent;

    static readonly List<BoneHeap> all = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    public static void Add(BoneHeap heap) { if (heap != null && heap.pieces.Count > 0) all.Add(heap); }

    // Where the bones lie now (the middle of what is left), or null when none are left.
    public Vector3? Centre
    {
        get
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var p in pieces) if (p.transform != null) { sum += p.transform.position; n++; }
            return n > 0 ? sum / n : null;
        }
    }

    // The nearest heap within reach of a point, its bones still lying there.
    public static BoneHeap Nearest(Vector3 from, float reach)
    {
        BoneHeap best = null;
        float bestDistance = reach * reach;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var centre = all[i].Centre;
            if (centre == null) { all.RemoveAt(i); continue; }
            float d = (centre.Value - from).sqrMagnitude;
            if (d > bestDistance) continue;
            best = all[i]; bestDistance = d;
        }
        return best;
    }

    // Taken for a raise: no one else can raise it.
    public void Claim() => all.Remove(this);
}
