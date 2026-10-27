using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Dungeon section's Room events page: what a floor's rooms hold beyond their enemies, in plain words. First how a
// floor's rooms are split (the first room, the stairs room, the tavern; the rest fights or quiet rooms), then one card
// per room event: where it can land (as DungeonGenerator.Events rolls it), its chance as a share of the rooms it can
// land in, its caps, what that comes to on a floor, what real floors held (RoomEventSurvey: runs of the level built in a
// test scene) and a Try it run with the event forced on wherever it can be. The hand-built-only features (pits, ledges,
// candlelit and moonlit rooms) are switches that let the rooms made for them be used. Every floor shares the base
// biome's settings unless its biome has its own (Override Dungeon Settings).
public partial class AssetStudioWindow
{
    [SerializeField] int eventSet;   // the settings shown: 0 the shared ones, then each floor with its own
    string eventHash;                // the level's settings hash, worked out at the layout pass

    class EventSet
    {
        public DungeonBiome owner;
        public string label;
        public readonly List<AssetStudioWorld.Stage> stages = new();
        public DungeonRoomFeatures F => owner.roomFeatures;
    }

    List<EventSet> EventSets(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var sets = new List<EventSet>();
        if (d.baseBiome != null)
        {
            var shared = new EventSet { owner = d.baseBiome };
            shared.stages.AddRange(l.RunStages.Where(st => st.biome == null || st.IsBase || !st.biome.overrideDungeonSettings));
            shared.label = shared.stages.Count == l.RunStages.Count ? "Every floor" : "Floors " + string.Join(", ", shared.stages.Select(st => FloorsLabel(st).Replace("Floors ", "").Replace("Floor ", "")));
            sets.Add(shared);
        }
        foreach (var st in l.RunStages.Where(st => st.biome != null && !st.IsBase && st.biome.overrideDungeonSettings))
        {
            var own = new EventSet { owner = st.biome, label = $"{FloorsLabel(st)} · {st.biome.name}" };
            own.stages.Add(st);
            sets.Add(own);
        }
        return sets;
    }

    void RoomEventsPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        if (Event.current.type == EventType.Layout || eventHash == null) eventHash = RoomEventSurvey.SettingsHash(d);
        var survey = RoomEventSurvey.Last(d);
        bool fresh = survey != null && survey.hash == eventHash;
        PageHeader("Room events", "What a floor's rooms hold beyond their enemies: ambushes, sleeping packs, shrines, trapped chests, dark and green-lit rooms, and the rooms built round pits, ledges, candles and moonlight. Each chance is a share of the rooms it can land in; Measured is what real floors of this dungeon held.",
            ("Measure (10 runs, about 25 s)", "Builds 10 runs of this dungeon, every floor, as the game builds them (in a test scene, not in the game) and counts what each floor holds.", () => MeasureEvents(d)));
        if (survey == null) GUILayout.Label("<color=#e0a85a>Not measured yet: press Measure for what floors really hold. The estimates below are worked out from the chances.</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        else GUILayout.Label($"Measured {survey.runs} runs ({survey.floors.Sum(f => f.built)} floors) at {survey.At:HH:mm, d MMM}" + (fresh ? "." : "  <color=#e0a85a>· settings changed since: measure again.</color>") + (survey.failed > 0 ? $"  <color=#e0a85a>{survey.failed} floors failed to build (see the Console).</color>" : ""), new GUIStyle(dim) { richText = true, wordWrap = true });

        RoomSplit(l, survey);

        var sets = EventSets(l);
        if (sets.Count == 0) { GUILayout.Label("<color=#e0a85a>No base biome: nothing holds the room events.</color>", new GUIStyle(dim) { richText = true }); return; }
        eventSet = Mathf.Clamp(eventSet, 0, sets.Count - 1);
        Section("THE SETTINGS");
        if (sets.Count > 1)
        {
            eventSet = GUILayout.Toolbar(eventSet, sets.Select(s => s.label).ToArray(), StudioSkin.Tab, GUILayout.Height(28));
            GUILayout.Space(4);
        }
        var set = sets[eventSet];
        GUILayout.Label(set.owner == d.baseBiome
            ? $"{set.label} of {d.name} uses these (the base biome's). A floor can have its own: Its own settings in the table above."
            : $"Only {set.label} uses these; the other floors share the base biome's.", new GUIStyle(dim) { wordWrap = true });

        AmbushCard(l, set, survey);
        SleepCard(l, set, survey);
        ShrineCard(l, set, survey);
        TrapCard(l, set, survey);
        LightCard(l, set, survey);
        BuiltForCards(l, set, survey);
    }

