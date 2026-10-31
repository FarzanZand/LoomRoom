using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Co-op: MultiplayerManager.enemyPrefabs, the enemies a host can make mid-floor and name to the guests, is kept to every
// enemy prefab under Characters/Enemies before each Play in the editor and each build, so a new enemy is never left out
// (one left out was made on the host only: guests never saw it).
[InitializeOnLoad]
public class CoopEnemyList : IPreprocessBuildWithReport
{
    const string ManagerPrefabPath = "Assets/Game/Multiplayer/Prefabs/Multiplayer Manager.prefab";

    static CoopEnemyList()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Refresh(); };
    }

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Refresh();

    public static GameObject[] Find() => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" })
        .Select(AssetDatabase.GUIDToAssetPath)
        .Where(p => !p.Contains("/_Archive/") && !p.Contains("/Base/"))
        .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
        .Where(p => p != null && p.GetComponent<EnemyBrain>() != null)
        .OrderBy(p => p.name).ToArray();

    public static void Refresh()
    {
        // A Multiplayer Play Mode player's editor shares the project and only reads it.
        if (Application.dataPath.Contains("/Library/VP/")) return;
        var manager = AssetDatabase.LoadAssetAtPath<MultiplayerManager>(ManagerPrefabPath);
        if (manager == null) return;
        var found = Find();
        if (manager.enemyPrefabs != null && manager.enemyPrefabs.SequenceEqual(found)) return;
        manager.enemyPrefabs = found;
        EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssetIfDirty(manager);
        Debug.Log($"[Coop] Enemy prefabs for co-op spawns brought up to date: {found.Length}.");
    }
}
