using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AdventureMenuSetup
{
    const string Folder = "Assets/Game/UI/Prefabs/Adventure";
    static AdventureMenuSetup() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/AdventureMenuSetup.request")) return;
        File.Delete("Temp/AdventureMenuSetup.request");
        var mode = EditorSettings.serializationMode;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        try { Build(); File.WriteAllText("Temp/AdventureMenuSetup.report", "PASS: authored adventure menu and reusable option prefab assigned to WorldManager."); }
        catch (System.Exception e) { File.WriteAllText("Temp/AdventureMenuSetup.report", e.ToString()); }
        finally { EditorSettings.serializationMode = mode; }
    }
    static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/UI/Prefabs", "Adventure");
        var card = AssetDatabase.LoadAssetAtPath<AdventureOptionUI>(Folder + "/Adventure option.prefab");
        if (card == null)
        {
            var go = new GameObject("Adventure option", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(AdventureOptionUI));
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(696, 100);
            var image = go.GetComponent<Image>(); image.color = Color.white;
            var button = go.GetComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = new Color(.085f,.095f,.11f); colors.highlightedColor = colors.selectedColor = new Color(.18f,.215f,.245f);
            colors.pressedColor = new Color(.24f,.29f,.33f); colors.disabledColor = new Color(.06f,.065f,.075f); colors.fadeDuration = .12f; button.colors = colors;
            go.GetComponent<LayoutElement>().preferredHeight = 100;
            var view = go.GetComponent<AdventureOptionUI>(); view.button = button;
            view.title = Label("Title", go.transform, "Destination", 28, new Color(.93f,.95f,.97f));
            Rect(view.title.rectTransform, new Vector2(22,-16), new Vector2(644,36));
            view.description = Label("Description", go.transform, "Destination description", 20, new Color(.66f,.71f,.76f));
            Rect(view.description.rectTransform, new Vector2(22,-56), new Vector2(644,30));
            var edge = new GameObject("Steel border", typeof(RectTransform), typeof(Image)); edge.transform.SetParent(go.transform,false);
            var er=edge.GetComponent<RectTransform>();er.anchorMin=new Vector2(0,0);er.anchorMax=new Vector2(0,1);er.offsetMin=Vector2.zero;er.offsetMax=new Vector2(3,0);
            edge.GetComponent<Image>().color=new Color(.32f,.4f,.46f);edge.GetComponent<Image>().raycastTarget=false;
            card = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Adventure option.prefab").GetComponent<AdventureOptionUI>();
            Object.DestroyImmediate(go);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<TableAdventureMenuView>(Folder + "/Table adventures.prefab");
        if (prefab == null)
        {
            var go = new GameObject("Table adventures", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(TableAdventureMenuView));
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=800;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var bg=Box("Backdrop",go.transform,new Color(.015f,.02f,.025f,.78f)); Stretch(bg.rectTransform);
            var panel=Box("Panel",go.transform,new Color(.045f,.052f,.062f));var r=panel.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(760,0);
            var border=panel.gameObject.AddComponent<Outline>();border.effectColor=new Color(.25f,.31f,.36f);border.effectDistance=new Vector2(1,-1);
            var layout=panel.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(32,32,30,30);layout.spacing=12;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fit=panel.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var view=go.GetComponent<TableAdventureMenuView>();view.group=go.GetComponent<CanvasGroup>();view.panel=r;view.optionPrefab=card;
            SizedLabel("Eyebrow",r,"THE TABLE",18,new Color(.56f,.66f,.73f),24);
            view.heading=SizedLabel("Heading",r,"Choose your adventure",36,Color.white,48);
            SizedLabel("Subtitle",r,"Choose a destination to enter.",22,new Color(.67f,.72f,.77f),42);
            var options=new GameObject("Destinations",typeof(RectTransform),typeof(VerticalLayoutGroup));options.transform.SetParent(r,false);
            var stack=options.GetComponent<VerticalLayoutGroup>();stack.spacing=12;stack.childControlWidth=stack.childControlHeight=true;stack.childForceExpandHeight=false;
            view.options=options.transform;
            var spacer=new GameObject("Space",typeof(RectTransform),typeof(LayoutElement));spacer.transform.SetParent(r,false);spacer.GetComponent<LayoutElement>().preferredHeight=12;
            view.resume=Action(card,r,"Resume / Cancel");view.returnToRoom=Action(card,r,"Return to room");
            SizedLabel("Help",r,"Arrows to navigate  /  Enter to choose",18,new Color(.53f,.59f,.65f),30);
            prefab=PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Table adventures.prefab").GetComponent<TableAdventureMenuView>();Object.DestroyImmediate(go);
        }
        var world=Object.FindAnyObjectByType<WorldManager>();world.tableAdventureMenu=prefab;EditorUtility.SetDirty(world);EditorSceneManager.MarkSceneDirty(world.gameObject.scene);EditorSceneManager.SaveScene(world.gameObject.scene);AssetDatabase.SaveAssets();
    }
    static AdventureOptionUI Action(AdventureOptionUI prefab, Transform parent, string title)
    {
        var option=((GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject,parent)).GetComponent<AdventureOptionUI>();option.name=title;option.title.text=title;option.description.gameObject.SetActive(false);option.GetComponent<LayoutElement>().preferredHeight=56;
        Rect(option.title.rectTransform,new Vector2(22,-10),new Vector2(644,36)); option.title.fontSize=23;return option;
    }
    static Image Box(string name,Transform parent,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var image=go.GetComponent<Image>();image.color=color;return image;}
    static Text Label(string name,Transform parent,string text,int size,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);var label=go.GetComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text=text;label.fontSize=size;label.color=color;label.raycastTarget=false;return label;}
    static Text SizedLabel(string name,Transform parent,string text,int size,Color color,float height){var label=Label(name,parent,text,size,color);label.gameObject.AddComponent<LayoutElement>().preferredHeight=height;return label;}
    static void Rect(RectTransform r,Vector2 pos,Vector2 size){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;}
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
}
