using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The dice room (on the Dice room template's root, with a ScriptedEventRoom): a room open to the sky. Walk in and the
// Dungeon Master leans over, brings his die up in his fingers and drops it through the opening; it tumbles across the
// floor and the face it shows decides the room:
//   1 an ambush (gates and two waves)   2 he sets a tough enemy down     3 nothing
//   4 a rest (health back to full)      5 he sets down an iron chest     6 he sets down a gold chest with gear rolled Unique
// The die is his: it stays where it lands. The number is picked from the floor's seed, then the die is made to land on it.
public class DiceEvent : MonoBehaviour
{
    [Tooltip("The Dungeon Master's die (a cube; its faces: +Y 1, +X 2, +Z 3, -Z 4, -X 5, -Y 6).")] public GameObject diePrefab;
    [Tooltip("Where it lands (under the opening).")] public Transform landing;
    [Min(.5f), Tooltip("Metres into the room (from the nearest way in) before he rolls.")] public float triggerDepth = 2.5f;
    [Min(.3f), Tooltip("Seconds his hand takes to bring the die up over the room.")] public float raiseSeconds = 1.6f;
    [Tooltip("A knock for each bounce.")] public AudioClip[] knocks = new AudioClip[0];
    [Tooltip("Rolling to a stop.")] public AudioClip roll;
    [Tooltip("The tough enemy a 2 brings (empty: the room's own pick).")] public GameObject toughEnemy;
    [Tooltip("The effect on the player when a 4 heals them.")] public GameObject healEffect;
    [Tooltip("Testing: the face it always lands on (0: the floor's seed picks).")] [Range(0, 6)] public int forceFace;
    [Range(1, 4)] public int maxTier = 2;
    [Header("Lines")]
    public DungeonMaster.Line rollLine = new() { text = "Let's see what the dice say." };
    public DungeonMaster.Line[] faceLines =
    {
        new() { text = "A one. Oh dear." },
        new() { text = "A two gets you a friend of mine." },
        new() { text = "Three does nothing at all." },
        new() { text = "Four puts you back on your feet." },
        new() { text = "Five, not bad." },
        new() { text = "Six! Lucky you." },
    };

    // Which local axis of the die faces up for each number (1..6).
    static readonly Vector3[] FaceAxes = { Vector3.up, Vector3.right, Vector3.forward, Vector3.back, Vector3.left, Vector3.down };

    public static int ForcedNext;   // the dev console: the next roll lands on this (0: none)

    ScriptedEventRoom room;
    bool rolled;
    Vector3? seenAt;
    public int Result { get; private set; }

    void Start() => room = GetComponent<ScriptedEventRoom>();

    void Update()
    {
        if (rolled || room == null || !room.Find()) return;
        var player = ScriptedEventRoom.TablePlayer;
        if (player == null || !player.IsAlive || !room.Inside(player.transform.position)) { seenAt = null; return; }
        // The first place the player is seen inside, then a metre's walk in from it and well inside.
        if (seenAt == null) seenAt = player.transform.position;
        var walked = player.transform.position - seenAt.Value; walked.y = 0f;
        if (walked.magnitude < 1f || room.DepthOf(player.transform.position) < triggerDepth) return;
        if (DungeonMasterReach.Busy) return;
        rolled = true;
        StartCoroutine(Roll(player));
    }

    IEnumerator Roll(Player player)
    {
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 60));
        Result = ForcedNext > 0 ? ForcedNext : forceFace > 0 ? forceFace : 1 + rng.Next(6);
        ForcedNext = 0;
        ScriptedEventRoom.Say(rollLine);
        var spot = landing != null ? landing.position : transform.position;
        if (diePrefab == null) { yield return Outcome(player, spot, rng); yield break; }
        var die = Instantiate(diePrefab, spot + Vector3.down * 50f, Random.rotation, room.Dungeon.transform);
        var colliders = die.GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = false;
        bool released = false;
        Coroutine tumble = null;
        // Over the room if he can reach it; else from as far as he can reach, he tosses it in. It falls (or flies) the
        // moment he lets go.
        bool reach = DungeonMasterReach.CanReach(spot);
        yield return DungeonMasterReach.DropOver(die.transform, spot, raiseSeconds, () =>
        {
            released = true;
            tumble = StartCoroutine(Tumble(die.transform, spot, Result, rng, Toss(die.transform.position, spot)));
        }, reach);
        StartCoroutine(DungeonMasterReach.Leave());
        if (!released) yield break;
        yield return tumble;
        foreach (var c in colliders) if (c != null) c.enabled = true;
        yield return new WaitForSeconds(.5f);
        ScriptedEventRoom.Say(faceLines[Result - 1]);
        yield return new WaitForSeconds(.8f);
        yield return Outcome(player, spot, rng);
    }

    // Falls from his fingers, bounces three times across the floor and comes to rest with face up, its half size above
    // the floor.
    // The velocity that carries the die from where he lets go to the spot in a high arc (straight down when he lets go
    // over it).
    const float Gravity = 30f;
    static Vector3 Toss(Vector3 from, Vector3 to)
    {
        var flat = to - from; flat.y = 0f;
        if (flat.magnitude < 1.5f) return Vector3.zero;
        float t = Mathf.Clamp(.35f + flat.magnitude * .04f, .9f, 2.6f);
        var v = flat / t;
        v.y = (to.y - from.y + .5f * Gravity * t * t) / t;
        return v;
    }

    IEnumerator Tumble(Transform die, Vector3 spot, int face, System.Random rng, Vector3 toss)
    {
        var bounds = new Bounds(die.position, Vector3.zero);
        foreach (var r in die.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        float half = Mathf.Max(.1f, die.lossyScale.y * .5f * MeshSize(die));
        float floorY = spot.y + half;
        var pos = die.position;
        var heading = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward;
        var velocity = toss.sqrMagnitude > .01f ? toss : heading * 1.6f;
        var spin = Random.onUnitSphere * 720f;
        float g = Gravity;
        int bounces = 0;
        var restRotation = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up) * Quaternion.FromToRotation(FaceAxes[face - 1], Vector3.up);
        // The flight down and the bounces.
        while (bounces < 4)
        {
            velocity.y -= g * Time.deltaTime;
            pos += velocity * Time.deltaTime;
            die.Rotate(spin * Time.deltaTime, Space.World);
            if (pos.y <= floorY && velocity.y < 0f)
            {
                pos.y = floorY;
                bounces++;
                float impact = -velocity.y;
                velocity.y = impact * (bounces == 1 ? .38f : .3f);
                // The first landing keeps little of a toss's sideways speed: it lands in the room, not past it.
                float keep = bounces == 1 && toss.sqrMagnitude > .01f ? .12f : .75f;
                velocity.x *= keep; velocity.z *= keep;
                if (bounces == 1 && toss.sqrMagnitude > .01f) velocity += heading * 1.2f;
                spin *= .55f;
                if (knocks.Length > 0) ScriptedEventRoom.Play(knocks[(bounces - 1) % knocks.Length], pos, Mathf.Clamp01(.3f + impact * .04f));
                if (bounces == 1) DungeonMasterReach.Shake(.25f);
                // Low enough: it rolls the rest of the way to its face.
                if (velocity.y < 1.2f) break;
            }
            die.position = pos;
            yield return null;
        }
        if (roll != null) ScriptedEventRoom.Play(roll, pos, .6f);
        // It rolls over onto its face, sliding to a stop.
        var from = die.rotation; var start = pos;
        var stop = pos + new Vector3(velocity.x, 0f, velocity.z) * .35f;
        for (float t = 0; t < .55f; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / .55f);
            die.SetPositionAndRotation(Vector3.Lerp(start, stop, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * half * .25f, Quaternion.Slerp(from, restRotation, u));
            yield return null;
        }
        die.SetPositionAndRotation(stop, restRotation);
        if (knocks.Length > 0) ScriptedEventRoom.Play(knocks[0], stop, .35f);
    }

    // The die's own size along one axis (it is a cube): its mesh's bounds.
    static float MeshSize(Transform die)
    {
        var mf = die.GetComponentInChildren<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.y * mf.transform.localScale.y : 1f;
    }

    IEnumerator Outcome(Player player, Vector3 spot, System.Random rng)
    {
        switch (Result)
        {
            case 1: yield return Ambush(player, rng); break;
            case 2:
            {
                var at = Beside(player, spot, 2.4f);
                var prefab = toughEnemy != null ? toughEnemy : room.Dungeon.PickEnemy(room.Room, rng);
                var enemy = prefab != null ? room.Dungeon.SpawnAmbusher(prefab, at, Quaternion.LookRotation(Flat(player.transform.position - at))) : null;
                if (enemy != null) { yield return DungeonMasterReach.Put(enemy, at); yield return DungeonMasterReach.Leave(); }
                break;
            }
            case 3: break;
            case 4:
                if (player != null && player.Stats != null) player.Stats.Heal(player.Stats.MaxHealth);
                if (healEffect != null && player != null) Destroy(Instantiate(healEffect, player.transform.position + player.transform.forward * .6f, Quaternion.identity), 4f);
                break;
            case 5:
            case 6:
            {
                var at = Beside(player, spot, 1.9f);
                var chest = room.Dungeon.SpawnChest(Result == 5 ? 1 : 2, at, Quaternion.LookRotation(Flat(player.transform.position - at)), rng.Next(), false);
                if (chest == null) break;
                if (Result == 6) { var prize = ScriptedEventRoom.Gear(ItemRarity.Unique, rng, maxTier); if (prize != null) chest.guaranteedItems.Add(prize); }
                yield return DungeonMasterReach.PutThing(chest.transform, at);
                yield return DungeonMasterReach.Leave();
                break;
            }
        }
    }

    IEnumerator Ambush(Player player, System.Random rng)
    {
        room.ShutGates();
        yield return new WaitForSeconds(.8f);
        var points = room.FreeFloor(rng, 2.5f);
        var from = player.transform.position;
        points.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
        foreach (int count in new[] { 2 + (ScriptedEventRoom.Floor - 1) / 3, 2 })
        {
            var wave = new List<Character>();
            for (int i = 0; i < count; i++)
            {
                var e = room.Summon(null, points.Count > 0 ? points[i % Mathf.Max(1, points.Count / 2)] : transform.position, rng);
                if (e != null) wave.Add(e);
                yield return new WaitForSeconds(.35f);
            }
            while (true)
            {
                wave.RemoveAll(c => c == null || !c.IsAlive);
                if (wave.Count == 0) break;
                if (!ScriptedEventRoom.RunGoing) yield break;
                yield return new WaitForSeconds(.25f);
            }
            yield return new WaitForSeconds(1f);
        }
        room.OpenGates();
    }

    // Floor in the room beside the die, across the player's view of it (so what he brings is set down where it is seen,
    // not into the player's face), on the side where his hand's way in from where he stands passes clear of the player
    // (on the other side it swept past their eyes).
    Vector3 Beside(Player player, Vector3 spot, float distance)
    {
        var across = Vector3.Cross(Vector3.up, Flat(player.transform.position - spot));
        Vector3? best = null; float bestClear = -1f;
        foreach (float turn in new[] { 0f, 180f, 35f, -35f, 145f, -145f })
        {
            var p = spot + Quaternion.Euler(0f, turn, 0f) * across * distance;
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 1f, UnityEngine.AI.NavMesh.AllAreas) || !room.Inside(hit.position)) continue;
            float clear = ClearOfPath(player.transform.position, DungeonMasterReach.StandFor(hit.position), hit.position);
            if (clear >= 2.5f) return hit.position;
            if (clear > bestClear) { bestClear = clear; best = hit.position; }
        }
        return best ?? Near(player, spot, distance);
    }

    // How far the player stands (on the floor's plane) from his hand's line between where he stands and the spot.
    static float ClearOfPath(Vector3 player, Vector3 from, Vector3 to)
    {
        var a = new Vector2(from.x, from.z); var b = new Vector2(to.x, to.z); var p = new Vector2(player.x, player.z);
        var ab = b - a;
        float t = ab.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // Floor in the room between the die and the player, a little off the die.
    Vector3 Near(Player player, Vector3 spot, float distance)
    {
        var toPlayer = Flat(player.transform.position - spot);
        foreach (float turn in new[] { 0f, 40f, -40f, 80f, -80f, 180f })
        {
            var p = spot + Quaternion.Euler(0f, turn, 0f) * toPlayer * distance;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 1f, UnityEngine.AI.NavMesh.AllAreas) && room.Inside(hit.position)) return hit.position;
        }
        return spot;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
