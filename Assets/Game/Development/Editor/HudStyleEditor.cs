using TMPro;
using Unity.Cinemachine;
using Unity.Cinemachine.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Authors the shared UI in the open scene; no runtime UI generation.
public static class HudStyleEditor
{
    static readonly Color Panel = new(0.055f, 0.055f, 0.055f, 0.96f);
    static readonly Color Empty = new(0.10f, 0.10f, 0.10f, 0.88f);
    static readonly Color Filled = new(0.19f, 0.19f, 0.19f, 0.97f);
    static readonly Color Accent = new(0.64f, 0.64f, 0.64f, 1f);
    static readonly Color Ink = new(0.9f, 0.9f, 0.9f, 1f);

    static Sprite barSprite;

    [MenuItem("Tools/LoomRoom/Apply HUD polish %&h")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Room") { Debug.LogWarning("Open Room before applying HUD styling."); return; }
        foreach (var root in scene.GetRootGameObjects()) Undo.RegisterFullObjectHierarchyUndo(root, "Polish HUD");
        SaveDuringPlay.Enabled = false;
        const string spritePath = "Assets/Game/UI/Fonts/HudBar.png";
        if (!System.IO.File.Exists(spritePath))
        {
            var texture = new Texture2D(2,2);
            texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); texture.Apply();
            System.IO.File.WriteAllBytes(spritePath,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(spritePath);
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(spritePath);
        importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
        importer.filterMode=FilterMode.Point; importer.SaveAndReimport();
        barSprite=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        foreach (var rig in Object.FindObjectsByType<PlayerCameraRig>(FindObjectsInactive.Include))
        {
            var cam = rig.GetComponentInChildren<CinemachineCamera>(true);
            if (cam != null) { cam.Lens.FieldOfView = 70f; EditorUtility.SetDirty(cam); PrefabUtility.RecordPrefabInstancePropertyModifications(cam); }
        }
        foreach (var ui in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include))
        {
            var canvas = ui.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var panel = Ref<GameObject>(ui, "panel");
            Rect(panel.transform, new(.5f,.5f), new(.5f,.5f), Vector2.zero, new(660, 510));
            Surface(panel, Panel);
            var grid = panel.GetComponentInChildren<GridLayoutGroup>(true);
            if (grid != null)
            {
                grid.cellSize = new(88,88); grid.spacing = new(10,10);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 6;
                grid.padding = new RectOffset(0,0,0,0); grid.childAlignment = TextAnchor.UpperLeft;
                Rect(grid.transform, new(.5f,1), new(.5f,1), new(0,-84), new(578,382));
            }
            Label(panel.transform,"InventoryTitle","INVENTORY",new(28,-22),new(400,36),26,Accent);
            Label(panel.transform,"InventoryHint","Drag to move  /  Right-click for actions  /  Tab to close",new(28,-477),new(610,22),17,Ink);
            panel.SetActive(false);
        }
        foreach (var hotbar in Object.FindObjectsByType<HotbarUI>(FindObjectsInactive.Include))
        {
            Rect(hotbar.transform,new(.5f,0),new(.5f,0),new(0,28),new(602,104));
            var layout = hotbar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.padding = new RectOffset(12,12,8,8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            RemoveBacking(hotbar.gameObject);
            foreach(var slot in hotbar.GetComponentsInChildren<ItemSlotUI>(true)) ((RectTransform)slot.transform).sizeDelta = new(88,88);
        }
        foreach (var slot in Object.FindObjectsByType<ItemSlotUI>(FindObjectsInactive.Include))
        {
            var so = new SerializedObject(slot);
            so.FindProperty("emptyColor").colorValue = Empty; so.FindProperty("filledColor").colorValue = Filled;
            so.ApplyModifiedProperties();
            StyleSlot(slot);
            var icon = Ref<Image>(slot,"iconImage");
            if(icon != null) { Stretch(icon.rectTransform, 13); icon.preserveAspect = true; icon.raycastTarget = false; icon.enabled = icon.sprite != null; }
            var key = Ref<TextMeshProUGUI>(slot,"keyLabel");
            if(key != null) { Rect(key.transform,new(0,1),new(0,1),new(7,-4),new(24,22)); key.fontSize=16; key.color=Accent; key.raycastTarget=false; }
            var count = Ref<TextMeshProUGUI>(slot,"stackLabel");
            if(count != null) { Rect(count.transform,new(1,0),new(1,0),new(-5,4),new(44,22)); count.alignment=TextAlignmentOptions.BottomRight; count.fontSize=17; count.color=Ink; count.raycastTarget=false; }
            var highlight = Ref<Image>(slot,"equippedHighlight");
            if(highlight != null) { highlight.sprite=null; highlight.color=Accent; highlight.raycastTarget=false; var hr=highlight.rectTransform; hr.anchorMin=Vector2.zero; hr.anchorMax=new(1,0); hr.pivot=Vector2.zero; hr.anchoredPosition=Vector2.zero; hr.sizeDelta=new(0,3); highlight.gameObject.SetActive(false); }
        }
        foreach(var vitals in Object.FindObjectsByType<PlayerVitalsUI>(FindObjectsInactive.Include))
        {
            Rect(vitals.transform,new(0,0),new(0,0),new(32,28),new(260,100));
            vitals.transform.localScale = new Vector3(1.2f,1.2f,1f);
            RemoveBacking(vitals.gameObject);
            Bar(Ref<Image>(vitals,"healthFill"), "HEALTH", 63, new(.76f,.27f,.26f,1));
            Bar(Ref<Image>(vitals,"staminaFill"), "STAMINA", 23, new(.37f,.65f,.56f,1));
            foreach(var field in new[]{"health", "stamina"})
            {
                var root=Ref<Image>(vitals,field+"Fill").transform.parent;
                Label(root,"Value","100 / 100",new(110,23),new(118,20),15,Ink);
                var value=root.Find("Value").GetComponent<TextMeshProUGUI>(); value.alignment=TextAlignmentOptions.Right;
                var so=new SerializedObject(vitals); so.FindProperty(field+"Value").objectReferenceValue=value;so.ApplyModifiedProperties();
            }
        }
        StyleTooltips();
        foreach(var menu in Object.FindObjectsByType<ContextMenuUI>(FindObjectsInactive.Include))
        {
            Surface(Ref<RectTransform>(menu,"panel").gameObject,Panel);
            var button=Ref<Button>(menu,"buttonTemplate"); Surface(button.gameObject,Empty);
            var colors=button.colors; colors.normalColor=Color.white; colors.highlightedColor=new(.75f,.75f,.75f,1); colors.pressedColor=Accent; button.colors=colors;
            foreach(var t in button.GetComponentsInChildren<TMP_Text>(true)) { t.color=Ink; t.fontSize=20; }
        }
        foreach(var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
            if(t.text.Contains("F1:") && t.text.Contains("player"))
            { t.text="F1  Room   /   F2  Table"; t.fontSize=18; t.color=new(.85f,.85f,.85f,.65f); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[HUD] Authored camera FOV 70, compact hotbar, inventory and vitals. Cinemachine Save During Play disabled.");
    }
    [MenuItem("Tools/LoomRoom/Unify inventory slots %&j")]
    public static void UnifySlots()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach(var slot in Object.FindObjectsByType<ItemSlotUI>(FindObjectsInactive.Include))
        {
            Undo.RegisterFullObjectHierarchyUndo(slot.gameObject, "Unify inventory slots");
            StyleSlot(slot);
        }
        var scene=EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
    [MenuItem("Tools/LoomRoom/Polish icons and tooltips")]
    public static void PolishDetails()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach(var hotbar in Object.FindObjectsByType<HotbarUI>(FindObjectsInactive.Include))
        {
            Undo.RegisterFullObjectHierarchyUndo(hotbar.gameObject, "Enlarge hotbar icons");
            ((RectTransform)hotbar.transform).sizeDelta=new(602,104);
            foreach(var slot in hotbar.GetComponentsInChildren<ItemSlotUI>(true))
                ((RectTransform)slot.transform).sizeDelta=new(88,88);
        }
        foreach(var slot in Object.FindObjectsByType<ItemSlotUI>(FindObjectsInactive.Include))
        {
            var icon=Ref<Image>(slot,"iconImage");
            if(icon!=null) { Stretch(icon.rectTransform,13); icon.preserveAspect=true; }
        }
        StyleTooltips();
        var scene=EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }

    static void StyleTooltips()
    {
        foreach(var tooltip in Object.FindObjectsByType<TooltipUI>(FindObjectsInactive.Include))
        {
            var panel=Ref<GameObject>(tooltip,"panel");
            Undo.RegisterFullObjectHierarchyUndo(panel,"Style tooltip");
            Surface(panel,Panel);
            ((RectTransform)panel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,384);
            var layout=panel.GetComponent<VerticalLayoutGroup>();
            layout.padding=new RectOffset(18,18,16,16); layout.spacing=8;
            layout.childControlWidth=layout.childControlHeight=true;
            layout.childForceExpandWidth=true; layout.childForceExpandHeight=false;
            var group=panel.GetComponent<CanvasGroup>();
            if(group==null) group=Undo.AddComponent<CanvasGroup>(panel);
            group.blocksRaycasts=false; group.interactable=false;
            foreach(var t in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                t.color=Ink; t.fontSize=24; t.enableAutoSizing=false;
                t.fontStyle=FontStyles.Normal; t.richText=true; t.raycastTarget=false;
                var le=t.GetComponent<LayoutElement>();
                if(le!=null) { le.minHeight=-1; le.preferredHeight=-1; le.flexibleHeight=0; }
            }
            var title=Ref<TextMeshProUGUI>(tooltip,"nameText");
            title.fontStyle=FontStyles.Bold; title.transform.SetSiblingIndex(0);
            var divider=panel.transform.Find("Divider");
            if(divider!=null)
            {
                divider.SetSiblingIndex(1);
                var le=divider.GetComponent<LayoutElement>();
                if(le==null) le=Undo.AddComponent<LayoutElement>(divider.gameObject);
                le.minHeight=le.preferredHeight=2; le.flexibleHeight=0;
                var line=divider.GetComponent<Image>(); line.color=Accent; line.raycastTarget=false;
            }
            var type=Ref<TextMeshProUGUI>(tooltip,"typeText");
            type.fontSize=16; type.color=new(.65f,.68f,.72f,1); type.transform.SetSiblingIndex(2);
        }
    }

    static void StyleSlot(ItemSlotUI slot)
    {
        // Outline duplicates translucent quads and aliases at fractional canvas scales.
        // A solid, unadorned surface keeps every cell identical at any resolution.
        var color=new Color(.24f,.24f,.24f,1f);
        foreach(var shadow in slot.GetComponents<Shadow>()) Undo.DestroyObjectImmediate(shadow);
        var image=Ref<Image>(slot,"background");
        image.sprite=null; image.type=Image.Type.Simple; image.color=color;
        var so=new SerializedObject(slot);
        so.FindProperty("emptyColor").colorValue=color;
        so.FindProperty("filledColor").colorValue=color;
        so.ApplyModifiedProperties();
    }

    static T Ref<T>(Object owner,string name) where T:Object => new SerializedObject(owner).FindProperty(name).objectReferenceValue as T;
    static void Rect(Transform t,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
    { var r=(RectTransform)t; r.anchorMin=r.anchorMax=anchor; r.pivot=pivot; r.anchoredPosition=pos; r.sizeDelta=size; r.localScale=Vector3.one; }
    static void Stretch(RectTransform r,float inset)
    { r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=Vector2.one*-inset;r.localScale=Vector3.one; }
    static void RemoveBacking(GameObject go)
    {
        var image=go.GetComponent<Image>(); if(image!=null) Undo.DestroyObjectImmediate(image);
        var outline=go.GetComponent<Outline>(); if(outline!=null) Undo.DestroyObjectImmediate(outline);
    }
    static void Surface(GameObject go,Color color)
    {
        var image=go.GetComponent<Image>() ?? Undo.AddComponent<Image>(go);
        image.sprite=null; image.type=Image.Type.Simple; image.color=color;
        var outline=go.GetComponent<Outline>() ?? Undo.AddComponent<Outline>(go);
        outline.effectColor=new(Accent.r,Accent.g,Accent.b,.32f);outline.effectDistance=new(1,-1);outline.useGraphicAlpha=false;
    }
    static void Label(Transform parent,string name,string value,Vector2 pos,Vector2 size,float font,Color color)
    {
        var child=parent.Find(name);
        if(child==null) { var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); Undo.RegisterCreatedObjectUndo(go,"HUD label"); child=go.transform;child.SetParent(parent,false); }
        Rect(child,new(0,1),new(0,1),pos,size);
        var t=child.GetComponent<TextMeshProUGUI>(); t.text=value;t.fontSize=font;t.color=color;t.raycastTarget=false;t.fontStyle=FontStyles.Normal;
    }
    static void Bar(Image fill,string title,float y,Color color)
    {
        if(fill==null)return;
        var root=fill.transform.parent;
        Rect(root,new(0,0),new(0,0),new(16,y),new(228,8));
        Surface(root.gameObject,new(.025f,.035f,.045f,1));
        Stretch(fill.rectTransform,1);fill.sprite=barSprite;fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillOrigin=0;fill.color=color;
        Label(root,title,title,new(0,23),new(228,20),15,Ink);
    }
}

