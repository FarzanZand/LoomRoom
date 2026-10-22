using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

// All serialized by integer: append only.
// Boardgame: a dungeon laid out as board-game tiles on the table, no walls, rooms revealed as their doors open.
public enum TableLevelKind { Town = 0, Dungeon = 1, Boardgame = 2 }
// How the room looks while a table level is played (seen from the dungeon through its skylights).
public enum RoomLighting { Normal = 0, Dim = 1, Dark = 2, Night = 3 }
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8 }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    // Inspector layout only. Field names are unchanged: level assets keep their data.
    const string Tabs = "Tabs";
    const string Run = "Run", Map = "Map", Look = "Look & Sound", Building = "Building", Contents = "Contents", Dialogue = "Dialogue";
    const string D = nameof(IsDungeon);
    // Dungeons and board-game dungeons: everything a run needs.
    public bool IsDungeon => kind != TableLevelKind.Town;
    public bool IsBoard => kind == TableLevelKind.Boardgame;
    // With biomes, each biome sets its own lighting and torches; the level's lighting is only for biome-less dungeons.
    // Board levels lie under the room's own light instead (Room Lighting and Room Tint).
    bool HasBiomes => IsDungeon && stages != null && System.Array.Exists(stages, s => s != null && s.biome != null);
    bool ShowLevelLighting => IsDungeon && !IsBoard && !HasBiomes;

    // ── Level ─────────────────────────────────────────────────────────
    [Title("Level")]
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Picture on the adventure card at the table (a shot of the level).")]
    [PreviewField(80)] public Sprite cardArt;
    public TableLevelKind kind;

    // ── Run ───────────────────────────────────────────────────────────
    [System.Serializable]
    public class BiomeStage
    {
        [HideLabel, AssetsOnly] public DungeonBiome biome;
        [HorizontalGroup("Row", LabelWidth = 45), LabelText("Floors"), Min(1), Tooltip("How many floors of this biome the run has.")]
        public int floors = 3;
        [HorizontalGroup("Row", LabelWidth = 105), LabelText("Boss on last floor"), Tooltip("The biome's guardian (its Boss tab) waits on this stage's last floor. Off: no boss in this stage, whatever the biome has set up.")]
        public bool boss = true;
    }
    [TabGroup(Tabs, Run), ShowIf(D), Title("Biomes", "In order from the top floor down. Each biome lasts its number of floors and its guardian waits on its last floor.", HorizontalLine = false)]
    [ListDrawerSettings(ShowFoldout = true)]
    public BiomeStage[] stages = new BiomeStage[0];
    [TabGroup(Tabs, Run), ShowIf(D), ShowInInspector, ReadOnly, LabelText("The run"), MultiLineProperty(2)]
    public string RunSummary
    {
        get
        {
            if (!IsDungeon || stages == null) return "";
            var parts = new List<string>();
            int start = 1;
            foreach (var stage in stages)
            {
                if (stage == null || stage.biome == null) continue;
                int count = Mathf.Max(1, stage.floors), end = start + count - 1;
                bool fight = stage.boss && stage.biome.guardian != null && stage.biome.guardian.enabled && stage.biome.guardian.boss != null;
                parts.Add((count == 1 ? $"Floor {start}" : $"Floors {start}-{end}") + $" {stage.biome.name}" + (fight ? $" (boss on {end})" : ""));
                start = end + 1;
            }
            return string.Join(", ", parts);
        }
    }
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
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("Per-floor enemy scaling across the whole run.")]
    public DungeonBalance balance;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Title("Gear by floor", "Which gear tiers drop on each floor of the run, whatever the biome (each row lasts until the next row's floor). Biome loot profiles roll their gear from here; a profile with Follow Floor Tiers off (a boss's own reward) keeps its own tiers. Empty: each profile uses its own.", HorizontalLine = false)]
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "Label")]
    public List<LootFloorTiers> gearByFloor = new();
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Title("Start", HorizontalLine = false), AssetsOnly, Tooltip("Room template prefab the player starts in on the first floor and on the first floor of each new biome. Give its footprint one door cell: the only way out, which the player faces from the centre. Empty uses an ordinary entrance room.")]
    public GameObject startingRoom;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), AssetsOnly, Tooltip("The starting room's chest always holds two things: one piece of gear rolled from the floor's loot, and this (a potion). Its chest is never trapped. Empty: an ordinary chest.")]
    public ItemData startingChestConsumable;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("Zero generates a new seed on each entry.")]
    public int fixedSeed;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("A pocket: a room reached through a portal (PortalArch), built beside the floor the player came from. No ladder in and no stairs out; its own portal gate leads back.")]
    public bool pocket;
    [TabGroup(Tabs, Run), ShowIf(D), Tooltip("Posted when the player reaches the next floor ({0} is the floor's number): a tower climbs.")]
    public string nextFloorMessage = "You descend to floor {0}.";

    // The gear row for a floor: the last row starting on or before it (null when the table is empty).
    public LootFloorTiers GearAt(int floor)
    {
        LootFloorTiers best = null;
        if (gearByFloor != null)
            foreach (var row in gearByFloor)
                if (row != null && row.fromFloor <= floor && (best == null || row.fromFloor >= best.fromFloor)) best = row;
        return best;
    }

    void OnValidate()
    {
        if (!IsDungeon) return;
        // An authored plan sets the grid size.
        SyncMapSize();
    }

    // ── Map: the floor plan ───────────────────────────────────────────
    [TabGroup(Tabs, Map), ShowIf(D), Title("Layout style", HorizontalLine = false), Tooltip("Grown: shaped rooms packed outwards from the entrance with winding tunnels, dead ends and loops. Authored: a floor plan made by hand (a map template, or the text plan below), the same every time. Partition is retired and builds as Grown.")]
    public DungeonLayoutMode layoutMode = DungeonLayoutMode.Grown;
    [TabGroup(Tabs, Map), ShowIf(D), Title("Grid (cells)", HorizontalLine = false), Range(24, 48), DisableIf(nameof(IsAuthored)), Tooltip("An authored floor plan sets the width and depth itself.")]
    public int width = 32;
    [TabGroup(Tabs, Map), ShowIf(D), Range(24, 56), DisableIf(nameof(IsAuthored))]
    public int depth = 42;
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGenerated)), Title("Rooms and tunnels", HorizontalLine = false), Range(6, 36)]
    public int roomCount = 12;
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGrown)), MinMaxSlider(3, 16, true), Tooltip("Room width and depth range in cells, before profile scaling. Painted shapes keep their own size.")]
    public Vector2Int roomSize = new Vector2Int(4, 9);
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGrown)), MinMaxSlider(1, 8, true), Tooltip("Rock between a new room and the room it grows from. 1 often gives a doorway straight through the wall.")]
    public Vector2Int roomSpacing = new Vector2Int(1, 4);
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGenerated)), Range(0, 40), Tooltip("Additional short room connections, as a percentage of room count.")]
    public float loopPercent = 15;
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGrown)), Range(2, 24), Tooltip("Furthest gap, in cells, a loop connection may tunnel across.")]
    public int loopReach = 10;
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGrown)), Range(0, 4), Tooltip("How much tunnels wander. 0 digs the shortest route.")]
    public float corridorWander = 1.2f;
    [TabGroup(Tabs, Map), ShowIf(nameof(IsGrown)), Range(0, 100), Tooltip("Chance a tunnel is two cells wide.")]
    public float wideCorridorPercent = 15;
    [TabGroup(Tabs, Map), ShowIf(D), Title("Doors", HorizontalLine = false), LabelText("Door Prefab"), Tooltip("Editable door prefab, one cell wide. Instantiated at selected room entrances.")]
    public GameObject doorPrefab;

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
        [HorizontalGroup(70), LabelWidth(52), Tooltip("The merchant stands in this room, on every floor of this layout.")] public bool merchant;
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

    [TabGroup(Tabs, Map), ShowIf(nameof(IsAuthored)), Title("Authored plan", HorizontalLine = false), AssetsOnly, Tooltip("A map template prefab (several rooms with their passages and doors, each room's interior under its Room child): the floor plan and rooms come from it, and the text plan below is not used.")]
    public DungeonMapTemplate mapTemplate;
    [TabGroup(Tabs, Map), ShowIf(nameof(UsesTextMap)), TextArea(8, 40), Title("Floor plan", "North at the top. 0-9 then A-Z: rooms (0 is the entrance, the highest is the exit with the stairs). . passage, + passage with a door. Anything else is rock. Width and depth follow the plan.", HorizontalLine = false)]
    public string authoredMap = "";
    [TabGroup(Tabs, Map), ShowIf(nameof(UsesTextMap)), ListDrawerSettings(ShowIndexLabels = true), Tooltip("Per room, by number: its role, an optional template and height. Missing rooms: room 0 is the entrance, the last the exit, the rest quiet rooms.")]
    public AuthoredRoom[] authoredRooms = new AuthoredRoom[0];
    [TabGroup(Tabs, Map), ShowIf(nameof(IsAuthored)), TextArea(8, 40), Title("Content", "Same size as the floor plan; each marker key below puts its prefab or item on that cell. Anything else is ignored.", HorizontalLine = false)]
    public string authoredContent = "";
    [TabGroup(Tabs, Map), ShowIf(nameof(IsAuthored))]
    public AuthoredMarker[] authoredMarkers = new AuthoredMarker[0];

    bool IsAuthored => IsDungeon && layoutMode == DungeonLayoutMode.Authored;
    bool UsesTextMap => IsAuthored && mapTemplate == null;
    // The floor plan and rooms the generator builds: the map template's, else the text plan's.
    public string AuthoredMapText => mapTemplate != null ? mapTemplate.MapText() : authoredMap ?? "";
    public AuthoredRoom[] AuthoredRoomList => mapTemplate != null ? mapTemplate.Rooms() : authoredRooms ?? new AuthoredRoom[0];
    // Width and depth follow the floor plan (the map template's, padded to the generator's smallest grid).
    public void SyncMapSize()
    {
        if (!IsAuthored) return;
        var text = AuthoredMapText;
        if (string.IsNullOrWhiteSpace(text)) return;
        var rows = AuthoredRows(text.TrimEnd());
        depth = rows.Length; width = 0;
        foreach (var r in rows) width = Mathf.Max(width, r.Length);
    }
    bool IsGenerated => IsDungeon && layoutMode != DungeonLayoutMode.Authored;
    public string[] AuthoredRows(string map) => (map ?? "").Replace("\r", "").Split('\n');

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

    // ── Look & Sound ──────────────────────────────────────────────────
    [TabGroup(Tabs, Look), HideIf(nameof(IsDungeon)), Tooltip("The town's lighting.")]
    public SceneMood mood;
    [TabGroup(Tabs, Look), ShowIf(nameof(ShowLevelLighting)), Title("Table", HorizontalLine = false), LabelText("Table Lighting")]
    [Tooltip("Sky, sun and ambient colour on every floor (a dungeon without biomes only: each biome sets its own). On dark floors the Lighting Manager holds the ambient down (Dungeon darkness), so the torches give most of the light.")]
    [UnityEngine.Serialization.FormerlySerializedAs("moodLightning")]
    public DungeonMoodLighting moodLighting = DungeonMoodLighting.AmberCrypt;
    [TabGroup(Tabs, Look), ShowIf(nameof(IsDungeon)), InfoBox("Table lighting and torches are set on each biome (its Look tab).", VisibleIf = nameof(HasBiomes)), Title("Room", HorizontalLine = false), Tooltip("The room while this level is played. Normal: as it was when you sat down, visible from the dungeon. Dim: its lights lowered. Dark: black, with only the lamp over the table lit (the intro's look).")]
    public RoomLighting roomLighting;
    [TabGroup(Tabs, Look), ShowIf(nameof(IsBoard)), Title("Board light", "The light hung over a board level, like a lamp over a gaming table: it lights every room.", HorizontalLine = false)]
    [ColorUsage(false)] public Color boardLightColor = new Color(1f, .86f, .68f);
    [TabGroup(Tabs, Look), ShowIf(nameof(IsBoard)), Min(0)] public float boardLightIntensity = 2600f;
    [TabGroup(Tabs, Look), Title("Music", HorizontalLine = false)]
    public AudioClip backgroundMusic;
    [TabGroup(Tabs, Look), Range(0f, 1f), LabelText("Volume"), Tooltip("BGM volume for this level: 0 is silent, 1 is full volume. Still respects the AudioManager Music and Master mixer settings. Reload the level to apply changes.")]
    public float backgroundMusicVolume = 1f;

    // ── Building: styles and the board's pieces ───────────────────────
    [TabGroup(Tabs, Building), ShowIf(D), Title("Default style", HorizontalLine = false), LabelText("Style"), InlineEditor(InlineEditorObjectFieldModes.Foldout)]
    [Tooltip("The fallback. Rooms and corridors no style is picked for use this style; other styles take any material they leave empty from it. Its Wood and Metal make the exit hatch and gate.")]
    public DungeonRoomStyle defaultStyle;
    // The default style's materials (null without one).
    public Material floorMaterial => defaultStyle != null ? defaultStyle.floor : null;
    public Material wallMaterial => defaultStyle != null ? defaultStyle.bottomWall : null;
    public Material upperWallMaterial => defaultStyle != null ? defaultStyle.upperWall : null;
    public Material trimMaterial => defaultStyle != null ? defaultStyle.trim : null;
    public Material ceilingMaterial => defaultStyle != null ? defaultStyle.ceiling : null;
    public Material woodMaterial => defaultStyle != null ? defaultStyle.wood : null;
    public Material metalMaterial => defaultStyle != null ? defaultStyle.metal : null;
    [TabGroup(Tabs, Building), ShowIf(D), Title("Styles", "Shared by every biome. A biome's own styles are added to these.", HorizontalLine = false), Tooltip("Weighted styles selected once per whole room. Empty or disabled entries use the Default Style.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Tabs, Building), ShowIf(D), Tooltip("Copy a connected room's complete style without crossing a planned doorway. Search through open corridors if needed. If no room is reachable without crossing a door, use the Default Style. Selection is seeded; opening doors does not change styles.")]
    public bool corridorsCopyConnectedRoomStyle;
    [TabGroup(Tabs, Building), ShowIf(D), HideIf(nameof(corridorsCopyConnectedRoomStyle)), Tooltip("Weighted styles selected once per whole corridor section. Expand Style to edit its materials. Empty uses the Default Style.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [TabGroup(Tabs, Building), ShowIf(D), Tooltip("Hand-built rooms use the kit's painted models (pillars, arches, rubble, railings) where a piece has one. Off: every kit piece is built of plain blocks in the room's own stone.")]
    public bool kitModels = true;
    [TabGroup(Tabs, Building), Tooltip("While the floor is built on the table, the starting room's wall on the viewer's side and its ceiling stay hidden (from the room they show only their dark outside), and come back when the player arrives. The intro uses it.")]
    public bool hideStartWallInReveal;
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), Title("Board", "Board-game levels: the tiles' printed tops use the floor materials; these make the rest.", HorizontalLine = false), Tooltip("The cut edge of a ground-level tile: layers of card.")]
    public Material boardEdgeMaterial;
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), Tooltip("Printed grid over each tile (transparent).")]
    public Material boardGridMaterial;
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), Tooltip("The bottom of a pit on the board (a template's Pit floor cut): the tiles there are taken away down to the table and this dark card lies in the hole. Empty uses the edge material.")]
    public Material boardPitMaterial;
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), Range(0, 8), Tooltip("How vertical the board is. The tallest room rises to its layer height times this; about half the other rooms keep their level, some dip lower and some climb part of the way. 1 keeps the layers as set, below 1 flattens. Rooms that would need too steep a stair are lifted to fit.")]
    public float boardVerticality = 1f;
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), Tooltip("Soft taps played as tiles land when a room is revealed (Tools/board_tap.py makes them).")]
    public AudioClip[] boardTileTaps = new AudioClip[0];
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), AssetsOnly, Tooltip("Board doors stand between two of these (trees, for a forest) instead of a stone arch with a beam. Empty builds the arch.")]
    public GameObject[] boardDoorPosts = new GameObject[0];
    [TabGroup(Tabs, Building), ShowIf(nameof(IsBoard)), AssetsOnly, Tooltip("Cells a room's shape leaves out inside it (between the columns of a pillar hall, the middle of a ring) become low mounds with one of these on top, instead of holes in the board. Empty leaves the holes.")]
    public GameObject[] boardMoundProps = new GameObject[0];

    // ── Contents: lights, furnishing, chests, room events ─────────────
