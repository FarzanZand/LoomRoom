using System.Text;
using UnityEngine;

// Builds many floors of a level in a row (Play mode, from an eval) and reports every one that fails to generate:
// DungeonStressTest.Run("Assets/Game/Levels/Dungeon1/Dungeon1.asset", 20, 3) builds 20 seeds x floors 1-3. A floor the
// loader can't build after its retries shows "Could not load that level", so a level should pass with 0 failures.
// Each floor's portal pockets are built too (PortalArch.PrebuildAll); a pocket that fails counts as a failure.
// Writes Temp/stress.log (with each failure's exception and stack) and returns the summary. Outside Play mode the floors
// are built in a temporary scene (BeginTestScene/EndTestScene, also for other test builds), so the open scene stays saved.
public static class DungeonStressTest
{
#if UNITY_EDITOR
    // An empty scene loaded beside the open one and made active, so test floors go there; it shares the open scene's
    // physics, so the generator's checks work as usual. EndTestScene closes it unsaved and gives the old scene back.
    public static UnityEngine.SceneManagement.Scene BeginTestScene()
    {
        if (Application.isPlaying) return default;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        return scene;
    }
    public static void EndTestScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.Scene previous)
    {
        if (!scene.IsValid()) return;
        if (previous.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
    }
#endif

    public static string Run(string levelPath, int seeds = 20, int floors = 3)
    {
#if UNITY_EDITOR
        var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (level == null) return "no level at " + levelPath;
        var report = new StringBuilder();
        int fails = 0, built = 0, pockets = 0, pocketFails = 0;
        var pocketErrors = new System.Collections.Generic.List<string>();
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var testScene = BeginTestScene();
        try
        {
            for (int i = 0; i < seeds; i++)
            {
                int seed = 1000 + i * 7919;
                for (int floor = 1; floor <= floors; floor++)
                {
                    var go = new GameObject("Stress test floor");
                    go.transform.position = new Vector3(500, 0, 500);
                    DungeonGenerator generator = null;
                    try { generator = go.AddComponent<DungeonGenerator>(); generator.Build(level, unchecked(seed + floor * 104729), floor); built++; }
                    catch (System.Exception e) { fails++; generator = null; report.AppendLine($"seed {seed} floor {floor}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
                    if (generator != null)
                    {
                        pocketErrors.Clear();
                        pocketFails += PortalArch.PrebuildAll(generator, unchecked(seed + floor * 104729), pocketErrors);
                        foreach (var error in pocketErrors) report.AppendLine($"seed {seed} floor {floor} pocket: {error}");
                        // Pockets stand beside the floor, not under it.
                        foreach (var pocket in new System.Collections.Generic.List<DungeonGenerator>(PortalArch.PocketsOf(generator)))
                            if (pocket != null) { pockets++; Object.DestroyImmediate(pocket.gameObject); }
                    }
                    Object.DestroyImmediate(go);
                }
            }
        }
        finally { EndTestScene(testScene, previous); }
        string summary = $"{level.name}: {fails} of {fails + built} floors failed, {pocketFails} of {pocketFails + pockets} portal pockets failed";
        System.IO.File.WriteAllText("Temp/stress.log", summary + "\n" + report);
        return summary;
#else
        return "editor only";
#endif
    }
}
