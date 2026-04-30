using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class SetupCanvasScaler
{
    [MenuItem("Tools/Setup Canvas Scaler (Scale With Screen Size)")]
    static void Run()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;

        foreach (var canvas in canvases)
        {
            if (canvas.renderMode == RenderMode.WorldSpace) continue;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();

            Undo.RecordObject(scaler, "Setup Canvas Scaler");
            scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1920, 1080);
            scaler.screenMatchMode      = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight   = 0.5f;

            EditorUtility.SetDirty(scaler);
            count++;
        }

        Debug.Log($"[SetupCanvasScaler] Configured {count} canvas(es).");
    }
}
