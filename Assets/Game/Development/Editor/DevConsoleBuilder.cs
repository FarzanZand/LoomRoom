using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds Development/Resources/Dev Console.prefab, the dev console's window (DevConsole): a dark band across the top
// of the screen with the log and a line to type in, in PixelOperator. Edit the prefab freely afterwards; running this
// again replaces it.
public static class DevConsoleBuilder
{
    const string Folder = "Assets/Game/Development/Resources";
    const string Path = Folder + "/Dev Console.prefab";
    const string Font = "Assets/Game/UI/Fonts/PixelOperator.asset";

    [MenuItem("Tools/LoomRoom/Dev/Build Dev Console")]
    public static string Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Development", "Resources");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Font);

        var root = new GameObject("Dev Console", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var pr = (RectTransform)panel.transform;
        pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(1, 1); pr.pivot = new Vector2(.5f, 1);
        pr.sizeDelta = new Vector2(0, 460); pr.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0, 0, 0, .82f);

        var logGo = new GameObject("Log", typeof(RectTransform));
        logGo.transform.SetParent(panel.transform, false);
        var lr = (RectTransform)logGo.transform;
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(24, 70); lr.offsetMax = new Vector2(-24, -14);
        var log = logGo.AddComponent<TextMeshProUGUI>();
        log.font = font; log.fontSize = 28; log.color = new Color(.85f, .9f, .85f); log.alignment = TextAlignmentOptions.BottomLeft;
        log.textWrappingMode = TextWrappingModes.Normal; log.overflowMode = TextOverflowModes.Truncate; log.raycastTarget = false;

        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "Input";
        inputGo.transform.SetParent(panel.transform, false);
        var ir = (RectTransform)inputGo.transform;
        ir.anchorMin = new Vector2(0, 0); ir.anchorMax = new Vector2(1, 0); ir.pivot = new Vector2(.5f, 0);
        ir.offsetMin = new Vector2(16, 12); ir.offsetMax = new Vector2(-16, 58);
        inputGo.GetComponent<Image>().color = new Color(.12f, .12f, .14f, .95f);
        var input = inputGo.GetComponent<TMP_InputField>();
        foreach (var t in inputGo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font = font; t.fontSize = 28;
            t.color = t.gameObject.name == "Placeholder" ? new Color(.5f, .5f, .5f) : Color.white;
            if (t.gameObject.name == "Placeholder") t.text = "Type a command (help)";
        }
        input.pointSize = 28;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.restoreOriginalTextOnEscape = false;

        var console = root.AddComponent<DevConsole>();
        var so = new SerializedObject(console);
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("input").objectReferenceValue = input;
        so.FindProperty("log").objectReferenceValue = log;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);
        return Path;
    }
}
