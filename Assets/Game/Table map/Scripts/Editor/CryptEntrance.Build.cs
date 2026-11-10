using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The Crypt's place on the realm map: the run's main entrance in the map's near-left corner, a great stone gate set
// into a dark crag on a paved court (the user's pick of five trials, 2026-11-06). Behind the arch a flight of stairs
// runs down into the rock, lit by a torch on the passage wall, so the gate reads as a way in. Build() writes it into the
// realm map prefab under the Crypt's place (laid out in its own frame: +z toward the visitor, the court at 0, the group
// turned to face the player's end of the table), levels the court and cuts the stairwell into the ground; then run
// CampaignMapBuilder.PixelProps() for the pixel copies at the map's 6.4 texels a unit and Tools/crypt_court_paint.py
// for the court's paving.
public static partial class CryptEntrance
{
    // The place: the near-left corner (x toward the Dungeon Master, z to the player's left), far enough in that the
    // court's sloped rim stays clear of the map's cut edge. No round base: the props stand on the paved court.
    public static Vector2 Spot = new(-28.5f, 41.5f);
    public const float CourtRadius = 7f, CourtRim = 2.5f;
    public const float Radius = 5.8f;
    public const float Facing = 195f;   // the gate's front turned toward the player's end of the table
    const string GroupName = "Entrance (gate in the rock)";

    // The stairwell behind the arch (in the entrance's frame): two flights from the arch's back, each running down
    // StairDrop over StairRun, the second on from the first, into the dark under the crag (the torches only light the
    // top, so its far end reads as depth, not as a wall).
    const float StairScale = .5f, StairTop = -1.3f, StairRun = 5.09f * StairScale, StairDrop = 2.7f * StairScale, StairHalfWidth = 1.45f;
    const int Flights = 2;
    const float StairEnd = StairTop - Flights * StairRun - .5f;

    const string DF = "Assets/Synty/PolygonDarkFantasy/Prefabs/";
    const string FO = "Assets/Synty/PolygonDarkFortress/Prefabs/";
    const string DU = "Assets/Synty/PolygonDungeon/Prefabs/Environments/";

    const string Cliff = DF + "Environment/SM_Env_Rock_Cliff_01.prefab";
    const string Crag = DF + "Environment/SM_Env_Rock_Cliff_03.prefab";
    const string SkullPile = DF + "Props/SM_Prop_Skull_Pile_01.prefab";
    const string BonePile2 = DF + "Props/SM_Prop_Bone_Pile_02.prefab";
    const string Rock = DF + "Environment/SM_Env_Rock_01.prefab";
    const string Rock2 = DF + "Environment/SM_Env_Rock_02.prefab";
    const string DeadTree = DF + "Environment/SM_Env_Tree_Dead_01.prefab";
    const string WallTorch = DF + "Props/SM_Prop_Torch_01.prefab";
    const string Brazier = DF + "Props/SM_Prop_Brazier_01.prefab";
    // The flame every fire here burns with (Tools/crypt_flame.py): a tongue of fire at the map's 6.4 texels a unit.
    const string FlameModel = "Assets/Game/Table map/Map/Place art/Crypt flame/Crypt flame.fbx";
    const string FlameMaterial = "Assets/Game/Table map/Map/Place art/Crypt flame/Crypt flame.mat";

    struct Piece
    {
        // light: height of its fire (a light and a flame, children of the piece, in its own frame), 0 = none; lightOut: how
        // far out along its +z (a wall torch leans off the wall); flame: the flame's height in map units.
        public string path; public Vector3 pos; public float yaw, scale; public float light, lightOut, flame;
        public Piece(string path, float x, float y, float z, float yaw, float scale, float light = 0, float lightOut = 0, float flame = 0)
        { this.path = path; pos = new Vector3(x, y, z); this.yaw = yaw; this.scale = scale; this.light = light; this.lightOut = lightOut; this.flame = flame; }
    }

    static Piece P(string path, float x, float y, float z, float yaw, float s, float light = 0, float lightOut = 0, float flame = 0) => new(path, x, y, z, yaw, s, light, lightOut, flame);

