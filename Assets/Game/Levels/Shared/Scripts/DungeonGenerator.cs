using System;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class DungeonGenerator : MonoBehaviour
{
    public DungeonLayout Layout { get; private set; }
    public Vector3 SpawnPoint { get; private set; }
    public Vector3 ExitPoint { get; private set; }
    public NavMeshSurface Surface { get; private set; }
    TableLevelData data;
    public TableLevelData LevelData => data;
    Transform geometry, ceiling;
    System.Random random;
    public Vector3 Cell(Vector2Int p) => transform.position + new Vector3((p.x-data.width*.5f)*data.cellSize, 0, (p.y-data.depth*.5f)*data.cellSize);

    public void Build(TableLevelData level, int seed)
    {
        data = level; random = new System.Random(seed);
        Layout = new DungeonLayout(level.width, level.depth, level.roomCount, seed);
        geometry = new GameObject("Architecture").transform; geometry.SetParent(transform, false);
        ceiling = new GameObject("Ceilings").transform; ceiling.SetParent(transform, false);
        float size = level.cellSize, height = 3.2f;
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int x = 0; x < level.width; x++) for (int z = 0; z < level.depth; z++)
        {
            if (!Layout.floor[x,z]) continue;
            var pos = Cell(new Vector2Int(x,z));
            Box("Flagstone", pos - Vector3.up*.12f, new Vector3(size,.24f,size), level.floorMaterial, geometry);
            Box("Ceiling", pos + Vector3.up*(height+.12f), new Vector3(size,.24f,size), level.ceilingMaterial, ceiling);
            foreach (var dir in dirs)
            {
                int nx = x+dir.x, nz = z+dir.y;
                if (nx>=0 && nz>=0 && nx<level.width && nz<level.depth && Layout.floor[nx,nz]) continue;
                Vector3 center = pos + new Vector3(dir.x,0,dir.y)*size*.5f;
                Vector3 scale = dir.x != 0 ? new Vector3(.22f,height,size) : new Vector3(size,height,.22f);
                Box("Crypt masonry", center+Vector3.up*height*.5f, scale, level.wallMaterial, geometry);
                scale.y=.15f; scale.x+=.05f; scale.z+=.05f;
                Box("Stone cornice", center+Vector3.up*2.65f, scale, level.trimMaterial, geometry);
                Box("Stone footing", center+Vector3.up*.12f, scale, level.trimMaterial, geometry);
            }
        }
        for (int i=0;i<Layout.rooms.Count;i++) DecorateRoom(Layout.rooms[i],i);
        Physics.SyncTransforms();
        Surface = gameObject.AddComponent<NavMeshSurface>();
        Surface.collectObjects = CollectObjects.Children;
        Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        Surface.overrideVoxelSize = true; Surface.voxelSize = .12f;
        Surface.BuildNavMesh();
        SpawnPoint = Cell(Layout.Start)+Vector3.up*.12f;
        ExitPoint = Cell(Layout.Exit);
        MakeExit(SpawnPoint + Vector3.left*2, true);
        MakeExit(ExitPoint, false);
        // Spawn after navigation exists. The first room is always safe.
        for (int i=1;i<Layout.rooms.Count;i++)
        {
            var room = Layout.rooms[i];
            int count = i%3==0 ? 2 : 1;
            for(int j=0;j<count;j++)
            {
                if (level.enemies == null || level.enemies.Length == 0) break;
                var prefab = level.enemies[random.Next(level.enemies.Length)];
                var pos = Cell(DungeonLayout.Center(room)) + Vector3.right*j*1.7f;
                if (prefab == null || !NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas)) continue;
                var enemy = Instantiate(prefab,hit.position,Quaternion.Euler(0,random.Next(360),0),transform);
                var drop = enemy.GetComponent<DungeonLootDrop>();
                if(drop != null) { drop.table = level.loot; drop.seed = random.Next(); }
            }
        }
        DungeonPickup.Spawn(level.guaranteedHealing, SpawnPoint + Vector3.forward*2, transform);
        gameObject.AddComponent<DungeonHud>().Initialize(this);
    }

    void DecorateRoom(RectInt room,int index)
    {
        Vector3 center = Cell(DungeonLayout.Center(room));
        // Broad pools of warm and cool light, with no torch requirement.
        var lamp = new GameObject("Amber chamber light"); lamp.transform.SetParent(transform,false);
        lamp.transform.position = center+Vector3.up*2.65f;
        var light = lamp.AddComponent<Light>(); light.type=LightType.Point;
        light.color = index%3==0 ? new Color(.62f,.79f,1f) : new Color(1f,.73f,.4f);
        light.range=20; light.intensity=5f; light.shadows=LightShadows.None;
        // Corner piers frame the room without obstructing the routes through its centre.
        foreach (var p in new[] { new Vector2Int(room.x,room.y), new Vector2Int(room.xMax-1,room.yMax-1) })
        {
            var at = Cell(p);
            Box("Square pier",at+Vector3.up*1.5f,new Vector3(.65f,3,.65f),data.trimMaterial,geometry);
            Box("Pier capital",at+Vector3.up*2.8f,new Vector3(.9f,.2f,.9f),data.trimMaterial,geometry);
        }
        Vector3 spot=Cell(new Vector2Int(room.x+1,room.y+1));
        MakeContainer(spot,index%3==0,index==0);
        if(index%2==0) MakeContainer(Cell(new Vector2Int(room.xMax-2,room.yMax-2)),false,false);
        // Wall-side burial slab / storage bench, alternating with scattered floor supplies.
        if(index%2==1)
        {
            Box("Burial plinth",Cell(new Vector2Int(room.x+1,room.yMax-2))+Vector3.up*.35f,
                new Vector3(1.2f,.7f,2.5f),data.trimMaterial,geometry);
            DungeonPickup.Spawn(data.loot?.Roll(random), center+Vector3.right*2,transform);
        }
    }

    void MakeContainer(Vector3 pos,bool chest,bool healing)
    {
        var go=new GameObject(chest ? "Supply chest" : "Breakable barrel"); go.transform.SetParent(transform,false); go.transform.position=pos;
        if(chest)
        {
            Box("Chest",pos+Vector3.up*.43f,new Vector3(1.25f,.85f,.7f),data.woodMaterial,go.transform);
            Box("Lid",pos+Vector3.up*.92f,new Vector3(1.35f,.16f,.78f),data.woodMaterial,go.transform);
            Box("Latch",pos+new Vector3(0,.66f,-.42f),new Vector3(.15f,.3f,.08f),data.metalMaterial,go.transform);
        }
        else
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
        var container=go.AddComponent<DungeonContainer>(); container.chest=chest; container.loot=data.loot;
        container.seed=random.Next(); container.debrisMaterial=data.woodMaterial;
        container.openAudio=chest ? data.chestOpenAudio:data.containerBreakAudio;
        if(healing) container.guaranteedItem=data.guaranteedHealing;
        if(chest)
        {
            var col=go.AddComponent<BoxCollider>(); col.center=Vector3.up*.5f; col.size=new Vector3(1.4f,1,.9f);
            go.AddComponent<InteractableTrigger>();
        }
    }

    void MakeExit(Vector3 pos,bool entrance)
    {
        var go=new GameObject(entrance ? "Entrance stair" : "Exit stair"); go.transform.SetParent(transform,false); go.transform.position=pos;
        for(int i=0;i<5;i++) Box("Step",pos+new Vector3(0,.1f+i*.14f,i*.28f),new Vector3(1.7f,.2f+i*.28f,.3f),data.trimMaterial,go.transform);
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
