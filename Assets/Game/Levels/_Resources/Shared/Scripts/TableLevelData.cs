using UnityEngine;
using Sirenix.OdinInspector;

public enum TableLevelKind { Town, Dungeon }
// Keep existing numeric values: level assets serialize these selections as integers.
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5 }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    public string displayName;
    [TextArea] public string description;
    public TableLevelKind kind;
    public SceneMood mood;
    [Header("Dungeon mood lighting")]
    [ShowIf("IsDungeon"), HideIf("overrideLighting")]
    public DungeonMoodLighting moodLightning = DungeonMoodLighting.AmberCrypt;
    [ShowIf("IsDungeon"), Tooltip("Use the settings below instead of the selected lighting style.")]
    public bool overrideLighting;
    [ShowIf("ShowLightingOverride"), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    bool IsDungeon => kind == TableLevelKind.Dungeon;
    bool ShowLightingOverride => IsDungeon && overrideLighting;

    public LightingManager.MoodState DungeonLighting(int floorNumber=1)
    {
        if(multipleLevels && floorSettings!=null && floorNumber>0 && floorNumber<=floorSettings.Length && floorSettings[floorNumber-1]!=null)
            return floorSettings[floorNumber-1].Lighting(mood);
        if (overrideLighting) return lightingSettings.ToState();
        var preset = Resources.Load<SceneMood>("DungeonLighting/" + moodLightning);
        if (preset == null) preset = mood;
        return preset != null ? new LightingManager.MoodState {
            sky = preset.skyTint, exposure = preset.skyExposure, light = preset.lightColor,
            intensity = preset.lightIntensity, top = preset.ambientSky, horizon = preset.ambientHorizon,
            ground = preset.ambientGround, fog = preset.fogColor
        } : lightingSettings.ToState();
    }
    public AudioClip backgroundMusic;
    [Range(0f, 1f), Tooltip("BGM volume for this level: 0 is silent, 1 is full volume. Still respects the AudioManager Music and Master mixer settings. Reload the level to apply changes.")]
    public float backgroundMusicVolume = 1f;
    public bool loopMusic = true;
    [Min(0)] public float musicFadeSeconds = 1.5f;
    public GameObject environmentPrefab;
    [Header("Dungeon generation (world units)")]
    [Range(24, 48)] public int width = 32;
    [Range(24, 56)] public int depth = 42;
    [Min(1)] public float cellSize = 2f;
    [Range(6, 36)] public int roomCount = 12;
    [Range(0,40), Tooltip("Additional short room connections, as a percentage of room count.")] public float loopPercent=15;
    [Range(0,1)] public float encounterChance=.7f;
    [Range(1,6)] public int maxEnemiesPerRoom=2;
    [Tooltip("Designer-authored decorations. Must fit inside one cell and leave walkways clear.")] public GameObject[] roomPropPrefabs;
    [Tooltip("Weighted variations for each room role, with optional enemies, rewards, props and lighting.")] public DungeonRoomProfile[] roomProfiles;
    [Tooltip("Zero generates a new seed on each entry.")] public int fixedSeed;
    [Header("Architecture tiles")]
    [Min(3), Tooltip("Physical width and height of one square texture tile. 32x32 artwork repeats every 3 units by default. Independent of layout grid spacing so changing art scale does not enlarge the table footprint.")]
    public float architectureTileSize = 3f;
    [Header("Architecture styles")]
    [Tooltip("Weighted styles selected once per whole room. Empty or disabled entries use the level materials.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [Tooltip("Copy a connected room's complete style without crossing a planned doorway. Search through open corridors if needed. If no room is reachable without crossing a door, use level defaults. Selection is seeded; opening doors does not change styles.")]
    public bool corridorsCopyConnectedRoomStyle;
    [HideIf(nameof(corridorsCopyConnectedRoomStyle)), Tooltip("Weighted styles selected once per whole corridor section. Expand Style to edit its materials. Empty uses the level materials.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [Header("Room heights")]
    [Range(0, 100), Tooltip("Percentage of whole rooms two tiles tall. The remainder after both percentages are one tile tall. Totals over 100 are normalized.")]
    public float twoTileRoomPercent = 25;
    [Range(0, 100), Tooltip("Percentage of whole rooms three tiles tall. Selection is seeded; counts round to whole rooms.")]
    public float threeTileRoomPercent;
    [Header("Corridor heights")]
    [Range(0, 100), Tooltip("Target percentage two tiles tall. Requires a direct connection to a room at least two tiles tall. Too few eligible sections reduces the count. Remaining sections are one tile tall.")]
    public float twoTileCorridorPercent = 25;
    [Range(0, 100), Tooltip("Target percentage three tiles tall. Requires a direct connection to a three-tile room. Totals over 100 are normalized.")]
    public float threeTileCorridorPercent;
    [Header("Dungeon openings")]
    [Tooltip("Hide ceilings in a seeded selection of whole rooms and corridor sections to reveal the surrounding room above.")]
    public bool hideRoof;
    [Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections without visible ceilings. 100 hides every ceiling.")]
    public float hideRoofPercent = 100;
    [Tooltip("Hide outward-facing perimeter walls only. Interior walls stay visible; collision remains to keep actors on the table.")]
    public bool hideEdges;
    [Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections whose exposed outer walls are hidden. 100 hides all exposed edges.")]
    public float hideEdgesPercent = 100;
    [Header("Dungeon floors")]
    public bool multipleLevels;
    [ShowIf("multipleLevels"), Min(1), Tooltip("Total floors in a run, including the first. Final floor has an exit instead of descending stairs.")]
    public int levelCount = 3;
    [ShowIf("multipleLevels"), Tooltip("Entry 0 is floor 1, entry 1 is floor 2, and so on. Missing entries inherit the dungeon lighting.")]
    public DungeonFloorSettings[] floorSettings = new DungeonFloorSettings[0];
    [Header("Default architecture materials")]
    public Material floorMaterial;
    [LabelText("Bottom Wall Material"), Tooltip("First wall tile, from floor to one architecture tile high. Room styles may override this.")]
    public Material wallMaterial;
    [Tooltip("Wall tiles above the first. Empty uses Bottom Wall Material. Room styles may override this.")]
    public Material upperWallMaterial;
    public Material trimMaterial, ceilingMaterial, woodMaterial, metalMaterial;
    public GameObject[] enemies;
    [Header("Room doors")]
    [Tooltip("Editable door prefab, one cell wide. Instantiated at selected room entrances.")]
    public GameObject doorPrefab;
    [Tooltip("Authored chest prefab with DungeonContainer, lid reference and interaction collider.")]
    public GameObject chestPrefab;
    [Range(0,100), Tooltip("Chance for each connected corridor passage to have one door. Other entrances stay open; 0 leaves every passage open.")]
    public float doorPercent = 65;
    public DungeonBalance balance;
    public DungeonLootTable loot;
    public DungeonLootTable enemyLoot, chestLoot, barrelLoot;
    public DungeonFloorSettings Floor(int number)=>multipleLevels && floorSettings!=null && number>0 && number<=floorSettings.Length ? floorSettings[number-1]:null;
    public ItemData[] startingItems;
    public ItemData[] startingEquipment;
    public ItemData guaranteedHealing;
    public AudioClip containerBreakAudio, chestOpenAudio;
    [Header("Dungeon HUD")]
    public TMPro.TMP_FontAsset hudFont;
    public Sprite hotbarFrame;
    public DungeonEnemyBar enemyBarPrefab;
    public TMPro.TMP_Text damageNumberPrefab;
    public TMPro.TMP_Text messagePrefab;
}

[System.Serializable]
public class DungeonFloorSettings
{
    [Header("Encounters and rewards")]
    public bool overrideEncounters;
    [ShowIf("overrideEncounters"), Range(0,1)] public float encounterChance=.7f;
    [ShowIf("overrideEncounters"), Range(1,6)] public int maxEnemiesPerRoom=2;
    [Tooltip("Empty inherits level enemies.")] public GameObject[] enemies;
    [Tooltip("Empty references inherit the level's reward tables.")] public DungeonLootTable enemyLoot, chestLoot, barrelLoot;
    [HideIf("overrideLighting")] public DungeonMoodLighting moodLightning;
    public bool overrideLighting;
    [ShowIf("overrideLighting"), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    public LightingManager.MoodState Lighting(SceneMood fallback)
    {
        if(overrideLighting)return lightingSettings.ToState();
        var preset=Resources.Load<SceneMood>("DungeonLighting/"+moodLightning);
        if(preset==null)preset=fallback;
        return preset==null ? lightingSettings.ToState() : new LightingManager.MoodState {
            sky=preset.skyTint,exposure=preset.skyExposure,light=preset.lightColor,intensity=preset.lightIntensity,
            top=preset.ambientSky,horizon=preset.ambientHorizon,ground=preset.ambientGround,fog=preset.fogColor
        };
    }
}

[System.Serializable]
public class DungeonLightingSettings
{
    public Color skyTint = new Color(.4f, .5f, .62f);
    [Min(0)] public float skyExposure = .8f;
    public Color lightColor = new Color(.78f, .85f, 1f);
    [Min(0)] public float lightIntensity = .65f;
    [ColorUsage(false, true)] public Color ambientSky = new Color(.85f, .88f, .94f);
    [ColorUsage(false, true)] public Color ambientHorizon = new Color(.74f, .76f, .79f);
    [ColorUsage(false, true)] public Color ambientGround = new Color(.5f, .52f, .56f);
    [Tooltip("Used when fog is enabled by the scene.")]
    public Color fogColor = new Color(.28f, .34f, .42f);
    public LightingManager.MoodState ToState() => new LightingManager.MoodState {
        sky = skyTint, exposure = skyExposure, light = lightColor, intensity = lightIntensity,
        top = ambientSky, horizon = ambientHorizon, ground = ambientGround, fog = fogColor
    };
}
