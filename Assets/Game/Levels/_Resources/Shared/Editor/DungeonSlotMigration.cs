using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class DungeonSlotMigration
{
    static DungeonSlotMigration()=>EditorApplication.update+=Tick;
    static void Tick(){
        if(EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/DungeonSlots.request"))return;
        File.Delete("Temp/DungeonSlots.request");
        var mode=EditorSettings.serializationMode;
        try {
            EditorSettings.serializationMode=SerializationMode.ForceText;
            var scene=EditorSceneManager.GetActiveScene();
            var slots=UnityEngine.Object.FindObjectsByType<ItemSlotUI>(FindObjectsInactive.Include).Where(s=>s.gameObject.scene==scene).ToArray();
            if(slots.Length==0)throw new Exception("No scene item slots found");
            Directory.CreateDirectory("Assets/Game/UI/Prefabs/Items");AssetDatabase.Refresh();
            int converted=0;
            foreach(bool hotbar in new[]{true,false}) {
                var group=slots.Where(s=>{var data=new SerializedObject(s);return data.FindProperty("keyLabel").objectReferenceValue!=null==hotbar;}).ToArray();
                if(group.Length==0)continue;
                string path="Assets/Game/UI/Prefabs/Items/"+(hotbar?"Hotbar slot":"Inventory slot")+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null){
                    var clone=UnityEngine.Object.Instantiate(group[0].gameObject);clone.name=hotbar?"Hotbar slot":"Inventory slot";
                    var level=AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
                    var go=new GameObject("Dungeon frame",typeof(RectTransform),typeof(Image));go.transform.SetParent(clone.transform,false);
                    var image=go.GetComponent<Image>();image.sprite=level.hotbarFrame;image.type=Image.Type.Sliced;image.raycastTarget=false;image.enabled=false;
                    var rect=image.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                    var data=new SerializedObject(clone.GetComponent<ItemSlotUI>());data.FindProperty("dungeonFrame").objectReferenceValue=image;data.ApplyModifiedPropertiesWithoutUndo();
                    prefab=PrefabUtility.SaveAsPrefabAsset(clone,path);UnityEngine.Object.DestroyImmediate(clone);
                }
                foreach(var slot in group){
                    if(PrefabUtility.IsPartOfPrefabInstance(slot))continue;
                    var rect=(RectTransform)slot.transform;var position=rect.anchoredPosition;var size=rect.sizeDelta;
                    PrefabUtility.ConvertToPrefabInstance(slot.gameObject,prefab,new ConvertToPrefabInstanceSettings{objectMatchMode=ObjectMatchMode.ByHierarchy,recordPropertyOverridesOfMatches=false,changeRootNameToAssetName=false},InteractionMode.AutomatedAction);
                    rect.anchoredPosition=position;rect.sizeDelta=size;PrefabUtility.RecordPrefabInstancePropertyModifications(rect);converted++;
                }
            }
            foreach(var owner in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).Where(c=>c is HotbarUI || c is InventoryUI)){
                var values=new SerializedObject(owner).FindProperty("slots");
                for(int i=0;i<values.arraySize;i++)if(values.GetArrayElementAtIndex(i).objectReferenceValue==null)throw new Exception("Slot reference lost on "+owner.name);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/DungeonSlots.report",$"PASS: {converted} scene slots connected to editable prefabs; inventory/hotbar references intact.");
        }catch(Exception e){File.WriteAllText("Temp/DungeonSlots.report","FAIL: "+e);Debug.LogException(e);}finally{EditorSettings.serializationMode=mode;}
    }
}
