using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// All dimensions are authored in world units: 20 units = one room-character metre.
public static class ApartmentLayoutRefinement
{
    const float T=3.2f, Y=.5f, Top=60.5f;
    const float L=-471.8667f, R=-279.568481f, S=238.529663f, N=491.308929f;
    const float HR=L-T, HL=HR-40, KR=HL-T, KL=KR-160, Split=S+170, VX=HL-32;
    static Material plaster, kitchenPlaster, trim, wood, stone, glass, oak;
    static Transform root;

    [MenuItem("Tools/LoomRoom/Apartment/Refine Apartment Layout")]
    public static void Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/Room.unity" || EditorApplication.isPlaying) throw new InvalidOperationException("Open Room in edit mode.");
        root=GameObject.Find("Apartment Layout").transform;
        if(root.Find("Entrance and Hall")) throw new InvalidOperationException("Refinement already applied; edit the authored geometry directly.");
        Directory.CreateDirectory("Temp/ApartmentBackup");
        EditorSceneManager.SaveScene(scene,"Temp/ApartmentBackup/Room.before-refinement.unity",true);
        // Snapshot every transform outside the three shell groups being replaced.
        var old=new[]{root.Find("Hallway"),root.Find("Kitchen"),root.Find("Second Bedroom")};
        var protectedTransforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
            .Where(t=>!old.Any(o=>t==o || t.IsChildOf(o))).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        int undo=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Refine apartment architecture");
        wood=root.Find("Existing Bedroom/Floor").GetComponent<Renderer>().sharedMaterial;
        plaster=Mat("Hall warm plaster",new Color(.72f,.69f,.62f));
        kitchenPlaster=Mat("Kitchen warm white",new Color(.79f,.77f,.70f));
        trim=Mat("Architectural trim",new Color(.27f,.30f,.28f));
        stone=Mat("Kitchen limestone",new Color(.52f,.51f,.44f));
        oak=Mat("Entrance oak",new Color(.26f,.17f,.10f));
        glass=Mat("Frosted window glass",new Color(.52f,.66f,.72f));
        glass.EnableKeyword("_EMISSION"); glass.SetColor("_EmissionColor",new Color(.13f,.18f,.21f)); EditorUtility.SetDirty(glass);
        foreach(var t in old) Undo.DestroyObjectImmediate(t.gameObject);
        var hall=Group("Entrance and Hall"); var kitchen=Group("Kitchen and Dining"); var bedroom=Group("Second Bedroom");
        // Hall: a wider arrival space, then a narrower gallery to the private room.
        Slab(hall,"Floor gallery",HL,L,S-40,N,wood);
        Slab(hall,"Floor entrance recess",VX,HL,S-40,S+30,stone);
        Ceiling(hall,HL,L,S-40,N); Ceiling(hall,VX,HL,S-40,S+30);
        WallZ(hall,"Entrance east return",L,L+T,S-40,S,plaster);
        WallZ(hall,"Entrance west return",VX-T,VX,S-40,S+30,plaster);
        WallX(hall,"Entrance recess head",VX,HL,S+30,S+30+T,plaster);
        WallX(hall,"Hall north end",HL,L,N,N+T,plaster);
        // Closed exterior entrance is an architectural boundary, not an unexplained open edge.
        float entryX=(VX+HR)/2;
        WallX(hall,"Entry wall west",VX-T,entryX-14,S-40-T,S-40,plaster);
        WallX(hall,"Entry wall east",entryX+14,L+T,S-40-T,S-40,plaster);
        Box(hall,"Entry lintel",entryX-14,entryX+14,S-40-T,S-40,Y+48,Top,plaster);
        Box(hall,"Apartment entrance - closed exterior door",entryX-14,entryX+14,S-40-T/2,S-40-T/2+1,Y,Y+48,oak);
        Box(hall,"Entry frame left",entryX-15.2f,entryX-14,S-40-.5f,S-40+1,Y,Y+49.2f,trim,false);
        Box(hall,"Entry frame right",entryX+14,entryX+15.2f,S-40-.5f,S-40+1,Y,Y+49.2f,trim,false);
        Box(hall,"Entry frame head",entryX-15.2f,entryX+15.2f,S-40-.5f,S-40+1,Y+48,Y+49.2f,trim,false);
        Box(hall,"Entry pull",entryX+10.5f,entryX+11.2f,S-39,S-37.8f,22,28,trim,false);
        // A kitchen with a shallow west dining bay and a cut-away corner at the entrance.
        Slab(kitchen,"Floor cooking end",KL,VX-T,S,S+33.2f,stone);
        Slab(kitchen,"Floor main",KL,KR,S+33.2f,Split,stone);
        Slab(kitchen,"Floor dining bay",KL-26,KL,S+40,S+130,stone);
        Slab(kitchen,"Floor doorway threshold",KR,HL,S+63,S+107,stone);
        Ceiling(kitchen,KL,VX-T,S,S+33.2f); Ceiling(kitchen,KL,KR,S+33.2f,Split); Ceiling(kitchen,KL-26,KL,S+40,S+130);
        WallX(kitchen,"Kitchen south",KL-T,VX-T,S-T,S,kitchenPlaster);
        WallZ(kitchen,"Kitchen west south",KL-T,KL,S,S+40,kitchenPlaster);
        WallZ(kitchen,"Kitchen west north",KL-T,KL,S+130,Split,kitchenPlaster);
        WallX(kitchen,"Dining bay south return",KL-26-T,KL-T,S+40-T,S+40,kitchenPlaster);
        WallX(kitchen,"Dining bay north return",KL-26-T,KL-T,S+130,S+130+T,kitchenPlaster);
        WindowZ(kitchen,"Dining bay window",KL-26-T,KL-26,S+40,S+130,S+48,S+122,19,51);
        DoorZ(kitchen,"Wide kitchen opening",KR,HL,S+33.2f,Split,S+85,44,kitchenPlaster);
        // Bedroom remains comfortably bed-sized, with the doorway offset from the kitchen.
        Slab(bedroom,"Floor",KL,HL,Split+T,N,wood); Ceiling(bedroom,KL,HL,Split+T,N);
        WallX(bedroom,"Kitchen bedroom partition",KL-T,HL,Split,Split+T,plaster);
        WallZ(bedroom,"Bedroom west",KL-T,KL,Split+T,N,plaster);
        WindowX(bedroom,"Bedroom north window",KL,HL,N,N+T,KL+45,KL+115,21,51);
        DoorZ(bedroom,"Private bedroom doorway",KR,HL,Split+T,N,449,28,plaster);
        // Trim stays flush to the walls and does not narrow collision openings.
        SkirtZ(kitchen,KL,S,S+40); SkirtZ(kitchen,KL,S+130,Split);
        SkirtX(kitchen,KL,VX-T,S); SkirtX(kitchen,KL,KR,Split-1);
        SkirtX(bedroom,KL,KR,Split+T); SkirtX(bedroom,KL,KR,N-1);
        SkirtZ(bedroom,KL,Split+T,N);
        // Readable light pools at each change in space; no furniture or decorative props.
        LightAt(hall,"Entrance light",new Vector3(entryX,52,S-8),85,1.5f,new Color(1,.88f,.7f));
        LightAt(hall,"Gallery light south",new Vector3((HL+HR)/2,52,S+85),85,1.25f,new Color(1,.92f,.8f));
        LightAt(hall,"Gallery light north",new Vector3((HL+HR)/2,52,449),85,1.25f,new Color(1,.92f,.8f));
        LightAt(kitchen,"Cooking light",new Vector3(KL+85,49,S+44),125,1.6f,new Color(1,.92f,.79f));
        LightAt(kitchen,"Dining daylight",new Vector3(KL-6,46,S+85),140,1.8f,new Color(.82f,.91f,1));
        LightAt(kitchen,"Kitchen north fill",new Vector3(KL+94,49,Split-30),100,1.2f,new Color(1,.93f,.84f));
        LightAt(bedroom,"Bedroom daylight",new Vector3(KL+80,47,N-24),125,1.4f,new Color(.85f,.92f,1));
        foreach(var p in protectedTransforms) if(!p.Key || p.Key.localToWorldMatrix!=p.Value) throw new InvalidOperationException("Protected transform changed: "+p.Key?.name);
        Undo.RegisterCreatedObjectUndo(hall.gameObject,"Refined hall"); Undo.RegisterCreatedObjectUndo(kitchen.gameObject,"Refined kitchen"); Undo.RegisterCreatedObjectUndo(bedroom.gameObject,"Refined bedroom");
        Undo.CollapseUndoOperations(undo);
        Physics.SyncTransforms(); Validate(); RenderViews();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/apartment-refinement.txt",$"Applied. Protected transforms: {protectedTransforms.Count}. Kitchen clear area: {KitchenArea():F1} world-square units / {KitchenArea()/400:F1} character-scale square metres. Original kitchen 58 square metres. Bedroom: 8 x {(N-Split-T)/20:F2} metres.\n");
        Selection.activeGameObject=hall.gameObject;
    }
    static float KitchenArea() => (VX-T-KL)*33.2f+(KR-KL)*(170-33.2f)+26*90;
    static Transform Group(string name) {var t=new GameObject(name).transform;t.SetParent(root,false);return t;}
    static Material Mat(string name,Color c) {
        string path="Assets/Materials/Apartment/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find(GraphicsSettings.currentRenderPipeline==null?"Standard":"Universal Render Pipeline/Lit"));m.color=c;if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",.05f);AssetDatabase.CreateAsset(m,path);}return m;
    }
    static GameObject Box(Transform p,string name,float x0,float x1,float z0,float z1,float y0,float y1,Material m,bool collision=true) {
        if(x1<=x0 || z1<=z0 || y1<=y0)throw new InvalidOperationException("Invalid dimensions: "+name);
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(p,false);g.transform.position=new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2);g.transform.localScale=new Vector3(x1-x0,y1-y0,z1-z0);g.GetComponent<Renderer>().sharedMaterial=m;
        if(!collision)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;
    }
    static void Slab(Transform p,string name,float x0,float x1,float z0,float z1,Material m)=>Box(p,name,x0,x1,z0,z1,Y-1,Y,m);
    static void Ceiling(Transform p,float x0,float x1,float z0,float z1)=>Box(p,"Ceiling",x0,x1,z0,z1,Top,Top+T,kitchenPlaster);
    static void WallX(Transform p,string name,float x0,float x1,float z0,float z1,Material m)=>Box(p,name,x0,x1,z0,z1,Y,Top,m);
    static void WallZ(Transform p,string name,float x0,float x1,float z0,float z1,Material m)=>Box(p,name,x0,x1,z0,z1,Y,Top,m);
    static void DoorZ(Transform p,string name,float x0,float x1,float z0,float z1,float center,float width,Material m) {
        WallZ(p,name+" south jamb",x0,x1,z0,center-width/2,m);WallZ(p,name+" north jamb",x0,x1,center+width/2,z1,m);
        Box(p,name+" lintel",x0,x1,center-width/2,center+width/2,Y+48,Top,m);
        Box(p,name+" trim south",x0-.6f,x1+.6f,center-width/2-1.2f,center-width/2,Y,Y+49.2f,trim,false);
        Box(p,name+" trim north",x0-.6f,x1+.6f,center+width/2,center+width/2+1.2f,Y,Y+49.2f,trim,false);
        Box(p,name+" trim head",x0-.6f,x1+.6f,center-width/2-1.2f,center+width/2+1.2f,Y+48,Y+49.2f,trim,false);
    }
    static void WindowZ(Transform p,string name,float x0,float x1,float z0,float z1,float a,float b,float sill,float head) {
        WallZ(p,name+" south",x0,x1,z0,a,kitchenPlaster);WallZ(p,name+" north",x0,x1,b,z1,kitchenPlaster);
        Box(p,name+" below",x0,x1,a,b,Y,sill,kitchenPlaster);Box(p,name+" above",x0,x1,a,b,head,Top,kitchenPlaster);
        var g=Box(p,name+" glazing",x0+.7f,x0+1,a,b,sill,head,glass);g.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        Box(p,name+" sill",x0,x1+2,a-1,b+1,sill-1,sill,trim,false);
        for(int i=0;i<=3;i++){float z=Mathf.Lerp(a,b,i/3f);Box(p,name+" mullion",x0+.5f,x1+.4f,z-.6f,z+.6f,sill,head,trim,false);}
        Box(p,name+" head frame",x0,x1+.4f,a,b,head-1,head,trim,false);
    }
    static void WindowX(Transform p,string name,float x0,float x1,float z0,float z1,float a,float b,float sill,float head) {
        WallX(p,name+" west",x0,a,z0,z1,plaster);WallX(p,name+" east",b,x1,z0,z1,plaster);
        Box(p,name+" below",a,b,z0,z1,Y,sill,plaster);Box(p,name+" above",a,b,z0,z1,head,Top,plaster);
        var g=Box(p,name+" glazing",a,b,z1-1,z1-.7f,sill,head,glass);g.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        Box(p,name+" sill",a-1,b+1,z0-2,z1,sill-1,sill,trim,false);
        for(int i=0;i<=2;i++){float x=Mathf.Lerp(a,b,i/2f);Box(p,name+" mullion",x-.6f,x+.6f,z0-.4f,z1-.5f,sill,head,trim,false);}
        Box(p,name+" head frame",a,b,z0-.4f,z1,head-1,head,trim,false);
    }
    static void SkirtX(Transform p,float x0,float x1,float z)=>Box(p,"Skirting",x0,x1,z,z+.7f,Y,Y+2.2f,trim,false);
    static void SkirtZ(Transform p,float x,float z0,float z1)=>Box(p,"Skirting",x,x+.7f,z0,z1,Y,Y+2.2f,trim,false);
    static void LightAt(Transform p,string name,Vector3 pos,float range,float intensity,Color color){var g=new GameObject(name);g.transform.SetParent(p,false);g.transform.position=pos;var l=g.AddComponent<Light>();l.type=LightType.Point;l.range=range;l.intensity=intensity;l.color=color;l.shadows=LightShadows.None;}

    [MenuItem("Tools/LoomRoom/Apartment/Validate Refined Layout")]
    public static void Validate() {
        // Check usable circulation with the doors open, then restore the authored scene exactly.
        var doors=UnityEngine.Object.FindObjectsByType<HingedDoor>(FindObjectsInactive.Include);
        var poses=doors.Select(d=>d.Hinge.localRotation).ToArray();
        try {
            if(!Application.isPlaying) foreach(var d in doors) {
                float open=new SerializedObject(d).FindProperty("openAngle").floatValue;
                d.Hinge.localRotation*=Quaternion.Euler(0,open,0);
            }
            ValidateRoutes();
        } finally {
            if(!Application.isPlaying) for(int i=0;i<doors.Length;i++)doors[i].Hinge.localRotation=poses[i];
            Physics.SyncTransforms();
        }
    }
    static void ValidateRoutes() {
        Physics.SyncTransforms();
        var source=UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Include).Single(p=>p.kind==PlayerKind.Room).GetComponent<CharacterController>();
        float radius=source.radius*source.transform.lossyScale.x,height=source.height*source.transform.lossyScale.y;
        var paths=new[]{
            new[]{P(-410,437),P(-410,285),P(-495,285),P(-495,S+85),P(-620,S+85),P(KL-10,S+85)},
            new[]{P(-495,285),P(-495,S-4),P(VX+20,S-4)},
            new[]{P(-495,S-15),P(-511.0667f,S-15),P(-511.0667f,S-55)},
            new[]{P(-495,285),P(-495,449),P(-600,449)},
            new[]{P(-620,S+85),P(-620,S+20),P(-580,S+20)},
            new[]{P(-620,S+85),P(-620,Split-20),P(-545,Split-20)}
        };
        int count=0;
        foreach(var route in paths)for(int i=1;i<route.Length;i++) {
            int n=Mathf.CeilToInt(Vector3.Distance(route[i-1],route[i])/2);
            for(int j=0;j<=n;j++) {
                var pos=Vector3.Lerp(route[i-1],route[i],j/(float)n);
                var hits=Physics.OverlapCapsule(pos+Vector3.up*(radius+.1f),pos+Vector3.up*(height-radius),radius,~0,QueryTriggerInteraction.Ignore).Where(c=>!(c is CharacterController)&&c.enabled).ToArray();
                if(hits.Length>0)throw new InvalidOperationException("Blocked full-size route at "+pos+": "+string.Join(",",hits.Select(c=>c.name)));
                if(!Physics.Raycast(pos+Vector3.up*2,Vector3.down,4,~0,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Missing floor at "+pos);
                count++;
            }
        }
        // Reserve a six-seat dining envelope with chair pull-out, plus a separate cooking run.
        Vector3 diningCenter=new Vector3(-632,19,S+85); Vector3 diningHalf=new Vector3(39,18,38);
        var blockers=Physics.OverlapBox(diningCenter,diningHalf,Quaternion.identity,~0,QueryTriggerInteraction.Ignore).Where(c=>c.enabled).ToArray();
        if(blockers.Length>0)throw new InvalidOperationException("Dining clearance blocked: "+string.Join(",",blockers.Select(c=>c.name)));
        File.WriteAllText("Temp/apartment-refinement-validation.txt",$"PASS {count} capsule / floor samples. Full-size radius={radius}, height={height}.\nPASS six-seat dining reservation 78 x 76 units (3.9 x 3.8m), clear of architecture.\n");
    }
    static Vector3 P(float x,float z)=>new Vector3(x,.7f,z);
    [MenuItem("Tools/LoomRoom/Apartment/Render Refined Views")]
    public static void RenderViews() {
        root=GameObject.Find("Apartment Layout").transform;
        var hidden=root.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Ceiling" || r.name.Contains("lintel") || r.name.Contains("trim head")).ToArray();
        var states=hidden.Select(r=>r.enabled).ToArray();
        bool expanded=root.Find("Second Bedroom/Floor").GetComponent<Renderer>().bounds.max.z>540;
        try {foreach(var r in hidden)r.enabled=false;Render("Temp/apartment-refined-plan.png",new Vector3(-493,650,expanded?366:326),Quaternion.Euler(90,0,0),true,expanded?230:190);}
        finally{for(int i=0;i<hidden.Length;i++)hidden[i].enabled=states[i];}
        Render("Temp/apartment-refined-kitchen.png",new Vector3(-540,34,S+87),Quaternion.LookRotation(new Vector3(-1,-.04f,0)),false,0);
        Render("Temp/apartment-refined-hall.png",new Vector3(-495,34,S-12),Quaternion.LookRotation(Vector3.forward),false,0);
    }
    static void Render(string path,Vector3 position,Quaternion rotation,bool ortho,float size) {
        var g=new GameObject("Temporary architecture camera");var c=g.AddComponent<Camera>();var rt=new RenderTexture(1500,1000,24);var prior=RenderTexture.active;
        try {c.transform.SetPositionAndRotation(position,rotation);c.orthographic=ortho;c.orthographicSize=size;c.fieldOfView=75;c.nearClipPlane=.2f;c.farClipPlane=1000;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.12f,.14f,.16f);c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1500,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1500,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
        finally{RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(g);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
}

public static class ApartmentDoorSetup
{
    const string Folder="Assets/Prefabs/Props";
    static AudioData openSound,closeSound;
    static Mesh sourceMesh;
    static Material[] sourceMaterials;
    [MenuItem("Tools/LoomRoom/Apartment/Install Reusable Doors")]
    public static void Install()
    {
        if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Room.unity")throw new InvalidOperationException("Open Room in edit mode.");
        var apartment=GameObject.Find("Apartment Layout").transform;
        if(apartment.Find("Doors"))throw new InvalidOperationException("Doors already installed.");
        Directory.CreateDirectory(Folder);
        openSound=AudioAsset("ApartmentDoorOpen","Drs_Wood_Door_Open_01.wav","Drs_Wood_Door_Open_02.wav");
        closeSound=AudioAsset("ApartmentDoorClose","Drs_Wood_Door_Close_01.wav","Drs_Wood_Door_Close_02.wav");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_House_Door_01.prefab");
        var leaf=source.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.gameObject.name=="SM_Bld_House_Door");
        sourceMesh=leaf.sharedMesh;sourceMaterials=leaf.GetComponent<Renderer>().sharedMaterials;
        var single=MakeLeaf("ApartmentDoor",1.36f); AddFrame(single); single.transform.localScale=Vector3.one*20;
        var singlePrefab=PrefabUtility.SaveAsPrefabAsset(single,Folder+"/ApartmentDoor.prefab"); UnityEngine.Object.DestroyImmediate(single);
        var doors=new GameObject("Doors").transform;doors.SetParent(apartment,false);Undo.RegisterCreatedObjectUndo(doors.gameObject,"Install doors");
        Place(singlePrefab,doors,"Current Bedroom Door",new Vector3(-470.8667f,.75f,271.4f),-90,100,"bedroom door");
        Place(singlePrefab,doors,"Second Bedroom Door",new Vector3(-519.2667f,.75f,435.4f),-90,-100,"bedroom door");
        Place(singlePrefab,doors,"Kitchen Door",new Vector3(-519.2667f,.75f,309.929663f),-90,-100,"kitchen door");
        var kitchen=apartment.Find("Kitchen and Dining");
        foreach(var name in new[]{"south jamb","north jamb","lintel","trim south","trim north","trim head"}) {
            var t=kitchen.Find("Wide kitchen opening "+name);Undo.RecordObject(t,"Fit single kitchen door");
            var size=t.localScale;var pos=t.position;
            if(name=="south jamb"){size.z+=8;pos.z+=4;}
            if(name=="north jamb"){size.z+=8;pos.z-=4;}
            if(name=="lintel")size.z=28;
            if(name=="trim south")pos.z+=8;
            if(name=="trim north")pos.z-=8;
            if(name=="trim head")size.z-=16;
            t.localScale=size;t.position=pos;
        }
        var hall=apartment.Find("Entrance and Hall");
        foreach(var t in hall.GetComponentsInChildren<Transform>().Where(t=>t.name=="Apartment entrance - closed exterior door"||t.name=="Entry pull").ToArray())Undo.DestroyObjectImmediate(t.gameObject);
        float entryX=(-547.0667f-475.0667f)/2;
        Place(singlePrefab,doors,"Apartment Entrance Door",new Vector3(entryX-13.6f,.75f,199.529663f),0,-100,"entrance door");
        foreach(var t in apartment.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Entry frame ") || t.name.Contains("opening trim ") || t.name.Contains("doorway trim ")).ToArray()) Undo.DestroyObjectImmediate(t.gameObject);
        // A bounded shared landing supports the operable entrance instead of exposing the void.
        var landing=new GameObject("Shared entrance landing").transform;landing.SetParent(apartment,false);Undo.RegisterCreatedObjectUndo(landing.gameObject,"Entrance landing");
        var floor=apartment.Find("Entrance and Hall/Floor entrance recess").GetComponent<Renderer>().sharedMaterial;
        var wall=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Apartment/Hall warm plaster.mat");
        Cube(landing,"Landing floor",new Vector3(-509.4667f,0,178.529663f),new Vector3(75.2f,1,40),floor);
        Cube(landing,"Ceiling",new Vector3(-509.4667f,62.1f,178.529663f),new Vector3(75.2f,3.2f,40),wall);
        Cube(landing,"Landing west",new Vector3(-548.6667f,30.5f,178.529663f),new Vector3(3.2f,60,40),wall);
        Cube(landing,"Landing east",new Vector3(-470.2667f,30.5f,178.529663f),new Vector3(3.2f,60,40),wall);
        Cube(landing,"Landing outer boundary",new Vector3(-509.4667f,30.5f,156.929663f),new Vector3(81.6f,60,3.2f),wall);
        var light=new GameObject("Landing light");light.transform.SetParent(landing,false);light.transform.position=new Vector3(-509,48,177);var l=light.AddComponent<Light>();l.type=LightType.Point;l.range=85;l.intensity=1.3f;
        // Existing reach was 8 world units (40cm), shorter than the room player's capsule diameter.
        var room=UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Include).Single(p=>p.kind==PlayerKind.Room);
        var interaction=new SerializedObject(room.GetComponent<InteractController>());interaction.FindProperty("rayDistance").floatValue=50;interaction.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(room.GetComponent<InteractController>());
        Physics.SyncTransforms();AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(room.gameObject.scene);EditorSceneManager.SaveScene(room.gameObject.scene);
        File.WriteAllText("Temp/apartment-doors-installed.txt","Installed reusable Synty single doors, four animated leaves, two AudioData effects, 2.5m room-player reach and enclosed entrance landing.\n");
    }
    static AudioData AudioAsset(string name,params string[] clips) {
        string path="Assets/Data/Audio/"+name+".asset";var data=AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if(!data){data=ScriptableObject.CreateInstance<AudioData>();data.clips=clips.Select(c=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/_Sound Effects Library/Doors/"+c)).ToArray();data.volume=.8f;data.pitchVariance=.025f;data.minDistance=20;data.maxDistance=240;AssetDatabase.CreateAsset(data,path);}return data;
    }
    static GameObject MakeLeaf(string name,float width) {
        var g=new GameObject(name);var door=g.AddComponent<HingedDoor>();var hinge=new GameObject("Hinge").transform;hinge.SetParent(g.transform,false);
        var visual=new GameObject("Synty door leaf");visual.transform.SetParent(hinge,false);visual.AddComponent<MeshFilter>().sharedMesh=sourceMesh;visual.AddComponent<MeshRenderer>().sharedMaterials=sourceMaterials;
        Bounds bounds=sourceMesh.bounds;Vector3 scale=new Vector3(width/bounds.size.x,2.36f/bounds.size.y,.16f/bounds.size.z);
        visual.transform.localScale=scale;visual.transform.localRotation=Quaternion.Euler(0,180,0);visual.transform.localPosition=new Vector3(bounds.max.x*scale.x,-bounds.min.y*scale.y,bounds.center.z*scale.z);
        var box=hinge.gameObject.AddComponent<BoxCollider>();box.center=new Vector3(width/2,1.18f,0);box.size=new Vector3(width,2.36f,.075f);
        var target=new GameObject("Interaction");target.transform.SetParent(hinge,false);var triggerBox=target.AddComponent<BoxCollider>();triggerBox.center=box.center;triggerBox.size=new Vector3(width,2.36f,.18f);triggerBox.isTrigger=true;
        var trigger=target.AddComponent<InteractableTrigger>();var so=new SerializedObject(door);so.FindProperty("hinge").objectReferenceValue=hinge;so.FindProperty("leafCollider").objectReferenceValue=box;so.FindProperty("interactionTrigger").objectReferenceValue=trigger;
        so.FindProperty("openingEffect.audio").objectReferenceValue=openSound;so.FindProperty("closingEffect.audio").objectReferenceValue=closeSound;so.ApplyModifiedPropertiesWithoutUndo();return g;
    }
    // Frame stays stationary; the stop overlaps the closed leaf on its non-swing side.
    public static void AddFrame(GameObject door) {
        if(door.transform.Find("Frame")) return;
        var frame=new GameObject("Frame").transform; frame.SetParent(door.transform,false);
        var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Apartment/Architectural trim.mat");
        void Piece(string name,float x0,float x1,float y0,float y1,float z0,float z1) {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube); g.name=name; g.transform.SetParent(frame,false);
            g.transform.localPosition=new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2);
            g.transform.localScale=new Vector3(x1-x0,y1-y0,z1-z0);
            g.GetComponent<Renderer>().sharedMaterial=mat; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        }
        Piece("Hinge jamb",-.075f,-.015f,-.0125f,2.385f,-.085f,.22f);
        Piece("Latch jamb",1.375f,1.435f,-.0125f,2.385f,-.085f,.22f);
        Piece("Head jamb",-.075f,1.435f,2.385f,2.445f,-.085f,.22f);
        Piece("Hinge stop",-.014f,.035f,-.0125f,2.335f,.085f,.125f);
        Piece("Latch stop",1.325f,1.374f,-.0125f,2.335f,.085f,.125f);
        Piece("Head stop",-.014f,1.374f,2.335f,2.384f,.085f,.125f);
        foreach(bool back in new[]{false,true}) {
            float z0=back?.221f:-.111f,z1=back?.247f:-.086f;
            Piece((back?"Back ":"Front ")+"hinge casing",-.09f,-.015f,-.0125f,2.385f,z0,z1);
            Piece((back?"Back ":"Front ")+"latch casing",1.375f,1.45f,-.0125f,2.385f,z0,z1);
            Piece((back?"Back ":"Front ")+"head casing",-.09f,1.45f,2.385f,2.46f,z0,z1);
        }
    }
    static void SetAngle(GameObject g,float angle){var so=new SerializedObject(g.GetComponent<HingedDoor>());so.FindProperty("openAngle").floatValue=angle;so.ApplyModifiedPropertiesWithoutUndo();if(g.transform.Find("Frame"))g.transform.Find("Frame").localScale=new Vector3(1,1,Mathf.Sign(angle));}
    static void Place(GameObject prefab,Transform parent,string name,Vector3 position,float yaw,float angle,string prompt){var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);g.name=name;g.transform.position=position;g.transform.rotation=Quaternion.Euler(0,yaw,0);g.transform.localScale=Vector3.one*20;SetAngle(g,angle);var so=new SerializedObject(g.GetComponent<HingedDoor>());so.FindProperty("doorName").stringValue=prompt;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Cube(Transform p,string name,Vector3 pos,Vector3 size,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(p,false);g.transform.position=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;}
}
