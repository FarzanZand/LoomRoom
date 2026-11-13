using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The sleeping giant (on the Giant's hoard template's root, with a ScriptedEventRoom): a giant sleeps in the middle of
// its hoard, gold and gear lying about it and a chest at its back. A push-your-luck theft (the user, 2026-11-13):
//
//  - Each giant has a hidden wake point, rolled with the floor between Wake Point Low and Wake Point High (80-120).
//  - Each piece of its gear picked up adds (Piece Base + Piece Per Floor x (floor - 1)) x 40 / (Stealth Divisor +
//    Stealth rank) to its unrest (42 a piece on floor 1, 57 on floor 4, untrained Stealth 10 is x1). Gold adds nothing.
//  - A piece that leaves its unrest under the band's low end is safe; at or over the high end it wakes the giant at once;
//    inside the band it is risky: the Dungeon Master rolls for it, heard only (the user, 2026-11-13: no die in the room,
//    he rolls it outside the dungeon), and at Roll Seconds (1.5) after the lift past the wake point the giant gets up,
//    else the snore goes on.
//  - After each piece the Dungeon Master says a line about the next piece's band (never the line he just said).
//  - Its hearing (EnemySleep): walking, any noise wakes it only within melee range (Melee Hearing); crouched, nothing
//    but a piece crossing the wake point or a blow does. The room's own doors open quietly.
//  - However it wakes, the ambush's gates shut every way out of the room until it is dead.
//  - Co-op: the host's giant is the real one. A guest's piece reaches the host with that guest's Stealth (Used); the host
//    counts every piece, rolls and wakes it, and its lines and dice come to every player (Step). Each player's gates shut
//    as their own giant gets up.
public class SleepingGiantEvent : MonoBehaviour, ICoopEventRoom
{
    [Tooltip("Where the giant sleeps (an Enemy socket of the template marks the same spot).")] public Transform bed;
    [Tooltip("Where the hoard lies: each spot gets a pile of gold or a piece of loot.")] public Transform[] hoard = new Transform[0];
    [Min(0), Tooltip("Pieces of gear in the hoard, each on a hoard spot picked at random; the other spots hold gold (the user, 2026-11-11: 3; it was half the spots, about five).")] public int items = 3;
    [Min(0)] public int goldPerPile = 6;
    [Min(0)] public int goldPerFloor = 3;
    [Tooltip("The rarity the hoard's gear is rolled at.")] public ItemRarity gearRarity = ItemRarity.Rare;
    [Tooltip("Highest gear tier of its prize on a level with no Gear by floor table; otherwise the prize is of the floor's tiers.")] [Range(1, 12)] public int maxTier = 2;
    [Tooltip("Its snore (empty: the level's).")] public AudioClip snore;
    [Min(0f), Tooltip("Metres within which any noise wakes it while the player is not crouched (its melee range); none from further off.")]
    public float meleeHearing = 2.5f;
    [Min(.2f), Tooltip("Seconds it takes to get up.")] public float riseSeconds = 1.8f;
    [Min(0f), Tooltip("How far the room's own doors carry when opened (a dungeon door's is 7: the giant heard the way in open from across the room).")]
    public float doorNoise = 1.5f;
    [Tooltip("What it leaves when it dies, besides its own drops: gear rolled at this rarity and the biggest spoils.")]
    public ItemRarity dropRarity = ItemRarity.Unique;

    [Header("Theft")]
    [Tooltip("Its hidden wake point is rolled with the floor between these (x low, y high). A piece leaving its unrest under x is safe, at or over y wakes it, in between is a roll.")]
    public Vector2 wakePoint = new(80f, 120f);
    [Min(0f), Tooltip("Unrest a piece of gear adds on floor 1 at Stealth 10.")] public float pieceBase = 42f;
    [Min(0f), Tooltip("Unrest a piece adds for each floor past the first.")] public float piecePerFloor = 5f;
    [Min(1f), Tooltip("A piece's unrest is multiplied by 40 / (this + Stealth rank): untrained Stealth (10) is x1, Stealth 30 is x2/3.")] public float stealthDivisor = 30f;

    [Header("The roll")]
    [Tooltip("The Dungeon Master's dice rattling as he rolls, heard from outside the dungeon.")] public AudioClip roll;
    [Tooltip("The die knocking on his table as it lands, one after another.")] public AudioClip[] knocks = new AudioClip[0];
    [Tooltip("Seconds after the roll starts each knock is heard.")] public float[] knockTimes = { .55f, .8f, .95f, 1.15f };
    [Min(.5f), Tooltip("Seconds from the lift to the result.")] public float rollSeconds = 1.5f;

