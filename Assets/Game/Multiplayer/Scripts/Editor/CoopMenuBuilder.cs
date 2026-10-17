using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the co-op screen (CoopMenuUI) next to the pause menu, out of the pause menu's own pieces (its panel, title,
// buttons and hint), and puts a Co-op button in the pause menu. Run by Set Up Co-op; skipped when it already exists.
public static class CoopMenuBuilder
{
    public static void Build()
    {
        var pause = Object.FindAnyObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
        if (pause == null) { Debug.LogWarning("[Coop] No pause menu in the scene: the co-op screen is not built."); return; }
        var screen = pause.transform.Find("Pause screen");
        var panel = screen != null ? screen.Find("Panel") : null;
        var resume = panel != null ? panel.Find("Resume") : null;
        if (resume == null) { Debug.LogWarning("[Coop] The pause menu has no Pause screen/Panel/Resume to copy."); return; }

        PauseButton(pause, panel, resume);
        PartyHud(pause.transform.parent);
        if (Object.FindAnyObjectByType<CoopMenuUI>(FindObjectsInactive.Include) != null) return;
        Screen(pause, screen, panel, resume);
    }

    [MenuItem("Tools/LoomRoom/Multiplayer/Rebuild Co-op Screen")]
    public static void Rebuild()
    {
        var old = Object.FindAnyObjectByType<CoopMenuUI>(FindObjectsInactive.Include);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Build();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    // The co-op screen: the pause screen's backing and panel with three pages (start, the host's game, a guest's view).
    static void Screen(PauseMenuUI pause, Transform screen, Transform panel, Transform resume)
    {
        var menu = new GameObject("Co-op menu", typeof(RectTransform));
        menu.transform.SetParent(pause.transform.parent, false);
        Stretch((RectTransform)menu.transform);
        var ui = menu.AddComponent<CoopMenuUI>();

        var root = Object.Instantiate(screen.gameObject, menu.transform);
        root.name = "Co-op screen";
        for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        var box = Object.Instantiate(panel.gameObject, root.transform);
        box.name = "Panel";
        for (int i = box.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(box.transform.GetChild(i).gameObject);
        ((RectTransform)box.transform).sizeDelta = new Vector2(620, 720);

        var hint = panel.Find("Hint");
        var hintText = hint.GetComponent<TMP_Text>();
        var gold = new Color(.91f, .78f, .48f);
        Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            Stretch((RectTransform)g.transform);
            return g.transform;
        }
        TMP_Text Line(string name, Transform parent, string text, float y, float height = 30, float size = 0, Color? color = null)
        {
            var t = Copy(hint, parent, name, new Vector2(0, y), new Vector2(560, height)).GetComponent<TMP_Text>();
            t.text = text; t.textWrappingMode = TextWrappingModes.Normal; t.alignment = TextAlignmentOptions.Center;
            if (size > 0) t.fontSize = size;
            if (color.HasValue) t.color = color.Value;
            return t;
        }
        Button MakeButton(string name, Transform parent, string label, Vector2 at, float width = 320)
        {
            var b = Object.Instantiate(resume.gameObject, parent);
            b.name = name;
            var rt = (RectTransform)b.transform;
            rt.anchoredPosition = at; rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            var text = b.GetComponentInChildren<TMP_Text>();
            if (text != null) { text.text = label; ((RectTransform)text.transform).sizeDelta = new Vector2(width, rt.sizeDelta.y); }
            var button = b.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            return button;
        }

        var title = Copy(panel.Find("Title"), box.transform, "Title", new Vector2(0, 310)).GetComponent<TMP_Text>();
        var status = Line("Status", box.transform, "", 255, 56);

        // Start: name, host, or join with a code.
        var start = Group("Start page", box.transform);
        Line("Name caption", start, "YOUR NAME", 200, 26, 18);
        var playerName = AddressField(start, hintText, new Vector2(0, 166));
        playerName.name = "Name";
        playerName.placeholder.GetComponent<TMP_Text>().text = "Your name, over your head";
        playerName.characterLimit = 20;
        var host = MakeButton("Host", start, "Host a game", new Vector2(0, 85), 400);
        Line("Host caption", start, "Friends join you with a code.", 44, 26, 18);
        Line("Or", start, "OR JOIN A FRIEND", -15, 26, 18);
        var address = AddressField(start, hintText, new Vector2(-85, -62));
        ((RectTransform)address.transform).sizeDelta = new Vector2(330, 52);
        address.placeholder.GetComponent<TMP_Text>().text = "Their join code";
        address.characterLimit = 64;
        var join = MakeButton("Join", start, "Join", new Vector2(175, -62), 160);
        Line("Join caption", start, "On the same network, their address works too.", -110, 26, 16);

        // A game: the host's code, who is in, the rules.
        var game = Group("Game page", box.transform);
        var code = Group("Code", game);
        var codeCaption = Line("Code caption", code, "JOIN CODE", 205, 26, 18);
        var codeText = Copy(panel.Find("Title"), code, "Code", new Vector2(0, 165), new Vector2(560, 56)).GetComponent<TMP_Text>();
        codeText.text = "ABC123"; codeText.color = gold; codeText.fontSize = 48;
        var copy = MakeButton("Copy", code, "Copy", new Vector2(0, 108), 200);
        var playersHeader = Line("Players caption", game, "PLAYERS", 60, 26, 18);
        var players = Line("Players", game, "", 5, 84);
        var rules = Group("Rules", game);
        Line("Rules caption", rules, "RULES (CLICK TO CHANGE)", -45, 26, 18);
        var loot = MakeButton("Loot rule", rules, "Loot", new Vector2(0, -85), 560);
        var adventurer = MakeButton("Adventurer rule", rules, "Classes", new Vector2(0, -137), 560);
        var progress = MakeButton("Progress rule", rules, "Progress", new Vector2(0, -189), 560);
        var spoils = MakeButton("Spoils rule", rules, "Spoils", new Vector2(0, -241), 560);
        var guestRules = Line("Guest rules", game, "", -150, 170);
        guestRules.lineSpacing = 20;

        // The bottom row (CoopMenuUI places them).
        var begin = MakeButton("Begin", box.transform, "Begin", new Vector2(-145, -300), 270);
        var alone = MakeButton("Alone", box.transform, "Play alone", new Vector2(145, -300), 270);
        var leave = MakeButton("Leave", box.transform, "Leave the game", new Vector2(145, -300), 270);
        var close = MakeButton("Close", box.transform, "Back", new Vector2(-145, -300), 270);

        var so = new SerializedObject(ui);
        void Set(string field, Object value) => so.FindProperty(field).objectReferenceValue = value;
        // The lobby's blank backdrop: over the whole screen, behind the box.
        var backdrop = root.transform.Find("Lobby backdrop");
        if (backdrop == null)
        {
            var go = new GameObject("Lobby backdrop", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.layer = root.layer;
            go.transform.SetParent(root.transform, false);
            var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(.02f, .045f, .05f, 1f);
            backdrop = go.transform;
        }
        backdrop.SetAsFirstSibling();
        backdrop.gameObject.SetActive(false);
        Set("lobbyBackdrop", backdrop.gameObject);
        Set("root", root); Set("title", title); Set("status", status);
        Set("startPage", start.gameObject); Set("playerName", playerName); Set("address", address);
        Set("hostButton", host); Set("joinButton", join);
        Set("gamePage", game.gameObject); Set("codeGroup", code.gameObject); Set("codeCaption", codeCaption); Set("codeText", codeText);
        Set("playersHeader", playersHeader); Set("playerList", players); Set("copyButton", copy);
        Set("hostRules", rules.gameObject); Set("sharedLootButton", loot); Set("hostAdventurerButton", adventurer); Set("partyProgressionButton", progress); Set("sharedSpoilsButton", spoils);
        Set("guestRules", guestRules);
        Set("beginButton", begin); Set("aloneButton", alone); Set("leaveButton", leave); Set("closeButton", close);
        so.ApplyModifiedPropertiesWithoutUndo();
        Layout(ui);
        root.SetActive(false);
        Undo.RegisterCreatedObjectUndo(menu, "Co-op screen");
    }

    // The game page's layout, top to bottom with room between every row: a small title in the body font, the status,
    // the join code with Copy beside it, the players on one line, the four rules, then the bottom row. Safe to run again.
    [MenuItem("Tools/LoomRoom/Multiplayer/Lay Out Co-op Screen")]
    public static void LayOut()
    {
        var ui = Object.FindAnyObjectByType<CoopMenuUI>(FindObjectsInactive.Include);
        if (ui == null) { Debug.LogWarning("[Coop] No co-op screen in the open scenes."); return; }
        Layout(ui);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
    }

    static void Layout(CoopMenuUI ui)
    {
        var box = ui.transform.Find("Co-op screen/Panel");
        if (box == null) { Debug.LogWarning("[Coop] The co-op screen has no Co-op screen/Panel."); return; }
        // The pause panel it was copied from lays its buttons out in a column; this screen places its own.
        foreach (var c in box.GetComponents<LayoutGroup>()) Object.DestroyImmediate(c);
        foreach (var c in box.GetComponents<ContentSizeFitter>()) Object.DestroyImmediate(c);
        foreach (Transform child in box) foreach (var c in child.GetComponents<LayoutElement>()) Object.DestroyImmediate(c);
        ((RectTransform)box).sizeDelta = new Vector2(620, 700);
        var game = box.Find("Game page");
        var body = box.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Status");
        void Place(Transform parent, string path, float x, float y, float w = -1, float h = -1)
        {
            var t = parent != null ? parent.Find(path) as RectTransform : null;
            if (t == null) { Debug.LogWarning($"[Coop] Co-op screen: no {path}."); return; }
            t.anchoredPosition = new Vector2(x, y);
            if (w > 0 || h > 0) t.sizeDelta = new Vector2(w > 0 ? w : t.sizeDelta.x, h > 0 ? h : t.sizeDelta.y);
            var label = t.GetComponent<Button>() != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
            if (label != null) ((RectTransform)label.transform).sizeDelta = t.sizeDelta;
        }

        // Title: the body font, gold, a modest size (the pause menu's big pixel title was too loud here).
        var title = box.Find("Title")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            if (body != null) { title.font = body.font; title.fontSharedMaterial = body.font.material; }
            title.fontSize = 34; title.enableAutoSizing = false; title.color = new Color(.91f, .78f, .48f);
            title.alignment = TextAlignmentOptions.Center;
        }
        Place(box, "Title", 0, 308, 560, 44);
        Place(box, "Status", 0, 264, 560, 44);

        // The start page sits under the status as before, just lower for the taller panel.
        var start = box.Find("Start page");
        if (start != null) ((RectTransform)start).anchoredPosition = new Vector2(0, -10);

        // The code with Copy beside it.
        var code = game != null ? game.Find("Code") : null;
        Place(code, "Code caption", -90, 230, 300, 26);
        Place(code, "Code", -90, 194, 300, 52);
        var codeText = code != null ? code.Find("Code")?.GetComponent<TMP_Text>() : null;
        // A long LAN address shrinks to fit beside Copy.
        if (codeText != null) { codeText.enableAutoSizing = true; codeText.fontSizeMax = 40; codeText.fontSizeMin = 16; codeText.textWrappingMode = TextWrappingModes.NoWrap; codeText.alignment = TextAlignmentOptions.Center; }
        Place(code, "Copy", 150, 194, 160, 58);

        // The players on one line (wrapping to a second with four long names).
        Place(game, "Players caption", 0, 146);
        Place(game, "Players", 0, 112, 560, 50);

        // The rules, 58 tall on a 68 pitch.
        var rules = game != null ? game.Find("Rules") : null;
        Place(rules, "Rules caption", 0, 70);
        string[] rows = { "Loot rule", "Adventurer rule", "Progress rule", "Spoils rule" };
        for (int i = 0; i < rows.Length; i++) Place(rules, rows[i], 0, 28 - i * 68, 560, 58);
        Place(game, "Guest rules", 0, -66, 560, 220);

        // The bottom row, well clear of the last rule (CoopMenuUI places the buttons at bottomY).
        var so = new SerializedObject(ui);
        so.FindProperty("bottomY").floatValue = -282f;
        so.FindProperty("bottomSpread").floatValue = 145f;
        so.ApplyModifiedPropertiesWithoutUndo();
        foreach (var name in new[] { "Begin", "Alone", "Leave", "Close" }) Place(box, name, ((RectTransform)box.Find(name)).anchoredPosition.x, -282);
        EditorUtility.SetDirty(ui);
    }

    // A Co-op button under Settings; the buttons below move down to make room.
    static void PauseButton(PauseMenuUI pause, Transform panel, Transform resume)
    {
        var so = new SerializedObject(pause);
        if (so.FindProperty("coopButton").objectReferenceValue != null) return;
        var settings = panel.Find("Settings");
        var coop = Object.Instantiate(settings != null ? settings.gameObject : resume.gameObject, panel);
        coop.name = "Co-op";
        coop.transform.SetSiblingIndex((settings != null ? settings : resume).GetSiblingIndex() + 1);
        var label = coop.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "Co-op";
        var button = coop.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        float step = 70f;
        var panelRect = (RectTransform)panel;
        panelRect.sizeDelta += new Vector2(0, step);
        // The title goes up and the hint down half a step; the buttons are laid out again in order, Co-op under Settings.
        foreach (RectTransform child in panel)
        {
            if (child.anchorMin != child.anchorMax || child.GetComponent<Button>() != null) continue;   // stretched pieces grow with the panel
            child.anchoredPosition += Vector2.up * (child.anchoredPosition.y > 0 ? step * .5f : -step * .5f);
        }
        LayOutPauseButtons(panel, (RectTransform)coop.transform, settings, step);
        so.FindProperty("coopButton").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The pause menu's shown buttons one under another at step, from the top one's place (half a step higher, as the
    // panel grew), Co-op right under Settings; hidden ones keep their place.
    public static void LayOutPauseButtons(Transform panel, RectTransform coop, Transform settings, float step = 70f)
    {
        var buttons = new System.Collections.Generic.List<RectTransform>();
        foreach (RectTransform child in panel)
            if (child != coop && child.GetComponent<Button>() != null && child.gameObject.activeSelf) buttons.Add(child);
        if (buttons.Count == 0) return;
        buttons.Sort((x, y) => y.anchoredPosition.y.CompareTo(x.anchoredPosition.y));
        float top = buttons[0].anchoredPosition.y + step * .5f;
        int at = settings != null ? buttons.IndexOf((RectTransform)settings) + 1 : 1;
        if (coop != null) buttons.Insert(Mathf.Clamp(at, 0, buttons.Count), coop);
        for (int i = 0; i < buttons.Count; i++) buttons[i].anchoredPosition = new Vector2(buttons[i].anchoredPosition.x, top - i * step);
    }

    // The other players' rows at the top left, made from the HUD's own health bar.
    static void PartyHud(Transform canvas)
    {
        var existing = Object.FindAnyObjectByType<PartyHudUI>(FindObjectsInactive.Include);
        // A half-built set (a failed build) is made again.
        if (existing != null && existing.transform.childCount < 3) { Object.DestroyImmediate(existing.gameObject); existing = null; }
        if (existing != null) { PlaceParty((RectTransform)existing.transform); JoinCodeLabel(existing); return; }
        var vitals = canvas.Find("PlayerVitals");
        var health = vitals != null ? vitals.Find("Health") : null;
        if (health == null) { Debug.LogWarning("[Coop] No Canvas/PlayerVitals/Health to make the party rows from."); return; }
        var party = new GameObject("Party", typeof(RectTransform));
        party.transform.SetParent(canvas, false);
        var rt = (RectTransform)party.transform;
        PlaceParty(rt);
        var ui = party.AddComponent<PartyHudUI>();
        var so = new SerializedObject(ui);
        var list = so.FindProperty("rows");
        list.arraySize = 3;
        var bodyFont = AssetDatabase.FindAssets("PixelOperator t:TMP_FontAsset").Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(f => f != null && f.name == "PixelOperator");
        for (int i = 0; i < 3; i++)
        {
            var bar = Object.Instantiate(health.gameObject, party.transform);
            bar.name = $"Player {i + 2}";
            var br = (RectTransform)bar.transform;
            br.anchorMin = br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 1);
            br.anchoredPosition = new Vector2(0, -50 - i * 56); br.sizeDelta = new Vector2(220, 18);
            var trail = bar.transform.Find("Trail"); if (trail != null) Object.DestroyImmediate(trail.gameObject);
            // The bar's own label (gone from the newer health bar: then a copy of its value text).
            var labelT = bar.transform.Find("HEALTH");
            if (labelT == null) { labelT = Object.Instantiate(bar.transform.Find("Value").gameObject, bar.transform).transform; ((RectTransform)labelT).anchorMin = ((RectTransform)labelT).anchorMax = ((RectTransform)labelT).pivot = new Vector2(0, 0); ((RectTransform)labelT).anchoredPosition = new Vector2(0, 20); ((RectTransform)labelT).sizeDelta = new Vector2(220, 24); labelT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Left; }
            var label = labelT.GetComponent<TMP_Text>();
            label.gameObject.name = "Name"; label.text = $"Player {i + 2}";
            label.gameObject.SetActive(true);   // the health bar's own label is hidden
            if (bodyFont != null) label.font = bodyFont;
            label.fontSize = 18;
            var value = bar.transform.Find("Value").GetComponent<TMP_Text>();
            value.text = ""; value.fontSize = 14;
            var row = list.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("root").objectReferenceValue = bar;
            row.FindPropertyRelative("name").objectReferenceValue = label;
            row.FindPropertyRelative("fill").objectReferenceValue = bar.transform.Find("Fill").GetComponent<Image>();
            row.FindPropertyRelative("value").objectReferenceValue = value;
            bar.SetActive(false);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        JoinCodeLabel(ui);
    }

    // Lower left above the player's own bars (the quest log has the top left).
    static void PlaceParty(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(32, 150); rt.sizeDelta = new Vector2(240, 180);
    }

    // The host's join code, a line above the rows (from the first row's name label).
    static void JoinCodeLabel(PartyHudUI ui)
    {
        var so = new SerializedObject(ui);
        var field = so.FindProperty("joinCode");
        if (field.objectReferenceValue is TMP_Text placed) { ((RectTransform)placed.transform).anchoredPosition = new Vector2(0, 24); return; }
        var template = ui.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Name");
        if (template == null) return;
        var go = Object.Instantiate(template.gameObject, ui.transform);
        go.name = "Join code";
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, 24); rt.sizeDelta = new Vector2(320, 24);
        var text = go.GetComponent<TMP_Text>();
        text.text = "Join code:"; text.fontSize = 18; text.alignment = TextAlignmentOptions.Left;
        go.SetActive(false);
        field.objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    static RectTransform Copy(Transform template, Transform parent, string name, Vector2 at, Vector2? size = null)
    {
        var go = Object.Instantiate(template.gameObject, parent);
        go.name = name;
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = at;
        if (size.HasValue) rt.sizeDelta = size.Value;
        return rt;
    }

    // An address box in the panel's own style: the slider's inset background and the hint's font.
    static TMP_InputField AddressField(Transform parent, TMP_Text style, Vector2 at)
    {
        var go = new GameObject("Address", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = at; rt.sizeDelta = new Vector2(400, 44);
        var inset = AssetDatabase.FindAssets("Inset t:Sprite", new[] { "Assets/Game/UI/Sprites/Kit" });
        var image = go.GetComponent<Image>();
        if (inset.Length > 0) { image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(inset[0])); image.type = Image.Type.Sliced; }
        image.color = new Color(1, 1, 1, .9f);

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRect = (RectTransform)area.transform;
        areaRect.anchorMin = Vector2.zero; areaRect.anchorMax = Vector2.one; areaRect.offsetMin = new Vector2(12, 4); areaRect.offsetMax = new Vector2(-12, -4);

        TextMeshProUGUI Text(string name, string value, float alpha)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(area.transform, false);
            var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            t.font = style.font; t.fontSize = style.fontSize; t.text = value;
            t.color = new Color(style.color.r, style.color.g, style.color.b, alpha);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }
        var placeholder = Text("Placeholder", "Join code or address", .45f);
        var text = Text("Text", "", 1f);

        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRect; field.textComponent = text; field.placeholder = placeholder;
        field.fontAsset = style.font; field.pointSize = style.fontSize;
        field.characterLimit = 64;
        return field;
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
}
