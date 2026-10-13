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

        var menu = new GameObject("Co-op menu", typeof(RectTransform));
        menu.transform.SetParent(pause.transform.parent, false);
        Stretch((RectTransform)menu.transform);
        var ui = menu.AddComponent<CoopMenuUI>();

        // The dim full-screen backing and the panel, as the pause screen has them.
        var root = Object.Instantiate(screen.gameObject, menu.transform);
        root.name = "Co-op screen";
        for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        var box = Object.Instantiate(panel.gameObject, root.transform);
        box.name = "Panel";
        for (int i = box.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(box.transform.GetChild(i).gameObject);
        ((RectTransform)box.transform).sizeDelta = new Vector2(620, 720);

        var title = Copy(panel.Find("Title"), box.transform, "Title", new Vector2(0, 310));
        var hint = panel.Find("Hint");
        var status = Copy(hint, box.transform, "Status", new Vector2(0, 250), new Vector2(560, 56));
        status.GetComponent<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
        var players = Copy(hint, box.transform, "Players", new Vector2(0, 150), new Vector2(560, 110));
        players.GetComponent<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;

        var address = AddressField(box.transform, hint.GetComponent<TMP_Text>(), new Vector2(0, 60));

        Button MakeButton(string name, string label, Vector2 at, float width = 320)
        {
            var b = Object.Instantiate(resume.gameObject, box.transform);
            b.name = name;
            var rt = (RectTransform)b.transform;
            rt.anchoredPosition = at; rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            var text = b.GetComponentInChildren<TMP_Text>();
            if (text != null) { text.text = label; ((RectTransform)text.transform).sizeDelta = new Vector2(width, rt.sizeDelta.y); }
            var button = b.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            return button;
        }
        var host = MakeButton("Host", "Host a game", new Vector2(-145, -10), 270);
        var join = MakeButton("Join", "Join", new Vector2(145, -10), 270);
        var begin = MakeButton("Begin", "Begin", new Vector2(-145, -10), 270);
        var alone = MakeButton("Alone", "Play alone", new Vector2(0, -290), 300);
        var leave = MakeButton("Leave", "Leave the game", new Vector2(145, -10), 270);
        var close = MakeButton("Close", "Back", new Vector2(0, -290), 300);
        var loot = MakeButton("Loot rule", "Loot", new Vector2(0, -85), 560);
        var adventurer = MakeButton("Adventurer rule", "Adventurer", new Vector2(0, -150), 560);
        var progress = MakeButton("Progress rule", "Progress", new Vector2(0, -215), 560);

        var so = new SerializedObject(ui);
        void Set(string field, Object value) => so.FindProperty(field).objectReferenceValue = value;
        Set("root", root);
        Set("title", title.GetComponent<TMP_Text>()); Set("status", status.GetComponent<TMP_Text>()); Set("playerList", players.GetComponent<TMP_Text>());
        Set("address", address);
        Set("hostButton", host); Set("joinButton", join); Set("leaveButton", leave); Set("beginButton", begin);
        Set("aloneButton", alone); Set("closeButton", close);
        Set("sharedLootButton", loot); Set("hostAdventurerButton", adventurer); Set("partyProgressionButton", progress);
        so.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(false);
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
        foreach (RectTransform child in panel)
        {
            if (child == (RectTransform)coop.transform) continue;
            var p = child.anchoredPosition;
            // Everything above Co-op goes up half a step, everything below down half a step.
            bool above = p.y > (settings != null ? ((RectTransform)settings).anchoredPosition.y - 1 : 0);
            child.anchoredPosition = p + Vector2.up * (above ? step * .5f : -step * .5f);
        }
        var settingsY = settings != null ? ((RectTransform)settings).anchoredPosition.y : 0;
        ((RectTransform)coop.transform).anchoredPosition = new Vector2(0, settingsY - step);
        so.FindProperty("coopButton").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The other players' rows at the top left, made from the HUD's own health bar.
    static void PartyHud(Transform canvas)
    {
        if (Object.FindAnyObjectByType<PartyHudUI>(FindObjectsInactive.Include) != null) return;
        var vitals = canvas.Find("PlayerVitals");
        var health = vitals != null ? vitals.Find("Health") : null;
        if (health == null) { Debug.LogWarning("[Coop] No Canvas/PlayerVitals/Health to make the party rows from."); return; }
        var party = new GameObject("Party", typeof(RectTransform));
        party.transform.SetParent(canvas, false);
        var rt = (RectTransform)party.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(32, -40); rt.sizeDelta = new Vector2(240, 180);
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
            var label = bar.transform.Find("HEALTH").GetComponent<TMP_Text>();
            label.gameObject.name = "Name"; label.text = $"Player {i + 2}";
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
        var placeholder = Text("Placeholder", "Host's address", .45f);
        var text = Text("Text", "", 1f);

        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRect; field.textComponent = text; field.placeholder = placeholder;
        field.fontAsset = style.font; field.pointSize = style.fontSize;
        field.characterLimit = 64;
        return field;
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
}
