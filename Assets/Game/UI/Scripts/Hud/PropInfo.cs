using System;
using System.Collections.Generic;
using UnityEngine;

// A placed room prop that says what it is when the crosshair rests on it (the user, 2026-11-14: the dice on the table,
// each hung trophy): the item tooltip's look, its name, a kind line and label and number rows. Added at run time by the
// prop's own script (TableDice, TrophyWall) with Describe set; it answers for the renderers under it (or those given),
// so it needs no collider. PropInfoLook (made by the first one) does the looking.
public class PropInfo : MonoBehaviour
{
    // Title, kind line (empty for none) and body: read each time the tooltip opens, so it says how things stand now.
    public Func<(string title, string kind, string body)> Describe;
    Renderer[] parts;

    static readonly List<PropInfo> all = new();
    public static IReadOnlyList<PropInfo> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    // pieces: the objects it answers for (null: everything under it).
    public void Bind(Func<(string, string, string)> describe, GameObject[] pieces = null)
    {
        Describe = describe;
        if (pieces == null) { parts = null; return; }
        var list = new List<Renderer>();
        foreach (var p in pieces) if (p != null) list.AddRange(p.GetComponentsInChildren<Renderer>(true));
        parts = list.ToArray();
    }

    // Where it is now: the bounds of its shown renderers, none while nothing of it is shown.
    public bool Shown(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in parts ?? (parts = GetComponentsInChildren<Renderer>(true)))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // A renderer of its own: the line of sight to it may pass through it.
    public bool Owns(Transform t)
    {
        if (t == null) return false;
        if (parts == null) return t.IsChildOf(transform);
        foreach (var r in parts) if (r != null && t.IsChildOf(r.transform)) return true;
        return false;
    }

    void OnEnable()
    {
        all.Add(this);
        PropInfoLook.Ensure();
    }
    void OnDisable() => all.Remove(this);

    // A label and number row in the item tooltip's style (a spell's rows).
    public static string Row(string label, string value, string colour = null) =>
        $"<color={ItemData.DimColor}>{label}</color><pos=48%><color={colour ?? ItemData.GoodColor}>{value}</color>";
}
