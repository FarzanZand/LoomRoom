using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Marks on the realm map (F9 19f3cac7, the user's pick C, 2026-10-31): a small banner floats over every open place not
// yet visited, and the place to start with (home, until it has been played) also gets a warm gold glow round it
// (QuestGlow), so "Start with the Crypt" points at something you can see. A place is visited once a run has started
// there (flag campaign.visited.<id>, set as the run starts), or it was beaten or died in on an older save. Refreshed with the
// saved-run flag (RefreshMarker); Reset campaign clears it.
public partial class CampaignManager
{
    public const string VisitedPrefix = "campaign.visited.";

    [TabGroup("Map"), Tooltip("The glow round the place to start with (home until it has been played).")]
    public Color startGlowColour = new(1f, .78f, .38f, 1f);
    [TabGroup("Map"), Min(0f), Tooltip("Its width, in world units (the room is about 22 units to the metre).")]
    public float startGlowWidth = .1f;

    readonly Dictionary<CampaignPoint, GameObject> banners = new();
    QuestGlow startGlow;

    public bool Visited(CampaignEncounter e) => e != null && PM != null
        && (PM.HasFlag(VisitedPrefix + e.id) || Beaten(e) || Deaths(e) > 0);

    void MarkVisited(CampaignEncounter e) { if (e != null && PM != null && !openEverything) PM.SetFlag(VisitedPrefix + e.id, 1); }

    void RefreshNewPlaces()
    {
        if (Map == null) return;
        var prefab = Resources.Load<GameObject>("Campaign new place banner");
        var wanted = new HashSet<CampaignPoint>();
        foreach (var e in AllEncounters)
        {
            if (e == null || e.kind != CampaignEncounterKind.Level || !IsOpen(e) || Visited(e)) continue;
            var point = Map.PointFor(e);
            if (point != null && point.PropsShown) wanted.Add(point);
        }
        // Banners of places now visited go; new ones come over their place's tallest prop.
        var gone = new List<CampaignPoint>();
        foreach (var kv in banners) if (!wanted.Contains(kv.Key) || kv.Value == null) gone.Add(kv.Key);
        foreach (var p in gone) { if (banners[p] != null) Destroy(banners[p]); banners.Remove(p); }
        if (prefab != null)
            foreach (var point in wanted)
            {
                if (banners.ContainsKey(point)) continue;
                var banner = Instantiate(prefab, point.transform);
                banner.name = "New place banner";
                LightingManager.LightAsRoom(banner);
                float top = point.radius * 1.4f;
                foreach (var r in point.GetComponentsInChildren<Renderer>())
                    if (!r.transform.IsChildOf(banner.transform) && (marker == null || !r.transform.IsChildOf(marker.transform)))
                        top = Mathf.Max(top, point.transform.InverseTransformPoint(r.bounds.max).y + .3f);
                var local = new Vector3(0f, top, 0f);
                if (banner.TryGetComponent<CampaignBanner>(out var b)) b.SetRest(local); else banner.transform.localPosition = local;
                banners[point] = banner;
            }

        var start = home != null && IsOpen(home) && !Visited(home) ? Map.PointFor(home) : null;
        if (startGlow != null && (start == null || startGlow.gameObject != start.gameObject)) { startGlow.Show(false); startGlow = null; }
        if (start != null && start.PropsShown)
        {
            if (!start.TryGetComponent(out startGlow)) startGlow = start.gameObject.AddComponent<QuestGlow>();
            startGlow.colour = startGlowColour; startGlow.width = startGlowWidth;
            startGlow.Show(true);
        }
    }

    void ClearNewPlaces()
    {
        foreach (var kv in banners) if (kv.Value != null) Destroy(kv.Value);
        banners.Clear();
        if (startGlow != null) { startGlow.Show(false); startGlow = null; }
    }
}
