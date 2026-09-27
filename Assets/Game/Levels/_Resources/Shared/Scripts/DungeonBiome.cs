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
    const string Look = "Look", Life = "Population", Loot = "Rewards", Boss = "Guardian", Sound = "Audio";

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
    [TabGroup(Look), Title("Lighting"), Tooltip("The lighting in this biome: a preset, or Custom to set every value below.")]
    public Lighting lighting = Lighting.Darkness;
    bool CustomLighting => lighting == Lighting.Custom;
    [TabGroup(Look), ShowIf(nameof(CustomLighting)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();

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
        float total = 0;
        foreach (var s in enemies) if (s != null && s.enemy != null && s.weight > 0 && s.fromFloor <= floorInBiome) total += s.weight;
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var s in enemies)
        {
            if (s == null || s.enemy == null || s.weight <= 0 || s.fromFloor > floorInBiome) continue;
            roll -= s.weight;
            if (roll < 0) return s.enemy;
        }
        return null;
    }
}
