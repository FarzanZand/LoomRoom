using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the quest log (QuestLogUI) on the HUD canvas of the open scene: at the right edge under the minimap, a dark
// backing panel with a gold edge, a gold title over a gold line, and one row template (right-aligned, with an underlay
// so it reads over bright rooms). Safe to run again: the log is rebuilt in place. Run from an eval: QuestLogSetup.Build().
public static class QuestLogSetup
{
    static readonly Color Gold = new(.93f, .76f, .38f);

    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Quest log");
        int sibling = old != null ? old.GetSiblingIndex() : -1;
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var titleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain slot grey.png");

        var root = new GameObject("Quest log", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(QuestLogUI));
        Undo.RegisterCreatedObjectUndo(root, "Build quest log");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        if (sibling >= 0) rect.SetSiblingIndex(sibling);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -250f);
        rect.sizeDelta = new Vector2(560f, 120f);
        var panel = root.GetComponent<Image>();
        panel.sprite = panelSprite; panel.type = Image.Type.Sliced; panel.pixelsPerUnitMultiplier = 1f;
        panel.color = new Color(.05f, .05f, .07f, .82f); panel.raycastTarget = false;
        var layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 26, 14, 18);
        layout.spacing = 8f; layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        root.GetComponent<CanvasGroup>().alpha = 0f;
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;

        // A gold edge down the right side of the panel.
        var edge = new GameObject("Gold edge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        edge.layer = root.layer;
        var edgeRect = (RectTransform)edge.transform;
        edgeRect.SetParent(rect, false);
        edge.GetComponent<LayoutElement>().ignoreLayout = true;
        edgeRect.anchorMin = new Vector2(1f, 0f); edgeRect.anchorMax = new Vector2(1f, 1f); edgeRect.pivot = new Vector2(1f, .5f);
        edgeRect.anchoredPosition = new Vector2(-6f, 0f); edgeRect.sizeDelta = new Vector2(6f, -12f);
        var edgeImage = edge.GetComponent<Image>(); edgeImage.color = Gold; edgeImage.raycastTarget = false;

        var heading = Text("Title", rect, title, 34f, Gold, titleMaterial);
        heading.text = "QUEST";
        heading.characterSpacing = 6f;
        heading.alignment = TextAlignmentOptions.Right;
        heading.rectTransform.pivot = new Vector2(1f, .5f);

        var line = new GameObject("Gold line", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        line.layer = root.layer;
        line.transform.SetParent(rect, false);
        line.GetComponent<LayoutElement>().preferredHeight = 2f;
        var lineImage = line.GetComponent<Image>(); lineImage.color = new Color(Gold.r, Gold.g, Gold.b, .7f); lineImage.raycastTarget = false;

        var row = Text("Row template", rect, body, 30f, Color.white, bodyMaterial);
        row.text = "Hold your shield up against an arrow or a spell to send it back.";
        row.textWrappingMode = TextWrappingModes.Normal;
        row.alignment = TextAlignmentOptions.Right;
        row.rectTransform.pivot = new Vector2(1f, .5f);
        row.gameObject.SetActive(false);

        var so = new SerializedObject(root.GetComponent<QuestLogUI>());
        so.FindProperty("group").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("rowTemplate").objectReferenceValue = row;
        so.FindProperty("slide").objectReferenceValue = rect;
        so.FindProperty("pulse").objectReferenceValue = heading.rectTransform;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return $"quest log built under {hud.name} (save the scene)";
    }

    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, float size, Color colour, Material material)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size; t.color = colour; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
