using UnityEngine;
using Sirenix.OdinInspector;

public enum TableLevelKind { Town = 0, Dungeon = 1 }
// Keep existing numeric values: level assets serialize these selections as integers.
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6 }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    // Inspector layout only. Field names are unchanged: level assets keep their data.
    const string Tabs = "Tabs";
    const string Look = "Look & Sound", Layout = "Layout", Architecture = "Architecture", Rooms = "Rooms",
                 Enemies = "Enemies", Loot = "Loot & Shop", Floors = "Floors & Bosses", Hud = "HUD";
    const string D = nameof(IsDungeon);
    bool IsDungeon => kind == TableLevelKind.Dungeon;
    bool ShowLightingOverride => IsDungeon && overrideLighting;

    // ── Identity ──────────────────────────────────────────────────────
    [Title("Level")]
    public string displayName;
    [TextArea] public string description;
    public TableLevelKind kind;
    [Tooltip("Town: the authored environment. Dungeon: optional root for the generated floor.")]
    public GameObject environmentPrefab;

    // ── Look & Sound ──────────────────────────────────────────────────
    [TabGroup(Tabs, Look), Tooltip("Town lighting. Dungeons use it as the fallback when a lighting preset is missing.")]
    public SceneMood mood;
    [TabGroup(Tabs, Look), ShowIf(D), HideIf(nameof(overrideLighting)), LabelText("Dungeon Lighting")]
    [UnityEngine.Serialization.FormerlySerializedAs("moodLightning")]
    public DungeonMoodLighting moodLighting = DungeonMoodLighting.AmberCrypt;
    [TabGroup(Tabs, Look), ShowIf(D), Tooltip("Use the settings below instead of the selected lighting style. Floors and themes can still override.")]
    public bool overrideLighting;
    [TabGroup(Tabs, Look), ShowIf(nameof(ShowLightingOverride)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [TabGroup(Tabs, Look), Title("Music", HorizontalLine = false)]
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
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Default materials", "Used where no theme, style or profile overrides them.", HorizontalLine = false)]
    public Material floorMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), LabelText("Bottom Wall Material"), Tooltip("First wall tile, from floor to one architecture tile high. Room styles may override this.")]
    public Material wallMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Wall tiles above the first. Empty uses Bottom Wall Material. Room styles may override this.")]
    public Material upperWallMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D)]
    public Material trimMaterial, ceilingMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Procedural chests and barrels only.")]
    public Material woodMaterial, metalMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Styles", "Floors with a theme use the theme's styles instead.", HorizontalLine = false), Tooltip("Weighted styles selected once per whole room. Empty or disabled entries use the level materials.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Copy a connected room's complete style without crossing a planned doorway. Search through open corridors if needed. If no room is reachable without crossing a door, use level defaults. Selection is seeded; opening doors does not change styles.")]
    public bool corridorsCopyConnectedRoomStyle;
    [TabGroup(Tabs, Architecture), ShowIf(D), HideIf(nameof(corridorsCopyConnectedRoomStyle)), Tooltip("Weighted styles selected once per whole corridor section. Expand Style to edit its materials. Empty uses the level materials.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];

    // ── Rooms ─────────────────────────────────────────────────────────
    [TabGroup(Tabs, Rooms), ShowIf(D), Tooltip("Weighted variations for each room role, with optional enemies, rewards, props, lighting and a hand-built Room Template.")]
    public DungeonRoomProfile[] roomProfiles;
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Props"), Tooltip("Designer-authored decorations. Must fit inside one cell and leave walkways clear. Profiles and themes can replace them.")]
    public GameObject[] roomPropPrefabs;
    [TabGroup(Tabs, Rooms), ShowIf(D), Tooltip("Authored chest prefab with DungeonContainer, lid reference and interaction collider.")]
    public GameObject chestPrefab;
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Breakables", HorizontalLine = false), Tooltip("Barrels, crates, pots and cobwebs. Storage and supply spots use Floor placements; cobwebs hang in ceiling corners. Themes can replace this list.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Extra Per Room"), Tooltip("Extra breakables scattered per room, on top of supply spots.")]
    [MinMaxSlider(0, 8, true)] public Vector2Int destructiblesPerRoom = new Vector2Int(1, 3);
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Features", HorizontalLine = false), Tooltip("Fountains, altars, graves, bookshelves, levers. Each has a chance per eligible room. Themes can replace this list.")]
    public DungeonFeature[] features = new DungeonFeature[0];

    // ── Enemies ───────────────────────────────────────────────────────
    [TabGroup(Tabs, Enemies), ShowIf(D), Range(0, 1), Tooltip("Chance a non-entrance room is a combat room.")]
    public float encounterChance = .7f;
    [TabGroup(Tabs, Enemies), ShowIf(D), Range(1, 6)]
    public int maxEnemiesPerRoom = 2;
    [TabGroup(Tabs, Enemies), ShowIf(D), Tooltip("Enemy pool when no profile, floor or theme provides one. Repeat an entry to weight it.")]
    public GameObject[] enemies;
    [TabGroup(Tabs, Enemies), ShowIf(D), Tooltip("Per-floor enemy scaling.")]
    public DungeonBalance balance;

    // ── Loot & Shop ───────────────────────────────────────────────────
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Reward tables", "Floors, themes and room profiles can override these.", HorizontalLine = false), LabelText("Fallback")]
    public DungeonLootTable loot;
    [TabGroup(Tabs, Loot), ShowIf(D)]
    public DungeonLootTable enemyLoot, chestLoot, barrelLoot;
    [TabGroup(Tabs, Loot), ShowIf(D), Tooltip("Placed in the rest room's supply container.")]
    public ItemData guaranteedHealing;
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Starting kit", "Given on a new run; descending keeps the current loadout.", HorizontalLine = false)]
    public ItemData[] startingItems;
    [TabGroup(Tabs, Loot), ShowIf(D)]
    public ItemData[] startingEquipment;
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Merchant", HorizontalLine = false), LabelText("Prefab"), Tooltip("Merchant prefab with a Merchant component. Placed in a quiet room on floors that roll one.")]
    public GameObject merchantPrefab;
    [TabGroup(Tabs, Loot), ShowIf(D), LabelText("Stock"), Tooltip("Rolled for the merchant's wares (Chest source).")]
    public DungeonLootTable merchantStock;
    [TabGroup(Tabs, Loot), ShowIf(D), Range(0, 1), LabelText("Chance Per Floor"), Tooltip("Used when neither the floor nor its theme sets one.")]
    public float merchantChance = .3f;

    // ── Floors & Bosses ───────────────────────────────────────────────
    [TabGroup(Tabs, Floors), ShowIf(D), LabelText("Multiple Floors")]
    public bool multipleLevels;
    [TabGroup(Tabs, Floors), ShowIf(D), EnableIf(nameof(multipleLevels)), Min(1), LabelText("Floor Count"), Tooltip("Total floors in a run, including the first. Final floor has an exit instead of descending stairs.")]
    public int levelCount = 3;
    [TabGroup(Tabs, Floors), ShowIf(D), Tooltip("Used by floors without their own theme. Empty means plain level settings.")]
    public DungeonTheme defaultTheme;
    [TabGroup(Tabs, Floors), ShowIf(D), EnableIf(nameof(multipleLevels)), LabelText("Floors"), Tooltip("Entry 0 is floor 1, entry 1 is floor 2, and so on. Missing entries inherit the level.")]
    [ListDrawerSettings(ShowFoldout = true, ShowIndexLabels = true)]
    public DungeonFloorSettings[] floorSettings = new DungeonFloorSettings[0];
    [TabGroup(Tabs, Floors), ShowIf(D), Title("Milestones", "One entry per boss floor. The boss waits in a hand-built arena in the exit room; the way down stays sealed until it falls.", HorizontalLine = false)]
    [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = nameof(DungeonMilestone.Label))]
    public DungeonMilestone[] milestones = new DungeonMilestone[0];

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

    public DungeonFloorSettings Floor(int number) => multipleLevels && floorSettings != null && number > 0 && number <= floorSettings.Length ? floorSettings[number - 1] : null;

    public DungeonTheme Theme(int floor)
    {
        var settings = Floor(floor);
        return settings != null && settings.theme != null ? settings.theme : defaultTheme;
    }

    public LightingManager.MoodState DungeonLighting(int floorNumber = 1)
    {
        var theme = Theme(floorNumber);
        if (theme != null && theme.overrideLighting) return theme.lightingSettings.ToState();
        if (theme != null && theme.useThemeMood)
        {
            var themed = Resources.Load<SceneMood>("DungeonLighting/" + theme.moodLighting);
            if (themed != null) return LightingManager.FromPreset(themed);
        }
        var floor = Floor(floorNumber);
        if (floor != null) return floor.Lighting(mood);
        if (overrideLighting) return lightingSettings.ToState();
        var preset = Resources.Load<SceneMood>("DungeonLighting/" + moodLighting);
        if (preset == null) preset = mood;
        return preset != null ? LightingManager.FromPreset(preset) : lightingSettings.ToState();
    }

    public AudioClip MusicFor(int floor, out float volume)
    {
        var theme = kind == TableLevelKind.Dungeon ? Theme(floor) : null;
        if (theme != null && theme.music != null) { volume = theme.musicVolume * backgroundMusicVolume; return theme.music; }
        volume = backgroundMusicVolume;
        return backgroundMusic;
    }

    public DungeonMilestone Milestone(int floor)
    {
        if (milestones == null) return null;
        foreach (var m in milestones) if (m != null && m.enabled && m.floor == floor && m.boss != null) return m;
        return null;
    }

    public float MerchantChance(int floor)
    {
        var settings = Floor(floor);
        if (settings != null && settings.overrideMerchant) return settings.merchantChance;
        var theme = Theme(floor);
        return theme != null ? theme.merchantChance : merchantChance;
    }

#if UNITY_EDITOR
    [TabGroup(Tabs, Floors), ShowIf(D), Button("Fill milestones every N floors"), PropertyTooltip("Adds a milestone on floor N, 2N, 3N... up to the floor count, copying the first milestone's boss, arena and audio. Existing floors are kept.")]
    void FillMilestones([MinValue(2)] int every = 4)
    {
        var template = milestones != null && milestones.Length > 0 ? milestones[0] : null;
        var list = new System.Collections.Generic.List<DungeonMilestone>(milestones ?? new DungeonMilestone[0]);
        int count = multipleLevels ? Mathf.Max(1, levelCount) : 1;
        for (int f = every; f <= count; f += every)
        {
            if (list.Exists(m => m != null && m.floor == f)) continue;
            list.Add(template != null ? template.CopyForFloor(f) : new DungeonMilestone { floor = f });
        }
        list.Sort((a, b) => (a?.floor ?? 0).CompareTo(b?.floor ?? 0));
        UnityEditor.Undo.RecordObject(this, "Fill milestones");
        milestones = list.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    [TabGroup(Tabs, Floors), ShowIf(D), Button("Match floor list to floor count"), PropertyTooltip("Adds or removes Floors entries so there is exactly one per floor. Existing entries keep their settings.")]
    void MatchFloors()
    {
        int count = Mathf.Max(1, levelCount);
        var list = new System.Collections.Generic.List<DungeonFloorSettings>(floorSettings ?? new DungeonFloorSettings[0]);
        while (list.Count < count) list.Add(new DungeonFloorSettings { theme = list.Count > 0 ? list[list.Count - 1].theme : defaultTheme });
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        UnityEditor.Undo.RecordObject(this, "Match floors");
        floorSettings = list.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

[System.Serializable]
public class DungeonFloorSettings
{
    [Tooltip("Crypt, catacombs, sewer, mines... Supplies styles, enemies, loot, ambience and music. Settings below override it.")]
    [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
    public DungeonTheme theme;
    [Tooltip("Use Merchant Chance below instead of the theme's.")]
    public bool overrideMerchant;
    [ShowIf("overrideMerchant"), Range(0,1)] public float merchantChance=.3f;
    [Header("Encounters and rewards")]
    public bool overrideEncounters;
    [ShowIf("overrideEncounters"), Range(0,1)] public float encounterChance=.7f;
    [ShowIf("overrideEncounters"), Range(1,6)] public int maxEnemiesPerRoom=2;
    [Tooltip("Empty inherits level enemies.")] public GameObject[] enemies;
    [Tooltip("Empty references inherit the level's reward tables.")] public DungeonLootTable enemyLoot, chestLoot, barrelLoot;
    [HideIf("overrideLighting"), UnityEngine.Serialization.FormerlySerializedAs("moodLightning")] public DungeonMoodLighting moodLighting;
    public bool overrideLighting;
    [ShowIf("overrideLighting"), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    public LightingManager.MoodState Lighting(SceneMood fallback)
    {
        if(overrideLighting)return lightingSettings.ToState();
        var preset=Resources.Load<SceneMood>("DungeonLighting/"+moodLighting);
        if(preset==null)preset=fallback;
        return preset==null ? lightingSettings.ToState() : LightingManager.FromPreset(preset);
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
    public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage;
    [Min(1)] public int minFloor = 1;
    [Min(0), Tooltip("Zero means no limit.")] public int maxPerFloor = 2;
}

[System.Serializable]
public class DungeonMilestone
{
    public bool enabled = true;
    [Min(1)] public int floor = 3;
    [AssetsOnly, Tooltip("Boss enemy prefab. Spawned at the arena's Boss socket, or the exit room centre.")]
    public GameObject boss;
    [AssetsOnly, Tooltip("Hand-built arena (DungeonRoomTemplate) placed in the exit room. Empty keeps the generated room.")]
    public GameObject arena;
    [Tooltip("Shown under the boss name on the health bar.")]
    public string title = "Guardian of the Crypt";
    [TextArea] public string introMessage = "The dead stir. Something vast rises from its throne.";
    public AudioClip introSting;
    [Range(0, 1)] public float stingVolume = 1f;
    public AudioClip bossMusic;
    [Range(0, 1)] public float bossMusicVolume = 1f;
    [Tooltip("The stairs stay sealed until the boss dies.")]
    public bool sealExit = true;
    [Tooltip("Boss reward table. Empty uses the boss prefab's own.")]
    public DungeonLootTable bossLoot;
    [Min(0)] public int bonusGold = 60;
    [Min(1), Tooltip("Exit room is enlarged by at least this factor to fit the arena.")]
    public float arenaSizeScale = 1.5f;

    public string Label => $"Floor {floor}: {(boss != null ? boss.name : "(no boss)")}{(enabled ? "" : " (off)")}";

    public DungeonMilestone CopyForFloor(int newFloor)
    {
        var copy = (DungeonMilestone)MemberwiseClone();
        copy.floor = newFloor;
        return copy;
    }
}