#if UNITY_EDITOR
    // Under the tabs: what is no longer on a level because every level had the same.
    [ShowIf(D), PropertyOrder(2), Button("Shared by every level: Table level defaults"), PropertyTooltip("The plain chest and its sounds, the merchant and his stock, breakables, features, wall lights, room shapes and the run's HUD pieces are the same on every level: Levels/Resources/Table level defaults.")]
    void SelectSharedDefaults() { if (Shared != null) UnityEditor.Selection.activeObject = Shared; }
#endif
    [TabGroup(Tabs, Contents), ShowIf(D), InfoBox("A biome adds its own on top of these. The plain chest, the merchant, breakables, features and wall lights every level shares are on Table level defaults (the button under the tabs).")]
    [Title("Torches", "The only lights in the dungeon besides the player's own.", HorizontalLine = false), MinMaxSlider(1, 4, true), Tooltip("Torches in a room that rolls its full count (at least one).")]
    public Vector2Int torchesPerRoom = new Vector2Int(1, 2);
    [TabGroup(Tabs, Contents), ShowIf(D), Range(0, 1), LabelText("Full Torch Chance"), Tooltip("Chance a room gets its Torches Per Room; the others get one. The entrance and exit always get the full count. Every room has at least one light (a dark one only Dark Room Light).")]
    public float litRoomChance = .75f;
    [TabGroup(Tabs, Contents), ShowIf(D), Range(0, 1), Tooltip("Chance of a torch on each stretch of corridor wall (about every 8 cells).")]
    public float corridorTorchChance = .3f;
    [TabGroup(Tabs, Contents), ShowIf(D), Tooltip("The one light a dark room gets instead of torches (a candle cluster on the floor by a wall), so no room is without a light; it stays dim. Empty = dark rooms have no light at all.")]
    public GameObject darkRoomLight;
    [TabGroup(Tabs, Contents), ShowIf(D), Title("Furnishing", HorizontalLine = false), LabelText("Furnishing"), Tooltip("Props placed by rule and scaled by floor area: against walls, in corners, in the middle. A biome's rules are added.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [TabGroup(Tabs, Contents), ShowIf(D), Tooltip("Furnish the rim of the entrance and exit rooms too (their middles stay clear). Off keeps both rooms bare.")]
    public bool furnishEntranceRims;
    [TabGroup(Tabs, Contents), ShowIf(D), LabelText("Extra Breakables Per Room"), Tooltip("Extra breakables scattered per room, on top of supply spots.")]
    [MinMaxSlider(0, 8, true)] public Vector2Int destructiblesPerRoom = new Vector2Int(1, 3);
    [TabGroup(Tabs, Contents), ShowIf(D), Title("Chests and the shop", HorizontalLine = false), Tooltip("Chest tiers picked by weight (wood, iron, gold: each prefab's DungeonContainer says what it gives). Empty uses the shared plain chest.")]
    public DungeonWeightedPrefab[] chestTiers = new DungeonWeightedPrefab[0];
    [TabGroup(Tabs, Contents), ShowIf(D), LabelText("Shop Room"), Tooltip("Room template the merchant's room is built from (a Merchant socket marks where he stands). Empty puts him in a quiet side room as it is.")]
    public GameObject shopRoom;
    [TabGroup(Tabs, Contents), ShowIf(D), Title("Room features", "Room events, heights and room light on every floor. A biome can replace them (its Rooms tab: Override Dungeon Settings).", HorizontalLine = false), InlineProperty, HideLabel]
    public DungeonRoomFeatures roomFeatures = new();
    [TabGroup(Tabs, Contents), ShowIf(D), Title("Room event pieces", HorizontalLine = false), AssetsOnly, Tooltip("The shrine set in quiet rooms (DungeonShrine).")]
    public GameObject shrinePrefab;
    [TabGroup(Tabs, Contents), ShowIf(D), AssetsOnly, Tooltip("A spike plate (DungeonSpikeTrap), laid around trapped chests.")]
    public GameObject spikeTrapPrefab;
    [TabGroup(Tabs, Contents), ShowIf(D), AssetsOnly, Tooltip("An ambush gate: its first child is the bars, raised to local y 0 and lowered under the floor.")]
    public GameObject ambushGatePrefab;
    [TabGroup(Tabs, Contents), ShowIf(D), AssetsOnly, Tooltip("The puff an ambusher (and the reward chest) comes out of.")]
    public GameObject ambushSpawnPuff;
    [TabGroup(Tabs, Contents), ShowIf(D)] public AudioClip ambushSlamAudio, ambushLiftAudio, ambushSpawnAudio;
    [TabGroup(Tabs, Contents), ShowIf(D), Tooltip("Now and then from a sleeping enemy.")] public AudioClip sleepAudio;

    // ── Dialogue ──────────────────────────────────────────────────────
    [TabGroup(Tabs, Dialogue), ShowIf(D), ListDrawerSettings(ShowFoldout = true), Tooltip("What the Dungeon Master says during a run here: a trigger, a chance, and lines to pick from. Never two within the remark gap set on the Dialogue Manager.")]
    public DungeonDialogue[] dialogue = new DungeonDialogue[0];

    // ── Shared by every level (Levels/Resources/Table level defaults) ─
    static TableLevelDefaults Shared => TableLevelDefaults.Shared;
    public DungeonShapeChoice[] roomShapes => Shared != null ? Shared.roomShapes : DefaultShapes();
    public DungeonWallLight[] lightingPrefabs => Shared != null ? Shared.lightingPrefabs : new DungeonWallLight[0];
    public DungeonWeightedPrefab[] destructibles => Shared != null ? Shared.destructibles : new DungeonWeightedPrefab[0];
    public DungeonFeature[] features => Shared != null ? Shared.features : new DungeonFeature[0];
    public GameObject chestPrefab => Shared != null ? Shared.chestPrefab : null;
    public AudioClip containerBreakAudio => Shared != null ? Shared.containerBreakAudio : null;
    public AudioClip chestOpenAudio => Shared != null ? Shared.chestOpenAudio : null;
    public GameObject merchantPrefab => Shared != null ? Shared.merchantPrefab : null;
    public LootSource merchantStock => Shared != null ? Shared.merchantStock : null;
    public DungeonEnemyBar enemyBarPrefab => Shared != null ? Shared.enemyBarPrefab : null;
    public TMPro.TMP_Text damageNumberPrefab => Shared != null ? Shared.damageNumberPrefab : null;
    public TMPro.TMP_Text messagePrefab => Shared != null ? Shared.messagePrefab : null;

    // ── The same on every level ───────────────────────────────────────
    // Never changed on any level, so no longer on the asset: the value every level had.
    public float cellSize => 2f;                    // world units per grid cell
    public float architectureTileSize => 3f;        // one square texture tile (32x32 art repeats every 3 units)
    public float largeRoomPercent => 10f;           // whole rooms given extra floor space
    public float largeRoomScale => 1.6f;            // their width and depth multiplier
    public float deadEndPercent => 0f;              // spurs that lead nowhere, a share of the room count
    public float doorPercent => 65f;                // generated passages that get a door
    public float boardTileThickness => .14f;        // a board tile's thickness
    public float boardLayerHeight => .32f;          // one layer a board room can be raised by
    public int boardMaxRaise => 2;                  // the most layers a generated board room is raised
    public bool boardRevealAll => false;            // boards deal rooms in as their doors open
    public float darkRoomLightStrength => .6f;      // a dark room's one light against the prefab's own
    public Color roomTint => Color.white;           // the room's light keeps its colour while a level is played
    public float minimumAmbient => .07f;            // a dungeon is never darker than this
    public bool loopMusic => true;
    public float musicFadeSeconds => 1.5f;
    // Switched off on every level: their code stays, off.
    public bool hideRoof => false;                  // generated skylights (a room template's Open Ceiling still opens its own)
    public float hideRoofPercent => 100f;
    public float skylightFrame => .9f;
    public bool hideEdges => false;                 // windows in outer walls
    public float hideEdgesPercent => 100f;
    public float windowSill => .55f;
    public float windowLintel => .75f;
    public float windowJamb => .45f;
    // Retired: biome room templates and furnishing rules replaced them.
    public DungeonRoomProfile[] roomProfiles => System.Array.Empty<DungeonRoomProfile>();
    public GameObject[] roomPropPrefabs => System.Array.Empty<GameObject>();

    // ── Queries ───────────────────────────────────────────────────────

    public DungeonBiome Biome(int floor) => BiomeAt(floor, out _, out _);

    // The biome a floor belongs to, which of its floors this is (1-based) and how many it has.
    // Floors past the plan stay on the last biome's deepest floor.
    public DungeonBiome BiomeAt(int floor, out int floorInBiome, out int biomeFloors) => StageAt(floor, out floorInBiome, out biomeFloors)?.biome;

    // The stage a floor belongs to (past the end: the last stage, on its last floor).
    BiomeStage StageAt(int floor, out int floorInBiome, out int biomeFloors)
    {
        int start = 1;
        BiomeStage last = null;
        if (stages != null)
            foreach (var stage in stages)
            {
                if (stage == null || stage.biome == null) continue;
                int count = Mathf.Max(1, stage.floors);
                if (floor < start + count) { floorInBiome = Mathf.Max(1, floor - start + 1); biomeFloors = count; return stage; }
                last = stage; start += count;
            }
        biomeFloors = last != null ? Mathf.Max(1, last.floors) : 1;
        floorInBiome = biomeFloors;
        return last;
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
        state.minAmbient = minimumAmbient;
        return state;
    }

    float RoomDarkness => LightingManager.Darkness(roomLighting);

    // A board lies under the room's own light: the room as it is now, with this level's Room Lighting and tint.
    public LightingManager.MoodState BoardLighting(LightingManager lighting) => lighting.RoomLook(roomLighting, roomTint);
    // The light a board floor gets: the room's own light, or its biome's lighting when the biome asks (lightBoard).
    public LightingManager.MoodState BoardLighting(LightingManager lighting, int floorNumber) =>
        Biome(floorNumber) is { lightBoard: true } ? DungeonLighting(floorNumber) : BoardLighting(lighting);

    LightingManager.MoodState DungeonLightingPreset(int floorNumber)
    {
        var biome = Biome(floorNumber);
        if (biome != null && biome.lighting == DungeonBiome.Lighting.Custom) return biome.lightingSettings.ToState();
        if (biome != null)
        {
            var themed = Resources.Load<SceneMood>("DungeonLighting/" + (DungeonMoodLighting)(int)biome.lighting);
            if (themed != null) return LightingManager.FromPreset(themed);
        }
        var preset = Resources.Load<SceneMood>("DungeonLighting/" + moodLighting);
        if (preset == null) preset = mood;
        return preset != null ? LightingManager.FromPreset(preset) : new DungeonLightingSettings().ToState();
    }

    public AudioClip MusicFor(int floor, out float volume)
    {
        var biome = IsDungeon ? Biome(floor) : null;
        if (biome != null && biome.music != null) { volume = biome.musicVolume * backgroundMusicVolume; return biome.music; }
        volume = backgroundMusicVolume;
        return backgroundMusic;
    }

    // The biome's guardian, on the stage's last floor (when the stage has its boss).
    public DungeonMilestone Milestone(int floor)
    {
        if (floor > FloorCount) return null;
        var stage = StageAt(floor, out int floorInBiome, out int biomeFloors);
        var guardian = stage != null && stage.boss && stage.biome != null ? stage.biome.guardian : null;
        return guardian != null && guardian.enabled && guardian.boss != null && floorInBiome == biomeFloors ? guardian : null;
    }

    // A pocket (one room behind a portal) never has a merchant.
    public float MerchantChance(int floor) { var biome = Biome(floor); return pocket || biome == null ? 0 : biome.merchantChance; }
}
