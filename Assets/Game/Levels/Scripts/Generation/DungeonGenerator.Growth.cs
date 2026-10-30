using System.Collections.Generic;
using UnityEngine;

// Grown layouts and the dressing added with them: room shapes, surface variants, wall
// decorations, corridor dressing, dead-end caches and rule-based furnishing.
public partial class DungeonGenerator
{
    System.Random decorRandom, propRandom;
    readonly HashSet<Vector2Int> furnished = new();
    readonly List<(Vector2Int cell, Vector2Int dir, int region)> wallFaces = new();

    int seed0;   // the floor's seed (furnishing groups derive their loot seeds from it)
    void BeginDressing(int seed)
    {
        decorRandom = new System.Random(unchecked(seed + 71993));
        propRandom = new System.Random(unchecked(seed + 104729));
        seed0 = seed;
        furnished.Clear(); wallFaces.Clear();
    }

    // ── Layout ───────────────────────────────────────────────────────

    // Roles, profiles, the merchant's room and shapes are known before placement: room 0 is the
    // entrance and the last room, grown off the furthest room, is the exit.
    void GrowLayout(TableLevelData level, int seed)
    {
        int count = Mathf.Max(2, Biome != null && Biome.roomCount > 0 && !Biome.isLevelBase ? Biome.roomCount : level.roomCount);
        var roles = new RoomRole[count];
        float encounter = EncounterChance;
        for (int i = 0; i < count; i++)
            roles[i] = i == 0 ? RoomRole.Entrance : i == count - 1 ? RoomRole.Exit : random.NextDouble() < encounter ? RoomRole.Combat : (RoomRole)random.Next(2, 5);
        var chosen = new DungeonRoomProfile[count];
        for (int i = 0; i < count; i++) chosen[i] = ChooseProfile(roles[i]);
        if (UsesStartingRoom) chosen[0] = null;
        pooledTemplates = PoolTemplates(roles, chosen, seed);
        // The shop's template, like any other, sets its room's shape.
        int merchant = ChooseMerchantRoom(roles, i => RequestedTemplate(i, roles[i], chosen[i]) != null);
        var shapeRandom = new System.Random(unchecked(seed + 88667));
        var shapes = new DungeonShape[count];
        for (int i = 0; i < count; i++) shapes[i] = ChooseShape(roles[i], chosen[i], shapeRandom, RequestedTemplate(i, roles[i], chosen[i], merchant));
        // A stage arena is kept at the edge in front of the Dungeon Master (DungeonGenerator.Stage).
        if (UsesStartingRoom) shapes[0] = FacingDungeonMaster(shapes[0]);
        var entranceAt = EntranceOrigin(shapes[0]);
        Layout = DungeonLayout.Grow(level.width, level.depth, shapes, seed, level.Growth, StageOrigin(shapes[count - 1]), entranceAt);
        // The shop (the tavern) is built whole or not at all. Grown late on a full grid it can come out as a small
        // fallback room, which drops most of its interior: the floor is grown again with the shop's room moved earlier
        // in the growing order (halfway back each time, then every earlier place), and failing that later, until it
        // stands whole. Floors where it fitted are unchanged.
        var shopFootprint = merchant > 0 && data.shopRoom != null && data.shopRoom.GetComponent<DungeonRoomTemplate>() is { HasFootprint: true } shop ? shop.Footprint() : null;
        if (shopFootprint != null && !ShopWhole(Layout, merchant, shopFootprint.FloorCount))
            foreach (int to in OtherPlaces(merchant, count))
            {
                var movedShapes = Moved(shapes, merchant, to);
                var again = DungeonLayout.Grow(level.width, level.depth, movedShapes, seed, level.Growth, StageOrigin(movedShapes[count - 1]), entranceAt);
                if (!ShopWhole(again, to, shopFootprint.FloorCount)) continue;
                Layout = again; shapes = movedShapes;
                roles = Moved(roles, merchant, to); chosen = Moved(chosen, merchant, to); pooledTemplates = Moved(pooledTemplates, merchant, to);
                merchant = to;
                break;
            }
        var placed = Layout.PlacedRequests;
        Roles = new RoomRole[placed.Length];
        profiles = new DungeonRoomProfile[placed.Length];
        var requestedPool = pooledTemplates;
        pooledTemplates = new GameObject[placed.Length];
        for (int i = 0; i < placed.Length; i++) { Roles[i] = roles[placed[i]]; profiles[i] = chosen[placed[i]]; pooledTemplates[i] = requestedPool[placed[i]]; }
        // A chunk whose footprint didn't fit was grown as a small fallback rectangle: most of its pieces would fall
        // outside, so that room is generated normally instead.
        for (int i = 0; i < placed.Length; i++)
        {
            var chunk = pooledTemplates[i] != null ? pooledTemplates[i].GetComponent<DungeonRoomTemplate>() : null;
            if (chunk != null && chunk.HasFootprint && Layout.RoomCells(i).Count < chunk.Footprint().FloorCount * .9f) pooledTemplates[i] = null;
        }
        MerchantRoom = System.Array.IndexOf(placed, merchant);
        if (shopFootprint != null && MerchantRoom >= 0 && !ShopWhole(Layout, merchant, shopFootprint.FloorCount)) MerchantRoom = -1;
        // A shop room that did not fit the grid: the merchant sets up in a placed quiet room with no interior of its
        // own instead (no shop template there, its shape wasn't made for it), so every floor that rolls one has one.
        shopInPlainRoom = false;
        if (merchant >= 0 && MerchantRoom < 0)
        {
            var quiet = new System.Collections.Generic.List<int>();
            for (int i = 1; i < placed.Length; i++)
                if ((Roles[i] == RoomRole.Rest || Roles[i] == RoomRole.Storage || Roles[i] == RoomRole.Treasure) && RequestedTemplate(i, Roles[i], profiles[i]) == null) quiet.Add(i);
            // No quiet room left: any plain room but the exit (the merchant's room gets no enemies).
            if (quiet.Count == 0)
                for (int i = 1; i < placed.Length; i++) if (Roles[i] != RoomRole.Exit && RequestedTemplate(i, Roles[i], profiles[i]) == null) quiet.Add(i);
            if (quiet.Count > 0)
            {
                MerchantRoom = quiet[new System.Random(unchecked(seed + 52361)).Next(quiet.Count)];
                shopInPlainRoom = true;
            }
        }
        // After the merchant has his room (a plain one when the shop didn't fit), so events never take the last quiet room.
        TopUpEventRooms(seed);
    }
    bool shopInPlainRoom;