    void MeasureEvents(TableLevelData d)
    {
        if (EditorApplication.isPlaying) { Toast("Stop playing first: the floors are built in a test scene in Edit mode."); return; }
        var r = RoomEventSurvey.Measure(d, 10);
        Toast(r == null ? "Measuring cancelled." : $"Measured {r.floors.Sum(f => f.built)} floors.");
        eventHash = null;
        GUIUtility.ExitGUI();
    }

    // ── How a floor's rooms are split ──

    void RoomSplit(AssetStudioWorld.Level l, RoomEventSurvey.Result survey)
    {
        var d = l.data;
        Section("HOW A FLOOR'S ROOMS ARE SPLIT");
        int middle = Mathf.Max(0, d.roomCount - 2);
        GUILayout.Label($"A floor has about {d.roomCount} rooms: the first room, the stairs room, and {middle} more. Each of those is a fight at the floor's fight share, else a quiet room (treasure, rest or storage, one each in turn). The tavern takes one quiet room (a fight room when there is none). Hand-built is the share of rooms that get a hand-built interior when one in the pool suits them.", new GUIStyle(dim) { wordWrap = true });
        foreach (var st in l.RunStages.Where(s => s.biome != null))
        {
            var b = st.biome;
            GUILayout.BeginVertical(StudioSkin.Inset);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{FloorsLabel(st)} · {b.name}", EditorStyles.boldLabel, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            if (!st.IsBase)
            {
                if (b.overrideDungeonSettings)
                {
                    GUILayout.Label("its own room events", dim);
                    if (GUILayout.Button(new GUIContent("Share again", "Use the settings every other floor shares (this floor's own are kept for if you switch back)."), GUILayout.Width(110)))
                    { EditBiome(b, () => b.overrideDungeonSettings = false); eventSet = 0; }
                }
                else if (GUILayout.Button(new GUIContent("Give it its own", "This floor gets its own room event settings, starting as a copy of the shared ones."), GUILayout.Width(130)))
                {
                    EditBiome(b, () => { b.roomFeatures = DungeonBiome.Clone(d.baseBiome != null ? d.baseBiome.roomFeatures : new DungeonRoomFeatures()); b.overrideDungeonSettings = true; });
                    eventSet = Mathf.Max(0, EventSets(l).FindIndex(s => s.owner == b));
                }
            }
            GUILayout.EndHorizontal();
            // Fights: a stage of one floor uses the share at its last floor (encounterChance.y).
            var enc = b.encounterChance;
            if (st.lastFloor > st.firstFloor)
            {
                float from = Pct("Fights on its first floor", enc.x, 1, "% of rooms");
                float to = Pct("Fights on its last floor", enc.y, 1, "% of rooms");
                if (!Mathf.Approximately(from, enc.x) || !Mathf.Approximately(to, enc.y)) EditBiome(b, () => b.encounterChance = new Vector2(from, to));
            }
            else
            {
                float to = Pct("Fights", enc.y, 1, "% of rooms");
                if (!Mathf.Approximately(to, enc.y)) EditBiome(b, () => b.encounterChance = new Vector2(Mathf.Min(enc.x, to), to));
            }
            float hb = Pct("Hand-built", b.templateChance, 1, "% of rooms one suits");
            if (!Mathf.Approximately(hb, b.templateChance)) EditBiome(b, () => b.templateChance = hb);
            float share = (enc.x + enc.y) / 2;
            if (st.lastFloor == st.firstFloor) share = enc.y;
            float fights = middle * share, quiet = middle * (1 - share);
            bool tavern = b.merchantChance > 0;
            GUILayout.Label($"About {fights:0.#} fights and {quiet:0.#} quiet rooms a floor{(tavern ? ", one of them the tavern" : "")}.", gold);
            var m = MeasuredFloors(survey, st);
            if (m.Count > 0)
                GUILayout.Label($"Measured: {Avg(m, f => f.fights):0.#} fights, {Avg(m, f => f.treasure):0.#} treasure, {Avg(m, f => f.rest):0.#} rest and {Avg(m, f => f.storage):0.#} storage rooms, {Avg(m, f => f.tavern):0.#} tavern; {Avg(m, f => f.handBuilt):0.#} rooms hand-built ({Avg(m, f => f.quietHandBuilt):0.#} of the quiet ones).", new GUIStyle(dim) { wordWrap = true });
            GUILayout.EndVertical();
        }
    }

