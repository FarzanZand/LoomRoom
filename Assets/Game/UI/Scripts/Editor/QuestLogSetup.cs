using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the quest log (QuestLogUI) on the HUD canvas of the open scene: top left, a gold title and one row template.
// Safe to run again: the log is rebuilt in place.
public static class QuestLogSetup
{
    static readonly Color Gold = new(.93f, .76f, .38f);

    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Quest log");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");

        var root = new GameObject("Quest log", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.VerticalLayoutGroup), typeof(QuestLogUI));
        Undo.RegisterCreatedObjectUndo(root, "Build quest log");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(28f, -96f);
        rect.sizeDelta = new Vector2(520f, 200f);
        var layout = root.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 6f; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
        root.GetComponent<CanvasGroup>().alpha = 0f;
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;

        var heading = Text("Title", rect, title, 22f, Gold);
        heading.text = "QUEST";
        var row = Text("Row template", rect, body, 26f, Color.white);
        row.text = "Hold your shield up against an arrow or a spell to send it back.";
        row.textWrappingMode = TextWrappingModes.Normal;
        row.gameObject.SetActive(false);

        var so = new SerializedObject(root.GetComponent<QuestLogUI>());
        so.FindProperty("group").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("rowTemplate").objectReferenceValue = row;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return $"quest log built under {hud.name} (save the scene)";
    }

    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, float size, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size; t.color = colour; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
