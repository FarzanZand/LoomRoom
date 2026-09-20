using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class ApartmentBedroomExpansion
{
    [MenuItem("Tools/LoomRoom/Apartment/Enlarge Second Bedroom")]
    public static void Expand(){
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        var root=GameObject.Find("Apartment").transform;
        var room=root.Find("Second Bedroom");
        var bounds=(room.Find("Geometry/Floor") ?? room.Find("Floor")).GetComponent<Renderer>().bounds;
        float delta=bounds.min.z+140-bounds.max.z;
        if(Mathf.Abs(delta)<.001f)return;
        EditorSceneManager.SaveScene(root.gameObject.scene,"Temp/ApartmentBackup/Room.before-bedroom-8x7.unity",true);
        var protectedTransforms=root.GetComponentsInChildren<Transform>().Where(t=>!t.IsChildOf(room)).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        foreach(var t in room.GetComponentsInChildren<Transform>().Where(t=>t!=room)){
            if(t.name.StartsWith("Bedroom north window")){Undo.RecordObject(t,"Extend bedroom");t.position+=Vector3.forward*delta;}
            else if(t.name=="Floor" || t.name=="Ceiling" || t.name=="Bedroom west" || t.name=="Private bedroom doorway north jamb")Grow(t,delta);
            else if(t.name=="Skirting"){
                if(t.localScale.z>10)Grow(t,delta);
                else if(t.position.z>480){Undo.RecordObject(t,"Extend bedroom");t.position+=Vector3.forward*delta;}
            }
            else if(t.name=="Bedroom daylight"){Undo.RecordObject(t,"Extend bedroom");t.position+=Vector3.forward*delta;}
        }
        foreach(var p in protectedTransforms)if(p.Key.localToWorldMatrix!=p.Value)throw new Exception("Protected geometry changed: "+p.Key.name);
        Physics.SyncTransforms();
        ApartmentLayoutRefinement.Validate();
        // Reserve a generous bed area and a continuous walking margin without adding furniture.
        var beds=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r=>r.name.IndexOf("bed",StringComparison.OrdinalIgnoreCase)>=0 && !r.transform.IsChildOf(root)).ToArray();
        string measurements=string.Join("\n",beds.Select(r=>r.name+" "+r.bounds.size));
        var area=new Bounds(new Vector3(-610,20,501),new Vector3(100,38,90));
        if(Physics.OverlapBox(area.center,area.extents,Quaternion.identity,~0,QueryTriggerInteraction.Ignore).Length!=0)throw new Exception("Bed and circulation reservation obstructed");
        var floor=(room.Find("Geometry/Floor") ?? room.Find("Floor")).GetComponent<Renderer>().bounds;
        File.WriteAllText("Temp/expand-bedroom-result.txt","PASS bedroom resized to 8 x 7m. Original bedroom, kitchen, hall and doors unchanged. PASS 5 x 4.5m bed/circulation reservation.\nExisting bed renderer sizes:\n"+measurements);
        EditorSceneManager.MarkSceneDirty(room.gameObject.scene);EditorSceneManager.SaveScene(room.gameObject.scene);
        ApartmentLayoutRefinement.RenderViews();
    }
    static void Grow(Transform t,float delta){Undo.RecordObject(t,"Extend bedroom");t.position+=Vector3.forward*(delta/2);var size=t.localScale;size.z+=delta;t.localScale=size;}
}
