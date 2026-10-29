using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds Development/Resources/Dev Spawner.prefab, the spawn window (DevSpawner, F10), laid out like Asset Studio in the
// inventory's look: the plain grey frame and slots, gold PixelOperator-Bold titles, PixelOperator text. Left the kinds and
// their groups, middle the search and a scrolling grid, right the picked entry and its buttons. Edit the prefab freely
// afterwards; running this again replaces it. RefreshLists() (from an eval) only refills the enemy and room lists that
// development builds use (the Editor finds them itself).
public static class DevSpawnerBuilder
{
    const string Folder = "Assets/Game/Development/Resources";
    const string Path = Folder + "/Dev Spawner.prefab";
    const string Fonts = "Assets/Game/UI/Fonts/", Sprites = "Assets/Game/UI/Sprites/Inventory/";
    static readonly Color Gold = new(1f, .851f, .4f), Text = new(.902f, .902f, .902f), Dim = new(.847f, .8f, .706f), Warn = new(.92f, .6f, .45f);
    const float W = 1700, H = 960, Left = 30, LeftWidth = 260, Middle = 314, Right = 430;

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
        var panel = Stretch("Panel", root.transform, Vector2.zero, Vector2.zero);
        panel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .6f);
        var window = new GameObject("Window", typeof(RectTransform)).GetComponent<RectTransform>();
        window.SetParent(panel, false);
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
        window.sizeDelta = new Vector2(W, H);
        var wi = window.gameObject.AddComponent<Image>();
        wi.sprite = frame; wi.type = Image.Type.Tiled; wi.pixelsPerUnitMultiplier = 1f;

        var title = Label("Title", window, "SPAWN", bold, 44, Gold, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, 34, 24, 220, 50);
        var keys = Label("Keys", window, "Click picks.  Double-click or Enter uses it.  Right-click: its second button.  Arrows move.  Esc clears the search, then closes.",
            body, 20, Dim, TextAlignmentOptions.TopLeft);
        Place(keys.rectTransform, 210, 38, 1300, 28);

        // Left: the kinds, then the picked kind's groups.
        var left = new GameObject("Left", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        left.SetParent(window, false);
        Place(left, Left, 96, LeftWidth, H - 96 - 30);
        var lv = left.GetComponent<VerticalLayoutGroup>();
        lv.spacing = 8; lv.childControlWidth = lv.childControlHeight = true; lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;
        var kinds = new[] { "Items", "Spells", "Room items", "Enemies", "Rooms" }.Select(n => CountedButton(n, left, n, 56, 28)).ToArray();
        var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
        gap.transform.SetParent(left, false);
        gap.GetComponent<LayoutElement>().preferredHeight = 14;
        var groupsTitle = Label("Groups title", left, "SHOW", bold, 22, Dim, TextAlignmentOptions.BottomLeft);
        groupsTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
        var groups = new GameObject("Groups", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        groups.SetParent(left, false);
        var gv = groups.GetComponent<VerticalLayoutGroup>();
        gv.spacing = 6; gv.childControlWidth = gv.childControlHeight = true; gv.childForceExpandWidth = true; gv.childForceExpandHeight = false;
        var groupTemplate = CountedButton("Group", groups, "Group", 44, 23);

        // Middle: search, what is shown, the grid, the last thing done.
        float midWidth = W - Middle - Right - 20;
        var searchGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        searchGo.name = "Search";
        searchGo.transform.SetParent(window, false);
        Place((RectTransform)searchGo.transform, Middle, 96, midWidth, 56);
        var si = searchGo.GetComponent<Image>();
        si.sprite = slot; si.type = Image.Type.Sliced; si.pixelsPerUnitMultiplier = 1f; si.color = new Color(.75f, .75f, .78f);
        var search = searchGo.GetComponent<TMP_InputField>();
        foreach (var t in searchGo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font = body; t.fontSize = 28;
            t.color = t.gameObject.name == "Placeholder" ? new Color(.6f, .6f, .6f) : Color.white;
            if (t.gameObject.name == "Placeholder") { t.text = "Type to search"; t.fontStyle = FontStyles.Normal; }
        }
        search.pointSize = 28;
        search.lineType = TMP_InputField.LineType.SingleLine;
        search.restoreOriginalTextOnEscape = false;
        search.caretWidth = 2;

        var listTitle = Label("List title", window, "Items", bold, 26, Text, TextAlignmentOptions.MidlineLeft);
        Place(listTitle.rectTransform, Middle + 4, 162, midWidth, 34);
        listTitle.richText = true;

        var scrollGo = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(window, false);
        var sr = (RectTransform)scrollGo.transform;
        Place(sr, Middle, 202, midWidth, H - 202 - 84);
        var listBack = scrollGo.GetComponent<Image>();
        listBack.sprite = slot; listBack.type = Image.Type.Sliced; listBack.pixelsPerUnitMultiplier = 1f; listBack.color = new Color(.5f, .5f, .53f);
        var viewport = Stretch("Viewport", sr, new Vector2(8, 8), new Vector2(-30, -8));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);   // takes the wheel between cells
        var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(136, 158); grid.spacing = new Vector2(10, 10); grid.padding = new RectOffset(10, 10, 10, 10);
        grid.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // A scrollbar down the list's right edge.
        var barGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        barGo.transform.SetParent(sr, false);
        var br = (RectTransform)barGo.transform;
        br.anchorMin = new Vector2(1, 0); br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(1, .5f);
        br.offsetMin = new Vector2(-26, 10); br.offsetMax = new Vector2(-8, -10);
        barGo.GetComponent<Image>().color = new Color(.06f, .06f, .07f, .9f);
        var handleArea = Stretch("Sliding area", br, Vector2.zero, Vector2.zero);
        var handle = Stretch("Handle", handleArea, Vector2.zero, Vector2.zero);
        var hi = handle.gameObject.AddComponent<Image>();
        hi.sprite = slot; hi.type = Image.Type.Sliced; hi.pixelsPerUnitMultiplier = 1f;
        var bar = barGo.GetComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = handle; bar.targetGraphic = hi;
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 48f;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scroll.verticalScrollbarSpacing = 4;

        // One cell: a slot with the icon (or the Editor's picture) and the name under it, a gold dot when carried.
        var cell = new GameObject("Cell", typeof(RectTransform), typeof(Image));
        cell.transform.SetParent(content, false);
        var ci = cell.GetComponent<Image>();
        ci.sprite = slot; ci.type = Image.Type.Sliced; ci.pixelsPerUnitMultiplier = 1f;
        Picture(cell.transform, 104, 10);
        var name = Label("Name", cell.transform, "Name", body, 20, Text, TextAlignmentOptions.Top);
        var nr = name.rectTransform;
        nr.anchorMin = new Vector2(0, 0); nr.anchorMax = new Vector2(1, 0); nr.pivot = new Vector2(.5f, 0);
        nr.offsetMin = new Vector2(6, 6); nr.offsetMax = new Vector2(-6, 50);
        name.textWrappingMode = TextWrappingModes.Normal; name.overflowMode = TextOverflowModes.Ellipsis; name.lineSpacing = -14;
        var dot = new GameObject("Marked", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        dot.transform.SetParent(cell.transform, false);
        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = dot.rectTransform.pivot = new Vector2(1, 1);
        dot.rectTransform.sizeDelta = new Vector2(12, 12); dot.rectTransform.anchoredPosition = new Vector2(-10, -10);
        dot.color = Gold; dot.raycastTarget = false; dot.enabled = false;

        var status = Label("Status", window, "", bold, 24, Gold, TextAlignmentOptions.MidlineLeft);
        Place(status.rectTransform, Middle + 4, H - 74, midWidth, 44);
        status.textWrappingMode = TextWrappingModes.Normal; status.overflowMode = TextOverflowModes.Ellipsis;

        // Right: the picked entry.
        var side = new GameObject("Detail panel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        side.SetParent(window, false);
        Place(side, W - Right - 30 + 20, 96, Right - 20, H - 96 - 30);
        var sideImage = side.GetComponent<Image>();
        sideImage.sprite = slot; sideImage.type = Image.Type.Sliced; sideImage.pixelsPerUnitMultiplier = 1f; sideImage.color = new Color(.5f, .5f, .53f);

        var empty = Label("Nothing picked", side, "Pick something in the list.", body, 24, Dim, TextAlignmentOptions.Center);
        var er = empty.rectTransform; er.anchorMin = Vector2.zero; er.anchorMax = Vector2.one; er.offsetMin = er.offsetMax = Vector2.zero;

        var detail = Stretch("Detail", side, Vector2.zero, Vector2.zero);
        var dv = detail.gameObject.AddComponent<VerticalLayoutGroup>();
        dv.padding = new RectOffset(22, 22, 20, 20); dv.spacing = 10;
        dv.childControlWidth = dv.childControlHeight = true; dv.childForceExpandWidth = true; dv.childForceExpandHeight = false;
        var pictureBox = new GameObject("Picture", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        pictureBox.transform.SetParent(detail, false);
        var pictureLayout = pictureBox.GetComponent<LayoutElement>();
        pictureLayout.preferredHeight = pictureLayout.minHeight = 214;
        var pb = pictureBox.GetComponent<Image>();
        pb.sprite = slot; pb.type = Image.Type.Sliced; pb.pixelsPerUnitMultiplier = 1f; pb.color = new Color(.65f, .65f, .68f); pb.raycastTarget = false;
        var (detailIcon, detailPreview) = Picture(pictureBox.transform, 196, 9);
        var detailName = Label("Name", detail, "Name", bold, 32, Gold, TextAlignmentOptions.TopLeft);
        detailName.textWrappingMode = TextWrappingModes.Normal;
        var detailKind = Label("Kind", detail, "Kind", body, 22, Dim, TextAlignmentOptions.TopLeft);
        detailKind.textWrappingMode = TextWrappingModes.Normal;
        var detailNote = Label("Note", detail, "", body, 22, Warn, TextAlignmentOptions.TopLeft);
        detailNote.textWrappingMode = TextWrappingModes.Normal;
        // The text takes what room is left and is cut with an ellipsis; the picture and buttons keep their size.
        var detailText = Label("Text", detail, "", body, 22, Text, TextAlignmentOptions.TopLeft);
        detailText.textWrappingMode = TextWrappingModes.Normal; detailText.overflowMode = TextOverflowModes.Ellipsis; detailText.richText = true;
        var textLayout = detailText.gameObject.AddComponent<LayoutElement>();
        textLayout.flexibleHeight = 1; textLayout.minHeight = 30; textLayout.preferredHeight = 30;

        var countRow = new GameObject("Count row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
        countRow.SetParent(detail, false);
        var countLayout = countRow.GetComponent<LayoutElement>();
        countLayout.preferredHeight = countLayout.minHeight = 48;
        var ch = countRow.GetComponent<HorizontalLayoutGroup>();
        ch.spacing = 8; ch.childAlignment = TextAnchor.MiddleLeft; ch.childControlWidth = ch.childControlHeight = true; ch.childForceExpandWidth = ch.childForceExpandHeight = false;
        var countName = Label("Count title", countRow, "How many", body, 24, Dim, TextAlignmentOptions.MidlineLeft);
        countName.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        var countDown = Button("Less", countRow, "-", 30);
        Size(countDown, 52, 46);
        var countLabel = Label("Count", countRow, "1", bold, 30, Text, TextAlignmentOptions.Center);
        countLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 64;
        var countUp = Button("More", countRow, "+", 30);
        Size(countUp, 52, 46);

        var rarityRow = new GameObject("Rarity", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
        rarityRow.SetParent(detail, false);
        var rarityLayout = rarityRow.GetComponent<LayoutElement>();
        rarityLayout.preferredHeight = rarityLayout.minHeight = 92;
        var rg = rarityRow.GetComponent<GridLayoutGroup>();
        rg.cellSize = new Vector2(118, 42); rg.spacing = new Vector2(6, 6); rg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; rg.constraintCount = 3;
        var rarities = new[] { "Common", "Uncommon", "Rare", "Unique", "Legendary" }.Select(n => Button(n, rarityRow, n, 22)).ToArray();

        var primary = Button("Primary", detail, "Add to pack", 28);
        Size(primary, 0, 62);
        Find<TMP_Text>(primary.gameObject, "Label").font = bold;
        var secondary = Button("Secondary", detail, "Drop on the floor", 24);
        Size(secondary, 0, 50);

        var spawner = root.AddComponent<DevSpawner>();
        var so = new SerializedObject(spawner);
        void Ref(string field, Object value) => so.FindProperty(field).objectReferenceValue = value;
        Ref("panel", panel.gameObject); Ref("search", search); Ref("listTitle", listTitle); Ref("status", status);
        SetArray(so.FindProperty("kindButtons"), kinds);
        Ref("groupList", groups); Ref("groupTemplate", groupTemplate); Ref("groupTitle", groupsTitle.gameObject);
        Ref("scroll", scroll); Ref("content", content); Ref("cellTemplate", cell);
        Ref("detail", detail.gameObject); Ref("detailEmpty", empty.gameObject);
        Ref("detailIcon", detailIcon); Ref("detailPreview", detailPreview);
        Ref("detailName", detailName); Ref("detailKind", detailKind); Ref("detailText", detailText); Ref("detailNote", detailNote);
        Ref("countRow", countRow.gameObject); Ref("countLabel", countLabel); Ref("countDown", countDown); Ref("countUp", countUp);
        Ref("rarityRow", rarityRow.gameObject); SetArray(so.FindProperty("rarityButtons"), rarities);
        Ref("primary", primary); Ref("secondary", secondary);
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
        SetStrings(so.FindProperty("enemyGroups"), enemies.Select(DevSpawner.EnemyGroup).ToArray());
        var rooms = DevConsole.FindTemplatePrefabs().Where(p => DevSpawner.RoomGroup(p) != null).ToArray();
        SetArray(so.FindProperty("rooms"), rooms);
        SetStrings(so.FindProperty("roomGroups"), rooms.Select(DevSpawner.RoomGroup).ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{enemies.Length} enemies, {rooms.Length} rooms";
    }

    static void SetArray(SerializedProperty list, Object[] values)
    {
        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void SetStrings(SerializedProperty list, string[] values)
    {
        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).stringValue = values[i];
    }

    static T Find<T>(GameObject root, string name) where T : Component => root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.gameObject.name == name);

    // Filling its parent, with these insets.
    static RectTransform Stretch(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = offsetMin; r.offsetMax = offsetMax;
        return r;
    }

    // At x, y from the parent's top-left corner, this wide and tall.
    static void Place(RectTransform r, float x, float y, float width, float height)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = new Vector2(x, -y);
    }

    // An icon and the Editor's picture in one square, top-middle of the parent.
    static (Image, RawImage) Picture(Transform parent, float size, float top)
    {
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(parent, false);
        var preview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        preview.transform.SetParent(parent, false);
        foreach (var r in new[] { icon.rectTransform, preview.rectTransform })
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(size, size); r.anchoredPosition = new Vector2(0, -top);
        }
        icon.raycastTarget = preview.raycastTarget = false; icon.preserveAspect = true;
        return (icon, preview);
    }

    static TextMeshProUGUI Label(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }

    static void Size(Button b, float width, float height)
    {
        var le = b.GetComponent<LayoutElement>();
        if (width > 0) le.preferredWidth = width;
        le.preferredHeight = height; le.minHeight = height;
    }

    static Button Button(string name, Transform parent, string text, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = slot; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f;
        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, 1f, 1f); colors.pressedColor = new Color(.7f, .7f, .7f);
        colors.selectedColor = Color.white; colors.disabledColor = new Color(.6f, .6f, .6f); colors.colorMultiplier = 1.35f;
        button.colors = colors;
        var label = Label("Label", go.transform, text, body, fontSize, Text, TextAlignmentOptions.Center);
        var lr = label.rectTransform;
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(10, 0); lr.offsetMax = new Vector2(-10, 0);
        return button;
    }

    // A full-width button with its name at the left and a count at the right (the kinds and groups).
    static Button CountedButton(string name, Transform parent, string text, float height, float fontSize)
    {
        var b = Button(name, parent, text, fontSize);
        Size(b, 0, height);
        var label = Find<TMP_Text>(b.gameObject, "Label");
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.offsetMin = new Vector2(16, 0);
        var count = Label("Count", b.transform, "", body, fontSize - 4, Dim, TextAlignmentOptions.MidlineRight);
        var cr = count.rectTransform;
        cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = new Vector2(10, 0); cr.offsetMax = new Vector2(-16, 0);
        return b;
    }
}
