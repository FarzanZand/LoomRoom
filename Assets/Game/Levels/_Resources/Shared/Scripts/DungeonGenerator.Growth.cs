using System.Collections.Generic;
using UnityEngine;

// Grown layouts and the dressing added with them: room shapes, surface variants, wall
// decorations, corridor dressing, dead-end caches and rule-based furnishing.
public partial class DungeonGenerator
{
    System.Random decorRandom, propRandom;
    readonly HashSet<Vector2Int> furnished = new();
    readonly List<(Vector2Int cell, Vector2Int dir, int region)> wallFaces = new();

    void BeginDressing(int seed)
    {
        decorRandom = new System.Random(unchecked(seed + 71993));
        propRandom = new System.Random(unchecked(seed + 104729));
        furnished.Clear(); wallFaces.Clear();
    }

    // ── Layout ───────────────────────────────────────────────────────

    // Roles and shapes are known before placement: room 0 is the entrance and the last room,
    // grown off the furthest room, is the exit.
    void GrowLayout(TableLevelData level, int seed)
    {
        int count = Mathf.Max(2, level.roomCount);
        var roles = new RoomRole[count];
        float encounter = Settings != null && Settings.overrideEncounters ? Settings.encounterChance : level.encounterChance;
        for (int i = 0; i < count; i++)
            roles[i] = i == 0 ? RoomRole.Entrance : i == count - 1 ? RoomRole.Exit : random.NextDouble() < encounter ? RoomRole.Combat : (RoomRole)random.Next(2, 5);
        var chosen = new DungeonRoomProfile[count];
        for (int i = 0; i < count; i++) chosen[i] = ChooseProfile(roles[i]);
        var shapeRandom = new System.Random(unchecked(seed + 88667));
        var shapes = new DungeonShape[count];
        for (int i = 0; i < count; i++) shapes[i] = ChooseShape(roles[i], chosen[i], shapeRandom);
        Layout = DungeonLayout.Grow(level.width, level.depth, shapes, seed, level.Growth);
        var placed = Layout.PlacedRequests;
        Roles = new RoomRole[placed.Length];
        profiles = new DungeonRoomProfile[placed.Length];
        for (int i = 0; i < placed.Length; i++) { Roles[i] = roles[placed[i]]; profiles[i] = chosen[placed[i]]; }
    }