    [Header("Lines")]
    [Tooltip("Said the first time the table player walks in while it sleeps: one at random.")]
    public DungeonMaster.Line[] enterLines =
    {
        new() { text = "Time for nimble hands. Do you feel Lucky, punk?" },
        new() { text = "A stealthy rogue walks out rich. A bad doesn't walk out at all." },
        new() { text = "Shh. Light feet, heavy pockets." },
    };
    [Tooltip("Said when a step woke it.")] public DungeonMaster.Line wokeLine = new() { text = "Should have crouched." };
    [Tooltip("Said when a blow woke it.")] public DungeonMaster.Line struckLine = new() { text = "You hit it. It's awake now." };
    [Tooltip("Said when a piece past its wake point woke it with no roll.")] public DungeonMaster.Line lootedLine = new() { text = "It felt that." };
    [Tooltip("Said when a risky piece's roll woke it.")] public DungeonMaster.Line rollLostLine = new() { text = "Ooh. Bad roll." };
    [Tooltip("Said when a risky piece's roll went the player's way.")] public DungeonMaster.Line rollWonLine = new() { text = "Lucky. Don't push it." };
    [Tooltip("After a piece, when the next one would be safe (one at random, never the one just said).")]
    public DungeonMaster.Line[] safeLines = { new() { text = "Go on. It sleeps like a rock." }, new() { text = "Still snoring. Greedy, but fine." } };
    [Tooltip("After a piece, when the next one would be a roll.")]
    public DungeonMaster.Line[] riskyLines = { new() { text = "Go for more? Could go either way. I'm not telling which." }, new() { text = "Feeling lucky enough to try another one?" }, new() { text = "I'd roll for the next item." } };
    [Tooltip("After a piece, when the next one would wake it.")]
    public DungeonMaster.Line[] unsafeLines = { new() { text = "Take another and you're fighting it." }, new() { text = "I wouldn't. And I'd know." } };
    public DungeonMaster.Line quietLine = new() { text = "Not a sound. I'm impressed." };
    public DungeonMaster.Line killedLine = new() { text = "That was a lot of giant." };

    public enum Band { Safe, Risky, Unsafe }

    ScriptedEventRoom room;
    Character giant;
    EnemyBrain brain;
    bool laid, entered, woke, praised, took, struck, looted, rolling;
    int goldAtEntry = -1;
    float nextScan;
    readonly List<WorldItem> loot = new();
    readonly HashSet<int> taken = new();
    readonly List<GameObject> lootObjects = new();
    readonly Queue<(int stealth, Character who)> pieces = new();
    DungeonMaster.Line lastLine;

    // The theft's state, for tests: its unrest so far and its hidden wake point.
    public float Unrest { get; private set; }
    public float WakePoint { get; private set; }
    public int PiecesLeft { get { int n = 0; foreach (var w in loot) if (w != null) n++; return n; } }

    void Start()
    {
        room = GetComponent<ScriptedEventRoom>();
    }

    void Update()
    {
        if (room == null || !room.Find()) return;
        if (giant == null) { if (Time.time >= nextScan) { nextScan = Time.time + .5f; FindGiant(); } return; }
        // The hoard is laid once the table player is out on the floor (laid while it was built, the reveal would lift
        // it out of the table with nothing under it yet).
        if (!laid && ScriptedEventRoom.TablePlayer != null) Lay();
        var player = ScriptedEventRoom.TablePlayer;
        bool inside = room.PlayerInside;
        if (inside && !entered)
        {
            entered = true;
            if (brain.Asleep && enterLines != null && enterLines.Length > 0) ScriptedEventRoom.Say(enterLines[Random.Range(0, enterLines.Length)]);
            goldAtEntry = player != null && player.Wallet != null ? player.Wallet.Gold : -1;
        }
        if (!woke && giant.IsAlive && !brain.Asleep && brain.State != EnemyState.Dead)
        {
            woke = true;
            // Co-op: on a guest the host's lines come (Step).
            if (!looted && !Coop.IsGuest) Tell(struck ? LineStruck : LineWoke);
        }
        // The gates shut once it is up with the player in its room (one who woke it from outside meets them coming in).
        if (woke && giant.IsAlive && inside && !room.GatesShut) room.ShutGates();
        // Each piece of its gear picked up, in the order taken: by this player (a teammate's shared piece is theirs to
        // count). Co-op: a guest's go to the host, whose giant it is.
        for (int i = 0; i < loot.Count; i++)
            if (loot[i] == null && taken.Add(i))
            {
                if (CoopWorld.TakenByTeammate(lootObjects[i])) continue;
                took = true;
                if (Coop.IsGuest) CoopWorld.EventUsed(this, 1 + StealthOf(player));
                else pieces.Enqueue((StealthOf(player), player));
            }
        if (pieces.Count > 0 && !rolling) StartCoroutine(Take());
        // Its gold counts too (picked up as it is walked over).
        if (!took && inside && goldAtEntry >= 0 && player != null && player.Wallet != null && player.Wallet.Gold > goldAtEntry) took = true;
        // Out of the room with something of the hoard, and it never stirred.
        if (entered && took && !woke && !praised && player != null && !inside && room.DepthOf(player.transform.position) > 1.5f)
        {
            praised = true;
            ScriptedEventRoom.Say(quietLine);
        }
    }

