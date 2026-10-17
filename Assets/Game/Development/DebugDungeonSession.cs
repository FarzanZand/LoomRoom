using System.Collections;
using System.Linq;
using UnityEngine;

// Editor test sessions use the normal dungeon and combat systems, with isolated saves.
public class DebugDungeonSession : MonoBehaviour
{
    public enum Mode { Arena, Dungeon, Room, Boss }
    public const string RequestKey = "LoomRoom.DebugDungeon";
    public const string DungeonRequestKey = "LoomRoom.DebugDungeon.Generated";
    public const string RoomRequestKey = "LoomRoom.DebugDungeon.Room";
    public const string BossRequestKey = "LoomRoom.DebugDungeon.Boss";
    // The Boss button: a small level whose exit is the boss stage in front of the Dungeon Master. The player starts on
    // the stage; walking to its middle starts the fight (BossStageEncounter).
    public const string BossLevelPath = "Assets/Game/Development/Debug Dungeon/Boss Debug.asset";
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
        bool boss = UnityEditor.SessionState.GetBool(BossRequestKey, false);
        UnityEditor.SessionState.SetBool(BossRequestKey, false);
        RoomStart = UnityEditor.SessionState.GetBool(RoomRequestKey, false);
        UnityEditor.SessionState.SetBool(RequestKey, false);
        UnityEditor.SessionState.SetBool(DungeonRequestKey, false);
        UnityEditor.SessionState.SetBool(RoomRequestKey, false);
        // Co-op tests (the Co-op button, Multiplayer Play Mode's virtual players) start in the room like Room does.
        if (CoopEditorTesting.Requested && !CoopEditorTesting.NewGame) RoomStart = true;
        if (RoomStart) { CurrentMode = Mode.Room; return; }
        CurrentMode = boss ? Mode.Boss : dungeon ? Mode.Dungeon : Mode.Arena;
        if (arena || dungeon || boss) settings = UnityEditor.AssetDatabase.LoadAssetAtPath<DebugDungeonSettings>(SettingsPath);
        Active = dungeon || boss || arena && settings != null && settings.level != null;
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
        var level = CurrentMode == Mode.Arena ? settings.level : CurrentMode == Mode.Boss ? BossLevel : SelectedDungeon;
        if (level == null) { Debug.LogError("Add the levels the Dungeon button should play to WorldManager's Debug Dungeons."); yield break; }
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (settings != null && settings.startingClass != null) player.GetComponent<AdventurerProgress>().selectedClass = settings.startingClass;
        loader.Load(level);
        if (CurrentMode == Mode.Boss) yield return OntoStage(loader, player);
        if (CurrentMode == Mode.Arena) yield return GiveStartingItems(loader, player);
    }

    // Arena: once the floor is built (the run has handed out the class's kit), the settings' extra items go in the bag.
    IEnumerator GiveStartingItems(TableLevelLoader loader, Player player)
    {
        if (settings == null || settings.startingItems == null || settings.startingItems.Length == 0) yield break;
        for (float t = 0; t < 10f && (loader.Busy || loader.Dungeon == null); t += Time.deltaTime) yield return null;
        yield return null;
        foreach (var item in settings.startingItems)
            if (item != null && player.Bag != null && !player.Bag.TryAdd(item)) Debug.LogWarning($"Debug arena: no room in the bag for {item.itemName}.");
    }

    static TableLevelData BossLevel
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(BossLevelPath);
#else
            return null;
