using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// An ambush room (DungeonRoomFeatures.ambushes): it looks empty. Once the player is well inside, iron gates slam down
// across every way out and the room's enemies come in waves, each out of a puff of dust at the far side. When the last
// one falls the gates lift and a chest is left where the fight was. Set up by the generator (DungeonGenerator.Events).
public class DungeonAmbush : MonoBehaviour
{
    [HideInInspector] public DungeonGenerator dungeon;
    [HideInInspector] public int room = -1;
    [HideInInspector] public List<Transform> gates = new();
    [HideInInspector] public List<Vector3> spawnPoints = new();
    [HideInInspector] public List<GameObject> waveOne = new(), waveTwo = new();
    [HideInInspector] public Vector3 chestAt;
    [HideInInspector] public Quaternion chestFacing = Quaternion.identity;
    [HideInInspector] public int chestSeed;
    [HideInInspector] public List<Vector3> doorways = new();
    [HideInInspector] public GameObject spawnPuff;
    [HideInInspector] public AudioClip slamAudio, liftAudio, spawnAudio;

    [Tooltip("Metres the player must be from every way in before it springs.")]
    public float triggerDepth = 2.6f;
    public float gateTravel = 3.1f;

    enum Phase { Waiting, Fighting, Done }
    Phase phase;
    public bool Sprung => phase != Phase.Waiting;
    public bool Cleared => phase == Phase.Done;
    readonly List<Character> alive = new();

    void Start()
    {
        foreach (var g in gates) if (g != null) Lower(g, true);
    }

    void Update()
    {
        if (phase != Phase.Waiting || dungeon == null || dungeon.Layout == null) return;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null || !player.isActiveAndEnabled || !player.IsAlive) return;
        var p = player.transform.position;
        if (dungeon.Layout.RoomAt(dungeon.CellOf(p)) != room) return;
        foreach (var d in doorways) { var flat = d - p; flat.y = 0f; if (flat.sqrMagnitude < triggerDepth * triggerDepth) return; }
        phase = Phase.Fighting;
        StartCoroutine(Fight(player));
    }

    void Play(AudioClip clip, Vector3 at, float volume = .9f) { if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(clip, at, volume, .04f); }

    void Lower(Transform gate, bool instant)
    {
        var bars = gate.GetChild(0);
        var p = bars.localPosition; p.y = -gateTravel;
        if (instant) bars.localPosition = p;
        foreach (var c in gate.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var o in gate.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>()) o.enabled = false;
        if (!instant) StartCoroutine(Slide(bars, p, .9f));
    }

    void Raise(Transform gate)
    {
        var bars = gate.GetChild(0);
        foreach (var c in gate.GetComponentsInChildren<Collider>()) c.enabled = true;
        foreach (var o in gate.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>()) o.enabled = true;
        StartCoroutine(Slide(bars, new Vector3(bars.localPosition.x, 0f, bars.localPosition.z), .18f));
    }

    static IEnumerator Slide(Transform t, Vector3 to, float seconds)
    {
        var from = t.localPosition;
        for (float s = 0f; s < seconds; s += Time.deltaTime) { t.localPosition = Vector3.Lerp(from, to, s / seconds); yield return null; }
        t.localPosition = to;
    }

    IEnumerator Fight(Player player)
    {
        foreach (var g in gates) if (g != null) { Raise(g); Play(slamAudio, g.position); }
        MessageLog.Post("Iron gates slam down across the ways out.", MessageKind.Warning);
        yield return new WaitForSeconds(.7f);
        foreach (var wave in new[] { waveOne, waveTwo })
        {
            if (wave.Count == 0) continue;
            yield return Spawn(wave, player);
            while (true)
            {
                alive.RemoveAll(c => c == null || !c.IsAlive);
                if (alive.Count == 0) break;
                if (!player.IsAlive) yield break;
                yield return new WaitForSeconds(.25f);
            }
            yield return new WaitForSeconds(1f);
        }
        phase = Phase.Done;
        foreach (var g in gates) if (g != null) { Lower(g, false); Play(liftAudio, g.position, .7f); }
        dungeon.SpawnRewardChest(chestAt, chestFacing, chestSeed);
        MessageLog.Post("The gates grind up. Something was left behind.", MessageKind.Good);
    }

    // Each enemy of the wave at a spawn point on the far side of the room from the player, a moment apart.
    IEnumerator Spawn(List<GameObject> wave, Player player)
    {
        var points = new List<Vector3>(spawnPoints);
        var from = player.transform.position;
        points.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
        int far = Mathf.Max(1, Mathf.Min(points.Count, Mathf.Max(wave.Count, (points.Count + 1) / 2)));
        for (int i = 0; i < wave.Count; i++)
        {
            var at = points.Count > 0 ? points[i % far] : transform.position;
            var look = from - at; look.y = 0f;
            if (spawnPuff != null) PoolManager.SpawnOrInstantiate(spawnPuff, at, Quaternion.identity, 3f);
            Play(spawnAudio, at, .7f);
            var enemy = dungeon.SpawnAmbusher(wave[i], at, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward));
            if (enemy != null)
            {
                alive.Add(enemy);
                var brain = enemy.GetComponent<EnemyBrain>();
                if (brain != null) StartCoroutine(AlertSoon(brain));
            }
            yield return new WaitForSeconds(.35f);
        }
    }

    static IEnumerator AlertSoon(EnemyBrain brain)
    {
        yield return null;
        if (brain != null) brain.Alert();
    }
}
