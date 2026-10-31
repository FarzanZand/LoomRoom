using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Synty's Sidekick tool keeps its character parts (about 470 MB) in its own Resources folder, and Unity puts every file in
// a Resources folder into every build. The game never loads them by name: the armour sets are baked from them in the
// editor, and the creatures made of Sidekick parts point at their meshes. So a build renames the folder for as long as it
// runs: what the game's scenes and prefabs point at still goes in through those references, the rest stays out. The tool
// only needs the name in the editor, and the folder gets it back when the build ends, or on the next editor tick after a
// build that stopped half way (a failed build never calls OnPostprocessBuild).
public class SidekickBuildExclusion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    const string Folder = "Assets/Synty/SidekickCharacters/Resources";
    const string AsideName = "Resources (left out of builds)";
    const string Aside = "Assets/Synty/SidekickCharacters/" + AsideName;

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return;
        var error = AssetDatabase.RenameAsset(Folder, AsideName);
        if (!string.IsNullOrEmpty(error)) { Debug.LogWarning($"[SidekickBuildExclusion] Could not set the Sidekick parts aside, so the build carries them all: {error}"); return; }
        EditorApplication.update -= RestoreWhenBuilt;
        EditorApplication.update += RestoreWhenBuilt;
    }

    public void OnPostprocessBuild(BuildReport report) => Restore();

    [InitializeOnLoadMethod]
    static void RestoreAfterStoppedBuild()
    {
        EditorApplication.update -= RestoreWhenBuilt;
        EditorApplication.update += RestoreWhenBuilt;
    }

    static void RestoreWhenBuilt()
    {
        if (BuildPipeline.isBuildingPlayer) return;
        EditorApplication.update -= RestoreWhenBuilt;
        Restore();
    }

    static void Restore()
    {
        if (!AssetDatabase.IsValidFolder(Aside) || AssetDatabase.IsValidFolder(Folder)) return;
        var error = AssetDatabase.RenameAsset(Aside, "Resources");
        if (!string.IsNullOrEmpty(error)) Debug.LogError($"[SidekickBuildExclusion] Could not give the Sidekick folder its Resources name back ({error}); rename \"{Aside}\" to Resources by hand.");
    }
}
