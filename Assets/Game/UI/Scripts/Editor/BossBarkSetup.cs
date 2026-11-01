using TMPro;
using UnityEditor;
using UnityEngine;

// Builds the boss's mid-fight lines (BossBarkUI) on the boss bar's canvas in the open scene: a line under the boss bar,
// one that floats over the boss's head and a subtitle in the lower middle, each switched on by UIManager > Boss Bark
// Style. Safe to run again: rebuilt in place. Run from an eval: BossBarkSetup.Build().
public static class BossBarkSetup
{
    static readonly Color Line = new(1f, .93f, .86f);

    public static string Build()
    {
        var bar = Object.FindAnyObjectByType<BossBarUI>(FindObjectsInactive.Include);
        if (bar == null) return "no BossBarUI in the open scene (open Room)";
        var canvas = (RectTransform)bar.transform.parent;
        var old = canvas.Find("Boss bark");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Bar Text.mat");

        var root = new GameObject("Boss bark", typeof(RectTransform), typeof(BossBarkUI));
        Undo.RegisterCreatedObjectUndo(root, "Build boss bark");
        root.layer = canvas.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(canvas, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;

        // Under the boss bar (900 x 96, 40 down from the top): its foot is 136 down.
        var (under, underText) = Part("Under boss bar", rect, font, material, new Vector2(.5f, 1f), new Vector2(0f, -158f), new Vector2(1100f, 44f), 30f);
        // Over the boss's head: placed every frame; its foot at the head.
        var (head, headText) = Part("Over head", rect, font, material, new Vector2(.5f, .5f), Vector2.zero, new Vector2(560f, 90f), 28f);
        ((RectTransform)head.transform).pivot = new Vector2(.5f, 0f);
        headText.alignment = TextAlignmentOptions.Bottom;
        headText.textWrappingMode = TextWrappingModes.Normal;
        // A subtitle over the Dungeon Master's feed (its foot 250 up).
        var (sub, subText) = Part("Subtitle", rect, font, material, new Vector2(.5f, 0f), new Vector2(0f, 340f), new Vector2(1200f, 44f), 30f);

        var so = new SerializedObject(root.GetComponent<BossBarkUI>());
        so.FindProperty("underBar").objectReferenceValue = under;
        so.FindProperty("overHead").objectReferenceValue = head;
        so.FindProperty("subtitle").objectReferenceValue = sub;
        so.FindProperty("underBarText").objectReferenceValue = underText;
        so.FindProperty("overHeadText").objectReferenceValue = headText;
        so.FindProperty("subtitleText").objectReferenceValue = subText;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return "built the boss bark on " + canvas.name;
    }

    static (CanvasGroup, TMP_Text) Part(string name, RectTransform parent, TMP_FontAsset font, Material material, Vector2 anchor, Vector2 at, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(.5f, anchor.y >= 1f ? 1f : anchor.y <= 0f ? 0f : .5f);
        r.anchoredPosition = at;
        r.sizeDelta = size;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        if (material != null) t.fontSharedMaterial = material;
        t.fontSize = fontSize;
        t.color = Line;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = "";
        return (group, t);
    }
}
