using System;
using Sirenix.OdinInspector;
using UnityEngine;

// All serialized by integer: append only.
// What a biome's floors are built as: walled rooms, or a board game of printed tiles on the table.
public enum DungeonFloorKind { Dungeon = 0, [InspectorName("Board game")] Boardgame = 1 }
// How a board floor comes onto the table: room by room as its doors open, or all of it at once.
public enum BoardRoomReveal { [InspectorName("As their doors open")] DoorByDoor = 0, [InspectorName("All at once")] WholeFloor = 1 }

// One stretch of a dungeon run: cellars, crypt, caves. A level lists its biomes in order with a
// floor count each (TableLevelData.stages). Everything that makes a biome feel different lives
// here: how it looks and sounds, what lives in it and how densely, what it drops, and the guardian
// waiting on its last floor. Every level also has a base biome stored inside its asset
// (TableLevelData.baseBiome, isLevelBase): its Level tab holds what every floor of the level shares,
// a stage's lists are added to the base biome's, and a level without stages runs on its base biome
// alone (a one-floor level needs no biome file). Music replaces the level's only when set here.
[CreateAssetMenu(menuName = "LoomRoom/Dungeon Biome", fileName = "Biome")]
public class DungeonBiome : ScriptableObject
{
    const string Look = "Look", RoomsTab = "Rooms", Life = "Population", Loot = "Rewards", Boss = "Guardian", Sound = "Audio", LevelTab = "Level";

    [TabGroup(Look), InfoBox("Lists in this biome are added to its level's base biome's, which every floor shares. Put things all stages use on the base biome; put what makes this stage different here.", VisibleIf = "@!isLevelBase")]
    [TabGroup(Look)] public string displayName = "The Crypt";
    [TabGroup(Look), Tooltip("Dungeon: walled rooms with ceilings. Board game: the same rooms built as printed tiles on the table, no walls (the board's pieces and light are on the level's base biome, Level tab). Each floor is built as its own biome says, so a run can go from one to the other.")]
    public DungeonFloorKind kind;
    [TabGroup(Look), ShowIf(nameof(IsBoard)), LabelText("Rooms appear"), Tooltip("As their doors open: only the rooms reachable from the start are on the table; opening a door deals in the rooms behind it, tile by tile. All at once: the whole floor is built from the start, as on a walled dungeon.")]
    public BoardRoomReveal boardReveal;
    public bool IsBoard => kind == DungeonFloorKind.Boardgame;
    [TabGroup(Look), InfoBox("A level's base biome: what every floor of the level shares (its Level tab, room features and lists; a stage's biome adds its own lists). On a level without stages every floor is this biome.", VisibleIf = nameof(isLevelBase))]
    [TextArea, Tooltip("Posted to the message log when the run enters this biome. Keep it short and plain.")]
    public string entryMessage;

