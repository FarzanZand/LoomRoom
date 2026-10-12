using System.Text;
using UnityEngine;

// Builds many floors of a level in a row (Play mode, from an eval) and reports every one that fails to generate:
// DungeonStressTest.Run("Assets/Game/Levels/Forest1/Forest1.asset", 20, 3) builds 20 seeds x floors 1-3. A floor the
// loader can't build after its retries shows "Could not load that level", so a level should pass with 0 failures.
// Writes Temp/stress.log (with each failure's exception and stack) and returns the summary.
public static class DungeonStressTest
{
    public static string Run(string levelPath, int seeds = 20, int floors = 3)
    {
#if UNITY_EDITOR
        var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (level == null) return "no level at " + levelPath;
        var report = new StringBuilder();
        int fails = 0, built = 0;
        for (int i = 0; i < seeds; i++)
        {
            int seed = 1000 + i * 7919;
            for (int floor = 1; floor <= floors; floor++)
            {
                var go = new GameObject("Stress test floor");
                go.transform.position = new Vector3(500, 0, 500);
                try { go.AddComponent<DungeonGenerator>().Build(level, unchecked(seed + floor * 104729), floor); built++; }
                catch (System.Exception e) { fails++; report.AppendLine($"seed {seed} floor {floor}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
                Object.DestroyImmediate(go);
            }
        }
        string summary = $"{level.name}: {fails} of {fails + built} floors failed";
        System.IO.File.WriteAllText("Temp/stress.log", summary + "\n" + report);
        return summary;
#else
        return "editor only";
#endif
    }
}
