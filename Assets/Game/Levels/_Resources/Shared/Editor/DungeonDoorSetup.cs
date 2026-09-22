using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class DungeonDoorSetup
{
    [InitializeOnLoadMethod]
    static void Schedule()=>EditorApplication.delayCall+=Ensure;
    [MenuItem("Tools/Table Levels/Create dungeon door prefab")]
    public static void Ensure(){
        const string path="Assets/Game/Levels/Dungeon1/Prefabs/Dungeon door.prefab";
        var level=AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        if(level==null || EditorApplication.isPlaying || File.Exists(path))return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var root=new GameObject("Dungeon door");root.SetActive(false);
        try{
            var door=root.AddComponent<DungeonDoor>();
            var leaf=GameObject.CreatePrimitive(PrimitiveType.Cube);leaf.name="Wooden gate";leaf.transform.SetParent(root.transform,false);
            leaf.transform.localPosition=new Vector3(0,1.4f,0);leaf.transform.localScale=new Vector3(1.92f,2.8f,.18f);
            leaf.GetComponent<Renderer>().sharedMaterial=level.woodMaterial;
            Object.DestroyImmediate(leaf.GetComponent<Collider>());
            door.leaf=leaf.transform;
            var barrier=root.AddComponent<BoxCollider>();barrier.center=new Vector3(0,1.4f,0);barrier.size=new Vector3(1.92f,2.8f,.18f);door.barrier=barrier;
            var obstacle=root.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=barrier.center;obstacle.size=barrier.size;obstacle.carving=true;door.obstacle=obstacle;
            var target=new GameObject("Interaction");target.transform.SetParent(root.transform,false);
            var trigger=target.AddComponent<BoxCollider>();trigger.center=barrier.center;trigger.size=new Vector3(1.95f,2.8f,.3f);trigger.isTrigger=true;
            target.AddComponent<InteractableTrigger>();
            root.SetActive(true);
            level.doorPrefab=PrefabUtility.SaveAsPrefabAsset(root,path);EditorUtility.SetDirty(level);AssetDatabase.SaveAssets();
        }finally{Object.DestroyImmediate(root);}
    }
}