    // Where to move the shop's request to: halfway back each time down to right after the entrance, then every other
    // earlier place, latest first, then the later ones (never the exit's). Right after the entrance it can still lose
    // out: a floor grown again for lost rooms puts a plain room before any with marked doors.
    static System.Collections.Generic.IEnumerable<int> OtherPlaces(int from, int count)
    {
        var tried = new System.Collections.Generic.HashSet<int> { from };
        for (int to = from / 2; to >= 1; to = to == 1 ? 0 : Mathf.Max(1, to / 2)) if (tried.Add(to)) yield return to;
        for (int to = from - 1; to >= 1; to--) if (tried.Add(to)) yield return to;
        for (int to = from + 1; to < count - 1; to++) if (tried.Add(to)) yield return to;
    }

    // The requests with the one at `from` moved to `to`, the ones between shifted one place toward where it was.
    static T[] Moved<T>(T[] items, int from, int to)
    {
        var moved = (T[])items.Clone();
        for (int i = from; i > to; i--) moved[i] = moved[i - 1];
        for (int i = from; i < to; i++) moved[i] = moved[i + 1];
        moved[to] = items[from];
        return moved;
    }

    // Whether requested room `request` was placed with (nearly) all of the shop's footprint, as the pooled chunks are checked.
    static bool ShopWhole(DungeonLayout layout, int request, int footprintCells)
    {
        int placedIndex = System.Array.IndexOf(layout.PlacedRequests, request);
        return placedIndex >= 0 && layout.RoomCells(placedIndex).Count >= footprintCells * .9f;
    }

    // The first floor of the run and of every new biome opens in the level's Starting room.
    // A stage with a look of its own (a forest under the cellars) opens in an ordinary entrance room instead.
    bool UsesStartingRoom => data.startingRoom != null && !Authored && !OwnLook
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

    // The merchant's room (the tavern) can ask for a door on every way in (DungeonRoomTemplate.wayInDoor): one door per
    // opening cell, as the ambush's gates, in place of any other door on those passages (the starting room's keeps its own).
    readonly System.Collections.Generic.Dictionary<(Vector2Int, Vector2Int), GameObject> doorPrefabs = new();
    void AddWayInDoors()
    {
        doorPrefabs.Clear();
        if (MerchantRoom <= 0) return;
        // The merchant in a plain room (the shop didn't fit) has no tavern, so no tavern door either.
        var prefab = TemplatePrefab(MerchantRoom);
        var template = prefab != null ? prefab.GetComponent<DungeonRoomTemplate>() : null;
        if (template == null || template.wayInDoor == null) return;
        foreach (var cell in Layout.RoomCells(MerchantRoom))
            foreach (var dir in DungeonLayout.Neighbours)
            {
                if (!Layout.IsOpening(MerchantRoom, cell, dir)) continue;
                var outside = cell + dir;
                int passage = Layout.RegionIds[outside.x, outside.y];
                // The passage's other doors go (one door a passage), the starting room's own excepted.
                doorways.RemoveAll(d =>
                {
                    var n = d.roomCell + d.direction;
                    return Layout.InBounds(n) && Layout.RegionIds[n.x, n.y] == passage && !Layout.InRoom(0, d.roomCell) && !Layout.InRoom(MerchantRoom, d.roomCell);
                });
                // A door the doorway already has goes too: two leaves stood in one doorway.
                doorways.RemoveAll(d => d.roomCell == cell && d.direction == dir);
                doorways.Add((cell, dir));
                doorPrefabs[(cell, dir)] = template.wayInDoor;
            }
    }

