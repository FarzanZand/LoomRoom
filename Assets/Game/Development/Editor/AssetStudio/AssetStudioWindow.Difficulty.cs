using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// How hard a level's run gets, in one place (the user, 2026-10-30). The Levels page's Balance tab: a line saying how far
// the run climbs, a chart of each floor's damage and health multiplier (the stages named over it, a stage that starts
// deeper marked with its jump, boss floors in red, an elite's reach as a pale tick; hovering a floor reads it out), each
// stage's jump and elites in one table, the balance's growth numbers, then the floors one by one (a typical enemy's and
// an elite's numbers). Hovering a floor in the chart or the table lights it in both. Everything fits the page's width
// (pageWidth). The Dungeon view's Difficulty page shares the growth numbers and the floor table; the Run tab's stage strip
// edits each stage's jump and elites in place too.
public partial class AssetStudioWindow
{
    static readonly Color DamageBar = new(.72f, .565f, .24f), HealthBar = new(.75f, .3f, .27f), EliteTick = new(.95f, .9f, .78f);
    static readonly Color ChartBack = new(.17f, .17f, .18f), ChartLine = new(.3f, .3f, .31f), StageBand = new(1f, 1f, 1f, .03f);
    static readonly Color RowHover = new(.72f, .565f, .24f, .14f), RowBoss = new(.89f, .29f, .29f, .08f), RowEven = new(1f, 1f, 1f, .025f);

    float pageWidth = 600;       // the middle page's width inside its frame and scroll bar (set as each page is drawn)
    int hoverFloor, hoverFrom;   // the floor under the mouse (0: none) and where it is: 1 the chart, 2 the floor table

    // The mouse over a floor in one of them, or off it (floor 0); repaints when it changes.
    void HoverFloor(int floor, int from)
    {
        if (floor == 0 && hoverFrom != from) return;   // leaving one doesn't clear the other's
        if (floor == hoverFloor && (floor == 0 || hoverFrom == from)) return;
        hoverFloor = floor; hoverFrom = floor == 0 ? 0 : from;
        Repaint();
    }

    // One floor of a run as the game grows it.
    struct FloorDifficulty
    {
        public int floor, floorInStage;
        public AssetStudioWorld.Stage stage;
        public float depth, damage, health, armour;   // the balance's multipliers there (armour: added)
        public bool firstOfStage, bossFloor;
        public float step;                             // the Extra floors the stage starting here adds (0 elsewhere)
    }

    List<FloorDifficulty> RunDifficulty(AssetStudioWorld.Level l)
    {
        var d = l.data; var bal = d.balance;
        var list = new List<FloorDifficulty>();
        foreach (var st in l.RunStages)
            for (int f = st.firstFloor; f <= st.lastFloor; f++)
            {
                float depth = DungeonBalance.Depth(f, d.ExtraFloors(f));
                var stage = StageOf(d, st);
                list.Add(new FloorDifficulty
                {
                    floor = f, floorInStage = f - st.firstFloor + 1, stage = st, depth = depth,
                    damage = bal != null ? bal.damageMultiplier * DungeonBalance.Grown(bal.damageGrowthPerFloor, depth) : 1f,
                    health = bal != null ? bal.healthMultiplier * DungeonBalance.Grown(bal.healthGrowthPerFloor, depth) : 1f,
                    armour = bal != null ? bal.armorPerFloor * depth : 0f,
                    firstOfStage = f == st.firstFloor, bossFloor = f == st.lastFloor && st.boss && AssetStudioWorld.HasGuardian(st.biome),
                    step = f == st.firstFloor && stage != null ? Mathf.Max(0f, stage.extraFloors) : 0f,
                });
            }
        return list;
    }

    static TableLevelData.BiomeStage StageOf(TableLevelData d, AssetStudioWorld.Stage st) =>
        st != null && !st.IsBase && d.stages != null && st.index < d.stages.Length ? d.stages[st.index] : null;