    DungeonShape ChooseShape(RoomRole role, DungeonRoomProfile profile, System.Random rng)
    {
        var prefab = role == RoomRole.Exit && Milestone != null && Milestone.arena != null ? Milestone.arena : profile?.roomTemplate;
        var template = prefab != null ? prefab.GetComponent<DungeonRoomTemplate>() : null;
        float scale = profile != null ? profile.sizeScale : 1;
        if (rng.NextDouble() * 100 < Mathf.Clamp(data.largeRoomPercent, 0, 30)) scale *= Mathf.Clamp(data.largeRoomScale, 1, 3);
        if (role == RoomRole.Exit && Milestone != null) scale = Mathf.Max(scale, Mathf.Clamp(Milestone.arenaSizeScale, 1, 3));
        int w = RollSize(rng, scale), h = RollSize(rng, scale);
        if (template != null)
        {
            if (template.footprint != null) return template.footprint.ToShape(rng, false);
            return DungeonShape.Rectangle(Mathf.Max(w, template.minimumCells.x), Mathf.Max(h, template.minimumCells.y));
        }
        var choices = FirstList(profile?.shapes, Theme?.roomShapes, data.roomShapes);
        // Stairs stand beside the centre of the entrance and exit, so those need open floor there.
        bool needsOpenCentre = role == RoomRole.Entrance || role == RoomRole.Exit;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var choice = DungeonShapeChoice.Choose(choices, rng);
            var shape = choice == null ? DungeonShape.Create(DungeonShapeKind.Rectangle, w, h, rng)
                : choice.kind == DungeonShapeKind.Authored ? choice.shape.ToShape(rng, true)
                : DungeonShape.Create(choice.kind, w, h, rng);
            if (!needsOpenCentre || shape.OpenAroundCentre()) return shape;
        }
        return DungeonShape.Rectangle(w, h);
    }

    int RollSize(System.Random rng, float scale)
    {
        int min = Mathf.Clamp(data.roomSize.x, 3, 16), max = Mathf.Clamp(data.roomSize.y, min, 16);
        int fit = Mathf.Max(3, Mathf.Min(data.width, data.depth) / 2);
        return Mathf.Clamp(Mathf.RoundToInt(rng.Next(min, max + 1) * scale), 3, fit);
    }

    // ── Surfaces ─────────────────────────────────────────────────────

    // A stable per-tile draw: the same seed and tile always give the same material.
    float TileRoll(int x, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)Layout.seed * 2654435761u ^ (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)salt * 83492791u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // Weighted draw between the base material and the variants that suit this tile.
    static Material Pick(Material basic, float baseWeight, DungeonSurfaceVariant[] variants, float roll, int rowFilter)
    {
        if (variants == null || variants.Length == 0) return basic;
        bool Eligible(DungeonSurfaceVariant v) => v != null && v.material != null && v.weight > 0
            && !(rowFilter == 1 && v.rows == DungeonVariantRows.UpperOnly) && !(rowFilter == 2 && v.rows == DungeonVariantRows.BottomOnly);
        float total = Mathf.Max(0, baseWeight);
        foreach (var v in variants) if (Eligible(v)) total += v.weight;
        if (total <= 0) return basic;
        float pick = roll * total - Mathf.Max(0, baseWeight);
        if (pick < 0) return basic;
        foreach (var v in variants)
        {
            if (!Eligible(v)) continue;
            pick -= v.weight;
            if (pick < 0) return v.material;
        }
        return basic;
    }

    Material FloorMaterial(int region, int x, int z)
    {
        var style = RegionStyles[region];
        var basic = DungeonRoomStyle.Resolve(style != null ? style.floor : null, data.floorMaterial);
        return style == null ? basic : Pick(basic, style.floorBaseWeight, style.floorVariants, TileRoll(x, z, 1), 0);
    }

    Material WallSurface(int region, float bottom, int x, int z, Vector2Int dir)
    {
        var basic = WallMaterial(region, bottom);
        var style = RegionStyles[region];
        if (style == null) return basic;
        int row = Mathf.RoundToInt(bottom / data.architectureTileSize);
        bool first = bottom < data.architectureTileSize - .001f;
        // Each face of a cell is its own tile.
        int face = dir.x > 0 ? 0 : dir.x < 0 ? 1 : dir.y > 0 ? 2 : 3;
        return Pick(basic, style.wallBaseWeight, style.wallVariants, TileRoll(x, z, 7 + face * 5 + row * 31), first ? 1 : 2);
    }

    // ── Walls and corridors ──────────────────────────────────────────

    bool Open(Vector2Int p) => Layout.InBounds(p) && Layout.floor[p.x, p.y];

    void DecorateWalls()
    {
        var byRegion = new Dictionary<int, List<(Vector2Int cell, Vector2Int dir)>>();
        foreach (var (cell, dir, region) in wallFaces)
        {
            var style = RegionStyles[region];
            if (style == null || style.wallDecor == null || style.wallDecor.Length == 0 || style.wallDecorPerTenFaces <= 0) continue;
            if (furnished.Contains(cell) || NextToOpening(cell, region)) continue;
            if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = new();
            list.Add((cell, dir));
        }
        foreach (var pair in byRegion)
        {
            var style = RegionStyles[pair.Key];
            var faces = pair.Value;
            for (int i = faces.Count - 1; i > 0; i--) { int j = decorRandom.Next(i + 1); (faces[i], faces[j]) = (faces[j], faces[i]); }
            int count = Mathf.RoundToInt(faces.Count * style.wallDecorPerTenFaces / 10f);
            var used = new HashSet<Vector2Int>();
            foreach (var (cell, dir) in faces)
            {
                if (count <= 0) break;
                if (!used.Add(cell)) continue;
                var prefab = DungeonWeightedPrefab.Choose(style.wallDecor, decorRandom);
                if (prefab == null) break;
                var outward = new Vector3(dir.x, 0, dir.y);
                Instantiate(prefab, Cell(cell) + outward * (data.cellSize * .5f - .11f), Quaternion.LookRotation(-outward), geometry);
                count--;
            }
        }
    }

    // Beside a doorway: decorations there would clip the door frame.
    bool NextToOpening(Vector2Int cell, int region)
    {
        foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
        {
            var n = cell + d;
            if (Open(n) && Layout.RegionIds[n.x, n.y] != region) return true;
        }
        return false;
    }

    void DressCorridors()
    {
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            if (!Layout.floor[x, z]) continue;
            int region = Layout.RegionIds[x, z];
            if (region < Layout.rooms.Count) continue;
            var style = RegionStyles[region];
            if (style == null || style.corridorDressing == null) continue;
            var p = new Vector2Int(x, z);
            bool alongZ = !Open(p + Vector2Int.left) && !Open(p + Vector2Int.right) && (Open(p + Vector2Int.up) || Open(p + Vector2Int.down));
            bool alongX = !Open(p + Vector2Int.up) && !Open(p + Vector2Int.down) && (Open(p + Vector2Int.left) || Open(p + Vector2Int.right));
            if (!alongZ && !alongX) continue;
            int spacing = Mathf.Max(2, style.dressingSpacing);
            if ((alongZ ? z : x) % spacing != (region * 7) % spacing) continue;
            var go = Instantiate(style.corridorDressing, Cell(p), Quaternion.LookRotation(alongZ ? Vector3.forward : Vector3.right), geometry);
            if (style.stretchDressingToCeiling)
            {
                var s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x, s.y * HeightAt(x, z) / data.architectureTileSize, s.z);
            }
        }
    }

    // A breakable, sometimes a chest, at the end of spurs.
    void StockDeadEnds()
    {
        foreach (var (cell, dir) in Layout.DeadEnds)
        {
            double roll = decorRandom.NextDouble();
            var facing = Quaternion.LookRotation(new Vector3(-dir.x, 0, -dir.y));
            if (roll < .15) MakeContainer(Cell(cell), true, false, null, facing);
            else if (roll < .65)
            {
                var prefab = DungeonWeightedPrefab.Choose(Destructibles, decorRandom, IsFloorBreakable);
                if (prefab != null) SpawnDestructible(prefab, Cell(cell), Quaternion.Euler(0, decorRandom.Next(4) * 90, 0), null, null, decorRandom.Next());
            }
        }
    }

    // ── Furnishing ───────────────────────────────────────────────────

    // The most specific level (profile, theme, level) that has rules or plain props wins.
    (DungeonPropRule[] rules, GameObject[] props) Furnishing(DungeonRoomProfile profile)
    {
        if (profile != null && profile.propRules != null && profile.propRules.Length > 0) return (profile.propRules, null);
        if (profile != null && profile.props != null && profile.props.Length > 0) return (null, profile.props);
        if (Theme != null && Theme.propRules != null && Theme.propRules.Length > 0) return (Theme.propRules, null);
        if (Theme != null && Theme.props != null && Theme.props.Length > 0) return (null, Theme.props);
        if (data.propRules != null && data.propRules.Length > 0) return (data.propRules, null);
        return (null, data.roomPropPrefabs);
    }

    void PlaceRules(int index, List<Vector2Int> free, DungeonPropRule[] rules)
    {
        var open = new HashSet<Vector2Int>(free);
        var role = Mask(Roles[index]);
        int area = Layout.RoomCells(index).Count;
        foreach (var rule in rules)
        {
            if (rule == null || rule.prefab == null || (rule.rooms & role) == 0) continue;
            if (propRandom.NextDouble() >= rule.chance) continue;
            float expected = area * rule.perTenCells / 10f * (float)(.75 + propRandom.NextDouble() * .5);
            int count = Mathf.Clamp(Mathf.RoundToInt(expected), rule.perRoom.x, Mathf.Max(rule.perRoom.x, rule.perRoom.y));
            for (int i = 0; i < count; i++) if (!PlaceProp(rule, index, free, open)) break;
        }
    }

    static readonly Vector2Int[] Sides = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    bool PlaceProp(DungeonPropRule rule, int index, List<Vector2Int> order, HashSet<Vector2Int> open)
    {
        var size = new Vector2Int(Mathf.Clamp(rule.footprint.x, 1, 6), Mathf.Clamp(rule.footprint.y, 1, 6));
        int start = propRandom.Next(order.Count);
        for (int k = 0; k < order.Count; k++)
        {
            var anchor = order[(start + k) % order.Count];
            if (!open.Contains(anchor)) continue;
            int turn = propRandom.Next(4);
            for (int t = 0; t < 4; t++)
            {
                var forward = Sides[(turn + t) & 3];
                if (!Fits(rule.placement, size, index, anchor, forward, open)) continue;
                var right = new Vector2Int(forward.y, -forward.x);
                var offset = ((Vector2)right * (size.x - 1) + (Vector2)forward * (size.y - 1)) * .5f;
                var pos = Cell(anchor) + new Vector3(offset.x, 0, offset.y) * data.cellSize;
                Instantiate(rule.prefab, pos, Quaternion.LookRotation(new Vector3(forward.x, 0, forward.y)), geometry);
                for (int i = 0; i < size.x; i++) for (int j = 0; j < size.y; j++)
                {
                    var c = anchor + right * i + forward * j;
                    open.Remove(c); furnished.Add(c);
                }
                return true;
            }
        }
        return false;
    }

    bool Fits(DungeonPropPlacement placement, Vector2Int size, int index, Vector2Int anchor, Vector2Int forward, HashSet<Vector2Int> open)
    {
        var right = new Vector2Int(forward.y, -forward.x);
        for (int i = 0; i < size.x; i++) for (int j = 0; j < size.y; j++)
        {
            var c = anchor + right * i + forward * j;
            if (!open.Contains(c) || !Layout.InRoom(index, c)) return false;
            switch (placement)
            {
                case DungeonPropPlacement.AgainstWall:
                    if (j == 0 && Open(c - forward)) return false;
                    break;
                case DungeonPropPlacement.Corner:
                    if (j == 0 && Open(c - forward)) return false;
                    // One end of the back row must also meet a side wall.
                    if (j == 0 && i == 0 && Open(c - right) && Open(anchor + right * size.x)) return false;
                    break;
                case DungeonPropPlacement.Centre:
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                        if (!Open(c + new Vector2Int(dx, dy))) return false;
                    break;
            }
        }
        return true;
    }
}