    // prefab: the room's template (RequestedTemplate), whose footprint or minimum size wins.
    DungeonShape ChooseShape(RoomRole role, DungeonRoomProfile profile, System.Random rng, GameObject prefab)
    {
        var template = prefab != null ? prefab.GetComponent<DungeonRoomTemplate>() : null;
        float scale = profile != null ? profile.sizeScale : 1;
        if (Biome != null && !Biome.isLevelBase) scale *= Mathf.Clamp(Biome.roomSizeScale, .5f, 2.5f);
        if (rng.NextDouble() * 100 < Mathf.Clamp(data.largeRoomPercent, 0, 30)) scale *= Mathf.Clamp(data.largeRoomScale, 1, 3);
        if (role == RoomRole.Exit && Milestone != null) scale = Mathf.Max(scale, Mathf.Clamp(Milestone.arenaSizeScale, 1, 3));
        int w = RollSize(rng, scale), h = RollSize(rng, scale);
        if (template != null)
        {
            if (template.HasFootprint) return template.FootprintShape(rng);
            return DungeonShape.Rectangle(Mathf.Max(w, template.minimumCells.x), Mathf.Max(h, template.minimumCells.y));
        }
        // A room profile's own shapes replace the rest; otherwise the shared ones, the base biome's and the stage's together.
        // A stage with a look of its own and shapes of its own uses only those (a forest's clearings are never boxes).
        var ownShapes = OwnLook && StageBiome.roomShapes != null && StageBiome.roomShapes.Length > 0 ? StageBiome.roomShapes : null;
        var choices = FirstList(profile != null ? profile.shapes : null, ownShapes, Shared(data.roomShapes, b => b.roomShapes));
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
    // The stage biome's lights, else the base biome's, else the ones every level shares.
    float TorchBrightness => Biome != null ? Biome.torchBrightness : 1f;
    DungeonWallLight[] WallLights
    {
        get
        {
            var stage = StageBiome;
            if (stage != null && DungeonWallLight.Any(stage.lightingPrefabs)) return stage.lightingPrefabs;
            var levelBase = data.baseBiome;
            if (levelBase != null && DungeonWallLight.Any(levelBase.lightingPrefabs)) return levelBase.lightingPrefabs;
            return data.lightingPrefabs;
        }
    }

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
                bool full = always || torchRandom.NextDouble() < data.litRoomChance;
                count = Mathf.Max(1, torchRandom.Next(data.torchesPerRoom.x, data.torchesPerRoom.y + 1));
                if (!full) count = 1;
                // A dark room (DungeonRoomFeatures) has none; its rolls are still taken so other rooms keep theirs.
                if (LightOf(region) == DungeonRoomLight.Dark) count = 0;
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
        LightUnlitRooms(torchRandom, parent);
    }

    // Every room has at least one light: a template that replaces the generated torches without lighting itself, or a
    // room whose walls were all furnished or beside openings, gets one on any wall it has; a dark room gets the level's
    // dim Dark Room Light on the floor by a wall instead of a torch.
    void LightUnlitRooms(System.Random rng, Transform parent)
    {
        if (Board) return;
        var lit = new HashSet<int>();
        foreach (var l in GetComponentsInChildren<Light>(true))
        {
            if (l.intensity <= 0) continue; // a chest's glow, dark until it opens
            int r = Layout.RoomAt(CellOf(l.transform.position));
            if (r >= 0) lit.Add(r);
        }
        for (int room = 0; room < Layout.rooms.Count; room++)
        {
            bool dark = LightOf(room) == DungeonRoomLight.Dark;
            if (lit.Contains(room) || (dark && data.darkRoomLight == null)) continue;
            (Vector2Int cell, Vector2Int dir)? best = null;
            int bestScore = -1;
            foreach (var (cell, dir, region) in wallFaces)
            {
                if (region != room) continue;
                int score = (NextToOpening(cell, region) ? 0 : 2) + (furnished.Contains(cell) ? 0 : 1);
                if (score > bestScore || (score == bestScore && rng.Next(3) == 0)) { best = (cell, dir); bestScore = score; }
                if (bestScore == 3 && rng.Next(4) == 0) break;
            }
            if (!best.HasValue) continue;
            if (dark) SpawnDarkRoomLight(best.Value.cell, best.Value.dir, parent);
            else SpawnTorch(best.Value.cell, best.Value.dir, room, parent, rng);
        }
    }

