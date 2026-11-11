using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the stamina readout (StaminaReadoutUI) on the HUD canvas of the open scene: a row of pale segments, one per 10
// stamina, centred about a fifth of the screen below the crosshair. Only one segment is authored (switched off, the
// template); the readout lays out as many as the player's stamina needs. Safe to run again: the readout is rebuilt in
// place. Run from an eval: StaminaReadoutSetup.Build().
public static class StaminaReadoutSetup
{
    public static readonly Color Fill = new(.85f, .9f, .83f);
    const float SegmentWidth = 34f, SegmentHeight = 14f, Below = 204f;

    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Stamina readout");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject("Stamina readout", typeof(RectTransform), typeof(CanvasGroup), typeof(StaminaReadoutUI));
        Undo.RegisterCreatedObjectUndo(root, "Build stamina readout");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(400f, SegmentHeight);
        rect.anchoredPosition = new Vector2(0f, -Below);
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // The template segment: a faint cell of the fill's colour, its fill a pale bar anchored left that the readout shrinks.
        var cell = Box("Segment", rect, new Color(Fill.r, Fill.g, Fill.b, .2f));
        cell.rectTransform.sizeDelta = new Vector2(SegmentWidth, SegmentHeight);
        var fill = Box("Fill", cell.rectTransform, Fill);
        var f = fill.rectTransform;
        f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one; f.offsetMin = f.offsetMax = Vector2.zero;
        f.pivot = new Vector2(0f, .5f);
        // A faint dark drop under the pale fill, so it reads over a bright wall.
        var shadow = fill.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .55f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        cell.gameObject.SetActive(false);

        var so = new SerializedObject(root.GetComponent<StaminaReadoutUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("template").objectReferenceValue = cell.rectTransform;
        so.ApplyModifiedPropertiesWithoutUndo();

        BuildRefusedText();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return "built the stamina readout";
    }

    // The readout's "Not enough stamina", just above its segments (StaminaReadoutUI.refusedText). Safe to run again: only
    // this label is made again, the rest of the readout is left as it is.
    public static string BuildRefusedText()
    {
        var readout = Object.FindAnyObjectByType<StaminaReadoutUI>(FindObjectsInactive.Include);
        if (readout == null) return "no stamina readout in the open scene (StaminaReadoutSetup.Build)";
        var old = readout.transform.Find("Refused text");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var go = new GameObject("Refused text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(go, "Build stamina refused text");
        go.layer = readout.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(readout.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(400f, 30f);
        rect.anchoredPosition = new Vector2(0f, SegmentHeight * .5f + 18f);
        var text = go.GetComponent<TMPro.TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        if (material != null) text.fontSharedMaterial = material;
        text.fontSize = 24f;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        text.color = new Color(.96f, .55f, .48f);
        text.raycastTarget = false;
        text.text = "Not enough stamina";
        text.alpha = 0f;
        var so = new SerializedObject(readout);
        so.FindProperty("refusedText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(readout.gameObject.scene);
        return "built the stamina readout's refused text";
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
}
