using TMPro;
using UnityEditor;
using UnityEngine;

// Builds the unspent attribute points sign (UnspentPointsUI) on the HUD canvas of the open scene: a yellow "+" at the
// bottom left, just over the health bar. Safe to run again: it is rebuilt in place. Run from an eval: UnspentPointsSetup.Build().
public static class UnspentPointsSetup
{
    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Points to spend");
        int sibling = old != null ? old.GetSiblingIndex() : -1;
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        var root = new GameObject("Points to spend", typeof(RectTransform), typeof(CanvasGroup), typeof(UnspentPointsUI));
        Undo.RegisterCreatedObjectUndo(root, "Build points to spend");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        if (sibling >= 0) rect.SetSiblingIndex(sibling);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(50f, 112f);
        rect.sizeDelta = new Vector2(130f, 80f);
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        var text = new GameObject("Plus", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.layer = root.layer;
        var textRect = (RectTransform)text.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var label = text.GetComponent<TextMeshProUGUI>();
        label.font = font; if (material != null) label.fontSharedMaterial = material;
        label.fontSize = 80f; label.color = new Color(1f, .84f, .25f);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = "+";
        root.GetComponent<UnspentPointsUI>().label = label;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return $"Points to spend built under {hud.name}";
    }
}
