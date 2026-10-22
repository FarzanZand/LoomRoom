using Sirenix.OdinInspector;
using UnityEngine;

// The light radius on dungeon floors: ambient light is full around the player and fades to almost nothing past
// a radius, so the dark reads as dark and lamps, torches and braziers (direct light) still show their surroundings far
// off. Drawn by the Pixel Lit shaders (_LoomLightRadius); room surfaces are never darkened. A biome can widen or
// narrow it (DungeonBiome.lightRadius).
public sealed partial class LightingManager
{
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [Title("Light radius")]
    [Tooltip("Ambient light fades out with distance from the player on dungeon floors (while the dungeon darkness applies).")]
    public bool lightRadius = true;
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [ShowIf(nameof(lightRadius)), Min(.5f), Tooltip("Metres around the player with full ambient light (a biome's Light Radius replaces it).")]
    public float lightRadiusMetres = 6.5f;
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [ShowIf(nameof(lightRadius)), Min(.1f), Tooltip("Metres over which the ambient fades past the radius.")]
    public float lightRadiusEdge = 2.5f;
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [ShowIf(nameof(lightRadius)), Range(0f, 1f), Tooltip("Share of the ambient left beyond the edge (0 = black where no lamp reaches).")]
    public float lightRadiusFloor = .3f;
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [ShowIf(nameof(lightRadius)), Tooltip("The table player's own lights (the fill over the head and the key light) also fade out by the radius plus its edge, so the dark starts where the radius says (a circle of light). Off, they reach as far as they are set to (about 12 m) and the radius only takes the faint ambient out of the far dark.")]
    public bool radiusDimsPlayerLights = true;

    [ShowIf(nameof(lightRadius)), Min(0f), Tooltip("Seconds the radius takes to close in once the table player has the view (from the room it is wide open, so the table reveal shows the floor lit).")]
    public float radiusBlendSeconds = 1.2f;
    const float RoomViewRadius = 400f;
    float radiusBlend;

    // How far the table player's carried lights may reach across the floor now (metres; 0 = as authored).
    public static float CarriedLightReach { get; private set; }

    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [Title("Lamp shadows")]
    [Min(0), Tooltip("Only this many of the floor's lamps (torches, braziers, candles) nearest the player cast shadows; the rest light without them. A shadowed point light draws the scene six times.")]
    public int shadowedLamps = 4;
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [Min(.05f)] public float shadowBudgetSeconds = .3f;
    readonly System.Collections.Generic.Dictionary<Light, LightShadows> lampShadows = new();
    readonly System.Collections.Generic.List<Light> lamps = new(), found = new(), stale = new(), pocketFound = new();
    readonly System.Collections.Generic.List<DungeonGenerator> pockets = new();
    static Vector3 sortEye;
    static readonly System.Comparison<Light> ByDistance = (a, b) => (a.transform.position - sortEye).sqrMagnitude.CompareTo((b.transform.position - sortEye).sqrMagnitude);
    DungeonGenerator lampsOf;
    float nextShadowBudget;

    [TabGroup("Tabs", "Dungeon"), PropertyOrder(10)]
    [Tooltip("While the table player is on a dungeon floor, the room's own point and spot lights (under Room Roots) cast no shadows; the room is still lit and seen. They get their shadows back in the room.")]
    public bool roomShadowsOffInDungeon = true;
    readonly System.Collections.Generic.Dictionary<Light, LightShadows> roomShadows = new();
    readonly System.Collections.Generic.List<Light> roomLights = new();
    bool roomShadowsOff;

    // The floor's own lamps (and its portals' pockets', built beside it) keep their authored shadows only while they
    // are among the nearest few.
    void ApplyShadowBudget(Vector3 eye)
    {
        var loader = TableManager.Loader;
        var dungeon = loader != null ? loader.Dungeon : null;
        // A new floor is budgeted at once, before its first frame draws every lamp's shadows.
        if (dungeon == lampsOf && Time.unscaledTime < nextShadowBudget) return;
        nextShadowBudget = Time.unscaledTime + shadowBudgetSeconds;
        // Not while a floor loads or the way back flies into the room: the room is in view then.
        bool onFloor = dungeon != null && !loader.Busy && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        ApplyRoomShadows(onFloor && roomShadowsOffInDungeon);
        if (dungeon != lampsOf)
        {
            foreach (var pair in lampShadows) if (pair.Key != null) pair.Key.shadows = pair.Value;
            lampShadows.Clear(); lampsOf = dungeon;
        }
        if (dungeon == null) return;
        lamps.Clear();
        found.Clear();
        // Lamps in rooms not dealt in yet count too, so they light up already within the budget.
        dungeon.GetComponentsInChildren(true, found);
        PortalArch.PocketsOf(dungeon, pockets);
        foreach (var pocket in pockets)
        {
            if (pocket == null) continue;
            pocketFound.Clear();
            pocket.GetComponentsInChildren(true, pocketFound);
            found.AddRange(pocketFound);
        }
        pocketFound.Clear();
        foreach (var l in found)
        {
            if (!lampShadows.ContainsKey(l)) lampShadows[l] = l.shadows;
            if (lampShadows[l] != LightShadows.None) lamps.Add(l);
        }
        // Lights destroyed since (a broken candle barrel, a spent spell) drop out.
        if (lampShadows.Count > found.Count + 32) { stale.Clear(); foreach (var key in lampShadows.Keys) if (key == null) stale.Add(key); foreach (var key in stale) lampShadows.Remove(key); }
        sortEye = eye;
        lamps.Sort(ByDistance);
        for (int i = 0; i < lamps.Count; i++) lamps[i].shadows = i < shadowedLamps ? lampShadows[lamps[i]] : LightShadows.None;
    }

