using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class DungeonInventoryFinishing
{
    static DungeonInventoryFinishing()=>EditorApplication.update+=Tick;
    static void Tick()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/DungeonInventoryFinish.request"))return;
        File.Delete("Temp/DungeonInventoryFinish.request");
        var mode=EditorSettings.serializationMode;EditorSettings.serializationMode=SerializationMode.ForceText;
        try
        {
            Directory.CreateDirectory("Assets/Game/Items/Prefabs/Props");AssetDatabase.Refresh();
            string path="Assets/Game/Items/Prefabs/Props/Equipment loot pouch.prefab";
            var pouch=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(pouch==null)
            {
                var root=new GameObject("Equipment loot pouch");
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonFantasyKingdom/Prefabs/Items/SM_Item_Pouch_01.prefab");
                if(source==null)throw new Exception("Synty pouch not found");
                var model=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
                foreach(var collider in model.GetComponentsInChildren<Collider>())collider.enabled=false;
                var renderers=model.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float scale=.35f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);model.transform.localScale=Vector3.one*scale;
                model.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*scale;
                var shine=root.AddComponent<ItemShine>();shine.surfaces=Array.Empty<Renderer>();
                var glint=GameObject.CreatePrimitive(PrimitiveType.Sphere);glint.name="Loot glint";glint.transform.SetParent(root.transform,false);Object.DestroyImmediate(glint.GetComponent<Collider>());
                glint.transform.localPosition=new Vector3(.07f,bounds.size.y*scale+.025f,0);glint.transform.localScale=Vector3.one*.025f;shine.glint=glint.transform;
                var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(1,.8f,.35f));
                string materialPath="Assets/Game/Items/Materials/Loot glint.mat";var old=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(old==null)AssetDatabase.CreateAsset(material,materialPath);else{Object.DestroyImmediate(material);material=old;}
                glint.GetComponent<Renderer>().sharedMaterial=material;
                pouch=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);
            }
            foreach(var guid in AssetDatabase.FindAssets("t:ItemData",new[]{"Assets/Game/Items/Data/Armor"}))
            {var item=AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));item.pickupVisualPrefab=pouch;EditorUtility.SetDirty(item);}
            string managerPath="Assets/Game/Core/Prefabs/InventoryManager.prefab";
            var managerRoot=PrefabUtility.LoadPrefabContents(managerPath);
            try{managerRoot.GetComponent<InventoryManager>().defaultPickupVisual=pouch;PrefabUtility.SaveAsPrefabAsset(managerRoot,managerPath);}finally{PrefabUtility.UnloadPrefabContents(managerRoot);}
            foreach(var manager in Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include))
            {manager.defaultPickupVisual=pouch;EditorUtility.SetDirty(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);}
            Directory.CreateDirectory("Assets/Game/Items/Prefabs/Consumables");AssetDatabase.Refresh();
            foreach(string name in new[]{"Apple","Pear","Roasted mushroom","Cooked meat"})
            {
                string from="Assets/Game/Items/Prefabs/Equipment/"+name+".prefab",to="Assets/Game/Items/Prefabs/Consumables/"+name+".prefab";
                if(AssetDatabase.LoadAssetAtPath<GameObject>(from)!=null){string error=AssetDatabase.MoveAsset(from,to);if(!string.IsNullOrEmpty(error))throw new Exception(error);}
            }
            var ui=Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
            var equipment=Object.FindAnyObjectByType<EquipmentPanelUI>(FindObjectsInactive.Include);
            var font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
            var tooltip=Object.FindAnyObjectByType<TooltipUI>(FindObjectsInactive.Include);
            if(tooltip!=null)
            {
                var so=new SerializedObject(tooltip);var panel=(GameObject)so.FindProperty("panel").objectReferenceValue;
                var canvas=panel.GetComponent<Canvas>();if(canvas==null)canvas=panel.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=2000;
                foreach(var text in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)){text.font=font;text.fontSharedMaterial=font.material;EditorUtility.SetDirty(text);}
                ((RectTransform)panel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,420);
                EditorUtility.SetDirty(canvas);
            }
            if(ui?.inventoryPanel!=null)foreach(var text in ui.inventoryPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if(text.text=="INVENTORY" || text.text.StartsWith("Drag to move")){text.font=font;text.fontSharedMaterial=font.material;text.fontSize=text.text=="INVENTORY"?32:20;EditorUtility.SetDirty(text);}
            foreach(string tier in new[]{"Bronze","Iron"})
            {
                var item=AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Armor/"+tier+" Gloves.asset");string art=tier=="Bronze"?"Copper":"Iron";
                if(item!=null && item.icon!=null && item.icon.name.EndsWith("Gloves1")){item.icon=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/PixelItems/Armory/Singles/Armor Singles/"+art+"/"+art+"_Gloves18.png").OfType<Sprite>().First();EditorUtility.SetDirty(item);}
            }
            if(ui!=null && equipment!=null && ui.inventoryPanel==null)
            {
                ui.inventoryPanel=Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include).First(t=>t.name=="InventoryPanel");
                ui.statsPanel=(RectTransform)equipment.transform;ui.equipmentPanel=equipment;EditorUtility.SetDirty(ui);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            File.WriteAllText("Temp/DungeonInventoryFinish.report","PASS: armor pickup-only Synty pouch assigned, shared glint prefab authored, food models grouped under Consumables.");
        }
        catch(Exception ex){File.WriteAllText("Temp/DungeonInventoryFinish.report","FAIL: "+ex);}
        finally{EditorSettings.serializationMode=mode;}
    }
}