    // An elite's multiplier on that floor (damage: a Brutal one's, health: a Tough one's); 0 where the biome has none.
    static float EliteMultiplier(DungeonBalance bal, FloorDifficulty f, bool damage)
    {
        var b = f.stage.biome;
        if (bal == null || b == null || b.eliteChance <= 0f) return 0f;
        float depth = f.depth + Mathf.Max(0f, b.eliteFloors);
        return damage ? bal.damageMultiplier * DungeonBalance.Grown(bal.damageGrowthPerFloor, depth) * (1f + EliteEnemy.BrutalDamage)
                      : bal.healthMultiplier * DungeonBalance.Grown(bal.healthGrowthPerFloor, depth) * (1f + EliteEnemy.ToughHealth);
    }

    GUIStyle Wrapped => new(dim) { wordWrap = true };
    GUIStyle Unit => new(dim) { fixedHeight = 18, alignment = TextAnchor.MiddleLeft };   // a word beside a number field

    // The levels page's Balance tab.
    void BalanceTab(AssetStudioWorld.Level l)
    {
        var d = l.data;
        ReadGameWide();
        var floors = RunDifficulty(l);
        Section("HOW HARD THE RUN GETS");
        GUILayout.Label(Summary(floors), new GUIStyle(StudioSkin.Label) { richText = true }, GUILayout.Width(pageWidth));
        GUILayout.Space(4);
        DifficultyChart(l, floors);
        Section("STAGES");
        GUILayout.Label("A stage can start deeper than the floor before it (a new biome after a boss): its enemies then grow as if that many floors further down, and the jump carries on through the run. Elites are each biome's.", Wrapped, GUILayout.Width(pageWidth));
        StageSteps(l);
        Section("GROWTH");
        GrowthFields(d);
        Section("FLOOR BY FLOOR");
        FloorTable(l, floors);
        Section("GEAR BY FLOOR");
        Fields(d, "gearByFloor", "startingChest");
    }

    // One line: how far the run climbs and where its biggest jump is.
    static string Summary(List<FloorDifficulty> floors)
    {
        if (floors.Count == 0) return "No floors yet: give the run a stage on the Run tab.";
        var first = floors[0]; var last = floors[floors.Count - 1];
        string line = floors.Count == 1
            ? $"One floor: enemies hit <color=#d8aa48>x{first.damage:0.0#}</color> and have <color=#d8665e>x{first.health:0.0#}</color> health."
            : $"From floor 1 to floor {last.floor} enemies go from <color=#d8aa48>x{first.damage:0.0#}</color> to <color=#d8aa48>x{last.damage:0.0#}</color> damage and from <color=#d8665e>x{first.health:0.0#}</color> to <color=#d8665e>x{last.health:0.0#}</color> health.";
        float jump = 0; int at = 0;
        for (int i = 1; i < floors.Count; i++)
        {
            float rise = floors[i].damage / Mathf.Max(.001f, floors[i - 1].damage) - 1f;
            if (rise > jump + .001f) { jump = rise; at = i; }
        }
        if (at > 0 && floors[at].step > 0) line += $" Biggest jump: floor {floors[at].floor} ({floors[at].stage.biome?.name}), <color=#e0a85a>+{jump:0%}</color> damage over the floor before.";
        return line;
    }