    // ── The cards ──

    void AmbushCard(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        float fights = Fights(l, set);
        string estimate = f.ambushes && f.maxAmbushes > 0 ? $"With about {fights:0} fights a floor, {Chance(f.ambushChance, fights) * 100:0}% of floors roll one, and rooms built as an ambush add to that." : "None.";
        EventCard(l, set, "Ambushes", "ambush", f.ambushes, v => f.ambushes = v,
            "A fight room becomes a trap: once you are inside, bars slam over every way out and the enemies come in two waves; the bars lift after the last and a better chest is left. Any fight room can roll it (a hand-built one whose event is Rolled); rooms built as an ambush always are one. Never the first room, the stairs room or the tavern.",
            () =>
            {
                float c = Pct("Chance", f.ambushChance, 1, "% of fights");
                bool one = Toggle("At most one a floor", f.maxAmbushes > 0, "Off: no ambushes at all, not even rooms built for one.");
                if (!Mathf.Approximately(c, f.ambushChance) || one != f.maxAmbushes > 0) EditBiome(set.owner, () => { f.ambushChance = c; f.maxAmbushes = one ? 1 : 0; });
            },
            estimate, Measured(survey, set, x => x.ambushes, x => x.withAmbush, "ambushes"));
    }

    void SleepCard(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        float fights = Fights(l, set);
        float expected = f.sleepingPacks ? fights * f.sleepChance : 0;
        if (f.maxSleepingPacks > 0) expected = Mathf.Min(expected, f.maxSleepingPacks);
        EventCard(l, set, "Sleeping packs", "sleep", f.sleepingPacks, v => f.sleepingPacks = v,
            "A fight room's enemies lie asleep: creep up for backstabs. Noise, a walking step close by or a hit wakes them, and they wake each other. Any fight room that isn't an ambush can roll it.",
            () =>
            {
                float c = Pct("Chance", f.sleepChance, 1, "% of fights");
                int most = IntRow("Most a floor", f.maxSleepingPacks, "0: no limit");
                if (!Mathf.Approximately(c, f.sleepChance) || most != f.maxSleepingPacks) EditBiome(set.owner, () => { f.sleepChance = c; f.maxSleepingPacks = Mathf.Max(0, most); });
            },
            f.sleepingPacks ? $"About {expected:0.#} a floor." : "Off: none.", Measured(survey, set, x => x.sleeping, x => x.withSleeping, "sleeping packs"));
    }

