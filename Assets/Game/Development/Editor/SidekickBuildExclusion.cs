using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Synty's Sidekick tool keeps its character parts (about 470 MB) in its own Resources folder, and Unity puts every file in
// a Resources folder into every build. The game never loads them by name: the armour sets are baked from them in the
// editor, and the creatures made of Sidekick parts point at their meshes. So a build renames the folder for as long as it
// runs: what the game's scenes and prefabs point at still goes in through those references, the rest stays out. The tool
// only needs the name in the editor, and the folder gets it back when the build ends, or on the next editor tick after a
// build that stopped half way (a failed build never calls OnPostprocessBuild). It runs after Synty's own build step
// (DatabaseBuildSync, order 0), which copies the tool's database into Resources/Database: set aside first, that copy
// made a new Resources folder beside the real one, which then couldn't take its name back. Should anything make one
// again while the real folder is aside, its files are merged into the real folder before the rename.
public class SidekickBuildExclusion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    const string Folder = "Assets/Synty/SidekickCharacters/Resources";
    const string AsideName = "Resources (left out of builds)";
    const string Aside = "Assets/Synty/SidekickCharacters/" + AsideName;

    public int callbackOrder => 1000;

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
        if (!AssetDatabase.IsValidFolder(Aside)) return;
        if (AssetDatabase.IsValidFolder(Folder) && !MergeStray()) return;
        var error = AssetDatabase.RenameAsset(Aside, "Resources");
        if (!string.IsNullOrEmpty(error)) Debug.LogError($"[SidekickBuildExclusion] Could not give the Sidekick folder its Resources name back ({error}); rename \"{Aside}\" to Resources by hand.");
    }

    // A Resources folder made while the real one was aside: what it holds was written later, so it wins. A file the real
    // folder has too takes the new contents and keeps the real file's .meta (its GUID, so references hold); anything the
    // real folder hasn't is moved in with its own .meta. The emptied folder is then removed. False (both left as they
    // are) when anything couldn't be moved.
    static bool MergeStray()
    {
        var done = new List<string>();
        var failed = new List<string>();
        MergeInto(Folder, Aside, done, failed);
        if (failed.Count > 0)
        {
            Debug.LogError($"[SidekickBuildExclusion] A new \"{Folder}\" was made while the real one was set aside, and merging it in failed:\n{string.Join("\n", failed)}\nDone before that:\n{string.Join("\n", done)}\nMerge it into \"{Aside}\" and rename that to Resources by hand.");
            return false;
        }
        if (!AssetDatabase.DeleteAsset(Folder))
        {
            Debug.LogError($"[SidekickBuildExclusion] Merged the new \"{Folder}\" into the real one but could not remove it; delete it and rename \"{Aside}\" to Resources by hand.");
            return false;
        }
        Debug.Log($"[SidekickBuildExclusion] A new \"{Folder}\" was made while the real one was set aside (Synty's database copy, or the tool); merged it into the real one and removed it:\n{string.Join("\n", done)}");
        return true;
    }

    static void MergeInto(string from, string to, List<string> done, List<string> failed)
    {
        foreach (var dir in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(dir), source = $"{from}/{name}", target = $"{to}/{name}";
            if (AssetDatabase.IsValidFolder(target)) { MergeInto(source, target, done, failed); continue; }
            var error = AssetDatabase.MoveAsset(source, target);
            if (string.IsNullOrEmpty(error)) done.Add($"moved {source} in"); else failed.Add($"{source}: {error}");
        }
        foreach (var file in Directory.GetFiles(from))
        {
            if (file.EndsWith(".meta")) continue;
            string name = Path.GetFileName(file), source = $"{from}/{name}", target = $"{to}/{name}";
            if (File.Exists(target))
            {
                try { File.Copy(source, target, true); AssetDatabase.ImportAsset(target); done.Add($"{target} takes the newer contents of {source}"); }
                catch (IOException e) { failed.Add($"{source}: {e.Message}"); }
                continue;
            }
            var error = AssetDatabase.MoveAsset(source, target);
            if (string.IsNullOrEmpty(error)) done.Add($"moved {source} in"); else failed.Add($"{source}: {error}");
        }
    }
}
