using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Unlockables view (the user, 2026-10-30): everything the player can unlock that is not there from the
// start, read from the game's own data: classes (AdventurerClass.unlockFlag, and what unlocks each: an item with
// unlocksClass picked up, a room that drops it, the Arena's win, the room puzzle), places on the campaign map (a rumour,
// deaths elsewhere, a later act), room key items (the missing tile, the symbol puzzle's dice), abilities (the skill
// milestones bought at the character sheet), features (revive) and campaign events. Left, the list by type or in the
// order the game unlocks them; in the middle the picked one (how it is unlocked, what it gives, its flag, its assets)
// over the whole unlock flow, step by step. Read again with the World model; nothing here changes the game.
public partial class AssetStudioWindow
{
    class Unlockable
    {
        public string name, type, how, gives, flag, step;
        public int order;
        public Texture icon;
        public readonly List<UnityEngine.Object> assets = new();
    }

    [SerializeField] int unlockMode;          // 0 by type, 1 unlock flow
    [SerializeField] string unlockPicked;
    Vector2 unlockListScroll, unlockPageScroll;
    [NonSerialized] List<Unlockable> unlockables;
    [NonSerialized] AssetStudioWorld.Model unlockablesFor;
    bool showingUnlockables => studioView == StudioView.Unlockables;
    static readonly string[] UnlockTypes = { "Class", "Place", "Room item", "Ability", "Feature", "Campaign event" };
    const int ReviveDeaths = 3;   // RunRecapUI.reviveAfterDeaths (not serialized: the code's number)

    void OpenUnlockables() { studioView = StudioView.Unlockables; GUI.FocusControl(null); }

    List<Unlockable> Unlockables()
    {
        if (unlockables != null && unlockablesFor == World) return unlockables;
        unlockablesFor = World;
        return unlockables = BuildUnlockables();
    }