    // The dark room's one light: on the floor against the wall, facing into the room, dimmed.
    void SpawnDarkRoomLight(Vector2Int cell, Vector2Int dir, Transform parent)
    {
        var outward = new Vector3(dir.x, 0, dir.y);
        var at = Cell(cell) + outward * Mathf.Max(0f, data.cellSize * .5f - .45f);
        var go = Instantiate(data.darkRoomLight, at, Quaternion.LookRotation(-outward), parent);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) { l.intensity *= data.darkRoomLightStrength; l.range *= Mathf.Lerp(.6f, 1f, data.darkRoomLightStrength); }
        foreach (var flicker in go.GetComponentsInChildren<TorchFlicker>(true)) flicker.Rebase();
        furnished.Add(cell);
    }

    // Colour, most specific first: the socket, the room template, the room profile, the light entry, the biome's torch colour.
    void SpawnTorch(Vector2Int cell, Vector2Int dir, int room, Transform parent, System.Random rng, DungeonSocket socket = null)
    {
        var entry = socket != null && socket.overridePrefab != null ? null : DungeonWallLight.Choose(WallLights, rng);
        var prefab = entry != null ? entry.prefab : socket != null ? socket.overridePrefab : null;
        if (prefab == null) return;
        Color? color = entry?.Tint ?? (Biome != null ? Biome.TorchColor : null);
        var profile = room >= 0 ? profiles[room] : null;
        var template = room >= 0 ? templates[room] : null;
        if (profile != null && profile.overrideLight) color = profile.lightColor;
        if (template != null && template.overrideLightColor) color = template.lightColor;
        if (socket != null && socket.overrideLightColor) color = socket.lightColor;
        // The prefab carries its own mounting height; its origin sits on the floor at the wall face.
        var torch = Instantiate(prefab, Cell(cell) + WallLightOffset(dir, data.cellSize), Quaternion.LookRotation(-new Vector3(dir.x, 0, dir.y)), parent);
        DungeonWallLight.Apply(torch, color, TorchBrightness);
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
                // A full-height piece (PropMount: a pilaster, a long banner) is made for one tile: stacked up a taller room's wall.
                if (PropMount.KindOf(prefab) == PropMount.Kind.FullHeight)
                    for (float up = data.architectureTileSize; up < HeightAt(cell.x, cell.y) - .5f; up += data.architectureTileSize)
                        Instantiate(prefab, Cell(cell) + outward * (data.cellSize * .5f) + Vector3.up * up, Quaternion.LookRotation(-outward), geometry);
                // Taken: a torch or a wall prop placed after it would stand inside the piece (torches inside carved pilasters).
                furnished.Add(cell);
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
        // Base biome (the level's) and stage biome together: furnishing rules when either has any, otherwise plain props.
        var stage = StageBiome;
        var rules = Both(OwnLook ? null : data.propRules, stage != null ? stage.propRules : null);
        if (rules != null && rules.Length > 0) return (rules, null);
        return (null, Shared(data.roomPropPrefabs, b => b.props));
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
        var placed = Instantiate(rule.prefab, pos, rotation, geometry);
        BackToWall(placed, rule.placement, size);
        // What stands against a wall takes the wall faces of the cells it covers: wall pieces and torches, placed after,
        // keep off them (a shelf stood inside a pilaster).
        if (rule.placement == DungeonPropPlacement.AgainstWall || rule.placement == DungeonPropPlacement.Corner)
        {
            var renderers = placed.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                Vector2Int a = CellOf(bounds.min + new Vector3(.05f, 0f, .05f)), b = CellOf(bounds.max - new Vector3(.05f, 0f, .05f));
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    for (int z = Mathf.Min(a.y, b.y); z <= Mathf.Max(a.y, b.y); z++) furnished.Add(new Vector2Int(x, z));
            }
        }
        // Hung from the ceiling (PropMount; made for a one-tile room): up with a taller room's ceiling.
        if (PropMount.KindOf(rule.prefab) == PropMount.Kind.HangsFromCeiling && RoomHeightTiles != null && index >= 0 && index < RoomHeightTiles.Length)
            placed.transform.position += Vector3.up * (RoomHeightTiles[index] - 1) * data.architectureTileSize;
        // Furnishing groups may hold breakables (a barrel under candles, pots on a shelf): give them loot.
        if (placed.GetComponentInChildren<DungeonDestructible>(true) != null) SeedGroup(placed, unchecked(pos.GetHashCode() ^ seed0));
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
