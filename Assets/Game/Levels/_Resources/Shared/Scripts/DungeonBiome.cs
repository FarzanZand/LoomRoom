using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One stretch of a dungeon run: cellars, crypt, caves. A level lists its biomes in order with a
// floor count each (TableLevelData.stages). Everything that makes a biome feel different lives
// here: how it looks and sounds, what lives in it and how densely, what it drops, and the guardian
// waiting on its last floor. Empty look fields fall back to the level's defaults.
[CreateAssetMenu(menuName = "LoomRoom/Dungeon Biome", fileName = "Biome")]
public class DungeonBiome : ScriptableObject
{
    const string Look = "Look", Life = "Population", Loot = "Rewards", Boss = "Guardian", Sound = "Audio";

    [TabGroup(Look)] public string displayName = "The Crypt";
    [TabGroup(Look), TextArea, Tooltip("Posted to the message log when the run enters this biome. Keep it short and plain.")]
    public string entryMessage;

    [TabGroup(Look), Tooltip("Weighted styles per whole room. Empty uses the level's room styles.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Weighted styles per corridor section. Empty uses the level's corridor styles.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Weighted room shapes for grown layouts. Empty uses the level's shapes.")]
    public DungeonShapeChoice[] roomShapes = new DungeonShapeChoice[0];
    [TabGroup(Look), Tooltip("Decorations for rooms without their own profile props. Empty uses the level props.")]
    public GameObject[] props = new GameObject[0];
    [TabGroup(Look), Tooltip("Furnishing rules. Replace the level's rules and props when set.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [TabGroup(Look), Tooltip("Barrels, crates, pots. Empty uses the level's destructibles.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];

    [TabGroup(Look), Title("Lighting")]
    public bool overrideLighting;
    [TabGroup(Look), HideIf(nameof(overrideLighting))] public DungeonMoodLighting moodLighting = DungeonMoodLighting.AmberCrypt;
    [TabGroup(Look), HideIf(nameof(overrideLighting)), Tooltip("Use the Mood Lighting preset above rather than the level's lighting.")]
    public bool useThemeMood;
    [TabGroup(Look), ShowIf(nameof(overrideLighting)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [TabGroup(Look), Tooltip("Tint and strength of each room's chamber light. Alpha 0 keeps the level's colours.")]
    public Color roomLightTint = new Color(1, 1, 1, 0);

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
    [TabGroup(Life), Tooltip("Fountains, graves, altars, bookshelves. Min floor counts from the biome's first floor. Empty uses the level's features.")]
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
    [TabGroup(Sound), Tooltip("Replaces the level's background music in this biome.")]
    public AudioClip music;
    [TabGroup(Sound), Range(0, 1)] public float musicVolume = 1f;

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
