using System;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class DungeonGenerator : MonoBehaviour
{
    public DungeonLayout Layout { get; private set; }
    public Vector3 SpawnPoint { get; private set; }
    public Quaternion SpawnRotation { get; private set; }=Quaternion.identity;
    public Vector3 ExitPoint { get; private set; }
    public NavMeshSurface Surface { get; private set; }
    TableLevelData data;
    public TableLevelData LevelData => data;
    Transform geometry, ceiling;
    readonly System.Collections.Generic.Dictionary<(Vector3, Vector3), Mesh> architectureMeshes = new();
    System.Random random;
    public int FloorNumber { get; private set; }=1;
    int floorNumber=>FloorNumber;
    public enum RoomRole { Entrance, Combat, Treasure, Rest, Storage, Exit }
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
    DungeonFloorSettings Settings=>data.Floor(floorNumber);
    DungeonLootTable Loot(DungeonLootSource source)=>source==DungeonLootSource.Enemy ? (Settings?.enemyLoot ?? data.enemyLoot ?? data.loot) : source==DungeonLootSource.Chest ? (Settings?.chestLoot ?? data.chestLoot ?? data.loot) : (Settings?.barrelLoot ?? data.barrelLoot ?? data.loot);

    public Vector3 Cell(Vector2Int p) => transform.position + new Vector3((p.x-data.width*.5f)*data.cellSize, 0, (p.y-data.depth*.5f)*data.cellSize);

    public void Build(TableLevelData level, int seed, int floorNumber=1)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        FloorNumber=floorNumber;
        data = level; random = new System.Random(seed);
        Layout = new DungeonLayout(level.width, level.depth, level.roomCount, seed, level.loopPercent);
        Roles=new RoomRole[Layout.rooms.Count];
        for(int i=0;i<Roles.Length;i++) {
            float encounter=Settings!=null && Settings.overrideEncounters ? Settings.encounterChance:data.encounterChance;
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
        // Rebuild routes, region ownership, reserved walkways and distances around the resized rooms.
        Layout = new DungeonLayout(level.width, level.depth, level.roomCount, seed, level.loopPercent, scales, exitRoom);
        doorways = level.doorPrefab != null && level.doorPercent > 0
            ? Layout.SelectDoorways(level.doorPercent) : new();
        RoomHeightTiles = SelectRoomHeights(Layout.rooms.Count, seed, level.twoTileRoomPercent, level.threeTileRoomPercent);
        SelectCorridorHeights(unchecked(seed + 3571));
        RegionStyles = new DungeonRoomStyle[Layout.RegionCount];
        var styleRandom = new System.Random(unchecked(seed + 15485863));
        for (int i = 0; i < RegionStyles.Length; i++)
            RegionStyles[i] = DungeonRoomStyle.Choose(i < Layout.rooms.Count ? level.roomStyles : level.corridorStyles, styleRandom);
        for (int i=0;i<profiles.Length;i++)
            if (profiles[i] != null && profiles[i].styleOverride != null) RegionStyles[i] = profiles[i].styleOverride;
        if (level.corridorsCopyConnectedRoomStyle) CopyConnectedRoomStyles(seed);
        var openRoofs = Layout.SelectOpenRegions(level.hideRoof ? level.hideRoofPercent : 0, 173);
        var openEdges = Layout.SelectOpenRegions(level.hideEdges ? level.hideEdgesPercent : 0, 419);
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
            ArchitectureBox("Flagstone", pos - Vector3.up*.12f, new Vector3(size,.24f,size), DungeonRoomStyle.Resolve(style != null ? style.floor : null, level.floorMaterial), geometry);
            var roof = ArchitectureBox("Ceiling", pos + Vector3.up*(height+.12f), new Vector3(size,.24f,size), DungeonRoomStyle.Resolve(style != null ? style.ceiling : null, level.ceilingMaterial), ceiling);
            roof.GetComponent<Renderer>().enabled = !openRoofs[room];
            foreach (var dir in dirs)
            {
                int nx = x+dir.x, nz = z+dir.y;
                bool neighbor = nx>=0 && nz>=0 && nx<level.width && nz<level.depth && Layout.floor[nx,nz];
                float bottom = neighbor ? HeightAt(nx,nz) : 0;
                if (bottom >= height) continue;
                Vector3 center = pos + new Vector3(dir.x,0,dir.y)*size*.5f;
                Vector3 scale = dir.x != 0 ? new Vector3(.22f,tile,size) : new Vector3(size,tile,.22f);
                bool visible = !(openEdges[room] && Layout.FacesOutside(new Vector2Int(x,z), dir));
                // Square wall tiles preserve texture density; upper walls seal height changes above passages.
                for (float y = bottom; y < height - .01f; y += tile)
                    ArchitectureBox("Crypt masonry", center+Vector3.up*(y+tile*.5f), scale, WallMaterial(room,y), geometry).GetComponent<Renderer>().enabled = visible;
                if (style == null || !style.hideTrims)
                {
                    scale.y=.15f; scale.x+=.05f; scale.z+=.05f;
                    var trim = DungeonRoomStyle.Resolve(style != null ? style.trim : null, level.trimMaterial);
                    Box("Stone cornice", center+Vector3.up*(height-.55f), scale, trim, geometry).GetComponent<Renderer>().enabled = visible;
                    if (!neighbor) Box("Stone footing", center+Vector3.up*.12f, scale, trim, geometry).GetComponent<Renderer>().enabled = visible;
                }
            }
        }
        for (int i=0;i<Layout.rooms.Count;i++) DecorateRoom(Layout.rooms[i],i);
        var batching=gameObject.AddComponent<DungeonStaticGeometry>();
        batching.Combine(geometry,data.cellSize*8);
        batching.Combine(ceiling,data.cellSize*8);
        Physics.SyncTransforms();
        Surface = gameObject.AddComponent<NavMeshSurface>();
        Surface.collectObjects = CollectObjects.Children;
        Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        Surface.overrideVoxelSize = true; Surface.voxelSize = .12f;
        Surface.BuildNavMesh();
        ValidateNavigation();
        MakeDoors(seed);
        SpawnPoint = Cell(Layout.Start)+Vector3.up*.12f;
        var entranceRoom=Layout.rooms[0];
        float closest=float.MaxValue;
        foreach(var cell in entranceRoom.allPositionsWithin)foreach(var direction in dirs) {
            var outside=cell+direction;
            if(entranceRoom.Contains(outside) || !Layout.floor[outside.x,outside.y])continue;
            var delta=Cell(outside)-Cell(Layout.Start);
            if(delta.sqrMagnitude<closest){closest=delta.sqrMagnitude;SpawnRotation=Quaternion.LookRotation(delta);}
        }
        ExitPoint = Cell(Layout.Exit);
        MakeExit(SpawnPoint + Vector3.left*2, true);
        MakeExit(ExitPoint, false);
        // Spawn after navigation exists. The first room is always safe.
        for (int i=1;i<Layout.rooms.Count;i++)
        {
            var room = Layout.rooms[i];
            if(Roles[i]!=RoomRole.Combat && Roles[i]!=RoomRole.Exit)continue;
            int max=Settings!=null && Settings.overrideEncounters ? Settings.maxEnemiesPerRoom:data.maxEnemiesPerRoom;
            int count=random.Next(1,Mathf.Clamp(max,1,6)+1);
            var choices=profiles[i]!=null && profiles[i].enemies!=null && profiles[i].enemies.Length>0 ? profiles[i].enemies : Settings?.enemies!=null && Settings.enemies.Length>0 ? Settings.enemies:level.enemies;
            for(int j=0;j<count;j++)
            {
                if (choices == null || choices.Length == 0) break;
                var prefab = choices[random.Next(choices.Length)];
                var pos = Cell(DungeonLayout.Center(room)) + Vector3.right*((j%3)-1)*1.1f+Vector3.forward*(j/3)*1.2f;
                if (prefab == null || !NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas)) continue;
                var enemy = Instantiate(prefab,hit.position,Quaternion.Euler(0,random.Next(360),0),transform);
                level.balance?.Apply(enemy.GetComponent<Character>(),floorNumber);
                var drop = enemy.GetComponent<DungeonLootDrop>();
                if(drop != null) { if(!drop.overrideLevelTable)drop.table = profiles[i]?.rewards ?? Loot(DungeonLootSource.Enemy); drop.seed = random.Next(); drop.floorNumber=floorNumber; }
            }
        }
        gameObject.AddComponent<DungeonHud>().Initialize(this);
        watch.Stop();GenerationMilliseconds=watch.Elapsed.TotalMilliseconds;
        Debug.Log($"Dungeon seed {seed}, floor {floorNumber}: {Layout.rooms.Count} rooms, {Layout.Connections.Count} connections, generated in {GenerationMilliseconds:F0} ms.",this);
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
    }

    DungeonRoomProfile ChooseProfile(RoomRole role)
    {
        if(data.roomProfiles==null)return null;
        float total=0;
        bool Eligible(DungeonRoomProfile p)=>p!=null && p.role==role && p.weight>0 && floorNumber>=p.minFloor && (p.maxFloor<=0||floorNumber<=p.maxFloor);
        foreach(var profile in data.roomProfiles)if(Eligible(profile))total+=profile.weight;
        double roll=random.NextDouble()*total;
        foreach(var profile in data.roomProfiles)if(Eligible(profile)){roll-=profile.weight;if(roll<0)return profile;}
        return null;
    }

    void MakeDoors(int seed)
    {
        if(data.doorPrefab==null || data.doorPercent<=0)return;
        foreach(var doorway in doorways) {
            var p=doorway.roomCell;
            var dir=doorway.direction;
            var n=p+dir;
            var pos=(Cell(p)+Cell(n))*.5f;
            var door=Instantiate(data.doorPrefab,pos,Quaternion.LookRotation(new Vector3(dir.x,0,dir.y)),transform);
            // Cell width changes the span, not the doorway height or ceiling clearance.
            door.transform.localScale=new Vector3(data.cellSize/2f,1,1);
            door.GetComponent<DungeonDoor>()?.FitCeiling(Mathf.Min(HeightAt(p.x,p.y), HeightAt(n.x,n.y)), data.architectureTileSize);
            var gate = door.GetComponent<DungeonDoor>();
            if (gate != null && gate.lintel != null)
            {
                // Split stationary lintel at tile boundaries so tall doors use the correct upper material.
                var renderer = gate.lintel.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
                float top = Mathf.Min(HeightAt(p.x,p.y), HeightAt(n.x,n.y));
                float bottom = Mathf.Min(gate.openingHeight, top) - .025f;
                for (float y = bottom; y < top + .025f;)
                {
                    float end = Mathf.Min(top + .025f, (Mathf.Floor(y/data.architectureTileSize)+1)*data.architectureTileSize);
                    var span = dir.x != 0 ? new Vector3(.24f,end-y,data.cellSize) : new Vector3(data.cellSize,end-y,.24f);
                    var lintel = ArchitectureBox("Styled door lintel", pos+Vector3.up*((y+end)*.5f), span,
                        WallMaterial(Layout.RegionIds[p.x,p.y],y), transform);
                    Destroy(lintel.GetComponent<Collider>()); // Original fitted lintel retains collision.
                    y = end;
                }
            }
        }
    }

    void DecorateRoom(RectInt room,int index)
    {
        Vector3 center = Cell(DungeonLayout.Center(room));
        // Broad pools of warm and cool light, with no torch requirement.
        var lamp = new GameObject("Amber chamber light"); lamp.transform.SetParent(transform,false);
        lamp.transform.position = center+Vector3.up*Mathf.Min(2.65f, data.architectureTileSize-.35f);
        var light = lamp.AddComponent<Light>(); light.type=LightType.Point;
        var lighting = data.DungeonLighting(floorNumber);
        light.color = index%3==0 ? lighting.top : lighting.light;
        var profile=profiles[index];
        light.range=profile!=null && profile.overrideLight?profile.lightRange:20;
        light.intensity=profile!=null && profile.overrideLight?profile.lightIntensity:5;
        if(profile!=null && profile.overrideLight)light.color=profile.lightColor;
        light.shadows=LightShadows.None;
        // Only decorate cells outside the reserved doorway-to-centre routes.
        var available=new System.Collections.Generic.List<Vector2Int>();
        foreach(var p in room.allPositionsWithin)if(!Layout.Reserved.Contains(p))available.Add(p);
        for(int i=available.Count-1;i>0;i--){int j=random.Next(i+1);(available[i],available[j])=(available[j],available[i]);}
        int cursor=0;
        bool Spot(out Vector3 pos) {pos=default;if(cursor>=available.Count)return false;pos=Cell(available[cursor++]);return true;}
        if(Spot(out var supply))MakeContainer(supply,Roles[index]==RoomRole.Treasure,Roles[index]==RoomRole.Rest,profile?.rewards);
        if(Roles[index]==RoomRole.Storage && Spot(out var extra))MakeContainer(extra,false,false,profile?.rewards);
        int min=profile!=null?Mathf.Clamp(profile.minProps,0,8):1;
        int max=profile!=null?Mathf.Clamp(profile.maxProps,min,8):3;
        int props=random.Next(min,max+1);
        var prefabs=profile!=null && profile.props!=null && profile.props.Length>0?profile.props:data.roomPropPrefabs;
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
        foreach(var room in Layout.rooms) {
            if(!NavMesh.SamplePosition(Cell(DungeonLayout.Center(room)),out var target,1f,NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position,target.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException($"Furnished dungeon has an unreachable room. Seed {Layout.seed}; room {room}. Check prop collider sizes.");
        }
    }

    void MakeContainer(Vector3 pos,bool chest,bool healing,DungeonLootTable rewardOverride=null)
    {
        bool authoredChest=chest && data.chestPrefab!=null;
        var go=authoredChest ? Instantiate(data.chestPrefab,transform) : new GameObject(chest ? "Supply chest" : "Breakable barrel"); go.transform.SetParent(transform,false); go.transform.position=pos;
        if(chest && !authoredChest)
        {
            Box("Chest",pos+Vector3.up*.43f,new Vector3(1.25f,.85f,.7f),data.woodMaterial,go.transform);
            Box("Lid",pos+Vector3.up*.92f,new Vector3(1.35f,.16f,.78f),data.woodMaterial,go.transform);
            Box("Latch",pos+new Vector3(0,.66f,-.42f),new Vector3(.15f,.3f,.08f),data.metalMaterial,go.transform);
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
        var container=go.GetComponent<DungeonContainer>() ?? go.AddComponent<DungeonContainer>(); container.chest=chest; container.loot=rewardOverride ?? Loot(chest?DungeonLootSource.Chest:DungeonLootSource.Barrel); container.floorNumber=floorNumber;
        container.seed=random.Next(); container.debrisMaterial=data.woodMaterial;
        container.openAudio=chest ? data.chestOpenAudio:data.containerBreakAudio;
        if(healing) container.guaranteedItem=data.guaranteedHealing;
        if(chest && !authoredChest)
        {
            var col=go.AddComponent<BoxCollider>(); col.center=Vector3.up*.5f; col.size=new Vector3(1.4f,1,.9f);
            go.AddComponent<InteractableTrigger>();
        }
    }

    void MakeExit(Vector3 pos,bool entrance)
    {
        bool descending=!entrance && data.multipleLevels && floorNumber<Mathf.Max(1,data.levelCount);
        var go=new GameObject(entrance ? "Entrance stair" : descending ? "Stairs down" : "Final exit stair"); go.transform.SetParent(transform,false); go.transform.position=pos;
        for(int i=0;i<5;i++) {
            int step=descending ? 4-i:i;
            Box("Step",pos+new Vector3(0,.1f+step*.14f,i*.28f),new Vector3(1.7f,.2f+step*.28f,.3f),data.trimMaterial,go.transform);
        }
        var gate=Box("Exit marker",pos+new Vector3(0,1.8f,1.5f),new Vector3(1.6f,2.8f,.15f),data.metalMaterial,go.transform);
        var light=gate.AddComponent<Light>(); light.type=LightType.Point; light.color=entrance ? new Color(.5f,.7f,1):new Color(.4f,1,.7f); light.range=5;light.intensity=3;
        var exit=go.AddComponent<DungeonExit>(); exit.entrance=entrance;
        var col=go.AddComponent<BoxCollider>(); col.center=new Vector3(0,1,0); col.size=new Vector3(2,2,1.8f);col.isTrigger=true;
        go.AddComponent<InteractableTrigger>();
    }

    public void ShowCeilings(bool value) { if(ceiling!=null) ceiling.gameObject.SetActive(value); }
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
    }
    public static GameObject Box(string name,Vector3 pos,Vector3 size,Material material,Transform parent)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,true);
        go.transform.position=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        return go;
    }
}
