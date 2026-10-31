using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The dice room (on the Dice room template's root, with a ScriptedEventRoom): a room open to the sky with his gaming board
// on the floor under the opening, six painted squares saying what each face brings, and a little gambler's table at its
// edge where a skeleton died over his last bet. Walking in, the Dungeon Master says what the room is; shaking the cup on
// the table (DiceCup) asks for the roll: he leans over, brings his die up in his fingers and drops it through the opening
// onto the board, where it comes to rest on the square of its own number, and a light comes up on that square:
//   1 an ambush (gates and two waves)   2 he sets a tough enemy down     3 nothing
//   4 a rest (health back to full)      5 he sets down an iron chest     6 he sets down a gold chest with gear rolled Unique
// The die is his: it stays where it lands. The number is picked from the floor's seed, then the die is made to land on it.
public class DiceEvent : MonoBehaviour, ICoopEventRoom
{
    [Tooltip("The Dungeon Master's die (a cube; its faces: +Y 1, +X 2, +Z 3, -Z 4, -X 5, -Y 6).")] public GameObject diePrefab;
    [Tooltip("Where it lands when the board has no squares (under the opening).")] public Transform landing;
    [Tooltip("The board's squares, 1 to 6: the die comes to rest on its own number's square.")] public Transform[] squares = new Transform[0];
    [Tooltip("A light (the board's child) moved over the square the die lands on and faded in.")] public Light squareLight;
    [Min(0f)] public float squareLightIntensity = 6f;
    [Min(.5f), Tooltip("Metres into the room (from the nearest way in) before he says what the room is.")] public float triggerDepth = 2.5f;
    public string cupPrompt = "Shake the cup";
    [Tooltip("The cup's rattle.")] public AudioClip rattle;
    [Min(.3f), Tooltip("Seconds his hand takes to bring the die up over the room.")] public float raiseSeconds = 1.6f;
    [Tooltip("A knock for each bounce.")] public AudioClip[] knocks = new AudioClip[0];
    [Tooltip("Rolling to a stop.")] public AudioClip roll;
    [Tooltip("The tough enemy a 2 brings (empty: the room's own pick).")] public GameObject toughEnemy;
    [Range(1, 4), Tooltip("How many tough enemies a 2 brings, set down one after another.")] public int toughCount = 2;
    [Range(1, 4), Tooltip("Each of the ambush's waves (a 1) is this many times its usual size.")] public int ambushScale = 2;
    [Tooltip("The effect on the player when a 4 heals them.")] public GameObject healEffect;
    [Tooltip("Testing: the face it always lands on (0: the floor's seed picks).")] [Range(0, 6)] public int forceFace;
    [Range(1, 12)] public int maxTier = 2;
    [Header("Lines")]
    [Tooltip("Said once, as the player comes into the room.")]
    public DungeonMaster.Line enterLine = new() { text = "One roll. Shake the cup if you dare, and the board says what you get." };
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
    bool rolled, greeted;
    Vector3? seenAt;
    public int Result { get; private set; }

    void Start()
    {
        room = GetComponent<ScriptedEventRoom>();
        if (squareLight != null) { squareLight.intensity = 0f; squareLight.enabled = false; }
    }

    // The cup: once, while his hand is free.
    public bool CanRoll => !rolled && room != null && room.Find() && !DungeonMasterReach.Busy && ScriptedEventRoom.RunGoing;

    // Co-op: a teammate shook the cup (their stand-in here): the same roll here, from the same seed.
    public void Used(int what, Character who) => Shake(who);

    // Co-op guests: the host's ambush (a 1) is over (step 1): the gates lift.
    bool ambushOver;
    public void Step(int step, int value, int seed) { if (step == 1) ambushOver = true; }

    public void Shake(Character player)
    {
        if (!CanRoll || player == null) return;
        rolled = true;
        greeted = true;
        if (rattle != null) ScriptedEventRoom.Play(rattle, player.transform.position, .8f);
        StartCoroutine(Roll(player));
    }

    void Update()
    {
        if (greeted || room == null || !room.Find()) return;
        var player = ScriptedEventRoom.TablePlayer;
        if (player == null || !player.IsAlive || !room.Inside(player.transform.position)) { seenAt = null; return; }
        // The first place the player is seen inside, then a metre's walk in from it and well inside.
        if (seenAt == null) seenAt = player.transform.position;
        var walked = player.transform.position - seenAt.Value; walked.y = 0f;
        if (walked.magnitude < 1f || room.DepthOf(player.transform.position) < triggerDepth) return;
        greeted = true;
        ScriptedEventRoom.Say(enterLine);
    }

    // The square of a number, else the landing.
    Vector3 SpotFor(int face)
    {
        if (squares != null && face >= 1 && face <= squares.Length && squares[face - 1] != null) return squares[face - 1].position;
        return landing != null ? landing.position : transform.position;
    }

    IEnumerator Roll(Character player)
    {
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 60));
        Result = ForcedNext > 0 ? ForcedNext : forceFace > 0 ? forceFace : 1 + rng.Next(6);
        ForcedNext = 0;
        // Its rattle first, then his word.
        yield return new WaitForSeconds(.6f);
        ScriptedEventRoom.Say(rollLine);
        var spot = SpotFor(Result);
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
        StartCoroutine(LightSquare(die.transform.position));
        yield return new WaitForSeconds(.5f);
        ScriptedEventRoom.Say(faceLines[Result - 1]);
        yield return new WaitForSeconds(.8f);
        yield return Outcome(player, spot, rng);
    }

    // The square the die rests on lights up (its light stays: the board says what the room became).
    IEnumerator LightSquare(Vector3 die)
    {
        if (squareLight == null) yield break;
        var t = squareLight.transform;
        t.position = new Vector3(die.x, t.position.y, die.z);
        squareLight.intensity = 0f;
        squareLight.enabled = true;
        for (float u = 0; u < 1f; u += Time.deltaTime / .8f)
        {
            squareLight.intensity = squareLightIntensity * Mathf.SmoothStep(0, 1, u);
            yield return null;
        }
        squareLight.intensity = squareLightIntensity;
    }

    // The velocity that carries the die from where he lets go to the spot in a high arc (straight down when he lets go
    // over it).
    const float Gravity = 30f;
    static float TossSeconds(float distance) => Mathf.Clamp(.35f + distance * .04f, .9f, 2.6f);
    static Vector3 Toss(Vector3 from, Vector3 to)
    {
        var flat = to - from; flat.y = 0f;
        if (flat.magnitude < 1.5f) return Vector3.zero;
        float t = TossSeconds(flat.magnitude);
        var v = flat / t;
        v.y = (to.y - from.y + .5f * Gravity * t * t) / t;
        return v;
    }

    // Falls from his fingers (or flies in from a toss), lands, bounces lower and shorter each time along the way it was
    // going and tips over once onto its face, coming to rest on its own number's square. The path is planned backward
    // from where it rests, so every landing is a real one and it never slides into place.
    IEnumerator Tumble(Transform die, Vector3 spot, int face, System.Random rng, Vector3 toss)
    {
        float half = Mathf.Max(.1f, die.lossyScale.y * .5f * MeshSize(die));
        float floorY = spot.y + half;
        var start = die.position;
        var restRotation = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up) * Quaternion.FromToRotation(FaceAxes[face - 1], Vector3.up);
        // Where it comes to rest: its own square a little off the middle on a board, else the spot.
        var rest = spot;
        if (squares != null && squares.Length >= 6) rest += new Vector3(((float)rng.NextDouble() - .5f) * .3f, 0f, ((float)rng.NextDouble() - .5f) * .3f);
        rest.y = floorY;
        // It travels across the floor the way it was thrown, else a way of its own.
        var flatToss = new Vector3(toss.x, 0f, toss.z);
        var heading = flatToss.sqrMagnitude > .01f ? flatToss.normalized : Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward;
        // A drop from over the room falls straight from rest; a toss keeps its arc from his hand (which may be below
        // the room's floor, at the table's edge), only aimed at the first landing instead of the square (worked out
        // below, once the landing is known).
        float vy0 = 0f;
        float drop = Mathf.Max(0f, start.y - floorY);
        float fall = Mathf.Max(.05f, Mathf.Sqrt(2f * drop / Gravity));
        if (flatToss.sqrMagnitude > .01f)
        {
            var toSpot = spot - start; toSpot.y = 0f;
            fall = TossSeconds(toSpot.magnitude);
            vy0 = (floorY - start.y + .5f * Gravity * fall * fall) / fall;
        }
        float impact = Mathf.Abs(vy0 - Gravity * fall);
        // The bounces: each keeps a share of the last one's height (the first a little more, but never much over a
        // metre, however hard it came down), until it is too low to leave the floor; each hop is shorter, as it loses
        // speed.
        var hopUp = new List<float>(); var hopLength = new List<float>();
        float up = Mathf.Min(impact * .38f, 8f), speed = Mathf.Clamp(flatToss.magnitude * .25f, 1.3f, 2.4f);
        while (up >= 1.2f && hopUp.Count < 3)
        {
            hopUp.Add(up); hopLength.Add(speed * 2f * up / Gravity);
            up *= .3f; speed *= .7f;
        }
        // The last of it: tipping over one edge onto its face, one die's width.
        float tip = half * 2f;
        float travel = tip; foreach (var l in hopLength) travel += l;
        var land = rest - heading * travel;
        var spin = Random.onUnitSphere * 720f;
        // The flight down to its first landing.
        var flight = land - start; flight.y = 0f;
        var across = flight / fall;
        for (float t = 0f; t < fall; t += Time.deltaTime)
        {
            die.position = start + across * t + Vector3.up * (vy0 * t - .5f * Gravity * t * t);
            die.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        var at = land;
        die.position = at;
        Knock(0, at, impact);
        DungeonMasterReach.Shake(.25f);
        // The hops, each from one landing to the next.
        for (int k = 0; k < hopUp.Count; k++)
        {
            spin *= .55f;
            float time = 2f * hopUp[k] / Gravity;
            var from = at; var to = at + heading * hopLength[k];
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                die.position = Vector3.Lerp(from, to, t / time) + Vector3.up * (hopUp[k] * t - .5f * Gravity * t * t);
                die.Rotate(spin * Time.deltaTime, Space.World);
                yield return null;
            }
            at = to; die.position = at;
            Knock(k + 1, at, hopUp[k]);
        }
        // It tips over onto its face (its middle rises as it goes over the edge) and is still.
        if (roll != null) ScriptedEventRoom.Play(roll, at, .6f);
        var turnFrom = die.rotation;
        const float tipSeconds = .4f;
        for (float t = 0f; t < tipSeconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / tipSeconds);
            die.SetPositionAndRotation(Vector3.Lerp(at, rest, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * half * .41f, Quaternion.Slerp(turnFrom, restRotation, u));
            yield return null;
        }
        die.SetPositionAndRotation(rest, restRotation);
        if (knocks.Length > 0) ScriptedEventRoom.Play(knocks[0], rest, .35f);
    }

    void Knock(int bounce, Vector3 at, float speed)
    {
        if (knocks.Length > 0) ScriptedEventRoom.Play(knocks[bounce % knocks.Length], at, Mathf.Clamp01(.3f + speed * .04f));
    }

    // The die's own size along one axis (it is a cube): its mesh's bounds.
    static float MeshSize(Transform die)
    {
        var mf = die.GetComponentInChildren<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.y * mf.transform.localScale.y : 1f;
    }

    IEnumerator Outcome(Character player, Vector3 spot, System.Random rng)
    {
        switch (Result)
        {
            case 1: yield return Ambush(player, rng); break;
            case 2:
            {
                // Co-op guests: the host's hand brings them; he goes to where they land here.
                if (Coop.IsGuest)
                {
                    for (int i = 0; i < toughCount; i++) { yield return DungeonMasterReach.Approach(Beside(player, spot, 2.4f + 1.3f * i)); yield return new WaitForSeconds(2f); }
                    yield return DungeonMasterReach.Leave();
                    break;
                }
                var prefab = toughEnemy != null ? toughEnemy : room.Dungeon.PickEnemy(room.Room, rng);
                bool put = false;
                for (int i = 0; i < toughCount && prefab != null; i++)
                {
                    // Each a little further round from the last, so they don't land on one spot.
                    var at = Beside(player, spot, 2.4f + 1.3f * i);
                    var enemy = room.Dungeon.SpawnAmbusher(prefab, at, Quaternion.LookRotation(Flat(player.transform.position - at)));
                    if (enemy == null) continue;
                    yield return DungeonMasterReach.Put(enemy, at);
                    put = true;
                }
                if (put) yield return DungeonMasterReach.Leave();
                break;
            }
            case 3: break;
            case 4:
                // Only the one who rolled is healed (on their own machine: a teammate's stand-in here only shows it).
                if (player is Player && player.Stats != null) player.Stats.Heal(player.Stats.MaxHealth);
                if (healEffect != null && player != null) Destroy(Instantiate(healEffect, player.transform.position + player.transform.forward * .6f, Quaternion.identity), 4f);
                break;
            case 5:
            case 6:
            {
                var at = Beside(player, spot, 1.9f);
                var chest = room.Dungeon.SpawnChest(Result == 5 ? 1 : 2, at, Quaternion.LookRotation(Flat(player.transform.position - at)), rng.Next(), false);
                if (chest == null) break;
                CoopWorld.AddContainer(chest);
                if (Result == 6) { var prize = ScriptedEventRoom.Gear(ItemRarity.Unique, rng, maxTier); if (prize != null) chest.guaranteedItems.Add(prize); }
                yield return DungeonMasterReach.PutThing(chest.transform, at);
                yield return DungeonMasterReach.Leave();
                break;
            }
        }
    }

    IEnumerator Ambush(Character player, System.Random rng)
    {
        room.ShutGates();
        // Co-op guests: the host's waves come from there; its word lifts the gates.
        if (Coop.IsGuest)
        {
            while (!ambushOver && ScriptedEventRoom.RunGoing) yield return new WaitForSeconds(.25f);
            room.OpenGates();
            yield break;
        }
        yield return new WaitForSeconds(.8f);
        var points = room.FreeFloor(rng, 2.5f);
        var from = player.transform.position;
        points.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
        foreach (int size in new[] { 2 + (ScriptedEventRoom.Floor - 1) / 3, 2 })
        {
            int count = size * ambushScale;
            // The far half of the room's free floor, or more of it when the wave needs more spots.
            int spots = Mathf.Max(1, Mathf.Min(points.Count, Mathf.Max(count, points.Count / 2)));
            var wave = new List<Character>();
            for (int i = 0; i < count; i++)
            {
                var e = room.Summon(null, points.Count > 0 ? points[i % spots] : transform.position, rng);
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
        CoopWorld.EventStep(this, 1);
    }

    // Floor in the room beside the die, across the player's view of it (so what he brings is set down where it is seen,
    // not into the player's face), on the side where his hand's way in from where he stands passes clear of the player
    // (on the other side it swept past their eyes).
    Vector3 Beside(Character player, Vector3 spot, float distance)
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
    Vector3 Near(Character player, Vector3 spot, float distance)
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
