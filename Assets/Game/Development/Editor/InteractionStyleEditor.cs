using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class InteractionStyleEditor
{
    [MenuItem("Tools/LoomRoom/Style interaction targeting")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach(var ui in Object.FindObjectsByType<InteractUI>(FindObjectsInactive.Include))
        {
            Undo.RegisterFullObjectHierarchyUndo(ui.gameObject,"Style interaction targeting");
            var so=new SerializedObject(ui);
            var root=(GameObject)so.FindProperty("promptRoot").objectReferenceValue;
            var text=(TMP_Text)so.FindProperty("promptText").objectReferenceValue;
            var rect=(RectTransform)root.transform;
            rect.anchorMin=rect.anchorMax=rect.pivot=new(.5f,.5f);
            rect.anchoredPosition=new(0,-48); rect.sizeDelta=new(320,48);
            text.rectTransform.anchorMin=Vector2.zero; text.rectTransform.anchorMax=Vector2.one;
            text.rectTransform.offsetMin=new(14,8); text.rectTransform.offsetMax=new(-14,-8);
            text.fontSize=24; text.enableAutoSizing=false; text.color=new(.95f,.95f,.95f,1);
            text.alignment=TextAlignmentOptions.Center; text.richText=true; text.raycastTarget=false;
            var background=root.GetComponent<Image>(); if(background==null) background=Undo.AddComponent<Image>(root);
            background.color=new(.04f,.04f,.04f,.65f); background.sprite=null; background.raycastTarget=false;
            var canvas=ui.GetComponentInParent<Canvas>();
            var marker=canvas.transform.Find("PickupTargetMarker") as RectTransform;
            if(marker==null)
            {
                var go=new GameObject("PickupTargetMarker",typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go,"Pickup target marker");
                marker=(RectTransform)go.transform; marker.SetParent(canvas.transform,false);
                marker.anchorMin=marker.anchorMax=marker.pivot=new(.5f,.5f);
                for(int x=0;x<2;x++) for(int y=0;y<2;y++)
                {
                    AddStroke(marker,new(x,y),new(12,2),"Horizontal");
                    AddStroke(marker,new(x,y),new(2,12),"Vertical");
                }
            }
            so.FindProperty("targetMarker").objectReferenceValue=marker; so.ApplyModifiedProperties();
            marker.gameObject.SetActive(false); root.SetActive(false);
        }
        var scene=EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static void AddStroke(RectTransform parent,Vector2 corner,Vector2 size,string name)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image)); go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform; r.anchorMin=r.anchorMax=r.pivot=corner;
        r.anchoredPosition=Vector2.zero; r.sizeDelta=size;
        var image=go.GetComponent<Image>(); image.color=new(.85f,.9f,.88f,.95f); image.raycastTarget=false;
    }
}
