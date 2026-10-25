using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Room styles in the Asset Studio: who uses each one, and a corner of a room built from it for the view (a floor of
// cells, two walls two tiles high, footing and cornice), every tile drawing its material as the generator does (the
// base or one of the weighted variants, walls by row), so the tiles are seen side by side.
public static class AssetStudioStyles
{
    const int Wide = 6, Deep = 5, Rows = 2;
    const float WallThickness = .22f, TrimDepth = .04f, TrimInset = .02f;

    // The levels and biomes that build with this style, read from every level (its base biome and stages) and every
    // biome file.
    public static List<(DungeonBiome biome, TableLevelData level, string use)> Users(DungeonRoomStyle style)
    {
        var found = new List<(DungeonBiome, TableLevelData, string)>();
        var seen = new HashSet<DungeonBiome>();
        void Look(DungeonBiome b, TableLevelData level)
        {
            if (b == null || style == null || !seen.Add(b)) return;
            if (b.defaultStyle == style) found.Add((b, level, "default"));
            else if (b.roomStyles != null && b.roomStyles.Any(c => c != null && c.style == style)) found.Add((b, level, "rooms"));
            else if (b.corridorStyles != null && b.corridorStyles.Any(c => c != null && c.style == style)) found.Add((b, level, "corridors"));
        }
        foreach (var g in AssetDatabase.FindAssets("t:TableLevelData", new[] { "Assets/Game" }))
        {
            var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(g));
            if (level == null) continue;
            Look(level.baseBiome, level);
            if (level.stages != null) foreach (var s in level.stages) if (s != null) Look(s.biome, level);
        }
        foreach (var g in AssetDatabase.FindAssets("t:DungeonBiome", new[] { "Assets/Game" }))
            Look(AssetDatabase.LoadAssetAtPath<DungeonBiome>(AssetDatabase.GUIDToAssetPath(g)), null);
        return found;
    }

    public static string Describe(DungeonRoomStyle style)
    {
        var users = Users(style);
        if (users.Count == 0) return "Not used by any level";
        string Name((DungeonBiome biome, TableLevelData level, string use) u) => u.biome.isLevelBase && u.level != null ? u.level.name : u.biome.name;
        var parts = new List<string>();
        foreach (var use in new[] { "default", "rooms", "corridors" })
        {
            var names = users.Where(u => u.use == use).Select(Name).Distinct().ToArray();
            if (names.Length > 0) parts.Add((use == "default" ? "Default of " : use == "rooms" ? "Rooms in " : "Corridors in ") + string.Join(", ", names));
        }
        return string.Join(" · ", parts);
    }

    public static string KindOf(DungeonRoomStyle style) => Users(style).Any(u => u.use == "default") ? "Default style" : "Room style";

    // An empty slot on a style takes the default style of a level that uses it (Dungeon1's when none does).
    static DungeonRoomStyle Fallback(DungeonRoomStyle style)
    {
        foreach (var u in Users(style))
        {
            var level = u.level;
            if (level != null && level.defaultStyle != null && level.defaultStyle != style) return level.defaultStyle;
        }
        var dungeon1 = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        return dungeon1 != null && dungeon1.defaultStyle != style ? dungeon1.defaultStyle : null;
    }

    // The corner of a room, unsaved, for the preview to copy (the caller destroys it).
    public static GameObject Sample(DungeonRoomStyle style)
    {
        var fallback = Fallback(style);
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        float cell = level != null && level.cellSize > 0 ? level.cellSize : 2f, tile = level != null && level.architectureTileSize > 0 ? level.architectureTileSize : 3f;
        Material Pick(Material mine, Material theirs) => DungeonRoomStyle.Resolve(mine, theirs);
        var floor = Pick(style.floor, fallback != null ? fallback.floor : null);
        var bottom = Pick(style.bottomWall, fallback != null ? fallback.bottomWall : null);
        var upper = Pick(style.upperWall, Pick(fallback != null ? fallback.upperWall : null, bottom));
        var trim = Pick(style.trim, fallback != null ? fallback.trim : null);
        var random = new System.Random(7);

        var root = new GameObject(style.name + " sample") { hideFlags = HideFlags.HideAndDontSave };
        Vector3 Cell(int x, int z) => new Vector3((x - (Wide - 1) * .5f) * cell, 0, (z - (Deep - 1) * .5f) * cell);
        for (int x = 0; x < Wide; x++)
            for (int z = 0; z < Deep; z++)
                Box("Floor", Cell(x, z) - Vector3.up * .12f, new Vector3(cell, .24f, cell), Variant(floor, style.floorBaseWeight, style.floorVariants, 0, random), root.transform, tile);
        float height = Rows * tile;
        // The back wall (along x, at the far edge) and the left wall (along z, at the near-left edge); the back wall's
        // first piece reaches over the left wall's thickness to close the corner.
        for (int x = 0; x < Wide; x++)
        {
            var at = Cell(x, Deep - 1) + Vector3.forward * (cell * .5f);
            float extra = x == 0 ? WallThickness : 0;
            Wall(at + Vector3.left * extra * .5f, new Vector3(cell + extra, 0, 0), Vector3.forward);
        }
        // The left wall's trims stop just inside the back wall's, so the two never lie in one plane at the corner.
        for (int z = 0; z < Deep; z++)
            Wall(Cell(0, z) + Vector3.left * (cell * .5f), new Vector3(0, 0, cell), Vector3.left, z == Deep - 1 ? TrimDepth - .01f : 0);

        void Wall(Vector3 face, Vector3 along, Vector3 outward, float trimShort = 0)
        {
            float length = along.magnitude;
            Vector3 Size(float thick, float tall) => along.x != 0 ? new Vector3(length, tall, thick) : new Vector3(thick, tall, length);
            for (int row = 0; row < Rows; row++)
            {
                var basic = row == 0 ? bottom : upper;
                var material = Variant(basic, style.wallBaseWeight, style.wallVariants, row == 0 ? 1 : 2, random);
                Box("Wall", face + outward * (WallThickness * .5f) + Vector3.up * (row * tile + tile * .5f), Size(WallThickness, tile), material, root.transform, tile);
            }
            if (style.hideTrims || trim == null) return;
            // As the generator lays them (WallPiece): TrimDepth proud of the wall, the back TrimInset short of the wall's.
            float thick = WallThickness + TrimDepth - TrimInset;
            length -= trimShort;
            var centre = face + outward * ((WallThickness - TrimDepth - TrimInset) * .5f) - along.normalized * (trimShort * .5f);
            Box("Footing", centre + Vector3.up * .12f, Size(thick, .15f), trim, root.transform, tile, false);
            Box("Cornice", centre + Vector3.up * (height - .55f), Size(thick, .15f), trim, root.transform, tile, false);
        }
        return root;
    }

    // The generator's draw between the base material and the variants that suit the tile (rows: 0 floor, 1 the first
    // wall tile, 2 the tiles above).
    static Material Variant(Material basic, float baseWeight, DungeonSurfaceVariant[] variants, int rows, System.Random random)
    {
        if (variants == null || variants.Length == 0) return basic;
        float Weight(DungeonSurfaceVariant v) => v != null && v.material != null
            && !(rows == 1 && v.rows == DungeonVariantRows.UpperOnly) && !(rows == 2 && v.rows == DungeonVariantRows.BottomOnly) ? Mathf.Max(0, v.weight) : 0;
        float before = Mathf.Max(0, baseWeight), total = before + variants.Sum(Weight);
        if (total <= 0) return basic;
        float pick = (float)random.NextDouble() * total - before;
        if (pick < 0) return basic;
        foreach (var v in variants) { pick -= Weight(v); if (pick < 0) return v.material; }
        return basic;
    }

    // A box with the generator's architectural UVs: one texture repeat per architecture tile, in world units, so
    // neighbouring pieces carry on where the last one stopped. Trims (architectural off) keep the cube's own UVs.
    static readonly Dictionary<(Vector3, Vector3), Mesh> meshes = new();
    static void Box(string name, Vector3 at, Vector3 size, Material material, Transform parent, float tile, bool architectural = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.hideFlags = HideFlags.HideAndDontSave;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!architectural) return;
        var phase = new Vector3(Mathf.Repeat(at.x, tile), Mathf.Repeat(at.y, tile), Mathf.Repeat(at.z, tile));
        phase = new Vector3(Mathf.Round(phase.x * 10000) / 10000, Mathf.Round(phase.y * 10000) / 10000, Mathf.Round(phase.z * 10000) / 10000);
        if (!meshes.TryGetValue((size, phase), out var mesh) || mesh == null)
        {
            mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            var vertices = mesh.vertices; var normals = mesh.normals; var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = phase + Vector3.Scale(vertices[i], size);
                var n = normals[i];
                uv[i] = Mathf.Abs(n.y) > .5f ? new Vector2(point.x, point.z) / tile : Mathf.Abs(n.x) > .5f ? new Vector2(point.z, point.y) / tile : new Vector2(point.x, point.y) / tile;
            }
            mesh.uv = uv;
            meshes[(size, phase)] = mesh;
        }
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
