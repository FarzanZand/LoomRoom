using System.Collections.Generic;
using UnityEngine;

// Marks a place while a quest points at it (the character sheet's table, the class desk, the locked box's table, a
// carried trophy's plaque, the dice cup, the place to start with on the map): a cone of warm light falls on it from
// above with dust turning in it, and the place itself is lit (QuestLight, Resources/Quest light). Added and switched by
// the place's own script; nothing to set up. It was a bright blue outline drawn round the object until 2026-11-16 (the
// user: "the highlight effect is real ugly and sticks out too much in the room, do something more fitting. Especially
// ugly when the table or desk is highlighted").
public class QuestGlow : MonoBehaviour
{
    [Tooltip("The light's colour.")]
    public Color colour = new(1f, .84f, .56f, 1f);
    // Flat things hung on a wall (a trophy plaque): the light falls on them at a slant from the room's side, since a
    // light straight down a wall would only graze them.
    [Tooltip("Set by the owner's script for a flat piece on a wall: its forward points out of the wall.")]
    public Transform flatOn;
    [Min(0), Tooltip("How high over the place its light comes from, in metres. 0: from the ceiling over it. Set by the owner's script for a small place under a high ceiling (a place on the map).")]
    public float reach;

    // The room is built about 20 units to the metre; a floor on the table 1.
    const float RoomUnit = 20f;

    QuestLight beam;
    static bool warned;   // the prefab is missing: said once, not tried again
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { warned = false; }
    bool shown;
    Bounds fitted;
    float nextFit, unit = RoomUnit;

    public void Show(bool on)
    {
        shown = on;
        // Built when first wanted with something to light. Switched on while its object wasn't drawn yet (the table's
        // build hides every piece until it lands) it finds nothing, and tries again until it does (Update).
        if (on && beam == null && !warned) Build();
        if (beam != null) beam.Show(on && isActiveAndEnabled);
    }

    void Update()
    {
        if (!shown) return;
        if (beam == null) { if (!warned) Build(); if (beam != null) beam.Show(true); return; }
        // A place that moves or changes size (the map rising out of the table) keeps its light on it.
        if (Time.unscaledTime < nextFit) return;
        nextFit = Time.unscaledTime + .5f;
        if (Place(out var now, out uint layers) && ((now.center - fitted.center).sqrMagnitude + (now.size - fitted.size).sqrMagnitude) > 1e-4f * unit * unit) Fit(now, layers);
    }

    void OnEnable() { if (shown && beam != null) beam.Show(true); }
    void OnDisable() { if (beam != null) beam.Hide(); }
    void OnDestroy() { if (beam != null) Destroy(beam.gameObject); }

    void Build()
    {
        if (!Place(out var place, out uint layers)) return;
        var prefab = Resources.Load<QuestLight>("Quest light");
        if (prefab == null) { if (!warned) Debug.LogWarning("[QuestGlow] Resources/Quest light prefab is missing (Tools/quest_light.eval.cs makes it)."); warned = true; return; }
        // Its own root object, not this one's child: the place may be scaled (room props are) or squashed (the map).
        beam = Instantiate(prefab);
        beam.name = "Quest light (" + name + ")";
        beam.gameObject.SetActive(false);
        Fit(place, layers);
    }

    // What the light falls on: the pieces that make the object's shape, its renderers at least a quarter the size of the
    // biggest (a table, not the brushes and figures on it).
    bool Place(out Bounds place, out uint layers)
    {
        place = default; layers = LightingManager.RoomLayer;
        var renderers = new List<MeshRenderer>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(false))
            if (r.enabled && r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null) renderers.Add(r);
        float biggest = 0f;
        foreach (var r in renderers) biggest = Mathf.Max(biggest, Volume(r.bounds));
        bool any = false;
        foreach (var r in renderers)
        {
            if (Volume(r.bounds) < biggest * .25f) continue;
            if (!any) { place = r.bounds; any = true; } else place.Encapsulate(r.bounds);
        }
        if (!any) return false;
        // A floor on the table is lit by its own lamps and measured in metres; everything else is the room.
        bool onTable = GetComponentInParent<DungeonGenerator>() != null;
        unit = onTable ? 1f : RoomUnit;
        if (onTable) layers = renderers[0].renderingLayerMask;
        return true;
    }

    void Fit(Bounds place, uint layers)
    {
        fitted = place;
        Vector3 foot, source; float radius, litFrom;
        float room = RoomOver(place);
        if (flatOn != null)
        {
            // On a wall: from up and out in the room, down onto its face.
            var e = place.extents;
            radius = Mathf.Max(e.x, e.y, e.z) * 1.35f;
            foot = place.center;
            // As far out as up: steeper, the cone crossed the plaque above it on its way down.
            float rise = Mathf.Min(room + e.y, 1.3f * unit);
            source = foot + Vector3.up * rise + flatOn.forward * rise;
            litFrom = (source - foot).magnitude;
        }
        else
        {
            radius = Mathf.Clamp(Mathf.Max(place.extents.x, place.extents.z) * 1.12f, .16f * unit, 1.5f * unit);
            foot = new Vector3(place.center.x, place.min.y, place.center.z);
            source = new Vector3(foot.x, place.max.y + room, foot.z);
            litFrom = room;   // its top is what the lamp is set for: set for the floor, a table's top was bleached
        }
        beam.Fit(foot, source, radius, litFrom, unit, colour, layers);
    }

    // How far the place has clear over it, up to its ceiling: where the light comes from.
    float RoomOver(Bounds place)
    {
        var from = new Vector3(place.center.x, place.max.y + .02f * unit, place.center.z);
        float most = 3.2f * unit, least = .9f * unit;
        if (reach > 0f) return reach * unit;
        if (flatOn != null) from += flatOn.forward * (.3f * unit);
        foreach (var hit in Physics.RaycastAll(from, Vector3.up, most, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform)) most = Mathf.Min(most, hit.distance - .03f * unit);
        return Mathf.Max(least, most);
    }

    static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;
}
