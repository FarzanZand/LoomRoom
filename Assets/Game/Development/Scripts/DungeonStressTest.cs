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
        FreeTestFloors();
    }

    // Outside Play no OnDestroy runs, so the meshes a floor makes for itself (merged chunks, kit blocks, stair steps)
    // outlive the floor destroyed in a test scene: each built floor left about 900, and later builds got slower. Frees
    // them; call it every few floors in a long test and once at the end (EndTestScene does).
    public static void FreeTestFloors()
    {
        if (!Application.isPlaying) UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate();
    }
#endif

    // How far the stairs stand from the way in on every floor: walk and straight line in metres (2 m a cell), against the
    // furthest room on that floor. A boss stage's place is fixed, so its floors are listed apart.
    public static string Stairs(string levelPath, int seeds = 5, int floors = 12)
    {
#if UNITY_EDITOR
        var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (level == null) return "no level at " + levelPath;
        var report = new StringBuilder();
        var walks = new System.Collections.Generic.List<int>(); var lines = new System.Collections.Generic.List<int>();
        int built = 0, close = 0, short_ = 0, failed = 0;
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var testScene = BeginTestScene();
        try
        {
            for (int i = 0; i < seeds; i++)
                for (int floor = 1; floor <= floors; floor++)
                {
                    var go = new GameObject("Stress test floor");
                    go.transform.position = new Vector3(500, 0, 500);
                    var generator = go.AddComponent<DungeonGenerator>();
                    try { generator.Build(level, unchecked(1000 + i * 7919 + floor * 104729), floor); }
                    catch (System.Exception e) { report.AppendLine($"seed {i} floor {floor}: FAILED {e.Message}"); failed++; Object.DestroyImmediate(go); continue; }
                    var d = generator.Layout.ExitDistance();
                    bool stage = generator.GetComponentInChildren<DungeonStage>(true) != null;
                    report.AppendLine($"seed {i} floor {floor}{(stage ? " stage" : "")}: walk {d.walk * 2} m of {d.furthestWalk * 2}, straight {d.straight * 2} m of {d.furthestStraight * 2}, rooms {generator.Layout.PlacedRequests.Length}");
                    if (!stage) { walks.Add(d.walk * 2); lines.Add(d.straight * 2); built++; if (d.straight * 2 < d.furthestStraight) close++; if (d.walk * 2 < d.furthestWalk) short_++; }
                    Object.DestroyImmediate(go);
                    if ((i * floors + floor) % 10 == 0) FreeTestFloors();
                }
        }
        finally { EndTestScene(testScene, previous); }
        walks.Sort(); lines.Sort();
        string summary = built == 0 ? "no floors" : $"{level.name}, {failed} failed, {built} floors without a stage: walk least {walks[0]} m, median {walks[walks.Count / 2]} m; straight least {lines[0]} m, tenth {lines[walks.Count / 10]} m, median {lines[lines.Count / 2]} m; straight under half the widest on {close} floors, walk under half the furthest on {short_}";
        return summary + "\n" + report;
#else
        return "editor only";
#endif
    }

    public static string Run(string levelPath, int seeds = 20, int floors = 3)
    {
#if UNITY_EDITOR
        var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (level == null) return "no level at " + levelPath;
        var report = new StringBuilder();
        int fails = 0, built = 0, pockets = 0, pocketFails = 0, noExit = 0;
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
                        // Every floor has its way in and its stairs on (on a stage, hidden until the boss falls).
                        if (generator.GetComponentsInChildren<DungeonExit>(true).Length < 2) { noExit++; report.AppendLine($"seed {seed} floor {floor}: no stairs on"); }
                        pocketErrors.Clear();
                        pocketFails += PortalArch.PrebuildAll(generator, unchecked(seed + floor * 104729), pocketErrors);
                        foreach (var error in pocketErrors) report.AppendLine($"seed {seed} floor {floor} pocket: {error}");
                        // Pockets stand beside the floor, not under it.
                        foreach (var pocket in new System.Collections.Generic.List<DungeonGenerator>(PortalArch.PocketsOf(generator)))
                            if (pocket != null) { pockets++; Object.DestroyImmediate(pocket.gameObject); }
                    }
                    Object.DestroyImmediate(go);
                    if ((built + fails) % 10 == 0) FreeTestFloors();   // every tenth floor tried, failed or not
                }
            }
        }
        finally { EndTestScene(testScene, previous); }
        string summary = $"{level.name}: {fails} of {fails + built} floors failed, {noExit} without stairs, {pocketFails} of {pocketFails + pockets} portal pockets failed";
        System.IO.File.WriteAllText("Temp/stress.log", summary + "\n" + report);
        return summary;
#else
        return "editor only";
#endif
    }
}
