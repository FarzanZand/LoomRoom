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

    // Roles, profiles, the merchant's room and shapes are known before placement: room 0 is the
    // entrance and the last room, grown off the furthest room, is the exit.
    void GrowLayout(TableLevelData level, int seed)
    {
        int count = Mathf.Max(2, level.roomCount);
        var roles = new RoomRole[count];
        float encounter = EncounterChance;
        for (int i = 0; i < count; i++)
            roles[i] = i == 0 ? RoomRole.Entrance : i == count - 1 ? RoomRole.Exit : random.NextDouble() < encounter ? RoomRole.Combat : (RoomRole)random.Next(2, 5);
        var chosen = new DungeonRoomProfile[count];
        for (int i = 0; i < count; i++) chosen[i] = ChooseProfile(roles[i]);
        if (UsesStartingRoom) chosen[0] = null;
        // The shop's template, like any other, sets its room's shape.
        int merchant = ChooseMerchantRoom(roles, i => RequestedTemplate(i, roles[i], chosen[i]) != null);
        var shapeRandom = new System.Random(unchecked(seed + 88667));
        var shapes = new DungeonShape[count];
        for (int i = 0; i < count; i++) shapes[i] = ChooseShape(roles[i], chosen[i], shapeRandom, RequestedTemplate(i, roles[i], chosen[i], merchant));
        Layout = DungeonLayout.Grow(level.width, level.depth, shapes, seed, level.Growth);
        var placed = Layout.PlacedRequests;
        Roles = new RoomRole[placed.Length];
        profiles = new DungeonRoomProfile[placed.Length];
        for (int i = 0; i < placed.Length; i++) { Roles[i] = roles[placed[i]]; profiles[i] = chosen[placed[i]]; }
        // A shop room that did not fit leaves the floor without a merchant.
        MerchantRoom = System.Array.IndexOf(placed, merchant);
    }

    // The first floor of the run and of every new biome opens in the level's Starting room.
    bool UsesStartingRoom => data.startingRoom != null && !Authored
        && (FloorNumber == 1 || data.Biome(FloorNumber) != data.Biome(FloorNumber - 1));

    // The starting room's only way out gets the level's door, replacing any other door on that passage.
    void AddStartingRoomDoor()
    {
        if (!UsesStartingRoom || data.doorPrefab == null) return;
        foreach (var cell in Layout.RoomCells(0))
            foreach (var dir in DungeonLayout.Neighbours)
            {
                var outside = cell + dir;
                if (!Layout.InBounds(outside) || !Layout.floor[outside.x, outside.y] || Layout.InRoom(0, outside)) continue;
                int passage = Layout.RegionIds[outside.x, outside.y];
                doorways.RemoveAll(d => { var n = d.roomCell + d.direction; return Layout.InBounds(n) && Layout.RegionIds[n.x, n.y] == passage; });
                doorways.Add((cell, dir));
                return;
            }
    }

    // prefab: the room's template (RequestedTemplate), whose footprint or minimum size wins.
    DungeonShape ChooseShape(RoomRole role, DungeonRoomProfile profile, System.Random rng, GameObject prefab)
    {
        var template = prefab != null ? prefab.GetComponent<DungeonRoomTemplate>() : null;
        float scale = profile != null ? profile.sizeScale : 1;
        if (rng.NextDouble() * 100 < Mathf.Clamp(data.largeRoomPercent, 0, 30)) scale *= Mathf.Clamp(data.largeRoomScale, 1, 3);
        if (role == RoomRole.Exit && Milestone != null) scale = Mathf.Max(scale, Mathf.Clamp(Milestone.arenaSizeScale, 1, 3));
        int w = RollSize(rng, scale), h = RollSize(rng, scale);
        if (template != null)
        {
            if (template.HasFootprint) return template.FootprintShape(rng);
            return DungeonShape.Rectangle(Mathf.Max(w, template.minimumCells.x), Mathf.Max(h, template.minimumCells.y));
        }
        // A room profile's own shapes replace the rest; otherwise the level's and the biome's together.
        var choices = FirstList(profile != null ? profile.shapes : null, Both(data.roomShapes, Biome != null ? Biome.roomShapes : null));
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

    // A variant's weight for a tile: rowFilter 0 for a floor cell, 1 for a bottom wall tile, 2 for an upper one.
    static float VariantWeight(DungeonSurfaceVariant v, int rowFilter) => v != null && v.material != null
        && !(rowFilter == 1 && v.rows == DungeonVariantRows.UpperOnly) && !(rowFilter == 2 && v.rows == DungeonVariantRows.BottomOnly) ? v.weight : 0;
    static readonly System.Func<DungeonSurfaceVariant, float>[] VariantWeights = { v => VariantWeight(v, 0), v => VariantWeight(v, 1), v => VariantWeight(v, 2) };

    // Weighted draw between the base material and the variants that suit this tile.
    static Material Pick(Material basic, float baseWeight, DungeonSurfaceVariant[] variants, float roll, int rowFilter)
    {
        if (variants == null || variants.Length == 0) return basic;
        var weight = VariantWeights[rowFilter];
        float before = Mathf.Max(0, baseWeight);
        float total = WeightedPick.TotalSingle(variants, weight, before);
        if (total <= 0) return basic;
        float pick = roll * total - before;
        if (pick < 0) return basic;
        int index = WeightedPick.LandSingle(variants, weight, pick);
        return index >= 0 ? variants[index].material : basic;
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

    // Wall torches: a few per lit room, spread apart, and the odd one along corridors.
    // The floor's biome lights, or the level's when the biome lists none.
    DungeonWallLight[] WallLights => Biome != null && DungeonWallLight.Any(Biome.lightingPrefabs) ? Biome.lightingPrefabs : data.lightingPrefabs;

    void PlaceTorches()
    {
        if (!DungeonWallLight.Any(WallLights)) return;
        var torchRandom = new System.Random(Scramble(unchecked(Layout.seed + 30011)));
        var parent = TorchParent;
        var byRegion = new Dictionary<int, List<(Vector2Int cell, Vector2Int dir)>>();
        foreach (var (cell, dir, region) in wallFaces)
        {
            if (region < Layout.rooms.Count && templates[region] != null && templates[region].replaceGeneratedTorches) continue;
            if (furnished.Contains(cell) || NextToOpening(cell, region)) continue;
            if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = new();
            list.Add((cell, dir));
        }
        foreach (var pair in byRegion)
        {
            int region = pair.Key; var faces = pair.Value;
            bool room = region < Layout.rooms.Count;
            for (int i = faces.Count - 1; i > 0; i--) { int j = torchRandom.Next(i + 1); (faces[i], faces[j]) = (faces[j], faces[i]); }
            int count;
            if (room)
            {
                bool always = Roles[region] == RoomRole.Entrance || Roles[region] == RoomRole.Exit;
                if (!always && torchRandom.NextDouble() >= data.litRoomChance) continue;
                count = torchRandom.Next(data.torchesPerRoom.x, data.torchesPerRoom.y + 1);
                if (always) count = Mathf.Max(1, count);
            }
            else
            {
                count = 0;
                for (int n = faces.Count / 8 + 1; n > 0; n--) if (torchRandom.NextDouble() < data.corridorTorchChance) count++;
            }
            var placed = new List<Vector2Int>();
            foreach (var (cell, dir) in faces)
            {
                if (placed.Count >= count) break;
                bool crowded = false;
                foreach (var p in placed) if ((p - cell).sqrMagnitude < 16) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(cell);
                SpawnTorch(cell, dir, room ? region : -1, parent, torchRandom);
            }
        }
    }

    // Colour, most specific first: the socket, the room template, the room profile, the light entry.
    void SpawnTorch(Vector2Int cell, Vector2Int dir, int room, Transform parent, System.Random rng, DungeonSocket socket = null)
    {
        var entry = socket != null && socket.overridePrefab != null ? null : DungeonWallLight.Choose(WallLights, rng);
        var prefab = entry != null ? entry.prefab : socket != null ? socket.overridePrefab : null;
        if (prefab == null) return;
        Color? color = entry?.Tint;
        var profile = room >= 0 ? profiles[room] : null;
        var template = room >= 0 ? templates[room] : null;
        if (profile != null && profile.overrideLight) color = profile.lightColor;
        if (template != null && template.overrideLightColor) color = template.lightColor;
        if (socket != null && socket.overrideLightColor) color = socket.lightColor;
        // The prefab carries its own mounting height; its origin sits on the floor at the wall face.
        var torch = Instantiate(prefab, Cell(cell) + WallLightOffset(dir, data.cellSize), Quaternion.LookRotation(-new Vector3(dir.x, 0, dir.y)), parent);
        if (color.HasValue) DungeonWallLight.Apply(torch, color.Value);
        furnished.Add(cell);
    }

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
                Instantiate(prefab, Cell(cell) + outward * (data.cellSize * .5f), Quaternion.LookRotation(-outward), geometry);
                count--;
            }
        }
    }

    // Beside a doorway: decorations there would clip the door frame.
    bool NextToOpening(Vector2Int cell, int region)
    {
        foreach (var d in DungeonLayout.Neighbours)
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
            if (roll < .15) MakeContainer(Cell(cell), true, null, facing);
            else if (roll < .65)
            {
                var prefab = DungeonWeightedPrefab.Choose(Destructibles, decorRandom, IsFloorBreakable);
                if (prefab != null) SpawnDestructible(prefab, Cell(cell), Quaternion.Euler(0, decorRandom.Next(4) * 90, 0), null, decorRandom.Next());
            }
        }
    }

    // ── Furnishing ───────────────────────────────────────────────────

    // The most specific level (the room profile replaces; level and biome combine) that has rules or plain props wins.
    (DungeonPropRule[] rules, GameObject[] props) Furnishing(DungeonRoomProfile profile)
    {
        if (profile != null && profile.propRules != null && profile.propRules.Length > 0) return (profile.propRules, null);
        if (profile != null && profile.props != null && profile.props.Length > 0) return (null, profile.props);
        // Level and biome together: furnishing rules when either has any, otherwise plain props.
        var rules = Both(data.propRules, Biome != null ? Biome.propRules : null);
        if (rules != null && rules.Length > 0) return (rules, null);
        return (null, Both(data.roomPropPrefabs, Biome != null ? Biome.props : null));
    }

    void PlaceRules(int index, List<Vector2Int> free, DungeonPropRule[] rules)
    {
        var open = new HashSet<Vector2Int>(free);
        open.ExceptWith(furnished);
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
        if (!FindSpot(rule.placement, size, index, order, open, out var pos, out var rotation)) return false;
        BackToWall(Instantiate(rule.prefab, pos, rotation, geometry), rule.placement, size);
        return true;
    }

    // Against Wall and Corner: slide the object back until its rear touches the wall behind its
    // footprint, whatever the prefab's pivot.
    void BackToWall(GameObject go, DungeonPropPlacement placement, Vector2Int size)
    {
        if (go == null || (placement != DungeonPropPlacement.AgainstWall && placement != DungeonPropPlacement.Corner)) return;
        var forward = go.transform.forward;
        float rear = float.MaxValue;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            var b = r.bounds;
            float along = Vector3.Dot(b.center - go.transform.position, forward);
            float half = Mathf.Abs(forward.x) * b.extents.x + Mathf.Abs(forward.z) * b.extents.z;
            rear = Mathf.Min(rear, along - half);
        }
        if (rear == float.MaxValue) return;
        float wall = -data.cellSize * size.y * .5f;   // wall face behind the footprint's back row
        go.transform.position += forward * (wall + .03f - rear);
    }

    // First free spot for a footprint with the given placement; claims its cells.
    bool FindSpot(DungeonPropPlacement placement, Vector2Int size, int index, List<Vector2Int> order, HashSet<Vector2Int> open, out Vector3 pos, out Quaternion rotation)
    {
        pos = default; rotation = Quaternion.identity;
        if (order.Count == 0) return false;
        int start = propRandom.Next(order.Count);
        for (int k = 0; k < order.Count; k++)
        {
            var anchor = order[(start + k) % order.Count];
            if (!open.Contains(anchor)) continue;
            int turn = propRandom.Next(4);
            for (int t = 0; t < 4; t++)
            {
                var forward = Sides[(turn + t) & 3];
                if (!Fits(placement, size, index, anchor, forward, open)) continue;
                var right = new Vector2Int(forward.y, -forward.x);
                var offset = ((Vector2)right * (size.x - 1) + (Vector2)forward * (size.y - 1)) * .5f;
                pos = Cell(anchor) + new Vector3(offset.x, 0, offset.y) * data.cellSize;
                rotation = Quaternion.LookRotation(new Vector3(forward.x, 0, forward.y));
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
