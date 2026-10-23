using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Hand-built rooms ("chunks") and the kit they are made of. Kit pieces are
// DungeonKitBlocks (built of the room's own stone when the floor is generated); the pillar, broken pillar, arch and
// gallery railing also carry a painted model (Rooms/Kit/Models, Synty Dark Fantasy pieces through the painted-prop
// pipeline) that the level uses instead of those blocks while its Kit Models is on (base biome, TableLevelData.kitModels; DungeonKitPiece).
// Writes the kit to Levels/Dungeon1/Rooms/Kit, the chunks to Levels/Shared/Rooms (every Dungeon level shares them), and adds the chunks to Cellars' and
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
    const string ChunkFolder = "Assets/Game/Levels/Shared/Rooms";
    const string Synty = "Assets/Synty/PolygonDarkFantasy/Prefabs";
    const float C = 2f;   // Dungeon1's cell size
    static readonly Dictionary<string, GameObject> kit = new();
    static bool overwrite;

    public static string Run() => Run(false);

    public static string RunOverwrite() => Run(true);

    public static string Run(bool replaceExisting)
    {
        overwrite = replaceExisting;
        if (!AssetDatabase.IsValidFolder(KitFolder)) AssetDatabase.CreateFolder(Rooms, "Kit");
        if (!AssetDatabase.IsValidFolder(ModelFolder)) AssetDatabase.CreateFolder(KitFolder, "Models");
        if (!AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.CreateFolder("Assets/Game/Levels/Shared", "Rooms");
        BuildKit();
        var chunks = BuildChunks();
        var log = new System.Text.StringBuilder($"kit {kit.Count} pieces, chunks: ");
        foreach (var c in chunks) log.Append(c.name).Append(", ");
        Pool("Assets/Game/Levels/Shared/Biomes/Cellars.asset", chunks, c => c.cellars);
        Pool("Assets/Game/Levels/Shared/Biomes/Crypt.asset", chunks, c => c.crypt);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // Shows every kit block's shape in the editor (prefab thumbnails, Prefab Mode): for prefabs made before previews existed.
    public static string RefreshPreviews()
    {
        int n = 0;
        // Every level's rooms and kit pieces, not only the shared ones.
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels", "Assets/Game/Development" }))
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
        public DungeonRoomEvent roomEvent;
        public DungeonRoomLight mood;
        public bool openCeiling, noTorches;
        public int skylightRadius;
        // The kinds of room it may become, and its weight in Cellars and in the Crypt (0: not in that biome).
        public DungeonRoomRoles rooms = DungeonRoomRoles.Combat;
        public float cellars = 1f, crypt = 1f;
        // Most copies on one floor (DungeonBiome.RoomTemplateChoice.maxPerFloor); cells built as solid wall ('O').
        public int maxPerFloor = 2;
        public HashSet<Vector2Int> solid = new();
        public float Ceiling => heightTiles * 3f;
        public Vector3 Cell(float x, float y) => new((x - centre.x) * C, 0f, (y - centre.y) * C);
    }

    static readonly List<Chunk> chunkInfo = new();

    static Chunk Begin(string name, int w, int h, int heightTiles, params Vector2Int[] doors)
    {
        var c = new Chunk { name = name, w = w, h = h, heightTiles = heightTiles, centre = new Vector2Int(w / 2, h / 2) };
        c.doors.AddRange(doors);
        c.root = new GameObject(name).transform;
        return c;
    }

    static GameObject Save(Chunk c)
    {
        chunkInfo.Add(c);
        string path = $"{ChunkFolder}/{c.name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !overwrite) { UnityEngine.Object.DestroyImmediate(c.root.gameObject); return existing; }
        var t = c.root.gameObject.AddComponent<DungeonRoomTemplate>();
        var rows = new string[c.h];
        for (int y = 0; y < c.h; y++)
        {
            var row = new char[c.w];
            for (int x = 0; x < c.w; x++) { var p = new Vector2Int(x, y); row[x] = c.doors.Contains(p) ? '+' : c.solid.Contains(p) ? 'O' : p == c.centre ? '*' : '#'; }
            rows[y] = new string(row);
        }
        t.paintFootprint = true; t.width = c.w; t.height = c.h; t.rows = rows; t.allowRotation = true;
        t.heightTiles = c.heightTiles; t.replaceGeneratedProps = false; t.replaceGeneratedEnemies = true;   // the pack is the hand-placed Enemy sockets
        t.replaceGeneratedTorches = c.noTorches || c.mood == DungeonRoomLight.Dark;
        t.roomEvent = c.roomEvent;
        t.openCeiling = c.openCeiling; t.skylightRadius = c.skylightRadius; t.ceilingFrame = .3f;
        if (c.mood != DungeonRoomLight.Normal) c.root.gameObject.AddComponent<DungeonRoomMood>().kind = c.mood;
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

    static DungeonSocket Socket(Chunk c, DungeonSocketType type, Vector3 pos, float chance = 1f, float yaw = 0f, GameObject prefab = null)
    {
        var go = new GameObject(type + " socket"); go.transform.SetParent(c.root, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        var s = go.AddComponent<DungeonSocket>(); s.type = type; s.chance = chance; s.overridePrefab = prefab;
        return s;
    }

    static void Archer(Chunk c, Vector3 pos, float chance = 1f) => Socket(c, DungeonSocketType.Enemy, pos, chance).archer = true;

    // A wall light facing the wall it hangs on.
    static void Light(Chunk c, int x, int y, Vector2Int wall) => Socket(c, DungeonSocketType.WallLight, c.Cell(x, y), 1f, Mathf.Atan2(wall.x, wall.y) * Mathf.Rad2Deg);

    static readonly Vector2Int East = new(1, 0), West = new(-1, 0), North = new(0, 1), South = new(0, -1);

    static readonly Dictionary<string, GameObject> props = new();
    static GameObject PropPrefab(string path)
    {
        if (!props.TryGetValue(path, out var p)) props[path] = p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Levels/Dungeon1/Prefabs/{path}.prefab");
        return p;
    }

    // A prop from Prefabs (furnishing, features, effects); removeIfBlocking: taken out where it would block a walkway.
    static Transform Prop(Chunk c, string path, Vector3 pos, float yaw = 0f, bool removeIfBlocking = false, float scale = 1f)
    {
        var prefab = PropPrefab(path);
        if (prefab == null) { Debug.LogWarning($"Chunk kit: no prefab {path}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, c.root);
        go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        if (scale != 1f) go.transform.localScale = Vector3.one * scale;
        if (removeIfBlocking) go.AddComponent<DungeonTemplatePiece>().removeIfBlocking = true;
        return go.transform;
    }

    // A chain hanging from the ceiling (3 m long). Over walkable floor in a one-tile room, length shortens it so it
    // ends above head height; over a pit it may hang its full length.
    static void Chain(Chunk c, float x, float y, float length = 1f)
    {
        var chain = Prop(c, "Furnishing/Hanging chain", c.Cell(x, y) + Vector3.up * (c.Ceiling - .03f));
        if (length < 1f) chain.localScale = new Vector3(chain.localScale.x, chain.localScale.y * length, chain.localScale.z);
    }

    // A hole in the floor over cells x0..x1, y0..y1.
    static DungeonFloorCut Cut(Chunk c, DungeonFloorCut.Kind kind, int x0, int y0, int x1, int y1, float depth)
    {
        var go = new GameObject(kind == DungeonFloorCut.Kind.Pit ? "Pit" : "Sunken floor"); go.transform.SetParent(c.root, false);
        go.transform.localPosition = c.Cell((x0 + x1) * .5f, (y0 + y1) * .5f);
        var cut = go.AddComponent<DungeonFloorCut>(); cut.kind = kind; cut.cells = new Vector2Int(x1 - x0 + 1, y1 - y0 + 1); cut.depth = depth;
        return cut;
    }

    // Steps from the floor down into a sunken cut: they start at the cut's edge on the side `from` (the room side) of
    // cell (x, y), one cell wide, going `into` the cut.
    static void StepsDown(Chunk c, int x, int y, Vector2Int into, float depth)
    {
        var holder = new GameObject("Steps down").transform; holder.SetParent(c.root, false);
        holder.localPosition = c.Cell(x, y) - new Vector3(into.x, 0f, into.y) * (C * .5f);
        holder.localRotation = Quaternion.LookRotation(new Vector3(into.x, 0f, into.y));
        int steps = Mathf.Max(1, Mathf.RoundToInt(depth / .3f));
        float rise = depth / steps;
        for (int i = 1; i < steps; i++)
            Block(holder, "Step " + i, new Vector3(0f, -depth, .55f * (i - .5f)), new Vector3(C, depth - rise * i, .55f), Trim, Floor);
    }

    static List<GameObject> BuildChunks()
    {
        chunkInfo.Clear();
        var made = new List<GameObject>();
        var altar = PropPrefab("Features/Altar");
        var brazier = PropPrefab("Furnishing/Brazier");
        const DungeonRoomRoles Combat = DungeonRoomRoles.Combat, Treasure = DungeonRoomRoles.Treasure, Quiet = DungeonRoomRoles.Rest, Storage = DungeonRoomRoles.Storage;

        // 1. Two rows of pillars down a tall hall, niches in the side walls, chains hanging between the pillars.
        var c = Begin("Pillared hall", 9, 7, 2, new Vector2Int(4, 0), new Vector2Int(4, 6));
        c.rooms = Combat | Treasure | Quiet | Storage; c.cellars = 1f; c.crypt = .8f;
        foreach (var (x, y) in new[] { (2, 1), (6, 1), (2, 5), (6, 5) }) Piece(c, "Pillar", c.Cell(x, y));
        foreach (var (x, into) in new[] { (0, East), (8, West) }) foreach (int y in new[] { 1, 5 }) { var n = WallPiece(c, "Niche", x, y, into); Socket(c, DungeonSocketType.Breakable, n.localPosition + new Vector3(into.x, 0, into.y) * .75f, .45f); }
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 4), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 3), .5f);
        Socket(c, DungeonSocketType.Feature, c.Cell(2, 3) + new Vector3(-.4f, 0f, 0f), 1f, 0f, brazier); Socket(c, DungeonSocketType.Feature, c.Cell(6, 3) + new Vector3(.4f, 0f, 0f), 1f, 0f, brazier);
        Chain(c, 3.4f, 1.3f); Chain(c, 5.6f, 4.8f);
        Light(c, 0, 3, West); Light(c, 8, 3, East);
        made.Add(Save(c));

        // 2. A candlelit chapel of the dead: a raised dais with an altar, candle racks either side, pillars down the nave.
        c = Begin("Raised altar", 9, 9, 1, new Vector2Int(4, 0));
        c.rooms = Combat | Treasure; c.mood = DungeonRoomLight.Candlelit; c.cellars = .6f; c.crypt = 1f;
        WallPiece(c, "Platform", 4, 8, South);
        Socket(c, DungeonSocketType.Feature, c.Cell(4, 8) + new Vector3(0f, .75f, -.2f), 1f, 180f, altar);
        Socket(c, DungeonSocketType.Chest, c.Cell(4, 8) + new Vector3(-1.4f, .75f, -.2f), .35f, 180f);   // on the dais, beside the altar
        Prop(c, "Furnishing/Candle rack", c.Cell(1.5f, 7.6f), 180f); Prop(c, "Furnishing/Candle rack", c.Cell(6.5f, 7.6f), 180f);
        foreach (var (x, y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) }) { Piece(c, "Pillar", c.Cell(x, y)); Prop(c, "Furnishing/Candle cluster", c.Cell(x, y) + new Vector3(.75f, 0f, -.65f), 30f * x); }
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 6), .6f);
        Light(c, 0, 4, West); Light(c, 8, 4, East);
        made.Add(Save(c));

        // 3. A burial passage in green light, niches in both walls: the dead step out of them (an ambush).
        c = Begin("Crypt niches", 5, 9, 1, new Vector2Int(2, 0), new Vector2Int(2, 8));
        c.rooms = Combat; c.mood = DungeonRoomLight.Eerie; c.roomEvent = DungeonRoomEvent.Ambush; c.cellars = .5f; c.crypt = 1.2f; c.maxPerFloor = 1;
        foreach (var (x, into) in new[] { (0, East), (4, West) })
            foreach (int y in new[] { 1, 2, 6, 7 })
            {
                var n = WallPiece(c, "Niche", x, y, into);
                var front = n.localPosition + new Vector3(into.x, 0, into.y) * .75f;
                if (y == 2 || y == 6) Prop(c, "Furnishing/Skull pile", n.localPosition + new Vector3(into.x, 0, into.y) * .3f + Vector3.up * .55f, 90f * y, false, .55f);
                else Socket(c, DungeonSocketType.Breakable, front, .4f);
                Socket(c, DungeonSocketType.Enemy, front + new Vector3(into.x, 0, into.y) * .35f, .45f);
            }
        Prop(c, "Furnishing/Sarcophagus", c.Cell(1, 4) + new Vector3(-.2f, 0f, 0f)); Prop(c, "Furnishing/Sarcophagus", c.Cell(3, 4) + new Vector3(.2f, 0f, 0f));
        Light(c, 0, 4, West); Light(c, 4, 3, East);
        made.Add(Save(c));

        // 4. Two stone gates across a tall hall, pillars in the corners, a fallen block, chains.
        c = Begin("Gate hall", 9, 9, 2, new Vector2Int(4, 0), new Vector2Int(4, 8), new Vector2Int(0, 4));
        c.rooms = Combat | Treasure | Quiet | Storage; c.cellars = .8f; c.crypt = .9f;
        Piece(c, "Arch", c.Cell(4, 2)); Piece(c, "Arch", c.Cell(4, 6));
        foreach (var (x, y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) }) Piece(c, "Pillar", c.Cell(x, y));
        Piece(c, "Rubble", c.Cell(7, 7), 30f);
        Socket(c, DungeonSocketType.Feature, c.Cell(3, 4), 1f, 0f, brazier); Socket(c, DungeonSocketType.Feature, c.Cell(5, 4), 1f, 0f, brazier);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 3), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 4), .5f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(7, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 7), .6f);
        Chain(c, 2f, 4f); Chain(c, 6f, 4f);
        Light(c, 8, 4, East); Light(c, 3, 8, North);
        made.Add(Save(c));

        // 5. A tall hall with raised galleries along the far wall: archers shoot from up there while the rest close in.
        c = Begin("Gallery hall", 11, 9, 3, new Vector2Int(5, 0), new Vector2Int(0, 4), new Vector2Int(10, 4));
        c.rooms = Combat; c.cellars = .7f; c.crypt = .8f;
        var left = Piece(c, "Gallery (stairs right)", c.Cell(2.5f, 8) + new Vector3(0f, 0f, C * .5f - .05f), 0f);
        var right = Piece(c, "Gallery (stairs left)", c.Cell(7.5f, 8) + new Vector3(0f, 0f, C * .5f - .05f), 0f);
        foreach (var g in new[] { left, right }) { g.localEulerAngles = Vector3.zero; Archer(c, g.localPosition + new Vector3(-.8f, 1.25f, -.9f), .85f); Socket(c, DungeonSocketType.Feature, g.localPosition + new Vector3(.6f, 1.25f, -.5f), 1f, 0f, brazier); }
        foreach (var (x, y) in new[] { (2, 3), (8, 3), (3, 5), (7, 5) }) Piece(c, "Pillar", c.Cell(x, y));
        Socket(c, DungeonSocketType.Enemy, c.Cell(4, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 2), .6f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(1, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(9, 1), .6f);
        Chain(c, 5f, 6f);
        Light(c, 0, 6, West); Light(c, 10, 6, East); Light(c, 5, 8, North);
        made.Add(Save(c));

        // 6. A low chamber half fallen in, no torches: something sleeps in the dark.
        c = Begin("Ruined chamber", 7, 7, 1, new Vector2Int(3, 0), new Vector2Int(6, 3));
        c.rooms = Combat; c.mood = DungeonRoomLight.Dark; c.roomEvent = DungeonRoomEvent.SleepingPack; c.cellars = 1f; c.crypt = .7f;
        Piece(c, "Rubble", c.Cell(1, 1), 15f); Piece(c, "Rubble", c.Cell(5, 5), 200f); Piece(c, "Rubble", c.Cell(1, 5), 95f);
        Piece(c, "Broken pillar", c.Cell(5, 1), 40f); Piece(c, "Pillar", c.Cell(2, 5));
        Prop(c, "Furnishing/Bone pile", c.Cell(4, 5), 70f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(4, 4), .9f); Socket(c, DungeonSocketType.Enemy, c.Cell(2, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 3), .6f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(5, 2), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 4), .5f);
        made.Add(Save(c));

        // 7. A chasm across the hall, one narrow way over it. Archers wait on the far side; a blow near the edge throws an enemy down.
        c = Begin("Chasm bridge", 9, 7, 1, new Vector2Int(4, 0), new Vector2Int(4, 6), new Vector2Int(0, 1), new Vector2Int(8, 5));
        c.rooms = Combat; c.cellars = .9f; c.crypt = .9f; c.maxPerFloor = 1;
        Cut(c, DungeonFloorCut.Kind.Pit, 0, 3, 3, 4, 4f); Cut(c, DungeonFloorCut.Kind.Pit, 5, 3, 8, 4, 4f);
        Chain(c, 1.6f, 3.4f); Chain(c, 6.6f, 3.7f); Chain(c, 7.4f, 3.2f);
        Archer(c, c.Cell(2, 5.6f), .85f); Archer(c, c.Cell(6, 5.6f), .6f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(4, 5), .9f); Socket(c, DungeonSocketType.Enemy, c.Cell(3, 1), .5f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(7, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 6), .6f);
        Light(c, 0, 2, West); Light(c, 8, 5, East); Light(c, 6, 6, North);
        made.Add(Save(c));

        // 8. A sunken court open to the sky: moonlight falls into the middle, pillars stand on the rim.
        c = Begin("Sunken court", 9, 9, 1, new Vector2Int(4, 0), new Vector2Int(4, 8), new Vector2Int(0, 4), new Vector2Int(8, 4));
        c.rooms = Combat | Quiet; c.mood = DungeonRoomLight.Moonlit; c.openCeiling = true; c.skylightRadius = 1; c.cellars = .8f; c.crypt = .8f;
        const float sunk = .9f;
        Cut(c, DungeonFloorCut.Kind.Sunken, 2, 2, 6, 6, sunk);
        StepsDown(c, 4, 2, North, sunk); StepsDown(c, 4, 6, South, sunk); StepsDown(c, 2, 4, East, sunk); StepsDown(c, 6, 4, West, sunk);
        foreach (var (x, y) in new[] { (1, 1), (7, 1), (1, 7), (7, 7) }) Piece(c, "Pillar", c.Cell(x, y));
        Prop(c, "Props/Effects/Moonlight", c.Cell(4, 4) + Vector3.down * sunk);
        Prop(c, "Furnishing/Bone heap", c.Cell(3, 5) + Vector3.down * sunk, 40f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 3) + Vector3.down * sunk, .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 5) + Vector3.down * sunk, .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(1, 4), .5f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(7, 2), .5f); Socket(c, DungeonSocketType.Breakable, c.Cell(2, 7), .5f);
        Light(c, 0, 2, West); Light(c, 8, 6, East);
        made.Add(Save(c));

        // 9. A small vault: an iron chest on a pressure plate, the bones of the last one who opened it.
        c = Begin("Treasure vault", 5, 5, 1, new Vector2Int(2, 0));
        c.rooms = Treasure; c.cellars = 1f; c.crypt = 1f; c.maxPerFloor = 1;
        var chest = Prop(c, "Props/Chests/Iron chest", c.Cell(2, 2), 180f);
        var trap = chest.gameObject.AddComponent<DungeonChestTrap>();
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var plate = Prop(c, "Traps/Spike trap", c.Cell(2 + dx, 2 + dy), 90f * ((dx + dy) & 3));
                trap.traps.Add(plate.GetComponent<DungeonSpikeTrap>());
            }
        Prop(c, "Furnishing/Bone heap", c.Cell(1, 1) + new Vector3(.2f, .05f, .3f), 25f); Prop(c, "Furnishing/Skull pile", c.Cell(3, 3) + new Vector3(.3f, .05f, .2f), 140f, false, .5f);
        Prop(c, "Furnishing/Candle cluster", c.Cell(0, 4) + new Vector3(-.4f, 0f, .4f)); Prop(c, "Furnishing/Candle cluster", c.Cell(4, 4) + new Vector3(.4f, 0f, .4f), 90f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 1), .5f);
        Light(c, 2, 4, North);
        made.Add(Save(c));

        // 10. An ossuary in green light: skulls in every niche and heaped on the floor, and a pack asleep among the bones.
        c = Begin("Ossuary", 7, 9, 1, new Vector2Int(3, 0), new Vector2Int(3, 8));
        c.rooms = Combat; c.mood = DungeonRoomLight.Eerie; c.roomEvent = DungeonRoomEvent.SleepingPack; c.cellars = .4f; c.crypt = 1.2f; c.maxPerFloor = 1;
        foreach (var (x, into) in new[] { (0, East), (6, West) })
            foreach (int y in new[] { 1, 2, 3, 5, 6, 7 })
            {
                var n = WallPiece(c, "Niche", x, y, into);
                Prop(c, "Furnishing/Skull pile", n.localPosition + new Vector3(into.x, 0, into.y) * .3f + Vector3.up * .55f, 60f * y, false, .55f);
            }
        foreach (var (x, y, yaw) in new[] { (1f, 4f, 10f), (5f, 4.3f, 200f), (2f, 7.5f, 90f), (4.6f, 1.2f, 300f) }) Prop(c, "Furnishing/Skull pile", c.Cell(x, y), yaw, true, .9f);
        Prop(c, "Furnishing/Bone heap", c.Cell(2, 2), 45f); Prop(c, "Furnishing/Bone pile", c.Cell(4, 6), 120f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(2, 3), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 5), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(2, 6), .6f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 2), .5f);
        Chain(c, 3f, 2.5f, .4f); Chain(c, 3f, 6f, .4f);
        Light(c, 3, 8, North); Light(c, 3, 0, South);
        made.Add(Save(c));

        // 11. A candlelit burial hall, rows of sarcophagi: once you are in, the lids grind and the dead come (an ambush).
        c = Begin("Burial hall", 9, 7, 1, new Vector2Int(4, 0), new Vector2Int(0, 3), new Vector2Int(8, 3));
        c.rooms = Combat; c.mood = DungeonRoomLight.Candlelit; c.roomEvent = DungeonRoomEvent.Ambush; c.cellars = .6f; c.crypt = 1.1f; c.maxPerFloor = 1;
        foreach (var (x, y) in new[] { (2, 1.5f), (6, 1.5f), (2, 4.5f), (6, 4.5f) })
        {
            Prop(c, "Furnishing/Sarcophagus", c.Cell(x, y), 0f, true);
            Prop(c, "Furnishing/Candle cluster", c.Cell(x, y) + new Vector3(x < 4 ? -1.05f : 1.05f, 0f, y < 3 ? -.9f : .9f), 40f * x);
            Socket(c, DungeonSocketType.Enemy, c.Cell(x, y) + new Vector3(x < 4 ? 1.1f : -1.1f, 0f, 0f), .45f);
        }
        Light(c, 4, 6, North);
        made.Add(Save(c));

        // 12. A chapel: pews facing a blood shrine under racks of candles. A quiet room.
        c = Begin("Chapel", 7, 11, 2, new Vector2Int(3, 0), new Vector2Int(0, 2), new Vector2Int(6, 2));
        c.rooms = Quiet; c.mood = DungeonRoomLight.Candlelit; c.cellars = 1f; c.crypt = 1f; c.maxPerFloor = 1;
        Prop(c, "Features/Shrine", c.Cell(3, 10) + new Vector3(0f, 0f, .35f), 180f);
        Prop(c, "Furnishing/Candle rack", c.Cell(1, 10) + new Vector3(0f, 0f, .45f), 180f); Prop(c, "Furnishing/Candle rack", c.Cell(5, 10) + new Vector3(0f, 0f, .45f), 180f);
        foreach (int y in new[] { 3, 4, 6, 7, 8 }) { Prop(c, "Furnishing/Pew", c.Cell(1.5f, y), 0f, true); Prop(c, "Furnishing/Pew", c.Cell(4.5f, y), 0f, true); }
        Prop(c, "Furnishing/Candle cluster", c.Cell(0, 9) + new Vector3(-.4f, 0f, 0f)); Prop(c, "Furnishing/Candle cluster", c.Cell(6, 9) + new Vector3(.4f, 0f, 0f), 120f);
        Socket(c, DungeonSocketType.Merchant, c.Cell(5, 1), 1f, 270f);
        Light(c, 0, 5, West); Light(c, 6, 5, East);
        made.Add(Save(c));

        // 13. A floor broken through into the dark below: holes everywhere, narrow ways between them.
        c = Begin("Broken floor", 9, 9, 1, new Vector2Int(4, 0), new Vector2Int(4, 8), new Vector2Int(0, 4), new Vector2Int(8, 4));
        c.rooms = Combat | Storage; c.cellars = .9f; c.crypt = .7f;
        Cut(c, DungeonFloorCut.Kind.Pit, 1, 1, 2, 2, 4f); Cut(c, DungeonFloorCut.Kind.Pit, 6, 2, 6, 2, 4f); Cut(c, DungeonFloorCut.Kind.Pit, 7, 6, 7, 6, 4f);
        Cut(c, DungeonFloorCut.Kind.Pit, 2, 6, 2, 6, 4f); Cut(c, DungeonFloorCut.Kind.Pit, 5, 6, 6, 6, 4f);
        Chain(c, 1.5f, 1.5f);
        Piece(c, "Rubble", c.Cell(3, 1), 60f); Piece(c, "Rubble", c.Cell(7, 3), 160f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 6), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(6, 4), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 2), .6f); Socket(c, DungeonSocketType.Enemy, c.Cell(3, 3), .4f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(7, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(1, 7), .6f);
        Light(c, 0, 6, West); Light(c, 8, 2, East);
        made.Add(Save(c));

        // 14. A guard post: a long table, barrels and crates, and the guards asleep around it.
        c = Begin("Guard post", 7, 7, 1, new Vector2Int(3, 0), new Vector2Int(6, 3));
        c.rooms = Combat; c.roomEvent = DungeonRoomEvent.SleepingPack; c.cellars = 1f; c.crypt = .6f;
        Prop(c, "Furnishing/Long table", c.Cell(1.5f, 4.5f), 0f, true);
        Prop(c, "Groups/Barrels huddled", c.Cell(5.3f, 5.3f), 0f, true); Prop(c, "Groups/Crates and sacks", c.Cell(5.2f, 1f), 180f, true);
        Prop(c, "Furnishing/Storage shelf", c.Cell(0, 1) + new Vector3(-.5f, 0f, 0f), 90f, true);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 4), .9f); Socket(c, DungeonSocketType.Enemy, c.Cell(1, 2.6f), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(3, 6), .6f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 2), .4f);
        Light(c, 3, 6, North);
        made.Add(Save(c));
        // ── Small and medium rooms (5 x 5 to 7 x 7): they fit where the halls don't ──

        // 15. A cell block: three cells behind bars along one wall, the last one broken open with something left inside.
        c = Begin("Cell block", 5, 7, 1, new Vector2Int(3, 0), new Vector2Int(3, 6));
        c.rooms = Combat | Storage; c.cellars = 1f; c.crypt = .8f;
        foreach (int y in new[] { 0, 2, 4, 6 }) c.solid.Add(new Vector2Int(0, y));
        foreach (int y in new[] { 1, 3 }) Prop(c, "Traps/Ambush gate", c.Cell(0, y) + new Vector3(C * .5f, 0f, 0f), 90f);
        Prop(c, "Furnishing/Bone heap", c.Cell(0, 1) + new Vector3(-.2f, 0f, .1f), 30f); Prop(c, "Furnishing/Skull pile", c.Cell(0, 3) + new Vector3(-.3f, 0f, -.2f), 200f, false, .45f);
        Prop(c, "Props/Supply chest", c.Cell(0, 5) + new Vector3(-.25f, 0f, 0f), 90f);
        Piece(c, "Rubble", c.Cell(1, 5) + new Vector3(-.4f, 0f, .3f), 70f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(2, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(3, 4), .7f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(4, 1), .6f); Socket(c, DungeonSocketType.Breakable, c.Cell(4, 5), .5f);
        Light(c, 4, 3, East);
        made.Add(Save(c));

        // 16. A stair that fell in: a hole in one corner where the steps went down, rubble round it, a chain over the drop.
        c = Begin("Collapsed stair", 5, 5, 1, new Vector2Int(2, 0), new Vector2Int(0, 2));
        c.rooms = Combat; c.cellars = 1f; c.crypt = 1f;
        Cut(c, DungeonFloorCut.Kind.Pit, 3, 3, 4, 4, 4f);
        Chain(c, 3.6f, 3.4f);
        Piece(c, "Rubble", c.Cell(2, 4), 120f); Piece(c, "Rubble", c.Cell(4, 2), 20f); Piece(c, "Broken pillar", c.Cell(1, 4), 70f);
        Socket(c, DungeonSocketType.Enemy, c.Cell(3, 1), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(1, 3), .6f);
        Socket(c, DungeonSocketType.Breakable, c.Cell(4, 0), .5f);
        Light(c, 0, 4, West);
        made.Add(Save(c));

        // 17. A small court open to the night: a fountain under the moon. A quiet room.
        c = Begin("Moonlit fountain", 5, 5, 2, new Vector2Int(2, 0), new Vector2Int(2, 4), new Vector2Int(0, 2));
        c.rooms = Quiet; c.mood = DungeonRoomLight.Moonlit; c.openCeiling = true; c.skylightRadius = 1; c.cellars = 1f; c.crypt = .8f; c.maxPerFloor = 1;
        Socket(c, DungeonSocketType.Feature, c.Cell(3, 3), 1f, 225f, PropPrefab("Features/Fountain"));
        Prop(c, "Props/Effects/Moonlight", c.Cell(3, 3));
        Prop(c, "Furnishing/Candle cluster", c.Cell(4, 1) + new Vector3(.4f, 0f, -.4f)); Prop(c, "Furnishing/Candle cluster", c.Cell(1, 4) + new Vector3(-.4f, 0f, .4f), 150f);
        Socket(c, DungeonSocketType.Merchant, c.Cell(4, 2), 1f, 270f);
        Light(c, 0, 0, South);
        made.Add(Save(c));

        // 18. A narrow chapel of bones: skulls in the wall niches, candles, the blood shrine at the end. A quiet room.
        c = Begin("Bone chapel", 5, 7, 1, new Vector2Int(2, 0));
        c.rooms = Quiet; c.mood = DungeonRoomLight.Candlelit; c.cellars = .7f; c.crypt = 1.1f; c.maxPerFloor = 1;
        Prop(c, "Features/Shrine", c.Cell(2, 6) + new Vector3(0f, 0f, .35f), 180f);
        foreach (var (x, into) in new[] { (0, East), (4, West) })
            foreach (int y in new[] { 2, 4 })
            {
                var n = WallPiece(c, "Niche", x, y, into);
                Prop(c, "Furnishing/Skull pile", n.localPosition + new Vector3(into.x, 0, into.y) * .3f + Vector3.up * .55f, 70f * y, false, .55f);
            }
        foreach (var (x, y) in new[] { (1f, 6f), (3f, 6f), (0.6f, 1f), (3.4f, 1f) }) Prop(c, "Furnishing/Candle cluster", c.Cell(x, y), 50f * x);
        Prop(c, "Furnishing/Bone pile", c.Cell(1, 3), 15f, true);
        made.Add(Save(c));

        // 19. A storeroom gone to the rats: crates, barrels and sacks along the walls and a nest of them asleep in the middle.
        c = Begin("Rat nest", 5, 5, 1, new Vector2Int(2, 0), new Vector2Int(4, 2));
        c.rooms = Combat | Storage; c.roomEvent = DungeonRoomEvent.SleepingPack; c.cellars = 1.1f; c.crypt = .6f;
        Prop(c, "Groups/Crates and sacks", c.Cell(0.6f, 4.1f), 0f, true); Prop(c, "Groups/Barrels huddled", c.Cell(3.6f, 4f), 200f, true);
        Prop(c, "Furnishing/Sacks", c.Cell(0.4f, 1f), 80f, true); Prop(c, "Furnishing/Bone pile", c.Cell(1.4f, 2.6f), 30f);
        var rat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Rat.prefab");
        foreach (var (x, y, chance) in new[] { (1.3f, 2.2f, 1f), (2.2f, 3f, .9f), (1.8f, 1.6f, .7f), (2.9f, 2.2f, .5f) }) Socket(c, DungeonSocketType.Enemy, c.Cell(x, y), chance, 40f * x, rat);
        Socket(c, DungeonSocketType.Breakable, c.Cell(4, 4), .6f);
        Light(c, 0, 2, West);
        made.Add(Save(c));

        // 20. A trapped hall: every step across it springs the plate underfoot (run!), and a chest at the far end.
        c = Begin("Spike hall", 3, 8, 1, new Vector2Int(1, 0));
        c.rooms = Treasure; c.cellars = 1f; c.crypt = 1f; c.maxPerFloor = 1;
        for (int y = 2; y <= 5; y++)
            for (int x = 0; x <= 2; x++)
            {
                var plate = Prop(c, "Traps/Spike trap", c.Cell(x, y), 90f * ((x + y) & 3));
                plate.GetComponent<DungeonSpikeTrap>().springOnStep = true;
            }
        Prop(c, "Props/Chests/Iron chest", c.Cell(1, 7), 180f);
        Prop(c, "Furnishing/Bone heap", c.Cell(0, 6) + new Vector3(-.2f, 0f, 0f), 60f); Prop(c, "Furnishing/Skull pile", c.Cell(2, 4) + new Vector3(.4f, .05f, 0f), 10f, false, .4f);
        Light(c, 1, 7, North); Light(c, 0, 1, West);
        made.Add(Save(c));

        // 21. A hall of watchers: stone gargoyles along the walls, one fallen to pieces, braziers between them.
        c = Begin("Statue hall", 7, 7, 2, new Vector2Int(3, 0), new Vector2Int(3, 6), new Vector2Int(0, 3));
        c.rooms = Combat | Treasure; c.cellars = .9f; c.crypt = 1f;
        foreach (var (x, y, yaw) in new[] { (1, 1, 45f), (5, 1, -45f), (5, 5, 225f) }) Prop(c, "Furnishing/Gargoyle statue", c.Cell(x, y), yaw, true);
        Piece(c, "Rubble", c.Cell(1, 5), 40f); Piece(c, "Broken pillar", c.Cell(1.3f, 4.6f), 200f);
        foreach (var (x, y) in new[] { (2, 2), (4, 4) }) Socket(c, DungeonSocketType.Feature, c.Cell(x, y) + new Vector3(x < 3 ? -.5f : .5f, 0f, 0f), 1f, 0f, brazier);
        Socket(c, DungeonSocketType.Enemy, c.Cell(2, 4), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(4, 2), .8f); Socket(c, DungeonSocketType.Enemy, c.Cell(5, 3), .5f);
        Light(c, 6, 3, East); Light(c, 3, 6, North);
        made.Add(Save(c));

        return made;
    }

    // A blank hand-built room: a painted 7 x 7 footprint with its centre and one door, opened in Prefab Mode. Paint the
    // footprint in the inspector, drag pieces from Rooms/Kit (they take the room's stone when it is built) and sockets in,
    // then add it to a biome's Room Templates.
    public static void NewRoomTemplate()
    {
        if (!AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.CreateFolder("Assets/Game/Levels/Shared", "Rooms");
        var prefab = NewRoomTemplate(ChunkFolder, "New room");
        AssetDatabase.OpenAsset(prefab);
        Selection.activeObject = prefab;
    }

    // The same blank room saved as <folder>/<name>.prefab (a free name), not opened (Asset Studio's New room).
    public static GameObject NewRoomTemplate(string folder, string name)
    {
        var c = Begin(name, 7, 7, 1, new Vector2Int(3, 0));
        Light(c, 0, 3, West); Light(c, 6, 3, East);
        Socket(c, DungeonSocketType.Enemy, c.Cell(2, 4), .8f);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.prefab");
        var t = c.root.gameObject.AddComponent<DungeonRoomTemplate>();
        var rows = new string[c.h];
        for (int y = 0; y < c.h; y++) { var row = new char[c.w]; for (int x = 0; x < c.w; x++) row[x] = c.doors.Contains(new Vector2Int(x, y)) ? '+' : new Vector2Int(x, y) == c.centre ? '*' : '#'; rows[y] = new string(row); }
        t.paintFootprint = true; t.width = c.w; t.height = c.h; t.rows = rows; t.replaceGeneratedProps = false; t.replaceGeneratedEnemies = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(c.root.gameObject, path);
        UnityEngine.Object.DestroyImmediate(c.root.gameObject);
        return prefab;
    }

    // Adds my chunks the biome doesn't list yet (with their kinds of room and weight); entries already there stay as you
    // set them, unless the kit is rebuilt with Overwrite. Entries whose prefab is gone are dropped.
    static void Pool(string biomePath, List<GameObject> chunks, Func<Chunk, float> weightOf)
    {
        var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomePath);
        if (biome == null) return;
        var list = new List<DungeonBiome.RoomTemplateChoice>(biome.roomTemplates ?? new DungeonBiome.RoomTemplateChoice[0]);
        list.RemoveAll(e => e == null || e.template == null);
        foreach (var chunk in chunks)
        {
            var info = chunkInfo.Find(i => i.name == chunk.name);
            if (info == null) continue;
            var entry = list.Find(e => e.template == chunk);
            float weight = weightOf(info);
            if (entry != null && !overwrite) continue;
            if (entry == null) { if (weight <= 0f) continue; entry = new DungeonBiome.RoomTemplateChoice { template = chunk }; list.Add(entry); }
            entry.weight = weight; entry.rooms = info.rooms; entry.maxPerFloor = info.maxPerFloor;
        }
        biome.roomTemplates = list.ToArray();
        EditorUtility.SetDirty(biome);
    }
}