    // The room's point and spot lights without shadows (their own put back when off is false). Gathered once each
    // time the table player goes down to a floor.
    void ApplyRoomShadows(bool off)
    {
        if (off == roomShadowsOff) return;
        roomShadowsOff = off;
        if (!off)
        {
            foreach (var pair in roomShadows) if (pair.Key != null) pair.Key.shadows = pair.Value;
            roomShadows.Clear();
            return;
        }
        if (roomRoots == null) return;
        foreach (var root in roomRoots)
        {
            if (root == null) continue;
            roomLights.Clear();
            root.GetComponentsInChildren(true, roomLights);
            foreach (var l in roomLights)
            {
                if (l == null || (l.type != LightType.Point && l.type != LightType.Spot) || l.shadows == LightShadows.None) continue;
                if (roomShadows.ContainsKey(l) || IsNotRoom(l.transform)) continue;
                roomShadows[l] = l.shadows;
                l.shadows = LightShadows.None;
            }
        }
        roomLights.Clear();
    }

    static readonly int LightRadiusId = Shader.PropertyToID("_LoomLightRadius");
    static readonly int LightRadiusEdgeId = Shader.PropertyToID("_LoomLightRadiusEdge");

    void ApplyLightRadius()
    {
        float dark = moodActive && dungeonDarkness ? Mathf.Clamp01(displayedMood.darkness) : 0f;
        var biome = CurrentBiome();
        if (biome != null && biome.litThroughout) dark = 0f;
        var cam = Camera.main;
        if (cam != null && Application.isPlaying) ApplyShadowBudget(cam.transform.position);
        if (!lightRadius || dark <= 0f || cam == null || !Application.isPlaying)
        {
            Shader.SetGlobalVector(LightRadiusId, Vector4.zero);
            CarriedLightReach = 0f;
            return;
        }
        float radius = biome != null && biome.lightRadius > 0f ? biome.lightRadius : lightRadiusMetres;
        // Gear that lights the way (LightRadius on the table player).
        var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (table != null && table.Stats != null) radius += Mathf.Max(0f, table.Stats.GetFinal(StatType.LightRadius));
        // Seen from the room (the table reveal, the flight in), the radius around a far camera would leave the floor's
        // outer walls black and then light them up on arrival: it closes in only once the table player has the view.
        radiusBlend = Mathf.MoveTowards(radiusBlend, table != null && table.IsActive ? 1f : 0f, Time.deltaTime / Mathf.Max(.05f, radiusBlendSeconds));
        radius = Mathf.Lerp(RoomViewRadius, radius, radiusBlend * radiusBlend);
        var centre = cam.transform.position;
        // The room the player is in may pull the light in (a dark room) or open it out (DungeonRoomMood).
        float edge = lightRadiusEdge, floor = lightRadiusFloor;
        DungeonRoomMood.Tick(centre, Time.deltaTime);
        DungeonRoomMood.LightRadius(ref radius, ref edge, ref floor);
        // Never past the level's minimum ambient, even beyond the radius or in a dark room.
        if (displayedMood.minAmbient > 0f && ambientPeak > .0001f) floor = Mathf.Max(floor, Mathf.Clamp01(displayedMood.minAmbient / ambientPeak));
        Shader.SetGlobalVector(LightRadiusId, new Vector4(centre.x, centre.y, centre.z, radius));
        // Partway into the darkness (a mood blending in) the floor rises towards full ambient.
        Shader.SetGlobalVector(LightRadiusEdgeId, new Vector4(edge, Mathf.Lerp(1f, floor, dark), 0f, 0f));
        // Partway into the darkness the carried lights reach out further, back to their own range.
        CarriedLightReach = radiusDimsPlayerLights ? (radius + edge) / Mathf.Max(.05f, dark) : 0f;
    }

    // The biome of the dungeon floor being played, if any.
    public static DungeonBiome CurrentBiome()
    {
        var loader = TableManager.Loader;
        var dungeon = loader != null ? loader.Dungeon : null;
        return dungeon != null ? dungeon.Biome : null;
    }
}
