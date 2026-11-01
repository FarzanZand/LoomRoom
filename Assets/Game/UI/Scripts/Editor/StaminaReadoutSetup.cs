using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the stamina readout (StaminaReadoutUI) on the HUD canvas of the open scene: eight segments, a thin bracket under
// them with its ends turned up, the share left and STAMINA under it, in one pale green, centred about a fifth of the
// screen below the crosshair. Safe to run again: the readout is rebuilt in place. Run from an eval:
// StaminaReadoutSetup.Build().
public static class StaminaReadoutSetup
{
    public static readonly Color Fill = new(.85f, .9f, .83f), Line = new(.76f, .82f, .76f);
    const int Segments = 8;
    const float Width = 316f, SegmentHeight = 14f, Gap = 5f, Inset = 6f, Below = 204f;

    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Stamina readout");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var fontMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Bar Text.mat");

        var root = new GameObject("Stamina readout", typeof(RectTransform), typeof(CanvasGroup), typeof(StaminaReadoutUI));
        Undo.RegisterCreatedObjectUndo(root, "Build stamina readout");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(Width, 60f);
        rect.anchoredPosition = new Vector2(0f, -Below);
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // The segments: a dark cell each, its fill a pale bar anchored left that the script shrinks.
        float cellWidth = (Width - 2f * Inset - (Segments - 1) * Gap) / Segments;
        var fills = new RectTransform[Segments];
        var cells = new Image[Segments];
        for (int i = 0; i < Segments; i++)
        {
            var cell = Box($"Segment {i + 1}", rect, new Color(.04f, .06f, .07f, .5f));
            cell.rectTransform.sizeDelta = new Vector2(cellWidth, SegmentHeight);
            cell.rectTransform.anchoredPosition = new Vector2(-Width * .5f + Inset + cellWidth * .5f + i * (cellWidth + Gap), 0f);
            var fill = Box("Fill", cell.rectTransform, Fill);
            var f = fill.rectTransform;
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one; f.offsetMin = f.offsetMax = Vector2.zero;
            f.pivot = new Vector2(0f, .5f);
            Shade(fill.gameObject);
            fills[i] = f; cells[i] = cell;
        }

        // The bracket: a line under the segments with its two ends turned up.
        var line = Box("Bracket", rect, Line);
        line.rectTransform.sizeDelta = new Vector2(Width, 2f);
        line.rectTransform.anchoredPosition = new Vector2(0f, -13f);
        Shade(line.gameObject);
        for (int side = -1; side <= 1; side += 2)
        {
            var tick = Box(side < 0 ? "Bracket left" : "Bracket right", rect, Line);
            tick.rectTransform.sizeDelta = new Vector2(2f, 9f);
            tick.rectTransform.anchoredPosition = new Vector2(side * (Width * .5f - 1f), -8.5f);
            Shade(tick.gameObject);
        }

        var percent = Text("Percent", rect, font, fontMaterial, TextAlignmentOptions.Left, "46%");
        var label = Text("Label", rect, font, fontMaterial, TextAlignmentOptions.Right, "STAMINA");
        label.characterSpacing = 2f;

        var so = new SerializedObject(root.GetComponent<StaminaReadoutUI>());
        so.FindProperty("group").objectReferenceValue = group;
        var fp = so.FindProperty("fills"); fp.arraySize = Segments;
        var cp = so.FindProperty("cells"); cp.arraySize = Segments;
        for (int i = 0; i < Segments; i++)
        {
            fp.GetArrayElementAtIndex(i).objectReferenceValue = fills[i];
            cp.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        }
        so.FindProperty("percent").objectReferenceValue = percent;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return "built the stamina readout";
    }

    static Image Box(string name, RectTransform parent, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    // A faint dark drop under the pale parts, so they read over a bright wall.
    static void Shade(GameObject go)
    {
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .55f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }

    static TMP_Text Text(string name, RectTransform parent, TMP_FontAsset font, Material material, TextAlignmentOptions align, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        if (material != null) t.fontSharedMaterial = material;
        t.fontSize = 26f;
        t.color = Line;
        t.alignment = align;
        t.text = text;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var r = t.rectTransform;
        r.sizeDelta = new Vector2(Width, 30f);
        r.anchoredPosition = new Vector2(0f, -31f);
        return t;
    }
}
