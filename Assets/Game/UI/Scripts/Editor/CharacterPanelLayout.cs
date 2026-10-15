using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lays out the character panel beside the inventory (EquipmentPanelUI, "Equipment and stats"): larger, the skills with a
// marker per character sheet milestone (AdventureSkillRowUI.milestoneIcons: the unlock's icon or "?", grey until learned),
// the food line under the skills, and no controls line. Safe to run again: it places what exists and rebuilds the markers.
public static class CharacterPanelLayout
{
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    const int Markers = 3;

    [MenuItem("Tools/LoomRoom/UI/Character Panel Layout")]
    public static void LayoutMenu() => Debug.Log("[CharacterPanelLayout] " + Layout());

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
        Top(root, "Health", -200f, W, 36f, 26f);
        Top(root, "Stamina", -242f, W, 36f, 26f);
        Top(root, "Mana", -284f, W, 36f, 26f);
        Top(root, "Damage", -344f, W, 36f, 26f);
        Top(root, "Armor", -386f, W, 36f, 26f);
        Top(root, "Gear bonuses", -424f, W, 28f, 20f);
        Top(root, "Skills title", -466f, W, 30f, 21f);
        Top(root, "Food regeneration", -862f, W, 56f, 22f);
        var controls = root.Find("Controls");
        if (controls != null) controls.gameObject.SetActive(false);

        const float RowH = 40f;
        var skills = root.Find("Skills") as RectTransform;
        int rows = 0;
        if (skills != null)
        {
            int count = skills.childCount;
            Top(root, "Skills", -488f - count * RowH * .5f, W, count * RowH, 0f);
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
                    frame.sprite = slot; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 2f; frame.raycastTarget = false;

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
