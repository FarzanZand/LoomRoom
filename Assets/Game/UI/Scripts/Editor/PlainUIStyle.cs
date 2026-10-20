using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Puts the HUD's menus in the inventory's look: the old kit's panels and card frames become the plain grey frame
// (tiled, as the inventory panels), buttons, insets and plaques the plain grey slot (buttons tint on hover instead of
// swapping sprites), and the grey pixel titles turn gold like the inventory's. Item slots, bars, pips and arrows stay.
// Safe to run again; it only touches UI under UI/Canvas in the open scene.
public static class PlainUIStyle
{
    const string Frame = "Assets/Game/UI/Sprites/Inventory/Plain frame grey.png";
    const string Slot = "Assets/Game/UI/Sprites/Inventory/Plain slot grey.png";
    static readonly Color FrameTint = new(1f, .839f, .827f);     // as the inventory panels
    static readonly Color TitleGold = new(.859f, .698f, .38f);  // as the inventory titles
    static readonly Color OldTitleGrey = new(.639f, .639f, .639f);

    public static void Apply()
    {
        var frame = AssetDatabase.LoadAssetAtPath<Sprite>(Frame);
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
        if (frame == null || slot == null) { Debug.LogError("[Plain UI] Plain frame or slot sprite missing."); return; }
        var canvas = GameObject.Find("UI/Canvas");
        if (canvas == null) { Debug.LogError("[Plain UI] Open the Room scene (UI/Canvas)."); return; }
        int panels = 0, buttons = 0, titles = 0;
        foreach (var image in canvas.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite == null || !AssetDatabase.GetAssetPath(image.sprite).Contains("/Sprites/Kit/")) continue;
            Undo.RecordObject(image, "Plain UI style");
            switch (image.sprite.name)
            {
                case "Panel":
                case "Card Frame":
                    image.sprite = frame; image.type = Image.Type.Tiled; image.pixelsPerUnitMultiplier = 1f;
                    image.color = FrameTint; image.material = null; panels++;
                    break;
                case "Button":
                case "Inset":
                case "Plaque":
                    image.sprite = slot; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f;
                    image.color = Color.white; image.material = null;
                    var button = image.GetComponent<Button>();
                    if (button != null)
                    {
                        Undo.RecordObject(button, "Plain UI style");
                        button.transition = Selectable.Transition.ColorTint;
                        button.spriteState = default;
                        var colours = button.colors;
                        colours.normalColor = new Color(.82f, .82f, .82f);
                        colours.highlightedColor = Color.white;
                        colours.selectedColor = new Color(.92f, .92f, .92f);
                        colours.pressedColor = new Color(.62f, .62f, .62f);
                        colours.disabledColor = new Color(.5f, .5f, .5f, .6f);
                        colours.colorMultiplier = 1f; colours.fadeDuration = .06f;
                        button.colors = colours;
                        button.targetGraphic = image;
                    }
                    buttons++;
                    break;
                default: continue;
            }
            if (PrefabUtility.IsPartOfPrefabInstance(image)) PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        }
        // Titles still in the old title grey (bold pixel font) turn gold, as the inventory's.
        foreach (var text in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            if (text.font == null || !text.font.name.StartsWith("PixelOperator-Bold") || !Similar(text.color, OldTitleGrey)) continue;
            Undo.RecordObject(text, "Plain UI style");
            text.color = TitleGold; titles++;
            if (PrefabUtility.IsPartOfPrefabInstance(text)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        }
        StyleContextMenu();
        EditorSceneManager.MarkSceneDirty(canvas.scene);
        Debug.Log($"[Plain UI] {panels} panels, {buttons} buttons and insets, {titles} titles restyled.");
    }

    const string LabelFont = "Assets/Game/UI/Fonts/PixelOperator-Bold.asset";
    static readonly Color LabelGold = new(.91f, .78f, .48f);   // as the pause menu's buttons

    // The right-click menu (ContextMenuUI): its panel the plain grey frame, its button template a plain grey slot that
    // tints on hover, with a gold PixelOperator-Bold label, as the pause menu's buttons. Safe to run again.
    // Apply runs it; on its own from an eval: PlainUIStyle.StyleContextMenu().
    public static string StyleContextMenu()
    {
        var menu = Object.FindAnyObjectByType<ContextMenuUI>(FindObjectsInactive.Include);
        if (menu == null) return "no ContextMenuUI in the open scene";
        var so = new SerializedObject(menu);
        var panel = so.FindProperty("panel").objectReferenceValue as RectTransform;
        var template = so.FindProperty("buttonTemplate").objectReferenceValue as Button;
        var frame = AssetDatabase.LoadAssetAtPath<Sprite>(Frame);
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(LabelFont);
        if (panel == null || template == null || frame == null || slot == null) return "context menu pieces or sprites missing";

        var panelImage = panel.GetComponent<Image>();
        Undo.RecordObject(panelImage, "Plain context menu");
        panelImage.sprite = frame; panelImage.type = Image.Type.Tiled; panelImage.pixelsPerUnitMultiplier = 1f;
        panelImage.color = FrameTint; panelImage.material = null;
        foreach (var outline in panel.GetComponents<Outline>()) { Undo.RecordObject(outline, "Plain context menu"); outline.enabled = false; }
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        if (layout != null) { Undo.RecordObject(layout, "Plain context menu"); layout.padding = new RectOffset(14, 14, 14, 14); layout.spacing = 4; }

        var image = template.GetComponent<Image>();
        Undo.RecordObject(image, "Plain context menu");
        image.sprite = slot; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f; image.color = Color.white; image.material = null;
        foreach (var outline in template.GetComponents<Outline>()) { Undo.RecordObject(outline, "Plain context menu"); outline.enabled = false; }
        Undo.RecordObject(template, "Plain context menu");
        template.transition = Selectable.Transition.ColorTint;
        template.spriteState = default;
        var colours = template.colors;
        colours.normalColor = new Color(.82f, .82f, .82f);
        colours.highlightedColor = Color.white;
        colours.selectedColor = new Color(.82f, .82f, .82f);   // a clicked entry must not stay lit on the next opening
        colours.pressedColor = new Color(.62f, .62f, .62f);
        colours.disabledColor = new Color(.5f, .5f, .5f, .6f);
        colours.colorMultiplier = 1f; colours.fadeDuration = .06f;
        template.colors = colours;
        template.targetGraphic = image;
        var element = template.GetComponent<LayoutElement>();
        if (element != null) { Undo.RecordObject(element, "Plain context menu"); element.preferredHeight = 46; }

        var label = template.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null)
        {
            Undo.RecordObject(label, "Plain context menu");
            Undo.RecordObject(label.rectTransform, "Plain context menu");
            if (font != null) { label.font = font; label.fontSharedMaterial = font.material; }
            label.fontSize = 22; label.color = LabelGold;
            label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            label.rectTransform.offsetMin = new Vector2(16, 2); label.rectTransform.offsetMax = new Vector2(-12, -2);
        }
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        return "context menu restyled";
    }

    static bool Similar(Color a, Color b) => Mathf.Abs(a.r - b.r) < .03f && Mathf.Abs(a.g - b.g) < .03f && Mathf.Abs(a.b - b.b) < .03f;
}