    void ShrineCard(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        var m = MeasuredFloors(survey, set);
        string warn = "";
        if (m.Count > 0 && f.shrineBargains)
        {
            float shrines = Avg(m, x => x.shrines), rest = Avg(m, x => x.rest), built = Avg(m, x => x.builtShrines);
            if (shrines < .25f) warn = $"\n<color=#e0a85a>Almost never comes: a floor has only about {rest:0.#} rest rooms and most of them are hand-built, which get no rolled shrine{(built > 0 ? $"; {built:0.##} a floor come from rooms built with a shrine" : "")}. Raise the rest rooms (fewer fights) or ask for shrines in hand-built quiet rooms.</color>";
        }
        EventCard(l, set, "Shrines", "shrine", f.shrineBargains, v => f.shrineBargains = v,
            "A shrine against a wall: bleed on it to raise the weapon in your hand (else the shield, else the body armour) one rarity. Only a rest room with no hand-built interior can roll one; rooms built with a shrine (the Shrine of Ashes) have theirs." + warn,
            () =>
            {
                float c = Pct("Chance", f.shrineChance, 1, "% of generated rest rooms");
                float price = Pct("It takes", f.shrinePrice, .9f, "% of your most health");
                if (!Mathf.Approximately(c, f.shrineChance) || !Mathf.Approximately(price, f.shrinePrice)) EditBiome(set.owner, () => { f.shrineChance = c; f.shrinePrice = Mathf.Max(.05f, price); });
            },
            null, Measured(survey, set, x => x.shrines, x => x.withShrine, "shrines"));
    }

    void TrapCard(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        var m = MeasuredFloors(survey, set);
        string estimate = m.Count > 0 && f.trappedTreasure ? $"With about {Avg(m, x => x.chests):0.#} chests a floor, about {Avg(m, x => x.chests) * f.trapChance:0.#} are trapped." : null;
        EventCard(l, set, "Trapped chests", "trap", f.trappedTreasure, v => f.trappedTreasure = v,
            "A chest on a pressure plate: opening it brings spikes up through the floor round it. Any chest can roll it but the first room's; chests built on a plate always are. Spike plates in hand-built rooms (the Spike hall) hurt the same.",
            () =>
            {
                float c = Pct("Chance", f.trapChance, 1, "% of chests, not the first room's");
                float hurt = Pct("Spikes take", f.spikeDamage, 1, "% of your most health");
                if (!Mathf.Approximately(c, f.trapChance) || !Mathf.Approximately(hurt, f.spikeDamage)) EditBiome(set.owner, () => { f.trapChance = c; f.spikeDamage = hurt; });
            },
            estimate, m.Count > 0 ? $"Measured: {Avg(m, x => x.trapped):0.##} trapped of {Avg(m, x => x.chests):0.#} chests a floor; {Share(m, x => x.withTrap) * 100:0}% of floors have one." : null);
    }

    void LightCard(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        var m = MeasuredFloors(survey, set);
        float generated = m.Count > 0 ? Avg(m, x => x.rooms - x.handBuilt - x.tavern - 2) : 0;
        string estimate = m.Count > 0 ? $"With about {generated:0} generated rooms a floor: about {generated * (f.darkRooms ? f.darkChance : 0):0.#} dark, {generated * (f.eerieRooms ? f.eerieChance : 0):0.#} green-lit." : null;
        GUILayout.Space(6);
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.Label("Dark and green-lit rooms", new GUIStyle(gold) { fontSize = StudioSkin.Size });
        GUILayout.Label("A generated room (not the first room or the stairs room) may be dark (no torches, only your own light) or green-lit. A hand-built room has its own light.", new GUIStyle(dim) { wordWrap = true });
        GUILayout.BeginHorizontal();
        bool dark = Toggle("Dark rooms", f.darkRooms, null);
        if (GUILayout.Button(new GUIContent("Try it", "A run with every generated room dark."), GUILayout.Width(70))) TryEvent(l, set, "dark");
        GUILayout.EndHorizontal();
        float dc = f.darkRooms ? Pct("Chance", f.darkChance, 1, "% of generated rooms") : f.darkChance;
        GUILayout.BeginHorizontal();
        bool eerie = Toggle("Green-lit rooms", f.eerieRooms, null);
        if (GUILayout.Button(new GUIContent("Try it", "A run with every generated room green-lit."), GUILayout.Width(70))) TryEvent(l, set, "eerie");
        GUILayout.EndHorizontal();
        float ec = f.eerieRooms ? Pct("Chance", f.eerieChance, 1, "% of generated rooms") : f.eerieChance;
        if (dark != f.darkRooms || eerie != f.eerieRooms || !Mathf.Approximately(dc, f.darkChance) || !Mathf.Approximately(ec, f.eerieChance))
            EditBiome(set.owner, () => { f.darkRooms = dark; f.eerieRooms = eerie; f.darkChance = dc; f.eerieChance = Mathf.Min(ec, 1 - dc); });
        if (estimate != null) GUILayout.Label(estimate, gold);
        if (m.Count > 0) GUILayout.Label($"Measured: {Avg(m, x => x.dark):0.#} dark and {Avg(m, x => x.eerie):0.#} green-lit rooms a floor (hand-built ones made dark or green included).", dim);
        GUILayout.EndVertical();
    }

    // The features only hand-built rooms have: a switch each, and the rooms in these floors' pools made for it.
    void BuiltForCards(AssetStudioWorld.Level l, EventSet set, RoomEventSurvey.Result survey)
    {
        var f = set.F;
        var m = MeasuredFloors(survey, set);
        var rooms = set.stages.Where(st => st.biome != null).SelectMany(st => st.biome.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>())
            .Where(c => c != null && c.template != null).Select(c => c.template).Distinct().ToList();
        Section("ROOMS BUILT FOR IT");
        GUILayout.Label("These come only from hand-built rooms made for them. Switching one off leaves those rooms out of the floor (or, for a light, builds them without it).", new GUIStyle(dim) { wordWrap = true });
        void Row(string name, string what, DungeonRoomFeatureMask need, bool on, Action<bool> set1, Func<RoomEventSurvey.Floor, float> measured)
        {
            var made = rooms.Where(r => r.TryGetComponent<DungeonRoomTemplate>(out var t) && (t.Requires() & need) != 0).ToList();
            GUILayout.BeginVertical(StudioSkin.Inset);
            GUILayout.BeginHorizontal();
            bool now = Toggle(name, on, what);
            GUILayout.FlexibleSpace();
            if (m.Count > 0) GUILayout.Label($"measured {Avg(m, measured):0.#} a floor", dim);
            GUILayout.EndHorizontal();
            GUILayout.Label(made.Count == 0 ? "No room in these floors' pools is made for it." : $"{made.Count} room{(made.Count == 1 ? "" : "s")} made for it: {string.Join(", ", made.Select(r => r.name))}.", new GUIStyle(dim) { wordWrap = true });
            if (need == DungeonRoomFeatureMask.Pit && on)
            {
                bool falls = Toggle("The player can fall in", f.playerFallsInPits, "Off: a pit's edge holds the player back like a wall. Enemies knocked over the edge fall in either way.");
                float hurt = falls ? Pct("A fall takes", f.pitFallDamage, 1, "% of your most health") : f.pitFallDamage;
                if (falls != f.playerFallsInPits || !Mathf.Approximately(hurt, f.pitFallDamage)) EditBiome(set.owner, () => { f.playerFallsInPits = falls; f.pitFallDamage = hurt; });
            }
            GUILayout.EndVertical();
            if (now != on) EditBiome(set.owner, () => set1(now));
        }
        Row("Pits and chasms", "Holes in the floor, bridges across them.", DungeonRoomFeatureMask.Pit, f.pits, v => f.pits = v, x => x.pits);
        Row("Sunken floors", "Steps down into a lower court.", DungeonRoomFeatureMask.SunkenFloor, f.sunkenFloors, v => f.sunkenFloors = v, x => x.sunken);
        Row("Archer ledges", "Raised ledges and galleries where archers stand.", DungeonRoomFeatureMask.ArcherLedge, f.archerLedges, v => f.archerLedges = v, x => x.ledges);
        Row("Candlelit rooms", "Lit by clusters of candles.", DungeonRoomFeatureMask.Candlelit, f.candlelitRooms, v => f.candlelitRooms = v, x => x.candlelit);
        Row("Moonlit rooms", "Open to the sky: a shaft of moonlight.", DungeonRoomFeatureMask.Moonlit, f.moonlitRooms, v => f.moonlitRooms = v, x => x.moonlit);
    }

    // One event's card: its switch and Try it, what it is and where it lands, its settings, the estimate and what was
    // measured.
    void EventCard(AssetStudioWorld.Level l, EventSet set, string title, string force, bool on, Action<bool> setOn, string where, Action settings, string estimate, string measured)
    {
        GUILayout.Space(6);
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        bool now = GUILayout.Toggle(on, " " + title, new GUIStyle(EditorStyles.toggle) { fontStyle = FontStyle.Bold }, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("Try it", $"A run of {l.data.name} from {set.label.ToLowerInvariant()} with {GameLauncher.ForcedName(force)} (the settings themselves are not changed)."), GUILayout.Width(70))) TryEvent(l, set, force);
        GUILayout.EndHorizontal();
        if (now != on) EditBiome(set.owner, () => setOn(now));
        GUILayout.Label(where, new GUIStyle(dim) { wordWrap = true, richText = true });
        if (on) settings();
        if (!string.IsNullOrEmpty(estimate)) GUILayout.Label(estimate, new GUIStyle(gold) { wordWrap = true });
        if (!string.IsNullOrEmpty(measured)) GUILayout.Label(measured, new GUIStyle(dim) { wordWrap = true });
        GUILayout.EndVertical();
    }

