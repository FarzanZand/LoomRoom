using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds Development/Resources/Dev Spawner.prefab, the spawn window (DevSpawner, F10) in the inventory's look: the plain
// grey frame and slots, gold PixelOperator-Bold titles, PixelOperator text. Edit the prefab freely afterwards; running
// this again replaces it. DevSpawnerBuilder.RefreshLists() (from an eval) only refills its enemy and room lists, which
// development builds use (the Editor finds them itself).
public static class DevSpawnerBuilder
{
    const string Folder = "Assets/Game/Development/Resources";
    const string Path = Folder + "/Dev Spawner.prefab";
    const string Fonts = "Assets/Game/UI/Fonts/", Sprites = "Assets/Game/UI/Sprites/Inventory/";
    static readonly Color Gold = new(1f, .851f, .4f), Text = new(.902f, .902f, .902f), Dim = new(.847f, .8f, .706f);

    static TMP_FontAsset body, bold;
    static Sprite frame, slot;

    public static string Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Development", "Resources");
        body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "PixelOperator.asset");
        bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "PixelOperator-Bold.asset");
        frame = AssetDatabase.LoadAssetAtPath<Sprite>(Sprites + "Plain frame grey.png");
        slot = AssetDatabase.LoadAssetAtPath<Sprite>(Sprites + "Plain slot grey.png");

        var root = new GameObject("Dev Spawner", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 890;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();

        // A dim backdrop over the game, the window in the middle.
        var panel = Rect("Panel", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        panel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .55f);
        var window = Rect("Window", panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1520, 920));
        var wi = window.gameObject.AddComponent<Image>();
        wi.sprite = frame; wi.type = Image.Type.Tiled; wi.pixelsPerUnitMultiplier = 1f;

        var title = Label("Title", window, "SPAWN", bold, 44, Gold, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(34, -26), new Vector2(400, 50));
        var keys = Label("Keys", window, "F10 or Esc to close", body, 22, Dim, TextAlignmentOptions.TopLeft);
        Place(keys.rectTransform, new Vector2(0, 1), new Vector2(210, -38), new Vector2(400, 30));

        // Search, top right.
        var searchGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        searchGo.name = "Search";
        searchGo.transform.SetParent(window, false);
        Place((RectTransform)searchGo.transform, new Vector2(1, 1), new Vector2(-34 - 560, -24), new Vector2(560, 54));
        var si = searchGo.GetComponent<Image>();
        si.sprite = slot; si.type = Image.Type.Sliced; si.pixelsPerUnitMultiplier = 1f; si.color = new Color(.7f, .7f, .72f);
        var search = searchGo.GetComponent<TMP_InputField>();
        foreach (var t in searchGo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font = body; t.fontSize = 28;
            t.color = t.gameObject.name == "Placeholder" ? new Color(.55f, .55f, .55f) : Color.white;
            if (t.gameObject.name == "Placeholder") { t.text = "Search"; t.fontStyle = FontStyles.Normal; }
        }
        search.pointSize = 28;
        search.lineType = TMP_InputField.LineType.SingleLine;
        search.restoreOriginalTextOnEscape = false;

        // Tabs.
        var tabRow = Row("Tabs", window, new Vector2(34, -96), 64, 10);
        var tabs = new[] { "Items", "Spells", "Room items", "Enemies", "Rooms" }.Select(n => Button(n, tabRow, n, 230, 58, 28)).ToArray();

        // Count and the Items tab's rarity / the Rooms tab's category, on one line.
        var options = Row("Options", window, new Vector2(34, -172), 50, 8);
        var countName = Label("Count title", options, "Count", body, 26, Dim, TextAlignmentOptions.MidlineLeft);
        countName.gameObject.AddComponent<LayoutElement>().preferredWidth = 74;
        var countDown = Button("Less", options, "-", 50, 48, 30);
        var countLabel = Label("Count", options, "x1", bold, 28, Text, TextAlignmentOptions.Center);
        countLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
        var countUp = Button("More", options, "+", 50, 48, 30);

        var rarityRow = Row("Rarity", window, new Vector2(380, -172), 50, 8);
        var rarities = new[] { "Common", "Uncommon", "Rare", "Unique", "Legendary" }.Select(n => Button(n, rarityRow, n, 170, 48, 24)).ToArray();
        var categoryRow = Row("Categories", window, new Vector2(380, -172), 50, 8);
        var categoryTemplate = Button("Category", categoryRow, "Category", 150, 48, 24);
        var cf = categoryTemplate.GetComponent<LayoutElement>();
        cf.minWidth = 110; cf.preferredWidth = -1;
        var fit = categoryTemplate.gameObject.AddComponent<HorizontalLayoutGroup>();
        fit.padding = new RectOffset(16, 16, 0, 0); fit.childControlWidth = fit.childControlHeight = true; fit.childForceExpandWidth = false;

        // The list.
        var scrollGo = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(window, false);
        var sr = (RectTransform)scrollGo.transform;
        sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one; sr.offsetMin = new Vector2(30, 110); sr.offsetMax = new Vector2(-30, -238);
        var listBack = scrollGo.GetComponent<Image>();
        listBack.sprite = slot; listBack.type = Image.Type.Sliced; listBack.pixelsPerUnitMultiplier = 1f; listBack.color = new Color(.55f, .55f, .58f);
        var viewport = Rect("Viewport", sr, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        var content = Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(.5f, 1);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(138, 160); grid.spacing = new Vector2(10, 10); grid.padding = new RectOffset(10, 10, 10, 10);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // One cell: a slot with the icon (or the Editor's preview) and the name under it.
        var cell = new GameObject("Cell", typeof(RectTransform), typeof(Image));
        cell.transform.SetParent(content, false);
        var ci = cell.GetComponent<Image>();
        ci.sprite = slot; ci.type = Image.Type.Sliced; ci.pixelsPerUnitMultiplier = 1f;
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(cell.transform, false);
        Place(icon.rectTransform, new Vector2(.5f, 1), new Vector2(-48, -12), new Vector2(96, 96));
        icon.raycastTarget = false; icon.preserveAspect = true;
        var preview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        preview.transform.SetParent(cell.transform, false);
        Place(preview.rectTransform, new Vector2(.5f, 1), new Vector2(-48, -12), new Vector2(96, 96));
        preview.raycastTarget = false;
        var name = Label("Name", cell.transform, "Name", body, 20, Text, TextAlignmentOptions.Top);
        Place(name.rectTransform, new Vector2(0, 0), new Vector2(6, 6), new Vector2(126, 44));
        name.textWrappingMode = TextWrappingModes.Normal; name.overflowMode = TextOverflowModes.Ellipsis; name.lineSpacing = -12;

        // What is under the pointer, and the last thing done.
        var info = Label("Info", window, "", body, 24, Dim, TextAlignmentOptions.MidlineLeft);
        Place(info.rectTransform, new Vector2(0, 0), new Vector2(36, 58), new Vector2(1440, 36));
        var status = Label("Status", window, "", bold, 26, Gold, TextAlignmentOptions.MidlineLeft);
        Place(status.rectTransform, new Vector2(0, 0), new Vector2(36, 22), new Vector2(1440, 36));

        var spawner = root.AddComponent<DevSpawner>();
        var so = new SerializedObject(spawner);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("search").objectReferenceValue = search;
        SetArray(so.FindProperty("tabs"), tabs);
        so.FindProperty("content").objectReferenceValue = content;
        so.FindProperty("cellTemplate").objectReferenceValue = cell;
        so.FindProperty("status").objectReferenceValue = status;
        so.FindProperty("info").objectReferenceValue = info;
        so.FindProperty("countLabel").objectReferenceValue = countLabel;
        so.FindProperty("countDown").objectReferenceValue = countDown;
        so.FindProperty("countUp").objectReferenceValue = countUp;
        so.FindProperty("rarityRow").objectReferenceValue = rarityRow.gameObject;
        SetArray(so.FindProperty("rarityButtons"), rarities);
        so.FindProperty("categoryRow").objectReferenceValue = categoryRow;
        so.FindProperty("categoryTemplate").objectReferenceValue = categoryTemplate;
        so.ApplyModifiedPropertiesWithoutUndo();
        FillLists(spawner);

        PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);
        return Path;
    }

    public static string RefreshLists()
    {
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var spawner = root.GetComponent<DevSpawner>();
            if (spawner == null) return "No DevSpawner on " + Path;
            string said = FillLists(spawner);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            return said;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static string FillLists(DevSpawner spawner)
    {
        var so = new SerializedObject(spawner);
        var enemies = DevSpawner.FindEnemyPrefabs();
        SetArray(so.FindProperty("enemies"), enemies);
        var rooms = DevConsole.FindTemplatePrefabs();
        SetArray(so.FindProperty("rooms"), rooms);
        var categories = so.FindProperty("roomCategories");
        categories.arraySize = rooms.Length;
        for (int i = 0; i < rooms.Length; i++) categories.GetArrayElementAtIndex(i).stringValue = DevSpawner.RoomCategory(rooms[i]);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{enemies.Length} enemies, {rooms.Length} rooms";
    }

    static void SetArray(SerializedProperty list, Object[] values)
    {
        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMaxOrSize)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        r.anchorMin = min; r.anchorMax = max;
        if (min == max) { r.sizeDelta = offsetMaxOrSize; r.anchoredPosition = offsetMin; }
        else { r.offsetMin = offsetMin; r.offsetMax = offsetMaxOrSize; }
        return r;
    }

    // Anchored at a corner (anchor), its top-left at offset from it.
    static void Place(RectTransform r, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0, 1);
        r.sizeDelta = size;
        r.anchoredPosition = offset + new Vector2(0, anchor.y < .5f ? size.y : 0);
    }

    static RectTransform Row(string name, Transform parent, Vector2 topLeft, float height, float spacing)
    {
        var r = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        Place(r, new Vector2(0, 1), topLeft, new Vector2(1400, height));
        var h = r.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        return r;
    }

    static TextMeshProUGUI Label(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }

    static Button Button(string name, Transform parent, string text, float width, float height, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = slot; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f;
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width; le.preferredHeight = height;
        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.38f); colors.pressedColor = new Color(.75f, .75f, .75f); colors.selectedColor = Color.white;
        colors.colorMultiplier = 1.4f;
        button.colors = colors;
        var label = Label("Label", go.transform, text, body, fontSize, Text, TextAlignmentOptions.Center);
        var lr = label.rectTransform;
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
        return button;
    }
}
