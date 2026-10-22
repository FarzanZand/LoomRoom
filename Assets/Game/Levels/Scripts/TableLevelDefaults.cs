using Sirenix.OdinInspector;
using UnityEngine;

// What every dungeon level shares (they all had the same here): chests and their sounds, the merchant, breakables,
// features, wall lights, room shapes, the room event pieces and the run's HUD pieces. One asset, Levels/Resources/Table
// level defaults, read by every TableLevelData (its properties of the same names); a level's base biome and a stage's
// biome still add their own breakables, features, wall lights and shapes on top.
[CreateAssetMenu(menuName = "Table/Level Defaults", fileName = "Table level defaults")]
public class TableLevelDefaults : ScriptableObject
{
    const string Tabs = "Tabs";

    [TabGroup(Tabs, "Rooms"), Title("Room shapes", "Weighted room shapes for grown layouts. A biome's shapes add to them, a room profile's replace them.", HorizontalLine = false)]
    public DungeonShapeChoice[] roomShapes = TableLevelData.DefaultShapes();
    [TabGroup(Tabs, "Rooms"), Title("Wall lights", "Wall torches and other wall lights, weighted, each with an optional colour: the fallback for biomes whose own list is empty.", HorizontalLine = false)]
    public DungeonWallLight[] lightingPrefabs = new DungeonWallLight[0];
    [TabGroup(Tabs, "Rooms"), Title("Breakables", "Barrels, crates, pots and cobwebs. A biome's own entries are added.", HorizontalLine = false)]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];
    [TabGroup(Tabs, "Rooms"), Title("Features", "Fountains, altars, graves, bookshelves, each with a chance per eligible room. A biome's own entries are added.", HorizontalLine = false)]
    public DungeonFeature[] features = new DungeonFeature[0];

    [TabGroup(Tabs, "Chests & shop"), Tooltip("The chest when a level has no chest tiers: DungeonContainer, lid and interaction collider.")]
    public GameObject chestPrefab;
    [TabGroup(Tabs, "Chests & shop"), LabelText("Barrel Break")] public AudioClip containerBreakAudio;
    [TabGroup(Tabs, "Chests & shop"), LabelText("Chest Open")] public AudioClip chestOpenAudio;
    [TabGroup(Tabs, "Chests & shop"), Title("Merchant", HorizontalLine = false), LabelText("Prefab"), Tooltip("Merchant prefab with a Merchant component, placed on floors that roll one (how often is set per biome).")]
    public GameObject merchantPrefab;
    [TabGroup(Tabs, "Chests & shop"), LabelText("Stock"), Tooltip("Rolled for the merchant's wares (Chest source). Empty uses the biome's loot.")]
    public LootSource merchantStock;

    [TabGroup(Tabs, "Room events"), AssetsOnly, Tooltip("The shrine set in quiet rooms (DungeonShrine).")]
    public GameObject shrinePrefab;
    [TabGroup(Tabs, "Room events"), AssetsOnly, Tooltip("A spike plate (DungeonSpikeTrap), laid around trapped chests.")]
    public GameObject spikeTrapPrefab;
    [TabGroup(Tabs, "Room events"), AssetsOnly, Tooltip("An ambush gate: its first child is the bars, raised to local y 0 and lowered under the floor.")]
    public GameObject ambushGatePrefab;
    [TabGroup(Tabs, "Room events"), AssetsOnly, Tooltip("The puff an ambusher (and the reward chest, and a boss's chest) comes out of.")]
    public GameObject ambushSpawnPuff;
    [TabGroup(Tabs, "Room events")] public AudioClip ambushSlamAudio, ambushLiftAudio, ambushSpawnAudio;
    [TabGroup(Tabs, "Room events"), Tooltip("Now and then from a sleeping enemy.")] public AudioClip sleepAudio;

    [TabGroup(Tabs, "HUD")] public DungeonEnemyBar enemyBarPrefab;
    [TabGroup(Tabs, "HUD")] public TMPro.TMP_Text damageNumberPrefab;
    [TabGroup(Tabs, "HUD")] public TMPro.TMP_Text messagePrefab;

    static TableLevelDefaults shared;
    // The one asset (Resources); null, with a warning once, when it is missing.
    public static TableLevelDefaults Shared
    {
        get
        {
            if (shared == null)
            {
                shared = Resources.Load<TableLevelDefaults>("Table level defaults");
                if (shared == null && !warned) { warned = true; Debug.LogWarning("No Levels/Resources/Table level defaults asset: levels have no chests, merchant, breakables or HUD pieces."); }
            }
            return shared;
        }
    }
    static bool warned;
}