    static List<Piece> Pieces()
    {
        var d = new List<Piece>
        {
            // The crag: cliffs either side of the gate and crags heaped on its top.
            P(Cliff, -3.9f, 0, -1.8f, 20, .44f), P(Cliff, 3.9f, 0, -1.9f, 200, .47f),
            // (a crag's bulk lies toward its +z: these two are turned away from the guardians, who stand clear of them)
            P(Crag, -4.9f, 0, .8f, -60, .3f), P(Crag, 5.35f, 0, .6f, 50, .33f),
            P(DU + "Misc/SM_Env_Entrance_Large_01.prefab", 0, 0, -.7f, 0, .62f),
            P(Crag, -1.9f, 4.9f, -.6f, 30, .25f), P(Crag, 0, 5.0f, -.7f, 110, .26f), P(Crag, 1.9f, 4.8f, -.6f, 200, .25f),
            // The way in: the flight runs down from the arch into the rock (the pack's flight rises toward its -z, so it
            // is turned round), the passage walled and roofed with rock and closed at its foot.
            P(DU + "Floors/SM_Env_Stairs_01.prefab", 2.48f * StairScale, -StairDrop, StairTop - StairRun, 180, StairScale),
            P(DU + "Floors/SM_Env_Stairs_01.prefab", 2.48f * StairScale, -2 * StairDrop, StairTop - 2 * StairRun, 180, StairScale),
            P(Cliff, -2.85f, -.9f, -3.0f, 0, .36f), P(Cliff, 2.85f, -.9f, -3.1f, 180, .36f),
            P(Cliff, -2.85f, -2.3f, -5.9f, 180, .36f), P(Cliff, 2.85f, -2.3f, -6.0f, 0, .36f),
            P(Cliff, 0, -3.0f, -8.4f, 90, .36f),
            // the pack's stair ceiling over the flight (it rises toward +z as the turned flight does), rock heaped on it
            P(DU + "Walls/SM_Env_Ceiling_Stone_Stairs_01.prefab", 2.5f * StairScale, 2.9f - 2.5f * StairScale, StairTop - 5f * StairScale, 0, StairScale),
            P(DU + "Walls/SM_Env_Ceiling_Stone_Stairs_01.prefab", 2.5f * StairScale, 2.9f - 2.5f * StairScale - StairDrop, StairTop - 10f * StairScale, 0, StairScale),
            // (the crag's pivot is near its top and its bulk lies toward its +z: turned 90, it sits on the ceiling)
            P(Crag, 0, 4.6f, -2.8f, 90, .3f), P(Crag, 0, 3.4f, -5.3f, 90, .3f),
            // The pack's ceiling is only modelled from below: from the table's side the flights showed through it, so the
            // rock is heaped over the whole passage, down to its foot.
            P(Crag, 0, 2.3f, -7.6f, 90, .3f), P(Crag, -.6f, 1.2f, -9f, 60, .26f), P(Crag, .3f, 1.3f, -9.1f, 130, .24f),
            P(Crag, 1.75f, 2.5f, -6.4f, 150, .28f), P(Crag, 1.2f, 1.6f, -8.3f, 20, .26f),
            P(Crag, -1.3f, 2.6f, -6.3f, 250, .28f), P(Crag, -1.1f, 3f, -3.9f, 170, .26f), P(Crag, -1.4f, 1.4f, -7.5f, 300, .26f),
            P(WallTorch, -1.1f, -.35f, -2.3f, 90, .8f, .72f, .29f, .5f), P(WallTorch, 1.1f, -.75f, -3.0f, -90, .8f, .72f, .29f, .5f),
            // Guardians, fire stands and banners at the gate; bones and boulders on the court.
            P(DU + "Misc/SM_Env_Statue_02.prefab", -2.85f, 0, 1.5f, 10, 1.15f), P(DU + "Misc/SM_Env_Statue_02.prefab", 2.85f, 0, 1.5f, -10, 1.15f),
            P(Brazier, -1.5f, 0, 2.0f, 0, .85f, .72f, 0, .65f), P(Brazier, 1.5f, 0, 2.0f, 0, .85f, .72f, 0, .65f),
            P(FO + "Props/SM_Prop_Banner_03.prefab", -1.75f, 2.2f, .25f, 0, .7f), P(FO + "Props/SM_Prop_Banner_03.prefab", 1.75f, 2.2f, .25f, 0, .7f),
            P(SkullPile, -2.1f, 0, 3.0f, 30, .85f), P(SkullPile, 2.3f, 0, 2.8f, 200, .7f), P(BonePile2, .4f, 0, 3.9f, 80, .8f),
            P(Rock, -3.3f, 0, 3.6f, 30, .55f), P(Rock2, 3.6f, 0, 3.3f, 120, .6f), P(Rock, 4.6f, 0, -3.0f, 250, .7f),
            P(DeadTree, -3.6f, 0, -3.8f, 75, .95f),
        };
        return d;
    }

