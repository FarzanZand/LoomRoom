using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The dungeon shadow distance LightingManager sets on the shared pipeline asset while a floor is played is never
// saved: an asset save gets the pipeline's own distance (the dungeon's returns right after), Play ending puts it back,
// and so does the editor coming up with one still held (a crash in Play).
public class ShadowDistanceGuard : AssetModificationProcessor
{
    static string[] OnWillSaveAssets(string[] paths)
    {
        float held = LightingManager.HeldPipelineShadowDistance;
        if (held < 0f || GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return paths;
        float dungeon = urp.shadowDistance;
        urp.shadowDistance = held;
        EditorApplication.delayCall += () => { if (urp != null && LightingManager.HeldPipelineShadowDistance >= 0f) urp.shadowDistance = dungeon; };
        return paths;
    }

    [InitializeOnLoadMethod]
    static void Watch()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) LightingManager.ReleaseShadowDistance();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) LightingManager.ReleaseShadowDistance();
        };
    }
}