    // ── The theft ───────────────────────────────────────────────────

    // Unrest one piece adds now: (42 + 5 x (floor - 1)) x 40 / (30 + Stealth).
    public float PieceUnrest(Player player) => PieceUnrest(StealthOf(player));
    public float PieceUnrest(int stealth) => (pieceBase + piecePerFloor * (ScriptedEventRoom.Floor - 1)) * 40f / (stealthDivisor + stealth);

    // The Stealth rank of whoever picks up (10 untrained, or with no sheet).
    static int StealthOf(Player player)
    {
        var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        return progress != null ? progress.Rank(AdventureSkill.Stealth) : 10;
    }

    public Band BandOf(float unrest) => unrest < wakePoint.x ? Band.Safe : unrest >= wakePoint.y ? Band.Unsafe : Band.Risky;

    // The pieces taken, one after another, each with its taker's Stealth: a risky one's roll is over before the next is
    // reckoned. Only on the host in co-op.
    IEnumerator Take()
    {
        rolling = true;
        while (pieces.Count > 0)
        {
            var (stealth, who) = pieces.Dequeue();
            if (woke || !giant.IsAlive || !brain.Asleep) { pieces.Clear(); break; }
            Unrest += PieceUnrest(stealth);
            switch (BandOf(Unrest))
            {
                case Band.Safe: SayNext(stealth); break;
                case Band.Unsafe: Wake(who, LineLooted); break;
                default:
                {
                    bool lost = Unrest >= WakePoint;
                    CoopWorld.EventStep(this, StepRoll);
                    yield return Roll(ScriptedEventRoom.TablePlayer);
                    if (lost) Wake(who, LineRollLost);
                    else if (!woke && brain.Asleep) { Tell(LineRollWon); SayNext(stealth); }
                    break;
                }
            }
        }
        rolling = false;
    }

    void Wake(Character who, int line)
    {
        if (woke || !brain.Asleep) return;
        looted = true;
        Tell(line);
        if (giant.TryGetComponent<EnemySleep>(out var sleep)) brain.Wake(sleep.riseSeconds); else brain.Wake(riseSeconds);
        if (who != null) brain.Provoke(who.transform.position);
    }

    // What the next piece would do, while one is left: one line of its band, never the one just said.
    void SayNext(int stealth)
    {
        var band = BandOf(Unrest + PieceUnrest(stealth));
        var lines = NextLines(band);
        if (lines == null || lines.Length == 0) return;
        var pool = new List<int>();
        for (int i = 0; i < lines.Length; i++) if (lines[i] != null && !lines[i].IsEmpty && lines[i] != lastLine) pool.Add(i);
        if (pool.Count == 0) for (int i = 0; i < lines.Length; i++) if (lines[i] != null && !lines[i].IsEmpty) pool.Add(i);
        if (pool.Count == 0) return;
        int pick = pool[Random.Range(0, pool.Count)];
        lastLine = lines[pick];
        Tell(LineNext + (int)band * 100 + pick);
    }

    DungeonMaster.Line[] NextLines(Band band) => band switch { Band.Safe => safeLines, Band.Unsafe => unsafeLines, _ => riskyLines };

    // A risky piece: the Dungeon Master's dice, heard rolling and knocking on his table (at the player's ears: he rolls
    // outside the dungeon), and the result at Roll Seconds.
    IEnumerator Roll(Player player)
    {
        float start = Time.time;
        Vector3 Ears() => player != null ? player.transform.position + Vector3.up * 1.6f : giant.transform.position;
        if (roll != null) ScriptedEventRoom.Play(roll, Ears(), .8f);
        for (int k = 0; k < knockTimes.Length && knocks.Length > 0; k++)
        {
            while (Time.time - start < knockTimes[k]) yield return null;
            ScriptedEventRoom.Play(knocks[k % knocks.Length], Ears(), Mathf.Lerp(.75f, .35f, k / Mathf.Max(1f, knockTimes.Length - 1f)));
        }
        while (Time.time - start < rollSeconds) yield return null;
    }

    // ── Its lines, said here and sent to every guest ──────────────────

    const int LineWoke = 0, LineStruck = 1, LineLooted = 2, LineRollLost = 3, LineRollWon = 4, LineNext = 1000;
    const int StepLine = 1, StepRoll = 2;

