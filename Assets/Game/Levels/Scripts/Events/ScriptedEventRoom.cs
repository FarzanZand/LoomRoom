using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A hand-built room with an event of its own (the duel, the bell, the wager, the sleeping giant, the dice, and the older
// ones: the tavern, the merchant shop, the Skeleton Party, Not a witch), on its room template's root beside the event's
// script. It names the event for the dev console (event <name>) and Asset Studio
// (Play this event), marks where the player starts when a run opens in it (Entrance), and gives the event script what
// it needs from the floor: its room, the player, gates across its ways out, enemies out of a puff of dust and chests.
// Templates live in Levels/Shared/Rooms/Scripted. The newer events are dealt into the biomes' room pools like any other
// room, at most the floor's Max Event Rooms; the older rooms come by their own rules (Counts As Event off).
public class ScriptedEventRoom : MonoBehaviour
{
    [Tooltip("The event's name for the dev console (event <name>) and Asset Studio. One word, lower case.")]
    public string eventName;
    [TextArea, Tooltip("What happens, for Asset Studio and the console's list.")] public string summary;
    [Tooltip("Where the player starts when a run opens in this room (event <name>), facing along its blue arrow. Empty: the room's middle.")]
    public Transform entrance;
    [Tooltip("Counts toward the floor's Max Event Rooms (the duel, the bell...). Off for the rooms that come by their own rules: the tavern and the merchant shop (the floor's shop room), the Skeleton Party (behind the portal) and Not a witch (its own pool weight).")]
    public bool countsAsEvent = true;

    public DungeonGenerator Dungeon { get; private set; }
    public int Room { get; private set; } = -1;
    public bool Ready => Dungeon != null && Dungeon.Layout != null && Room >= 0;

    readonly List<Transform> gates = new();
    const float GateTravel = 3.1f;

    void Awake() => Find();

    // The floor and room it stands in (looked up again if the room was moved into place after Awake).
    public bool Find()
    {
        if (Ready && Dungeon.RoomOf(transform.position) == Room) return true;
        Dungeon = GetComponentInParent<DungeonGenerator>();
        Room = Dungeon != null && Dungeon.Layout != null ? Dungeon.RoomOf(transform.position) : -1;
        return Ready;
    }

    // ── The player ───────────────────────────────────────────────────

    public static Player TablePlayer
    {
        get
        {
            var p = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
            return p != null && p.isActiveAndEnabled ? p : null;
        }
    }

    public bool Inside(Vector3 world) => Ready && Dungeon.RoomOf(world) == Room;
    public bool PlayerInside { get { var p = TablePlayer; return p != null && p.IsAlive && Inside(p.transform.position); } }

    // Metres from the nearest way in (the player is "well inside" past a couple of metres).
    public float DepthOf(Vector3 world)
    {
        float best = float.MaxValue;
        foreach (var (edge, _) in Dungeon.WaysOut(Room)) { var d = edge - world; d.y = 0f; best = Mathf.Min(best, d.magnitude); }
        return best;
    }

    public static int Floor => TableManager.Loader != null ? Mathf.Max(1, TableManager.Loader.FloorNumber) : 1;
    public static bool RunGoing => !RunManager.HasInstance || RunManager.Instance.Running;

    // Lets an event's own enemy act again (its EventActor held it still until now).
    public static void Free(EnemyBrain brain)
    {
        if (brain == null) return;
        if (brain.TryGetComponent<EventActor>(out var actor)) actor.Release();
        brain.Entranced = false;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = false;
    }

    public static void Say(DungeonMaster.Line line) { if (line != null && !line.IsEmpty) DungeonMaster.Say(line); }

    public static void Play(AudioClip clip, Vector3 at, float volume = .9f) { if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(clip, at, volume, .04f); }

    // ── Gates ────────────────────────────────────────────────────────

    public bool GatesShut { get; private set; }

    // Iron gates (the ambush's) rise out of the floor across every way out of the room.
    public void ShutGates()
    {
        if (!Ready || GatesShut || Dungeon.AmbushGatePrefab == null) return;
        GatesShut = true;
        if (gates.Count == 0)
            foreach (var (edge, outward) in Dungeon.WaysOut(Room))
            {
                var gate = Instantiate(Dungeon.AmbushGatePrefab, edge - outward * .32f, Quaternion.LookRotation(outward), transform);
                gate.name = "Event gate";
                gates.Add(gate.transform);
                SetGate(gate.transform, false, true);
            }
        foreach (var g in gates) { SetGate(g, true, false); Play(Dungeon.GateSlamAudio, g.position); }
    }

    public void OpenGates()
    {
        if (!GatesShut) return;
        GatesShut = false;
        foreach (var g in gates) { SetGate(g, false, false); Play(Dungeon.GateLiftAudio, g.position, .7f); }
    }

    void SetGate(Transform gate, bool shut, bool instant)
    {
        var bars = gate.childCount > 0 ? gate.GetChild(0) : gate;
        foreach (var c in gate.GetComponentsInChildren<Collider>()) c.enabled = shut;
        foreach (var o in gate.GetComponentsInChildren<NavMeshObstacle>()) o.enabled = shut;
        var to = bars.localPosition; to.y = shut ? 0f : -GateTravel;
        if (instant) bars.localPosition = to; else StartCoroutine(Slide(bars, to, shut ? .18f : .9f));
    }

    static IEnumerator Slide(Transform t, Vector3 to, float seconds)
    {
        var from = t.localPosition;
        for (float s = 0f; s < seconds && t != null; s += Time.deltaTime) { t.localPosition = Vector3.Lerp(from, to, s / seconds); yield return null; }
        if (t != null) t.localPosition = to;
    }

    // ── Enemies ──────────────────────────────────────────────────────

    // Walkable floor in the room at least clearance metres from every way in, shuffled by rng.
    public List<Vector3> FreeFloor(System.Random rng, float clearance = 3f)
    {
        var points = new List<Vector3>();
        if (!Ready) return points;
        var openings = Dungeon.WaysOut(Room);
        var cells = new List<Vector2Int>(Dungeon.CellsOf(Room));
        for (int i = cells.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
        foreach (var c in cells)
        {
            var p = Dungeon.Cell(c);
            bool near = false;
            foreach (var (edge, _) in openings) { var d = edge - p; d.y = 0f; if (d.sqrMagnitude < clearance * clearance) { near = true; break; } }
            if (near || !NavMesh.SamplePosition(p, out var hit, .8f, NavMesh.AllAreas) || !Inside(hit.position)) continue;
            points.Add(hit.position);
        }
        return points;
    }

    // An enemy out of a puff of dust, facing the player and after them at once. prefab empty: one the room's pack would
    // have (the biome's mix for this floor), scaled for the floor like every other.
    public Character Summon(GameObject prefab, Vector3 at, System.Random rng, bool alert = true)
    {
        if (!Ready) return null;
        if (prefab == null) prefab = Dungeon.PickEnemy(Room, rng);
        if (prefab == null) return null;
        var player = TablePlayer;
        var look = player != null ? player.transform.position - at : transform.position - at; look.y = 0f;
        if (Dungeon.SpawnPuffPrefab != null) PoolManager.SpawnOrInstantiate(Dungeon.SpawnPuffPrefab, at, Quaternion.identity, 3f);
        Play(Dungeon.SpawnAudio, at, .7f);
        var enemy = Dungeon.SpawnAmbusher(prefab, at, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward));
        if (enemy != null && alert && enemy.TryGetComponent<EnemyBrain>(out var brain)) StartCoroutine(AlertSoon(brain));
        return enemy;
    }

    static IEnumerator AlertSoon(EnemyBrain brain)
    {
        yield return null;
        if (brain != null) brain.Alert();
    }

    // Every enemy standing in the room right now.
    public List<Character> EnemiesInside()
    {
        var list = new List<Character>();
        if (!Ready) return list;
        foreach (var brain in Dungeon.GetComponentsInChildren<EnemyBrain>())
            if (brain != null && brain.Character != null && brain.Character.IsAlive && Inside(brain.transform.position)) list.Add(brain.Character);
        return list;
    }

    // ── Rewards ──────────────────────────────────────────────────────

    // Gear rolled at a rarity (the floor's tiers), thrown out at a spot.
    public static ItemData Gear(ItemRarity rarity, System.Random rng, int maxTier = 2) => InventoryManager.GivenGear(rarity, maxTier, Floor, rng);

    // The meta progression's biggest spoils (none in the intro's practice run).
    public static ItemData BestSpoils()
    {
        if (!ProgressionManager.HasInstance || !ProgressionManager.MetaActive) return null;
        ItemData best = null; int worth = -1;
        foreach (var kind in ProgressionManager.Instance.spoils) if (kind != null && kind.item != null && kind.worth > worth) { worth = kind.worth; best = kind.item; }
        return best;
    }

    public void DropAt(ItemData item, Vector3 at, int count = 1) { if (item != null) DungeonPickup.Spawn(item, at, Dungeon != null ? Dungeon.transform : transform.parent, count); }

    public void GoldAt(int gold, Vector3 at, int piles = 3)
    {
        if (gold <= 0) return;
        DungeonLootDrop.Spill(new List<LootSource.Drop>(), gold, at, Dungeon != null ? Dungeon.transform : transform.parent);
    }

    void OnDrawGizmos()
    {
        if (entrance == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(entrance.position + Vector3.up * .9f, .35f);
        Gizmos.DrawLine(entrance.position + Vector3.up * .9f, entrance.position + Vector3.up * .9f + entrance.forward * 1.2f);
    }
}
