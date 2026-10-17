using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lays out the character panel beside the inventory (EquipmentPanelUI, "Equipment and stats"): identity, vitals and combat
// numbers in groups split by faint dividers (each stat line explains itself on hover, StatTooltipUI), the gear bonuses on two
// lines, a skills header naming its columns (unlocks, rank), the skills with a marker per character sheet milestone
// (AdventureSkillRowUI.milestoneIcons: the unlock's icon or "?", grey until learned; hover for what it unlocks,
// MilestoneMarkerUI), the attributes in two columns under them (AttributeCellUI: score and a + for a level's point,
// the header says how many are left), the food line at the foot, and no controls line. Safe to run again: it places
// what exists, makes the attribute cells once and rebuilds the markers.
public static class CharacterPanelLayout
{
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    const int Markers = 2;   // each skill has two milestones

    public static string Layout()
    {
        var panel = Object.FindAnyObjectByType<EquipmentPanelUI>(FindObjectsInactive.Include);
        if (panel == null) return "no EquipmentPanelUI in the open scene (open Room)";
        var root = (RectTransform)panel.transform;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Character panel layout");
        root.sizeDelta = new Vector2(600f, 930f);
        root.anchoredPosition = new Vector2(root.anchoredPosition.x, 50f);

        const float W = 520f;
        Top(root, "Title", -48f, W, 40f, 28f);
        Top(root, "Identity", -118f, W, 64f, 24f);
        Divider(root, 1, -156f, W);
        Top(root, "Health", -182f, W, 32f, 24f);
        Top(root, "Stamina", -214f, W, 32f, 24f);
        Top(root, "Mana", -246f, W, 32f, 24f);
        Divider(root, 2, -272f, W);
        Top(root, "Damage", -296f, W, 32f, 24f);
        Top(root, "Armor", -328f, W, 32f, 24f);
        Top(root, "Gear bonuses", -370f, W, 50f, 20f);
        Divider(root, 3, -404f, W);
        Top(root, "Skills title", -426f, W, 30f, 21f);
        Divider(root, 4, -752f, W);
        Top(root, "Food regeneration", -896f, W, 44f, 20f);
        // Each stat line explains itself on hover.
        foreach (var (line, kind) in new[] { ("Health", StatTooltipUI.Kind.Health), ("Stamina", StatTooltipUI.Kind.Stamina), ("Mana", StatTooltipUI.Kind.Mana), ("Damage", StatTooltipUI.Kind.Damage), ("Armor", StatTooltipUI.Kind.Armor) })
        {
            var t = root.Find(line);
            if (t == null) continue;
            var tip = t.GetComponent<StatTooltipUI>() ?? Undo.AddComponent<StatTooltipUI>(t.gameObject);
            tip.panel = panel; tip.kind = kind;
            if (t.GetComponent<TMP_Text>() is TMP_Text text) text.raycastTarget = true;
        }
        var bonuses = root.Find("Gear bonuses");
        if (bonuses != null)
        {
            if (bonuses.GetComponent<TMP_Text>() is TMP_Text text) { text.raycastTarget = true; text.alignment = TextAlignmentOptions.TopLeft; }
            if (bonuses.GetComponent<GearBonusesUI>() is GearBonusesUI gear) { var g = new SerializedObject(gear); g.FindProperty("shownInLine").intValue = 2; g.ApplyModifiedProperties(); }
        }
        // The skills header names the columns of the rows under it: the milestone markers and the rank.
        if (root.Find("Skills title")?.GetComponent<TMP_Text>() is TMP_Text header)
        {
            header.alignment = TextAlignmentOptions.Left;
            header.text = "SKILLS<pos=65%><size=70%><color=#9AA3AD>UNLOCKS</color><pos=88%><color=#9AA3AD>RANK</color></size>";
        }
        var controls = root.Find("Controls");
        if (controls != null) controls.gameObject.SetActive(false);

        const float RowH = 36f;
        var skills = root.Find("Skills") as RectTransform;
        int rows = 0;
        if (skills != null)
        {
            int count = skills.childCount;
            Top(root, "Skills", -444f - count * RowH * .5f, W, count * RowH, 0f);
            var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
            for (int i = 0; i < count; i++)
            {
                var row = skills.GetChild(i) as RectTransform;
                var ui = row.GetComponent<AdventureSkillRowUI>();
                if (ui == null) continue;
                row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -RowH * .5f - i * RowH);
                row.sizeDelta = new Vector2(W, RowH - 4f);
                if (ui.icon != null) ((RectTransform)ui.icon.transform).sizeDelta = new Vector2(30f, 30f);
                if (ui.label != null) { var r = (RectTransform)ui.label.transform; r.anchoredPosition = new Vector2(44f, 0f); r.sizeDelta = new Vector2(250f, RowH); ui.label.fontSize = 22f; }
                if (ui.rank != null) { ((RectTransform)ui.rank.transform).sizeDelta = new Vector2(70f, RowH); ui.rank.fontSize = 22f; }

                foreach (Transform old in row) if (old.name.StartsWith("Milestone ")) Undo.DestroyObjectImmediate(old.gameObject);
                var icons = new Image[Markers]; var marks = new TMP_Text[Markers];
                for (int m = 0; m < Markers; m++)
                {
                    var bubble = new GameObject($"Milestone {m + 1}", typeof(RectTransform), typeof(Image));
                    Undo.RegisterCreatedObjectUndo(bubble, "Milestone marker");
                    bubble.layer = row.gameObject.layer;
                    var br = (RectTransform)bubble.transform;
                    br.SetParent(row, false);
                    br.anchorMin = br.anchorMax = br.pivot = new Vector2(1f, .5f);
                    br.anchoredPosition = new Vector2(-80f - (Markers - 1 - m) * 34f, 0f);
                    br.sizeDelta = new Vector2(28f, 28f);
                    var frame = bubble.GetComponent<Image>();
                    frame.sprite = slot; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 2f; frame.raycastTarget = true;
                    var marker = bubble.AddComponent<MilestoneMarkerUI>(); marker.row = ui; marker.index = m;

                    var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    icon.layer = row.gameObject.layer;
                    var ir = (RectTransform)icon.transform;
                    ir.SetParent(br, false); ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one; ir.offsetMin = new Vector2(4f, 4f); ir.offsetMax = new Vector2(-4f, -4f);
                    var image = icon.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;

                    var mark = new GameObject("Mark", typeof(RectTransform), typeof(TextMeshProUGUI));
                    mark.layer = row.gameObject.layer;
                    var mr = (RectTransform)mark.transform;
                    mr.SetParent(br, false); mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one; mr.offsetMin = mr.offsetMax = Vector2.zero;
                    var text = mark.GetComponent<TextMeshProUGUI>();
                    if (font != null) text.font = font;
                    text.fontSize = 20f; text.alignment = TextAlignmentOptions.Center; text.text = "?"; text.raycastTarget = false;
                    icons[m] = image; marks[m] = text;
                }
                var so = new SerializedObject(ui);
                var pi = so.FindProperty("milestoneIcons"); pi.arraySize = Markers;
                var pm = so.FindProperty("milestoneMarks"); pm.arraySize = Markers;
                for (int m = 0; m < Markers; m++) { pi.GetArrayElementAtIndex(m).objectReferenceValue = icons[m]; pm.GetArrayElementAtIndex(m).objectReferenceValue = marks[m]; }
                so.ApplyModifiedProperties();
                rows++;
            }
        }
        Attributes(panel, root, W);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        return $"character panel laid out: 600 x 930, {rows} skill rows with {Markers} milestone markers each (save the scene)";
    }

    // The attributes header and two rows of two cells: icon, name, score and a small + button (made once, placed every run).
    static void Attributes(EquipmentPanelUI panel, RectTransform root, float width)
    {
        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
        var skillsTitle = root.Find("Skills title")?.GetComponent<TMP_Text>();
        var header = root.Find("Attributes title")?.GetComponent<TMP_Text>();
        if (header == null && skillsTitle != null)
        {
            header = Object.Instantiate(skillsTitle, root);
            header.name = "Attributes title";
            Undo.RegisterCreatedObjectUndo(header.gameObject, "Attributes title");
        }
        if (header != null) { header.text = "ATTRIBUTES"; header.alignment = TextAlignmentOptions.Left; Top(root, "Attributes title", -774f, width, 30f, 21f); }

        // Its own container: "Attributes" is the old hidden text of six class attributes (EquipmentPanelUI.attributes).
        var area = root.Find("Attribute cells") as RectTransform;
        if (area == null)
        {
            var go = new GameObject("Attribute cells", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Attributes");
            go.layer = root.gameObject.layer;
            area = (RectTransform)go.transform;
            area.SetParent(root, false);
        }
        // Cells an earlier run of this put inside that old text move over.
        var old = root.Find("Attributes");
        if (old != null) foreach (var stray in old.GetComponentsInChildren<AttributeCellUI>(true)) stray.transform.SetParent(area, false);
        const float CellH = 38f;
        area.anchorMin = area.anchorMax = new Vector2(.5f, 1f);
        area.anchoredPosition = new Vector2(0f, -792f - CellH);
        area.sizeDelta = new Vector2(width, CellH * 2f);

        var order = new[] { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };
        var cells = new AttributeCellUI[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            string name = order[i].ToString();
            var cell = area.Find(name) as RectTransform;
            if (cell == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(AttributeCellUI), typeof(StatTooltipUI));
                Undo.RegisterCreatedObjectUndo(go, "Attribute cell");
                go.layer = root.gameObject.layer;
                cell = (RectTransform)go.transform;
                cell.SetParent(area, false);
                var hit = go.GetComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
                var ui = go.GetComponent<AttributeCellUI>();
                ui.attribute = order[i];
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                icon.layer = go.layer; icon.transform.SetParent(cell, false);
                ui.icon = icon.GetComponent<Image>(); ui.icon.preserveAspect = true; ui.icon.raycastTarget = false;
                ui.label = Label(cell, "Name", bold, TextAlignmentOptions.Left);
                ui.value = Label(cell, "Score", bold, TextAlignmentOptions.Right);
                var plus = new GameObject("Plus", typeof(RectTransform), typeof(Image), typeof(Button));
                plus.layer = go.layer; plus.transform.SetParent(cell, false);
                var frame = plus.GetComponent<Image>(); frame.sprite = slot; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 2f;
                var button = plus.GetComponent<Button>(); button.targetGraphic = frame;
                var colours = button.colors; colours.highlightedColor = new Color(1f, .85f, .5f); colours.pressedColor = new Color(.8f, .7f, .45f); button.colors = colours;
                var mark = Label((RectTransform)plus.transform, "Mark", bold, TextAlignmentOptions.Center);
                mark.text = "+"; mark.color = new Color(.95f, .78f, .36f); mark.fontSize = 24f;
                var mr = mark.rectTransform; mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one; mr.offsetMin = mr.offsetMax = Vector2.zero;
                ui.plus = button;
            }
            var cellUi = cell.GetComponent<AttributeCellUI>();
            // Two columns: Strength and Dexterity on the first row, Constitution and Intelligence on the second.
            float half = width * .5f;
            cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f); cell.pivot = new Vector2(0f, 1f);
            cell.anchoredPosition = new Vector2((i % 2) * half, -(i / 2) * CellH);
            cell.sizeDelta = new Vector2(half - 16f, CellH - 4f);
            Place((RectTransform)cellUi.icon.transform, 0f, 28f, 28f);
            Place(cellUi.label.rectTransform, 38f, 150f, CellH); cellUi.label.fontSize = 21f;
            Place(cellUi.value.rectTransform, 160f, 50f, CellH); cellUi.value.fontSize = 22f;
            Place((RectTransform)cellUi.plus.transform, 218f, 28f, 28f);
            var tip = cell.GetComponent<StatTooltipUI>();
            tip.panel = panel;
            tip.kind = order[i] switch { MetaAttribute.Strength => StatTooltipUI.Kind.Strength, MetaAttribute.Dexterity => StatTooltipUI.Kind.Dexterity, MetaAttribute.Constitution => StatTooltipUI.Kind.Constitution, _ => StatTooltipUI.Kind.Intelligence };
            cells[i] = cellUi;
        }
        var so = new SerializedObject(panel);
        var list = so.FindProperty("attributeCells"); list.arraySize = cells.Length;
        for (int i = 0; i < cells.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        so.FindProperty("attributesTitle").objectReferenceValue = header;
        so.ApplyModifiedProperties();
    }

    static TMP_Text Label(RectTransform parent, string name, TMP_FontAsset font, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.alignment = align; t.raycastTarget = false; t.color = new Color(.9f, .88f, .84f);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // A child of a cell, left-anchored at x, vertically centred.
    static void Place(RectTransform r, float x, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, .5f); r.pivot = new Vector2(0f, .5f);
        r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(w, h);
    }

    // A faint line across the panel between groups ("Divider n", made once).
    static void Divider(RectTransform root, int n, float y, float width)
    {
        var line = root.Find($"Divider {n}") as RectTransform;
        if (line == null)
        {
            var go = new GameObject($"Divider {n}", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Divider");
            go.layer = root.gameObject.layer;
            line = (RectTransform)go.transform;
            line.SetParent(root, false);
        }
        line.anchorMin = line.anchorMax = new Vector2(.5f, 1f);
        line.anchoredPosition = new Vector2(0f, y);
        line.sizeDelta = new Vector2(width, 2f);
        var image = line.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, .07f); image.raycastTarget = false;
    }

    // A child anchored to the panel's top centre at this height, with a font size (0 keeps it).
    static void Top(RectTransform root, string name, float y, float width, float height, float fontSize)
    {
        var child = root.Find(name) as RectTransform;
        if (child == null) return;
        child.anchorMin = child.anchorMax = new Vector2(.5f, 1f);
        child.anchoredPosition = new Vector2(0f, y);
        child.sizeDelta = new Vector2(width, height);
        if (fontSize > 0 && child.GetComponent<TMP_Text>() is TMP_Text t) t.fontSize = fontSize;
    }
}
