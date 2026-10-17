using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lays out the character panel beside the inventory (EquipmentPanelUI, "Equipment and stats"): identity, vitals and combat
// numbers in groups split by faint dividers (each stat line explains itself on hover, StatTooltipUI), the gear bonuses on two
// lines, a skills header naming its columns (unlocks, rank), the skills with a marker per character sheet milestone
// (AdventureSkillRowUI.milestoneIcons: the unlock's icon or "?", grey until learned; hover for what it unlocks,
// MilestoneMarkerUI), the food line under the skills, and no controls line. Safe to run again: it places what exists and
// rebuilds the markers.
public static class CharacterPanelLayout
{
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    const int Markers = 3;

    public static string Layout()
    {
        var panel = Object.FindAnyObjectByType<EquipmentPanelUI>(FindObjectsInactive.Include);
        if (panel == null) return "no EquipmentPanelUI in the open scene (open Room)";
        var root = (RectTransform)panel.transform;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Character panel layout");
        root.sizeDelta = new Vector2(600f, 920f);
        root.anchoredPosition = new Vector2(root.anchoredPosition.x, 50f);

        const float W = 520f;
        Top(root, "Title", -48f, W, 40f, 28f);
        Top(root, "Identity", -124f, W, 64f, 24f);
        Divider(root, 1, -166f, W);
        Top(root, "Health", -196f, W, 34f, 25f);
        Top(root, "Stamina", -232f, W, 34f, 25f);
        Top(root, "Mana", -268f, W, 34f, 25f);
        Divider(root, 2, -298f, W);
        Top(root, "Damage", -326f, W, 34f, 25f);
        Top(root, "Armor", -362f, W, 34f, 25f);
        Top(root, "Gear bonuses", -408f, W, 50f, 20f);
        Divider(root, 3, -446f, W);
        Top(root, "Skills title", -470f, W, 30f, 21f);
        Top(root, "Food regeneration", -862f, W, 56f, 22f);
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

        const float RowH = 40f;
        var skills = root.Find("Skills") as RectTransform;
        int rows = 0;
        if (skills != null)
        {
            int count = skills.childCount;
            Top(root, "Skills", -490f - count * RowH * .5f, W, count * RowH, 0f);
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
                if (ui.icon != null) ((RectTransform)ui.icon.transform).sizeDelta = new Vector2(32f, 32f);
                if (ui.label != null) { var r = (RectTransform)ui.label.transform; r.anchoredPosition = new Vector2(44f, 0f); r.sizeDelta = new Vector2(250f, RowH); ui.label.fontSize = 24f; }
                if (ui.rank != null) { ((RectTransform)ui.rank.transform).sizeDelta = new Vector2(70f, RowH); ui.rank.fontSize = 24f; }

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
                    br.sizeDelta = new Vector2(30f, 30f);
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
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        return $"character panel laid out: 600 x 920, {rows} skill rows with {Markers} milestone markers each (save the scene)";
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