#endif
        }
    }

    // Boss: once the floor is built, the player stands on the stage, on the side away from the Dungeon Master, facing him.
    static IEnumerator OntoStage(TableLevelLoader loader, Player player)
    {
        for (float t = 0; t < 10f && (loader.Busy || loader.Dungeon == null); t += Time.deltaTime) yield return null;
        var dungeon = loader.Dungeon;
        if (dungeon == null || dungeon.Stage == null) { Debug.LogWarning("Boss debug: the floor has no boss stage."); yield break; }
        var centre = dungeon.StageCentre;
        var toDm = (TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position : centre + Vector3.right) - centre; toDm.y = 0;
        toDm = toDm.sqrMagnitude > .01f ? toDm.normalized : Vector3.right;
        var at = centre - toDm * dungeon.LevelData.cellSize * 2.5f;
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
        player.Warp(at, Quaternion.LookRotation(toDm));
        if (player.Look != null) { player.Look.SetYaw(Quaternion.LookRotation(toDm).eulerAngles.y); player.Look.SetPitch(8f); }
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

    // The settings' portal 5 m behind the player, facing them, leading to the settings' room (the Skeleton Party room).
    static void PlacePortal(DungeonGenerator dungeon)
    {
        if (settings.portal == null || settings.portalRoom == null) return;
        var back = dungeon.SpawnRotation * Vector3.back;
        var pos = dungeon.SpawnPoint + back * 5f;
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var go = Instantiate(settings.portal, pos, Quaternion.LookRotation(-back), dungeon.transform);
        // A kit arch outside a generated room: its painted model stands in for its blocks.
        foreach (var piece in go.GetComponentsInChildren<DungeonKitPiece>(true)) piece.Choose(true, 3f);
        foreach (var preview in go.GetComponentsInChildren<Transform>(true)) if (preview != null && preview.name == "Kit preview") Destroy(preview.gameObject);
        var portal = go.GetComponent<PortalArch>();
        portal.room = settings.portalRoom;
        portal.wayBack = settings.portalWayBack;
    }

    // The arena brings no enemies of its own: the settings' list stands in an arc in front of the player,
    // each just outside its detection radius, and stays put until it notices them.
    public static void ConfigureEnemies(DungeonGenerator dungeon)
    {
        // Only the arena itself: a floor loaded later in the session (the dev console's loadtemplate) keeps its own.
        if (!Active || CurrentMode != Mode.Arena || dungeon == null || dungeon.LevelData != settings.level) return;
        foreach (var generated in dungeon.GetComponentsInChildren<EnemyBrain>()) { generated.gameObject.SetActive(false); Destroy(generated.gameObject); }
        var level = settings.level;
        PlaceChests(dungeon);
        PlacePortal(dungeon);
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
        PlaceRow(dungeon, settings.fightBackRow, -1f, AggressionMode.AggressiveWhenHit, 0f);
        PlaceRow(dungeon, settings.dummyRow, 1f, AggressionMode.Passive, settings.dummyHealth);
    }

    // A row to one side of the player (side -1 left, 1 right), along their view, each facing them and idle.
    static void PlaceRow(DungeonGenerator dungeon, GameObject[] row, float side, AggressionMode aggression, float health)
    {
        if (row == null) return;
        var right = dungeon.SpawnRotation * Vector3.right;
        var forward = dungeon.SpawnRotation * Vector3.forward;
        var prefabs = row.Where(p => p != null).ToArray();
        for (int i = 0; i < prefabs.Length; i++)
        {
            var pos = dungeon.SpawnPoint + right * side * settings.rowDistance + forward * ((i - (prefabs.Length - 1) * .5f) * settings.rowSpacing + 2f);
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            var look = dungeon.SpawnPoint + forward * 2f - pos; look.y = 0f;
            var go = Instantiate(prefabs[i], pos, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity, dungeon.transform);
            if (settings.level.balance != null && go.TryGetComponent<Character>(out var balanced)) settings.level.balance.Apply(balanced, 1);
            var enemy = go.GetComponent<EnemyBrain>();
            if (enemy != null)
            {
                var profile = new EnemyBehaviourSettings();
                profile.CopyFrom(enemy.Profile);
                profile.defaultState = EnemyState.Idle;
                profile.aggressionMode = aggression;
                enemy.SetBehaviourOverride(profile);
            }
            // A test dummy: lots of health, topped up as it is hit hard, so it never dies by accident.
            if (health > 0f && go.TryGetComponent<Character>(out var dummy) && dummy.Stats != null)
            {
                // Percent bonuses (the level's balance) scale a flat amount too: measure by how much, then add the rest.
                float before = dummy.Stats.MaxHealth, flat = health - before;
                dummy.Stats.AddModifier(new StatModifier(StatType.MaxHealth, flat, ModifierType.Flat, "debug dummy"));
                float scale = (dummy.Stats.MaxHealth - before) / flat;
                if (scale > .01f && Mathf.Abs(dummy.Stats.MaxHealth - health) > .5f)
                    dummy.Stats.AddModifier(new StatModifier(StatType.MaxHealth, (health - dummy.Stats.MaxHealth) / scale, ModifierType.Flat, "debug dummy"));
                dummy.Stats.RestorePools(health, dummy.Stats.CurrentMana, dummy.Stats.CurrentStamina);
            }
        }
    }
}
