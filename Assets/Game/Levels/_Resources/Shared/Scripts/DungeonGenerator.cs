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
    System.Random random;
    public int FloorNumber { get; private set; }=1;
    int floorNumber=>FloorNumber;
    public enum RoomRole { Entrance, Combat, Treasure, Rest, Storage, Exit }
    public RoomRole[] Roles { get; private set; }
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
        var openRoofs = Layout.SelectOpenRegions(level.hideRoof ? level.hideRoofPercent : 0, 173);
        var openEdges = Layout.SelectOpenRegions(level.hideEdges ? level.hideEdgesPercent : 0, 419);
        geometry = new GameObject("Architecture").transform; geometry.SetParent(transform, false);
        ceiling = new GameObject("Ceilings").transform; ceiling.SetParent(transform, false);
        float size = level.cellSize, height = 3.2f;
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int x = 0; x < level.width; x++) for (int z = 0; z < level.depth; z++)
        {
            if (!Layout.floor[x,z]) continue;
            int room = Layout.RegionIds[x,z];
            var pos = Cell(new Vector2Int(x,z));
            Box("Flagstone", pos - Vector3.up*.12f, new Vector3(size,.24f,size), level.floorMaterial, geometry);
            var roof = Box("Ceiling", pos + Vector3.up*(height+.12f), new Vector3(size,.24f,size), level.ceilingMaterial, ceiling);
            roof.GetComponent<Renderer>().enabled = !openRoofs[room];
            foreach (var dir in dirs)
            {
                int nx = x+dir.x, nz = z+dir.y;
                if (nx>=0 && nz>=0 && nx<level.width && nz<level.depth && Layout.floor[nx,nz]) continue;
                Vector3 center = pos + new Vector3(dir.x,0,dir.y)*size*.5f;
                Vector3 scale = dir.x != 0 ? new Vector3(.22f,height,size) : new Vector3(size,height,.22f);
                bool visible = !(openEdges[room] && Layout.FacesOutside(new Vector2Int(x,z), dir));
                Box("Crypt masonry", center+Vector3.up*height*.5f, scale, level.wallMaterial, geometry).GetComponent<Renderer>().enabled = visible;
                scale.y=.15f; scale.x+=.05f; scale.z+=.05f;
                Box("Stone cornice", center+Vector3.up*2.65f, scale, level.trimMaterial, geometry).GetComponent<Renderer>().enabled = visible;
                Box("Stone footing", center+Vector3.up*.12f, scale, level.trimMaterial, geometry).GetComponent<Renderer>().enabled = visible;
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
        foreach(var doorway in Layout.SelectDoorways(data.doorPercent)) {
            var p=doorway.roomCell;
            var dir=doorway.direction;
            var n=p+dir;
            var pos=(Cell(p)+Cell(n))*.5f;
            var door=Instantiate(data.doorPrefab,pos,Quaternion.LookRotation(new Vector3(dir.x,0,dir.y)),transform);
            // Cell width changes the span, not the doorway height or ceiling clearance.
            door.transform.localScale=new Vector3(data.cellSize/2f,1,1);
            door.GetComponent<DungeonDoor>()?.FitCeiling(3.2f);
        }
    }

    void DecorateRoom(RectInt room,int index)
    {
        Vector3 center = Cell(DungeonLayout.Center(room));
        // Broad pools of warm and cool light, with no torch requirement.
        var lamp = new GameObject("Amber chamber light"); lamp.transform.SetParent(transform,false);
        lamp.transform.position = center+Vector3.up*2.65f;
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
    public static GameObject Box(string name,Vector3 pos,Vector3 size,Material material,Transform parent)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,true);
        go.transform.position=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        return go;
    }
}
