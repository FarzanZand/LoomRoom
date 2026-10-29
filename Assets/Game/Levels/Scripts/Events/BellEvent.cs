using System.Collections;
using UnityEngine;

// The bell (on the Bell room template's root, with a ScriptedEventRoom): a great bell hangs over the middle of the room
// with its rope down to the floor, and a plain chest waits on a step at the back. Pull the rope and the bell tolls,
// iron gates slam across the ways out and three waves come, one after another, out of the room's edge; clear them and the gates sink and the chest comes back a tier
// better (wood, iron, gold, then gold with a Legendary in it). Ring again or open the chest: opening it ends the game,
// and the rope can't be pulled once the last ring is done.
public class BellEvent : MonoBehaviour
{
    [Tooltip("The bell: swung about its local X when it tolls.")] public Transform bell;
    [Tooltip("The chest on the step (a chest placed in the template; stocked like any placed chest).")] public DungeonContainer chest;
    [Tooltip("The toll.")] public AudioClip toll;
    [Range(0f, 1f)] public float tollVolume = 1f;
    [Tooltip("Enemies in each of a ring's waves, first ring first; the number of entries is how many times it can be rung.")]
    public int[] waves = { 2, 3, 4, 5 };
    [Range(1, 5), Tooltip("Waves a ring brings, each once the last is down.")] public int wavesPerRing = 3;
    [Tooltip("The last ring's chest also holds a random piece of gear rolled Legendary.")]
    public bool legendaryLast = true;
    [Range(1, 4)] public int maxTier = 2;
    [Header("Lines")]
    public DungeonMaster.Line[] ringLines =
    {
        new() { text = "You rang?" },
        new() { text = "Again? Fine." },
        new() { text = "You want more, then." },
        new() { text = "This is the last one. I mean it." },
    };
    public DungeonMaster.Line clearedLine = new() { text = "The chest looks better already." };
    public DungeonMaster.Line cashOutLine = new() { text = "Stopping now is the sensible thing." };
    public DungeonMaster.Line doneLine = new() { text = "That's all the bell has." };

    ScriptedEventRoom room;
    int rings;
    bool busy, cashedOut;
    Quaternion bellRest, clapperRest, ropeRest;
    Transform clapper, rope;

    public int Rings => rings;
    public bool Busy => busy;
    public bool CanRing => !busy && !cashedOut && rings < waves.Length && chest != null && !chest.IsOpen;
    public string RopePrompt => cashedOut || chest == null || chest.IsOpen ? "" : rings >= waves.Length ? "" : rings == 0 ? "Ring the bell" : "Ring the bell again";

    void Start()
    {
        room = GetComponent<ScriptedEventRoom>();
        // The bell's art swings a child (its headstock on the axle), the hangers stay; its clapper swings a little behind.
        if (bell != null && bell.Find("Swing") is Transform swing) bell = swing;
        clapper = bell != null ? bell.Find("Clapper") : null;
        if (bell != null) bellRest = bell.localRotation;
        if (clapper != null) clapperRest = clapper.localRotation;
        // The rope hangs from the clapper and swings with it (about its top).
        var pull = GetComponentInChildren<BellRope>();
        rope = pull != null ? pull.transform : null;
        if (rope != null) ropeRest = rope.localRotation;
        Watch(chest);
    }

    void Watch(DungeonContainer c) { if (c != null) c.Opened += OnChestOpened; }

    void OnChestOpened(DungeonContainer c)
    {
        if (cashedOut) return;
        cashedOut = true;
        if (rings > 0 && rings < waves.Length) ScriptedEventRoom.Say(cashOutLine);
    }

    public void Ring(Player who)
    {
        if (!CanRing || room == null || !room.Find()) return;
        StartCoroutine(Toll(who));
    }

    IEnumerator Toll(Player player)
    {
        busy = true;
        int ring = rings++;
        ScriptedEventRoom.Play(toll, bell != null ? bell.position : transform.position, tollVolume);
        if (bell != null) StartCoroutine(Swing());
        if (ring < ringLines.Length) ScriptedEventRoom.Say(ringLines[ring]);
        room.ShutGates();
        yield return new WaitForSeconds(1.1f);
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 10 + ring));
        for (int w = 0; w < wavesPerRing; w++)
        {
            if (w > 0) yield return new WaitForSeconds(1f);
            // Each wave out of puffs of dust along the room's edge, on the far side from where the player stands now
            // (the whole room's floor when it has no edge cells clear of the ways in).
            var points = room.EdgeFloor(rng, 2.5f);
            if (points.Count == 0) points = room.FreeFloor(rng, 2.5f);
            var from = player != null ? player.transform.position : transform.position;
            points.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
            int count = waves[ring];
            int far = Mathf.Max(1, Mathf.Min(points.Count, Mathf.Max(count, (points.Count + 1) / 2)));
            var wave = new System.Collections.Generic.List<Character>();
            for (int i = 0; i < count; i++)
            {
                var at = points.Count > 0 ? points[i % far] : transform.position;
                var enemy = room.Summon(null, at, rng);
                if (enemy != null) wave.Add(enemy);
                yield return new WaitForSeconds(.35f);
            }
            // Until every one of them is down (a death waits for a revive; a run that ends stops it).
            while (true)
            {
                wave.RemoveAll(c => c == null || !c.IsAlive);
                if (wave.Count == 0) break;
                if (!ScriptedEventRoom.RunGoing) yield break;
                yield return new WaitForSeconds(.25f);
            }
        }
        yield return new WaitForSeconds(.8f);
        room.OpenGates();
        Upgrade(ring + 1, rng);
        ScriptedEventRoom.Say(rings >= waves.Length ? doneLine : clearedLine);
        busy = false;
    }

    // The chest comes back a tier better out of a puff of dust: rings 1 and 2 raise its tier, after that it is the best
    // tier with more in it.
    void Upgrade(int level, System.Random rng)
    {
        if (chest == null || chest.IsOpen) return;
        var at = chest.transform.position; var facing = chest.transform.rotation;
        var old = chest;
        var made = room.Dungeon.SpawnChest(level, at, facing, rng.Next());
        if (made == null) return;
        old.Opened -= OnChestOpened;
        Destroy(old.gameObject);
        chest = made;
        int tiers = room.Dungeon.ChestTiers().Count;
        // Past the best tier each ring adds a roll (and the last ring a Legendary).
        made.extraRolls += Mathf.Max(0, level - (tiers - 1));
        if (legendaryLast && level >= waves.Length)
        {
            var prize = ScriptedEventRoom.Gear(ItemRarity.Legendary, rng, maxTier);
            if (prize != null) made.guaranteedItems.Add(prize);
        }
        Watch(made);
    }

    IEnumerator Swing()
    {
        const float seconds = 3.2f;
        for (float t = 0; t < seconds && bell != null; t += Time.deltaTime)
        {
            float a = 22f * Mathf.Exp(-1.3f * t) * Mathf.Sin(t * 5.2f);
            bell.localRotation = bellRest * Quaternion.Euler(a, 0f, 0f);
            if (clapper != null) clapper.localRotation = clapperRest * Quaternion.Euler(-.6f * 22f * Mathf.Exp(-1.3f * t) * Mathf.Sin(t * 5.2f - .7f), 0f, 0f);
            if (rope != null) rope.localRotation = ropeRest * Quaternion.Euler(.35f * a, 0f, 0f);
            yield return null;
        }
        if (bell != null) bell.localRotation = bellRest;
        if (clapper != null) clapper.localRotation = clapperRest;
        if (rope != null) rope.localRotation = ropeRest;
    }
}