    void Tell(int line)
    {
        Say(line);
        CoopWorld.EventStep(this, StepLine, line);
    }

    void Say(int line)
    {
        if (line >= LineNext)
        {
            // A line about the next piece, only while this player has one left.
            var lines = NextLines((Band)((line - LineNext) / 100));
            int i = (line - LineNext) % 100;
            if (PiecesLeft > 0 && lines != null && i < lines.Length) ScriptedEventRoom.Say(lines[i]);
            return;
        }
        ScriptedEventRoom.Say(line switch { LineStruck => struckLine, LineLooted => lootedLine, LineRollLost => rollLostLine, LineRollWon => rollWonLine, _ => wokeLine });
    }

    // ── Co-op ──
    // Host: a guest picked up a piece of its gear (what = 1 + their Stealth); it is counted as the host's own are.
    public void Used(int what, Character who)
    {
        if (Coop.IsHost && what > 0 && giant != null) pieces.Enqueue((what - 1, who));
    }

    // Guest: the host's giant had a line said, or the Dungeon Master rolled for a piece.
    public void Step(int step, int value, int seed)
    {
        if (step == StepLine) Say(value);
        else if (step == StepRoll && giant != null) StartCoroutine(Roll(ScriptedEventRoom.TablePlayer));
    }

    // ── Setting up ──────────────────────────────────────────────────

    void FindGiant()
    {
        var at = bed != null ? bed.position : transform.position;
        Character best = null; float bestDistance = 4f;
        foreach (var c in room.EnemiesInside())
        {
            float d = Vector3.Distance(c.transform.position, at);
            if (d < bestDistance) { best = c; bestDistance = d; }
        }
        if (best == null || !best.TryGetComponent(out brain)) return;
        giant = best;
        if (bed != null) giant.transform.rotation = bed.rotation;
        // Its hidden wake point, the same every time for this floor.
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 52));
        WakePoint = Mathf.Lerp(wakePoint.x, wakePoint.y, (float)rng.NextDouble());
        // Asleep from here on: its sleep decides when it wakes, not its event.
        ScriptedEventRoom.Free(brain);
        EnemySleep.SleepPack(new List<Character> { giant }, giant.transform.position.y, snore != null ? snore : room.Dungeon.LevelData.sleepAudio);
        if (giant.TryGetComponent<EnemySleep>(out var sleep)) { sleep.wakeReach = meleeHearing; sleep.deafToCrouched = true; sleep.riseSeconds = riseSeconds; }
        // Its own doors open quietly: getting in is not what wakes it.
        var ways = room.Dungeon.WaysOut(room.Room);
        foreach (var door in room.Dungeon.GetComponentsInChildren<DungeonDoor>(true))
            foreach (var (edge, _) in ways)
                if (Vector3.Distance(door.transform.position, edge) < 2.5f) { door.openNoise = Mathf.Min(door.openNoise, doorNoise); break; }
        giant.Died += OnDied;
        giant.Damaged += OnStruck;
    }

    // A blow landing in its sleep (it wakes on it).
    void OnStruck(DamageInfo info) { if (!woke && brain != null && brain.Asleep) struck = true; }

    void OnDestroy() { if (giant != null) { giant.Died -= OnDied; giant.Damaged -= OnStruck; } }

    void Lay()
    {
        laid = true;
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 50));
        int gold = goldPerPile + goldPerFloor * (ScriptedEventRoom.Floor - 1);
        var parent = room.Dungeon.transform;
        // Which spots hold the gear: Items of them, picked at random (the rest hold gold).
        var spots = new List<Transform>();
        foreach (var spot in hoard) if (spot != null) spots.Add(spot);
        var gearSpots = new HashSet<Transform>();
        var pool = new List<Transform>(spots);
        for (int i = 0; i < items && pool.Count > 0; i++) { int k = rng.Next(pool.Count); gearSpots.Add(pool[k]); pool.RemoveAt(k); }
        foreach (var spot in spots)
        {
            if (gearSpots.Contains(spot))
            {
                var item = ScriptedEventRoom.Gear(gearRarity, rng, maxTier);
                if (item != null) { var w = DungeonPickup.Spawn(item, spot.position, parent); if (w != null) { loot.Add(w); lootObjects.Add(w.gameObject); } }
            }
            else room.GoldAt(Mathf.Max(1, gold + rng.Next(-2, 3)), spot.position);
        }
    }

    void OnDied()
    {
        ScriptedEventRoom.Say(killedLine);
        room.OpenGates();
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 51));
        var at = giant.transform.position + Vector3.up * .3f;
        room.DropAt(ScriptedEventRoom.Gear(dropRarity, rng, maxTier), at + giant.transform.right * .9f);
        room.DropAt(ScriptedEventRoom.BestSpoils(), at - giant.transform.right * .9f);
    }
}