    // Each stage's jump and its biome's elites, edited here.
    void StageSteps(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var run = l.RunStages;
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Stage", gold, GUILayout.Width(170));
        GUILayout.Label(new GUIContent("Starts deeper", "Extra floors: from this stage's first floor on, enemies grow as if this many floors deeper (added up with earlier stages'). 0: the usual one floor's step."), gold, GUILayout.Width(120));
        GUILayout.Label(new GUIContent("Elites", "How often a generated enemy on the biome's floors is an elite."), gold, GUILayout.Width(80));
        GUILayout.Label(new GUIContent("Elites grow", "How many floors deeper an elite grows and rolls its loot."), gold, GUILayout.Width(110));
        GUILayout.EndHorizontal();
        foreach (var st in run)
        {
            var b = st.biome;
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            string floors = st.IsBase && l.Staged ? "every floor" : st.lastFloor > st.firstFloor ? $"F{st.firstFloor}-{st.lastFloor}" : $"F{st.firstFloor}";
            if (GUILayout.Button($"<color=#b8ae9c>{floors}</color>  {(b != null ? b.name : "<color=#e0a85a>no biome</color>")}", cell, GUILayout.Width(170), GUILayout.Height(22)) && b != null) ShowAsset(b);
            var stage = StageOf(d, st);
            if (stage != null)
            {
                float extra = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(stage.extraFloors, GUILayout.Width(44)));
                GUILayout.Label(stage.extraFloors > 0 ? "<color=#e0a85a>floors</color>" : "floors", Unit, GUILayout.Width(72));
                if (!Mathf.Approximately(extra, stage.extraFloors)) { Undo.RecordObject(d, "Extra floors"); stage.extraFloors = extra; EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d); }
            }
            else GUILayout.Label("-", dim, GUILayout.Width(120));
            if (b != null)
            {
                float chance = Mathf.Clamp(EditorGUILayout.DelayedFloatField(Mathf.Round(b.eliteChance * 1000f) / 10f, GUILayout.Width(44)), 0f, 50f) / 100f;
                GUILayout.Label("%", Unit, GUILayout.Width(32));
                float grow = b.eliteFloors;
                if (b.eliteChance > 0f) { grow = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(b.eliteFloors, GUILayout.Width(44))); GUILayout.Label("floors", Unit, GUILayout.Width(60)); }
                else GUILayout.Label("no elites", Unit, GUILayout.Width(108));
                if (!Mathf.Approximately(chance, b.eliteChance) || !Mathf.Approximately(grow, b.eliteFloors))
                { Undo.RecordObject(b, "Elites"); b.eliteChance = chance; b.eliteFloors = grow; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
            }
            GUILayout.EndHorizontal();
        }
        var shared = run.Where(s => s.biome != null).GroupBy(s => s.biome).Where(g => g.Count() > 1 || World.LevelsOf(g.Key).Count(x => !x.tool) > 1).Select(g => g.Key.name).ToList();
        if (shared.Count > 0) GUILayout.Label($"<color=#e0a85a>{string.Join(", ", shared)} {(shared.Count == 1 ? "is" : "are")} run elsewhere too: its elites change there as well.</color>", Wrapped);
        GUILayout.EndVertical();
    }

