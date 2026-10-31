using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The rooms one floor of a level can have (the user, 2026-10-30: the old overview made it hard to see which rooms are in
// which biomes). A level page's Rooms tab and the Dungeon view's Rooms page: pick a floor along the strip (its stage named
// over it), then see what the generator can build there, as it decides it (DungeonGenerator.PoolTemplates):
// - its fixed rooms: the room the floor opens in (floor 1 and the first floor of each new biome, not on a floor with a
//   look of its own), the tavern (the level's shop room, on the floors its biome gives a merchant), the guardian's arena
//   at the stairs on a boss floor, and the rooms behind a portal in one of the pool's rooms;
// - the floor's pool (its biome's hand-built rooms): each with its picture, kind, how common it is (weight and its share
//   of what this floor can draw), the biome floor it first comes on, what kinds of room it may become, and where else in
//   the game it is used; rooms not come yet, or left out because the floor switches off what they are built around, are
//   listed after them. Weight and first floor are edited in place.
// A painted level (rooms in a row, a map template) shows its rooms as painted instead.
public partial class AssetStudioWindow
{
    [SerializeField] int roomsFloor = 1;
    [SerializeField] bool roomsShowLater = true;

    void FloorRooms(AssetStudioWorld.Level l)
    {
        var d = l.data;
        if (d.layoutMode == DungeonLayoutMode.Authored) { PaintedRooms(l); return; }
        int floors = Mathf.Max(1, d.FloorCount);
        roomsFloor = Mathf.Clamp(roomsFloor, 1, floors);
        Section("PICK A FLOOR");
        FloorStrip(l, floors);
        int floor = roomsFloor;
        var b = d.Biome(floor);
        d.BiomeAt(floor, out int inBiome, out int biomeFloors);
        var st = l.RunStages.FirstOrDefault(s => floor >= s.firstFloor && floor <= s.lastFloor);
        if (b == null) { GUILayout.Label("<color=#e0a85a>This floor has no biome: nothing to build it from.</color>", dim); return; }
        bool boss = st != null && floor == st.lastFloor && st.boss && AssetStudioWorld.HasGuardian(b);
        var features = b.RoomFeatures(d);
        int rooms = b.roomCount > 0 ? b.roomCount : d.roomCount;

        // What this floor is, in one line.
        GUILayout.Space(6);
        GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginVertical();
        GUILayout.Label($"Floor {floor} · {DungeonAct(b)} · {b.name}" + (biomeFloors > 1 ? $"  <color=#b8ae9c>its floor {inBiome} of {biomeFloors}</color>" : "") + (boss ? "  <color=#e24b4a>boss floor</color>" : ""), new GUIStyle(StudioSkin.Strong) { richText = true });
        GUILayout.Label($"About {rooms} rooms; {b.templateChance:0%} of the rooms that suit one are hand-built from the pool, the rest generated. No hand-built room comes up twice on a floor; {EventsLine(d)}.", Wrapped, GUILayout.Width(pageWidth - 150));
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.Width(130));
        if (GUILayout.Button(new GUIContent("Play this floor", $"A run of {d.name} that starts on floor {floor}."), GUILayout.Height(26)))
            Toast(GameLauncher.PlayFloor(d, floor));
        if (GUILayout.Button(new GUIContent("Show the biome", "Its inspector on the right."), GUILayout.Height(24))) ShowAsset(b);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        // The rooms the floor always has, or has by rule.
        var fixedRooms = new List<(GameObject room, string why)>();
        bool ownLook = d.StageBiome(b) is { ownLook: true };
        if (d.startingRoom != null && !ownLook && (floor == 1 || d.Biome(floor) != d.Biome(floor - 1))) fixedRooms.Add((d.startingRoom, "the room the floor opens in"));
        if (d.shopRoom != null && b.merchantChance > 0f) fixedRooms.Add((d.shopRoom, b.merchantChance >= 1f ? "the tavern, on every floor here" : $"the tavern, on {b.merchantChance:0%} of floors here"));
        if (boss && b.guardian.arena != null) fixedRooms.Add((b.guardian.arena, $"{b.guardian.boss.name}'s room, at the stairs"));
        var pool = b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>();
        foreach (var c in pool)
            if (c != null && c.template != null)
                foreach (var portal in c.template.GetComponentsInChildren<PortalArch>(true))
                    if (portal.room != null && !fixedRooms.Any(f => f.room == portal.room)) fixedRooms.Add((portal.room, $"behind the portal in {c.template.name}"));
        Section("ALWAYS HERE, OR BY RULE");
        if (fixedRooms.Count == 0) GUILayout.Label("None: every room on this floor comes from the pool or is generated.", dim);
        else
        {
            const float w = 150;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(pageWidth / (w + 8)));
            for (int i = 0; i < fixedRooms.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                foreach (var (room, why) in fixedRooms.Skip(i).Take(perRow))
                {
                    GUILayout.BeginVertical(GUILayout.Width(w));
                    RoomCell(room, w, 100);
                    GUILayout.Label(room.name, new GUIStyle(StudioSkin.Strong) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(w));
                    GUILayout.Label(why, new GUIStyle(dim) { alignment = TextAnchor.UpperCenter, wordWrap = true }, GUILayout.Width(w));
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        // The pool as this floor draws from it.
        bool Allowed(GameObject t) => features == null || !t.TryGetComponent<DungeonRoomTemplate>(out var tpl) || features.AllowsAll(tpl.Requires());
        var now = new List<int>(); var later = new List<int>(); var off = new List<int>();
        for (int i = 0; i < pool.Length; i++)
        {
            var c = pool[i];
            if (c == null || c.template == null) continue;
            if (!Allowed(c.template)) off.Add(i);
            else if (c.fromFloor > inBiome) later.Add(i);
            else now.Add(i);
        }
        float total = now.Sum(i => Mathf.Max(0f, pool[i].weight));
        now = now.OrderByDescending(i => pool[i].weight).ThenBy(i => pool[i].template.name, StringComparer.OrdinalIgnoreCase).ToList();
        GUILayout.Space(10);
        GUILayout.BeginHorizontal(GUILayout.Width(pageWidth));
        Section($"FROM THE POOL  ·  {now.Count} room{(now.Count == 1 ? "" : "s")} can come up here");
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Add a room...", GUILayout.Width(150), GUILayout.Height(24))) RoomMenu(l, b, pool);
        GUILayout.EndHorizontal();
        var shared = World.LevelsOf(b).Where(x => !x.tool).ToList();
        if (shared.Count > 1 || l.RunStages.Count(s => s.biome == b) > 1)
            GUILayout.Label($"<color=#e0a85a>{b.name}'s pool is shared ({string.Join(", ", shared.Select(x => x.data.name))}{(l.RunStages.Count(s => s.biome == b) > 1 ? ", several stages" : "")}): a change here shows there too.</color>", Wrapped, GUILayout.Width(pageWidth));
        if (now.Count == 0) GUILayout.Label("Nothing in the pool comes up on this floor: its rooms are all generated.", dim);
        foreach (int i in now) PoolRow(l, b, pool, i, total, "");
        if (later.Count + off.Count > 0)
        {
            GUILayout.Space(6);
            roomsShowLater = EditorGUILayout.Foldout(roomsShowLater, $"In the pool but not on this floor  ({later.Count + off.Count})", true);
            if (roomsShowLater)
            {
                foreach (int i in later.OrderBy(i => pool[i].fromFloor)) PoolRow(l, b, pool, i, 0, $"comes from the biome's floor {pool[i].fromFloor}");
                foreach (int i in off) PoolRow(l, b, pool, i, 0, "left out: this floor switches off what it is built around (its pits, shrine or light)");
            }
        }
        if (World.rooms.Any(r => AssetStudioRoomKinds.Of(r) == DungeonRoomKind.Unsorted))
        {
            int unsorted = World.rooms.Count(r => AssetStudioRoomKinds.Of(r) == DungeonRoomKind.Unsorted);
            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent($"Sort the {unsorted} rooms with no kind for me...", "Gives every room without a kind one, from what it holds: an event script makes it Scripted; a chest, spikes, a shrine or feature, a pit or a portal Active; the rest Visual. Rooms you sorted keep theirs."), GUILayout.Width(300), GUILayout.Height(24)))
                GuessRoomKinds();
        }
    }

    // The run as a strip of floors, each stage named over its floors; a click picks the floor.
    void FloorStrip(AssetStudioWorld.Level l, int floors)
    {
        var run = l.RunStages;
        float w = Mathf.Clamp(pageWidth / floors - 4, 34, 74);
        GUILayout.BeginHorizontal();
        for (int k = 0; k < run.Count; k++)
        {
            var st = run[k];
            bool actStarts = k == 0 || DungeonAct(st.biome) != DungeonAct(run[k - 1].biome);
            float width = (st.lastFloor - st.firstFloor + 1) * (w + 4);
            GUILayout.BeginVertical(GUILayout.Width(width));
            // the dungeon act over its first stage, its stages under it
            GUILayout.Label(actStarts ? DungeonAct(st.biome) : "", new GUIStyle(gold) { fontSize = StudioSkin.Size - 2, clipping = TextClipping.Overflow, wordWrap = false }, GUILayout.Width(width - 4));
            GUILayout.Label(st.biome != null ? st.biome.name : "no biome", new GUIStyle(dimLine) { clipping = TextClipping.Clip }, GUILayout.Width(width - 4));
            GUILayout.BeginHorizontal();
            for (int f = st.firstFloor; f <= st.lastFloor; f++)
            {
                bool boss = f == st.lastFloor && st.boss && AssetStudioWorld.HasGuardian(st.biome);
                string label = boss ? $"{f}  <color=#e24b4a>boss</color>" : f.ToString();
                if (GUILayout.Toggle(roomsFloor == f, label, StudioSkin.Tab, GUILayout.Width(w), GUILayout.Height(28)) && roomsFloor != f) { roomsFloor = f; GUI.FocusControl(null); }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    static string EventsLine(TableLevelData d)
    {
        var e = d.eventRoomsPerFloor;
        int most = Mathf.Max(0, e.y), fewest = Mathf.Min(Mathf.Max(0, e.x), most);
        return most == 0 ? "no event rooms" : fewest == most ? $"{most} event room{(most == 1 ? "" : "s")} a floor" : $"{fewest} to {most} event rooms a floor";
    }

    // One room of the pool: its picture, name, kind and what it may become; how common it is here; where else it is used.
    void PoolRow(AssetStudioWorld.Level l, DungeonBiome b, DungeonBiome.RoomTemplateChoice[] pool, int i, float total, string why)
    {
        var c = pool[i];
        var room = c.template;
        bool here = why.Length == 0;
        GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth), GUILayout.MinHeight(76));
        RoomCell(room, 96, 66);
        GUILayout.Space(4);
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(here ? room.name : $"<color=#8f897e>{room.name}</color>", cell, GUILayout.Height(20))) ShowAsset(room);
        if (IsEventRoom(room)) GUILayout.Label("<color=#d8aa48>event</color>", new GUIStyle(dimLine) { richText = true }, GUILayout.Width(44));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        var tmpl = room.GetComponent<DungeonRoomTemplate>();
        string what = tmpl != null && !string.IsNullOrWhiteSpace(tmpl.description) ? tmpl.description.Trim() : "";
        string about = RoomAbout(room, c);
        if (what.Length > 0) GUILayout.Label(what, new GUIStyle(dim) { wordWrap = true });
        GUILayout.Label($"<color=#8f897e>{about}</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        if (!here) GUILayout.Label($"<color=#e0a85a>{why}</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        // Where else it comes up, by level: its biomes there (test levels left out).
        var elsewhere = World.UsesOf(room).Where(u => u.biome != b && World.LevelOf(u.level) is AssetStudioWorld.Level ul && !ul.tool)
            .GroupBy(u => u.level).Select(g => $"{g.Key.name} ({string.Join(", ", g.Select(u => u.biome != null ? u.biome.name : u.role.ToLowerInvariant()).Distinct())})").ToList();
        GUILayout.Label(elsewhere.Count == 0 ? "<color=#8f897e>nowhere else</color>" : $"<color=#8f897e>also in {string.Join("; ", elsewhere)}</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        GUILayout.EndVertical();
        GUILayout.Space(6);
        GUILayout.BeginVertical(GUILayout.Width(150));
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("weight", "How common it is against the pool's other rooms."), Unit, GUILayout.Width(56));
        float weight = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(c.weight, GUILayout.Width(44)));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("from", "The biome's floor it first comes on (1: from its first floor)."), Unit, GUILayout.Width(56));
        int from = Mathf.Max(1, EditorGUILayout.DelayedIntField(c.fromFloor, GUILayout.Width(44)));
        GUILayout.EndHorizontal();
        if (here && total > 0f) GUILayout.Label($"<color=#d8aa48>{c.weight / total:0%}</color> of this floor's draws", new GUIStyle(dim) { richText = true });
        if (GUILayout.Button(new GUIContent("Take out", $"Out of {b.name}'s pool (the room itself stays)."), GUILayout.Width(90), GUILayout.Height(22))) { AssetStudioWorld.RemoveFromPool(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(weight, c.weight) || from != c.fromFloor)
        { Undo.RecordObject(b, "Pool room"); c.weight = weight; c.fromFloor = from; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); RefreshWorld(); }
    }

    // A scripted event room, or a room whose portal leads to one (as the generator counts them).
    static bool IsEventRoom(GameObject t)
    {
        if (t == null) return false;
        if (t.TryGetComponent<ScriptedEventRoom>(out _)) return true;
        foreach (var portal in t.GetComponentsInChildren<PortalArch>(true))
            if (portal.room != null && portal.room.TryGetComponent<ScriptedEventRoom>(out _)) return true;
        return false;
    }

    // A painted level: its rooms are fixed, as painted on its map or in its row.
    void PaintedRooms(AssetStudioWorld.Level l)
    {
        var d = l.data;
        Section("ITS ROOMS");
        GUILayout.Label(d.mapTemplate != null ? $"Painted on {d.mapTemplate.name}: every floor has these rooms, in this order. Build each under its Room child in the map." : "Painted as rooms in a row (the Map tab): every floor has these rooms, in this order.", Wrapped, GUILayout.Width(pageWidth));
        var painted = d.mapTemplate != null ? d.mapTemplate.Rooms() : d.authoredRooms ?? Array.Empty<TableLevelData.AuthoredRoom>();
        for (int i = 0; i < painted.Length; i++)
        {
            var a = painted[i];
            GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth));
            if (a.template != null) RoomCell(a.template, 96, 66);
            else GUILayout.Label("generated", new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(96), GUILayout.Height(66));
            GUILayout.BeginVertical();
            GUILayout.Label($"{DungeonMapTemplate.RoomMark(i)}  {(string.IsNullOrEmpty(a.label) ? (a.template != null ? a.template.name : "a room") : a.label)}", StudioSkin.Strong);
            GUILayout.Label($"{a.role.ToString().ToLowerInvariant()}{(a.merchant ? " · the merchant" : "")}{(a.template == null ? " · furnished by the generator" : "")}", dim);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        if (d.mapTemplate != null && GUILayout.Button("Open " + d.mapTemplate.name, GUILayout.Width(240), GUILayout.Height(26))) AssetDatabase.OpenAsset(d.mapTemplate.gameObject);
    }
}
