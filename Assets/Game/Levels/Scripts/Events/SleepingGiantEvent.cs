using System.Collections.Generic;
using UnityEngine;

// The sleeping giant (on the Giant's hoard template's root, with a ScriptedEventRoom): a giant sleeps in the middle of
// its hoard, gold and gear lying about it and a chest at its back. It sleeps like any sleeper (EnemySleep): a crouching
// step is never heard, a walking one close by wakes it, a hit wakes it. Take what you can and get out quietly, or wake
// it and fight for the rest.
public class SleepingGiantEvent : MonoBehaviour
{
    [Tooltip("Where the giant sleeps (an Enemy socket of the template marks the same spot).")] public Transform bed;
    [Tooltip("Where the hoard lies: each spot gets a pile of gold or a piece of loot.")] public Transform[] hoard = new Transform[0];
    [Range(0f, 1f), Tooltip("Share of the hoard spots that hold gear instead of gold.")] public float gearShare = .5f;
    [Min(0)] public int goldPerPile = 6;
    [Min(0)] public int goldPerFloor = 3;
    [Tooltip("The rarity the hoard's gear is rolled at.")] public ItemRarity gearRarity = ItemRarity.Rare;
    [Range(1, 4)] public int maxTier = 2;
    [Tooltip("Its snore (empty: the level's).")] public AudioClip snore;
    [Range(0f, 3f), Tooltip("Share of a sound's reach it hears in its sleep (a sleeping pack's is .5): 1.7 hears a walking step from about 5 m and a running one from across the room; a crouching step is never heard.")] public float hearing = 1.7f;
    [Min(.2f), Tooltip("Seconds it takes to get up.")] public float riseSeconds = 1.8f;
    [Tooltip("What it leaves when it dies, besides its own drops: gear rolled at this rarity and the biggest spoils.")]
    public ItemRarity dropRarity = ItemRarity.Unique;
    [Header("Lines")]
    public DungeonMaster.Line enterLine = new() { text = "Shh. It's sleeping. Crouch." };
    [Tooltip("Said when a step woke it.")] public DungeonMaster.Line wokeLine = new() { text = "Should have crouched." };
    [Tooltip("Said when a blow woke it.")] public DungeonMaster.Line struckLine = new() { text = "Bold. It's awake now." };
    public DungeonMaster.Line quietLine = new() { text = "Not a sound. I'm impressed." };
    public DungeonMaster.Line killedLine = new() { text = "The bigger they are." };

    ScriptedEventRoom room;
    Character giant;
    EnemyBrain brain;
    bool laid, entered, woke, praised, took, struck;
    float nextScan;
    readonly List<WorldItem> loot = new();

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
        if (inside && !entered) { entered = true; if (brain.Asleep) ScriptedEventRoom.Say(enterLine); }
        if (!woke && giant.IsAlive && !brain.Asleep && brain.State != EnemyState.Dead)
        {
            woke = true;
            ScriptedEventRoom.Say(struck ? struckLine : wokeLine);
        }
        if (!took) foreach (var item in loot) if (item == null) { took = true; break; }
        // Out of the room with something of the hoard, and it never stirred.
        if (entered && took && !woke && !praised && player != null && !inside && room.DepthOf(player.transform.position) > 1.5f)
        {
            praised = true;
            ScriptedEventRoom.Say(quietLine);
        }
    }

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
        // Asleep from here on: its sleep decides when it wakes, not its event.
        ScriptedEventRoom.Free(brain);
        EnemySleep.SleepPack(new List<Character> { giant }, giant.transform.position.y, snore != null ? snore : room.Dungeon.LevelData.sleepAudio);
        if (giant.TryGetComponent<EnemySleep>(out var sleep)) { sleep.hearing = hearing; sleep.riseSeconds = riseSeconds; }
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
        foreach (var spot in hoard)
        {
            if (spot == null) continue;
            if (rng.NextDouble() < gearShare)
            {
                var item = ScriptedEventRoom.Gear(gearRarity, rng, maxTier);
                if (item != null) { var w = DungeonPickup.Spawn(item, spot.position, parent); if (w != null) loot.Add(w); }
            }
            else room.GoldAt(Mathf.Max(1, gold + rng.Next(-2, 3)), spot.position);
        }
    }

    void OnDied()
    {
        ScriptedEventRoom.Say(killedLine);
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 51));
        var at = giant.transform.position + Vector3.up * .3f;
        room.DropAt(ScriptedEventRoom.Gear(dropRarity, rng, maxTier), at + giant.transform.right * .9f);
        room.DropAt(ScriptedEventRoom.BestSpoils(), at - giant.transform.right * .9f);
    }
}