    List<Unlockable> BuildUnlockables()
    {
        var list = new List<Unlockable>();
        var m = World;
        var pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : AssetDatabase.LoadAssetAtPath<GameObject>(ProgressionManagerPath)?.GetComponent<ProgressionManager>();
        var items = AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game" }).Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(i => i != null).ToList();
        var dungeon = AssetDatabase.LoadAssetAtPath<TableLevelData>(DungeonDefaultPath);

        // Classes not there from the start, and what unlocks each.
        var classes = pm != null && pm.classRules != null && pm.classRules.classes != null ? pm.classRules.classes : Array.Empty<AdventurerClass>();
        foreach (var c in classes.Where(c => c != null && !string.IsNullOrEmpty(c.unlockFlag)))
        {
            var u = new Unlockable { name = c.displayName, type = "Class", flag = c.unlockFlag, icon = c.portrait != null ? c.portrait.texture : null, gives = $"The {c.displayName} at the table and on the hero select; its poster and statue go up on the wall by the bed.", step = "In the dungeon", order = 40 };
            u.assets.Add(c);
            var ways = new List<string>();
            foreach (var item in items.Where(i => i.unlocksClass == c))
            {
                u.assets.Add(item);
                var rooms = PrefabsNaming(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)));
                foreach (var room in rooms)
                {
                    u.assets.Add(room);
                    int floor = FirstFloorWith(dungeon, room);
                    if (room.GetComponentInChildren<DuelEvent>(true) != null) { ways.Add($"win the duel in {room.name} fairly: the duellist drops {item.itemName}, picked up it unlocks the class" + (floor > 0 ? $" (from floor {floor} of {dungeon.name})" : "")); if (floor > 0) u.order = 30 + floor; }
                    else ways.Add($"pick up {item.itemName}, found in {room.name}");
                }
                if (rooms.Count == 0) ways.Add($"pick up {item.itemName} (nothing in the levels drops it yet)");
            }
            // the rooms whose win unlocks it (a map template holding such a room is the same fight: left out)
            foreach (var room in PrefabsNaming(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c))).Where(r => r.GetComponent<DungeonMapTemplate>() == null && r.GetComponentInChildren<ArenaFight>(true) is ArenaFight a && a.unlocks == c))
            {
                u.assets.Add(room);
                var arena = m.places.FirstOrDefault(p => p.level != null && AssetDatabase.GetAssetPath(p.level).StartsWith(Path.GetDirectoryName(AssetDatabase.GetAssetPath(room)).Replace('\\', '/').Split(new[] { "/Rooms" }, StringSplitOptions.None)[0]));
                ways.Add($"win the fight in {room.name}" + (arena != null ? $" ({arena.displayName}" + (arena.rumoured ? ", opened by a rumour" : "") + ")" : ""));
                u.order = Math.Max(u.order, arena != null && arena.rumoured ? 38 : 36);
            }
            foreach (var puzzle in UnityEngine.Object.FindObjectsByType<AdventureRoomPuzzle>(FindObjectsInactive.Include).Where(p => !p.givesDice && p.unlockedClass == c))
            { ways.Add("solve the symbol puzzle in the room"); u.assets.Add(puzzle); u.step = "Back in the room"; u.order = 70; }
            u.how = ways.Count > 0 ? string.Join("; or ", ways) : $"<color=#e0a85a>nothing unlocks it yet</color> (its flag {c.unlockFlag} is never set by the game's data)";
            list.Add(u);
        }

        // Places that open later: by a rumour, after deaths somewhere, or with a later act.
        int lastAct = m.Acts;
        var campaign = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Campaign/Prefabs/CampaignManager.prefab")?.GetComponent<CampaignManager>();
        if (campaign != null) lastAct = campaign.lastAct;
        foreach (var p in m.places.Where(p => p.kind == CampaignEncounterKind.Level))
        {
            string how = null, step = null; int order = 0;
            if (p.rumoured) { how = "buy its rumour from a tavern keeper, then it rises onto the map back in the room"; step = "In the dungeon"; order = 34; }
            else if (p.deathsToOpen > 0) { how = $"die {p.deathsToOpen} time{(p.deathsToOpen == 1 ? "" : "s")} in {(p.deathsIn != null ? p.deathsIn.displayName : "?")}"; step = "After some deaths"; order = 60 + p.deathsToOpen; }
            else if (p.act > 1) { how = $"when act {p.act} opens on the map" + (p.act > lastAct ? $" <color=#e0a85a>(closed for now: the campaign's last act is {lastAct})</color>" : ""); step = $"Act {p.act}"; order = 100 * p.act; }
            if (how == null) continue;
            var u = new Unlockable { name = p.displayName, type = "Place", how = how, flag = "campaign.opened." + p.id, step = step, order = order, gives = p.level != null ? $"A new place on the campaign map that plays {p.level.name}." : "A new place on the campaign map." };
            u.assets.Add(p); if (p.level != null) u.assets.Add(p.level);
            list.Add(u);
        }

        // Room key items that come in play.
        if (pm != null && pm.missingTile != null)
        {
            var u = new Unlockable { name = pm.missingTile.itemName, type = "Room item", how = "comes out of a chest opened after the starting room's, on any floor of a real run, until carried home", gives = "The class puzzle's missing piece, for the coffee table.", flag = "the tile's state", step = "In the dungeon", order = 22, icon = pm.missingTile.icon != null ? pm.missingTile.icon.texture : null };
            u.assets.Add(pm.missingTile); list.Add(u);
        }
        if (pm != null && pm.diceSet != null)
        {
            var u = new Unlockable { name = pm.diceSet.itemName, type = "Room item", how = "solve the symbol puzzle on the coffee table (it needs the missing tile)", gives = $"Put on the big table's border: +{pm.diceTradingBonus} Trading while they lie there.", flag = "room.dice", step = "Back in the room", order = 70, icon = pm.diceSet.icon != null ? pm.diceSet.icon.texture : null };
            u.assets.Add(pm.diceSet); list.Add(u);
        }

        // Abilities: the skill milestones bought at the character sheet with spoils.
        if (pm != null)
            foreach (var (track, milestone) in pm.AllMilestones())
            {
                if (milestone.ability == null && (string.IsNullOrEmpty(milestone.Name) || milestone.Name.Trim() == "?")) continue;
                var u = new Unlockable { name = milestone.Name, type = "Ability", how = $"train {track.displayName} to {milestone.value} on the character sheet (spoils), or reach it in a run; the trophy then hangs on the wall and unlocks it for every class", gives = milestone.ability != null ? milestone.ability.description : milestone.unlock, flag = $"meta.trophy.{track.id}.{milestone.value}", step = "After the first run", order = 10 + milestone.value / 5, icon = milestone.ability != null && milestone.ability.icon != null ? milestone.ability.icon.texture : null };
                if (milestone.ability != null) u.assets.Add(milestone.ability);
                if (milestone.trophy != null) u.assets.Add(milestone.trophy);
                list.Add(u);
            }

        // Features the game gives after a while.
        list.Add(new Unlockable { name = "Revive", type = "Feature", how = $"die {ReviveDeaths} times in real runs (the practice run doesn't count); always on in the editor and development builds", gives = "Revive on the death screen: back on your feet where you fell, the run going on, and the Dungeon Master mocks it.", flag = RunManager.DeathsFlag, step = "After some deaths", order = 60 + ReviveDeaths });

        // Campaign events: each shown once when its act opens.
        foreach (var e in m.events)
        {
            var u = new Unlockable { name = e.title, type = "Campaign event", how = $"shown once, the first time the map is seen after act {e.afterAct} opens", gives = "A text event over the map: choices that cost spoils and give items or lines.", flag = "campaign.event." + e.id, step = $"Act {e.afterAct}", order = 100 * e.afterAct + 5 };
            u.assets.Add(e); list.Add(u);
        }
        return list.OrderBy(u => u.order).ThenBy(u => u.name).ToList();
    }

    // The prefabs under Levels whose file names this GUID (rooms that hold the item or the class).
    static List<GameObject> PrefabsNaming(string guid)
    {
        var found = new List<GameObject>();
        if (string.IsNullOrEmpty(guid)) return found;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            try { if (File.ReadAllText(path).Contains(guid) && AssetDatabase.LoadAssetAtPath<GameObject>(path) is GameObject go) found.Add(go); }
            catch (IOException) { }
        }
        return found;
    }

    // The first floor of the level whose biome's pool holds this room (0: none).
    static int FirstFloorWith(TableLevelData d, GameObject room)
    {
        if (d == null) return 0;
        for (int f = 1; f <= d.FloorCount; f++)
        {
            var b = d.Biome(f);
            d.BiomeAt(f, out int inBiome, out _);
            if (b?.roomTemplates != null && b.roomTemplates.Any(c => c != null && c.template == room && c.fromFloor <= inBiome)) return f;
        }
        return 0;
    }

    void DrawUnlockList(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
        unlockMode = GUILayout.Toolbar(unlockMode, new[] { "By type", "Unlock flow" }, StudioSkin.Tab, GUILayout.Height(28));
        GUILayout.Label(unlockMode == 0 ? "Everything not there from the start, by kind." : "In the rough order a player unlocks them.", new GUIStyle(dim) { wordWrap = true });
        var all = Unlockables();
        unlockListScroll = GUILayout.BeginScrollView(unlockListScroll);
        IEnumerable<IGrouping<string, Unlockable>> groups = unlockMode == 0
            ? all.GroupBy(u => u.type).OrderBy(g => Array.IndexOf(UnlockTypes, g.Key))
            : all.GroupBy(u => u.step).OrderBy(g => g.Min(u => u.order));
        foreach (var g in groups)
        {
            GUILayout.Space(8);
            GUILayout.Label($"{g.Key.ToUpperInvariant()}  <color=#b8ae9c>{g.Count()}</color>", new GUIStyle(gold) { richText = true });
            foreach (var u in g)
            {
                string key = u.type + ":" + u.name;
                string sub = unlockMode == 0 ? u.step : u.type;
                if (GUILayout.Button($"{u.name}  <color=#b8ae9c>{sub.ToLowerInvariant()}</color>", unlockPicked == key ? cellSelected : cell, GUILayout.Height(24)))
                { unlockPicked = key; unlockPageScroll = Vector2.zero; if (u.assets.Count > 0) ShowAsset(u.assets[0]); }
            }
        }
        if (all.Count == 0) GUILayout.Label("Nothing found: is the ProgressionManager there?", dim);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawUnlockPage(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        unlockPageScroll = GUILayout.BeginScrollView(unlockPageScroll);
        try
        {
            var all = Unlockables();
            var picked = all.FirstOrDefault(u => u.type + ":" + u.name == unlockPicked);
            if (picked != null) UnlockCard(picked);
            else Title("Unlockables", $"{all.Count} things a player unlocks in play. Pick one on the left; the whole flow is below.");
            Section("THE UNLOCK FLOW");
            GUILayout.Label("Roughly in the order a player meets them, from the first run to the later acts.", Wrapped, GUILayout.Width(pageWidth));
            int n = 0;
            foreach (var g in all.GroupBy(u => u.step).OrderBy(g => g.Min(u => u.order)))
            {
                GUILayout.Space(6);
                GUILayout.Label($"{++n}.  {g.Key}", new GUIStyle(titleStyle) { fontSize = StudioSkin.Size + 2 });
                foreach (var u in g)
                {
                    bool on = picked == u;
                    GUILayout.BeginHorizontal(on ? cellSelected : StudioSkin.Inset, GUILayout.Width(pageWidth));
                    var pic = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
                    if (Event.current.type == EventType.Repaint) { GUI.DrawTexture(pic, StudioSkin.Slot); if (u.icon != null) GUI.DrawTexture(new Rect(pic.x + 3, pic.y + 3, 30, 30), u.icon, ScaleMode.ScaleToFit); }
                    GUILayout.BeginVertical();
                    GUILayout.Label($"{u.name}  <color=#b8ae9c>{u.type.ToLowerInvariant()}</color>", new GUIStyle(StudioSkin.Strong) { richText = true });
                    GUILayout.Label(Cap(u.how) + ".", new GUIStyle(dim) { richText = true, wordWrap = true }, GUILayout.Width(pageWidth - 70));
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    var row = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                    { unlockPicked = u.type + ":" + u.name; if (u.assets.Count > 0) ShowAsset(u.assets[0]); Event.current.Use(); Repaint(); }
                }
            }
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    // The picked one: what it is, how it is unlocked, what it gives, its flag and where it is made.
    void UnlockCard(Unlockable u)
    {
        GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth));
        var pic = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72), GUILayout.Height(72));
        if (Event.current.type == EventType.Repaint) { GUI.DrawTexture(pic, StudioSkin.Slot); if (u.icon != null) GUI.DrawTexture(new Rect(pic.x + 4, pic.y + 4, 64, 64), u.icon, ScaleMode.ScaleToFit); }
        GUILayout.Space(8);
        GUILayout.BeginVertical();
        GUILayout.Label(u.name, titleStyle);
        GUILayout.Label($"{u.type} · {u.step}", dim);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        Section("HOW IT IS UNLOCKED");
        GUILayout.Label(Cap(u.how) + ".", new GUIStyle(StudioSkin.Label) { richText = true, wordWrap = true }, GUILayout.Width(pageWidth));
        Section("WHAT IT GIVES");
        GUILayout.Label(string.IsNullOrEmpty(u.gives) ? "-" : u.gives, new GUIStyle(StudioSkin.Label) { wordWrap = true }, GUILayout.Width(pageWidth));
        GUILayout.Label($"Saved as {u.flag}", dim);
        if (u.assets.Count > 0)
        {
            Section("MADE IN");
            GUILayout.BeginHorizontal();
            foreach (var a in u.assets.Where(a => a != null).Distinct())
            {
                if (!GUILayout.Button(a.name, GUILayout.Height(24))) continue;
                if (a is CampaignEncounter || a is CampaignEvent || a is TableLevelData) { OpenWorld(); PickAsset(a); }
                else ShowAsset(a);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