    [TabGroup(Look), Tooltip("Weighted styles picked once per whole room (empty or disabled entries use the Default Style). A stage's are added to the base biome's.")]
    public DungeonStyleChoice[] roomStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Weighted styles picked once per whole corridor section (empty uses the Default Style). A stage's are added to the base biome's.")]
    public DungeonStyleChoice[] corridorStyles = new DungeonStyleChoice[0];
    [TabGroup(Look), Tooltip("Added to the room shapes every level shares (Table level defaults); a stage's also to the base biome's.")]
    public DungeonShapeChoice[] roomShapes = new DungeonShapeChoice[0];
    [TabGroup(Look), Tooltip("Plain props, used where no furnishing rules apply. A stage's are added to the base biome's.")]
    public GameObject[] props = new GameObject[0];
    [TabGroup(Look), Tooltip("Props placed by rule and scaled by floor area: against walls, in corners, in the middle. A stage's rules are added to the base biome's.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [TabGroup(Look), Tooltip("Added to the breakables every level shares (Table level defaults); a stage's also to the base biome's.")]
    public DungeonWeightedPrefab[] destructibles = new DungeonWeightedPrefab[0];

    // Same numbers as DungeonMoodLighting (a preset of that name in Resources/DungeonLighting), plus Custom.
    public enum Lighting { AmberCrypt = 0, MoonlitStone = 1, EmeraldRuins = 2, RoseSanctuary = 3, GoldenHall = 4, Default = 5, TableSpotlight = 6, Darkness = 7, Standard = 8, Custom = 100 }
    [TabGroup(Look), Title("Lighting", "How a walled dungeon floor of this biome is lit. A board level is lit by its board light (the level's Lighting tab) instead, unless Light Board is ticked; the room's look is on the level's Lighting tab too.")]
    [Tooltip("The lighting in this biome: a preset, or Custom to set every value below. On dark floors the Lighting Manager holds the ambient down (Dungeon darkness), so the torches give most of the light.")]
    public Lighting lighting = Lighting.Darkness;
    // The preset picked above, editable here (it is a shared asset in Levels/Resources/DungeonLighting: every biome
    // using it changes too). Custom shows its own values further down instead.
    [TabGroup(Look), ShowInInspector, ShowIf(nameof(UsesPreset)), InlineEditor(InlineEditorObjectFieldModes.Foldout), LabelText("Preset (shared)")]
    SceneMood LightingPreset { get => UsesPreset ? Resources.Load<SceneMood>("DungeonLighting/" + (DungeonMoodLighting)(int)lighting) : null; set { } }   // picked with the dropdown above
    bool UsesPreset => lighting != Lighting.Custom;
    [TabGroup(Look), Tooltip("Skip the Lighting Manager's dungeon darkness here: the preset's ambient light reaches everywhere, so unlit places stay visible.")]
    public bool litThroughout;
    [TabGroup(Look), Tooltip("Board-game levels: light the board with this biome's lighting above instead of the room's own light (the level's Room Lighting), so each biome of a board level can look different.")]
    public bool lightBoard;
    [System.Serializable]
    public class RoomTemplateChoice
    {
        [AssetsOnly, Tooltip("A DungeonRoomTemplate prefab: a hand-built room (footprint, kit blocks, props, sockets).")] public GameObject template;
        [Min(0)] public float weight = 1;
        [Tooltip("The kinds of room it may become.")] public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage;
        [Min(1), Tooltip("First floor of this biome it can appear on.")] public int fromFloor = 1;
        [Min(0), Tooltip("Unused: no room template comes up more than once on a floor (DungeonGenerator.PoolTemplates).")] public int maxPerFloor = 1;
    }
    [TabGroup(RoomsTab), Tooltip("Hand-built rooms (chunks) this biome's floors are made of: each room of a suitable kind is built from one of these by chance (Template Chance), the rest are generated. A room profile's own template comes first.")]
    public RoomTemplateChoice[] roomTemplates = new RoomTemplateChoice[0];
    [TabGroup(RoomsTab), Range(0f, 1f), Tooltip("Share of the rooms that get a hand-built room from the list above (when one suits them).")]
    public float templateChance = .7f;
    [TabGroup(RoomsTab), HideIf(nameof(isLevelBase)), Tooltip("Use the room features below on this biome's floors instead of the level's (its base biome's Room Features).")]
    public bool overrideDungeonSettings;
    [TabGroup(RoomsTab), ShowIf("@overrideDungeonSettings || isLevelBase"), InfoBox("Room events, heights and room light on every floor of the level. A stage's biome can replace them (its Override Dungeon Settings).", VisibleIf = nameof(isLevelBase))]
    [InlineProperty, HideLabel]
    public DungeonRoomFeatures roomFeatures = new();
    // The room features of this biome's floors: its own when it overrides the level's or is the level's base.
    public DungeonRoomFeatures RoomFeatures(TableLevelData level) => overrideDungeonSettings || isLevelBase || level == null ? roomFeatures : level.roomFeatures;
    [TabGroup(Look), Min(0f), Tooltip("Metres of full ambient light around the player here (LightingManager's light radius); 0 uses the manager's.")]
    public float lightRadius;
    [TabGroup(Look), Range(0f, 1f), Tooltip("How much of the filmic grade (ScreenManager > Filmic look: ACES tonemapping, contrast, saturation, warm white balance) this biome's floors get. 1 as in the room; 0 plain, truer colours.")]
    public float filmicLook = 1f;
    [TabGroup(Look), Tooltip("Colour split (ScreenManager): shadows and ambient lean to this (cool).")]
    public Color shadowTint = new(.55f, .85f, 1f);
    [TabGroup(Look), Tooltip("Colour split (ScreenManager): lamps, torches and the player's light lean to this (warm).")]
    public Color lampTint = new(1.06f, .97f, .86f);
    bool CustomLighting => lighting == Lighting.Custom;
    [TabGroup(Look), ShowIf(nameof(CustomLighting)), InlineProperty, HideLabel]
    public DungeonLightingSettings lightingSettings = new DungeonLightingSettings();
    [TabGroup(Look), Tooltip("Wall torches and other wall lights in this biome, weighted, each with an optional colour. Empty uses the base biome's, then the ones every level shares (Table level defaults).")]
    public DungeonWallLight[] lightingPrefabs = new DungeonWallLight[0];
    // The biome-wide fallback: hidden while every torch in the list sets its own colour (it would change nothing).
    [TabGroup(Look), HideIf(nameof(EveryTorchColoured)), LabelText("Default Torch Colour"),
     Tooltip("Colour for torches that nothing more specific colours: list entries without their own Override Color, the level's torches when this list is empty, room template sockets without a colour. A socket, room template, room profile or entry colour wins.")]
    public bool overrideTorchColor;
    [TabGroup(Look), ShowIf("@overrideTorchColor && !EveryTorchColoured"), ColorUsage(false), LabelText("Colour")]
    public Color torchColor = new Color(1f, .6f, .3f);
    bool EveryTorchColoured
    {
        get
        {
            bool any = false;
            if (lightingPrefabs != null) foreach (var l in lightingPrefabs) if (l != null && l.prefab != null) { if (!l.overrideColor) return false; any = true; }
            return any;
        }
    }
    [TabGroup(Look), Range(0f, 3f), Tooltip("Brightness of every torch in this biome. 1 is as the torch prefab is made.")]
    public float torchBrightness = 1f;
    // The colour a torch gets when nothing more specific sets one (null keeps the prefab's own). Off while hidden, so the
    // inspector never hides a colour that still applies (it would only reach socket prefabs then).
    public Color? TorchColor => overrideTorchColor && !EveryTorchColoured ? torchColor : null;

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
    [TabGroup(Life), Range(1, 6), Tooltip("Fewest enemies in a room that has any (packs: 2 or more).")] public int minEnemiesPerRoom = 1;
    [TabGroup(Life), Range(1, 6)] public int maxEnemiesPerRoom = 2;
    [TabGroup(Life), Tooltip("Added to the features every level shares (Table level defaults); a stage's also to the base biome's. Min Floor counts from the biome's first floor (for the shared features too).")]
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
    [TabGroup(Sound), MinMaxSlider(2, 200, true), Tooltip("Seconds between the occasional sounds: a random gap between the two.")] public Vector2 oneShotInterval = new Vector2(60, 200);
    [TabGroup(Sound), Range(0, 1)] public float oneShotVolume = .5f;
    [TabGroup(Sound), Tooltip("Replaces the level's music in this biome. Empty keeps the level's.")]
    public AudioClip music;
    [TabGroup(Sound), Range(0, 1)] public float musicVolume = 1f;

    [TabGroup(Sound), Title("Voice"), Range(0, 1), Tooltip("Chance the Dungeon Master says one of the lines below when the run enters this biome (the first biome too).")]
    public float entryLineChance = .6f;
    [TabGroup(Sound), Tooltip("Picked at random. Leave Voice empty to mumble.")]
    public DungeonMaster.Line[] entryLines = new DungeonMaster.Line[0];

    // ── Level: a level's base biome only (TableLevelData.baseBiome) ──
    // What every floor of the level shares. The level's properties of the same names read these.
    [HideInInspector, Tooltip("This biome is a level's base biome (set when it is made): its Level tab shows.")]
    public bool isLevelBase;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Default style", HorizontalLine = false), LabelText("Style"), InlineEditor(InlineEditorObjectFieldModes.Foldout)]
    [Tooltip("The fallback. Rooms and corridors no style is picked for use this style; other styles take any material they leave empty from it. Its Wood and Metal make the exit hatch and gate.")]
    public DungeonRoomStyle defaultStyle;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Tooltip("Copy a connected room's complete style without crossing a planned doorway. Search through open corridors if needed. If no room is reachable without crossing a door, use the Default Style. Selection is seeded; opening doors does not change styles.")]
    public bool corridorsCopyConnectedRoomStyle;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Tooltip("Hand-built rooms use the kit's painted models (pillars, arches, rubble, railings) where a piece has one. Off: every kit piece is built of plain blocks in the room's own stone.")]
    public bool kitModels = true;

    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Torches", "The only lights in the dungeon besides the player's own.", HorizontalLine = false), MinMaxSlider(1, 4, true), Tooltip("Torches in a room that rolls its full count (at least one).")]
    public Vector2Int torchesPerRoom = new Vector2Int(1, 2);
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Range(0, 1), LabelText("Full Torch Chance"), Tooltip("Chance a room gets its Torches Per Room; the others get one. The entrance and exit always get the full count. Every room has at least one light (a dark one only Dark Room Light).")]
    public float litRoomChance = .75f;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Range(0, 1), Tooltip("Chance of a torch on each stretch of corridor wall (about every 8 cells).")]
    public float corridorTorchChance = .3f;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), AssetsOnly, Tooltip("The one light a dark room gets instead of torches (a candle cluster on the floor by a wall), so no room is without a light; it stays dim. Empty = dark rooms have no light at all.")]
    public GameObject darkRoomLight;

    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Furnishing", HorizontalLine = false), Tooltip("Furnish the rim of the entrance and exit rooms too (their middles stay clear). Off keeps both rooms bare.")]
    public bool furnishEntranceRims;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), LabelText("Extra Breakables Per Room"), Tooltip("Extra breakables scattered per room, on top of supply spots.")]
    [MinMaxSlider(0, 8, true)] public Vector2Int destructiblesPerRoom = new Vector2Int(1, 3);

    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Chests and the shop", HorizontalLine = false), Tooltip("Chest tiers picked by weight (wood, iron, gold: each prefab's DungeonContainer says what it gives). Empty uses the shared plain chest.")]
    public DungeonWeightedPrefab[] chestTiers = new DungeonWeightedPrefab[0];
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), AssetsOnly, LabelText("Shop Room"), Tooltip("Room template the merchant's room is built from (a Merchant socket marks where he stands). Empty puts him in a quiet side room as it is.")]
    public GameObject shopRoom;

    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Board", "Board-game floors only (a biome whose Kind is Board game): the tiles' printed tops use the floor materials; these make the rest.", HorizontalLine = false), Tooltip("The cut edge of a ground-level tile: layers of card.")]
    public Material boardEdgeMaterial;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Tooltip("Printed grid over each tile (transparent).")]
    public Material boardGridMaterial;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Tooltip("The bottom of a pit on the board (a template's Pit floor cut): the tiles there are taken away down to the table and this dark card lies in the hole. Empty uses the edge material.")]
    public Material boardPitMaterial;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Range(0, 8), Tooltip("How vertical the board is. The tallest room rises to its layer height times this; about half the other rooms keep their level, some dip lower and some climb part of the way. 1 keeps the layers as set, below 1 flattens. Rooms that would need too steep a stair are lifted to fit.")]
    public float boardVerticality = 1f;
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Tooltip("Soft taps played as tiles land when a room is revealed (Tools/board_tap.py makes them).")]
    public AudioClip[] boardTileTaps = new AudioClip[0];
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), AssetsOnly, Tooltip("Board doors stand between two of these (trees, for a forest) instead of a stone arch with a beam. Empty builds the arch.")]
    public GameObject[] boardDoorPosts = new GameObject[0];
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), AssetsOnly, Tooltip("Cells a room's shape leaves out inside it (between the columns of a pillar hall, the middle of a ring) become low mounds with one of these on top, instead of holes in the board. Empty leaves the holes.")]
    public GameObject[] boardMoundProps = new GameObject[0];
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Title("Board light", "The light hung over a board level, like a lamp over a gaming table: it lights every room.", HorizontalLine = false)]
    [ColorUsage(false)] public Color boardLightColor = new Color(1f, .86f, .68f);
    [TabGroup(LevelTab), ShowIf(nameof(isLevelBase)), Min(0)] public float boardLightIntensity = 2600f;

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
        var pick = WeightedPick.Choose(enemies, s => s != null && s.enemy != null && s.fromFloor <= floorInBiome ? s.weight : 0, random, singleTotal: true);
        return pick != null ? pick.enemy : null;
    }

