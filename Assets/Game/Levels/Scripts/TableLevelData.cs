using UnityEngine;
using Sirenix.OdinInspector;

// All serialized by integer: append only.
// Boardgame: a dungeon laid out as board-game tiles on the table, no walls, rooms revealed as their doors open.
public enum TableLevelKind { Town = 0, Dungeon = 1, Boardgame = 2 }
// How the room looks while a table level is played (seen from the dungeon through its skylights).
public enum RoomLighting { Normal = 0, Dim = 1, Dark = 2 }
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8 }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    // Inspector layout only. Field names are unchanged: level assets keep their data.
    const string Tabs = "Tabs";
    const string Run = "Run", Look = "Look & Sound", Layout = "Layout", Architecture = "Architecture", Rooms = "Rooms",
                 Loot = "Loot & Shop", Hud = "HUD", Dialogue = "Dialogue";
    const string D = nameof(IsDungeon);
    // Dungeons and board-game dungeons: everything a run needs.
    public bool IsDungeon => kind != TableLevelKind.Town;
    public bool IsBoard => kind == TableLevelKind.Boardgame;
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
    [ListDrawerSettings(ShowFoldout = true)]
    public BiomeStage[] stages = new BiomeStage[0];
    [TabGroup(Tabs, Run), ShowIf(D), ShowInInspector, ReadOnly, LabelText("Floors in a run")]
    public int FloorCount
    {
        get
        {
            int total = 0;
            if (IsDungeon && stages != null) foreach (var s in stages) if (s != null && s.biome != null) total += Mathf.Max(1, s.floors);
            return Mathf.Max(1, total);
        }
    }
    public bool MultipleFloors => FloorCount > 1;
    [TabGroup(Tabs, Run), ShowIf(D), Tooltip("Per-floor enemy scaling across the whole run.")]
    public DungeonBalance balance;

    void OnValidate()
    {
        if (!IsDungeon) return;
        // An authored plan sets the grid size.
        if (IsAuthored && !string.IsNullOrWhiteSpace(authoredMap))
        {
            var rows = AuthoredRows(authoredMap.TrimEnd());
            depth = rows.Length; width = 0;
            foreach (var r in rows) width = Mathf.Max(width, r.Length);
        }
    }

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
    [TabGroup(Tabs, Look), ShowIf(nameof(IsDungeon)), Tooltip("The room while this level is played. Normal: as it was when you sat down, visible from the dungeon. Dim: its lights lowered. Dark: black, with only the lamp over the table lit (the intro's look).")]
    public RoomLighting roomLighting;
    [TabGroup(Tabs, Look), ShowIf(nameof(IsDungeon)), Tooltip("The colour the room's light takes while this level is played: dark yellow, cold blue ... White leaves it as it is.")]
    [ColorUsage(false)] public Color roomTint = Color.white;
    [TabGroup(Tabs, Look), ShowIf(nameof(IsBoard)), Tooltip("The light hung over a board level, like a lamp over a gaming table: it lights every room.")]
    [ColorUsage(false)] public Color boardLightColor = new Color(1f, .86f, .68f);
    [TabGroup(Tabs, Look), ShowIf(nameof(IsBoard)), Min(0)] public float boardLightIntensity = 2600f;
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
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Tooltip("The first floor's starting room opens toward the Dungeon Master, so the player starts facing him across the table (the IntroDungeon). Other layouts are skipped; with a Fixed Seed that already does this, nothing is skipped.")]
    public bool startFacingDungeonMaster;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Grid (cells)", HorizontalLine = false), Range(24, 48), DisableIf(nameof(IsAuthored)), Tooltip("An authored floor plan sets the width and depth itself.")]
    public int width = 32;
    [TabGroup(Tabs, Layout), ShowIf(D), Range(24, 56), DisableIf(nameof(IsAuthored))]
    public int depth = 42;
    [TabGroup(Tabs, Layout), ShowIf(D), Min(1), Tooltip("World units per grid cell.")]
    public float cellSize = 2f;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Range(6, 36)]
    public int roomCount = 12;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Range(0, 30), Tooltip("Whole rooms selected for extra floor space. Expansion respects other rooms and table edges.")]
    public float largeRoomPercent = 10;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Range(1, 3), Tooltip("Width/depth multiplier for the occasional larger room, multiplied by its profile size scale.")]
    public float largeRoomScale = 1.6f;
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Range(0, 40), Tooltip("Additional short room connections, as a percentage of room count.")]
    public float loopPercent = 15;
    [TabGroup(Tabs, Layout), ShowIf(D), Title("Layout style", HorizontalLine = false), Tooltip("Partition: the original, one rectangular room per slice of the grid. Grown: shaped rooms packed outwards from the entrance with winding tunnels, dead ends and loops. Authored: the floor plan painted below, the same every time (the IntroDungeon).")]
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
    [TabGroup(Tabs, Layout), ShowIf(nameof(UsesStartingRoom)), AssetsOnly, Tooltip("Room template prefab the player starts in on the first floor and on the first floor of each new biome. Give its footprint one door cell: the only way out, which the player faces from the centre. Empty uses an ordinary entrance room.")]
    public GameObject startingRoom;

    // ── Authored layout ───────────────────────────────────────────────
    [System.Serializable]
    public class AuthoredRoom
    {
        [HorizontalGroup(.3f), HideLabel] public string label;
        [HorizontalGroup, LabelWidth(36)] public DungeonGenerator.RoomRole role = DungeonGenerator.RoomRole.Rest;
        [HorizontalGroup, LabelWidth(60), AssetsOnly, Tooltip("A room template prefab for this room's interior (room 0 falls back to Starting Room).")] public GameObject template;
        [HorizontalGroup(70), LabelWidth(40), Range(0, 3), Tooltip("Quarter turns of the template.")] public int turns;
        [HorizontalGroup(80), LabelWidth(44), Min(0), Tooltip("Height in tiles; 0 keeps the generated height.")] public int height;
        [HorizontalGroup(70), LabelWidth(36), Range(0, 3), Tooltip("Board-game levels: how many layers the room is raised; stairs lead up to it.")] public int raised;
    }
    [System.Serializable]
    public class AuthoredMarker
    {
        [HorizontalGroup(40), HideLabel, Tooltip("The character used in the Content map.")] public string key = "a";
        [HorizontalGroup, LabelWidth(44), AssetsOnly, Tooltip("Spawned on the cell: an enemy, a breakable, a prop.")] public GameObject prefab;
        [HorizontalGroup, LabelWidth(30), AssetsOnly, Tooltip("Or an item lying on the floor.")] public ItemData item;
        [HorizontalGroup(70), LabelWidth(36), Min(1)] public int count = 1;
        [HorizontalGroup, LabelWidth(30), Tooltip("What a breakable drops.")] public LootSource loot;
    }

    [TabGroup(Tabs, Layout), ShowIf(nameof(IsAuthored)), TextArea(8, 40), Title("Floor plan", "North at the top. 0-9 then A-Z: rooms (0 is the entrance, the highest is the exit with the stairs). . passage, + passage with a door. Anything else is rock. Width and depth follow the plan.", HorizontalLine = false)]
    public string authoredMap = "";
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsAuthored)), TextArea(8, 40), Title("Content", "Same size as the floor plan; each marker key below puts its prefab or item on that cell. Anything else is ignored.", HorizontalLine = false)]
    public string authoredContent = "";
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsAuthored)), ListDrawerSettings(ShowIndexLabels = true), Tooltip("Per room, by number: its role, an optional template and height. Missing rooms: room 0 is the entrance, the last the exit, the rest quiet rooms.")]
    public AuthoredRoom[] authoredRooms = new AuthoredRoom[0];
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsAuthored))]
    public AuthoredMarker[] authoredMarkers = new AuthoredMarker[0];

    bool IsAuthored => IsDungeon && layoutMode == DungeonLayoutMode.Authored;
    bool IsGenerated => IsDungeon && layoutMode != DungeonLayoutMode.Authored;
    public string[] AuthoredRows(string map) => (map ?? "").Replace("\r", "").Split('\n');

    bool IsGrown => IsDungeon && layoutMode == DungeonLayoutMode.Grown;
    bool UsesStartingRoom => IsDungeon && layoutMode != DungeonLayoutMode.Partition;
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
    [TabGroup(Tabs, Layout), ShowIf(nameof(IsGenerated)), Range(0, 100), Tooltip("Chance for each connected corridor passage to have one door. Other entrances stay open; 0 leaves every passage open.")]
    public float doorPercent = 65;
    // ── Board game ────────────────────────────────────────────────────
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Title("Board", "Board-game levels: the tiles' printed tops use the floor materials; these make the rest.", HorizontalLine = false), Tooltip("The cut edge of a ground-level tile: layers of card.")]
    public Material boardEdgeMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Tooltip("Printed grid over each tile (transparent).")]
    public Material boardGridMaterial;
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Range(.05f, .4f), Tooltip("Thickness of a tile (world units).")]
    public float boardTileThickness = .14f;
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Range(.1f, 1f), Tooltip("Height of one layer a room can be raised by (world units).")]
    public float boardLayerHeight = .32f;
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Range(0, 3), Tooltip("Generated board levels: the most layers a room is raised.")]
    public int boardMaxRaise = 2;
    [TabGroup(Tabs, Architecture), ShowIf(nameof(IsBoard)), Tooltip("Soft taps played as tiles land when a room is revealed (Tools/board_tap.py makes them).")]
    public AudioClip[] boardTileTaps = new AudioClip[0];

    // ── Architecture ──────────────────────────────────────────────────
    [TabGroup(Tabs, Architecture), ShowIf(D), Min(3), Tooltip("Physical width and height of one square texture tile. 32x32 artwork repeats every 3 units by default. Independent of layout grid spacing so changing art scale does not enlarge the table footprint.")]
    public float architectureTileSize = 3f;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Room heights", HorizontalLine = false), Range(0, 100), LabelText("Two Tiles %"), Tooltip("Percentage of whole rooms two tiles tall. The remainder after both percentages are one tile tall. Totals over 100 are normalized.")]
    public float twoTileRoomPercent = 25;
    [TabGroup(Tabs, Architecture), ShowIf(D), Range(0, 100), LabelText("Three Tiles %"), Tooltip("Percentage of whole rooms three tiles tall. Selection is seeded; counts round to whole rooms.")]
    public float threeTileRoomPercent;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Corridor heights", HorizontalLine = false), Range(0, 100), LabelText("Two Tiles %"), Tooltip("Target percentage two tiles tall. Requires a direct connection to a room at least two tiles tall. Too few eligible sections reduces the count. Remaining sections are one tile tall.")]
    public float twoTileCorridorPercent = 25;
    [TabGroup(Tabs, Architecture), ShowIf(D), Range(0, 100), LabelText("Three Tiles %"), Tooltip("Target percentage three tiles tall. Requires a direct connection to a three-tile room. Totals over 100 are normalized.")]
    public float threeTileCorridorPercent;
    [TabGroup(Tabs, Architecture), ShowIf(D), Title("Openings (room player view)", HorizontalLine = false), Tooltip("Open a skylight in the ceiling of a seeded selection of whole rooms and corridor sections, so the room above shows through. A frame of ceiling stays round its edge.")]
    public bool hideRoof;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideRoof)), Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections with a skylight. 100 opens every ceiling.")]
    public float hideRoofPercent = 100;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideRoof)), Range(.1f, 3f), Tooltip("Width of the ceiling frame left round a skylight, in world units. Room templates with Open Ceiling use their own.")]
    public float skylightFrame = .9f;
    [TabGroup(Tabs, Architecture), ShowIf(D), Tooltip("Open a window in the outward-facing perimeter walls of a seeded selection. Interior walls stay solid; collision remains to keep actors on the table.")]
    public bool hideEdges;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideEdges)), Range(0, 100), Tooltip("Percentage of whole rooms and connected corridor sections whose outer walls have windows. 100 opens every exposed edge.")]
    public float hideEdgesPercent = 100;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideEdges)), Range(0f, 2f), Tooltip("Wall kept below a window, in world units.")]
    public float windowSill = .55f;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideEdges)), Range(.2f, 2f), Tooltip("Wall kept above a window, in world units.")]
    public float windowLintel = .75f;
    [TabGroup(Tabs, Architecture), ShowIf(D), EnableIf(nameof(hideEdges)), Range(.1f, 1f), Tooltip("Wall kept at each end of a run of windows, in world units.")]
    public float windowJamb = .45f;

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
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Torches", "The only lights in the dungeon besides the player's own.", HorizontalLine = false), LabelText("Lighting Prefabs")]
    [Tooltip("Wall torches and other wall lights, weighted, each with an optional colour. The fallback for biomes whose own list is empty.")]
    public DungeonWallLight[] lightingPrefabs = new DungeonWallLight[0];
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
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Container sounds", HorizontalLine = false), LabelText("Barrel Break")]
    public AudioClip containerBreakAudio;
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Chest Open")]
    public AudioClip chestOpenAudio;
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Breakables", HorizontalLine = false), Tooltip("Barrels, crates, pots and cobwebs. Storage and supply spots use Floor placements; cobwebs hang in ceiling corners. Shared by every biome; a biome's own entries are added.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];
    [TabGroup(Tabs, Rooms), ShowIf(D), LabelText("Extra Per Room"), Tooltip("Extra breakables scattered per room, on top of supply spots.")]
    [MinMaxSlider(0, 8, true)] public Vector2Int destructiblesPerRoom = new Vector2Int(1, 3);
    [TabGroup(Tabs, Rooms), ShowIf(D), Title("Features", HorizontalLine = false), Tooltip("Fountains, altars, graves, bookshelves, levers. Each has a chance per eligible room. Shared by every biome; a biome's own entries are added.")]
    public DungeonFeature[] features = new DungeonFeature[0];

    // ── Loot & Shop ───────────────────────────────────────────────────
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Starting kit", "Given on a new run; descending keeps the current loadout.", HorizontalLine = false)]
    public ItemData[] startingItems;
    [TabGroup(Tabs, Loot), ShowIf(D)]
    public ItemData[] startingEquipment;
    [TabGroup(Tabs, Loot), ShowIf(D), Title("Merchant", HorizontalLine = false), LabelText("Prefab"), Tooltip("Merchant prefab with a Merchant component. Placed in a quiet room on floors that roll one.")]
    public GameObject merchantPrefab;
    [TabGroup(Tabs, Loot), ShowIf(D), LabelText("Stock"), Tooltip("Rolled for the merchant's wares (Chest source). Empty uses the biome's loot. How often a merchant appears is set per biome.")]
    public LootSource merchantStock;
    [TabGroup(Tabs, Loot), ShowIf(D), LabelText("Shop Room"), Tooltip("Room template the merchant's room is built from (a Merchant socket marks where he stands). Empty puts him in a quiet side room as it is.")]
    public GameObject shopRoom;

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

    // ── Dialogue ──────────────────────────────────────────────────────
    [TabGroup(Tabs, Dialogue), ShowIf(D), ListDrawerSettings(ShowFoldout = true), Tooltip("What the Dungeon Master says during a run here: a trigger, a chance, and lines to pick from. Never two within the remark gap set on the Dialogue Manager.")]
    public DungeonDialogue[] dialogue = new DungeonDialogue[0];

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
    // Every dungeon floor is marked for the Lighting Manager's dungeon darkness unless its biome is Lit Throughout.
    public LightingManager.MoodState DungeonLighting(int floorNumber = 1)
    {
        var state = DungeonLightingPreset(floorNumber);
        var biome = Biome(floorNumber);
        state.darkness = biome != null && biome.litThroughout ? 0f : 1f;
        state.roomDark = RoomDarkness;
        state.roomShade = new Color(roomTint.r, roomTint.g, roomTint.b, 1f);
        return state;
    }

    float RoomDarkness => roomLighting == RoomLighting.Dark ? 1f : roomLighting == RoomLighting.Dim ? .6f : 0f;

    // A board lies under the room's own light: the room as it is now, with this level's Room Lighting and tint.
    public LightingManager.MoodState BoardLighting(LightingManager lighting) => lighting.RoomLook(roomLighting, roomTint);

    LightingManager.MoodState DungeonLightingPreset(int floorNumber)
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
        var biome = IsDungeon ? Biome(floor) : null;
        if (biome != null && biome.music != null) { volume = biome.musicVolume * backgroundMusicVolume; return biome.music; }
        volume = backgroundMusicVolume;
        return backgroundMusic;
    }

    // The biome's guardian, on the biome's last floor.
    public DungeonMilestone Milestone(int floor)
    {
        if (floor > FloorCount) return null;
        var biome = BiomeAt(floor, out int floorInBiome, out int biomeFloors);
        var guardian = biome != null ? biome.guardian : null;
        return guardian != null && guardian.enabled && guardian.boss != null && floorInBiome == biomeFloors ? guardian : null;
    }

    public float MerchantChance(int floor) => Biome(floor)?.merchantChance ?? 0;
}
