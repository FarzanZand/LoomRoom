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
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            int region = Layout.RegionIds[x,z];
            if (region < RoomHeightTiles.Length) continue;
            foreach (var dir in dirs)
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
    // The biome's loot answers for every source; the source only picks which of its rules applies.
    LootSource Loot(DungeonLootSource source)=>Biome!=null ? Biome.loot : null;
    public LootSource EnemyLootTable=>Loot(DungeonLootSource.Enemy);
    public DungeonBiome Biome { get; private set; }
    public int FloorInBiome { get; private set; }=1;
    public int BiomeFloors { get; private set; }=1;
    float EncounterChance=>Biome!=null ? Biome.EncounterChance(FloorInBiome,BiomeFloors) : 0;
    int MaxEnemiesPerRoom=>Biome!=null ? Biome.maxEnemiesPerRoom : 1;
    public DungeonMilestone Milestone { get; private set; }
    public DungeonBossEncounter BossEncounter { get; private set; }
    public int MerchantRoom { get; private set; }=-1;
    // Placement for everything added after the original generator (templates, breakables,
    // features, merchant). Its own stream keeps older seeds' layouts, fights and loot unchanged.
    System.Random extraRandom;
    DungeonRoomTemplate[] templates;
    GameObject[] templateInstances;
    readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<DungeonSocket>> actorSockets = new();
    readonly System.Collections.Generic.Dictionary<GameObject,int> featureCounts = new();

    public Vector3 Cell(Vector2Int p) => transform.position + new Vector3((p.x-data.width*.5f)*data.cellSize, 0, (p.y-data.depth*.5f)*data.cellSize);
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
        if (level.layoutMode == DungeonLayoutMode.Grown) GrowLayout(level, seed);
        else PartitionLayout(level, seed);
        doorways = level.doorPrefab != null && level.doorPercent > 0
            ? Layout.SelectDoorways(level.doorPercent) : new();
        AddStartingRoomDoor();
        BuildArchitecture(level, seed, floorNumber, watch);
    }

    // The original layout: one rectangular room per partition of the grid.
    void PartitionLayout(TableLevelData level, int seed)
    {
        Layout = new DungeonLayout(level.width, level.depth, level.roomCount, seed, level.loopPercent);
        Roles=new RoomRole[Layout.rooms.Count];
        for(int i=0;i<Roles.Length;i++) {
            float encounter=EncounterChance;
            Roles[i]=i==0?RoomRole.Entrance:Layout.rooms[i].Contains(Layout.Exit)?RoomRole.Exit:random.NextDouble()<encounter?RoomRole.Combat:(RoomRole)random.Next(2,5);
        }
        profiles=new DungeonRoomProfile[Roles.Length];
        for(int i=0;i<profiles.Length;i++)profiles[i]=ChooseProfile(Roles[i]);
        var scales = new float[Layout.rooms.Count];
        int exitRoom = 0;
        for (int i=0;i<scales.Length;i++)
        {
            scales[i] = profiles[i] != null ? profiles[i].sizeScale : 1;
            if (Roles[i] == RoomRole.Exit) exitRoom = i;
        }
        var sizeRandom = new System.Random(unchecked(seed + 49999));
        var order = new int[scales.Length];
        for (int i=0;i<order.Length;i++) order[i]=i;
        for (int i=order.Length-1;i>0;i--) { int j=sizeRandom.Next(i+1); (order[i],order[j])=(order[j],order[i]); }
        int largeCount = Mathf.RoundToInt(scales.Length*Mathf.Clamp(level.largeRoomPercent,0,30)/100f);
        for (int i=0;i<largeCount;i++) scales[order[i]] *= Mathf.Clamp(level.largeRoomScale,1,3);
        if (Milestone != null) scales[exitRoom] = Mathf.Max(scales[exitRoom], Mathf.Clamp(Milestone.arenaSizeScale,1,3));
        // Rebuild routes, region ownership, reserved walkways and distances around the resized rooms.
        Layout = new DungeonLayout(level.width, level.depth, level.roomCount, seed, level.loopPercent, scales, exitRoom);
    }

    void BuildArchitecture(TableLevelData level, int seed, int floorNumber, System.Diagnostics.Stopwatch watch)
    {
        RoomHeightTiles = SelectRoomHeights(Layout.rooms.Count, seed, level.twoTileRoomPercent, level.threeTileRoomPercent);
        for (int i = 0; i < RoomHeightTiles.Length; i++) { var t = TemplateOf(i); if (t != null && t.heightTiles > 0) RoomHeightTiles[i] = t.heightTiles; }
        SelectCorridorHeights(unchecked(seed + 3571));
        RegionStyles = new DungeonRoomStyle[Layout.RegionCount];
        var styleRandom = new System.Random(unchecked(seed + 15485863));
        var roomStyles = Both(level.roomStyles, Biome?.roomStyles); var corridorStyles = Both(level.corridorStyles, Biome?.corridorStyles);
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
        bool RoofOpen(Vector2Int c) => OpenCell(c) && openRoofs[Layout.RegionIds[c.x, c.y]];
        SpawnRoofOpen = RoofOpen(Layout.Start);
        bool WindowAt(Vector2Int c, Vector2Int d) => OpenCell(c) && openEdges[Layout.RegionIds[c.x, c.y]] && !OpenCell(c + d) && Layout.FacesOutside(c, d);
        geometry = new GameObject("Architecture").transform; geometry.SetParent(transform, false);
        ceiling = new GameObject("Ceilings").transform; ceiling.SetParent(transform, false);
        float size = level.cellSize, tile = level.architectureTileSize;
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int x = 0; x < level.width; x++) for (int z = 0; z < level.depth; z++)
        {
            if (!Layout.floor[x,z]) continue;
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
                return OpenCell(neighborCell) && HeightAt(neighborCell.x, neighborCell.y) > height
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
        var lighting = data.DungeonLighting(FloorNumber);
        ChooseTemplatesAndMerchant();
        for (int i=0;i<Layout.rooms.Count;i++) DecorateRoom(i,lighting);
        DecorateWalls();
        PlaceTorches();
        DressCorridors();
        StockDeadEnds();
        var batching=gameObject.AddComponent<DungeonStaticGeometry>();
        batching.Combine(geometry,data.cellSize*8);
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
            var delta=Cell(outside)-Cell(Layout.Start);
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
        // Spawn after navigation exists. The first room is always safe.
        for (int i=1;i<Layout.rooms.Count;i++)
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
                level.balance?.Apply(enemy.GetComponent<Character>(),floorNumber);
                var drop = enemy.GetComponent<DungeonLootDrop>();
                if(drop != null) { if(!drop.overrideLevelTable)drop.table = First(profiles[i]?.rewards, Loot(DungeonLootSource.Enemy)); drop.seed = random.Next(); drop.floorNumber=floorNumber; }
            }
        }
        SpawnSocketActors(exitStair);
        gameObject.AddComponent<DungeonAmbience>().Initialize(Biome);
        gameObject.AddComponent<DungeonHud>().Initialize(this);
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
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var n = new Vector2Int(x + d.x, z + d.y);
                    if (!Layout.InBounds(n) || !Layout.floor[n.x,n.y]) continue;
                    var style = RegionStyles[Layout.RegionIds[n.x,n.y]];
                    if (style != null) { RegionStyles[region] = style; break; }
                }
            }
    }

    DungeonRoomProfile ChooseProfile(RoomRole role)
    {
        if(data.roomProfiles==null)return null;
        float total=0;
        bool Eligible(DungeonRoomProfile p)=>p!=null && p.role==role && p.weight>0 && FloorNumber>=p.minFloor && (p.maxFloor<=0||FloorNumber<=p.maxFloor);
        foreach(var profile in data.roomProfiles)if(Eligible(profile))total+=profile.weight;
        double roll=random.NextDouble()*total;
        foreach(var profile in data.roomProfiles)if(Eligible(profile)){roll-=profile.weight;if(roll<0)return profile;}
        return null;
    }

    void MakeDoors(int seed)
    {
        if(data.doorPrefab==null)return;
        foreach(var doorway in doorways) {
            var p=doorway.roomCell;
            var dir=doorway.direction;
            var n=p+dir;
            var pos=(Cell(p)+Cell(n))*.5f;
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
                for (float y = bottom; y < top;)
                {
                    float end = Mathf.Min(top, (Mathf.Floor(y/data.architectureTileSize)+1)*data.architectureTileSize);
                    float depth = WallThickness - lintelFaceInset * 2;
                    var span = dir.x != 0 ? new Vector3(depth,end-y,data.cellSize) : new Vector3(data.cellSize,end-y,depth);
                    // In line with the room walls either side, which sit behind the edge on the corridor side.
                    var lintel = ArchitectureBox("Styled door lintel", pos+new Vector3(dir.x,0,dir.y)*(WallThickness*.5f)+Vector3.up*((y+end)*.5f), span,
                        WallMaterial(Layout.RegionIds[p.x,p.y],y), transform);
                    Destroy(lintel.GetComponent<Collider>()); // Original fitted lintel retains collision.
                    y = end;
                }
            }
        }
    }

    void DecorateRoom(int index,LightingManager.MoodState lighting)
    {
        Vector3 center = Cell(Layout.RoomCenter(index));
        var template = templates[index];
        if (template != null && !template.keepRoomLight) { PlaceTemplate(index); return; }
        // Light comes from wall torches (PlaceTorches) and the player, like Barony.
        var profile=profiles[index];
        if (template != null) { PlaceTemplate(index); return; }
        // Only decorate cells outside the reserved doorway-to-centre routes.
        var available=new System.Collections.Generic.List<Vector2Int>();
        foreach(var p in Layout.RoomCells(index))if(!Layout.Reserved.Contains(p))available.Add(p);
        for(int i=available.Count-1;i>0;i--){int j=random.Next(i+1);(available[i],available[j])=(available[j],available[i]);}
        int cursor=0;
        bool Spot(out Vector3 pos) {pos=default;if(cursor>=available.Count)return false;pos=Cell(available[cursor++]);return true;}
        if(Spot(out var supply))MakeContainer(supply,Roles[index]==RoomRole.Treasure,profile?.rewards,FaceCenter(supply,index));
        if(Roles[index]==RoomRole.Storage && Spot(out var extra))MakeContainer(extra,false,profile?.rewards,FaceCenter(extra,index));
        var (rules,prefabs)=Furnishing(profile);
        if(rules!=null)
        {
            int left=PlaceExtras(index,available,cursor,profile?.rewards);
            PlaceRules(index,available.GetRange(left,available.Count-left),rules);
            return;
        }
        int min=profile!=null?Mathf.Clamp(profile.minProps,0,8):1;
        int max=profile!=null?Mathf.Clamp(profile.maxProps,min,8):3;
        int props=random.Next(min,max+1);
        // Newer placement draws from the extra stream, after the original props consumed theirs.
        int propCursor=cursor+props;
        PlaceExtras(index,available,propCursor,profile?.rewards);
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
        var container=go.GetComponent<DungeonContainer>() ?? go.AddComponent<DungeonContainer>(); container.chest=chest; container.loot=rewardOverride ?? Loot(chest?DungeonLootSource.Chest:DungeonLootSource.Barrel); container.floorNumber=FloorNumber;
        container.seed=random.Next(); container.debrisMaterial=data.woodMaterial;
        container.openAudio=chest ? data.chestOpenAudio:data.containerBreakAudio;
        if(chest && !authoredChest)
        {
            var col=go.AddComponent<BoxCollider>(); col.center=Vector3.up*.5f; col.size=new Vector3(1.4f,1,.9f);
            go.AddComponent<InteractableTrigger>();
        }
    }

    bool Descends=>data.multipleLevels && FloorNumber<Mathf.Max(1,data.levelCount);
    DungeonExit MakeExit(Vector3 pos,bool entrance)
    {
        bool descending=!entrance && Descends;
        var go=new GameObject(entrance ? "Entrance stair" : descending ? "Stairs down" : "Final exit stair"); go.transform.SetParent(transform,false); go.transform.position=pos;
        var exit=go.AddComponent<DungeonExit>(); exit.entrance=entrance;
        var col=go.AddComponent<BoxCollider>(); col.isTrigger=true;
        if(descending){ BuildStairwell(go.transform,exit); col.center=new Vector3(0,1f,.3f); col.size=new Vector3(1.1f,1.6f,1f); }
        else { BuildLadder(go.transform); col.center=new Vector3(0,1,0); col.size=new Vector3(1.2f,2,1.2f); }
        go.AddComponent<InteractableTrigger>();
        return exit;
    }

    // A stone stairwell: up onto a landing, then steps going down into the dark under a lintel.
    // (The dungeon sits on the table top, so the way down is built above the floor.) Faces the
    // open neighbouring cell; an iron portcullis closes it while the exit is sealed.
    void BuildStairwell(Transform root, DungeonExit exit)
    {
        var cell=CellOf(root.position);
        foreach(var d in new[]{Vector2Int.down,Vector2Int.left,Vector2Int.right,Vector2Int.up})
            if(Layout.InBounds(cell+d) && Layout.floor[cell.x+d.x,cell.y+d.y]){ root.rotation=Quaternion.LookRotation(new Vector3(-d.x,0,-d.y)); break; }
        int region=Layout.RegionIds[cell.x,cell.y];
        var stone=FloorMaterial(region,cell.x,cell.y);
        var walls=stone;
        var block=new MaterialPropertyBlock();
        GameObject Part(string name,Vector3 local,Vector3 size,Material material,float shade=1,Transform parent=null)
        {
            var g=Box(name,root.TransformPoint(local),size,material,parent!=null ? parent : root); g.transform.rotation=root.rotation;
            if(shade<1){ var r=g.GetComponent<Renderer>(); r.GetPropertyBlock(block); var c=Color.white*shade; c.a=1; block.SetColor("_BaseColor",c); block.SetColor("_Color",c); r.SetPropertyBlock(block); }
            return g;
        }
        // Local +z points into the stairwell; the opening faces -z.
        const float width=1.1f, height=2.4f, wall=.28f, back=.95f;
        // Two steps up to the landing at the mouth.
        Part("Step",new Vector3(0,.06f,-.75f),new Vector3(width+.3f,.12f,.3f),data.trimMaterial);
        Part("Landing",new Vector3(0,.12f,-.42f),new Vector3(width+.3f,.24f,.4f),data.trimMaterial);
        // Inside: steps going down, darker the deeper they go, ending in black.
        for(int i=0;i<4;i++)
        {
            float top=.18f-i*.055f, z=-.09f+i*.25f;
            Part("Step down",new Vector3(0,top*.5f,z),new Vector3(width,top,.25f),stone,Mathf.Lerp(.55f,.04f,i/3f));
        }
        Part("Dark",new Vector3(0,height*.5f,back-.05f),new Vector3(width,height,.1f),walls,0);
        Part("Dark roof",new Vector3(0,height-.05f,.4f),new Vector3(width,.1f,1.2f),walls,0);
        // Stone housing.
        foreach(float x in new[]{-1f,1f})
        {
            Part("Side wall",new Vector3(x*(width+wall)*.5f,height*.5f,.35f),new Vector3(wall,height,1.3f),walls);
            Part("Side wall inner",new Vector3(x*(width*.5f-.01f),height*.5f,.45f),new Vector3(.02f,height,1f),walls,.08f);
        }
        Part("Back wall",new Vector3(0,height*.5f,back+wall*.5f),new Vector3(width+wall*2,height,wall),walls);
        Part("Lintel",new Vector3(0,height+.1f,-.25f),new Vector3(width+wall*2+.12f,.22f,.3f),data.trimMaterial);
        Part("Roof",new Vector3(0,height+.06f,.4f),new Vector3(width+wall*2+.06f,.14f,1.35f),walls);
        // Portcullis while a guardian lives.
        var gate=new GameObject("Portcullis"); gate.transform.SetParent(root,false);
        for(int i=-2;i<=2;i++) Part("Bar",new Vector3(i*.22f,height*.5f,-.2f),new Vector3(.05f,height,.05f),data.metalMaterial,1,gate.transform);
        for(int i=1;i<=3;i++) Part("Bar",new Vector3(0,i*.45f,-.2f),new Vector3(width,.05f,.05f),data.metalMaterial,1,gate.transform);
        var bars=gate.AddComponent<BoxCollider>(); bars.center=new Vector3(0,height*.5f,-.2f); bars.size=new Vector3(width,height,.1f);
        exit.grate=gate; gate.SetActive(exit.Sealed);
    }

    // A wooden ladder up to a hatch in the ceiling: the way in, and the way out on the last floor.
    void BuildLadder(Transform root)
    {
        var cell=CellOf(root.position);
        float height=Layout.InBounds(cell) && Layout.floor[cell.x,cell.y] ? HeightAt(cell.x,cell.y) : data.architectureTileSize;
        var p=root.position;
        var wood=data.woodMaterial;
        foreach(float x in new[]{-.28f,.28f}) Box("Rail",p+new Vector3(x,height*.5f,0),new Vector3(.08f,height,.08f),wood,root);
        for(float y=.3f;y<height-.1f;y+=.36f) Box("Rung",p+new Vector3(0,y,0),new Vector3(.52f,.05f,.06f),wood,root);
        // Hatch: a dark square framed in wood, just under the ceiling.
        Box("Hatch",p+Vector3.up*(height-.05f),new Vector3(1.1f,.06f,1.1f),wood,root);
        foreach(var d in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            Box("Hatch frame",p+d*.6f+Vector3.up*(height-.07f),d.x!=0 ? new Vector3(.12f,.14f,1.32f) : new Vector3(1.32f,.14f,.12f),data.trimMaterial,root);
    }

    // ── Templates, breakables, features, merchant, boss ──────────────

    DungeonWeightedPrefab[] Destructibles => Both(data.destructibles, Biome?.destructibles);
    DungeonFeature[] Features => Both(data.features, Biome?.features);
    static bool IsFloorBreakable(GameObject prefab) { var d=prefab.GetComponent<DungeonDestructible>(); return d==null || d.placement==DestructiblePlacement.Floor; }
    static DungeonRoomRoles Mask(RoomRole role) => (DungeonRoomRoles)(1<<(int)role);

    // A room profile's own enemies replace the biome's spawns for that room.
    GameObject ChooseEnemy(int room, System.Random rng)
    {
        var own=profiles[room]?.enemies;
        if(own!=null && own.Length>0) return own[rng.Next(own.Length)];
        return Biome!=null ? Biome.ChooseEnemy(rng,FloorInBiome) : null;
    }

    // The hand-built interior for a room: the starting room, a milestone arena or the profile's template.
    GameObject TemplatePrefab(int i) =>
        i==0 && UsesStartingRoom ? data.startingRoom
        : i==MerchantRoom && i!=0 && data.shopRoom!=null ? data.shopRoom
        : Roles[i]==RoomRole.Exit && Milestone!=null && Milestone.arena!=null ? Milestone.arena : profiles[i]?.roomTemplate;
    DungeonRoomTemplate TemplateOf(int i) { var p=TemplatePrefab(i); return p!=null ? p.GetComponent<DungeonRoomTemplate>() : null; }

    void ChooseTemplatesAndMerchant()
    {
        templates=new DungeonRoomTemplate[Layout.rooms.Count];
        templateInstances=new GameObject[Layout.rooms.Count];
        ChooseMerchantRoom();
        for(int i=0;i<templates.Length;i++)
        {
            var prefab=TemplatePrefab(i);
            if(prefab==null)continue;
            templates[i]=prefab.GetComponent<DungeonRoomTemplate>();
            if(templates[i]==null){Debug.LogWarning($"Room template {prefab.name} has no DungeonRoomTemplate component; generating the room normally.",prefab);continue;}
            // Smaller rooms still get the template: pieces outside the room or on walkways are removed.
        }
    }

    // A quiet room for the merchant. With a shop room template, rooms that have no hand-built
    // interior of their own are preferred so the shop never replaces a shrine or an arena.
    void ChooseMerchantRoom()
    {
        if(data.merchantPrefab==null || extraRandom.NextDouble()>=data.MerchantChance(FloorNumber))return;
        var candidates=new System.Collections.Generic.List<int>();
        var plain=new System.Collections.Generic.List<int>();
        for(int i=1;i<Roles.Length;i++)
            if(Roles[i]==RoomRole.Rest || Roles[i]==RoomRole.Storage || Roles[i]==RoomRole.Treasure){candidates.Add(i);if(TemplatePrefab(i)==null)plain.Add(i);}
        if(data.shopRoom!=null && plain.Count>0)candidates=plain;
        if(candidates.Count==0)candidates.Add(0);
        MerchantRoom=candidates[extraRandom.Next(candidates.Count)];
    }

    // Walkways inside a room: every doorway approach, and the centre cross or the routes to the centre.
    System.Collections.Generic.HashSet<Vector2Int> Lanes(int room)
    {
        var lanes=new System.Collections.Generic.HashSet<Vector2Int>();
        foreach(var p in Layout.RoomCells(room))if(Layout.Walkways.Contains(p))lanes.Add(p);
        return lanes;
    }

    void PlaceTemplate(int index)
    {
        var template=templates[index];
        // Grown rooms built from a template footprint may be turned; the interior turns with them.
        var instance=Instantiate(template.gameObject,Cell(Layout.RoomCenter(index)),Quaternion.Euler(0,90*Layout.RoomRotation(index),0),transform);
        instance.name=template.gameObject.name;
        templateInstances[index]=instance;
        var lanes=Lanes(index);
        foreach(var piece in instance.GetComponentsInChildren<DungeonTemplatePiece>(true))
        {
            if(piece==null)continue;
            var cell=CellOf(piece.transform.position);
            // Outside a smaller room it would sit in a wall; on a walkway it would block the route.
            if(!Layout.InRoom(index,cell) || (piece.removeIfBlocking && lanes.Contains(cell)))
                DestroyImmediate(piece.gameObject); // navigation is baked this frame
        }
        var rewards=profiles[index]?.rewards;
        foreach(var socket in instance.GetComponentsInChildren<DungeonSocket>(true))
        {
            if(!Layout.InRoom(index,CellOf(socket.transform.position)))continue;
            if(socket.chance<1f && extraRandom.NextDouble()>=socket.chance)continue;
            var t=socket.transform;
            switch(socket.type)
            {
                case DungeonSocketType.Chest: MakeContainer(t.position,true,rewards,t.rotation); break;
                case DungeonSocketType.Breakable:
                {
                    var prefab=socket.overridePrefab!=null ? socket.overridePrefab : DungeonWeightedPrefab.Choose(Destructibles,extraRandom);
                    if(prefab!=null)SpawnDestructible(prefab,t.position,t.rotation,rewards,extraRandom.Next());
                    break;
                }
                case DungeonSocketType.WallLight: SocketLight(socket,index); break;
                case DungeonSocketType.Feature:
                {
                    var prefab=socket.overridePrefab;
                    if(prefab==null){var f=PickFeature(index);prefab=f?.prefab;}
                    if(prefab!=null)SpawnFeature(prefab,t.position,t.rotation);
                    break;
                }
                default:
                    if(!actorSockets.TryGetValue(index,out var list))actorSockets[index]=list=new();
                    list.Add(socket);
                    break;
            }
        }
        if(!template.replaceGeneratedProps)
        {
            var available=new System.Collections.Generic.List<Vector2Int>();
            foreach(var p in Layout.RoomCells(index))if(!lanes.Contains(p) && !Layout.Reserved.Contains(p))available.Add(p);
            PlaceExtras(index,available,0,rewards);
        }
    }

    // A template's wall light: on the wall its socket faces, or the cell's nearest other wall.
    void SocketLight(DungeonSocket socket,int room)
    {
        if(socket.overridePrefab==null && !DungeonWallLight.Any(WallLights))return;
        var cell=CellOf(socket.transform.position);
        var f=socket.transform.forward;
        var facing=Mathf.Abs(f.x)>=Mathf.Abs(f.z) ? new Vector2Int(f.x>=0?1:-1,0) : new Vector2Int(0,f.z>=0?1:-1);
        var dirs=new[]{facing,new Vector2Int(-facing.y,facing.x),new Vector2Int(facing.y,-facing.x),-facing};
        foreach(var d in dirs)
        {
            var n=cell+d;
            if(Layout.InBounds(n) && Layout.floor[n.x,n.y])continue;
            SpawnTorch(cell,d,room,TorchParent,extraRandom,socket);
            return;
        }
    }
    Transform torchParent;
    Transform TorchParent { get { if(torchParent==null){torchParent=new GameObject("Torches").transform;torchParent.SetParent(transform,false);} return torchParent; } }

    // Breakables and features in the room's free cells, starting after the ones already used.
    // Returns the first cell left unused.
    int PlaceExtras(int index,System.Collections.Generic.List<Vector2Int> available,int cursor,LootSource rewards)
    {
        if(index==MerchantRoom && cursor<available.Count)cursor++; // keep a free cell for the stall
        var breakables=Destructibles;
        if(breakables!=null && breakables.Length>0)
        {
            int min=Mathf.Clamp(data.destructiblesPerRoom.x,0,8),max=Mathf.Clamp(data.destructiblesPerRoom.y,min,8);
            int count=extraRandom.Next(min,max+1);
            var corners=Corners(index);
            for(int i=0;i<count;i++)
            {
                var prefab=DungeonWeightedPrefab.Choose(breakables,extraRandom);
                if(prefab==null)break;
                var d=prefab.GetComponent<DungeonDestructible>();
                if(d!=null && d.placement==DestructiblePlacement.Corner)
                {
                    if(corners.Count==0)continue;
                    int pick=extraRandom.Next(corners.Count);var corner=corners[pick];corners.RemoveAt(pick);
                    SpawnDestructible(prefab,corner.position,corner.rotation,null,extraRandom.Next());
                    continue;
                }
                if(cursor>=available.Count)continue;
                SpawnDestructible(prefab,Cell(available[cursor++]),Quaternion.Euler(0,extraRandom.Next(4)*90,0),rewards,extraRandom.Next());
            }
        }
        if(Roles[index]==RoomRole.Entrance || (Roles[index]==RoomRole.Exit && Milestone!=null))return cursor;
        var feature=PickFeature(index);
        if(feature!=null && feature.placement!=DungeonPropPlacement.Anywhere)
        {
            // Against a wall, in a corner or in the middle, among the cells nothing else has taken.
            var free=available.GetRange(cursor,available.Count-cursor);
            var open=new System.Collections.Generic.HashSet<Vector2Int>(free);
            open.ExceptWith(furnished);
            if(FindSpot(feature.placement,Vector2Int.one,index,free,open,out var spot,out var turn))BackToWall(SpawnFeature(feature.prefab,spot,turn),feature.placement,Vector2Int.one);
        }
        else if(feature!=null && cursor<available.Count)
        {
            var cell=available[cursor++];
            var pos=Cell(cell);
            var toCenter=Cell(Layout.RoomCenter(index))-pos;
            var facing=Mathf.Abs(toCenter.x)>Mathf.Abs(toCenter.z) ? new Vector3(Mathf.Sign(toCenter.x),0,0) : new Vector3(0,0,Mathf.Sign(toCenter.z)==0?1:Mathf.Sign(toCenter.z));
            SpawnFeature(feature.prefab,pos,Quaternion.LookRotation(facing));
        }
        return cursor;
    }

    DungeonFeature PickFeature(int index)
    {
        var features=Features;
        if(features==null)return null;
        var role=Mask(Roles[index]);
        foreach(var f in features)
        {
            if(f==null || f.prefab==null || (f.rooms&role)==0 || FloorInBiome<f.minFloor)continue;
            featureCounts.TryGetValue(f.prefab,out int used);
            if(f.maxPerFloor>0 && used>=f.maxPerFloor)continue;
            if(extraRandom.NextDouble()>=f.chancePerRoom)continue;
            featureCounts[f.prefab]=used+1;
            return f;
        }
        return null;
    }

    // Room corners at ceiling height, pulled into the corner so hanging cobwebs meet both walls
    // and the ceiling. Corner prefabs hang down from their pivot.
    System.Collections.Generic.List<Pose> Corners(int index)
    {
        var list=new System.Collections.Generic.List<Pose>();
        float inset=data.cellSize*.5f-.03f; // walls are flush with the cell edge
        var room=Layout.rooms[index];
        var candidates=new System.Collections.Generic.List<(Vector2Int,int,int)>();
        if(Layout.IsRectangular(index))
            candidates.AddRange(new[]{(new Vector2Int(room.xMin,room.yMin),-1,-1),(new Vector2Int(room.xMax-1,room.yMin),1,-1),(new Vector2Int(room.xMin,room.yMax-1),-1,1),(new Vector2Int(room.xMax-1,room.yMax-1),1,1)});
        else
            // Shaped rooms: every inside corner, including those around pillars.
            foreach(var p in Layout.RoomCells(index))
                foreach(var (sx,sz) in new[]{(-1,-1),(1,-1),(-1,1),(1,1)})
                    if(!Open(p+new Vector2Int(sx,0)) && !Open(p+new Vector2Int(0,sz)))candidates.Add((p,sx,sz));
        foreach(var (cell,sx,sz) in candidates)
        {
            if(Layout.Reserved.Contains(cell))continue;
            var pos=Cell(cell)+new Vector3(sx*inset,HeightAt(cell.x,cell.y)-.02f,sz*inset);
            list.Add(new Pose(pos,Quaternion.LookRotation(new Vector3(-sx,0,-sz))));
        }
        return list;
    }

    DungeonDestructible SpawnDestructible(GameObject prefab,Vector3 pos,Quaternion rotation,LootSource rewardOverride,int seed)
    {
        var go=Instantiate(prefab,pos,rotation,transform);
        var d=go.GetComponent<DungeonDestructible>();
        if(d==null)return null;
        d.loot=First(rewardOverride,Loot(DungeonLootSource.Barrel));
        d.seed=seed;d.floorNumber=FloorNumber;
        return d;
    }

    GameObject SpawnFeature(GameObject prefab,Vector3 pos,Quaternion rotation)
    {
        var go=Instantiate(prefab,pos,rotation,transform);
        int seed=extraRandom.Next();
        var chestLoot=Loot(DungeonLootSource.Chest);
        foreach(var grave in go.GetComponentsInChildren<DungeonGrave>()){grave.seed=seed;grave.floorNumber=FloorNumber;grave.fallbackLoot=chestLoot;}
        foreach(var shelf in go.GetComponentsInChildren<DungeonBookshelf>()){shelf.seed=seed;shelf.floorNumber=FloorNumber;shelf.fallbackLoot=chestLoot;}
        foreach(var altar in go.GetComponentsInChildren<DungeonAltar>())altar.floorNumber=FloorNumber;
        return go;
    }

    Character SpawnEnemyAt(GameObject prefab,Vector3 pos,Quaternion rotation,LootSource rewards)
    {
        if(prefab==null || !NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas))return null;
        var enemy=Instantiate(prefab,hit.position,rotation,transform);
        var character=enemy.GetComponent<Character>();
        data.balance?.Apply(character,FloorNumber);
        var drop=enemy.GetComponent<DungeonLootDrop>();
        if(drop!=null){if(!drop.overrideLevelTable)drop.table=First(rewards,Loot(DungeonLootSource.Enemy));drop.seed=extraRandom.Next();drop.floorNumber=FloorNumber;}
        return character;
    }

    // After navigation exists: enemies, the boss and the merchant at their sockets.
    void SpawnSocketActors(DungeonExit exitStair)
    {
        Character boss=null;
        int exitIndex=System.Array.IndexOf(Roles,RoomRole.Exit);
        foreach(var pair in actorSockets)
        {
            int room=pair.Key;
            foreach(var socket in pair.Value)
            {
                var t=socket.transform;
                switch(socket.type)
                {
                    case DungeonSocketType.Enemy:
                    {
                        var prefab=socket.overridePrefab!=null ? socket.overridePrefab : ChooseEnemy(room,extraRandom);
                        SpawnEnemyAt(prefab,t.position,t.rotation,profiles[room]?.rewards);
                        break;
                    }
                    case DungeonSocketType.Boss:
                        if(Milestone!=null && boss==null && room==exitIndex)boss=SpawnBoss(t.position,t.rotation);
                        break;
                    case DungeonSocketType.Merchant:
                        if(room==MerchantRoom)SpawnMerchant(t.position,t.rotation);
                        break;
                }
            }
        }
        if(Milestone!=null && boss==null && exitIndex>=0)
        {
            var c=Cell(Layout.RoomCenter(exitIndex));
            var toExit=ExitPoint-c;toExit.y=0;
            boss=SpawnBoss(c+(toExit.sqrMagnitude>.01f ? -toExit.normalized*data.cellSize : Vector3.zero),Quaternion.LookRotation(toExit.sqrMagnitude>.01f ? -toExit : Vector3.forward));
        }
        if(boss!=null)
        {
            BossEncounter=gameObject.AddComponent<DungeonBossEncounter>();
            BossEncounter.Initialize(this,Milestone,boss,exitIndex,exitStair);
        }
        if(MerchantRoom>=0 && FindAnyMerchant()==null)
        {
            var lanes=Lanes(MerchantRoom);
            var middle=Cell(Layout.RoomCenter(MerchantRoom));
            foreach(var p in Layout.RoomCells(MerchantRoom))
            {
                if(lanes.Contains(p) || Layout.Reserved.Contains(p) || Blocked(Cell(p)))continue;
                var toCenter=middle-Cell(p);
                SpawnMerchant(Cell(p),Quaternion.LookRotation(toCenter.sqrMagnitude>.01f ? toCenter : Vector3.forward));
                break;
            }
            if(FindAnyMerchant()==null)SpawnMerchant(middle+Vector3.right*data.cellSize*.5f,Quaternion.identity);
        }
    }

    bool Blocked(Vector3 pos)=>Physics.CheckBox(pos+Vector3.up*.6f,new Vector3(.45f,.5f,.45f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
    Merchant FindAnyMerchant()=>GetComponentInChildren<Merchant>(true);
    public Merchant Merchant=>FindAnyMerchant();

    Character SpawnBoss(Vector3 pos,Quaternion rotation)
    {
        var boss=SpawnEnemyAt(Milestone.boss,pos,rotation,Milestone.bossLoot);
        if(boss==null){Debug.LogWarning($"Milestone boss {Milestone.boss.name} could not be placed on navigation.",this);return null;}
        var drop=boss.GetComponent<DungeonLootDrop>();
        if(drop!=null){if(Milestone.bossLoot!=null){drop.table=Milestone.bossLoot;drop.overrideLevelTable=true;}drop.bonusGold+=Milestone.bonusGold;}
        if(boss.Stats!=null && (Milestone.healthMultiplier!=1 || Milestone.damageMultiplier!=1))
        {
            boss.Stats.AddModifier(new StatModifier(StatType.MaxHealth,Milestone.healthMultiplier-1,ModifierType.PercentMultiply,Milestone));
            boss.Stats.AddModifier(new StatModifier(StatType.AttackDamage,Milestone.damageMultiplier-1,ModifierType.PercentMultiply,Milestone));
            boss.Stats.Heal(boss.Stats.MaxHealth);
        }
        return boss;
    }

    void SpawnMerchant(Vector3 pos,Quaternion rotation)
    {
        if(data.merchantPrefab==null || FindAnyMerchant()!=null)return;
        if(NavMesh.SamplePosition(pos,out var hit,1.5f,NavMesh.AllAreas))pos=hit.position;
        var go=Instantiate(data.merchantPrefab,pos,rotation,transform);
        var merchant=go.GetComponentInChildren<Merchant>(true);
        if(merchant==null)return;
        if(merchant.stockTable==null)merchant.stockTable=data.merchantStock!=null ? data.merchantStock : Loot(DungeonLootSource.Chest);
        merchant.seed=extraRandom.Next();merchant.floorNumber=FloorNumber;
    }

    public void ShowCeilings(bool value) { if(ceiling!=null) ceiling.gameObject.SetActive(value); }
    public Transform Ceilings => ceiling;
    // The spawn room has a skylight: the table reveal's camera can fly in through it.
    public bool SpawnRoofOpen { get; private set; }

    // ── Openings ──────────────────────────────────────────────────────

    // An open ceiling cell keeps a frame of ceiling only along its sides that border closed ceiling,
    // so neighbouring open cells join into one skylight. A trim lip runs along the frame's inner edge.
    void Skylight(Vector2Int cell, Vector3 center, Vector3 size, float frame, Material ceilingMaterial, Material trim, System.Func<Vector2Int, bool> open)
    {
        float halfX = size.x * .5f, halfZ = size.z * .5f;
        float fx = Mathf.Min(frame, halfX), fz = Mathf.Min(frame, halfZ);
        bool l = !open(cell + Vector2Int.left), r = !open(cell + Vector2Int.right);
        bool b = !open(cell + Vector2Int.down), f = !open(cell + Vector2Int.up);
        const float lip = .1f, lipHeight = .14f;
        float lipY = center.y - size.y * .5f - lipHeight * .5f + .02f;
        void Strip(Vector3 at, Vector3 scale) => ArchitectureBox("Skylight frame", at, scale, ceilingMaterial, ceiling);
        void Lip(Vector3 at, Vector3 scale)
        {
            var lipBox = Box("Skylight trim", at, scale, trim, ceiling);
            Destroy(lipBox.GetComponent<Collider>());
        }
        if (l) { Strip(center + new Vector3(-halfX + fx * .5f, 0, 0), new Vector3(fx, size.y, size.z)); Lip(new Vector3(center.x - halfX + fx + lip * .5f, lipY, center.z), new Vector3(lip, lipHeight, size.z)); }
        if (r) { Strip(center + new Vector3(halfX - fx * .5f, 0, 0), new Vector3(fx, size.y, size.z)); Lip(new Vector3(center.x + halfX - fx - lip * .5f, lipY, center.z), new Vector3(lip, lipHeight, size.z)); }
        if (b) { Strip(center + new Vector3(0, 0, -halfZ + fz * .5f), new Vector3(size.x, size.y, fz)); Lip(new Vector3(center.x, lipY, center.z - halfZ + fz + lip * .5f), new Vector3(size.x, lipHeight, lip)); }
        if (f) { Strip(center + new Vector3(0, 0, halfZ - fz * .5f), new Vector3(size.x, size.y, fz)); Lip(new Vector3(center.x, lipY, center.z + halfZ - fz - lip * .5f), new Vector3(size.x, lipHeight, lip)); }
        // Inner corners: both sides open but the diagonal cell closed; fill the frame's corner square.
        void Corner(Vector2Int a, Vector2Int c, float sx, float sz)
        {
            if (open(cell + a) && open(cell + c) && !open(cell + a + c))
                Strip(center + new Vector3(sx * (halfX - fx * .5f), 0, sz * (halfZ - fz * .5f)), new Vector3(fx, size.y, fz));
        }
        Corner(Vector2Int.left, Vector2Int.down, -1, -1); Corner(Vector2Int.right, Vector2Int.down, 1, -1);
        Corner(Vector2Int.left, Vector2Int.up, -1, 1); Corner(Vector2Int.right, Vector2Int.up, 1, 1);
    }

    // A window in an outer wall: sill and lintel along the whole cell, jambs where the run of windows
    // ends, and a ledge on the sill. Visual only; the hidden full wall keeps the collision.
    void Window(Vector2Int cell, Vector2Int dir, float height, Material lower, Material upper, Material trim, System.Func<Vector2Int, Vector2Int, bool> windowAt)
    {
        float sill = Mathf.Min(data.windowSill, height * .4f), lintel = Mathf.Min(data.windowLintel, height * .4f);
        GameObject Piece(string name, Vector3 at, Vector3 scale, Material material)
        {
            var go = ArchitectureBox(name, at, scale, material, geometry);
            Destroy(go.GetComponent<Collider>());
            return go;
        }
        if (sill > .01f) { var (c, s) = WallPiece(cell, dir, 0, 0, sill, sill * .5f); Piece("Window sill wall", c, s, lower); }
        { var (c, s) = WallPiece(cell, dir, height - lintel, 0, lintel, height - lintel * .5f); Piece("Window lintel", c, s, upper); }
        var (ledgeCenter, ledgeScale) = WallPiece(cell, dir, sill, TrimDepth * 3, .1f, sill + .05f);
        var ledge = Box("Window ledge", ledgeCenter, ledgeScale, trim, geometry);
        Destroy(ledge.GetComponent<Collider>());
        // Jambs where the neighbouring cell along the wall has no window facing the same way.
        var end = dir.x != 0 ? Vector2Int.up : Vector2Int.right;
        var along = new Vector3(end.x, 0, end.y);
        float openHeight = height - sill - lintel;
        if (openHeight <= .01f) return;
        var (mid, scale) = WallPiece(cell, dir, sill, 0, openHeight, sill + openHeight * .5f);
        float length = dir.x != 0 ? scale.z : scale.x;
        float jamb = Mathf.Min(data.windowJamb, length * .45f);
        foreach (var side in new[] { -1, 1 })
        {
            if (windowAt(cell + end * side, dir)) continue;
            var at = mid + along * side * (length * .5f - jamb * .5f);
            var jambScale = dir.x != 0 ? new Vector3(scale.x, openHeight, jamb) : new Vector3(jamb, openHeight, scale.z);
            Piece("Window jamb", at, jambScale, lower);
        }
    }

    // ── Wall corners ─────────────────────────────────────────────────
    // Wall pieces sit flush with the cell edge on the room side and extend into the rock behind
    // it; trims also stand TrimDepth proud of the wall. Each end of a piece is lengthened or
    // shortened so neighbouring pieces meet exactly, with no overlap and no gap:
    //   inside corner:  the north/south-facing piece runs through the corner, the east/west one
    //                   stops at its face;
    //   outside corner: the north/south-facing piece reaches the corner, the east/west one starts
    //                   behind it.
    // Diagonally touching rooms retain these box colliders, but their visible meshes are
    // mitered by MiterDiagonalEnds so neither room exposes the other room's wall end.
    const float WallThickness = .22f, TrimDepth = .04f;
    const float CeilingWallOverlap = .02f;

    bool OpenCell(Vector2Int p) => Layout.InBounds(p) && Layout.floor[p.x,p.y];

    // Does the cell carry a wall piece facing d at height y?
    bool WallAt(Vector2Int cell, Vector2Int d, float y)
    {
        if (!OpenCell(cell) || y >= HeightAt(cell.x,cell.y) - .01f) return false;
        var n = cell + d;
        return !OpenCell(n) || HeightAt(n.x,n.y) <= y + .01f;
    }

    float EndAdjust(Vector2Int cell, Vector2Int d, Vector2Int end, float y, float proud)
    {
        bool zFacing = d.y != 0;
        bool side = WallAt(cell, end, y);
        // Checkerboard: another room touches this one only at the corner; its wall shares this
        // quarter of rock, so only the north/south-facing piece reaches the corner.
        if (side && WallAt(cell + end + d, -end, y)) return zFacing ? 0 : -WallThickness;
        if (side) return zFacing ? WallThickness : -proud;
        if (!WallAt(cell + end, d, y) && WallAt(cell + end + d, -end, y))
        {
            // An opening's corner (a doorway between a room and a corridor): the room's wall keeps
            // the corner so the corridor wall's end never shows as a strip in the room's wall.
            bool room = IsRoomCell(cell), otherRoom = IsRoomCell(cell + end + d);
            if (room && !otherRoom) return zFacing ? proud : 0;
            if (!room && otherRoom) return -WallThickness;
            return zFacing ? proud : -WallThickness;
        }
        return 0;
    }

    bool IsRoomCell(Vector2Int c) => Layout.InBounds(c) && Layout.floor[c.x, c.y] && Layout.RegionIds[c.x, c.y] < Layout.rooms.Count;

    void MiterDiagonalEnds(GameObject piece, Vector2Int cell, Vector2Int direction, float probeY, float proud, bool tiled)
    {
        var along = direction.x != 0 ? Vector2Int.up : Vector2Int.right;
        float y = probeY + .001f;
        bool Diagonal(Vector2Int end) => WallAt(cell, end, y) && WallAt(cell + end + direction, -end, y);
        bool back = Diagonal(-along), front = Diagonal(along);
        if (!back && !front) return;

        var filter = piece.GetComponent<MeshFilter>();
        // Architecture meshes are shared by size and UV phase; never edit them in place.
        var mesh = Instantiate(filter.sharedMesh);
        mesh.name = "Mitered diagonal wall corner";
        cornerMeshes.Add(mesh);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uv = mesh.uv;
        var scale = piece.transform.localScale;
        var outward = new Vector3(direction.x, 0, direction.y);
        var tangent = new Vector3(along.x, 0, along.y);
        for (int i = 0; i < vertices.Length; i++)
        {
            bool positive = Vector3.Dot(vertices[i], tangent) > 0;
            if (positive ? !front : !back) continue;
            var end = positive ? along : -along;
            float oldEnd = EndAdjust(cell, direction, end, y, proud);
            // Inner edge reaches the room corner (including projecting trim); outer
            // edge retreats into the rock. Opposing pieces meet on a diagonal plane.
            float depth = (Vector3.Dot(vertices[i], outward) + .5f) * (WallThickness + proud);
            float newEnd = proud - depth;
            var shift = new Vector3(end.x, 0, end.y) * (newEnd - oldEnd);
            vertices[i] += new Vector3(shift.x / scale.x, 0, shift.z / scale.z);
            if (tiled)
            {
                var normal = normals[i];
                uv[i] += (Mathf.Abs(normal.y) > .5f ? new Vector2(shift.x, shift.z)
                    : Mathf.Abs(normal.x) > .5f ? new Vector2(shift.z, 0)
                    : new Vector2(shift.x, 0)) / Mathf.Max(.01f, data.architectureTileSize);
            }
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    (Vector3 center, Vector3 scale) WallPiece(Vector2Int cell, Vector2Int d, float probeY, float proud, float height, float centerY)
    {
        var end = d.x != 0 ? Vector2Int.up : Vector2Int.right;
        var along = new Vector3(end.x, 0, end.y);
        var outward = new Vector3(d.x, 0, d.y);
        float y = probeY + .001f;
        float back = EndAdjust(cell, d, -end, y, proud), front = EndAdjust(cell, d, end, y, proud);
        float length = data.cellSize + back + front, thickness = WallThickness + proud;
        var center = Cell(cell) + outward * (data.cellSize * .5f + (WallThickness - proud) * .5f)
            + along * ((front - back) * .5f) + Vector3.up * centerY;
        return (center, d.x != 0 ? new Vector3(thickness, height, length) : new Vector3(length, height, thickness));
    }
    GameObject ArchitectureBox(string name, Vector3 pos, Vector3 size, Material material, Transform parent)
    {
        var go = Box(name, pos, size, material, parent);
        float tile = Mathf.Max(.01f, data.architectureTileSize);
        Vector3 relative = pos - transform.position;
        // Repeat phases are shared across adjacent grid cells, even when grid spacing differs from tile size.
        Vector3 phase = new Vector3(Mathf.Repeat(relative.x, tile), Mathf.Repeat(relative.y, tile), Mathf.Repeat(relative.z, tile));
        phase = new Vector3(Mathf.Round(phase.x*10000)/10000, Mathf.Round(phase.y*10000)/10000, Mathf.Round(phase.z*10000)/10000);
        var key = (size, phase);
        if (!architectureMeshes.TryGetValue(key, out var mesh))
        {
            mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = "Square architectural tile UVs";
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = phase + Vector3.Scale(vertices[i], size);
                var normal = normals[i];
                uv[i] = Mathf.Abs(normal.y) > .5f ? new Vector2(point.x, point.z) / tile
                    : Mathf.Abs(normal.x) > .5f ? new Vector2(point.z, point.y) / tile
                    : new Vector2(point.x, point.y) / tile;
            }
            mesh.uv = uv;
            architectureMeshes.Add(key, mesh);
        }
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        return go;
    }
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
