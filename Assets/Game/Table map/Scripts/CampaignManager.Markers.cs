using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Marks on the realm map (F9 19f3cac7, 2026-10-31; a yellow question mark since 2026-11-11, the user, a banner before):
// a yellow question mark floats over every open place not yet visited (Resources/Campaign new place mark, made by
// CampaignMarkSetup.Build()), and while one of them has not been looked at on the map yet the table's Map prompt carries
// the same mark (NewsMark, read by TableManager; flag campaign.noticed.<id>, set as the map view begins). The place to
// start with (home, until it has been played) also gets a warm gold glow round it
// (QuestGlow), so "Start with the Crypt" points at something you can see. A place is visited once a run has started
// there (flag campaign.visited.<id>, set as the run starts), or it was beaten or died in on an older save. Refreshed with the
// saved-run flag (RefreshMarker); Reset campaign clears it.
public partial class CampaignManager
{
    public const string VisitedPrefix = "campaign.visited.", NoticedPrefix = "campaign.noticed.";

    [TabGroup("Map"), Tooltip("Put after the table's Map prompt while a new place on the map has not been looked at there yet (rich text: a yellow question mark).")]
    public string newMark = "<color=#FFD23F>?</color>";

    [TabGroup("Map"), Tooltip("The glow round the place to start with (home until it has been played).")]
    public Color startGlowColour = new(1f, .78f, .38f, 1f);
    [TabGroup("Map"), Min(0f), Tooltip("Its width, in world units (the room is about 22 units to the metre).")]
    public float startGlowWidth = .1f;

    readonly Dictionary<CampaignPoint, GameObject> banners = new();
    QuestGlow startGlow;

    public bool Visited(CampaignEncounter e) => e != null && PM != null
        && (PM.HasFlag(VisitedPrefix + e.id) || Beaten(e) || Deaths(e) > 0);

    // New on the map: a place that can be entered and where no run has started yet.
    bool New(CampaignEncounter e) => e != null && e.kind == CampaignEncounterKind.Level && IsOpen(e) && !Visited(e);

    // Something new lies on the map that the player has not opened the map on since it came. Asked by the table's
    // prompt every frame it shows, so the answer is kept for half a second.
    float newsCheckedAt = -10f;
    bool news;
    public bool HasNews
    {
        get
        {
            if (Time.unscaledTime - newsCheckedAt < .5f) return news;
            newsCheckedAt = Time.unscaledTime;
            news = false;
            if (PM == null || !PM.HasFlag(SeenFlag)) return false;
            foreach (var e in AllEncounters)
                if (New(e) && !PM.HasFlag(NoticedPrefix + e.id)) { news = true; break; }
            return news;
        }
    }
    // " ?" in yellow for the table's prompt, or nothing.
    public string NewsMark => HasNews && !string.IsNullOrEmpty(newMark) ? " " + newMark : "";

    // The map view begins: what is new on it has now been looked at (its mark over the place stays until it is played).
    void NoticeNewPlaces()
    {
        if (PM == null) return;
        foreach (var e in AllEncounters)
            if (New(e) && !PM.HasFlag(NoticedPrefix + e.id)) PM.SetFlag(NoticedPrefix + e.id, 1);
        newsCheckedAt = -10f;
    }

    void MarkVisited(CampaignEncounter e) { if (e != null && PM != null && !openEverything) PM.SetFlag(VisitedPrefix + e.id, 1); }

    void RefreshNewPlaces()
    {
        if (Map == null) return;
        var prefab = Resources.Load<GameObject>("Campaign new place mark");
        if (prefab == null) prefab = Resources.Load<GameObject>("Campaign new place banner");
        var wanted = new HashSet<CampaignPoint>();
        foreach (var e in AllEncounters)
        {
            if (!New(e)) continue;
            var point = Map.PointFor(e);
            if (point != null && point.PropsShown) wanted.Add(point);
        }
        // Marks of places now visited go; new ones come over their place's tallest prop.
        var gone = new List<CampaignPoint>();
        foreach (var kv in banners) if (!wanted.Contains(kv.Key) || kv.Value == null) gone.Add(kv.Key);
        foreach (var p in gone) { if (banners[p] != null) Destroy(banners[p]); banners.Remove(p); }
        if (prefab != null)
            foreach (var point in wanted)
            {
                if (banners.ContainsKey(point)) continue;
                var banner = Instantiate(prefab, point.transform);
                banner.name = "New place mark";
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