    void TryEvent(AssetStudioWorld.Level l, EventSet set, string force)
    {
        var st = set.stages.FirstOrDefault(s => s.biome != null);
        Toast(GameLauncher.PlayStage(l.data, st != null ? Mathf.Max(0, st.index) : 0, false, force));
    }

    // ── Small parts ──

    // A share drawn as a percentage: a slider, the number and its unit.
    float Pct(string label, float value, float max, string unit)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(170));
        float v = GUILayout.HorizontalSlider(value * 100, 0, max * 100, GUILayout.MinWidth(120));
        v = EditorGUILayout.DelayedFloatField((float)Math.Round(v, 1), GUILayout.Width(46));
        GUILayout.Label(unit, dim, GUILayout.Width(260));
        GUILayout.EndHorizontal();
        float result = Mathf.Clamp(v, 0, max * 100) / 100;
        return Mathf.Abs(result - value) < .0005f ? value : result;
    }

    static bool Toggle(string label, bool value, string tip) => GUILayout.Toggle(value, new GUIContent(" " + label, tip), GUILayout.ExpandWidth(false));

    int IntRow(string label, int value, string note)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(170));
        int v = EditorGUILayout.DelayedIntField(value, GUILayout.Width(46));
        GUILayout.Label(note, dim);
        GUILayout.EndHorizontal();
        return v;
    }

    // A change to a biome's settings: on Ctrl+Z, saved at once.
    void EditBiome(DungeonBiome b, Action change)
    {
        if (b == null) return;
        Undo.RecordObject(b, "Room events");
        change();
        EditorUtility.SetDirty(b);
        AssetDatabase.SaveAssetIfDirty(b);
        eventHash = null;
        Repaint();
    }

    // Fights a floor of this set, worked out from its floors' fight shares.
    static float Fights(AssetStudioWorld.Level l, EventSet set)
    {
        int middle = Mathf.Max(0, l.data.roomCount - 2);
        var shares = set.stages.Where(st => st.biome != null).Select(st => st.lastFloor > st.firstFloor ? (st.biome.encounterChance.x + st.biome.encounterChance.y) / 2 : st.biome.encounterChance.y).ToList();
        return middle * (shares.Count > 0 ? shares.Average() : .5f);
    }

    // The chance at least one of n rooms rolls it.
    static float Chance(float p, float n) => 1 - Mathf.Pow(1 - Mathf.Clamp01(p), Mathf.Max(0, n));

    static List<RoomEventSurvey.Floor> MeasuredFloors(RoomEventSurvey.Result r, AssetStudioWorld.Stage st) =>
        r == null ? new List<RoomEventSurvey.Floor>() : r.floors.Where(f => f.built > 0 && f.floor >= st.firstFloor && f.floor <= st.lastFloor).ToList();

    static List<RoomEventSurvey.Floor> MeasuredFloors(RoomEventSurvey.Result r, EventSet set) => set.stages.SelectMany(st => MeasuredFloors(r, st)).Distinct().ToList();

    static float Avg(List<RoomEventSurvey.Floor> floors, Func<RoomEventSurvey.Floor, float> value)
    {
        int built = floors.Sum(f => f.built);
        return built == 0 ? 0 : floors.Sum(f => value(f) * f.built) / built;
    }

    static float Share(List<RoomEventSurvey.Floor> floors, Func<RoomEventSurvey.Floor, int> with)
    {
        int built = floors.Sum(f => f.built);
        return built == 0 ? 0 : floors.Sum(with) / (float)built;
    }

    static string Measured(RoomEventSurvey.Result r, EventSet set, Func<RoomEventSurvey.Floor, float> value, Func<RoomEventSurvey.Floor, int> with, string what)
    {
        var m = MeasuredFloors(r, set);
        if (m.Count == 0) return null;
        return $"Measured: {Avg(m, value):0.##} {what} a floor; {Share(m, with) * 100:0}% of floors have at least one.";
    }

    // The floor page's short word on its room events, with the way to the Room events page.
    void FloorEventsSummary(AssetStudioWorld.Level l, AssetStudioWorld.Stage st)
    {
        var d = l.data;
        var b = st.biome;
        var f = b.RoomFeatures(d);
        Section("ROOM EVENTS");
        var on = new List<string>();
        if (f.ambushes && f.maxAmbushes > 0) on.Add($"ambushes {f.ambushChance * 100:0}% of fights (one a floor at most)");
        if (f.sleepingPacks) on.Add($"sleeping packs {f.sleepChance * 100:0}% of fights");
        if (f.shrineBargains) on.Add($"shrines {f.shrineChance * 100:0}% of generated rest rooms");
        if (f.trappedTreasure) on.Add($"trapped chests {f.trapChance * 100:0}%");
        if (f.darkRooms) on.Add($"dark rooms {f.darkChance * 100:0}%");
        if (f.eerieRooms) on.Add($"green-lit rooms {f.eerieChance * 100:0}%");
        GUILayout.Label((b.overrideDungeonSettings ? $"{b.name} has its own: " : $"Shared with every floor of {d.name}: ") + (on.Count > 0 ? string.Join(", ", on) + "." : "none."), new GUIStyle(dim) { wordWrap = true });
        var m = MeasuredFloors(RoomEventSurvey.Last(d), st);
        if (m.Count > 0) GUILayout.Label($"Measured here: {Avg(m, x => x.ambushes):0.#} ambushes, {Avg(m, x => x.shrines):0.##} shrines, {Avg(m, x => x.trapped):0.#} trapped chests, {Avg(m, x => x.dark):0.#} dark rooms a floor.", dim);
        if (GUILayout.Button("Change them on the Room events page", GUILayout.Width(300), GUILayout.Height(26))) GoTo("events", b.overrideDungeonSettings ? b : d.baseBiome);
    }
}
