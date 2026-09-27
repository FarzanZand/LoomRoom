using UnityEngine;
using Sirenix.OdinInspector;

public enum TableLevelKind { Town = 0, Dungeon = 1 }
// Keep existing numeric values: level assets serialize these selections as integers.
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8 }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    // Inspector layout only. Field names are unchanged: level assets keep their data.
    const string Tabs = "Tabs";
    const string Run = "Run", Look = "Look & Sound", Layout = "Layout", Architecture = "Architecture", Rooms = "Rooms",
                 Loot = "Loot & Shop", Hud = "HUD";
    const string D = nameof(IsDungeon);
    bool IsDungeon => kind == TableLevelKind.Dungeon;
    bool ShowLightingOverride => ShowLevelLighting && overrideLighting;
    // With biomes, each biome sets its own lighting; the level's is only for biome-less dungeons.
    bool HasBiomes => IsDungeon && stages != null && System.Array.Exists(stages, s => s != null && s.biome != null);
    bool ShowLevelLighting => IsDungeon && !HasBiomes;

    // ── Identity ──────────────────────────────────────────────────────
    [Title("Level")]
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Picture on the adventure card at the table (a shot of the level).")]
    [PreviewField(80)] public Sprite cardArt;
    public TableLevelKind kind;
    [Tooltip("Town: the authored environment. Dungeon: optional root for the generated floor.")]
    public GameObject environmentPrefab;

    // ── Run ───────────────────────────────────────────────────────────
    [System.Serializable]
    public class BiomeStage
    {
        [HorizontalGroup, HideLabel, AssetsOnly] public DungeonBiome biome;
        [HorizontalGroup(90), LabelText("Floors"), LabelWidth(40), Min(1)] public int floors = 3;
    }
    [TabGroup(Tabs, Run), ShowIf(D), Title("Biomes", "In order from the top floor down. Each biome lasts its number of floors and its guardian waits on its last floor.", HorizontalLine = false)]
    [ListDrawerSettings(ShowFoldout = true), OnValueChanged(nameof(CountFloors), true)]
    public BiomeStage[] stages = new BiomeStage[0];
    [TabGroup(Tabs, Run), ShowIf(D), ShowInInspector, ReadOnly, LabelText("Floors in a run")]
    int FloorsInRun => multipleLevels ? Mathf.Max(1, levelCount) : 1;
    [TabGroup(Tabs, Run), ShowIf(D), Tooltip("Per-floor enemy scaling across the whole run.")]
    public DungeonBalance balance;

    // Derived from the stages; kept serialized for the loader, HUD and run records.
    [HideInInspector] public bool multipleLevels;
    [HideInInspector] public int levelCount = 1;

    void CountFloors()
    {
        int total = 0;
        if (stages != null) foreach (var s in stages) if (s != null && s.biome != null) total += Mathf.Max(1, s.floors);
        if (total == 0) return;
        levelCount = total;
        multipleLevels = total > 1;
    }
    void OnValidate() { if (IsDungeon) CountFloors(); }

    // ── Look & Sound ──────────────────────────────────────────────────
    [TabGroup(Tabs, Look), Tooltip("Town lighting. Dungeons use it as the fallback when a lighting preset is missing.")]
    public SceneMood mood;
    [TabGroup(Tabs, Look), ShowIf(nameof(ShowLevelLighting)), HideIf(nameof(overrideLighting)), LabelText("Dungeon Lighting")]
    [UnityEngine.Serialization.FormerlySerializedAs("moodLightning")]
    public DungeonMoodLighting moodLighting = DungeonMoodLighting.AmberCrypt;
    [TabGroup(Tabs, Look), ShowIf(nameof(ShowLevelLighting)), Tooltip("Use the settings below instead of the selected lighting style.")]
    public bool overrideLighting;
    [TabGroup(Tabs, Look), ShowIf(nameof(ShowLightingOverride)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [TabGroup(Tabs, Look), InfoBox("Dungeon lighting is set on each biome (its Look tab).", VisibleIf = nameof(HasBiomes)), Title("Music", HorizontalLine = false)]
    public AudioClip backgroundMusic;
    [TabGroup(Tabs, Look), Range(0f, 1f), LabelText("Volume"), Tooltip("BGM volume for this level: 0 is silent, 1 is full volume. Still respects the AudioManager Music and Master mixer settings. Reload the level to apply changes.")]
    public float backgroundMusicVolume = 1f;
    [TabGroup(Tabs, Look), LabelText("Loop")]
    public bool loopMusic = true;
    [TabGroup(Tabs, Look), Min(0), LabelText("Fade Seconds")]
    public float musicFadeSeconds = 1.5f;

    // ── Layout ────────────────────────────────────────────────────────
    [TabGroup(Tabs, Layout), ShowIf(D), Tooltip("Zero generates a new seed on each entry.")]
    public int fixedSeed;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Grid (cells)", HorizontalLine = false), Range(24, 48)]
    public int width = 32;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(24, 56)]
    public int depth = 42;
    [TabGroup(Tabs, Layout), ShowIf(D), Min(1), Tooltip("World units per grid cell.")]
    public float cellSize = 2f;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(6, 36)]
    public int roomCount = 12;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(0, 30), Tooltip("Whole rooms selected for extra floor space. Expansion respects other rooms and table edges.")]
    public float largeRoomPercent = 10;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(1, 3), Tooltip("Width/depth multiplier for the occasional larger room, multiplied by its profile size scale.")]
    public float largeRoomScale = 1.6f;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(0, 40), Tooltip("Additional short room connections, as a percentage of room count.")]
    public float loopPercent = 15;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Layout style", HorizontalLine = false), Tooltip("Partition: the original, one rectangular room per slice of the grid. Grown: shaped rooms packed outwards from the entrance with winding tunnels, dead ends and loops.")]
    public DungeonLayoutMode layoutMode = DungeonLayoutMode.Grown;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), MinMaxSlider(3, 16, true), Tooltip("Room width and depth range in cells, before profile and large-room scaling. Painted shapes keep their own size.")]
    public Vector2Int roomSize = new Vector2Int(4, 9);
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), MinMaxSlider(1, 8, true), Tooltip("Rock between a new room and the room it grows from. 1 often gives a doorway straight through the wall.")]
    public Vector2Int roomSpacing = new Vector2Int(1, 4);
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), Range(0, 4), Tooltip("How much tunnels wander. 0 digs the shortest route.")]
    public float corridorWander = 1.2f;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), Range(0, 100), Tooltip("Chance a tunnel is two cells wide.")]
    public float wideCorridorPercent = 15;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), Range(0, 100), Tooltip("Spurs that lead nowhere, as a percentage of room count. Some hide a breakable or a chest.")]
    public float deadEndPercent = 0;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), Range(2, 24), Tooltip("Furthest gap, in cells, a loop connection may tunnel across.")]
    public int loopReach = 10;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGrown)), Tooltip("Weighted room shapes. Authored entries use a painted Room Shape asset (Create > Table > Room Shape). Shared by every biome; a biome's shapes add to it, a room profile's replace it.")]
    public DungeonShapeChoice[] roomShapes = DefaultShapes();

    bool IsGrown => IsDungeon && layoutMode == DungeonLayoutMode.Grown;
    public DungeonGrowthSettings Growth => new DungeonGrowthSettings
    {
        spacing = roomSpacing, wander = corridorWander, widePercent = wideCorridorPercent,
        deadEndPercent = deadEndPercent, loopPercent = loopPercent, loopReach = loopReach,
    };
    public static DungeonShapeChoice[] DefaultShapes() => new[]
    {
        new DungeonShapeChoice { kind = DungeonShapeKind.Rectangle, weight = 3 },
        new DungeonShapeChoice { kind = DungeonShapeKind.CutCorners, weight = 2 },
        new DungeonShapeChoice { kind = DungeonShapeKind.LShape, weight = 2 },
        new DungeonShapeChoice { kind = DungeonShapeKind.TShape, weight = 1 },
        new DungeonShapeChoice { kind = DungeonShapeKind.Cross, weight = 1 },
        new DungeonShapeChoice { kind = DungeonShapeKind.PillarHall, weight = 2 },
        new DungeonShapeChoice { kind = DungeonShapeKind.Ring, weight = 1 },
        new DungeonShapeChoice { kind = DungeonShapeKind.RoomInRoom, weight = 1 },
        new DungeonShapeChoice { kind = DungeonShapeKind.Cave, weight = 1 },
    };
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Doors", HorizontalLine = false), LabelText("Door Prefab"), Tooltip("Editable door prefab, one cell wide. Instantiated at selected room entrances.")]
    public GameObject doorPrefab;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(0, 100), Tooltip("Chance for each connected corridor passage to have one door. Other entrances stay open; 0 leaves every passage open.")]
    public float doorPercent = 65;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Room heights", HorizontalLine = false), Range(0, 100), LabelText("Two Tiles %"), Tooltip("Percentage of whole rooms two tiles tall. The remainder after both percentages are one tile tall. Totals over 100 are normalized.")]
    public float twoTileRoomPercent = 25;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(0, 100), LabelText("Three Tiles %"), Tooltip("Percentage of whole rooms three tiles tall. Selection is seeded; counts round to whole rooms.")]
    public float threeTileRoomPercent;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Corridor heights", HorizontalLine = false), Range(0, 100), LabelText("Two Tiles %"), Tooltip("Target percentage two tiles tall. Requires a direct connection to a room at least two tiles tall. Too few eligible sections reduces the count. Remaining sections are one tile tall.")]
    public float twoTileCorridorPercent = 25;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(0, 100), LabelText("Three Tiles %"), Tooltip("Target percentage three tiles tall. Requires a direct connection to a three-tile room. Totals over 100 are normalized.")]
    public float threeTileCorridorPercent;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Openings (room player view)", HorizontalLine = false), Tooltip("Hide ceilings in a seeded selection of whole rooms and corridor sections to reveal the surrounding room above.")]
    public bool hideRoof;
    [TabGroup(Tabs, Layout), ShowIf(D), EnableIf(nameof(hideRoof)), Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections without visible ceilings. 100 hides every ceiling.")]
    public float hideRoofPercent = 100;
    [TabGroup(Tabs, Layout), ShowIf(D), Tooltip("Hide outward-facing perimeter walls only. Interior walls stay visible; collision remains to keep actors on the table.")]
    public bool hideEdges;
    [TabGroup(Tabs, Layout), ShowIf(D), EnableIf(nameof(hideEdges)), Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections whose exposed outer walls are hidden. 100 hides all exposed edges.")]
    public float hideEdgesPercent = 100;

    // ── Architecture ──────────────────────────────────────────────────
    [TabGroup(Tabs, Architecture), ShowIf(D), Min(3), Tooltip("Physical width and height of one square texture tile. 32x32 artwork repeats every 3 units by default. Independent of layout grid spacing so changing art scale does not enlarge the table footprint.")]
    public float architectureTileSize = 3f;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Default materials", "Used where no biome, style or profile overrides them.", HorizontalLine = false)]
    public Material floorMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), LabelText("Bottom Wall Material"), Tooltip("First wall tile, from floor to one architecture tile high. Room styles may override this.")]
    public Material wallMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Wall tiles above the first. Empty uses Bottom Wall Material. Room styles may override this.")]
    public Material upperWallMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D)]
    public Material trimMaterial, ceilingMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Procedural chests and barrels only.")]
    public Material woodMaterial, metalMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Styles", "Shared by every biome. A biome's own styles are added to these.", HorizontalLine = false), Tooltip("Weighted styles selected once per whole room. Empty or disabled entries use the level materials.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Copy a connected room's complete style without crossing a planned doorway. Search through open corridors if needed. If no room is reachable without crossing a door, use level defaults. Selection is seeded; opening doors does not change styles.")]
    public bool corridorsCopyConnectedRoomStyle;
    [TabGroup(Tabs, Architecture), ShowIf(D), HideIf(nameof(corridorsCopyConnectedRoomStyle)), Tooltip("Weighted styles selected once per whole corridor section. Expand Style to edit its materials. Empty uses the level materials.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];

    // ── Rooms ─────────────────────────────────────────────────────────
    [TabGroup(Tabs, Rooms), ShowIf(D), InfoBox("Everything here is shared by every biome. A biome adds its own props, furnishing, breakables and features on top; a room profile replaces them for its rooms."), Tooltip("Weighted variations for each room role, with optional enemies, rewards, props, lighting and a hand-built Room Template.")]
    public DungeonRoomProfile[] roomProfiles;
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Props"), Tooltip("Designer-authored decorations. Must fit inside one cell and leave walkways clear. Shared by every biome; a biome's props add to them, a room profile's replace them.")]
    public GameObject[] roomPropPrefabs;
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Torches", "The only lights in the dungeon besides the player's own.", HorizontalLine = false), LabelText("Wall Torch")]
    public GameObject wallTorch;
    [TabGroup(Tabs, Rooms), ShowIf(D), MinMaxSlider(0, 4, true), Tooltip("Torches in a lit room.")]
    public Vector2Int torchesPerRoom = new Vector2Int(1, 2);
    [TabGroup(Tabs, Rooms), ShowIf(D), Range(0, 1), Tooltip("Chance a room has torches. The entrance and exit always do.")]
    public float litRoomChance = .75f;
    [TabGroup(Tabs, Rooms), ShowIf(D), Range(0, 1), Tooltip("Chance of a torch on each stretch of corridor wall (about every 8 cells).")]
    public float corridorTorchChance = .3f;
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Furnishing"), Tooltip("Props placed by rule and scaled by floor area: against walls, in corners, in the middle. Shared by every biome; a biome's rules are added, a room profile's replace them. Rules win over the plain Props list.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [TabGroup(Tabs, Rooms), ShowIf(D), Tooltip("Authored chest prefab with DungeonContainer, lid reference and interaction collider.")]
    public GameObject chestPrefab;
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Breakables", HorizontalLine = false), Tooltip("Barrels, crates, pots and cobwebs. Storage and supply spots use Floor placements; cobwebs hang in ceiling corners. Shared by every biome; a biome's own entries are added.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Extra Per Room"), Tooltip("Extra breakables scattered per room, on top of supply spots.")]
    [MinMaxSlider(0, 8, true)] public Vector2Int destructiblesPerRoom = new Vector2Int(1, 3);
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Features", HorizontalLine = false), Tooltip("Fountains, altars, graves, bookshelves, levers. Each has a chance per eligible room. Shared by every biome; a biome's own entries are added.")]
    public DungeonFeature[] features = new DungeonFeature[0];

    // ── Loot & Shop ───────────────────────────────────────────────────
    [TabGroup(Tabs, Loot), ShowIf(D), Tooltip("Placed in the rest room's supply container. Everyday loot comes from each biome.")]
    public ItemData guaranteedHealing;
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Starting kit", "Given on a new run; descending keeps the current loadout.", HorizontalLine = false)]
    public ItemData[] startingItems;
    [TabGroup(Tabs, Loot), ShowIf(D)]
    public ItemData[] startingEquipment;
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Merchant", HorizontalLine = false), LabelText("Prefab"), Tooltip("Merchant prefab with a Merchant component. Placed in a quiet room on floors that roll one.")]
    public GameObject merchantPrefab;
    [TabGroup(Tabs, Loot), ShowIf(D), LabelText("Stock"), Tooltip("Rolled for the merchant's wares (Chest source). Empty uses the biome's loot. How often a merchant appears is set per biome.")]
    public LootSource merchantStock;

    // ── HUD ───────────────────────────────────────────────────────────
    [TabGroup(Tabs, Hud), ShowIf(D)]
    public TMPro.TMP_FontAsset hudFont;
    [TabGroup(Tabs, Hud), ShowIf(D)]
    public Sprite hotbarFrame;
    [TabGroup(Tabs, Hud), ShowIf(D)]
    public DungeonEnemyBar enemyBarPrefab;
    [TabGroup(Tabs, Hud), ShowIf(D)]
    public TMPro.TMP_Text damageNumberPrefab;
    [TabGroup(Tabs, Hud), ShowIf(D)]
    public TMPro.TMP_Text messagePrefab;
    [TabGroup(Tabs, Hud), ShowIf(D), Title("Container sounds", HorizontalLine = false), LabelText("Barrel Break")]
    public AudioClip containerBreakAudio;
    [TabGroup(Tabs, Hud), ShowIf(D), LabelText("Chest Open")]
    public AudioClip chestOpenAudio;

    // ── Queries ───────────────────────────────────────────────────────

    public DungeonBiome Biome(int floor) => BiomeAt(floor, out _, out _);

    // The biome a floor belongs to, which of its floors this is (1-based) and how many it has.
    // Floors past the plan stay on the last biome's deepest floor.
    public DungeonBiome BiomeAt(int floor, out int floorInBiome, out int biomeFloors)
    {
        int start = 1;
        BiomeStage last = null;
        if (stages != null)
            foreach (var stage in stages)
            {
                if (stage == null || stage.biome == null) continue;
                int count = Mathf.Max(1, stage.floors);
                if (floor < start + count) { floorInBiome = Mathf.Max(1, floor - start + 1); biomeFloors = count; return stage.biome; }
                last = stage; start += count;
            }
        biomeFloors = last != null ? Mathf.Max(1, last.floors) : 1;
        floorInBiome = biomeFloors;
        return last?.biome;
    }

    // The floor's biome decides; the level's own setting only applies to dungeons without biomes.
    public LightingManager.MoodState DungeonLighting(int floorNumber = 1)
    {
        var biome = Biome(floorNumber);
        if (biome != null && biome.lighting == DungeonBiome.Lighting.Custom) return biome.lightingSettings.ToState();
        if (biome != null)
        {
            var themed = Resources.Load<SceneMood>("DungeonLighting/" + (DungeonMoodLighting)(int)biome.lighting);
            if (themed != null) return LightingManager.FromPreset(themed);
        }
        if (overrideLighting) return lightingSettings.ToState();
        var preset = Resources.Load<SceneMood>("DungeonLighting/" + moodLighting);
        if (preset == null) preset = mood;
        return preset != null ? LightingManager.FromPreset(preset) : lightingSettings.ToState();
    }

    public AudioClip MusicFor(int floor, out float volume)
    {
        var biome = kind == TableLevelKind.Dungeon ? Biome(floor) : null;
        if (biome != null && biome.music != null) { volume = biome.musicVolume * backgroundMusicVolume; return biome.music; }
        volume = backgroundMusicVolume;
        return backgroundMusic;
    }

    // The biome's guardian, on the biome's last floor.
    public DungeonMilestone Milestone(int floor)
    {
        if (floor > FloorsInRun) return null;
        var biome = BiomeAt(floor, out int floorInBiome, out int biomeFloors);
        var guardian = biome != null ? biome.guardian : null;
        return guardian != null && guardian.enabled && guardian.boss != null && floorInBiome == biomeFloors ? guardian : null;
    }

    public float MerchantChance(int floor) => Biome(floor)?.merchantChance ?? 0;
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

[System.Flags]
public enum DungeonRoomRoles { None = 0, Entrance = 1, Combat = 2, Treasure = 4, Rest = 8, Storage = 16, Exit = 32, Any = 63 }

[System.Serializable]
public class DungeonWeightedPrefab
{
    [AssetsOnly] public GameObject prefab;
    [Min(0)] public float weight = 1;
    public static GameObject Choose(DungeonWeightedPrefab[] choices, System.Random random, System.Func<GameObject,bool> filter = null)
    {
        if (choices == null) return null;
        double total = 0;
        foreach (var c in choices) if (c != null && c.prefab != null && c.weight > 0 && (filter == null || filter(c.prefab))) total += c.weight;
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var c in choices)
        {
            if (c == null || c.prefab == null || c.weight <= 0 || (filter != null && !filter(c.prefab))) continue;
            roll -= c.weight;
            if (roll < 0) return c.prefab;
        }
        return null;
    }
}

[System.Serializable]
public class DungeonFeature
{
    [AssetsOnly, Tooltip("Fountain, altar, grave, bookshelf or lever prefab. Must fit in one cell.")]
    public GameObject prefab;
    [Range(0, 1)] public float chancePerRoom = .12f;
    [Tooltip("Against Wall, Corner and Centre work as for Furnishing. Anywhere picks any free cell and faces the room centre.")]
    public DungeonPropPlacement placement = DungeonPropPlacement.Anywhere;
    public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage;
    [Min(1)] public int minFloor = 1;
    [Min(0), Tooltip("Zero means no limit.")] public int maxPerFloor = 2;
}

[System.Serializable]
public class DungeonMilestone
{
    public bool enabled = true;
    [AssetsOnly, Tooltip("Boss enemy prefab. Spawned at the arena's Boss socket, or the exit room centre.")]
    public GameObject boss;
    [Tooltip("Lets an ordinary enemy serve as a guardian: its health and damage are multiplied.")]
    [Min(.1f)] public float healthMultiplier = 1, damageMultiplier = 1;
    [AssetsOnly, Tooltip("Hand-built arena (DungeonRoomTemplate) placed in the exit room. Empty keeps the generated room.")]
    public GameObject arena;
    [Tooltip("Shown under the boss name on the health bar.")]
    public string title = "Guardian";
    [TextArea] public string introMessage = "Something guards the stairs down.";
    public AudioClip introSting;
    [Range(0, 1)] public float stingVolume = 1f;
    public AudioClip bossMusic;
    [Range(0, 1)] public float bossMusicVolume = 1f;
    [Tooltip("The stairs stay sealed until the boss dies.")]
    public bool sealExit = true;
    [Tooltip("Boss reward table. Empty uses the boss prefab's own.")]
    public LootSource bossLoot;
    [Min(0)] public int bonusGold = 60;
    [Min(1), Tooltip("Exit room is enlarged by at least this factor to fit the arena.")]
    public float arenaSizeScale = 1.5f;
}
