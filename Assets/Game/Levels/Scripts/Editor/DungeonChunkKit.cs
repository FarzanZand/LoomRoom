using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Hand-built rooms ("chunks", Heathen's way of building a floor) and the kit they are made of. Kit pieces are
// DungeonKitBlocks (built of the room's own stone when the floor is generated); the pillar, broken pillar, arch and
// gallery railing also carry a painted model (Rooms/Kit/Models, Synty Dark Fantasy pieces through the painted-prop
// pipeline) that the level uses instead of those blocks while TableLevelData.kitModels is on (DungeonKitPiece).
// Writes the kit to Levels/Dungeon1/Rooms/Kit, the chunks to Rooms/Templates/Chunks, and adds the chunks to Cellars' and
// Crypt's room pools (DungeonBiome.roomTemplates). Only what doesn't exist yet is made: a kit piece or room you have
// edited is never overwritten, and pool entries you changed or added stay (Build Kit and Chunks (Overwrite) rebuilds
// everything of mine from scratch).
//
// To make a room by hand: Tools > LoomRoom > Dungeon > New Room Template, paint the footprint in its inspector, drag
// kit pieces and sockets in, and add it to a biome's Room Templates.
public static class DungeonChunkKit
{
    const string Rooms = "Assets/Game/Levels/Dungeon1/Rooms";
    const string KitFolder = Rooms + "/Kit";
    const string ModelFolder = KitFolder + "/Models";
    const string ChunkFolder = Rooms + "/Templates/Chunks";
    const string Synty = "Assets/Synty/PolygonDarkFantasy/Prefabs";
    const float C = 2f;   // Dungeon1's cell size
    static readonly Dictionary<string, GameObject> kit = new();
    static bool overwrite;

    [MenuItem("Tools/LoomRoom/Dungeon/Build Kit and Chunks")]
    public static string Run() => Run(false);

    [MenuItem("Tools/LoomRoom/Dungeon/Build Kit and Chunks (Overwrite)")]
    public static string RunOverwrite() => Run(true);

    public static string Run(bool replaceExisting)
    {
        overwrite = replaceExisting;
        if (!AssetDatabase.IsValidFolder(KitFolder)) AssetDatabase.CreateFolder(Rooms, "Kit");
        if (!AssetDatabase.IsValidFolder(ModelFolder)) AssetDatabase.CreateFolder(KitFolder, "Models");
        if (!AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.CreateFolder(Rooms + "/Templates", "Chunks");
        BuildKit();
        var chunks = BuildChunks();
        var log = new System.Text.StringBuilder($"kit {kit.Count} pieces, chunks: ");
        foreach (var c in chunks) log.Append(c.name).Append(", ");
        Pool("Assets/Game/Levels/Dungeon1/Biomes/Cellars.asset", chunks, new Dictionary<string, float> { { "Pillared hall", 1f }, { "Ruined chamber", 1f }, { "Gate hall", .8f }, { "Crypt niches", .6f }, { "Gallery hall", .6f }, { "Raised altar", .5f } });
        Pool("Assets/Game/Levels/Dungeon1/Biomes/Crypt.asset", chunks, new Dictionary<string, float> { { "Crypt niches", 1.2f }, { "Raised altar", .9f }, { "Pillared hall", 1f }, { "Gate hall", .9f }, { "Gallery hall", .8f }, { "Ruined chamber", .6f } });
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // Shows every kit block's shape in the editor (prefab thumbnails, Prefab Mode): for prefabs made before previews existed.
    [MenuItem("Tools/LoomRoom/Dungeon/Refresh Kit Previews")]
    public static string RefreshPreviews()
    {
        int n = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Rooms }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<DungeonKitBlock>(true) == null) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { foreach (var b in root.GetComponentsInChildren<DungeonKitBlock>(true)) b.UpdatePreview(); PrefabUtility.SaveAsPrefabAsset(root, path); n++; }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return $"previews refreshed in {n} prefabs";
    }

    // ── Kit ─────────────────────────────────────────────────────────

    static DungeonKitBlock Block(Transform parent, string name, Vector3 pos, Vector3 size, DungeonKitBlock.Surface sides = DungeonKitBlock.Surface.Wall,
        DungeonKitBlock.Surface top = DungeonKitBlock.Surface.Floor, Vector3? euler = null)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler ?? Vector3.zero;
        var b = go.AddComponent<DungeonKitBlock>(); b.size = size; b.sides = sides; b.top = top;
        return b;
    }

    static Transform Group(Transform parent, string name) { var g = new GameObject(name).transform; g.SetParent(parent, false); return g; }

    static GameObject Kit(string name, bool removeIfBlocking, Action<Transform> build)
    {
        string path = $"{KitFolder}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !overwrite) { kit[name] = existing; return existing; }
        var root = new GameObject(name);
        root.AddComponent<DungeonTemplatePiece>().removeIfBlocking = removeIfBlocking;
        build(root.transform);
        foreach (var b in root.GetComponentsInChildren<DungeonKitBlock>(true)) b.UpdatePreview();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        kit[name] = prefab;
        return prefab;
    }

    // A painted model for a piece: Synty's model copied as plain objects (meshes, its materials, mesh colliders) into
    // Rooms/Kit/Models, where the painted-prop pipeline paints it. Never overwritten: its painting stays.
    static GameObject Model(string name, string syntyPrefab)
    {
        string path = $"{ModelFolder}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Synty}/{syntyPrefab}.prefab");
        var copy = (GameObject)UnityEngine.Object.Instantiate(source);
        copy.name = name;
        foreach (var mf in copy.GetComponentsInChildren<MeshFilter>(true))
            if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
        UnityEngine.Object.DestroyImmediate(copy);
        return prefab;
    }

    // The model under a piece, and the block groups it replaces.
    static DungeonKitPiece UseModel(Transform piece, GameObject model, Vector3 pos, Vector3 euler, Vector3 scale, bool toCeiling, params Transform[] replaces)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, piece);
        go.name = "Model";
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
        var p = piece.gameObject.AddComponent<DungeonKitPiece>();
        p.model = go; p.modelToCeiling = toCeiling; p.modelHeight = 3f * scale.y;
        p.replaces = Array.ConvertAll(replaces, t => t.gameObject);
        return p;
    }

    const DungeonKitBlock.Surface Wall = DungeonKitBlock.Surface.Wall, Trim = DungeonKitBlock.Surface.Trim, Floor = DungeonKitBlock.Surface.Floor, Upper = DungeonKitBlock.Surface.UpperWall;

    // Wall pieces are built with their back on local z = 0 (the wall) and their front towards -z (into the room).
    static void BuildKit()
    {
        kit.Clear();
        var pillar = Model("Kit pillar", "Building/SM_Bld_Pillar_02");
        var arch = Model("Kit arch", "Building/SM_Bld_Wall_Archway_01");
        var railing = Model("Kit railing", "Building/SM_Bld_Railing_01");

        Kit("Pillar", true, t =>
        {
            var g = Group(t, "Blocks");
            Block(g, "Base", Vector3.zero, new Vector3(1f, .3f, 1f), Trim, Trim);
            var shaft = Block(g, "Shaft", new Vector3(0f, .3f, 0f), new Vector3(.64f, 2.2f, .64f), Wall, Trim); shaft.toCeiling = true; shaft.ceilingGap = .45f;
            var cap = Block(g, "Capital", Vector3.zero, new Vector3(.95f, .45f, .95f), Trim, Trim); cap.fromCeiling = true; cap.solid = false;
            UseModel(t, pillar, Vector3.zero, Vector3.zero, new Vector3(.85f, 1f, .85f), true, g);
        });
        Kit("Broken pillar", true, t =>
        {
            var g = Group(t, "Blocks");
            Block(g, "Base", Vector3.zero, new Vector3(1f, .3f, 1f), Trim, Trim);
            Block(g, "Stump", new Vector3(0f, .3f, 0f), new Vector3(.64f, 1.25f, .64f), Wall, Wall, new Vector3(0f, 12f, 0f));
            Block(g, "Fallen drum", new Vector3(.7f, 0f, -.55f), new Vector3(.64f, .5f, .9f), Wall, Wall, new Vector3(0f, 34f, 6f));
            // The pillar sunk into the floor: a short broken stump (its texels keep their size).
            var holder = Group(t, "Model");
            var stump = (GameObject)PrefabUtility.InstantiatePrefab(pillar, holder);
            stump.transform.localPosition = new Vector3(0f, -1.55f, 0f); stump.transform.localEulerAngles = new Vector3(0f, 12f, 0f); stump.transform.localScale = new Vector3(.85f, 1f, .85f);
            var p = t.gameObject.AddComponent<DungeonKitPiece>(); p.model = holder.gameObject; p.replaces = new[] { g.gameObject };
        });
        Kit("Arch", false, t =>
        {
            var g = Group(t, "Blocks");
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                Block(g, "Base", new Vector3(x, 0f, 0f), new Vector3(.85f, .3f, .85f), Trim, Trim);
                Block(g, "Post", new Vector3(x, .3f, 0f), new Vector3(.6f, 2.3f, .6f), Wall, Trim);
            }
            var lintel = Block(g, "Lintel", new Vector3(0f, 2.6f, 0f), new Vector3(3.5f, .4f, .75f), Trim, Trim); lintel.solid = false;
            // Synty's archway runs from x = -2.5 to 0: centred on the piece.
            UseModel(t, arch, new Vector3(1.25f, 0f, 0f), Vector3.zero, Vector3.one, false, g);
        });
        Kit("Niche", true, t =>
        {
            foreach (float x in new[] { -.95f, .95f }) Block(t, "Jamb", new Vector3(x, 0f, -.35f), new Vector3(.35f, 2.2f, .7f), Wall, Trim);
            Block(t, "Header", new Vector3(0f, 2.2f, -.35f), new Vector3(2.25f, .45f, .7f), Trim, Trim);
            Block(t, "Ledge", new Vector3(0f, 0f, -.24f), new Vector3(1.55f, .55f, .48f), Trim, Trim);
            var back = Block(t, "Recess", new Vector3(0f, .55f, -.03f), new Vector3(1.55f, 1.65f, .06f), Upper, Upper); back.solid = false;
        });
        Kit("Platform", false, t =>
        {
            Block(t, "Dais", new Vector3(0f, 0f, -1.2f), new Vector3(4f, .75f, 2.4f), Wall, Floor);
            Block(t, "Step high", new Vector3(0f, 0f, -2.62f), new Vector3(1.8f, .5f, .45f), Trim, Floor);
            Block(t, "Step low", new Vector3(0f, 0f, -3.07f), new Vector3(1.8f, .25f, .45f), Trim, Floor);
        });
        void Gallery(string name, float stairSide)
        {
            Kit(name, false, t =>
            {
                Block(t, "Walkway", new Vector3(0f, 0f, -.9f), new Vector3(6f, 1.25f, 1.8f), Wall, Floor);
                for (int i = 0; i < 4; i++)
                    Block(t, "Step " + (i + 1), new Vector3(stairSide * 2.3f, 0f, -1.8f - .225f - .45f * i), new Vector3(1.4f, 1.25f - .25f * (i + 1), .45f), Trim, Floor);
                var g = Group(t, "Railing blocks");
                for (float x = -2.8f; x <= 2.81f; x += 1.4f)
                    if (x * stairSide < 1.5f) Block(g, "Post", new Vector3(x, 1.25f, -1.7f), new Vector3(.18f, .85f, .18f), Trim, Trim);
                Block(g, "Rail", new Vector3(-stairSide * .8f, 2.1f, -1.7f), new Vector3(4.4f, .14f, .16f), Trim, Trim);
                // Two lengths of Synty railing (each runs 2.5 m towards -x from its pivot) along the walkway's edge, short of the stairs.
                var holder = Group(t, "Model");
                float near = stairSide > 0 ? 1.4f : 2.9f, far = stairSide > 0 ? -.4f : 1.1f;
                foreach (float x in new[] { near, far })
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(railing, holder);
                    r.transform.localPosition = new Vector3(x, 1.25f, -1.7f);
                }
                var p = t.gameObject.AddComponent<DungeonKitPiece>();
                p.model = holder.gameObject; p.replaces = new[] { g.gameObject };
            });
        }
        Gallery("Gallery (stairs right)", 1f);
        Gallery("Gallery (stairs left)", -1f);
        Kit("Rubble", true, t =>
        {
            var g = Group(t, "Blocks");
            Block(g, "Slab", new Vector3(0f, 0f, 0f), new Vector3(1.1f, .4f, .8f), Wall, Wall, new Vector3(0f, 25f, 8f));
            Block(g, "Block", new Vector3(.55f, 0f, .45f), new Vector3(.6f, .5f, .55f), Wall, Wall, new Vector3(4f, 61f, 0f));
            Block(g, "Chip", new Vector3(-.6f, 0f, .35f), new Vector3(.4f, .3f, .35f), Trim, Trim, new Vector3(0f, -18f, 10f));
            Block(g, "Shard", new Vector3(.2f, 0f, -.6f), new Vector3(.5f, .22f, .4f), Wall, Wall, new Vector3(-6f, 77f, 0f));
        });
    }

    // ── Chunks ──────────────────────────────────────────────────────

    class Chunk
    {
        public string name; public int w, h, heightTiles; public Vector2Int centre; public List<Vector2Int> doors = new();
        public Transform root;
        public Vector3 Cell(float x, float y) => new((x - centre.x) * C, 0f, (y - centre.y) * C);
    }

    static Chunk Begin(string name, int w, int h, int heightTiles, params Vector2Int[] doors)
    {
        var c = new Chunk { name = name, w = w, h = h, heightTiles = heightTiles, centre = new Vector2Int(w / 2, h / 2) };
        c.doors.AddRange(doors);
        c.root = new GameObject(name).transform;
        return c;
    }

    static GameObject Save(Chunk c)
    {
        string path = $"{ChunkFolder}/{c.name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !overwrite) { UnityEngine.Object.DestroyImmediate(c.root.gameObject); return existing; }
        var t = c.root.gameObject.AddComponent<DungeonRoomTemplate>();
        var rows = new string[c.h];
        for (int y = 0; y < c.h; y++)
        {
            var row = new char[c.w];
            for (int x = 0; x < c.w; x++) row[x] = c.doors.Contains(new Vector2Int(x, y)) ? '+' : new Vector2Int(x, y) == c.centre ? '*' : '#';
            rows[y] = new string(row);
        }
        t.paintFootprint = true; t.width = c.w; t.height = c.h; t.rows = rows; t.allowRotation = true;
        t.heightTiles = c.heightTiles; t.replaceGeneratedProps = false; t.replaceGeneratedEnemies = true; t.replaceGeneratedTorches = false;   // the pack is the hand-placed Enemy sockets
        foreach (var b in c.root.GetComponentsInChildren<DungeonKitBlock>(true)) b.UpdatePreview();
        var prefab = PrefabUtility.SaveAsPrefabAsset(c.root.gameObject, path);
        UnityEngine.Object.DestroyImmediate(c.root.gameObject);
        return prefab;
    }

    static Transform Piece(Chunk c, string kitName, Vector3 pos, float yaw = 0f)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(kit[kitName], c.root);
        go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        return go.transform;
    }

    // A wall piece on the edge of cell (x, y), its front facing into the room along `into`.
    static Transform WallPiece(Chunk c, string kitName, int x, int y, Vector2Int into)
    {
        var back = new Vector3(-into.x, 0f, -into.y);
        float yaw = Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg;
        return Piece(c, kitName, c.Cell(x, y) + back * (C * .5f - .05f), yaw);
    }

    static void Socket(Chunk c, DungeonSocketType type, Vector3 pos, float chance = 1f, float yaw = 0f, GameObject prefab = null)
    {
        var go = new GameObject(type + " socket"); go.transform.SetParent(c.root, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        var s = go.AddComponent<DungeonSocket>(); s.type = type; s.chance = chance; s.overridePrefab = prefab;
    }

    // A wall light facing the wall it hangs on.
    static void Light(Chunk c, int x, int y, Vector2Int wall) => Socket(c, DungeonSocketType.WallLight, c.Cell(x, y), 1f, Mathf.Atan2(wall.x, wall.y) * Mathf.Rad2Deg);

    static readonly Vector2Int East = new(1, 0), West = new(-1, 0), North = new(0, 1), South = new(0, -1);

    static List<GameObject> BuildChunks()
    {
        var made = new List<GameObject>();
        var altar = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Prefabs/Features/Altar.prefab");
        var brazier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Prefabs/Furnishing/Brazier.prefab");

        // Two rows of pillars down a tall hall, niches in the side walls.
        var c = Begin("Pillared hall", 9, 7, 2, new Vector2Int(4, 0), new Vector2Int(4, 6));
        foreach (var (x, y) in new[] { (2, 1), (6, 1), (2, 5), (6, 5) }) Piece(c, "Pillar", c.Cell(x, y));
        foreach (var (x, into) in new[] { (0, East), (8, West) }) foreach (int y in new[] { 1, 5 }) { var n = WallPiece(c, "Niche", x, y, into); Socket(c, DungeonSocketType.Breakable, n.localPosition + new Vector3(into.x, 0, into.y) * .75f, .45f); }
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 4), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 3), .5f);
        Socket(c, DungeonSocketType.Feature, c.Cell(2, 3) + new Vector3(-.4f, 0f, 0f), 1f, 0f, brazier); Socket(c, DungeonSocketType.Feature, c.Cell(6, 3) + new Vector3(.4f, 0f, 0f), 1f, 0f, brazier);
        Light(c, 0, 3, West); Light(c, 8, 3, East);
        made.Add(Save(c));

        // A raised dais with an altar at the far end, braziers either side, pillars down the nave.
        c = Begin("Raised altar", 9, 9, 2, new Vector2Int(4, 0));
        WallPiece(c, "Platform", 4, 8, South);
        Socket(c, DungeonSocketType.Feature, c.Cell(4, 8) + new Vector3(0f, .75f, -.2f), 1f, 180f, altar);
        Socket(c, DungeonSocketType.Chest, c.Cell(4, 8) + new Vector3(-1.4f, .75f, -.2f), .35f, 180f);   // on the dais, beside the altar
        Socket(c, DungeonSocketType.Feature, c.Cell(2, 7), 1f, 0f, brazier); Socket(c, DungeonSocketType.Feature, c.Cell(6, 7), 1f, 0f, brazier);
        foreach (var (x, y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) }) Piece(c, "Pillar", c.Cell(x, y));
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 6), .6f);
        Light(c, 0, 6, West); Light(c, 8, 6, East);
        made.Add(Save(c));

        // A long burial passage lined with niches, an urn in some.
        c = Begin("Crypt niches", 5, 9, 1, new Vector2Int(2, 0), new Vector2Int(2, 8));
        foreach (var (x, into) in new[] { (0, East), (4, West) }) foreach (int y in new[] { 1, 2, 6, 7 }) { var n = WallPiece(c, "Niche", x, y, into); Socket(c, DungeonSocketType.Breakable, n.localPosition + new Vector3(into.x, 0, into.y) * .75f, .45f); }
        Socket(c, DungeonSocketType.Enemy, c.Cell(1, 3), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(3, 5), .8f);
        Light(c, 0, 4, West); Light(c, 4, 4, East);
        made.Add(Save(c));

        // Two stone gates across the hall, pillars in the corners, a fallen block.
        c = Begin("Gate hall", 9, 9, 2, new Vector2Int(4, 0), new Vector2Int(4, 8), new Vector2Int(0, 4));
        Piece(c, "Arch", c.Cell(4, 2)); Piece(c, "Arch", c.Cell(4, 6));
        foreach (var (x, y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) }) Piece(c, "Pillar", c.Cell(x, y));
        Piece(c, "Rubble", c.Cell(7, 7), 30f);
        Socket(c, DungeonSocketType.Feature, c.Cell(3, 4), 1f, 0f, brazier); Socket(c, DungeonSocketType.Feature, c.Cell(5, 4), 1f, 0f, brazier);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 3), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 4), .5f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(7, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 7), .6f);
        Light(c, 8, 4, East); Light(c, 3, 8, North);
        made.Add(Save(c));

        // A tall hall with raised walkways along the far wall (archers up there), pillars in the floor.
        c = Begin("Gallery hall", 11, 9, 3, new Vector2Int(5, 0), new Vector2Int(0, 4), new Vector2Int(10, 4));
        var left = Piece(c, "Gallery (stairs right)", c.Cell(2.5f, 8) + new Vector3(0f, 0f, C * .5f - .05f), 0f);
        var right = Piece(c, "Gallery (stairs left)", c.Cell(7.5f, 8) + new Vector3(0f, 0f, C * .5f - .05f), 0f);
        foreach (var g in new[] { left, right }) { g.localEulerAngles = Vector3.zero; Socket(c, DungeonSocketType.Enemy, g.localPosition + new Vector3(-.8f, 1.25f, -.9f), .7f); Socket(c, DungeonSocketType.Feature, g.localPosition + new Vector3(.6f, 1.25f, -.5f), 1f, 0f, brazier); }
        foreach (var (x, y) in new[] { (2, 3), (8, 3), (3, 5), (7, 5) }) Piece(c, "Pillar", c.Cell(x, y));
        Socket(c, DungeonSocketType.Enemy, c.Cell(4, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 2), .6f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(1, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(9, 1), .6f);
        Light(c, 0, 6, West); Light(c, 10, 6, East); Light(c, 5, 8, North);
        made.Add(Save(c));

        // A low chamber half fallen in.
        c = Begin("Ruined chamber", 7, 7, 1, new Vector2Int(3, 0), new Vector2Int(6, 3));
        Piece(c, "Rubble", c.Cell(1, 1), 15f); Piece(c, "Rubble", c.Cell(5, 5), 200f); Piece(c, "Rubble", c.Cell(1, 5), 95f);
        Piece(c, "Broken pillar", c.Cell(5, 1), 40f); Piece(c, "Pillar", c.Cell(2, 5));
        Socket(c, DungeonSocketType.Feature, c.Cell(4, 2), .7f, 0f, brazier);
        Socket(c, DungeonSocketType.Enemy, c.Cell(4, 4), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(2, 2), .6f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(5, 2), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 4), .5f);
        Light(c, 0, 3, West);
        made.Add(Save(c));
        return made;
    }

    // A blank hand-built room: a painted 7 x 7 footprint with its centre and one door, opened in Prefab Mode. Paint the
    // footprint in the inspector, drag pieces from Rooms/Kit (they take the room's stone when it is built) and sockets in,
    // then add it to a biome's Room Templates.
    [MenuItem("Tools/LoomRoom/Dungeon/New Room Template")]
    public static void NewRoomTemplate()
    {
        if (!AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.CreateFolder(Rooms + "/Templates", "Chunks");
        var c = Begin("New room", 7, 7, 1, new Vector2Int(3, 0));
        Light(c, 0, 3, West); Light(c, 6, 3, East);
        Socket(c, DungeonSocketType.Enemy, c.Cell(2, 4), .8f);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{ChunkFolder}/New room.prefab");
        var t = c.root.gameObject.AddComponent<DungeonRoomTemplate>();
        var rows = new string[c.h];
        for (int y = 0; y < c.h; y++) { var row = new char[c.w]; for (int x = 0; x < c.w; x++) row[x] = c.doors.Contains(new Vector2Int(x, y)) ? '+' : new Vector2Int(x, y) == c.centre ? '*' : '#'; rows[y] = new string(row); }
        t.paintFootprint = true; t.width = c.w; t.height = c.h; t.rows = rows; t.replaceGeneratedProps = false; t.replaceGeneratedEnemies = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(c.root.gameObject, path);
        UnityEngine.Object.DestroyImmediate(c.root.gameObject);
        AssetDatabase.OpenAsset(prefab);
        Selection.activeObject = prefab;
    }

    // Adds my chunks the biome doesn't list yet; entries already there (and their weights) stay as they are.
    static void Pool(string biomePath, List<GameObject> chunks, Dictionary<string, float> weights)
    {
        var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomePath);
        if (biome == null) return;
        var list = new List<DungeonBiome.RoomTemplateChoice>(biome.roomTemplates ?? new DungeonBiome.RoomTemplateChoice[0]);
        foreach (var chunk in chunks)
        {
            if (list.Exists(e => e != null && e.template == chunk)) continue;
            list.Add(new DungeonBiome.RoomTemplateChoice { template = chunk, weight = weights.TryGetValue(chunk.name, out var w) ? w : 1f,
                rooms = chunk.name == "Raised altar" ? DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest : DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage });
        }
        biome.roomTemplates = list.ToArray();
        EditorUtility.SetDirty(biome);
    }
}
