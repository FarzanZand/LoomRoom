using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Dungeon section's first page, the run as a timeline (the user, 2026-10-30: design the experience from the first
// floor to the last in one place). One column per floor, floor 1 at the left, grouped under the run's dungeon acts
// (Dungeon1: the Cellars, the Realms, the Ruins); each act's header edits its Difficulty jump (its first stage's) and its
// elite chance (every biome in it). Over the columns the difficulty chart, one floor to a column. Each column, top to
// bottom: the floor's biome and look, its difficulty (damage and health multipliers, the jump on the act's first floor),
// its enemies there (health and damage on that floor, weight edited in place, pack size), its rooms (the pool, event
// rooms, the tavern, the boss and its room; a click opens the floor in World's Rooms tab), its loot (the Gear by floor
// row), what the Dungeon Master says entering it (the biome's entry lines, edited in place) and Play from this floor.
public partial class AssetStudioWindow
{
    const float TimelineColumn = 236;
    Vector2 timelineScroll;

    void TimelinePage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var place = l.places.FirstOrDefault();
        PageHeader("The run", $"{(place != null ? place.displayName + " · " : "")}{d.name} · {d.FloorCount} floor{(d.FloorCount == 1 ? "" : "s")}, floor 1 to the last. Each column is a floor; edit it right there.",
            ("From the table", "As a player starts it: the room, the table, the map, the build.", () => Toast(GameLauncher.PlayFromTable(place))));
        foreach (var p in l.problems) GUILayout.Label("<color=#e0a85a>• " + p + "</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        var floors = RunDifficulty(l);
        if (floors.Count == 0) { GUILayout.Label("No floors yet: give the run a stage on its level page.", dim); return; }
        GUILayout.Label(Summary(floors), new GUIStyle(StudioSkin.Label) { richText = true }, GUILayout.Width(pageWidth));
        GUILayout.Space(6);

        // Everything below scrolls sideways together, one column a floor.
        float width = 40 + 10 + floors.Count * TimelineColumn;
        timelineScroll = GUILayout.BeginScrollView(timelineScroll, true, false, GUILayout.Height(Mathf.Max(400, position.height - 250)));
        try
        {
            ActHeaders(l, floors);
            GUILayout.Space(4);
            float keep = pageWidth;
            pageWidth = width;
            DifficultyChart(l, floors, false);
            pageWidth = keep;
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Space(40);
            foreach (var f in floors) FloorColumn(l, f);
            GUILayout.EndHorizontal();
        }
        finally { GUILayout.EndScrollView(); }
    }

