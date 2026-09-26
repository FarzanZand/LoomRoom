using Sirenix.OdinInspector;
using UnityEngine;

// A floor's identity: crypt, catacombs, flooded sewer, mines. Assign one per floor in the
// level's Dungeon Floors list. Empty fields fall back to the floor's own overrides, then to
// the level. Resolution for anything a floor can also set: floor override > theme > level.
[CreateAssetMenu(menuName = "Table/Dungeon Theme", fileName = "Dungeon Theme")]
public class DungeonTheme : ScriptableObject
{
    public string displayName = "The Crypt";
    [TextArea, Tooltip("Posted to the message log on arrival, e.g. \"The air grows damp. Water drips somewhere below.\"")]
    public string entryMessage;

    [Title("Architecture")]
    [Tooltip("Weighted styles per whole room. Empty uses the level's room styles.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [Tooltip("Weighted styles per corridor section. Empty uses the level's corridor styles.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [Tooltip("Decorations for rooms without their own profile props. Empty uses the level props.")]
    public GameObject[] props = new GameObject[0];
    [Tooltip("Breakables for this theme. Empty uses the level's destructibles.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];
    [Tooltip("Fountains, graves, bookshelves... Empty uses the level's features.")]
    public DungeonFeature[] features = new DungeonFeature[0];

    [Title("Lighting")]
    public bool overrideLighting;
    [HideIf(nameof(overrideLighting))] public DungeonMoodLighting moodLighting = DungeonMoodLighting.AmberCrypt;
    [Tooltip("Use Mood Lighting above even when the floor has its own lighting.")]
    [HideIf(nameof(overrideLighting))] public bool useThemeMood;
    [ShowIf(nameof(overrideLighting)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [Tooltip("Tint and strength of each room's chamber light. Alpha 0 keeps the level's colours.")]
    public Color roomLightTint = new Color(1, 1, 1, 0);

    [Title("Enemies and rewards")]
    [Tooltip("Enemy pool. Empty uses the floor's or level's enemies.")]
    public GameObject[] enemies = new GameObject[0];
    public DungeonLootTable enemyLoot, chestLoot, barrelLoot;

    [Title("Audio")]
    public AudioClip ambience;
    [Range(0, 1)] public float ambienceVolume = .55f;
    [Tooltip("Occasional distant sounds played around the player (rumbles, drips, creaks).")]
    public AudioClip[] ambientOneShots = new AudioClip[0];
    [MinMaxSlider(2, 60, true)] public Vector2 oneShotInterval = new Vector2(10, 24);
    [Range(0, 1)] public float oneShotVolume = .5f;
    [Tooltip("Replaces the level's background music on these floors.")]
    public AudioClip music;
    [Range(0, 1)] public float musicVolume = 1f;

    [Title("Merchant")]
    [Range(0, 1), Tooltip("Chance a merchant sets up shop on a floor using this theme. The floor can override.")]
    public float merchantChance = .3f;
}
