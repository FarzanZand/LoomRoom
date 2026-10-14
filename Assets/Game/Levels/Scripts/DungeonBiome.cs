using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One stretch of a dungeon run: cellars, crypt, caves. A level lists its biomes in order with a
// floor count each (TableLevelData.stages). Everything that makes a biome feel different lives
// here: how it looks and sounds, what lives in it and how densely, what it drops, and the guardian
// waiting on its last floor. Lists here are added to the level's (which every biome shares);
// lighting and music replace the level's only when set here.
[CreateAssetMenu(menuName = "LoomRoom/Dungeon Biome", fileName = "Biome")]
public class DungeonBiome : ScriptableObject
{
    const string Look = "Look", RoomsTab = "Rooms", Life = "Population", Loot = "Rewards", Boss = "Guardian", Sound = "Audio";

    [TabGroup(Look), InfoBox("Lists in this biome are added to the level's, which every biome shares. Put things all biomes use on the level; put what makes this biome different here.")]
    [TabGroup(Look)] public string displayName = "The Crypt";
    [TabGroup(Look), TextArea, Tooltip("Posted to the message log when the run enters this biome. Keep it short and plain.")]
    public string entryMessage;

    [TabGroup(Look), Tooltip("Added to the level's room styles in this biome.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Added to the level's corridor styles in this biome.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Added to the level's room shapes in this biome.")]
    public DungeonShapeChoice[] roomShapes = new DungeonShapeChoice[0];
    [TabGroup(Look), Tooltip("Added to the level's props in this biome.")]
    public GameObject[] props = new GameObject[0];
    [TabGroup(Look), Tooltip("Added to the level's furnishing rules in this biome.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [TabGroup(Look), Tooltip("Added to the level's breakables in this biome.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];

    // Same numbers as DungeonMoodLighting (a preset of that name in Resources/DungeonLighting), plus Custom.
    public enum Lighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8, Custom = 100 }
    [TabGroup(Look), Title("Lighting"), Tooltip("The lighting in this biome: a preset, or Custom to set every value below. On dark floors the Lighting Manager holds the ambient down (Dungeon darkness), so the torches give most of the light.")]
    public Lighting lighting = Lighting.Darkness;
    [TabGroup(Look), Tooltip("Skip the Lighting Manager's dungeon darkness here: the preset's ambient light reaches everywhere, so unlit places stay visible.")]
    public bool litThroughout;
    [TabGroup(Look), Tooltip("Board-game levels: light the board with this biome's lighting above instead of the room's own light (the level's Room Lighting), so each biome of a board level can look different.")]
    public bool lightBoard;
    [System.Serializable]
    public class RoomTemplateChoice
    {
        [AssetsOnly, Tooltip("A DungeonRoomTemplate prefab: a hand-built room (footprint, kit blocks, props, sockets).")] public GameObject template;
        [Min(0)] public float weight = 1;
        [Tooltip("The kinds of room it may become.")] public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage;
        [Min(1), Tooltip("First floor of this biome it can appear on.")] public int fromFloor = 1;
        [Min(0), Tooltip("Most copies of this room on one floor (0: no limit).")] public int maxPerFloor = 2;
    }
    [TabGroup(RoomsTab), Tooltip("Hand-built rooms (chunks) this biome's floors are made of: each room of a suitable kind is built from one of these by chance (Template Chance), the rest are generated. A room profile's own template comes first.")]
    public RoomTemplateChoice[] roomTemplates = new RoomTemplateChoice[0];
    [TabGroup(RoomsTab), Range(0f, 1f), Tooltip("Share of the rooms that get a hand-built room from the list above (when one suits them).")]
    public float templateChance = .7f;
    [TabGroup(RoomsTab), Tooltip("Use the room features below on this biome's floors instead of the level's (TableLevelData > Rooms > Room Features).")]
    public bool overrideDungeonSettings;
    [TabGroup(RoomsTab), ShowIf(nameof(overrideDungeonSettings)), InlineProperty, HideLabel]
    public DungeonRoomFeatures roomFeatures = new();
    // The room features of this biome's floors: its own when it overrides the level's.
    public DungeonRoomFeatures RoomFeatures(TableLevelData level) => overrideDungeonSettings || level == null ? roomFeatures : level.roomFeatures;
    [TabGroup(Look), Min(0f), Tooltip("Metres of full ambient light around the player here (LightingManager's light radius); 0 uses the manager's.")]
    public float lightRadius;
    [TabGroup(Look), Range(0f, 1f), Tooltip("How much of the filmic grade (ScreenManager > Filmic look: ACES tonemapping, contrast, saturation, warm white balance) this biome's floors get. 1 as in the room; 0 plain, truer colours.")]
    public float filmicLook = 1f;
    [TabGroup(Look), Tooltip("Colour split (ScreenManager): shadows and ambient lean to this (cool).")]
    public Color shadowTint = new(.55f, .85f, 1f);
    [TabGroup(Look), Tooltip("Colour split (ScreenManager): lamps, torches and the player's light lean to this (warm).")]
    public Color lampTint = new(1.06f, .97f, .86f);
    bool CustomLighting => lighting == Lighting.Custom;
    [TabGroup(Look), ShowIf(nameof(CustomLighting)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [TabGroup(Look), Tooltip("Wall torches and other wall lights in this biome, weighted, each with an optional colour. Empty uses the level's.")]
    public DungeonWallLight[] lightingPrefabs = new DungeonWallLight[0];
    // The biome-wide fallback: hidden while every torch in the list sets its own colour (it would change nothing).
    [TabGroup(Look), HideIf(nameof(EveryTorchColoured)), LabelText("Default Torch Colour"),
     Tooltip("Colour for torches that nothing more specific colours: list entries without their own Override Color, the level's torches when this list is empty, room template sockets without a colour. A socket, room template, room profile or entry colour wins.")]
    public bool overrideTorchColor;
    [TabGroup(Look), ShowIf("@overrideTorchColor && !EveryTorchColoured"), ColorUsage(false), LabelText("Colour")]
    public Color torchColor = new Color(1f, .6f, .3f);
    bool EveryTorchColoured
    {
        get
        {
            bool any = false;
            if (lightingPrefabs != null) foreach (var l in lightingPrefabs) if (l != null && l.prefab != null) { if (!l.overrideColor) return false; any = true; }
            return any;
        }
    }
    [TabGroup(Look), Range(0f, 3f), Tooltip("Brightness of every torch in this biome. 1 is as the torch prefab is made.")]
    public float torchBrightness = 1f;
    // The colour a torch gets when nothing more specific sets one (null keeps the prefab's own). Off while hidden, so the
    // inspector never hides a colour that still applies (it would only reach socket prefabs then).
    public Color? TorchColor => overrideTorchColor && !EveryTorchColoured ? torchColor : null;

    [Serializable]
    public class Spawn
    {
        [HorizontalGroup, HideLabel, AssetsOnly] public GameObject enemy;
        [HorizontalGroup(70), LabelWidth(44), Min(0)] public float weight = 1;
        [HorizontalGroup(90), LabelText("From floor"), LabelWidth(62), Min(1), Tooltip("Floor of this biome it first appears on (1 = the biome's first floor).")]
        public int fromFloor = 1;
    }
    [TabGroup(Life), Tooltip("What lives here. Weight is how common; From floor lets tougher enemies appear partway through the biome.")]
    public Spawn[] enemies = new Spawn[0];
    [TabGroup(Life), MinMaxSlider(0, 1, true), Tooltip("Share of rooms with enemies: on the biome's first floor (left) ramping to its last floor (right).")]
    public Vector2 encounterChance = new(.4f, .6f);
    [TabGroup(Life), Range(1, 6), Tooltip("Fewest enemies in a room that has any (packs: 2 or more).")] public int minEnemiesPerRoom = 1;
    [TabGroup(Life), Range(1, 6)] public int maxEnemiesPerRoom = 2;
    [TabGroup(Life), Tooltip("Added to the level's features in this biome. Min Floor counts from the biome's first floor (for the level's features too).")]
    public DungeonFeature[] features = new DungeonFeature[0];

    [TabGroup(Loot), Tooltip("Usually a Loot Profile (gear by tier). A hand-listed Loot Table also works.")]
    public LootSource loot;
    [TabGroup(Loot), Range(0, 1), Tooltip("Chance a merchant sets up shop on a floor of this biome.")]
    public float merchantChance = .3f;

    [TabGroup(Boss), Tooltip("Waits in the exit room of the biome's last floor and seals the stairs until killed. Leave the enemy empty for no guardian.")]
    [InlineProperty, HideLabel] public DungeonMilestone guardian = new() { enabled = false };

    [TabGroup(Sound)] public AudioClip ambience;
    [TabGroup(Sound), Range(0, 1)] public float ambienceVolume = .55f;
    [TabGroup(Sound), Tooltip("Occasional distant sounds played around the player (rumbles, drips, creaks).")]
    public AudioClip[] ambientOneShots = new AudioClip[0];
    [TabGroup(Sound), MinMaxSlider(2, 60, true)] public Vector2 oneShotInterval = new Vector2(10, 24);
    [TabGroup(Sound), Range(0, 1)] public float oneShotVolume = .5f;
    [TabGroup(Sound), Tooltip("Replaces the level's music in this biome. Empty keeps the level's.")]
    public AudioClip music;
    [TabGroup(Sound), Range(0, 1)] public float musicVolume = 1f;

    [TabGroup(Sound), Title("Voice"), Range(0, 1), Tooltip("Chance the Dungeon Master says one of the lines below when the run enters this biome (the first biome too).")]
    public float entryLineChance = .6f;
    [TabGroup(Sound), Tooltip("Picked at random. Leave Voice empty to mumble.")]
    public DungeonMaster.Line[] entryLines = new DungeonMaster.Line[0];


    public DungeonMaster.Line PickEntryLine()
    {
        if (entryLines == null || entryLines.Length == 0 || UnityEngine.Random.value >= entryLineChance) return null;
        return entryLines[UnityEngine.Random.Range(0, entryLines.Length)];
    }

    // floorInBiome is 1-based; floors is the biome's floor count in the level.
    public float EncounterChance(int floorInBiome, int floors) =>
        Mathf.Lerp(encounterChance.x, encounterChance.y, floors <= 1 ? 1 : (floorInBiome - 1) / (float)(floors - 1));

    public GameObject ChooseEnemy(System.Random random, int floorInBiome)
    {
        var pick = WeightedPick.Choose(enemies, s => s != null && s.enemy != null && s.fromFloor <= floorInBiome ? s.weight : 0, random, singleTotal: true);
        return pick != null ? pick.enemy : null;
    }
}
