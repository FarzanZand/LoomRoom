using Sirenix.OdinInspector;
using UnityEngine;

// What a dungeon's rooms may hold beyond walls and enemies: room events, heights and room lighting. Set on the level's
// base biome (its Rooms tab, TableLevelData.roomFeatures reads it); a stage's biome can replace the whole set for its floors (DungeonBiome > Rooms >
// Override Dungeon Settings). A hand-built room that is built around something switched off (a chasm, a shrine) is left
// out of the floor; one that only uses it on the side (candles, a sleeping pack) is built without it.
[System.Serializable]
public class DungeonRoomFeatures
{
    [Title("Room events", HorizontalLine = false)]
    [ToggleLeft, Tooltip("A shrine in some quiet rooms: bleed on it to hone the weapon in your hand (or your shield or armour) +1.")]
    public bool shrineBargains = true;
    [ShowIf(nameof(shrineBargains)), Range(0f, 1f), Indent, Tooltip("Chance a quiet room without a hand-built interior gets a shrine.")]
    public float shrineChance = .35f;
    [ShowIf(nameof(shrineBargains)), Range(.05f, .9f), Indent, Tooltip("Share of the player's most health a shrine takes for honing.")]
    public float shrinePrice = .35f;
    [ToggleLeft, Tooltip("The doors bar shut behind you and enemies come in waves; when the last one falls the bars lift and a chest is left.")]
    public bool ambushes = true;
    [ShowIf(nameof(ambushes)), Range(0f, 1f), Indent, Tooltip("Chance a combat room becomes an ambush (rooms built for one always are).")]
    public float ambushChance = .08f;
    [ShowIf(nameof(ambushes)), Range(0, 1), Indent, Tooltip("Most ambushes on one floor, rooms built for one included: 1 (a floor has one or none) or 0 (none).")]
    public int maxAmbushes = 1;
    [ToggleLeft, Tooltip("A chest on a pressure plate: opening it brings spikes up through the floor around it.")]
    public bool trappedTreasure = true;
    [ShowIf(nameof(trappedTreasure)), Range(0f, 1f), Indent, Tooltip("Chance a chest is trapped (chests built on a plate always are).")]
    public float trapChance = .3f;
    [ShowIf(nameof(trappedTreasure)), Range(0f, 1f), Indent, Tooltip("Share of the player's most health a thrust of spikes takes (every spike plate on these floors).")]
    public float spikeDamage = .2f;
    [ToggleLeft, Tooltip("The room's pack lies asleep: creep up for backstabs; noise, a light step close by or a hit wakes them.")]
    public bool sleepingPacks = true;
    [ShowIf(nameof(sleepingPacks)), Range(0f, 1f), Indent, Tooltip("Chance a combat room's pack is asleep.")]
    public float sleepChance = .1f;
    [ShowIf(nameof(sleepingPacks)), Min(0), Indent, Tooltip("Most sleeping packs on one floor, rooms built for one included (0: no limit).")]
    public int maxSleepingPacks = 3;

    [Title("Heights", HorizontalLine = false)]
    [ToggleLeft, Tooltip("Rooms with chasms and holes in the floor (bridges across them).")]
    public bool pits = true;
    [ShowIf(nameof(pits)), ToggleLeft, Indent, Tooltip("The player can fall into a pit (it hurts and puts them back at the edge). Off: a pit's edge holds the player back like a wall they slide along. Enemies knocked back over the edge fall in either way.")]
    public bool playerFallsInPits;
    [ShowIf("@pits && playerFallsInPits"), Range(0f, 1f), Indent, Tooltip("Share of the player's most health a fall into a pit takes.")]
    public float pitFallDamage = .25f;
    [ToggleLeft, Tooltip("Rooms with a sunken floor: steps down into a lower court.")]
    public bool sunkenFloors = true;
    [ToggleLeft, Tooltip("Raised ledges and galleries where archers stand.")]
    public bool archerLedges = true;

    [Title("Room light", HorizontalLine = false)]
    [ToggleLeft, Tooltip("Rooms with no torches and a tight light radius: only your own light shows the way.")]
    public bool darkRooms = true;
    [ShowIf(nameof(darkRooms)), Range(0f, 1f), Indent, Tooltip("Chance a generated room is dark.")]
    public float darkChance = .08f;
    [ToggleLeft, Tooltip("Rooms lit by clusters of candles.")]
    public bool candlelitRooms = true;
    [ToggleLeft, Tooltip("Rooms open to the sky: a cold shaft of moonlight.")]
    public bool moonlitRooms = true;
    [ToggleLeft, Tooltip("Rooms in a sickly green light.")]
    public bool eerieRooms = true;
    [ShowIf(nameof(eerieRooms)), Range(0f, 1f), Indent, Tooltip("Chance a generated room is green-lit.")]
    public float eerieChance = .06f;

    public bool Allows(DungeonRoomFeature f) => f switch
    {
        DungeonRoomFeature.ShrineBargain => shrineBargains,
        DungeonRoomFeature.Ambush => ambushes,
        DungeonRoomFeature.TrappedTreasure => trappedTreasure,
        DungeonRoomFeature.SleepingPack => sleepingPacks,
        DungeonRoomFeature.Pit => pits,
        DungeonRoomFeature.SunkenFloor => sunkenFloors,
        DungeonRoomFeature.ArcherLedge => archerLedges,
        DungeonRoomFeature.Dark => darkRooms,
        DungeonRoomFeature.Candlelit => candlelitRooms,
        DungeonRoomFeature.Moonlit => moonlitRooms,
        DungeonRoomFeature.Eerie => eerieRooms,
        _ => true,
    };

    // Every feature a set allows (a template needing all of them may be used).
    public bool AllowsAll(DungeonRoomFeatureMask mask)
    {
        for (int i = 0; i < 16; i++)
            if (((int)mask & (1 << i)) != 0 && !Allows((DungeonRoomFeature)i)) return false;
        return true;
    }
}

// Serialized by value: append only.
public enum DungeonRoomFeature { ShrineBargain = 0, Ambush = 1, TrappedTreasure = 2, SleepingPack = 3, Pit = 4, SunkenFloor = 5, ArcherLedge = 6, Dark = 7, Candlelit = 8, Moonlit = 9, Eerie = 10 }

[System.Flags]
public enum DungeonRoomFeatureMask
{
    None = 0,
    ShrineBargain = 1 << (int)DungeonRoomFeature.ShrineBargain, Ambush = 1 << (int)DungeonRoomFeature.Ambush, TrappedTreasure = 1 << (int)DungeonRoomFeature.TrappedTreasure,
    SleepingPack = 1 << (int)DungeonRoomFeature.SleepingPack, Pit = 1 << (int)DungeonRoomFeature.Pit, SunkenFloor = 1 << (int)DungeonRoomFeature.SunkenFloor,
    ArcherLedge = 1 << (int)DungeonRoomFeature.ArcherLedge, Dark = 1 << (int)DungeonRoomFeature.Dark, Candlelit = 1 << (int)DungeonRoomFeature.Candlelit,
    Moonlit = 1 << (int)DungeonRoomFeature.Moonlit, Eerie = 1 << (int)DungeonRoomFeature.Eerie,
}
