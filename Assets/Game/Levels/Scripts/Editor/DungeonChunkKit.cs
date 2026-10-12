using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Hand-built rooms ("chunks", Heathen's way of building a floor) and the kit they are made of. The kit pieces are
// DungeonKitBlocks, so every pillar, arch, niche, platform and gallery is built of the room's own stone when the floor
// is generated. Writes the kit to Levels/Dungeon1/Rooms/Kit, the chunks to Rooms/Templates/Chunks, and puts the chunks
// in Cellars' and Crypt's room pools (DungeonBiome.roomTemplates). Safe to run again (it rebuilds them).
//
// To make a room by hand: Tools > LoomRoom > Dungeon > New Room Template (DungeonRoomTemplateTools), paint the
// footprint in its inspector, drag kit pieces and sockets in, and add it to a biome's Room Templates.
public static class DungeonChunkKit
{
    const string Rooms = "Assets/Game/Levels/Dungeon1/Rooms";
    const string KitFolder = Rooms + "/Kit";
    const string ChunkFolder = Rooms + "/Templates/Chunks";
    const float C = 2f;   // Dungeon1's cell size
    static readonly Dictionary<string, GameObject> kit = new();

    [MenuItem("Tools/LoomRoom/Dungeon/Build Kit and Chunks")]
    public static string Run()
    {
        if (!AssetDatabase.IsValidFolder(KitFolder)) AssetDatabase.CreateFolder(Rooms, "Kit");
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

    // ── Kit ─────────────────────────────────────────────────────────

    static DungeonKitBlock Block(Transform parent, string name, Vector3 pos, Vector3 size, DungeonKitBlock.Surface sides = DungeonKitBlock.Surface.Wall,
        DungeonKitBlock.Surface top = DungeonKitBlock.Surface.Floor, Vector3? euler = null)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler ?? Vector3.zero;
        var b = go.AddComponent<DungeonKitBlock>(); b.size = size; b.sides = sides; b.top = top;
        return b;
    }

    static GameObject Kit(string name, bool removeIfBlocking, Action<Transform> build)
    {
        var root = new GameObject(name);
        root.AddComponent<DungeonTemplatePiece>().removeIfBlocking = removeIfBlocking;
        build(root.transform);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{KitFolder}/{name}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        kit[name] = prefab;
        return prefab;
    }

    const DungeonKitBlock.Surface Wall = DungeonKitBlock.Surface.Wall, Trim = DungeonKitBlock.Surface.Trim, Floor = DungeonKitBlock.Surface.Floor, Upper = DungeonKitBlock.Surface.UpperWall;

    // Wall pieces are built with their back on local z = 0 (the wall) and their front towards -z (into the room).
    static void BuildKit()
    {
        kit.Clear();
        Kit("Pillar", true, t =>
        {
            Block(t, "Base", Vector3.zero, new Vector3(1f, .3f, 1f), Trim, Trim);
            var shaft = Block(t, "Shaft", new Vector3(0f, .3f, 0f), new Vector3(.64f, 2.2f, .64f), Wall, Trim); shaft.toCeiling = true; shaft.ceilingGap = .45f;
            var cap = Block(t, "Capital", Vector3.zero, new Vector3(.95f, .45f, .95f), Trim, Trim); cap.fromCeiling = true; cap.solid = false;
        });
        Kit("Broken pillar", true, t =>
        {
            Block(t, "Base", Vector3.zero, new Vector3(1f, .3f, 1f), Trim, Trim);
            Block(t, "Stump", new Vector3(0f, .3f, 0f), new Vector3(.64f, 1.25f, .64f), Wall, Wall, new Vector3(0f, 12f, 0f));
            Block(t, "Fallen drum", new Vector3(.7f, 0f, -.55f), new Vector3(.64f, .5f, .9f), Wall, Wall, new Vector3(0f, 34f, 6f));
        });
        Kit("Arch", false, t =>
        {
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                Block(t, "Base", new Vector3(x, 0f, 0f), new Vector3(.85f, .3f, .85f), Trim, Trim);
                Block(t, "Post", new Vector3(x, .3f, 0f), new Vector3(.6f, 2.3f, .6f), Wall, Trim);
            }
            var lintel = Block(t, "Lintel", new Vector3(0f, 2.6f, 0f), new Vector3(3.5f, .4f, .75f), Trim, Trim); lintel.solid = false;
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
                for (float x = -2.8f; x <= 2.81f; x += 1.4f)
                    if (x * stairSide < 1.5f) Block(t, "Post", new Vector3(x, 1.25f, -1.7f), new Vector3(.18f, .85f, .18f), Trim, Trim);
                var rail = Block(t, "Rail", new Vector3(-stairSide * .8f, 2.1f, -1.7f), new Vector3(4.4f, .14f, .16f), Trim, Trim);
            });
        }
        Gallery("Gallery (stairs right)", 1f);
        Gallery("Gallery (stairs left)", -1f);
        Kit("Rubble", true, t =>
        {
            Block(t, "Slab", new Vector3(0f, 0f, 0f), new Vector3(1.1f, .4f, .8f), Wall, Wall, new Vector3(0f, 25f, 8f));
            Block(t, "Block", new Vector3(.55f, 0f, .45f), new Vector3(.6f, .5f, .55f), Wall, Wall, new Vector3(4f, 61f, 0f));
            Block(t, "Chip", new Vector3(-.6f, 0f, .35f), new Vector3(.4f, .3f, .35f), Trim, Trim, new Vector3(0f, -18f, 10f));
            Block(t, "Shard", new Vector3(.2f, 0f, -.6f), new Vector3(.5f, .22f, .4f), Wall, Wall, new Vector3(-6f, 77f, 0f));
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
        var prefab = PrefabUtility.SaveAsPrefabAsset(c.root.gameObject, $"{ChunkFolder}/{c.name}.prefab");
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

    static void Pool(string biomePath, List<GameObject> chunks, Dictionary<string, float> weights)
    {
        var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomePath);
        if (biome == null) return;
        var list = new List<DungeonBiome.RoomTemplateChoice>();
        foreach (var chunk in chunks)
            list.Add(new DungeonBiome.RoomTemplateChoice { template = chunk, weight = weights.TryGetValue(chunk.name, out var w) ? w : 1f,
                rooms = chunk.name == "Raised altar" ? DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest : DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage });
        biome.roomTemplates = list.ToArray();
        biome.templateChance = .7f;
        EditorUtility.SetDirty(biome);
    }
}
