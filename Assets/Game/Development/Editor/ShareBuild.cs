using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Tools > LoomRoom > Build > Build To Share: a release build of the game (the build's scene list), zipped with a short
// how-to, ready to send to someone to play co-op with. Everyone who plays together needs this same build (a different
// build is refused when joining). Goes to "The Table Waits builds" next to the project folder.
public static class ShareBuild
{
    const string Product = "The Table Waits";

    [MenuItem("Tools/LoomRoom/Build/Build To Share")]
    public static void Build()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", Product + " builds"));
        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd HHmm");
        string folder = Path.Combine(root, $"{Product} {stamp}");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) { Debug.LogError("[Build] No scenes in the build settings."); return; }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(folder, Product + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded) { Debug.LogError($"[Build] Build failed: {report.summary.result}"); return; }

        // Burst's debug folder is for us, not for players.
        foreach (var dir in Directory.GetDirectories(folder, "*DoNotShip")) Directory.Delete(dir, true);
        File.WriteAllText(Path.Combine(folder, "How to play together.txt"), HowTo);

        string zip = folder + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        EditorUtility.DisplayProgressBar("Build To Share", "Zipping the build...", .9f);
        try { ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal, true); }
        finally { EditorUtility.ClearProgressBar(); }
        Debug.Log($"[Build] Ready to share: {zip} ({new FileInfo(zip).Length / (1024 * 1024)} MB)");
        EditorUtility.RevealInFinder(zip);
    }

    const string HowTo =
@"THE TABLE WAITS - playing together

Everyone needs this same build. Unzip it anywhere and start The Table Waits.exe.
If Windows says it protected your PC, click More info, then Run anyway.

Hosting:
1. Click Co-op on the main menu and pick a save to play with. An empty slot starts a new game:
   choose Play the intro or Skip intro (Skip starts on the first morning, in bed).
2. Click Host a game. A join code appears (it is also copied, so you can paste it to your friends).
3. Press Begin when everyone is in. With the intro, it plays for all of you.
   Otherwise, sit down at the table together and pick your figures.

Joining:
1. Click Co-op on the main menu and pick your own save (or an empty slot). Your spoils, character
   sheet and trophies are kept in that save.
2. Paste the host's code into the box and click Join (or press Enter). Wait for the host to begin.

You can also host or join later from the pause menu (Esc, then Co-op).
Up to 4 players. Playing over the internet needs no port forwarding.
";
}
