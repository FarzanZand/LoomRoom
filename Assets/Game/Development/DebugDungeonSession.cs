using System.Collections;
using System.Linq;
using UnityEngine;

// Editor test sessions use the normal dungeon and combat systems, with isolated saves.
public class DebugDungeonSession : MonoBehaviour
{
    public enum Mode { Arena, Dungeon, Room }
    public const string RequestKey = "LoomRoom.DebugDungeon";
    public const string DungeonRequestKey = "LoomRoom.DebugDungeon.Generated";
    public const string RoomRequestKey = "LoomRoom.DebugDungeon.Room";
    public const string SettingsPath = "Assets/Game/Development/Debug Dungeon/Debug Dungeon Settings.asset";
    // Both dungeon modes skip the room (no intro, wake-up or reveal) and save to loomroom-debug.json.
    // Arena: the authored test arena with the settings' enemies. Dungeon: the level picked in the toolbar's dropdown (SelectedDungeon), generated as in a real run.
    public static bool Active { get; private set; }
    public static Mode CurrentMode { get; private set; }
    // The Room button: an ordinary morning in the room with the normal save, nothing played first (ProgressionManager applies it).
    public static bool RoomStart { get; private set; }
    static DebugDungeonSettings settings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        Active = false;
        RoomStart = false;
        CurrentMode = Mode.Arena;
        settings = null;
#if UNITY_EDITOR
        bool arena = UnityEditor.SessionState.GetBool(RequestKey, false);
        bool dungeon = UnityEditor.SessionState.GetBool(DungeonRequestKey, false);
        RoomStart = UnityEditor.SessionState.GetBool(RoomRequestKey, false);
        UnityEditor.SessionState.SetBool(RequestKey, false);
        UnityEditor.SessionState.SetBool(DungeonRequestKey, false);
        UnityEditor.SessionState.SetBool(RoomRequestKey, false);
        // Co-op tests (the Co-op button, Multiplayer Play Mode's virtual players) start in the room like Room does.
        if (CoopEditorTesting.Requested) RoomStart = true;
        if (RoomStart) { CurrentMode = Mode.Room; return; }
        CurrentMode = dungeon ? Mode.Dungeon : Mode.Arena;
        if (arena || dungeon) settings = UnityEditor.AssetDatabase.LoadAssetAtPath<DebugDungeonSettings>(SettingsPath);
        Active = dungeon || arena && settings != null && settings.level != null;
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
        var level = CurrentMode == Mode.Arena ? settings.level : SelectedDungeon;
        if (level == null) { Debug.LogError("Add the levels the Dungeon button should play to WorldManager's Debug Dungeons."); yield break; }
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (settings != null && settings.startingClass != null) player.GetComponent<AdventurerProgress>().selectedClass = settings.startingClass;
        loader.Load(level);
    }

    // The level the Dungeon button plays: the one picked in the toolbar's dropdown (remembered per user), else the first
    // of WorldManager's Debug Dungeons.
    public const string SelectedDungeonKey = "LoomRoom.DebugDungeon.Selected";
    public static TableLevelData[] DebugDungeons => WorldManager.HasInstance && WorldManager.Instance.debugDungeons != null ? WorldManager.Instance.debugDungeons : new TableLevelData[0];
    public static TableLevelData SelectedDungeon
    {
        get
        {
            TableLevelData first = null;
#if UNITY_EDITOR
            string picked = UnityEditor.EditorPrefs.GetString(SelectedDungeonKey, "");
#endif
            foreach (var level in DebugDungeons)
            {
                if (level == null) continue;
                if (first == null) first = level;
#if UNITY_EDITOR
                if (UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(level)) == picked) return level;
#endif
            }
            return first;
        }
    }

    // Chests to the player's right, 3.5 m out and 2 m apart, facing the player, with the arena biome's loot.
    static void PlaceChests(DungeonGenerator dungeon)
    {
        if (settings.chests == null) return;
        var right = dungeon.SpawnRotation * Vector3.right;
        var forward = dungeon.SpawnRotation * Vector3.forward;
        int i = 0;
        foreach (var prefab in settings.chests)
        {
            if (prefab == null) continue;
            var pos = dungeon.SpawnPoint + right * 3.5f + forward * (i++ * 2f - 1f);
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            var look = dungeon.SpawnPoint - pos; look.y = 0f;
            var go = Instantiate(prefab, pos, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity, dungeon.transform);
            if (go.TryGetComponent<DungeonContainer>(out var chest))
            {
                chest.loot = dungeon.Biome != null && dungeon.Biome.loot != null ? dungeon.Biome.loot : chest.loot;
                chest.seed = Random.Range(0, 99999);
                chest.floorNumber = 1;
            }
        }
    }

    // The arena brings no enemies of its own: the settings' list stands in an arc in front of the player,
    // each just outside its detection radius, and stays put until it notices them.
    public static void ConfigureEnemies(DungeonGenerator dungeon)
    {
        if (!Active || CurrentMode != Mode.Arena || dungeon == null) return;
        foreach (var generated in dungeon.GetComponentsInChildren<EnemyBrain>()) { generated.gameObject.SetActive(false); Destroy(generated.gameObject); }
        var level = settings.level;
        PlaceChests(dungeon);
        var spawned = settings.enemies.Where(p => p != null).ToArray();
        for (int i = 0; i < spawned.Length; i++)
        {
            var prefabBrain = spawned[i].GetComponent<EnemyBrain>();
            float radius = (prefabBrain != null ? prefabBrain.Profile.detectionRadius : 12f) + settings.margin;
            // Spread evenly across the view.
            float angle = spawned.Length == 1 ? 0f : Mathf.Lerp(-70f, 70f, (float)i / (spawned.Length - 1));
            var pos = dungeon.SpawnPoint + Quaternion.Euler(0f, angle, 0f) * (dungeon.SpawnRotation * Vector3.forward) * radius;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, radius, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            var look = dungeon.SpawnPoint - pos; look.y = 0f;
            var go = Instantiate(spawned[i], pos, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity, dungeon.transform);
            if (level.balance != null && go.TryGetComponent<Character>(out var character)) level.balance.Apply(character, 1);
            var enemy = go.GetComponent<EnemyBrain>();
            if (enemy == null) continue;
            var profile = new EnemyBehaviourSettings();
            profile.CopyFrom(enemy.Profile);
            profile.defaultState = EnemyState.Idle;
            if (settings.retaliateOnly) profile.aggressionMode = AggressionMode.AggressiveWhenHit;
            enemy.SetBehaviourOverride(profile);
        }
    }
}
