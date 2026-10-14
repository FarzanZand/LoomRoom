#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Play-mode check of the room events on the floor being played (Editor and development builds), started from an eval:
// RoomEventTest.Run(). It makes the table player very hard to kill, then, for whatever this floor has: springs an
// ambush and kills its waves (gates up, waves, gates down, the reward chest), wakes a sleeping pack (asleep at first,
// a step close by wakes it, the pack follows), opens a trapped chest beside the player (spikes hurt), bleeds on a
// shrine (the held weapon is honed), drops the player into a pit (hurt, back at the edge) and knocks an enemy over
// a pit's edge (it dies). Results go to Temp/RoomTest/report.txt and RoomEventTest.Report; frames to Temp/RoomTest.
public class RoomEventTest : MonoBehaviour
{
    static readonly StringBuilder log = new();
    public static string Report => log.ToString();
    public static bool Running { get; private set; }
    const string Folder = "Temp/RoomTest";

    // Statics outlive Play mode (domain reload is off): a test stopped with Play must not block the next one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Running = false;

    public static void Run()
    {
        if (Running) return;
        var go = new GameObject("Room event test");
        go.AddComponent<RoomEventTest>();
    }

    DungeonGenerator gen;
    Player player;

    void Line(string s) { log.AppendLine($"{Time.time:F1} {s}"); }

    void Start() => StartCoroutine(RunAll());

    IEnumerator RunAll()
    {
        Running = true; log.Clear();
        System.IO.Directory.CreateDirectory(Folder);
        // The floor being played: another generator can be in the scene without a built floor.
        foreach (var g in FindObjectsByType<DungeonGenerator>()) if (g.LevelData != null && g.isActiveAndEnabled) { gen = g; break; }
        player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (gen == null || player == null) { Line("no dungeon or no table player"); Finish(); yield break; }
        player.Stats.AddModifier(new StatModifier(StatType.MaxHealth, 5000, ModifierType.Flat, this));
        player.Stats.Heal(99999);
        Line($"{gen.LevelData.name} floor {gen.FloorNumber}, {gen.Layout.rooms.Count} rooms");
        yield return Trap();
        yield return Shrine();
        yield return Pits();
        yield return Sleep();
        yield return Ambush();
        Finish();
    }

    void Finish()
    {
        if (player != null) { player.Stats.RemoveAllFromSource(this); player.Stats.Heal(99999); }
        System.IO.File.WriteAllText($"{Folder}/report.txt", log.ToString());
        Running = false;
        Destroy(gameObject);
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot($"{Folder}/{name}.png");
    }

