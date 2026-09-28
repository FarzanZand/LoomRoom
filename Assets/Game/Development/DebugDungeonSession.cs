using System.Collections;
using System.Linq;
using UnityEngine;

// Editor test sessions use the normal dungeon and combat systems, with isolated saves.
public class DebugDungeonSession : MonoBehaviour
{
    public const string RequestKey = "LoomRoom.DebugDungeon";
    public const string SettingsPath = "Assets/Game/Development/Debug Dungeon/Debug Dungeon Settings.asset";
    public static bool Active { get; private set; }
    static DebugDungeonSettings settings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        Active = false;
        settings = null;
#if UNITY_EDITOR
        Active = UnityEditor.SessionState.GetBool(RequestKey, false);
        UnityEditor.SessionState.SetBool(RequestKey, false);
        if (Active) settings = UnityEditor.AssetDatabase.LoadAssetAtPath<DebugDungeonSettings>(SettingsPath);
        if (settings == null || settings.level == null) Active = false;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (Active) new GameObject("Debug dungeon session").AddComponent<DebugDungeonSession>();
    }

    IEnumerator Start()
    {
        yield return null; // Let scene managers initialize first.
        var loader = FindAnyObjectByType<TableLevelLoader>();
        if (loader == null) { Debug.LogError("Debug dungeon requires the Room scene."); yield break; }
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (settings.startingClass != null) player.GetComponent<AdventurerProgress>().selectedClass = settings.startingClass;
        loader.Load(settings.level);
    }

    public static void ConfigureEnemies(DungeonGenerator dungeon)
    {
        if (!Active || dungeon == null) return;
        // Keep those closest to the entrance so testing requires little walking.
        var enemies = dungeon.GetComponentsInChildren<EnemyBrain>()
            .OrderBy(e => (e.transform.position - dungeon.SpawnPoint).sqrMagnitude).ToArray();
        for (int i = 0; i < enemies.Length; i++)
        {
            var enemy = enemies[i];
            if (i >= settings.enemyCount) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); continue; }
            var profile = new EnemyBehaviourSettings();
            profile.CopyFrom(enemy.Profile);
            profile.aggressionMode = AggressionMode.AggressiveWhenHit;
            profile.defaultState = EnemyState.Idle;
            enemy.SetBehaviourOverride(profile);
        }
    }
}
