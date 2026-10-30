using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

// All serialized by integer: append only.
// Walled or board is set per biome (DungeonBiome.kind); Boardgame is retired here and read as a dungeon whose
// biomes have no kind of their own yet (a level saved before 2026-10-27 is built as a board).
public enum TableLevelKind { Town = 0, Dungeon = 1, Boardgame = 2 }
// How the room looks while a table level is played (seen from the dungeon through its skylights).
public enum RoomLighting { Normal = 0, Dim = 1, Dark = 2, Night = 3, Custom = 4 }
public enum DungeonMoodLighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8 }

// A table level: its run (stages of biomes, or a number of floors on its base biome), its floor plan, and its base biome,
// which is stored inside this asset and holds what every floor shares (styles, the board's pieces, torches, furnishing,
// chests and the shop, room features). The old level fields of those names read through to the base biome.
[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public partial class TableLevelData : ScriptableObject
{
    // Inspector layout only.
    const string Tabs = "Tabs";
    const string Run = "Run", Map = "Map", Light = "Lighting", Base = "Base biome", Sound = "Sound", Dialogue = "Dialogue";
    const string D = nameof(IsDungeon);
    // Dungeons and board-game dungeons: everything a run needs.
    public bool IsDungeon => kind != TableLevelKind.Town;
    public bool IsTown => kind == TableLevelKind.Town;
    // Whether a floor is a board is its biome's Kind (the stage's biome, or the base biome without stages).
    public bool BoardOn(int floor)
    {
        if (!IsDungeon) return false;
        var biome = Biome(floor);
        return biome != null ? biome.IsBoard || kind == TableLevelKind.Boardgame : kind == TableLevelKind.Boardgame;
    }
    // Some floor of the run is a board (the board light and the labels).
    public bool IsBoard
    {
        get
        {
            if (!IsDungeon) return false;
            if (kind == TableLevelKind.Boardgame) return true;
            foreach (var b in RunBiomes()) if (b != null && b.IsBoard) return true;
            return false;
        }
    }
    // A board floor whose biome builds it all at once instead of room by room as its doors open.
    public bool RevealWholeFloor(int floor) { var biome = Biome(floor); return biome != null && biome.boardReveal == BoardRoomReveal.WholeFloor; }
    // A level with stages runs each stage's biome in turn; one without runs Floors floors on its base biome.
    public bool HasStages => IsDungeon && stages != null && System.Array.Exists(stages, s => s != null && s.biome != null);
    bool NoStages => IsDungeon && !HasStages;

    // ── Level ─────────────────────────────────────────────────────────
    [Title("Level")]
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Picture on the adventure card at the table (a shot of the level).")]
    [PreviewField(80)] public Sprite cardArt;
    [ShowIf(nameof(IsTown)), Tooltip("A town. A dungeon's floors are walled or a board game by their biome's Kind (the biome's Look tab).")]
    public TableLevelKind kind = TableLevelKind.Dungeon;

    // ── Run ───────────────────────────────────────────────────────────
    [System.Serializable]
    public class BiomeStage
    {
        [HideLabel, AssetsOnly] public DungeonBiome biome;
        [HorizontalGroup("Row", LabelWidth = 45), LabelText("Floors"), Min(1), Tooltip("How many floors of this biome the run has.")]
        public int floors = 3;
        [HorizontalGroup("Row", LabelWidth = 105), LabelText("Boss on last floor"), Tooltip("The biome's guardian (its Boss tab) waits on this stage's last floor. Off: no boss in this stage, whatever the biome has set up.")]
        public bool boss = true;
        [HorizontalGroup("Row", LabelWidth = 72), LabelText("Extra floors"), Min(0), Tooltip("A bigger jump in difficulty as this stage begins (a new biome after a boss): from its first floor on, enemies grow as if they were this many floors deeper (the balance's damage, health and armour per floor). It adds up with earlier stages' steps. 0: the usual one floor's step.")]
        public float extraFloors;
    }

    // The difficulty steps reached by this floor: every stage's Extra Floors from the stage it starts in down to this one
    // (DungeonBalance.Apply adds them to the floor's depth).
    public float ExtraFloors(int floor)
    {
        if (!HasStages) return 0f;
        float extra = 0f; int start = 1;
        foreach (var stage in stages)
        {
            if (stage == null || stage.biome == null) continue;
            if (floor >= start) extra += Mathf.Max(0f, stage.extraFloors);
            start += Mathf.Max(1, stage.floors);
        }
        return extra;
    }
    [TabGroup(Tabs, Run), ShowIf(D), Title("Biomes", "In order from the top floor down. Each biome lasts its number of floors and its guardian waits on its last floor. Empty: every floor is the base biome (a one-floor level needs no biome of its own).", HorizontalLine = false)]
    [ListDrawerSettings(ShowFoldout = true)]
    public BiomeStage[] stages = new BiomeStage[0];
    [TabGroup(Tabs, Run), ShowIf(nameof(NoStages)), Min(1), Tooltip("How many floors a run has, every one on the base biome.")]
    public int floors = 1;
    [TabGroup(Tabs, Run), ShowIf(nameof(NoStages)), Tooltip("The base biome's guardian (its Guardian tab) waits on the last floor. Off: no boss, whatever the base biome has set up.")]
    public bool bossOnLastFloor = true;
    [TabGroup(Tabs, Run), ShowIf(D), ShowInInspector, ReadOnly, LabelText("The run"), MultiLineProperty(2)]
    public string RunSummary
    {
        get
        {
            if (!IsDungeon) return "";
            var parts = new List<string>();
            int start = 1;
            float step = 0f;
            void Add(DungeonBiome biome, int count, bool boss)
            {
                int end = start + count - 1;
                bool fight = boss && biome.guardian != null && biome.guardian.enabled && biome.guardian.boss != null;
                parts.Add((count == 1 ? $"Floor {start}" : $"Floors {start}-{end}") + $" {biome.name}" + (fight ? $" (boss on {end})" : "") + (step > 0f ? $" (+{step:0.#} floors harder)" : ""));
                start = end + 1;
            }
            if (!HasStages) { if (baseBiome != null) Add(baseBiome, Mathf.Max(1, floors), bossOnLastFloor); }
            else foreach (var stage in stages) if (stage != null && stage.biome != null) { step = stage.extraFloors; Add(stage.biome, Mathf.Max(1, stage.floors), stage.boss); }
            return string.Join(", ", parts);
        }
    }
    [TabGroup(Tabs, Run), ShowIf(D), ShowInInspector, ReadOnly, LabelText("Floors in a run")]
    public int FloorCount
    {
        get
        {
            if (!IsDungeon) return 1;
            if (!HasStages) return Mathf.Max(1, floors);
            int total = 0;
            foreach (var s in stages) if (s != null && s.biome != null) total += Mathf.Max(1, s.floors);
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
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Title("Scripted events", HorizontalLine = false), MinMaxSlider(0, 5, true), LabelText("Event rooms per floor"),
     Tooltip("Fewest and most scripted event rooms on each grown floor: every event the player can walk into (the duel, the bell, the wager, the sleeping giant, the dice, Not a witch, and the Portal hall, whose portal leads to the Skeleton Party). The tavern doesn't count. They come from the floor's biome pool: a floor whose pool has fewer event rooms than the fewest gets all it has (a forest's board pool has none). The room a run opens in counts.")]
    public Vector2Int eventRoomsPerFloor = new(0, 1);
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("The starting room keeps its chest. Off: the chest is taken out (the Arena starts with none).")]
    public bool startingChest = true;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), AssetsOnly, Tooltip("The starting room's chest always holds two things: one piece of gear rolled from the floor's loot, and this (a potion). Its chest is never trapped. Empty: an ordinary chest.")]
    public ItemData startingChestConsumable;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), AssetsOnly, Tooltip("More items the starting room's chest always holds on top of the gear and the potion (a spell tome, say).")]
    public List<ItemData> startingChestItems = new();
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("Zero generates a new seed on each entry.")]
    public int fixedSeed;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("A pocket: a room reached through a portal (PortalArch), built beside the floor the player came from. No ladder in and no stairs out; its own portal gate leads back.")]
    public bool pocket;
    [TabGroup(Tabs, Run), ShowIf(D), PropertyOrder(1), Tooltip("Posted when the player reaches the next floor ({0} is the floor's number): a tower climbs.")]
    public string nextFloorMessage = "You descend to floor {0}.";
    // ── Lighting tab: everything about how this level and the room look while it is played ──
    [TabGroup(Tabs, Light), PropertyOrder(2), Title("The room", "How the bedroom looks while this level is played.", HorizontalLine = false), Tooltip("Normal: as it was when you sat down, seen from the table. Dim: its lights lowered. Night: a little lower. Dark: black, with only the lamp over the table lit (the intro's look). Custom: Room Brightness below.")]
    public RoomLighting roomLighting;
    [TabGroup(Tabs, Light), PropertyOrder(2), ShowIf(nameof(roomLighting), RoomLighting.Custom), Range(0f, 1f), Tooltip("How bright the room's lights stay: 1 as when you sat down, 0 black (below about 0.3 the lamp over the table comes on).")]
    public float roomBrightness = .5f;
    [TabGroup(Tabs, Light), PropertyOrder(2), ColorUsage(false), Tooltip("The room's light takes this colour while the level is played (white: its own colour).")]
    public Color roomTint = Color.white;
    [TabGroup(Tabs, Light), PropertyOrder(2), ShowIf(nameof(IsBoard)), Title("Board light", "The lamp over a board level: it lights every room on the board. (Kept on the base biome; set here.)", HorizontalLine = false), ShowInInspector, ColorUsage(false)]
    Color BoardLightColour { get => B != null ? B.boardLightColor : new Color(1f, .86f, .68f); set { if (B != null) { B.boardLightColor = value; MarkBaseDirty(); } } }
    [TabGroup(Tabs, Light), PropertyOrder(2), ShowIf(nameof(IsBoard)), ShowInInspector, LabelText("Board Light Strength"), MinValue(0)]
    float BoardLightStrength { get => B != null ? B.boardLightIntensity : 2600f; set { if (B != null) { B.boardLightIntensity = Mathf.Max(0f, value); MarkBaseDirty(); } } }
    [TabGroup(Tabs, Light), PropertyOrder(3), InfoBox("A walled dungeon floor is lit by its biome: the Look tab of each stage's biome (and the Base biome tab) holds its colours, darkness and lamps. A board level uses the board light above, unless its biome has Light Board ticked.", InfoMessageType.None), ShowInInspector, HideLabel, ReadOnly]
    string LightingNote => "";
    void MarkBaseDirty()
    {
#if UNITY_EDITOR
        if (B != null) UnityEditor.EditorUtility.SetDirty(B);
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
    [TabGroup(Tabs, Run), HideIf(nameof(IsDungeon)), Tooltip("The town's lighting.")]
    public SceneMood mood;

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
    [TabGroup(Tabs, Map), ShowIf(D), Title("Reveal", HorizontalLine = false), Tooltip("While the floor is built on the table, the starting room's wall on the viewer's side and its ceiling stay hidden (from the room they show only their dark outside), and come back when the player arrives. The intro uses it.")]
    public bool hideStartWallInReveal;

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

    // ── Base biome ────────────────────────────────────────────────────
    [TabGroup(Tabs, Base), InfoBox("What every floor of this level shares: its Level tab (styles, the board's pieces, torches, furnishing, chests and the shop), its room features and its lists (styles, shapes, props, furnishing, breakables, features, wall lights), which a stage's biome adds to. With no stages every floor is this biome: its enemies, loot, look, sound and guardian too. Kept inside this level's asset.")]
    [ShowIf("@IsDungeon && baseBiome != null"), HideLabel, InlineEditor(InlineEditorObjectFieldModes.Hidden)]
    public DungeonBiome baseBiome;
#if UNITY_EDITOR
    [TabGroup(Tabs, Base), ShowIf("@IsDungeon && baseBiome == null"), Button("Make the base biome", ButtonSizes.Large), PropertyTooltip("Makes this level's base biome inside its asset, with the level's own settings.")]
    void MakeBaseBiomeButton() => MakeBaseBiome();

    // Makes the base biome as a sub-asset of this level (named "<level> base"). An existing one is returned, renamed
    // for this level (a copied level brings its original's along).
    public DungeonBiome MakeBaseBiome()
    {
        if (baseBiome != null)
        {
            bool own = UnityEditor.AssetDatabase.GetAssetPath(baseBiome) == UnityEditor.AssetDatabase.GetAssetPath(this);
            if (own && (baseBiome.name != name + " base" || !baseBiome.isLevelBase))
            {
                baseBiome.name = name + " base"; baseBiome.isLevelBase = true;
                UnityEditor.EditorUtility.SetDirty(baseBiome); UnityEditor.EditorUtility.SetDirty(this);
            }
            return baseBiome;
        }
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        var biome = CreateInstance<DungeonBiome>();
        biome.name = name + " base";
        biome.isLevelBase = true;
        if (!string.IsNullOrEmpty(path)) UnityEditor.AssetDatabase.AddObjectToAsset(biome, path);
        baseBiome = biome;
        UnityEditor.EditorUtility.SetDirty(biome);
        UnityEditor.EditorUtility.SetDirty(this);
        return biome;
    }
#endif

    // ── Sound ─────────────────────────────────────────────────────────
    [TabGroup(Tabs, Sound), Title("Music", HorizontalLine = false), Tooltip("The level's music; a biome with music of its own plays that instead on its floors.")]
    public AudioClip backgroundMusic;
    [TabGroup(Tabs, Sound), Range(0f, 1f), LabelText("Volume"), Tooltip("BGM volume for this level: 0 is silent, 1 is full volume. Still respects the AudioManager Music and Master mixer settings. Reload the level to apply changes.")]
    public float backgroundMusicVolume = 1f;

    // ── Dialogue ──────────────────────────────────────────────────────
    [TabGroup(Tabs, Dialogue), ShowIf(D), ListDrawerSettings(ShowFoldout = true), Tooltip("What the Dungeon Master says during a run here: a trigger, a chance, and lines to pick from. Never two within the remark gap set on the Dialogue Manager.")]
    public DungeonDialogue[] dialogue = new DungeonDialogue[0];

#if UNITY_EDITOR
    // Under the tabs: what is no longer on a level because every level had the same.
    [ShowIf(D), PropertyOrder(2), Button("Shared by every level: Table level defaults"), PropertyTooltip("The plain chest and its sounds, the merchant and his stock, breakables, features, wall lights, room shapes, the room event pieces (shrine, spike plate, ambush gate and sounds, snoring) and the run's HUD pieces are the same on every level: Levels/Resources/Table level defaults.")]
    void SelectSharedDefaults() { if (Shared != null) UnityEditor.Selection.activeObject = Shared; }
#endif

    // ── On the base biome (its Level tab) ─────────────────────────────
    // Read through to the base biome; without one, the defaults.
    DungeonBiome B => baseBiome;
    public DungeonRoomStyle defaultStyle => B != null ? B.defaultStyle : null;
    public DungeonStyleChoice[] roomStyles => B != null ? B.roomStyles : new DungeonStyleChoice[0];
    public bool corridorsCopyConnectedRoomStyle => B != null ? B.corridorsCopyConnectedRoomStyle : false;
    public DungeonStyleChoice[] corridorStyles => B != null ? B.corridorStyles : new DungeonStyleChoice[0];
    public bool kitModels => B != null ? B.kitModels : true;
    public Material boardEdgeMaterial => B != null ? B.boardEdgeMaterial : null;
    public Material boardGridMaterial => B != null ? B.boardGridMaterial : null;
    public Material boardPitMaterial => B != null ? B.boardPitMaterial : null;
    public float boardVerticality => B != null ? B.boardVerticality : 1f;
    public AudioClip[] boardTileTaps => B != null ? B.boardTileTaps : new AudioClip[0];
    public GameObject[] boardDoorPosts => B != null ? B.boardDoorPosts : new GameObject[0];
    public GameObject[] boardMoundProps => B != null ? B.boardMoundProps : new GameObject[0];
    public Color boardLightColor => B != null ? B.boardLightColor : new Color(1f, .86f, .68f);
    public float boardLightIntensity => B != null ? B.boardLightIntensity : 2600f;
    public Vector2Int torchesPerRoom => B != null ? B.torchesPerRoom : new Vector2Int(1, 2);
    public float litRoomChance => B != null ? B.litRoomChance : .75f;
    public float corridorTorchChance => B != null ? B.corridorTorchChance : .3f;
    public GameObject darkRoomLight => B != null ? B.darkRoomLight : null;
    public DungeonPropRule[] propRules => B != null ? B.propRules : new DungeonPropRule[0];
    public bool furnishEntranceRims => B != null ? B.furnishEntranceRims : false;
    public Vector2Int destructiblesPerRoom => B != null ? B.destructiblesPerRoom : new Vector2Int(1, 3);
    public DungeonWeightedPrefab[] chestTiers => B != null ? B.chestTiers : new DungeonWeightedPrefab[0];
    // Settable for the Asset Studio's room field: it writes the base biome's.
    public GameObject shopRoom
    {
        get => B != null ? B.shopRoom : null;
        set
        {
            if (B != null) B.shopRoom = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(B != null ? B : this);
#endif
        }
    }
    public DungeonRoomFeatures roomFeatures => B != null ? B.roomFeatures : NoFeatures;
    static readonly DungeonRoomFeatures NoFeatures = new();
    // The intro's look sets its board light colour (on the base biome).
    public void SetBoardLightColor(Color color) { if (B != null) B.boardLightColor = color; }
    // The default style's materials (null without one).
    public Material floorMaterial => defaultStyle != null ? defaultStyle.floor : null;
    public Material wallMaterial => defaultStyle != null ? defaultStyle.bottomWall : null;
    public Material upperWallMaterial => defaultStyle != null ? defaultStyle.upperWall : null;
    public Material trimMaterial => defaultStyle != null ? defaultStyle.trim : null;
    public Material ceilingMaterial => defaultStyle != null ? defaultStyle.ceiling : null;
    public Material woodMaterial => defaultStyle != null ? defaultStyle.wood : null;
    public Material metalMaterial => defaultStyle != null ? defaultStyle.metal : null;

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
    // The room event pieces; before the defaults have them, the level's old ones.
    public GameObject shrinePrefab => Shared != null ? Shared.shrinePrefab : null;
    public GameObject spikeTrapPrefab => Shared != null ? Shared.spikeTrapPrefab : null;
    public GameObject ambushGatePrefab => Shared != null ? Shared.ambushGatePrefab : null;
    public GameObject ambushSpawnPuff => Shared != null ? Shared.ambushSpawnPuff : null;
    public AudioClip ambushSlamAudio => Shared != null ? Shared.ambushSlamAudio : null;
    public AudioClip ambushLiftAudio => Shared != null ? Shared.ambushLiftAudio : null;
    public AudioClip ambushSpawnAudio => Shared != null ? Shared.ambushSpawnAudio : null;
    public AudioClip sleepAudio => Shared != null ? Shared.sleepAudio : null;

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
    public float darkRoomLightStrength => .6f;      // a dark room's one light against the prefab's own
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
    // Floors past the plan stay on the last biome's deepest floor. Without stages: the base biome.
    public DungeonBiome BiomeAt(int floor, out int floorInBiome, out int biomeFloors) => StageAt(floor, out floorInBiome, out biomeFloors)?.biome;

    // The floor's biome when it is a stage's own, null when it is the base biome (or there is none): the base biome's
    // lists are read once, a stage biome's are added to them.
    public DungeonBiome StageBiome(DungeonBiome floorBiome) => floorBiome != null && floorBiome != baseBiome ? floorBiome : null;

    // Where a floor's board pieces and board light come from: a board stage that has its own (DungeonBiome.ownBoardPieces),
    // else the base biome (null without one; callers fall back to the defaults the properties above give).
    public DungeonBiome BoardBiome(int floor)
    {
        var stage = StageBiome(Biome(floor));
        return stage != null && stage.IsBoard && stage.ownBoardPieces ? stage : baseBiome;
    }
    public float BoardLightIntensity(int floor) { var b = BoardBiome(floor); return b != null ? b.boardLightIntensity : 2600f; }
    public Color BoardLightColor(int floor) { var b = BoardBiome(floor); return b != null ? b.boardLightColor : new Color(1f, .86f, .68f); }
    // The way the board light shines: straight down, or leaning by its biome's angle from its side.
    public Quaternion BoardLightRotation(int floor)
    {
        var b = BoardBiome(floor);
        float lean = b != null ? Mathf.Clamp(b.boardLightAngle, 0f, 70f) : 0f;
        return Quaternion.Euler(0f, b != null ? b.boardLightFrom + 180f : 0f, 0f) * Quaternion.Euler(90f - lean, 0f, 0f);
    }

    // The biomes a run of this level goes through, in order: the stages', or the base biome alone.
    public List<DungeonBiome> RunBiomes()
    {
        var list = new List<DungeonBiome>();
        if (HasStages) { foreach (var s in stages) if (s != null && s.biome != null) list.Add(s.biome); }
        else if (baseBiome != null) list.Add(baseBiome);
        return list;
    }

    // The stage a floor belongs to (past the end: the last stage, on its last floor). Without stages: one stage of
    // Floors floors on the base biome (null when there is none).
    BiomeStage StageAt(int floor, out int floorInBiome, out int biomeFloors)
    {
        if (!HasStages)
        {
            biomeFloors = Mathf.Max(1, floors);
            floorInBiome = Mathf.Clamp(floor, 1, biomeFloors);
            return baseBiome != null ? new BiomeStage { biome = baseBiome, floors = biomeFloors, boss = bossOnLastFloor } : null;
        }
        int start = 1;
        BiomeStage last = null;
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

    // The floor's biome decides (a stageless level's is its base biome).
    // Every dungeon floor is marked for the Lighting Manager's dungeon darkness unless its biome is Lit Throughout.
    public LightingManager.MoodState DungeonLighting(int floorNumber = 1)
    {
        var state = DungeonLightingPreset(floorNumber);
        var biome = Biome(floorNumber);
        state.darkness = biome != null && biome.litThroughout ? 0f : 1f;
        state.onFloor = 1f;
        if (biome != null && biome.litThroughout && biome.fog.y > 0f) { state.fogStart = biome.fog.x; state.fogEnd = biome.fog.y; }
        state.roomDark = RoomDarkness;
        state.roomShade = new Color(roomTint.r, roomTint.g, roomTint.b, 1f);
        state.minAmbient = minimumAmbient;
        return state;
    }

    float RoomDarkness => LightingManager.Darkness(roomLighting, roomBrightness);

    // A board lies under the room's own light: the room as it is now, with this level's Room Lighting and tint.
    public LightingManager.MoodState BoardLighting(LightingManager lighting) => lighting.RoomLook(roomLighting, roomTint, roomBrightness);
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
        // No biome at all: the level's old table lighting, else the town's mood, else plain settings.
        var preset = Resources.Load<SceneMood>("DungeonLighting/" + DungeonMoodLighting.Standard);
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

    // The biome's guardian, on the stage's last floor (when the stage has its boss; without stages, Boss On Last Floor).
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