    // Moves the Crypt to the corner, takes its old props and base away, levels the court, cuts the stairwell and
    // writes the entrance. Safe to run again (the ground starts from the heights kept before the first run).
    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        // The entrance itself is written into the place's own prefab.
        var place = PrefabUtility.LoadPrefabContents(CampaignMapBuilder.PlacePath("The Crypt"));
        try
        {
            var point = place.GetComponent<CampaignPoint>();
            // Old props off the place (its lights stay).
            var keep = new HashSet<string> { "poi light", "Place glow" };
            for (int i = point.transform.childCount - 1; i >= 0; i--)
            {
                var c = point.transform.GetChild(i);
                if (!keep.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
            }
            point.radius = Radius;
            point.showBase = false;
            var poi = point.transform.Find("poi light"); if (poi != null) { poi.localPosition = new Vector3(0, 13f, 0); poi.GetComponent<Light>().range = 24f; }
            var glow = point.transform.Find("Place glow"); if (glow != null) { glow.localPosition = new Vector3(0, 3f, 2f); var l = glow.GetComponent<Light>(); l.range = 10f; l.intensity = 6f; }
            var group = new GameObject(GroupName).transform;
            group.SetParent(point.transform, false);
            group.localRotation = Quaternion.Euler(0, Facing, 0);
            group.localPosition = new Vector3(0, -.25f, 0);
            int n = 0;
            foreach (var piece in Pieces()) if (Place(group, piece)) n++;
            log.Append($"{n} pieces; ");
            PrefabUtility.SaveAsPrefabAsset(place, CampaignMapBuilder.PlacePath("The Crypt"));
        }
        finally { PrefabUtility.UnloadPrefabContents(place); }
        // The ground and where the place stands are the map's.
        var root = PrefabUtility.LoadPrefabContents(MapPath);
        try
        {
            var map = root.GetComponent<CampaignMap>();
            var point = root.transform.Find("Points/The Crypt");
            float floor = Flatten(map);
            // CampaignPoint.Snap stands a place .25 over the highest ground in its radius (room for a base): the court
            // is flat, so the entrance is set .25 down to stand on it.
            point.localPosition = new Vector3(Spot.x, floor + .25f, Spot.y);
            // Scatter on the court goes (only switched off: the map's own hidden specks stay hidden).
            int hidden = 0;
            foreach (Transform s in root.transform.Find("Scatter"))
            {
                var d = s.localPosition - point.localPosition; d.y = 0;
                if (d.magnitude < CourtRadius + CourtRim + .5f && s.gameObject.activeSelf) { s.gameObject.SetActive(false); hidden++; }
            }
            log.Append($"scatter hidden {hidden}; at {point.localPosition}");
            PrefabUtility.SaveAsPrefabAsset(root, MapPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return log.ToString();
    }

    // The court: the ground under the entrance lowered to the lowest point in it, so it lies level and a little sunk,
    // with a sloped rim out to the land round it; its edge wanders (never a clean circle). The stairwell is cut below
    // it, just under the flight's steps. Only ever lowers, so the cloths, which lie over the old ground, never meet it.
    // The heights it started from are kept in Library, so it can be run again. Returns the court's height.
    const string HeightsBackup = "Library/crypt_court_heights.bin";
    static float Flatten(CampaignMap map)
    {
        var h = map.heights;
        if (System.IO.File.Exists(HeightsBackup))
        {
            var bytes = System.IO.File.ReadAllBytes(HeightsBackup);
            if (bytes.Length == h.Length * 4) System.Buffer.BlockCopy(bytes, 0, h, 0, bytes.Length);
        }
        else
        {
            var bytes = new byte[h.Length * 4]; System.Buffer.BlockCopy(h, 0, bytes, 0, bytes.Length);
            System.IO.File.WriteAllBytes(HeightsBackup, bytes);
        }
        int w = map.gridX + 1;
        float cell = map.halfX * 2 / map.gridX;
        float floor = float.MaxValue;
        for (int iz = 0; iz <= map.gridZ; iz++)
        for (int ix = 0; ix <= map.gridX; ix++)
        {
            float x = -map.halfX + ix * cell, z = -map.halfZ + iz * cell;
            if (Vector2.Distance(new Vector2(x, z), Spot) <= CourtRadius + .8f) floor = Mathf.Min(floor, h[iz * w + ix]);
        }
        floor -= .08f;   // a little sunk, so its edge reads
        var toEntrance = Quaternion.Inverse(Quaternion.Euler(0, Facing, 0));
        for (int iz = 0; iz <= map.gridZ; iz++)
        for (int ix = 0; ix <= map.gridX; ix++)
        {
            float x = -map.halfX + ix * cell, z = -map.halfZ + iz * cell;
            var d = new Vector2(x, z) - Spot;
            float dist = d.magnitude;
            if (dist > CourtRadius + CourtRim + 1f) continue;
            int i = iz * w + ix;
            float a = Mathf.Atan2(d.y, d.x);
            float r = CourtRadius + .6f * Mathf.Sin(a * 3 + 1.3f) + .35f * Mathf.Sin(a * 7 + .4f);
            float t = Mathf.Clamp01((dist - r) / CourtRim); t = t * t * (3 - 2 * t);
            h[i] = Mathf.Min(h[i], Mathf.Lerp(floor, h[i], t));
            // The stairwell: the ground just under the steps, down to the landing at their foot.
            var e = toEntrance * new Vector3(d.x, 0, d.y);
            if (Mathf.Abs(e.x) <= StairHalfWidth && e.z <= StairTop + .25f && e.z >= StairEnd)
            {
                float down = Mathf.Clamp01((StairTop - e.z) / (Flights * StairRun)) * Flights * StairDrop;
                h[i] = Mathf.Min(h[i], floor - down - .12f);
            }
        }
        // The terrain mesh's vertices are the height grid's points, unless its triangles were reduced
        // (CampaignMapBuilder.ReduceTriangles): then it is laid out as the full grid again, to be reduced again after.
        var mesh = map.terrain.sharedMesh;
        var v = mesh.vertices;
        if (v.Length == h.Length) { for (int i = 0; i < v.Length; i++) v[i].y = h[i]; mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); }
        else { CampaignMapBuilder.RebuildTerrainMesh(map, h); Debug.Log("[Entrance] the ground is its full height grid again: run CampaignMapBuilder.ReduceTriangles() when the map is done"); }
        AssetDatabase.SaveAssets();
        return floor;
    }

    static bool Place(Transform group, Piece piece)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(piece.path);
        if (prefab == null) { Debug.LogWarning("[Entrance] missing " + piece.path); return false; }
        var go = (GameObject)Object.Instantiate(prefab, group);
        go.name = prefab.name;
        go.transform.localPosition = piece.pos;
        go.transform.localRotation = Quaternion.Euler(0, piece.yaw, 0);
        go.transform.localScale = Vector3.one * piece.scale;
        // Only the first LOD, no colliders: a map prop is only drawn.
        foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = lod.GetLODs();
            for (int k = 1; k < lods.Length; k++) foreach (var r in lods[k].renderers) if (r != null) Object.DestroyImmediate(r.gameObject);
            Object.DestroyImmediate(lod);
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        if (piece.light > 0)
        {
            // The fire's own light, a child of the fire (it moves with it).
            var lg = new GameObject("Fire light");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0, piece.light, piece.lightOut);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, .56f, .26f); l.intensity = 3f; l.range = 3.2f; l.shadows = LightShadows.None;
            if (piece.flame > 0) Flame(go.transform, new Vector3(0, piece.light, piece.lightOut), piece.flame / piece.scale);
        }
        return true;
    }

    // A flame on its fire, standing up; height in the parent's frame.
    static void Flame(Transform parent, Vector3 local, float height)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FlameModel);
        var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        if (filter == null) { Debug.LogWarning("[Entrance] no flame in " + FlameModel); return; }
        var source = filter.transform;
        var go = new GameObject("Flame");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.rotation = source.rotation;   // standing up in the world, whichever way its fire is turned
        go.transform.localScale = Vector3.one * (height / filter.sharedMesh.bounds.size.z);
        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterial);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }
}
