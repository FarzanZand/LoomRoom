using System;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public partial class DungeonGenerator : MonoBehaviour
{
    public DungeonLayout Layout { get; private set; }
    public Vector3 SpawnPoint { get; private set; }
    public Quaternion SpawnRotation { get; private set; }=Quaternion.identity;
    public Vector3 ExitPoint { get; private set; }
    public NavMeshSurface Surface { get; private set; }
    NavMeshData navMeshData;
    TableLevelData data;
    public TableLevelData LevelData => data;
    Transform geometry, ceiling;
    readonly System.Collections.Generic.Dictionary<(Vector3, Vector3), Mesh> architectureMeshes = new();
    readonly System.Collections.Generic.List<Mesh> cornerMeshes = new();
    System.Random random;
    public int FloorNumber { get; private set; }=1;
    // Serialized in DungeonRoomProfile.role; Build casts random.Next(2,5) to Treasure..Storage.
    public enum RoomRole { Entrance = 0, Combat = 1, Treasure = 2, Rest = 3, Storage = 4, Exit = 5 }
    public RoomRole[] Roles { get; private set; }
    public int[] RoomHeightTiles { get; private set; }
    public int[] CorridorHeightTiles { get; private set; }
    public DungeonRoomStyle[] RegionStyles { get; private set; }
    System.Collections.Generic.List<(Vector2Int roomCell, Vector2Int direction)> doorways;
    Material WallMaterial(int region, float bottom)
    {
        var fallback = bottom < data.architectureTileSize - .001f ? data.wallMaterial : DungeonRoomStyle.Resolve(data.upperWallMaterial, data.wallMaterial);
        return RegionStyles[region] != null ? RegionStyles[region].Wall(bottom, data.architectureTileSize, fallback) : fallback;
    }
    float HeightAt(int x, int z) => data.architectureTileSize * (Layout.RegionIds[x,z] < RoomHeightTiles.Length
        ? RoomHeightTiles[Layout.RegionIds[x,z]] : CorridorHeightTiles[Layout.RegionIds[x,z] - RoomHeightTiles.Length]);

    public static int[] SelectRoomHeights(int count, int seed, float twoPercent, float threePercent)
    {
        var heights = new int[count];
        var order = new int[count];
        for (int i = 0; i < count; i++) { heights[i] = 1; order[i] = i; }
        var rng = new System.Random(unchecked(seed + 7919));
        for (int i = count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        float two = Mathf.Clamp(twoPercent, 0, 100), three = Mathf.Clamp(threePercent, 0, 100);
        float total = Mathf.Max(100, two + three);
        int threes = Mathf.RoundToInt(count * three / total);
        int twos = Mathf.Min(count - threes, Mathf.RoundToInt(count * two / total));
        for (int i = 0; i < threes; i++) heights[order[i]] = 3;
        for (int i = threes; i < threes + twos; i++) heights[order[i]] = 2;
        return heights;
    }

    void SelectCorridorHeights(int seed)
    {
        int count = Layout.CorridorCount;
        var requested = SelectRoomHeights(count, seed, data.twoTileCorridorPercent, data.threeTileCorridorPercent);
        CorridorHeightTiles = new int[count];
        var adjacentHeight = new int[count];
        var order = new int[count];
        for (int i = 0; i < count; i++) { CorridorHeightTiles[i] = 1; order[i] = i; }
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            int region = Layout.RegionIds[x,z];
            if (region < RoomHeightTiles.Length) continue;
            foreach (var dir in DungeonLayout.Neighbours)
            {
                int nx = x + dir.x, nz = z + dir.y;
                if (nx < 0 || nz < 0 || nx >= data.width || nz >= data.depth) continue;
                int neighbor = Layout.RegionIds[nx,nz];
                if (neighbor >= 0 && neighbor < RoomHeightTiles.Length)
                    adjacentHeight[region - RoomHeightTiles.Length] = Mathf.Max(adjacentHeight[region - RoomHeightTiles.Length], RoomHeightTiles[neighbor]);
            }
        }
        var rng = new System.Random(seed);
        for (int i = count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        // Allocate tallest first, and never create a tall section without a direct tall-room entrance.
        for (int height = 3; height >= 2; height--)
        {
            int remaining = 0;
            foreach (int value in requested) if (value == height) remaining++;
            foreach (int index in order)
            {
                if (remaining == 0) break;
                if (CorridorHeightTiles[index] != 1 || adjacentHeight[index] < height) continue;
                CorridorHeightTiles[index] = height;
                remaining--;
            }
        }
    }
    DungeonRoomProfile[] profiles;
    public double GenerationMilliseconds { get; private set; }
    // Room profile > biome > level. Unity's null check, not ??: empty references can be non-null wrappers.
    static T First<T>(params T[] options) where T:UnityEngine.Object { foreach(var o in options) if(o!=null) return o; return null; }
    static T[] FirstList<T>(params T[][] options) { foreach(var o in options) if(o!=null && o.Length>0) return o; return null; }
    // The level's list is shared by every biome; the biome's list adds to it.
    static T[] Both<T>(T[] level, T[] biome)
    {
        if (biome == null || biome.Length == 0) return level;
        if (level == null || level.Length == 0) return biome;
        var all = new T[level.Length + biome.Length];
        level.CopyTo(all, 0); biome.CopyTo(all, level.Length);
        return all;
    }
    // The biome's loot answers for every source (enemy, chest, barrel); the source passed to its
    // rolls picks which of its rules applies.
    LootSource FloorLoot=>Biome!=null ? Biome.loot : null;
    public LootSource EnemyLootTable=>FloorLoot;
    public DungeonBiome Biome { get; private set; }
    public int FloorInBiome { get; private set; }=1;
    public int BiomeFloors { get; private set; }=1;
    float EncounterChance=>Biome!=null ? Biome.EncounterChance(FloorInBiome,BiomeFloors) : 0;
    int MaxEnemiesPerRoom=>Biome!=null ? Biome.maxEnemiesPerRoom : 1;
    public DungeonMilestone Milestone { get; private set; }
    public int MerchantRoom { get; private set; }=-1;
    // Streams: random (the main one) draws roles, profiles, enemy counts and spawns, props and the
    // loot seeds of supply spots; extraRandom draws the merchant, templates and their sockets,
    // breakables and features, so adding those never shifted the main stream. decorRandom and
    // propRandom (Growth) draw wall decorations, dead ends and furnishing rules.
    System.Random extraRandom;
    DungeonRoomTemplate[] templates;
    GameObject[] templateInstances;
    readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<DungeonSocket>> actorSockets = new();
    readonly System.Collections.Generic.Dictionary<GameObject,int> featureCounts = new();

    // On a board-game level the floor of a raised room or a stair is above the table: Cell stands on it.
    public Vector3 Cell(Vector2Int p) => transform.position + new Vector3((p.x-data.width*.5f)*data.cellSize, BoardElevation(p), (p.y-data.depth*.5f)*data.cellSize);
    public Vector2Int CellOf(Vector3 world)
    {
        var local=world-transform.position;
        return new Vector2Int(Mathf.RoundToInt(local.x/data.cellSize+data.width*.5f),Mathf.RoundToInt(local.z/data.cellSize+data.depth*.5f));
    }

    static int Scramble(int x)
    {
        uint h=unchecked((uint)x);
        h^=h>>16; h=unchecked(h*0x85ebca6b); h^=h>>13; h=unchecked(h*0xc2b2ae35); h^=h>>16;
        return (int)(h&0x7fffffff);
    }

    public void Build(TableLevelData level, int seed, int floorNumber=1)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        FloorNumber=floorNumber;
        data = level; random = new System.Random(seed);
        // Hashed: System.Random's first values follow evenly spaced seeds almost linearly, so floor
        // seeds (runSeed + floor * step) made the merchant roll march in lockstep through a run.
        extraRandom = new System.Random(Scramble(unchecked(seed + 60013)));
        Biome = level.BiomeAt(floorNumber, out int floorInBiome, out int biomeFloors);
        FloorInBiome = floorInBiome; BiomeFloors = biomeFloors;
        Milestone = level.Milestone(floorNumber);
        actorSockets.Clear(); featureCounts.Clear(); MerchantRoom = -1;
        BeginDressing(seed);
        System.Collections.Generic.List<(Vector2Int, Vector2Int)> authoredDoors = null;
        if (level.layoutMode == DungeonLayoutMode.Authored)
        {
            authoredDoors = AuthoredLayout(level, seed);
            MerchantRoom = ChooseMerchantRoom(Roles, i => TemplatePrefab(i) != null);
        }
        else
        {
            if (level.layoutMode != DungeonLayoutMode.Grown)
                Debug.LogWarning($"{level.name}: the {level.layoutMode} layout style is retired; generating a Grown layout. Set Layout Style to Grown on the level.", level);
            GrowLayout(level, seed); // chooses the merchant's room too, before the room shapes
        }
        doorways = authoredDoors != null ? (level.doorPrefab != null ? authoredDoors : new())
            : level.doorPrefab != null && level.doorPercent > 0 ? Layout.SelectDoorways(level.doorPercent) : new();
        AddStartingRoomDoor();
        BuildArchitecture(level, seed, floorNumber, watch);
    }

    bool Authored => data.layoutMode == DungeonLayoutMode.Authored;

    // A hand-painted floor plan (the level's Authored Map): rooms, passages and doors exactly as drawn.
    System.Collections.Generic.List<(Vector2Int, Vector2Int)> AuthoredLayout(TableLevelData level, int seed)
    {
        var rows = level.AuthoredRows(level.authoredMap.TrimEnd());
        var rooms = level.authoredRooms ?? new TableLevelData.AuthoredRoom[0];
        var turns = new int[rooms.Length];
        for (int i = 0; i < rooms.Length; i++) turns[i] = rooms[i] != null ? rooms[i].turns : 0;
        Layout = DungeonLayout.FromMap(rows, seed, turns, out var doors);
        if (Layout.floor.GetLength(0) != level.width || Layout.floor.GetLength(1) != level.depth)
            throw new System.InvalidOperationException($"{level.name}: the floor plan is {Layout.floor.GetLength(0)}x{Layout.floor.GetLength(1)} but the level's grid is {level.width}x{level.depth}. Select the level asset to update it.");
        int count = Layout.rooms.Count;
        Roles = new RoomRole[count];
        for (int i = 0; i < count; i++)
            Roles[i] = i < rooms.Length && rooms[i] != null ? rooms[i].role : i == 0 ? RoomRole.Entrance : i == count - 1 ? RoomRole.Exit : RoomRole.Rest;
        profiles = new DungeonRoomProfile[count];
        // Furnishing keeps off the cells the content map uses.
        var content = level.AuthoredRows(level.authoredContent ?? "");
        for (int row = 0; row < content.Length; row++)
            for (int x = 0; x < content[row].Length; x++)
                if (AuthoredMarker(content[row][x]) != null) Layout.Reserve(new Vector2Int(x, level.depth - 1 - row));
        return doors;
    }

    TableLevelData.AuthoredMarker AuthoredMarker(char key)
    {
        if (data.authoredMarkers == null) return null;
        foreach (var m in data.authoredMarkers) if (m != null && !string.IsNullOrEmpty(m.key) && m.key[0] == key) return m;
        return null;
    }

    // The content map's enemies, breakables and floor items, after navigation exists.
    void SpawnAuthoredContent()
    {
        var content = data.AuthoredRows(data.authoredContent ?? "");
        for (int row = 0; row < content.Length; row++)
            for (int x = 0; x < content[row].Length; x++)
            {
                var marker = AuthoredMarker(content[row][x]);
                if (marker == null) continue;
                var cell = new Vector2Int(x, data.depth - 1 - row);
                var pos = Cell(cell);
                if (NavMesh.SamplePosition(pos, out var hit, data.cellSize, NavMesh.AllAreas)) pos = hit.position;
                // Facing the middle of its room, or along the passage.
                int room = Layout.RoomAt(cell);
                var look = room >= 0 ? Cell(Layout.RoomCenter(room)) - pos : Cell(Layout.Start) - pos; look.y = 0;
                var rotation = look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                if (marker.item != null) DungeonPickup.Spawn(marker.item, pos, transform, marker.count);
                if (marker.prefab == null) continue;
                if (marker.prefab.GetComponent<DungeonDestructible>() != null) { SpawnDestructible(marker.prefab, pos, rotation, marker.loot, random.Next()); continue; }
                // Authored enemies keep their own loot drop settings.
                SetUpEnemy(Instantiate(marker.prefab, pos, rotation, transform), null);
            }
    }

    void BuildArchitecture(TableLevelData level, int seed, int floorNumber, System.Diagnostics.Stopwatch watch)
    {
        RoomHeightTiles = SelectRoomHeights(Layout.rooms.Count, seed, level.twoTileRoomPercent, level.threeTileRoomPercent);
        for (int i = 0; i < RoomHeightTiles.Length; i++) { var t = TemplateOf(i); if (t != null && t.heightTiles > 0) RoomHeightTiles[i] = t.heightTiles; }
        if (Authored && level.authoredRooms != null)
            for (int i = 0; i < RoomHeightTiles.Length && i < level.authoredRooms.Length; i++)
                if (level.authoredRooms[i] != null && level.authoredRooms[i].height > 0) RoomHeightTiles[i] = level.authoredRooms[i].height;
        SelectCorridorHeights(unchecked(seed + 3571));
        RegionStyles = new DungeonRoomStyle[Layout.RegionCount];
        var styleRandom = new System.Random(unchecked(seed + 15485863));
        var roomStyles = Both(level.roomStyles, Biome != null ? Biome.roomStyles : null); var corridorStyles = Both(level.corridorStyles, Biome != null ? Biome.corridorStyles : null);
        for (int i = 0; i < RegionStyles.Length; i++)
            RegionStyles[i] = DungeonRoomStyle.Choose(i < Layout.rooms.Count ? roomStyles : corridorStyles, styleRandom);
        for (int i=0;i<profiles.Length;i++)
            if (profiles[i] != null && profiles[i].styleOverride != null) RegionStyles[i] = profiles[i].styleOverride;
        for (int i=0;i<profiles.Length;i++) { var t = TemplateOf(i); if (t != null && t.style != null) RegionStyles[i] = t.style; }
        if (level.corridorsCopyConnectedRoomStyle) CopyConnectedRoomStyles(seed);
        var openRoofs = Layout.SelectOpenRegions(level.hideRoof ? level.hideRoofPercent : 0, 173);
        var openEdges = Layout.SelectOpenRegions(level.hideEdges ? level.hideEdgesPercent : 0, 419);
        // A room template can open its own ceiling (the starting room's way in), with its own frame.
        var frames = new float[openRoofs.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            var t = i < Layout.rooms.Count ? TemplateOf(i) : null;
            if (t != null && t.openCeiling) openRoofs[i] = true;
            frames[i] = t != null && t.openCeiling ? t.ceilingFrame : level.skylightFrame;
        }
        bool RoofOpen(Vector2Int c) => Open(c) && openRoofs[Layout.RegionIds[c.x, c.y]];
        SpawnRoofOpen = RoofOpen(Layout.Start);
        bool WindowAt(Vector2Int c, Vector2Int d) => Open(c) && openEdges[Layout.RegionIds[c.x, c.y]] && !Open(c + d) && Layout.FacesOutside(c, d);
        if (Board) { ComputeBoardElevations(seed); BeginBoard(); }
        geometry = new GameObject("Architecture").transform; geometry.SetParent(transform, false);
        ceiling = new GameObject("Ceilings").transform; ceiling.SetParent(transform, false);
        float size = level.cellSize, tile = level.architectureTileSize;
        var dirs = DungeonLayout.Neighbours;
        for (int x = 0; x < level.width; x++) for (int z = 0; z < level.depth; z++)
        {
            if (!Layout.floor[x,z]) continue;
            if (Board) { BuildBoardCell(x, z); continue; }
            int room = Layout.RegionIds[x,z];
            var style = RegionStyles[room];
            float height = HeightAt(x,z);
            var pos = Cell(new Vector2Int(x,z));
            ArchitectureBox("Flagstone", pos - Vector3.up*.12f, new Vector3(size,.24f,size), FloorMaterial(room, x, z), geometry);
            // A taller neighbor's upper wall occupies a strip inside this cell. End the
            // ceiling just inside that wall's back. Ending exactly on the back face
            // makes the ceiling's vertical edge coplanar with the upper masonry.
            float CeilingInset(Vector2Int direction)
            {
                var neighborCell = new Vector2Int(x, z) + direction;
                return Open(neighborCell) && HeightAt(neighborCell.x, neighborCell.y) > height
                    ? WallThickness - CeilingWallOverlap : 0;
            }
            float left = CeilingInset(Vector2Int.left), right = CeilingInset(Vector2Int.right);
            float back = CeilingInset(Vector2Int.down), front = CeilingInset(Vector2Int.up);
            var roofCenter = pos + new Vector3((left-right)*.5f, height+.12f, (back-front)*.5f);
            var roofSize = new Vector3(size-left-right, .24f, size-back-front);
            var ceilingMaterial = DungeonRoomStyle.Resolve(style != null ? style.ceiling : null, level.ceilingMaterial);
            if (!openRoofs[room]) ArchitectureBox("Ceiling", roofCenter, roofSize, ceilingMaterial, ceiling);
            else Skylight(new Vector2Int(x, z), roofCenter, roofSize, frames[room], ceilingMaterial,
                DungeonRoomStyle.Resolve(style != null ? style.trim : null, level.trimMaterial), RoofOpen);
            foreach (var dir in dirs)
            {
                int nx = x+dir.x, nz = z+dir.y;
                bool neighbor = nx>=0 && nz>=0 && nx<level.width && nz<level.depth && Layout.floor[nx,nz];
                float bottom = neighbor ? HeightAt(nx,nz) : 0;
                if (bottom >= height) continue;
                var cell = new Vector2Int(x,z);
                bool window = !neighbor && WindowAt(cell, dir);
                bool visible = !window;
                if (!neighbor && visible) wallFaces.Add((cell, dir, room));
                // The full wall stays as collision; the window shows its sill, lintel and jambs.
                if (window) Window(cell, dir, height, WallSurface(room, 0, x, z, dir), WallSurface(room, Mathf.Max(0, height - tile), x, z, dir),
                    DungeonRoomStyle.Resolve(style != null ? style.trim : null, level.trimMaterial), WindowAt);
                // Square wall tiles preserve texture density; upper walls seal height changes above passages.
                for (float y = bottom; y < height - .01f; y += tile)
                {
                    var (wallCenter, wallScale) = WallPiece(cell, dir, y, 0, tile, y+tile*.5f);
                    var wall = ArchitectureBox("Crypt masonry", wallCenter, wallScale, WallSurface(room,y,x,z,dir), geometry);
                    MiterDiagonalEnds(wall, cell, dir, y, 0, true);
                    wall.GetComponent<Renderer>().enabled = visible;
                }
                if (style == null || !style.hideTrims)
                {
                    var trim = DungeonRoomStyle.Resolve(style != null ? style.trim : null, level.trimMaterial);
                    var (corniceCenter, corniceScale) = WallPiece(cell, dir, height-.6f, TrimDepth, .15f, height-.55f);
                    var cornice = Box("Stone cornice", corniceCenter, corniceScale, trim, geometry);
                    MiterDiagonalEnds(cornice, cell, dir, height-.6f, TrimDepth, false);
                    cornice.GetComponent<Renderer>().enabled = visible || window;
                    if (!neighbor)
                    {
                        var (footCenter, footScale) = WallPiece(cell, dir, 0, TrimDepth, .15f, .12f);
                        var footing = Box("Stone footing", footCenter, footScale, trim, geometry);
                        MiterDiagonalEnds(footing, cell, dir, 0, TrimDepth, false);
                        footing.GetComponent<Renderer>().enabled = visible || window;
                    }
                }
            }
        }
        ChooseTemplates();
        for (int i=0;i<Layout.rooms.Count;i++) DecorateRoom(i);
        DecorateWalls();
        PlaceTorches();
        DressCorridors();
        StockDeadEnds();
        if (Board) BuildBoardRim();
        // Board tiles stay separate: they are laid out one by one as rooms are revealed.
        var batching=gameObject.AddComponent<DungeonStaticGeometry>();
        if (!Board) batching.Combine(geometry,data.cellSize*8);
        batching.Combine(ceiling,data.cellSize*8);
        // Ceilings also answer to the player's ceiling light (see FirstPersonLighting).
        foreach(var r in ceiling.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= FirstPersonLighting.CeilingLayer;
        Physics.SyncTransforms();
        Surface = gameObject.AddComponent<NavMeshSurface>();
        Surface.collectObjects = CollectObjects.Children;
        Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        Surface.overrideVoxelSize = true; Surface.voxelSize = .12f;
        Surface.BuildNavMesh();
        navMeshData = Surface.navMeshData;
        ValidateNavigation();
        MakeDoors(seed);
        SpawnPoint = Cell(Layout.Start)+Vector3.up*.12f;
        float closest=float.MaxValue;
        foreach(var cell in Layout.RoomCells(0))foreach(var direction in dirs) {
            var outside=cell+direction;
            if(Layout.InRoom(0,outside) || !Layout.floor[outside.x,outside.y])continue;
            var delta=Cell(outside)-Cell(Layout.Start); delta.y=0;
            if(delta.sqrMagnitude<closest){closest=delta.sqrMagnitude;SpawnRotation=Quaternion.LookRotation(delta);}
        }
        ExitPoint = Cell(Layout.Exit);
        // The way in: a ladder right behind the player, who starts facing away from it into the room.
        var forward = SpawnRotation * Vector3.forward;
        var ladderAt = SpawnPoint - forward * 1.1f;
        var ladderCell = CellOf(ladderAt);
        if (!Layout.InBounds(ladderCell) || !Layout.floor[ladderCell.x, ladderCell.y]) { ladderAt = SpawnPoint; SpawnPoint += forward * 1.2f; }
        MakeExit(ladderAt - Vector3.up * .12f, true).transform.rotation = SpawnRotation;
        var exitStair = MakeExit(ExitPoint, false);
        // Spawn after navigation exists. The first room is always safe. An authored map brings its own.
        if (Authored) SpawnAuthoredContent();
        else for (int i=1;i<Layout.rooms.Count;i++)
        {
            // Treasure and storage rooms always have a guard; rest rooms stay safe.
            bool guarded=Roles[i]==RoomRole.Treasure || Roles[i]==RoomRole.Storage;
            if(Roles[i]!=RoomRole.Combat && Roles[i]!=RoomRole.Exit && !guarded)continue;
            int count=guarded ? 1 : random.Next(1,Mathf.Clamp(MaxEnemiesPerRoom,1,6)+1);
            if(templates[i]!=null && templates[i].replaceGeneratedEnemies)count=0;
            if(i==MerchantRoom)count=0;
            for(int j=0;j<count;j++)
            {
                var prefab = ChooseEnemy(i, random);
                if (prefab == null) break;
                var pos = Cell(Layout.RoomCenter(i)) + Vector3.right*((j%3)-1)*1.1f+Vector3.forward*(j/3)*1.2f;
                // A fountain or pillar in the middle can hide the floor near the centre; search the rest of the room
                // before giving up, so a combat room never ends up empty by accident.
                if (!NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas) && !FindRoomFloor(i,random,out hit)) continue;
                var enemy = Instantiate(prefab,hit.position,Quaternion.Euler(0,random.Next(360),0),transform);
                SetUpEnemy(enemy,random,profiles[i]!=null ? profiles[i].rewards : null);
            }
        }
        SpawnSocketActors(exitStair);
        gameObject.AddComponent<DungeonAmbience>().Initialize(Biome);
        gameObject.AddComponent<DungeonHud>().Initialize(this);
        if (Board) gameObject.AddComponent<BoardReveal>().Initialize(this);
        watch.Stop();GenerationMilliseconds=watch.Elapsed.TotalMilliseconds;
        Debug.Log($"Dungeon seed {seed}, floor {floorNumber}: {Layout.rooms.Count} rooms, {Layout.Connections.Count} connections, generated in {GenerationMilliseconds:F0} ms.",this);
    }

    // A walkable point somewhere inside the room, for spawns whose usual spot is blocked.
    bool FindRoomFloor(int room, System.Random rng, out NavMeshHit hit)
    {
        var rect = Layout.rooms[room];
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var cell = new Vector2Int(rng.Next(rect.xMin, rect.xMax), rng.Next(rect.yMin, rect.yMax));
            if (!Layout.InBounds(cell) || !Layout.floor[cell.x, cell.y]) continue;
            if (NavMesh.SamplePosition(Cell(cell), out hit, data.cellSize, NavMesh.AllAreas)) return true;
        }
        hit = default;
        return false;
    }

    void CopyConnectedRoomStyles(int seed)
    {
        // Use the exact doorway plan later instantiated by MakeDoors. Doors remain style
        // boundaries even after opening, so appearance never changes during play.
        var blocked = new System.Collections.Generic.HashSet<(Vector2Int, Vector2Int)>();
        foreach (var doorway in doorways)
        {
            var a = doorway.roomCell; var b = a + doorway.direction;
            blocked.Add((a,b)); blocked.Add((b,a));
        }
        // Region adjacency follows actual open passages, never proximity through a wall.
        var links = new System.Collections.Generic.SortedSet<int>[Layout.RegionCount];
        for (int i = 0; i < links.Length; i++) links[i] = new();
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            int region = Layout.RegionIds[x,z];
            if (region < 0) continue;
            Connect(x+1,z); Connect(x,z+1);
            void Connect(int nx, int nz)
            {
                if (nx >= data.width || nz >= data.depth) return;
                if (blocked.Contains((new Vector2Int(x,z), new Vector2Int(nx,nz)))) return;
                int other = Layout.RegionIds[nx,nz];
                if (other < 0 || other == region) return;
                links[region].Add(other); links[other].Add(region);
            }
        }
        var rng = new System.Random(unchecked(seed + 32452843));
        for (int region = Layout.rooms.Count; region < links.Length; region++)
        {
            var queue = new System.Collections.Generic.Queue<int>();
            var seen = new System.Collections.Generic.HashSet<int> { region };
            var candidates = new System.Collections.Generic.List<int>();
            queue.Enqueue(region);
            // Prefer directly adjoining rooms; search corridor junctions only if needed.
            while (queue.Count > 0 && candidates.Count == 0)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                    foreach (int neighbor in links[queue.Dequeue()])
                    {
                        if (!seen.Add(neighbor)) continue;
                        if (neighbor < Layout.rooms.Count) candidates.Add(neighbor);
                        else queue.Enqueue(neighbor);
                    }
            }
            RegionStyles[region] = candidates.Count > 0 ? RegionStyles[candidates[rng.Next(candidates.Count)]] : null;
        }
        // Spurs and passages cut off by doors on every side: take the style of whatever they touch.
        for (int pass = 0; pass < 4; pass++)
            for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
            {
                int region = Layout.RegionIds[x,z];
                if (region < Layout.rooms.Count || RegionStyles[region] != null) continue;
                foreach (var d in DungeonLayout.Neighbours)
                {
                    var n = new Vector2Int(x + d.x, z + d.y);
                    if (!Layout.InBounds(n) || !Layout.floor[n.x,n.y]) continue;
                    var style = RegionStyles[Layout.RegionIds[n.x,n.y]];
                    if (style != null) { RegionStyles[region] = style; break; }
                }
            }
    }

    // Rolls even when no profile fits the role, so the main stream stays in step.
    DungeonRoomProfile ChooseProfile(RoomRole role)
    {
        if(data.roomProfiles==null)return null;
        return WeightedPick.Choose(data.roomProfiles,p=>p!=null && p.role==role && FloorNumber>=p.minFloor && (p.maxFloor<=0||FloorNumber<=p.maxFloor) ? p.weight : 0,
            random,drawWhenEmpty:true,singleTotal:true);
    }

    void MakeDoors(int seed)
    {
        if(data.doorPrefab==null)return;
        foreach(var doorway in doorways) {
            var p=doorway.roomCell;
            var dir=doorway.direction;
            var n=p+dir;
            var pos=(Cell(p)+Cell(n))*.5f;
            if (Board) pos.y = Cell(p).y; // it stands at the room's edge, where the steps begin
            var door=Instantiate(data.doorPrefab,pos,Quaternion.LookRotation(new Vector3(dir.x,0,dir.y)),transform);
            // Cell width changes the span, not the doorway height or ceiling clearance.
            door.transform.localScale=new Vector3(data.cellSize/2f,1,1);
            var gate = door.GetComponent<DungeonDoor>();
            if (gate != null) gate.FitCeiling(Mathf.Min(HeightAt(p.x,p.y), HeightAt(n.x,n.y)), data.architectureTileSize);
            if (gate != null && gate.lintel != null)
            {
                // Split stationary lintel at tile boundaries so tall doors use the correct upper material.
                var renderer = gate.lintel.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
                float top = Mathf.Min(HeightAt(p.x,p.y), HeightAt(n.x,n.y));
                float bottom = Mathf.Min(gate.openingHeight, top) - .025f;
                // Bury the cap inside the ceiling so its top never shares the ceiling plane.
                // Recess both vertical faces slightly to avoid coplanar overlap with upper
                // masonry when the rooms on either side have different ceiling heights.
                top += CeilingWallOverlap;
                const float lintelFaceInset = .005f;
                // One piece per wall tile row, counted in whole rows so the loop always ends.
                float tileSize = data.architectureTileSize;
                for (int row = Mathf.FloorToInt(bottom/tileSize); row*tileSize < top; row++)
                {
                    float y = Mathf.Max(bottom, row*tileSize);
                    float end = Mathf.Min(top, (row+1)*tileSize);
                    if (end <= y) continue;
                    float depth = WallThickness - lintelFaceInset * 2;
                    // On a board the lintel is the arch's beam: across both posts, and part of the door.
                    float spanWidth = Board ? data.cellSize + .68f : data.cellSize;
                    if (Board) depth = WallThickness + .06f;
                    var span = dir.x != 0 ? new Vector3(depth,end-y,spanWidth) : new Vector3(spanWidth,end-y,depth);
                    // In line with the room walls either side, which sit behind the edge on the corridor side.
                    var lintel = ArchitectureBox("Styled door lintel", pos+new Vector3(dir.x,0,dir.y)*(Board ? 0 : WallThickness*.5f)+Vector3.up*((y+end)*.5f), span,
                        WallMaterial(Layout.RegionIds[p.x,p.y],y), Board ? door.transform : transform);
                    Destroy(lintel.GetComponent<Collider>()); // Original fitted lintel retains collision.
                }
                if (Board) BoardDoorFrame(gate, p, n, pos.y + top);
            }
            if (Board && gate != null) boardDoors.Add((gate, p, n));
        }
    }

    // Supply spots, breakables, features and props. A room with a template gets the template instead.
    // No lights here: they come from wall torches (PlaceTorches) and the player, like Barony.
    void DecorateRoom(int index)
    {
        if (templates[index] != null) { PlaceTemplate(index); return; }
        var profile=profiles[index];
        var rewards=profile!=null ? profile.rewards : null;
        // Only decorate cells outside the reserved doorway-to-centre routes.
        var available=new System.Collections.Generic.List<Vector2Int>();
        foreach(var p in Layout.RoomCells(index))if(!Layout.Reserved.Contains(p))available.Add(p);
        for(int i=available.Count-1;i>0;i--){int j=random.Next(i+1);(available[i],available[j])=(available[j],available[i]);}
        int cursor=0;
        bool Spot(out Vector3 pos) {pos=default;if(cursor>=available.Count)return false;pos=Cell(available[cursor++]);return true;}
        // An authored map places its own loot.
        if(!Authored && Spot(out var supply))MakeContainer(supply,Roles[index]==RoomRole.Treasure,rewards,FaceCenter(supply,index));
        if(Roles[index]==RoomRole.Storage && Spot(out var extra))MakeContainer(extra,false,rewards,FaceCenter(extra,index));
        var (rules,prefabs)=Furnishing(profile);
        if(rules!=null)
        {
            int left=PlaceExtras(index,available,cursor,rewards);
            PlaceRules(index,available.GetRange(left,available.Count-left),rules);
            return;
        }
        int min=profile!=null?Mathf.Clamp(profile.minProps,0,8):1;
        int max=profile!=null?Mathf.Clamp(profile.maxProps,min,8):3;
        int props=random.Next(min,max+1);
        // Newer placement draws from the extra stream, after the original props consumed theirs.
        int propCursor=cursor+props;
        PlaceExtras(index,available,propCursor,rewards);
        for(int i=0;i<props && Spot(out var pos);i++) {
            if(prefabs!=null && prefabs.Length>0) {
                var prefab=prefabs[random.Next(prefabs.Length)];
                if(prefab!=null)Instantiate(prefab,pos,Quaternion.Euler(0,random.Next(4)*90,0),geometry);
            }
        }
    }

    void ValidateNavigation()
    {
        if(!NavMesh.SamplePosition(Cell(Layout.Start),out var start,1f,NavMesh.AllAreas))throw new InvalidOperationException("No navigation at dungeon entrance.");
        var path=new NavMeshPath();
        for(int i=0;i<Layout.rooms.Count;i++) {
            if(!NavMesh.SamplePosition(Cell(Layout.RoomCenter(i)),out var target,1f,NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position,target.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException($"Furnished dungeon has an unreachable room. Seed {Layout.seed}; room {i} at {Layout.rooms[i]}. Check prop collider sizes.");
        }
    }

    // A rotation whose forward (+Z, the chest's latch side) points at the room centre, snapped to 90 degrees.
    Quaternion FaceCenter(Vector3 pos,int room)
    {
        var toCenter=Cell(Layout.RoomCenter(room))-pos; toCenter.y=0;
        if(toCenter.sqrMagnitude<.01f)return Quaternion.identity;
        var facing=Mathf.Abs(toCenter.x)>Mathf.Abs(toCenter.z) ? new Vector3(Mathf.Sign(toCenter.x),0,0) : new Vector3(0,0,Mathf.Sign(toCenter.z));
        return Quaternion.LookRotation(facing);
    }

    // A breakable's pick and turn come from the extra stream, but its loot seed (like a chest's or a
    // barrel's) from the main one: one main draw per supply spot, whatever stands there.
    void MakeContainer(Vector3 pos,bool chest,LootSource rewardOverride=null,Quaternion? facing=null)
    {
        if(!chest)
        {
            var breakable=DungeonWeightedPrefab.Choose(Destructibles,extraRandom,IsFloorBreakable);
            if(breakable!=null){SpawnDestructible(breakable,pos,Quaternion.Euler(0,extraRandom.Next(4)*90,0),rewardOverride,random.Next());return;}
        }
        bool authoredChest=chest && data.chestPrefab!=null;
        var go=authoredChest ? Instantiate(data.chestPrefab,transform) : new GameObject(chest ? "Supply chest" : "Breakable barrel"); go.transform.SetParent(transform,false); go.transform.position=pos;
        if(chest && facing.HasValue)go.transform.rotation=facing.Value;
        if(chest && !authoredChest)
        {
            // Built in local space so the latch (+Z) turns with the facing.
            Box("Chest",pos+Vector3.up*.43f,new Vector3(1.25f,.85f,.7f),data.woodMaterial,go.transform);
            Box("Lid",pos+Vector3.up*.92f,new Vector3(1.35f,.16f,.78f),data.woodMaterial,go.transform);
            Box("Latch",pos+new Vector3(0,.66f,.42f),new Vector3(.15f,.3f,.08f),data.metalMaterial,go.transform);
            foreach(Transform part in go.transform){part.localPosition=part.position-pos;part.localRotation=Quaternion.identity;}
        }
        else if(!chest)
        {
            var barrel=GameObject.CreatePrimitive(PrimitiveType.Cylinder); barrel.name="Oak staves";
            barrel.transform.SetParent(go.transform,false); barrel.transform.localPosition=Vector3.up*.55f;
            barrel.transform.localScale=new Vector3(.8f,.55f,.8f); barrel.GetComponent<Renderer>().sharedMaterial=data.woodMaterial;
            foreach(float y in new[]{.18f,.85f})
            {
                var band=GameObject.CreatePrimitive(PrimitiveType.Cylinder); band.name="Iron hoop";
                band.transform.SetParent(go.transform,false); band.transform.localPosition=Vector3.up*y;
                band.transform.localScale=new Vector3(.84f,.045f,.84f); band.GetComponent<Renderer>().sharedMaterial=data.metalMaterial;
                DestroyImmediate(band.GetComponent<Collider>());
            }
        }
        var container=go.GetComponent<DungeonContainer>();
        if(container==null)container=go.AddComponent<DungeonContainer>();
        container.chest=chest; container.loot=First(rewardOverride,FloorLoot); container.floorNumber=FloorNumber;
        container.seed=random.Next(); container.debrisMaterial=data.woodMaterial;
        container.openAudio=chest ? data.chestOpenAudio:data.containerBreakAudio;
        if(chest && !authoredChest)
        {
            var col=go.AddComponent<BoxCollider>(); col.center=Vector3.up*.5f; col.size=new Vector3(1.4f,1,.9f);
            go.AddComponent<InteractableTrigger>();
        }
    }

    public void ShowCeilings(bool value) { if(ceiling!=null) ceiling.gameObject.SetActive(value); }
    public Transform Ceilings => ceiling;
    // The spawn room has a skylight: the table reveal's camera can fly in through it.
    public bool SpawnRoofOpen { get; private set; }

    void OnDestroy()
    {
        foreach (var mesh in architectureMeshes.Values) if (mesh != null) Destroy(mesh);
        architectureMeshes.Clear();
        foreach (var mesh in cornerMeshes) if (mesh != null) Destroy(mesh);
        cornerMeshes.Clear();
        // NavMeshSurface only removes its data instance on disable; the NavMeshData asset itself is ours to free.
        if (navMeshData != null) Destroy(navMeshData);
        navMeshData = null;
    }
    public static GameObject Box(string name,Vector3 pos,Vector3 size,Material material,Transform parent)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,true);
        go.transform.position=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        return go;
    }
}
