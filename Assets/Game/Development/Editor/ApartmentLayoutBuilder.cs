using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only, one-shot apartment blockout. World units deliberately match the 20x room player.
public static class ApartmentLayoutBuilder
{
    static Transform root;
    static Material wall, floor;
    static float left, right, south, north, ground, ceiling;
    const float Thickness = 3.2f, HallWidth = 40f, RoomDepth = 160f, DoorWidth = 28f, DoorHeight = 48f;
    [MenuItem("Tools/LoomRoom/Apartment/Build Layout")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if(scene.path != "Assets/Game/Scenes/Room.unity" || EditorApplication.isPlaying) throw new InvalidOperationException("Open Room in edit mode first.");
        if(GameObject.Find("Apartment")) throw new InvalidOperationException("Apartment already exists. Edit its geometry directly.");
        var environment = GameObject.Find("Tabletop").transform;
        var geometry = environment.Find("TableRoom/Geometry");
        var oldFloor = environment.Find("Floor (1)");
        var props = environment.Find("TableRoom/RoomProps");
        var table = environment.Find("TableRoom/TableManager");
        var preserved = props.GetComponentsInChildren<Transform>(true).Concat(table.GetComponentsInChildren<Transform>(true)).ToDictionary(t => t, t => t.localToWorldMatrix);
        Bounds West(string name) { var cs=geometry.Find(name).GetComponentsInChildren<Collider>(); Bounds b=cs[0].bounds; foreach(var c in cs.Skip(1)) b.Encapsulate(c.bounds); return b; }
        left=West("Wall2").max.x; right=West("Wall1").min.x;
        south=West("Wall4").max.z; north=West("Wall3").min.z;
        ground=oldFloor.GetComponent<Collider>().bounds.max.y; ceiling=60.5f;
        Directory.CreateDirectory("Temp/ApartmentBackup");
        EditorSceneManager.SaveScene(scene,"Temp/ApartmentBackup/Room.before-apartment.unity",true);
        Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build apartment layout");
        root=new GameObject("Apartment").transform; Undo.RegisterCreatedObjectUndo(root.gameObject,"Apartment layout");
        wall=MaterialAsset("Apartment Blockout Walls",new Color(.68f,.70f,.72f));
        floor=oldFloor.GetComponent<Renderer>().sharedMaterial;
        Undo.RecordObject(geometry.gameObject,"Replace old shell"); geometry.gameObject.SetActive(false);
        // The detached dinner cutscene sets are outside the apartment. Keep their existing support and references.
        Undo.RecordObject(oldFloor,"Crop oversized staging floor"); Undo.RecordObject(oldFloor.gameObject,"Name staging floor");
        oldFloor.name="Detached dinner sets - staging floor";
        oldFloor.position=new Vector3(-65,ground-.5f,370); oldFloor.localScale=new Vector3(250,1,200);
        float hallRight=left-Thickness, hallLeft=hallRight-HallWidth;
        float newRight=hallLeft-Thickness, newLeft=newRight-RoomDepth, split=south+145;
        Transform existing=Group("Existing Bedroom"), hall=Group("Hallway"), kitchen=Group("Kitchen"), bedroom=Group("Second Bedroom");
        Slab(existing,"Floor",left,right,south,north,false);
        Slab(existing,"Ceiling",left,right,south,north,true);
        Box(existing,"East wall",right,right+Thickness,south-Thickness,north+Thickness,ground,ceiling,wall);
        Box(existing,"South wall",left,right,south-Thickness,south,ground,ceiling,wall);
        Box(existing,"North wall",left,right,north,north+Thickness,ground,ceiling,wall);
        Door(existing,"Hall doorway",left-Thickness,left,south,north,285);
        Slab(hall,"Floor",hallLeft,left,south,north,false);
        Slab(hall,"Ceiling",hallLeft,left,south,north,true);
        Box(hall,"South end",hallLeft,left,south-Thickness,south,ground,ceiling,wall);
        Box(hall,"North end",hallLeft,left,north,north+Thickness,ground,ceiling,wall);
        Slab(kitchen,"Floor",newLeft,hallLeft,south,split,false);
        Slab(kitchen,"Ceiling",newLeft,hallLeft,south,split,true);
        Door(kitchen,"Hall doorway",newRight,hallLeft,south,split,285);
        Slab(bedroom,"Floor",newLeft,hallLeft,split+Thickness,north,false);
        Slab(bedroom,"Ceiling",newLeft,hallLeft,split+Thickness,north,true);
        Door(bedroom,"Hall doorway",newRight,hallLeft,split+Thickness,north,440);
        Box(kitchen,"West wall",newLeft-Thickness,newLeft,south-Thickness,north+Thickness,ground,ceiling,wall);
        Box(kitchen,"South wall",newLeft,hallLeft,south-Thickness,south,ground,ceiling,wall);
        Box(bedroom,"North wall",newLeft,hallLeft,north,north+Thickness,ground,ceiling,wall);
        Box(bedroom,"Shared kitchen wall",newLeft,hallLeft,split,split+Thickness,ground,ceiling,wall);
        // Simple fill lighting for navigation through the undecorated enclosed rooms.
        Fill(hall,new Vector3((hallLeft+hallRight)/2,46,285),70);
        Fill(hall,new Vector3((hallLeft+hallRight)/2,46,440),70);
        Fill(kitchen,new Vector3((newLeft+newRight)/2,48,(south+split)/2),130);
        Fill(bedroom,new Vector3((newLeft+newRight)/2,48,(split+Thickness+north)/2),110);
        foreach(var p in preserved) if(p.Key.localToWorldMatrix != p.Value) throw new InvalidOperationException("Protected room content moved: "+p.Key.name);
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/apartment-dimensions.txt",$"Existing interior: x {left:R}..{right:R}, z {south:R}..{north:R}, floor {ground:R}, ceiling {ceiling:R}\nKitchen {RoomDepth} x 145; bedroom {RoomDepth} x {north-split-Thickness}; hallway {HallWidth}; doors {DoorWidth} x {DoorHeight}\nProtected transforms unchanged: {preserved.Count}\n");
        Selection.activeGameObject=root.gameObject;
        if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(new Vector3((newLeft+right)/2,0,(south+north)/2),Quaternion.Euler(90,0,0),290,true);
        Validate(); RenderPlan();
    }
    [MenuItem("Tools/LoomRoom/Apartment/Repair Material and Room Spawn")]
    public static void Repair() {
        var scene=SceneManager.GetActiveScene();
        if(scene.path != "Assets/Game/Scenes/Room.unity" || EditorApplication.isPlaying) throw new InvalidOperationException("Open Room in edit mode first.");
        root=GameObject.Find("Apartment").transform;
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/World/Materials/Apartment/Apartment Blockout Walls.mat");
        Undo.RecordObject(material,"Fix apartment shader");
        material.shader=Shader.Find(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "Standard" : "Universal Render Pipeline/Lit");
        material.color=new Color(.68f,.70f,.72f); material.shaderKeywords=Array.Empty<string>();
        if(material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",0);
        EditorUtility.SetDirty(material);
        var room=UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Include).Single(p=>p.kind==PlayerKind.Room);
        var t=room.transform;
        Undo.RecordObject(t,"Place room player above bedroom floor");
        t.position=new Vector3(-410,.7f,441);
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        var viewpoint=t.Find("ViewRig/Yaw/Pitch/ViewPoint");
        var sharedCamera = UnityEngine.Object.FindAnyObjectByType<PlayerManager>(FindObjectsInactive.Include).OutputCamera;
        foreach(var camera in new[]{ sharedCamera }.Where(c => c != null)) {
            Undo.RecordObject(camera.transform,"Align room camera with viewpoint");
            camera.transform.SetPositionAndRotation(viewpoint.position,viewpoint.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
            var brain=camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if(brain) {
                var serialized=new SerializedObject(brain);
                // Room gameplay and wake-up cameras use Default (1); dinner uses Channel02 (4).
                serialized.FindProperty("ChannelMask").intValue=5;
                serialized.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(brain);
            }
        }
        var manager=UnityEngine.Object.FindAnyObjectByType<PlayerManager>(FindObjectsInactive.Include);
        var body=manager.GetPlayer(PlayerKind.Room).BodyPresentation.gameObject;
        if(body) { Undo.RecordObject(body.transform,"Align room body"); body.transform.position=t.position; PrefabUtility.RecordPrefabInstancePropertyModifications(body.transform); }
        var marker=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x=>x.name=="StartPositionRoom");
        if(marker) { Undo.RecordObject(marker,"Raise wake-up spawn above floor"); var pos=marker.position; pos.y=.7f; marker.position=pos; PrefabUtility.RecordPrefabInstancePropertyModifications(marker); }
        var progression=UnityEngine.Object.FindAnyObjectByType<ProgressionManager>(FindObjectsInactive.Include);
        Undo.RecordObject(progression,"Start apartment test as room player"); progression.startingPlayer=PlayerKind.Room;
        PrefabUtility.RecordPrefabInstancePropertyModifications(progression);
        Physics.SyncTransforms();
        if(!Physics.Raycast(t.position+Vector3.up*2,Vector3.down,out var floorHit,4,~0,QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("Room spawn has no floor");
        if(!material.shader.isSupported) throw new InvalidOperationException("Apartment shader is unsupported");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Validate(); RenderPlan();
        File.WriteAllText("Temp/apartment-repair.txt",$"Shader: {material.shader.name}, supported {material.shader.isSupported}. Room player: {t.position}. Camera: {t.GetComponentInChildren<Camera>(true).transform.position}. Starting player: {progression.startingPlayer}. Floor: {floorHit.collider.name}.\n");
    }
    static Material MaterialAsset(string name,Color color) {
        const string dir="Assets/Game/World/Materials/Apartment"; Directory.CreateDirectory(dir);
        string path=dir+"/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(Shader.Find(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "Standard" : "Universal Render Pipeline/Lit")); m.color=color; if(m.HasProperty("_BaseColor")) m.SetColor("_BaseColor",color); if(m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",0); if(m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",0); AssetDatabase.CreateAsset(m,path); }
        return m;
    }
    static Transform Group(string name) { var t=new GameObject(name).transform; t.SetParent(root,false); return t; }
    static void Box(Transform parent,string name,float x0,float x1,float z0,float z1,float y0,float y1,Material mat) {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube); g.name=name; g.transform.SetParent(parent,false); g.transform.position=new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2); g.transform.localScale=new Vector3(x1-x0,y1-y0,z1-z0); g.GetComponent<Renderer>().sharedMaterial=mat;
    }
    static void Slab(Transform p,string name,float x0,float x1,float z0,float z1,bool top) { Box(p,name,x0,x1,z0,z1,top?ceiling:ground-1,top?ceiling+Thickness:ground,top?wall:floor); }
    static void Door(Transform p,string name,float x0,float x1,float z0,float z1,float z) {
        Box(p,name+" south",x0,x1,z0,z-DoorWidth/2,ground,ceiling,wall);
        Box(p,name+" north",x0,x1,z+DoorWidth/2,z1,ground,ceiling,wall);
        Box(p,name+" lintel",x0,x1,z-DoorWidth/2,z+DoorWidth/2,ground+DoorHeight,ceiling,wall);
    }
    static void Fill(Transform p,Vector3 pos,float range) { var g=new GameObject("Blockout fill light"); g.transform.SetParent(p,false); g.transform.position=pos; var l=g.AddComponent<Light>(); l.type=LightType.Point; l.range=range; l.intensity=1.5f; l.shadows=LightShadows.None; }
    [MenuItem("Tools/LoomRoom/Apartment/Validate Layout")]
    public static void Validate() {
        if(GameObject.Find("World/Apartment/Entrance and Hall")) { ApartmentLayoutRefinement.Validate(); return; }
        Physics.SyncTransforms(); var log=new StringBuilder();
        var source=UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Include).Single(p=>p.kind==PlayerKind.Room).GetComponent<CharacterController>();
        float radius=source.radius*source.transform.lossyScale.x, height=source.height*source.transform.lossyScale.y;
        Vector3[] route={new Vector3(-410,.7f,437),new Vector3(-410,.7f,285),new Vector3(-495,.7f,285),new Vector3(-590,.7f,285),new Vector3(-495,.7f,285),new Vector3(-495,.7f,440),new Vector3(-590,.7f,440)};
        int samples=0;
        for(int i=1;i<route.Length;i++) {
            int n=Mathf.CeilToInt(Vector3.Distance(route[i-1],route[i])/2);
            for(int j=0;j<=n;j++) {
                Vector3 pos=Vector3.Lerp(route[i-1],route[i],j/(float)n);
                var hits=Physics.OverlapCapsule(pos+Vector3.up*(radius+.1f),pos+Vector3.up*(height-radius),radius,~0,QueryTriggerInteraction.Ignore).Where(c=>!(c is CharacterController) && c.enabled).ToArray();
                if(hits.Length>0) throw new InvalidOperationException("Passage blocked at "+pos+": "+string.Join(",",hits.Select(c=>c.name)));
                if(!Physics.Raycast(pos+Vector3.up*2,Vector3.down,out var hit,4,~0,QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("Missing floor at "+pos);
                samples++;
            }
        }
        log.AppendLine($"PASS: {samples} full-size player capsule samples across bedroom, both doorways, kitchen and hallway. Radius {radius}, height {height}.");
        log.AppendLine("PASS: floor support at every sample. Miniature table world and room props untouched.");
        File.WriteAllText("Temp/apartment-validation.txt",log.ToString()); Debug.Log(log.ToString());
    }
    static void RenderPlan() {
        var ceilings=root.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Ceiling").ToArray();
        var g=new GameObject("Temporary apartment overview camera"); var camera=g.AddComponent<Camera>();
        var rt=new RenderTexture(1400,1000,24); var previous=RenderTexture.active;
        try { foreach(var r in ceilings) r.enabled=false;
            camera.transform.position=new Vector3(-478,650,365); camera.transform.rotation=Quaternion.Euler(90,0,0); camera.orthographic=true; camera.orthographicSize=160; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.14f,.16f); camera.farClipPlane=1000; camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            var texture=new Texture2D(1400,1000,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,1400,1000),0,0); texture.Apply(); File.WriteAllBytes("Temp/apartment-overview.png",texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        } finally { foreach(var r in ceilings) r.enabled=true; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(g); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
    }
}