    // One header per dungeon act over its floors: its name, its floors, its Difficulty jump and its elites.
    void ActHeaders(AssetStudioWorld.Level l, List<FloorDifficulty> floors)
    {
        var d = l.data;
        GUILayout.BeginHorizontal();
        GUILayout.Space(40);
        for (int i = 0; i < floors.Count;)
        {
            string act = DungeonAct(floors[i].stage.biome);
            int span = 1; while (i + span < floors.Count && DungeonAct(floors[i + span].stage.biome) == act) span++;
            var first = floors[i];
            var biomes = floors.Skip(i).Take(span).Select(f => f.stage.biome).Where(b => b != null).Distinct().ToList();
            GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(span * TimelineColumn - 6));
            GUILayout.Label($"{act.ToUpperInvariant()}  <color=#b8ae9c>floor{(span > 1 ? $"s {first.floor}-{first.floor + span - 1}" : " " + first.floor)}</color>", new GUIStyle(gold) { richText = true });
            GUILayout.BeginHorizontal();
            var stage = StageOf(d, first.stage);
            if (stage != null)
            {
                GUILayout.Label(new GUIContent("Difficulty jump", "Its enemies grow as if this many floors deeper from its first floor on (added up with earlier acts'): a jump in difficulty, in floors' worth of growth."), Unit, GUILayout.Width(104));
                float jump = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(stage.extraFloors, GUILayout.Width(36)));
                if (!Mathf.Approximately(jump, stage.extraFloors)) { Undo.RecordObject(d, "Difficulty jump"); stage.extraFloors = jump; EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d); }
                GUILayout.Space(10);
            }
            if (biomes.Count > 0)
            {
                // one number for the act; floors that differ show their range, and typing one sets them all
                float lo = biomes.Min(b => b.eliteChance), hi = biomes.Max(b => b.eliteChance);
                GUILayout.Label(new GUIContent("Elites", "How often a generated enemy is an elite, on every floor of this act (each biome's own; shared wherever the biome runs). Typing a number sets it on all of them."), Unit, GUILayout.Width(44));
                float shown = Mathf.Round(hi * 1000f) / 10f;
                float typed = EditorGUILayout.DelayedFloatField(shown, GUILayout.Width(36));
                GUILayout.Label(Mathf.Approximately(lo, hi) ? "%" : $"%  (its floors {lo:0%}-{hi:0%})", Unit, GUILayout.Width(Mathf.Approximately(lo, hi) ? 16 : 130));
                if (!Mathf.Approximately(typed, shown) && typed >= 0f)
                {
                    float chance = Mathf.Clamp(typed, 0f, 50f) / 100f;
                    foreach (var b in biomes) { Undo.RecordObject(b, "Elite chance"); b.eliteChance = chance; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
            i += span;
        }
        GUILayout.EndHorizontal();
    }

    // One floor, top to bottom: look, difficulty, enemies, rooms, loot, words, play.
    void FloorColumn(AssetStudioWorld.Level l, FloorDifficulty f)
    {
        var d = l.data;
        var b = f.stage.biome;
        float inner = TimelineColumn - 26;
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(TimelineColumn - 6));
        // the floor and its look
        GUILayout.BeginHorizontal();
        GUILayout.Label($"FLOOR {f.floor}" + (f.bossFloor ? "  <color=#e24b4a>BOSS</color>" : ""), new GUIStyle(gold) { richText = true });
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (b == null) { GUILayout.Label("<color=#e0a85a>no biome</color>", dim); GUILayout.EndVertical(); GUILayout.Space(6); return; }
        if (GUILayout.Button(b.name + (f.stage.lastFloor > f.stage.firstFloor ? $"  <color=#b8ae9c>its floor {f.floorInStage}</color>" : ""), cell, GUILayout.Height(20))) ShowAsset(b);
        var style = b.roomStyles != null && b.roomStyles.FirstOrDefault(c => c != null && c.style != null) is DungeonStyleChoice c0 ? c0.style : d.defaultStyle;
        var pic = GUILayoutUtility.GetRect(inner, 70, GUILayout.Width(inner), GUILayout.Height(70));
        var thumb = StyleThumb(style);
        if (Event.current.type == EventType.Repaint) { if (thumb != null) GUI.DrawTexture(pic, thumb, ScaleMode.ScaleAndCrop); else EditorGUI.DrawRect(pic, new Color(.1f, .1f, .12f)); }
        if (style != null && Event.current.type == EventType.MouseDown && pic.Contains(Event.current.mousePosition)) { ShowAsset(style); Event.current.Use(); }
        GUILayout.Label($"{(b.IsBoard ? "board" : "walled")} · {ObjectNames.NicifyVariableName(b.lighting.ToString())} lighting" + (style != null ? " · " + style.name : ""), new GUIStyle(dimLine) { clipping = TextClipping.Clip }, GUILayout.Width(inner));

        // difficulty
        TimelineSection("DIFFICULTY");
        GUILayout.Label($"<color=#d8aa48>x{f.damage:0.00} damage</color>   <color=#d8665e>x{f.health:0.00} health</color>", new GUIStyle(StudioSkin.Label) { richText = true, wordWrap = false });
        GUILayout.Label($"counts as {f.depth:0.#} floors down · +{f.armour:0.#} armour" + (f.step > 0 ? $"  <color=#e0a85a>jump +{f.step:0.#}</color>" : ""), new GUIStyle(dim) { richText = true, wordWrap = true }, GUILayout.Width(inner));

        // enemies
        TimelineSection("ENEMIES");
        var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
        float total = spawns.Where(s => s != null && s.enemy != null && s.fromFloor <= f.floorInStage).Sum(s => Mathf.Max(0f, s.weight));
        foreach (var s in spawns)
        {
            if (s == null || s.enemy == null) continue;
            bool here = s.fromFloor <= f.floorInStage;
            var at = NumbersAt(d, s.enemy, f.floor, null);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(here ? s.enemy.name : $"<color=#6d6860>{s.enemy.name}</color>", cell, GUILayout.Width(100), GUILayout.Height(18))) ShowAsset(DataOf(s.enemy) != null ? (UnityEngine.Object)DataOf(s.enemy) : s.enemy);
            GUILayout.Label(here ? $"{at.health:0}·{at.damage:0.#}" : $"from {s.fromFloor}", new GUIStyle(dimLine) { richText = true }, GUILayout.Width(56));
            float w = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(s.weight, GUILayout.Width(34)));
            GUILayout.Label(here && total > 0 ? $"{s.weight / total:0%}" : "", dimLine, GUILayout.Width(30));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(w, s.weight)) { AssetStudioWorld.SetEnemy(b, Array.IndexOf(spawns, s), w, s.fromFloor); RefreshWorld(); }
        }
        if (spawns.Length == 0) GUILayout.Label("<color=#e0a85a>nothing lives here</color>", new GUIStyle(dim) { richText = true });
        GUILayout.Label($"hp·dmg on this floor · {b.minEnemiesPerRoom}-{b.maxEnemiesPerRoom} a room · {b.eliteChance:0%} elites", new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(inner));

        // rooms
        TimelineSection("ROOMS");
        var pool = b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>();
        int poolHere = pool.Count(c => c != null && c.template != null && c.fromFloor <= f.floorInStage);
        int events = pool.Count(c => c != null && c.template != null && c.fromFloor <= f.floorInStage && IsEventRoom(c.template));
        var lines = new List<string> { $"{poolHere} hand-built room{(poolHere == 1 ? "" : "s")} to draw from ({events} event{(events == 1 ? "" : "s")}), {b.templateChance:0%} of rooms", EventsLine(d) };
        if (d.shopRoom != null && b.merchantChance > 0f) lines.Add(b.merchantChance >= 1f ? "the tavern" : $"the tavern, {b.merchantChance:0%} of floors");
        if (f.bossFloor) lines.Add($"<color=#e24b4a>{b.guardian.boss.name}</color>" + (b.guardian.arena != null ? " in " + b.guardian.arena.name : ""));
        GUILayout.Label(string.Join("\n", lines), new GUIStyle(dim) { richText = true, wordWrap = true }, GUILayout.Width(inner));
        if (GUILayout.Button(new GUIContent("Its rooms...", "This floor in World's Rooms tab: every room that can come up here."), GUILayout.Height(22)))
        { OpenLevel(d, -1); levelTab = RoomsTabIndex; roomsFloor = f.floor; }

        // loot
        TimelineSection("LOOT");
        var gear = d.GearAt(f.floor);
        GUILayout.Label(gear != null ? GearLine(gear) : "the loot profile's own tiers", new GUIStyle(dim) { richText = true, wordWrap = true }, GUILayout.Width(inner));
        GUILayout.Label(b.loot != null ? b.loot.name : "<color=#e0a85a>no loot</color>", new GUIStyle(dimLine) { richText = true, clipping = TextClipping.Clip }, GUILayout.Width(inner));

        // the Dungeon Master's words entering it (the biome's entry lines, on its stage's first floor)
        TimelineSection("THE DUNGEON MASTER");
        if (!f.firstOfStage) GUILayout.Label("nothing new: he speaks entering a biome", dim);
        else
        {
            var entries = b.entryLines ?? Array.Empty<DungeonMaster.Line>();
            if (entries.Length == 0) GUILayout.Label("says nothing entering it", dim);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null) continue;
                string text = EditorGUILayout.DelayedTextField(entries[i].text ?? "", new GUIStyle(EditorStyles.textField) { wordWrap = true }, GUILayout.Width(inner), GUILayout.Height(36));
                if (text != (entries[i].text ?? "")) { Undo.RecordObject(b, "Entry line"); entries[i].text = text; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("+ a line", "Another line he may say entering this biome (one is picked at random)."), GUILayout.Width(80), GUILayout.Height(20)))
            { Undo.RecordObject(b, "Entry line"); b.entryLines = entries.Append(new DungeonMaster.Line { text = "" }).ToArray(); EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
            if (entries.Length > 0) GUILayout.Label($"{b.entryLineChance:0%} of the time", dimLine);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        if (GUILayout.Button(new GUIContent("Play from this floor", $"A run of {d.name} that starts on floor {f.floor}."), GUILayout.Height(28))) Toast(GameLauncher.PlayFloor(d, f.floor));
        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    void TimelineSection(string title)
    {
        GUILayout.Space(6);
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(10, 1, GUILayout.ExpandWidth(true)), ChartLine);
        GUILayout.Label(title, new GUIStyle(gold) { fontSize = StudioSkin.Size - 3 });
    }

    // A Gear by floor row as its shares, the biggest first ("Bronze 50% · Iron 50%"), and its upgrade chance.
    static string GearLine(LootFloorTiers g)
    {
        var tiers = new (string name, float share)[] { ("Bronze", g.tier1), ("Iron", g.tier2), ("Steel", g.tier3), ("Elven", g.tier4), ("Warlord", g.tier5), ("Verdant", g.tier6) };
        float sum = tiers.Sum(t => t.share);
        if (sum <= 0f) return "no gear tiers";
        return string.Join(" · ", tiers.Where(t => t.share > 0f).OrderByDescending(t => t.share).Select(t => $"{t.name} {t.share / sum:0%}")) + (g.upgradeChance > 0f ? $"\n{g.upgradeChance:0%} a tier better" : "");
    }
}
