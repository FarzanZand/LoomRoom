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
    const string Frame = "Assets/Game/UI/Sprites/Diablo/Plain frame grey.png";
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    static readonly Color FrameTint = new(1f, .839f, .827f);     // as the inventory panels
    static readonly Color TitleGold = new(.859f, .698f, .38f);  // as the inventory titles
    static readonly Color OldTitleGrey = new(.639f, .639f, .639f);

    [MenuItem("Tools/LoomRoom/UI/Apply Plain Style")]
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
        // Titles in the old grey pixel font turn gold, as the inventory's.
        foreach (var text in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            if (text.font == null || !text.font.name.StartsWith("PIXEARG") || !Similar(text.color, OldTitleGrey)) continue;
            Undo.RecordObject(text, "Plain UI style");
            text.color = TitleGold; titles++;
            if (PrefabUtility.IsPartOfPrefabInstance(text)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        }
        EditorSceneManager.MarkSceneDirty(canvas.scene);
        Debug.Log($"[Plain UI] {panels} panels, {buttons} buttons and insets, {titles} titles restyled.");
    }

    static bool Similar(Color a, Color b) => Mathf.Abs(a.r - b.r) < .03f && Mathf.Abs(a.g - b.g) < .03f && Mathf.Abs(a.b - b.b) < .03f;
}
