using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The Dungeon Master cheats (DungeonRoomFeatures.meddling; added to every floor by the generator). Two ways:
// - Too easy: the player clears a room of two or more enemies quickly and barely scratched. He says so, lifts the
//   floor's ceilings off like a lid, sets one more enemy down in front of them with his hand and puts the lid back. By
//   chance, at most once a floor.
// - Mine now: rarely, as the player walks up to a chest, he lifts the lid, takes the chest away and drops a single coin
//   for their trouble. At most once a run.
// Never in the intro's practice run, a boss fight, a portal's pocket or a hand-built event room, and only where his hand
// can reach from the table's edge. The dev console's meddle command makes him do either at once.
public class DungeonMasterMeddling : MonoBehaviour
{
    // The dev console: every floor gets him, and the next chance of each kind is taken for sure.
    public static bool Forced;
    const string StoleFlag = "run.meddle.stole";
    // A new run: he may steal again.
    public static void NewRun() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(StoleFlag, 0); }

    public static DungeonMaster.Line[] tooEasyLines =
    {
        new() { text = "Too easy. Let me fix that." },
        new() { text = "You're making this look simple." },
        new() { text = "That wasn't fair on them." },
    };
    public static DungeonMaster.Line[] afterAddLines =
    {
        new() { text = "Better." },
        new() { text = "Now it's a fight." },
    };
    public static DungeonMaster.Line stealLine = new() { text = "Actually, I'll take that." };
    public static DungeonMaster.Line coinLine = new() { text = "A coin for the walk." };

    DungeonGenerator dungeon;
    DungeonRoomFeatures features;
    bool added, busy;
    int fightRoom = -1;
    float fightStart, healthAtStart;
    readonly List<Character> fightEnemies = new();
    readonly HashSet<DungeonContainer> passed = new();
    float nextLook;

    void Start()
    {
        dungeon = GetComponent<DungeonGenerator>();
        features = dungeon != null ? dungeon.RoomFeatures : null;
    }

    static Player Player => ScriptedEventRoom.TablePlayer;
    static bool Allowed => ProgressionManager.HasInstance && ProgressionManager.MetaActive && BossStageEncounter.Current == null && ScriptedEventRoom.RunGoing;

    void Update()
    {
        if (dungeon == null || dungeon.Layout == null || busy || features == null) return;
        // Co-op: the host's Dungeon Master decides (its enemies are the real ones); the guests see him do it.
        if (Coop.IsGuest) return;
        var player = Player;
        if (player == null || !player.IsAlive || !Allowed) return;
        // Only the floor the player is on (a pocket beside it has its own).
        if (player.GetComponentInParent<DungeonGenerator>() == null && dungeon.RoomOf(player.transform.position) < 0) return;
        WatchFight(player);
        if (Time.time >= nextLook) { nextLook = Time.time + .3f; LookForChest(player); }
    }

    // ── Too easy ─────────────────────────────────────────────────────

    void WatchFight(Player player)
    {
        int room = dungeon.RoomOf(player.transform.position);
        if (fightRoom < 0)
        {
            if (added || room <= 0 || EventRoom(room)) return;
            // A fight starts when an enemy in the player's room is after them.
            var awake = new List<Character>();
            foreach (var brain in dungeon.GetComponentsInChildren<EnemyBrain>())
                if (brain != null && brain.Character != null && brain.Character.IsAlive && dungeon.RoomOf(brain.transform.position) == room) awake.Add(brain.Character);
            if (awake.Count < 2) return;
            bool started = false;
            foreach (var c in awake) if (c.TryGetComponent<EnemyBrain>(out var b) && b.IsAlerted) started = true;
            if (!started) return;
            fightRoom = room; fightStart = Time.time; healthAtStart = player.Stats.CurrentHealth;
            fightEnemies.Clear(); fightEnemies.AddRange(awake);
            return;
        }
        fightEnemies.RemoveAll(c => c == null || !c.IsAlive);
        if (fightEnemies.Count > 0) return;
        // The room is clear.
        float seconds = Time.time - fightStart, lost = (healthAtStart - player.Stats.CurrentHealth) / Mathf.Max(1f, player.Stats.MaxHealth);
        int at = fightRoom;
        fightRoom = -1;
        bool easy = seconds <= features.tooEasySeconds && lost <= features.tooEasyHealth;
        if (!Forced && (!easy || Random.value > features.meddleChance)) return;
        var spot = SpotBefore(player);
        if (!spot.HasValue || !DungeonMasterReach.CanReach(spot.Value) || DungeonMasterReach.Busy) return;
        added = true;
        StartCoroutine(AddOne(at, spot.Value));
    }

    // Floor a few metres in front of the player, in their room, clear of them: the floor they stand on, walkable straight
    // from them (never the top of a prop, where an archer he put down shot from out of reach).
    Vector3? SpotBefore(Player player)
    {
        var from = player.transform.position;
        var forward = player.transform.forward; forward.y = 0f; forward.Normalize();
        int room = dungeon.RoomOf(from);
        if (!NavMesh.SamplePosition(from, out var standing, 1.5f, NavMesh.AllAreas)) return null;
        // In front first; facing a wall, beside or behind them.
        foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f })
            foreach (float d in new[] { 4f, 3f, 5f, 2.5f, 2f })
            {
                var p = from + Quaternion.Euler(0f, turn, 0f) * forward * d;
                if (!NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas) || dungeon.RoomOf(hit.position) != room) continue;
                if (Mathf.Abs(hit.position.y - standing.position.y) > .3f || NavMesh.Raycast(standing.position, hit.position, out _, NavMesh.AllAreas)) continue;
                return hit.position;
            }
        return null;
    }

    // Co-op guests: what the host's Dungeon Master did, done here for the look. The enemy is the host's and comes from
    // there, carried in on its snapshots; the chest is this floor's copy of the host's.
    public void AddFromHost(int room, Vector3 spot) { if (!busy && !DungeonMasterReach.Busy) StartCoroutine(AddOne(room, spot)); }
    public void StealFromHost(DungeonContainer chest) { if (!busy && chest != null && !DungeonMasterReach.Busy) StartCoroutine(Steal(chest)); }

    IEnumerator AddOne(int room, Vector3 spot)
    {
        busy = true;
        CoopWorld.Meddled(false, room, spot);
        yield return new WaitForSeconds(.9f);
        ScriptedEventRoom.Say(tooEasyLines[Random.Range(0, tooEasyLines.Length)]);
        yield return new WaitForSeconds(.6f);
        yield return DungeonMasterReach.Lid(dungeon, true);
        if (Coop.IsGuest)
        {
            // He goes to it and sets down what the host's hand brings.
            yield return DungeonMasterReach.Approach(spot);
            yield return new WaitForSeconds(2f);
        }
        else
        {
            var rng = new System.Random(dungeon.EventSeed(room, 90));
            var prefab = dungeon.PickEnemy(room, rng);
            var enemy = prefab != null ? dungeon.SpawnAmbusher(prefab, spot, Quaternion.LookRotation(Flat(Player != null ? Player.transform.position - spot : Vector3.forward))) : null;
            if (enemy != null) yield return DungeonMasterReach.Put(enemy, spot);
        }
        yield return DungeonMasterReach.Lid(dungeon, false);
        yield return DungeonMasterReach.Leave();
        ScriptedEventRoom.Say(afterAddLines[Random.Range(0, afterAddLines.Length)]);
        if (Forced) Forced = false;
        busy = false;
    }

    // ── Mine now ─────────────────────────────────────────────────────

    void LookForChest(Player player)
    {
        if (!Forced && (features.stealChance <= 0f || ProgressionManager.Instance.HasFlag(StoleFlag))) return;
        var p = player.transform.position;
        foreach (var chest in dungeon.GetComponentsInChildren<DungeonContainer>())
        {
            if (chest == null || !chest.chest || chest.IsOpen || chest.startingKit || passed.Contains(chest)) continue;
            var d = chest.transform.position - p; d.y = 0f;
            if (d.magnitude > 3.2f) continue;
            passed.Add(chest);
            int room = dungeon.RoomOf(chest.transform.position);
            // Not the start, not an event's own chest, not one with a trap or a set prize in it.
            if (room <= 0 || EventRoom(room) || chest.onlyGuaranteed || chest.guaranteedItems.Count > 0 || chest.GetComponent<DungeonChestTrap>() != null) continue;
            if (!Forced && Random.value > features.stealChance) continue;
            if (!DungeonMasterReach.CanReach(chest.transform.position) || DungeonMasterReach.Busy) continue;
            ProgressionManager.Instance.SetFlag(StoleFlag, 1);
            StartCoroutine(Steal(chest));
            return;
        }
    }

    IEnumerator Steal(DungeonContainer chest)
    {
        busy = true;
        CoopWorld.Meddled(true, CoopWorld.ContainerId(chest), chest.transform.position);
        ScriptedEventRoom.Say(stealLine);
        // Nobody opens it while his hand is on the way.
        foreach (var c in chest.GetComponentsInChildren<InteractableTrigger>()) c.enabled = false;
        yield return DungeonMasterReach.Lid(dungeon, true, .9f);
        var at = chest.transform.position;
        if (chest != null && !chest.IsOpen) yield return DungeonMasterReach.Take(chest.transform);
        yield return DungeonMasterReach.Lid(dungeon, false);
        StartCoroutine(DungeonMasterReach.Leave());
        // A single coin, dropped where the chest stood.
        DungeonLootDrop.Spill(new List<LootSource.Drop>(), 1, at + Vector3.up * .5f, dungeon.transform);
        ScriptedEventRoom.Say(coinLine);
        if (Forced) Forced = false;
        busy = false;
    }

    // ── The dev console ──────────────────────────────────────────────

    // Right now: one more enemy in front of the player ("add") or the nearest chest taken ("steal"). Returns what happened.
    public string Now(string what)
    {
        if (dungeon == null) Start();
        var player = Player;
        if (player == null || dungeon == null) return "No floor or player.";
        if (busy || DungeonMasterReach.Busy) return "He's busy.";
        if (what == "steal")
        {
            DungeonContainer best = null; float bestD = float.MaxValue;
            foreach (var c in dungeon.GetComponentsInChildren<DungeonContainer>())
                if (c != null && c.chest && !c.IsOpen) { float d = Vector3.Distance(c.transform.position, player.transform.position); if (d < bestD) { bestD = d; best = c; } }
            if (best == null) return "No closed chest on this floor.";
            if (!DungeonMasterReach.CanReach(best.transform.position)) return "The nearest chest is out of his reach.";
            StartCoroutine(Steal(best));
            return "He takes the nearest chest.";
        }
        var spot = SpotBefore(player);
        if (!spot.HasValue) return "No floor in front of you.";
        if (!DungeonMasterReach.CanReach(spot.Value)) return "Out of his reach here (too far from the table's edge).";
        StartCoroutine(AddOne(Mathf.Max(0, dungeon.RoomOf(player.transform.position)), spot.Value));
        return "He adds an enemy in front of you.";
    }

    bool EventRoom(int room)
    {
        foreach (var e in dungeon.GetComponentsInChildren<ScriptedEventRoom>()) if (e.Find() && e.Room == room) return true;
        return false;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
