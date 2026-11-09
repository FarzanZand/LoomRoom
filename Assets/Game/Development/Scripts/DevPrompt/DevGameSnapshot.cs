#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

// The bounded game state a developer request carries (game-state.json): where the game is and what the players hold,
// taken the moment Shift+Space is pressed, before the composer opens. Only these fields, never the save file, settings,
// accounts or a scene dump; the whole thing stays under DevReportStore.MaxStateBytes. Seeds are written as decimal text.
public static class DevGameSnapshot
{
    [Serializable] public class Vec { public float x, y, z; public Vec(Vector3 v) { x = Round(v.x); y = Round(v.y); z = Round(v.z); } }
    [Serializable] public class Stack { public string item; public int count; public int slot; }
    [Serializable] public class Equipped { public string slot; public string item; }

    [Serializable]
    public class PlayerState
    {
        public string kind;
        public bool alive;
        public Vec position;
        public float yaw, pitch;
        public float health, maxHealth, stamina, maxStamina, mana, maxMana;
        public int gold;
        public string adventurerClass;
        public int characterLevel;
        public List<Equipped> equipped = new();
        public List<Stack> hotbar = new(), bag = new();
    }

    // What the crosshair is on: the object hit from the middle of the screen, its place in the scene, and (in the
    // Editor) the prefab or model it comes from with its permanent Asset Studio ID.
    [Serializable] public class LookingAt { public string name, scene_path, source_asset, source_id; public float distance; public Vec point; }

    [Serializable]
    public class State
    {
        public int schema_version = 1;
        public string captured_at;
        public string unity_version, build;
        public string scene;
        public string game_state;
        public string active_player;
        public int screen_width, screen_height;
        public float time_scale;
        public string level, level_kind, biome, room;
        public int floor;
        public string run_seed;
        public bool run_active;
        public List<string> open_quests = new();
        public LookingAt looking_at;
        public List<PlayerState> players = new();
    }

    public static State Capture()
    {
        var s = new State
        {
            captured_at = DateTime.UtcNow.ToString("o"),
            unity_version = Application.unityVersion,
            build = Application.isEditor ? "editor" : "development",
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            screen_width = Screen.width,
            screen_height = Screen.height,
            time_scale = Time.timeScale,
        };
        try
        {
            if (GameManager.HasInstance) s.game_state = GameManager.Instance.State.ToString();
            var players = PlayerManager.HasInstance ? PlayerManager.Instance : null;
            if (players != null)
            {
                s.active_player = players.ActiveKind.ToString();
                foreach (PlayerKind kind in Enum.GetValues(typeof(PlayerKind)))
                {
                    var p = players.GetPlayer(kind);
                    if (p != null) s.players.Add(Player(p));
                }
            }
            if (RunManager.HasInstance) s.run_active = RunManager.Instance.Running;
            var loader = TableManager.HasInstance ? TableManager.Loader : null;
            if (loader != null && loader.Current != null)
            {
                s.level = loader.Current.name;
                s.level_kind = loader.Current.kind.ToString();
                s.floor = loader.FloorNumber;
                s.run_seed = loader.RunSeed.ToString();
                var dungeon = loader.Dungeon;
                if (dungeon != null)
                {
                    if (dungeon.Biome != null) s.biome = dungeon.Biome.name;
                    var table = players != null ? players.GetPlayer(PlayerKind.Table) : null;
                    if (table != null) s.room = dungeon.DescribeRoomAt(table.transform.position);
                }
            }
            s.looking_at = Look();
            if (ProgressionManager.HasInstance)
            {
                var pm = ProgressionManager.Instance;
                foreach (var q in pm.quests) if (q != null && pm.QuestState(q.id) == 1) s.open_quests.Add(q.id);
            }
        }
        catch (Exception e) { s.game_state = (s.game_state ?? "") + " (snapshot incomplete: " + e.GetType().Name + ")"; }
        return s;
    }

    static LookingAt Look()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        var hits = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            // Not the players themselves (their own colliders sit at the camera).
            if (h.collider.GetComponentInParent<Player>() != null) continue;
            var t = h.collider.transform;
            var look = new LookingAt { name = t.name, distance = Round(h.distance), point = new Vec(h.point), scene_path = PathOf(t) };
#if UNITY_EDITOR
            var root = UnityEditor.PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            var source = root != null ? UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(root) : null;
            string asset = source != null ? UnityEditor.AssetDatabase.GetAssetPath(source) : "";
            if (string.IsNullOrEmpty(asset))
            {
                var mesh = h.collider.GetComponentInParent<MeshFilter>()?.sharedMesh;
                if (mesh != null) asset = UnityEditor.AssetDatabase.GetAssetPath(mesh);
            }
            if (!string.IsNullOrEmpty(asset)) { look.source_asset = asset; look.source_id = "ast_" + UnityEditor.AssetDatabase.AssetPathToGUID(asset); }
#endif
            return look;
        }
        return null;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 8; i++, t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    static PlayerState Player(Player p)
    {
        var ps = new PlayerState { kind = p.kind.ToString(), alive = p.IsAlive, position = new Vec(p.transform.position) };
        if (p.Look != null) { ps.yaw = Round(p.Look.YawTransform.eulerAngles.y); ps.pitch = Round(p.Look.Pitch); }
        if (p.Stats != null)
        {
            ps.health = Round(p.Stats.CurrentHealth); ps.maxHealth = Round(p.Stats.MaxHealth);
            ps.stamina = Round(p.Stats.CurrentStamina); ps.maxStamina = Round(p.Stats.MaxStamina);
            ps.mana = Round(p.Stats.CurrentMana); ps.maxMana = Round(p.Stats.MaxMana);
        }
        if (p.Wallet != null) ps.gold = p.Wallet.Gold;
        var progress = p.GetComponent<AdventurerProgress>();
        if (progress != null)
        {
            ps.adventurerClass = progress.selectedClass != null ? progress.selectedClass.name : null;
            ps.characterLevel = progress.Level;
        }
        if (p.Equipment != null)
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                var item = p.Equipment.Get(slot);
                if (item != null) ps.equipped.Add(new Equipped { slot = slot.ToString(), item = ItemName(item) });
            }
        Fill(ps.hotbar, p.Hotbar);
        Fill(ps.bag, p.Bag);
        return ps;
    }

    static void Fill(List<Stack> list, Inventory inv)
    {
        if (inv == null) return;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var item = inv.ItemAt(i);
            if (item != null) list.Add(new Stack { item = ItemName(item), count = inv.CountAt(i), slot = i });
        }
    }

    static string ItemName(ItemData item) => $"{item.DisplayName} [{item.saveId}]";
    static float Round(float v) => Mathf.Round(v * 100f) / 100f;

    // JSON, cut down to the size limit (inventories dropped first) if it ever grows past it.
    public static string ToJson(State s)
    {
        string json = JsonUtility.ToJson(s, true);
        if (System.Text.Encoding.UTF8.GetByteCount(json) <= DevReportStore.MaxStateBytes) return json;
        foreach (var p in s.players) { p.bag.Clear(); p.hotbar.Clear(); }
        json = JsonUtility.ToJson(s, true);
        if (System.Text.Encoding.UTF8.GetByteCount(json) <= DevReportStore.MaxStateBytes) return json;
        s.players.Clear(); s.open_quests.Clear();
        return JsonUtility.ToJson(s, true);
    }
}
#endif