#if UNITY_EDITOR
    // The base biome's own fields (Level tab, room features) and the lists it holds for its level: a copy of another
    // biome's content leaves them alone.
    static readonly System.Collections.Generic.HashSet<string> LevelSide = new()
    {
        "m_Script", nameof(isLevelBase), nameof(defaultStyle), nameof(corridorsCopyConnectedRoomStyle), nameof(kitModels),
        nameof(torchesPerRoom), nameof(litRoomChance), nameof(corridorTorchChance), nameof(darkRoomLight), nameof(furnishEntranceRims),
        nameof(destructiblesPerRoom), nameof(chestTiers), nameof(shopRoom), nameof(boardEdgeMaterial), nameof(boardGridMaterial),
        nameof(boardPitMaterial), nameof(boardVerticality), nameof(boardTileTaps), nameof(boardDoorPosts), nameof(boardMoundProps),
        nameof(boardLightColor), nameof(boardLightIntensity), nameof(roomFeatures), nameof(overrideDungeonSettings),
        nameof(roomStyles), nameof(corridorStyles), nameof(propRules),
    };
    public static bool IsLevelSide(string field) => LevelSide.Contains(field);

    // Everything that makes another biome what it is (look, enemies, loot, sound, guardian, room pool, its other lists)
    // copied onto this one; this one's Level tab, room features and style and furnishing lists stay.
    public void CopyContentFrom(DungeonBiome other)
    {
        if (other == null || other == this) return;
        var from = new UnityEditor.SerializedObject(other);
        var to = new UnityEditor.SerializedObject(this);
        var it = from.GetIterator();
        for (bool more = it.NextVisible(true); more; more = it.NextVisible(false))
            if (!IsLevelSide(it.name)) to.CopyFromSerializedProperty(it);
        to.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    // A level's one stage biome folded into its base biome: its content, its styles and furnishing rules added after the
    // base's (the order the generator used to add them in), and its room features when it replaced the level's.
    public void FoldIn(DungeonBiome stage)
    {
        if (stage == null || stage == this) return;
        CopyContentFrom(stage);
        roomStyles = Join(roomStyles, CloneAll(stage.roomStyles));
        corridorStyles = Join(corridorStyles, CloneAll(stage.corridorStyles));
        propRules = Join(propRules, CloneAll(stage.propRules));
        if (stage.overrideDungeonSettings) roomFeatures = Clone(stage.roomFeatures ?? new DungeonRoomFeatures());
        overrideDungeonSettings = false;
        UnityEditor.EditorUtility.SetDirty(this);
    }
    static T[] Join<T>(T[] a, T[] b)
    {
        var all = new System.Collections.Generic.List<T>(a ?? new T[0]);
        if (b != null) all.AddRange(b);
        return all.ToArray();
    }

    // A deep copy of a serializable class through the editor's JSON (asset references kept).
    public static T Clone<T>(T source) where T : class, new()
    {
        if (source == null) return null;
        var copy = new T();
        UnityEditor.EditorJsonUtility.FromJsonOverwrite(UnityEditor.EditorJsonUtility.ToJson(source), copy);
        return copy;
    }
    public static T[] CloneAll<T>(T[] source) where T : class, new()
    {
        if (source == null) return new T[0];
        var copy = new T[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = Clone(source[i]);
        return copy;
    }
#endif
}
