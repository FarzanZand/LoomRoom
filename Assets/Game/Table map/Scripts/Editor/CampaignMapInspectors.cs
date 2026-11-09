using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A button on the map and on each place: models dragged in from a pack become pixel copies at the map's density.
static class CampaignBakeButton
{
    public static void Draw()
    {
        GUILayout.Space(6);
        if (!GUILayout.Button(new GUIContent("Bake new props", "Makes pixel copies, at the map's texel size, of every model dragged in that doesn't have one yet."))) return;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null) Debug.Log("[Map] " + CampaignMapBuilder.PixelProps());
        else
        {
            Debug.Log("[Map] " + CampaignMapBuilder.PixelProps(stage.prefabContentsRoot));
            EditorSceneManager.MarkSceneDirty(stage.scene);
        }
        GUIUtility.ExitGUI();
    }
}

[CustomEditor(typeof(CampaignPoint)), CanEditMultipleObjects]
public class CampaignPointInspector : OdinEditor
{
    public override void OnInspectorGUI() { base.OnInspectorGUI(); CampaignBakeButton.Draw(); }
}

[CustomEditor(typeof(CampaignMap))]
public class CampaignMapInspector : OdinEditor
{
    public override void OnInspectorGUI() { base.OnInspectorGUI(); CampaignBakeButton.Draw(); }
}