    void Stand(Vector3 at, Vector3 lookAt)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
        var look = lookAt - at; look.y = 0f;
        var rot = Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward);
        player.Warp(at + Vector3.up * .05f, rot);
        player.Look.YawTransform.rotation = rot;
        player.Look.SetPitch(10f);
    }

    int RoomOf(Vector3 p) => gen.Layout.RoomAt(gen.CellOf(p));

    void Kill(Character c) => c.Stats.TakeDamage(new DamageInfo { Amount = 99999, Source = player, HitPoint = c.transform.position + Vector3.up, Direction = c.transform.forward });

    // ── Trapped treasure ─────────────────────────────────────────────

    IEnumerator Trap()
    {
        var trap = FindAnyObjectByType<DungeonChestTrap>();
        if (trap == null) { Line("trap: none on this floor"); yield break; }
        var chest = trap.GetComponent<DungeonContainer>();
        Stand(chest.transform.position + chest.transform.forward * 1.3f, chest.transform.position);
        yield return new WaitForSeconds(.5f);
        float hp = player.Stats.CurrentHealth;
        chest.Interact(player);
        yield return new WaitForSeconds(.35f);
        yield return Shot("trap_warning");
        yield return new WaitForSeconds(.45f);
        yield return Shot("trap_spikes");
        float lost = hp - player.Stats.CurrentHealth;
        Line($"trap: {trap.traps.Count} plates, player stood on one and lost {lost:F0} health ({(lost > 0 ? "ok" : "NO DAMAGE")})");
        yield return new WaitForSeconds(1.5f);
    }

    // ── Shrine ───────────────────────────────────────────────────────

    IEnumerator Shrine()
    {
        var shrine = FindAnyObjectByType<DungeonShrine>();
        if (shrine == null) { Line("shrine: none on this floor"); yield break; }
        Stand(shrine.transform.position + shrine.transform.forward * 1.6f, shrine.transform.position);
        yield return new WaitForSeconds(.6f);
        var before = player.Equipment.Get(EquipmentSlot.RightHand);
        float hp = player.Stats.CurrentHealth;
        shrine.Interact(player);
        yield return new WaitForSeconds(.3f);
        var after = player.Equipment.Get(EquipmentSlot.RightHand);
        Line($"shrine: {(before != null ? before.itemName : "empty hand")} -> {(after != null ? after.itemName : "empty")}, cost {hp - player.Stats.CurrentHealth:F0} health, hotbar holds {player.Hotbar.IndexOf(after)}");
        yield return Shot("shrine");
    }

    // ── Pits ─────────────────────────────────────────────────────────

    IEnumerator Pits()
    {
        DungeonFloorCut pit = null;
        foreach (var c in FindObjectsByType<DungeonFloorCut>()) if (c.kind == DungeonFloorCut.Kind.Pit) { pit = c; break; }
        if (pit == null) { Line("pit: none on this floor"); yield break; }
        // Walk the player to the edge, then over it.
        Vector3 hole = Vector3.zero; foreach (var p in pit.CellCentres(gen.LevelData.cellSize)) { hole = p; break; }
        int room = RoomOf(pit.transform.position);
        Vector3 edge = hole;
        foreach (var d in DungeonLayout.Neighbours)
        {
            var n = gen.CellOf(hole) + d;
            var np = gen.Cell(n);
            if (gen.Layout.InRoom(room, n) && UnityEngine.AI.NavMesh.SamplePosition(np, out var h, .6f, UnityEngine.AI.NavMesh.AllAreas)) { edge = h.position; break; }
        }
        Stand(edge, hole);
        yield return new WaitForSeconds(.6f);
        yield return Shot("pit_edge");
        float hp = player.Stats.CurrentHealth;
        player.Warp(hole + Vector3.up * .3f);
        for (float t = 0; t < 2.5f && Mathf.Approximately(hp, player.Stats.CurrentHealth); t += Time.deltaTime) yield return null;
        yield return new WaitForSeconds(.3f);
        Line($"pit: fall cost {hp - player.Stats.CurrentHealth:F0} health, player now {(gen.CellOf(player.transform.position) == gen.CellOf(hole) ? "STILL IN THE PIT" : "back on the floor")} (y {player.transform.position.y - edge.y:F2} from the edge)");
        // An enemy at the edge, knocked over it.
        EnemyBrain victim = null;
        foreach (var b in EnemyBrain.Active) if (b.Character.IsAlive && !b.TryGetComponent<FlyingEnemy>(out _)) { victim = b; break; }
        if (victim == null) { Line("pit push: no enemy to push"); yield break; }
        victim.Motor.Agent.Warp(edge + (hole - edge).normalized * .45f);
        victim.transform.rotation = Quaternion.LookRotation(edge - hole);
        yield return null;
        var away = hole - edge; away.y = 0f;
        victim.Motor.ApplyKnockback(away, 4f);
        for (float t = 0; t < 1.2f && victim.Character.IsAlive; t += Time.deltaTime) yield return null;
        Line($"pit push: {victim.Character.DisplayName} {(victim.Character.IsAlive ? "STILL ALIVE" : "went over the edge")}");
    }

    // ── Sleeping pack ────────────────────────────────────────────────

    IEnumerator Sleep()
    {
        var sleepers = new List<EnemyBrain>();
        foreach (var b in EnemyBrain.Active) if (b.Asleep) sleepers.Add(b);
        if (sleepers.Count == 0) { Line("sleep: no sleeping enemies"); yield break; }
        int room = RoomOf(sleepers[0].transform.position);
        var pack = sleepers.FindAll(b => RoomOf(b.transform.position) == room);
        var first = pack[0];
        Stand(first.transform.position - first.transform.forward * 3.5f, first.transform.position);
        yield return new WaitForSeconds(1f);
        yield return Shot("sleep_pack");
        int still = pack.FindAll(b => b.Asleep).Count;
        // A walking step right by it.
        NoiseEvents.Report(first.transform.position + first.transform.forward * -1f, 3f, player);
        yield return new WaitForSeconds(2.5f);
        int awake = pack.FindAll(b => !b.Asleep && b.Character.IsAlive).Count;
        Line($"sleep: room {room}, {pack.Count} in the pack, {still} still asleep after standing 3.5 m off; after a step close by {awake} awake ({(awake == pack.Count ? "ok" : "SOME STILL ASLEEP")})");
        yield return Shot("sleep_woken");
    }

    // ── Ambush ───────────────────────────────────────────────────────

    IEnumerator Ambush()
    {
        var ambush = FindAnyObjectByType<DungeonAmbush>();
        if (ambush == null) { Line("ambush: none on this floor"); yield break; }
        var centre = gen.Cell(gen.Layout.RoomCenter(ambush.room));
        Stand(centre, centre + Vector3.forward);
        for (float t = 0; t < 3f && !ambush.Sprung; t += Time.deltaTime) yield return null;
        Line($"ambush: room {ambush.room}, {ambush.gates.Count} gates, sprung {ambush.Sprung}");
        yield return new WaitForSeconds(1.6f);
        yield return Shot("ambush_wave");
        int waves = 0, spawned = 0, unplaced = 0;
        for (float t = 0; t < 30f && !ambush.Cleared; t += .5f)
        {
            var inRoom = new List<EnemyBrain>();
            foreach (var b in EnemyBrain.Active) if (b.Character.IsAlive && RoomOf(b.transform.position) == ambush.room) inRoom.Add(b);
            if (inRoom.Count > 0)
            {
                waves++; spawned += inRoom.Count;
                foreach (var b in inRoom) if (b.Motor.Agent == null || !b.Motor.Agent.isOnNavMesh) unplaced++;
                yield return new WaitForSeconds(1.5f);
                foreach (var b in inRoom) if (b != null && b.Character.IsAlive) Kill(b.Character);
            }
            yield return new WaitForSeconds(.5f);
        }
        yield return new WaitForSeconds(1.5f);
        int chests = 0; foreach (var c in FindObjectsByType<DungeonContainer>()) if (c.chest && RoomOf(c.transform.position) == ambush.room) chests++;
        float gateY = ambush.gates.Count > 0 ? ambush.gates[0].GetChild(0).localPosition.y : 0f;
        Line($"ambush: cleared {ambush.Cleared}, {waves} kill passes, {spawned} enemies ({unplaced} off the NavMesh), gates now at {gateY:F1}, chests in room {chests}");
        yield return Shot("ambush_done");
    }
}
#endif