    // The balance numbers people change. The asset is shared by every level that uses it.
    void GrowthFields(TableLevelData d)
    {
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Balance asset", dim, GUILayout.Width(110));
        Fields(d, "balance");
        GUILayout.EndHorizontal();
        var bal = d.balance;
        if (bal == null) { GUILayout.Label("<color=#e0a85a>No balance asset: its enemies stay as authored on every floor.</color>", Wrapped); GUILayout.EndVertical(); return; }
        var users = World.levels.Where(x => !x.tool && x.data.balance == bal && x.data != d).ToList();
        if (users.Count > 0) GUILayout.Label($"<color=#e0a85a>Shared with {string.Join(", ", users.Select(u => u.data.name))}: a change here shows there too.</color>", Wrapped);
        GUILayout.Space(4);
        bool sideBySide = pageWidth >= 620;
        using (var so = new SerializedObject(bal))
        {
            so.Update();
            if (sideBySide) GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(sideBySide ? 290 : pageWidth - 24));
            GUILayout.Label("Each floor down", gold);
            Percent(so, "damageGrowthPerFloor", "Damage", "Each floor's enemies hit this much harder than the floor before's, compounded.");
            Percent(so, "healthGrowthPerFloor", "Health", "Each floor's enemies have this much more health than the floor before's, compounded.");
            Number(so, "armorPerFloor", "Armour", "Armour added for every floor of depth (not compounded).", "added");
            GUILayout.EndVertical();
            if (sideBySide) GUILayout.Space(12);
            GUILayout.BeginVertical(GUILayout.Width(sideBySide ? 290 : pageWidth - 24));
            GUILayout.Label("On every floor", gold);
            Number(so, "damageMultiplier", "Damage", "Every enemy's damage before the growth. 1 = as authored.", "times");
            Number(so, "healthMultiplier", "Health", "Every enemy's health before the growth. 1 = as authored. Not the boss.", "times");
            Number(so, "bossHealthMultiplier", "Boss health", "The boss's health, with no per-floor growth (it is tuned for its own floor).", "times");
            var separate = so.FindProperty("separateBossDamage");
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Boss damage", separate.tooltip), GUILayout.Width(110));
            if (separate.boolValue)
            {
                var boss = so.FindProperty("bossDamageMultiplier");
                boss.floatValue = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(boss.floatValue, GUILayout.Width(56)));
                GUILayout.Label("times", Unit, GUILayout.Width(44));
                if (GUILayout.Button(new GUIContent("as floors", "Let the boss's damage grow with the floors again."), GUILayout.Width(80))) separate.boolValue = false;
            }
            else
            {
                GUILayout.Label("grows with the floors", Unit, GUILayout.Width(150));
                if (GUILayout.Button(new GUIContent("set", "Give the boss a damage multiplier of its own."), GUILayout.Width(44))) separate.boolValue = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if (sideBySide) GUILayout.EndHorizontal();
            if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(bal);
        }
        if (!Mathf.Approximately(wideDamage, 1f) || !Mathf.Approximately(wideHealth, 1f))
            GUILayout.Label($"ProgressionManager > Balance multiplies every enemy on top: damage x{wideDamage:0.##}, health x{wideHealth:0.##}. The enemies' numbers below count it; the multipliers above don't.", Wrapped);
        GUILayout.EndVertical();
    }

    void Percent(SerializedObject so, string name, string label, string tip)
    {
        var p = so.FindProperty(name); if (p == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(label, tip), GUILayout.Width(110));
        float v = EditorGUILayout.DelayedFloatField(Mathf.Round(p.floatValue * 1000f) / 10f, GUILayout.Width(56));
        GUILayout.Label("% more", Unit, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        p.floatValue = Mathf.Clamp(v / 100f, 0f, 1f);
    }

    void Number(SerializedObject so, string name, string label, string tip, string unit)
    {
        var p = so.FindProperty(name); if (p == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(label, tip), GUILayout.Width(110));
        p.floatValue = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(p.floatValue, GUILayout.Width(56)));
        GUILayout.Label(unit, Unit, GUILayout.Width(60));
        GUILayout.EndHorizontal();
    }

    // Two bars a floor (damage, health) on one scale, the stages named over their floors, a line and the jump where a
    // stage starts deeper, the boss floors in red, and an elite's reach as a pale outline over each bar.
    void DifficultyChart(AssetStudioWorld.Level l, List<FloorDifficulty> floors)
    {
        var bal = l.data.balance;
        var r = GUILayoutUtility.GetRect(pageWidth, 210, GUILayout.Width(pageWidth), GUILayout.Height(210));
        var ev = Event.current;
        if (floors.Count == 0) return;
        var plot = new Rect(r.x + 40, r.y + 26, r.width - 50, r.height - 62);
        float slot = plot.width / floors.Count, bar = Mathf.Clamp(slot * .3f, 4f, 20f);
        int under = r.Contains(ev.mousePosition) && ev.mousePosition.x >= plot.x ? Mathf.Clamp(Mathf.FloorToInt((ev.mousePosition.x - plot.x) / slot), 0, floors.Count - 1) : -1;
        if (ev.type == EventType.MouseMove) HoverFloor(under >= 0 ? floors[under].floor : 0, 1);
        if (ev.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, ChartBack);
        float top = Mathf.Max(1.2f, floors.Max(f => Mathf.Max(Mathf.Max(f.damage, f.health), Mathf.Max(EliteMultiplier(bal, f, true), EliteMultiplier(bal, f, false)))) * 1.08f);
        float step = top > 8 ? 2f : top > 3 ? 1f : .5f;
        var small = new GUIStyle(dimLine) { fontSize = Mathf.Max(10, StudioSkin.Size - 3), alignment = TextAnchor.MiddleCenter, richText = true };
        for (float m = 0; m <= top; m += step)
        {
            float y = plot.yMax - plot.height * m / top;
            EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), ChartLine);
            GUI.Label(new Rect(r.x + 2, y - 8, 34, 16), "x" + m.ToString("0.#"), new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
        }
        for (int i = 0; i < floors.Count; i++)
        {
            var f = floors[i];
            float x = plot.x + slot * i;
            int count = f.stage.lastFloor - f.stage.firstFloor + 1;
            if (f.firstOfStage)
            {
                if ((l.RunStages.IndexOf(f.stage) & 1) == 1) EditorGUI.DrawRect(new Rect(x, r.y + 2, slot * count, plot.yMax - r.y - 2), StageBand);
                if (i > 0) EditorGUI.DrawRect(new Rect(x, r.y + 2, f.step > 0 ? 2 : 1, plot.yMax - r.y - 2), f.step > 0 ? new Color(.88f, .66f, .35f, .8f) : new Color(.5f, .48f, .44f));
                string name = f.stage.biome != null ? f.stage.biome.name : "no biome";
                GUI.Label(new Rect(x + 3, r.y + 3, slot * count - 6, 16), name, new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip });
                if (f.step > 0) GUI.Label(new Rect(x + 3, r.y + 15, slot * count - 6, 14), $"<color=#e0a85a>+{f.step:0.#} deeper</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            }
            if (f.floor == hoverFloor) EditorGUI.DrawRect(new Rect(x + 1, plot.y, slot - 2, plot.height), RowHover);
            float cx = x + slot / 2f;
            float hd = plot.height * f.damage / top, hh = plot.height * f.health / top;
            EditorGUI.DrawRect(new Rect(cx - bar - 1, plot.yMax - hd, bar, hd), DamageBar);
            EditorGUI.DrawRect(new Rect(cx + 1, plot.yMax - hh, bar, hh), HealthBar);
            void Ghost(float m, float left)
            {
                if (m <= 0f) return;
                float h = plot.height * m / top, y = plot.yMax - h;
                var c = new Color(EliteTick.r, EliteTick.g, EliteTick.b, .55f);
                EditorGUI.DrawRect(new Rect(left, y, bar, 2), EliteTick);
                EditorGUI.DrawRect(new Rect(left, y, 1, h), c);
                EditorGUI.DrawRect(new Rect(left + bar - 1, y, 1, h), c);
            }
            Ghost(EliteMultiplier(bal, f, true), cx - bar - 1);
            Ghost(EliteMultiplier(bal, f, false), cx + 1);
            GUI.Label(new Rect(x, plot.yMax + 2, slot, 16), f.bossFloor ? (slot >= 46 ? $"<color=#e24b4a>{f.floor} boss</color>" : $"<color=#e24b4a>{f.floor}</color>") : f.floor.ToString(), small);
        }
        // the key, or what the floor under the mouse has
        var foot = new Rect(r.x + 8, r.yMax - 18, r.width - 16, 16);
        var picked = floors.FirstOrDefault(f => f.floor == hoverFloor);
        if (hoverFloor > 0 && picked.stage != null)
        {
            float ed = EliteMultiplier(bal, picked, true), eh = EliteMultiplier(bal, picked, false);
            string elite = ed > 0 ? $"   an elite up to <color=#f2e6c7>x{ed:0.0#} / x{eh:0.0#}</color>" : "";
            GUI.Label(foot, $"Floor {picked.floor} · {picked.stage.biome?.name} · counts as {picked.depth:0.#} floors down:  <color=#d8aa48>x{picked.damage:0.0#} damage</color>   <color=#d8665e>x{picked.health:0.0#} health</color>   +{picked.armour:0.#} armour{elite}", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
        }
        else GUI.Label(foot, "<color=#b8903d>■</color> damage   <color=#c04d45>■</color> health   <color=#f2e6c7>▭</color> as far as an elite goes (a Brutal one's damage, a Tough one's health)   hover a floor for its numbers", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
    }

    // Each floor: its stage, the multipliers, a typical enemy there (the floor's enemies averaged by weight) and the
    // strongest elite it can bring (a Tough one's health, a Brutal one's damage). Columns drop out on a narrow page.
    void FloorTable(AssetStudioWorld.Level l) => FloorTable(l, RunDifficulty(l));

    void FloorTable(AssetStudioWorld.Level l, List<FloorDifficulty> floors)
    {
        var d = l.data;
        if (floors.Count == 0) { GUILayout.Label("No floors to show: give the run a stage on the Run tab.", dim); return; }
        var head = new GUIStyle(gold) { fontSize = StudioSkin.Size - 2 };
        var line = new GUIStyle(StudioSkin.Label) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
        float inner = pageWidth - 24;
        bool wide = inner >= 760;
        float wFloor = 58, wNum = 62, wEnemy = 140, wElite = 192, wDepth = wide ? 54 : 0, wArm = wide ? 58 : 0;
        float wStage = Mathf.Max(90, inner - wFloor - wNum * 2 - wEnemy - wElite - wDepth - wArm - 8);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Floor", head, GUILayout.Width(wFloor));
        GUILayout.Label("Stage", head, GUILayout.Width(wStage));
        if (wide) GUILayout.Label(new GUIContent("Depth", "How many floors of growth it counts as: the floors above it plus every stage's jump so far."), head, GUILayout.Width(wDepth));
        GUILayout.Label("Damage", head, GUILayout.Width(wNum));
        GUILayout.Label("Health", head, GUILayout.Width(wNum));
        if (wide) GUILayout.Label("Armour", head, GUILayout.Width(wArm));
        GUILayout.Label(new GUIContent("Typical enemy", "The floor's enemies averaged by how common they are."), head, GUILayout.Width(wEnemy));
        GUILayout.Label(new GUIContent("Elite", "How often, then the most an elite has: a Tough one's health, a Brutal one's damage."), head, GUILayout.Width(wElite));
        GUILayout.EndHorizontal();
        int row = 0; bool overRow = false;
        foreach (var f in floors)
        {
            var b = f.stage.biome;
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, f.floor == hoverFloor ? RowHover : f.bossFloor ? RowBoss : (row & 1) == 0 ? RowEven : Color.clear);
            if (Event.current.type == EventType.MouseMove && rowRect.Contains(Event.current.mousePosition)) { HoverFloor(f.floor, 2); overRow = true; }
            GUILayout.Label(f.bossFloor ? $"{f.floor}  <color=#e24b4a>boss</color>" : f.floor.ToString(), line, GUILayout.Width(wFloor));
            string stage = f.firstOfStage ? (b != null ? b.name : "<color=#e0a85a>no biome</color>") + (f.step > 0 ? $"  <color=#e0a85a>+{f.step:0.#} deeper</color>" : "") : $"<color=#5d5a55>{(b != null ? b.name : "")} {f.floorInStage}</color>";
            GUILayout.Label(stage, line, GUILayout.Width(wStage));
            if (wide) GUILayout.Label(f.depth.ToString("0.#"), line, GUILayout.Width(wDepth));
            GUILayout.Label($"<color=#d8aa48>x{f.damage:0.00}</color>", line, GUILayout.Width(wNum));
            GUILayout.Label($"<color=#d8665e>x{f.health:0.00}</color>", line, GUILayout.Width(wNum));
            if (wide) GUILayout.Label("+" + f.armour.ToString("0.#"), line, GUILayout.Width(wArm));
            var typical = Typical(d, b, f, 0f);
            GUILayout.Label(typical.weight > 0 ? $"{typical.health:0} hp · {typical.damage:0.#} dmg" : "<color=#5d5a55>no enemies</color>", line, GUILayout.Width(wEnemy));
            if (b != null && b.eliteChance > 0f && typical.weight > 0)
            {
                var elite = Typical(d, b, f, Mathf.Max(0f, b.eliteFloors));
                GUILayout.Label($"<color=#b8ae9c>{b.eliteChance:0%}</color> · up to {elite.health * (1f + EliteEnemy.ToughHealth):0} hp · {elite.damage * (1f + EliteEnemy.BrutalDamage):0.#} dmg", line, GUILayout.Width(wElite));
            }
            else GUILayout.Label("<color=#5d5a55>none</color>", line, GUILayout.Width(wElite));
            EditorGUILayout.EndHorizontal();
            row++;
            if (f.bossFloor && b != null && b.guardian != null && b.guardian.boss != null)
            {
                var boss = NumbersAt(d, b.guardian.boss, f.floor, b.guardian);
                var bossRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(bossRect, RowBoss);
                GUILayout.Label("", line, GUILayout.Width(wFloor));
                GUILayout.Label($"<color=#e24b4a>{b.guardian.boss.name}</color>", line, GUILayout.Width(wStage + (wide ? wDepth : 0) + wNum * 2 + (wide ? wArm : 0) + 12));
                GUILayout.Label($"<color=#e24b4a>{boss.health:0} hp · {boss.damage:0.#} dmg</color>", line, GUILayout.Width(wEnemy + wElite));
                EditorGUILayout.EndHorizontal();
            }
        }
        if (Event.current.type == EventType.MouseMove && !overRow) HoverFloor(0, 2);
        GUILayout.Space(4);
        GUILayout.Label($"An elite grows its biome's Elite floors deeper, and has one trait: Tough +{EliteEnemy.ToughHealth:0%} health, Brutal +{EliteEnemy.BrutalDamage:0%} damage or Swift {EliteEnemy.SwiftAttackSpeed:0%} faster swings. It is worth x{EliteEnemy.Experience:0} experience and its loot rolls as if that much deeper." + (wide ? "" : " Widen the window for each floor's depth and armour."), Wrapped);
        GUILayout.EndVertical();
    }

    // The floor's enemies (those that come by this floor of the stage) averaged by weight, grown deeper floors more.
    (float health, float damage, float weight) Typical(TableLevelData d, DungeonBiome b, FloorDifficulty f, float deeper)
    {
        if (b == null || b.enemies == null) return (0, 0, 0);
        float hp = 0, dmg = 0, w = 0;
        foreach (var s in b.enemies)
        {
            if (s == null || s.enemy == null || s.weight <= 0 || s.fromFloor > f.floorInStage) continue;
            var at = NumbersAt(d, s.enemy, f.floor, null, deeper);
            hp += at.health * s.weight; dmg += at.damage * s.weight; w += s.weight;
        }
        return w > 0 ? (hp / w, dmg / w, w) : (0, 0, 0);
    }

    // The Run tab's stage strip, under each stage: its jump and its biome's elites, edited in place.
    void StageStepRow(TableLevelData d, AssetStudioWorld.Stage st)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        var stage = StageOf(d, st);
        if (stage != null)
        {
            float extra = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(stage.extraFloors, GUILayout.Width(32)));
            GUILayout.Label(new GUIContent(stage.extraFloors > 0 ? "<color=#e0a85a>deeper</color>" : "deeper", "Extra floors: from this stage's first floor on, enemies grow as if this many floors deeper (added up with earlier stages'). The Balance tab shows the run."), Unit, GUILayout.Width(54));
            if (!Mathf.Approximately(extra, stage.extraFloors)) { Undo.RecordObject(d, "Extra floors"); stage.extraFloors = extra; EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d); }
        }
        if (st.biome != null)
        {
            var b = st.biome;
            float chance = Mathf.Clamp(EditorGUILayout.DelayedFloatField(Mathf.Round(b.eliteChance * 1000f) / 10f, GUILayout.Width(32)), 0f, 50f) / 100f;
            GUILayout.Label(new GUIContent("% elites", "How often a generated enemy on this biome's floors is an elite (the biome's, shared wherever it runs). The Balance tab shows what one has."), Unit, GUILayout.Width(60));
            if (!Mathf.Approximately(chance, b.eliteChance)) { Undo.RecordObject(b, "Elite chance"); b.eliteChance = chance; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
        }
        GUILayout.EndHorizontal();
    }
}
