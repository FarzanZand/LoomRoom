using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The library's pages: under the preview of the picked asset, what matters about it in the game's own words. "At a
// glance" holds its main fields, edited right there (the inspector on the right still has everything); "In the game"
// says where it is used, with the links that matter edited in place: the biomes an enemy lives in (how common, from
// which floor), the pools a room is in, the levels a style or a track is used by, and what else points at it.
public partial class AssetStudioWindow
{
    Vector2 pageScroll;
    [NonSerialized] string pageUsedFor;
    [NonSerialized] List<(string path, AssetStudioCatalog.Entry entry)> pageUsedBy;

    // The fields worth having up front, per kind of asset (the rest stay in the inspector).
    static readonly string[] ItemGlance = { "itemName", "rarity", "tier", "value", "damageSpread", "statModifiers", "effects", "gridSize", "maxStackSize", "description" };
    static readonly string[] CharacterGlance = { "characterName", "description", "stats", "experience", "attacks", "archer", "caster", "spells", "closesIn" };
    static readonly string[] SpellGlance = { "displayName", "manaCost", "power", "castSeconds", "recoverySeconds", "speed", "lifetime", "homing", "groundArea", "areaRadius", "areaDelay", "description" };
    static readonly string[] SoundGlance = { "clips", "volume", "pitch", "pitchVariance" };
    static readonly string[] RoomGlance = { "heightTiles", "roomEvent", "openCeiling", "skylightRadius", "style", "enterLines", "enterChance" };
    static readonly string[] StyleGlance = { "floor", "bottomWall", "upperWall", "ceiling", "trim", "hideTrims", "wallBaseWeight", "wallVariants", "floorBaseWeight", "floorVariants" };

    bool HasPage(AssetStudioCatalog.Entry e) => e != null && e.category != "Campaign";

    // The page under the preview, scrolled on its own.
    void DrawPage(Rect r)
    {
        var asset = selected.Load();
        if (asset == null) return;
        GUI.Box(r, GUIContent.none, StudioSkin.Inset);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
        pageScroll = GUILayout.BeginScrollView(pageScroll);
        try
        {
            switch (asset)
            {
                case ItemData item: Glance(item, ItemGlance); break;
                case SpellDefinition spell: Glance(spell, SpellGlance); break;
                case AudioData sound: Glance(sound, SoundGlance); break;
                case DungeonRoomStyle style: Glance(style, StyleGlance); break;
                case TableLevelData level:
                    GUILayout.Label("Levels have their own page, with their run, rooms and look.", dim);
                    if (GUILayout.Button("Open " + level.name + "'s page", GUILayout.Height(30), GUILayout.Width(280))) OpenLevel(level, -1);
                    break;
                case DungeonBiome biome:
                    GUILayout.Label("A biome is edited on the page of a level that runs it.", dim);
                    if (GUILayout.Button("Open it", GUILayout.Height(30), GUILayout.Width(200))) PickBiome(biome);
                    break;
                case GameObject go when go.GetComponent<EnemyBrain>() != null:
                    Glance(CharacterDataOf(go), CharacterGlance);
                    LivesIn(go);
                    break;
                case GameObject go when go.GetComponent<Character>() != null || go.GetComponentInChildren<Player>(true) != null:
                    Glance(CharacterDataOf(go), CharacterGlance);
                    break;
                case GameObject go when go.GetComponent<DungeonRoomTemplate>() != null:
                    Glance(go.GetComponent<DungeonRoomTemplate>(), RoomGlance);
                    InPools(go);
                    break;
            }
            InTheGame(asset);
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    static CharacterData CharacterDataOf(GameObject go) =>
        AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(go)).OfType<CharacterData>().FirstOrDefault();

    void Glance(UnityEngine.Object target, string[] names)
    {
        if (target == null) return;
        GUILayout.Label("At a glance", gold);
        using var so = new SerializedObject(target);
        so.Update();
        foreach (var n in names)
        {
            var p = so.FindProperty(n);
            if (p == null) continue;
            if (p.isArray && p.propertyType != SerializedPropertyType.String) ListRows(p);
            else EditorGUILayout.PropertyField(p, true);
        }
        if (so.ApplyModifiedProperties()) { AssetDatabase.SaveAssetIfDirty(target); MarkStale(); }
        GUILayout.Space(8);
    }

    // A list as plain rows: each entry's fields side by side (a stat and its value, an attack's numbers), a cross to take
    // it out and + Add under them. Unity's own list folds every entry shut and names none.
    void ListRows(SerializedProperty list)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(list.displayName, StudioSkin.Strong, GUILayout.Width(EditorGUIUtility.labelWidth));
        GUILayout.Label(list.arraySize == 0 ? "none" : list.arraySize.ToString(), dim);
        GUILayout.EndHorizontal();
        for (int i = 0; i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            if (e.propertyType == SerializedPropertyType.Generic && e.hasVisibleChildren)
            {
                var child = e.Copy(); var end = e.GetEndProperty();
                int shown = 0;
                for (bool enter = true; child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end) && shown < 6; enter = false, shown++)
                {
                    if (child.hasVisibleChildren && child.propertyType == SerializedPropertyType.Generic) { GUILayout.Label("…", dim, GUILayout.Width(16)); continue; }
                    bool small = child.propertyType is SerializedPropertyType.Float or SerializedPropertyType.Integer or SerializedPropertyType.Boolean;
                    if (small) GUILayout.Label(child.displayName, dimLine, GUILayout.ExpandWidth(false));
                    EditorGUILayout.PropertyField(child, GUIContent.none, small ? GUILayout.Width(child.propertyType == SerializedPropertyType.Boolean ? 18 : 56) : GUILayout.MinWidth(90));
                }
            }
            else EditorGUILayout.PropertyField(e, GUIContent.none);
            if (GUILayout.Button("✕", GUILayout.Width(28))) { list.DeleteArrayElementAtIndex(i); GUILayout.EndHorizontal(); break; }
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);
        if (GUILayout.Button("+ Add", GUILayout.Width(90), GUILayout.Height(24))) list.arraySize++;
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    // The biomes an enemy lives in, as on their stage pages: how common, from which floor, out again; and in more.
    void LivesIn(GameObject enemy)
    {
        GUILayout.Label("Lives in", gold);
        var m = World;
        var biomes = m.biomes.Concat(m.levels.Where(l => l.data.baseBiome != null).Select(l => l.data.baseBiome)).Distinct().ToList();
        bool any = false;
        foreach (var b in biomes)
        {
            var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
            for (int i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                if (s == null || s.enemy != enemy) continue;
                any = true;
                var users = AssetStudioWorld.IsBase(b) ? new[] { AssetStudioWorld.OwnerOf(b)?.name } : m.LevelsOf(b).Where(x => !x.tool).Select(x => x.data.name).ToArray();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{b.name}  <color=#b8ae9c>{(users.Length == 0 ? "no level" : string.Join(", ", users))}</color>", cell, GUILayout.Height(22))) PickBiome(b);
                GUILayout.Label("weight", dim, GUILayout.Width(56));
                float w = EditorGUILayout.DelayedFloatField(s.weight, GUILayout.Width(40));
                GUILayout.Label("from", dim, GUILayout.Width(40));
                int from = EditorGUILayout.DelayedIntField(s.fromFloor, GUILayout.Width(30));
                if (GUILayout.Button("✕", GUILayout.Width(28))) { AssetStudioWorld.RemoveEnemy(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
                if (w != s.weight || from != s.fromFloor) { AssetStudioWorld.SetEnemy(b, i, w, from); RefreshWorld(); }
            }
        }
        if (!any) GUILayout.Label("No biome yet: it never shows up in a run.", dim);
        if (GUILayout.Button("+ Let it live in a biome", GUILayout.Height(28), GUILayout.Width(260)))
        {
            var menu = new GenericMenu();
            foreach (var (group, levels) in m.Groups(false))
                foreach (var l in levels)
                    foreach (var st in l.RunStages.Where(x => x.biome != null && !(x.biome.enemies ?? Array.Empty<DungeonBiome.Spawn>()).Any(s => s != null && s.enemy == enemy)))
                        menu.AddItem(new GUIContent($"{group}/{l.data.name}/{st.biome.name}"), false, () => { AssetStudioWorld.AddEnemy(st.biome, enemy); RefreshWorld(); });
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("It lives in every biome already"));
            menu.ShowAsContext();
        }
        GUILayout.Space(8);
    }

    // The pools a room is in (how common there), and into more.
    void InPools(GameObject room)
    {
        GUILayout.Label("Built in", gold);
        var uses = World.UsesOf(room);
        if (uses.Count == 0) GUILayout.Label("Nobody uses it yet.", dim);
        foreach (var u in uses)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"{u.level.name}{(u.biome != null ? " · " + u.biome.name : "")}  <color=#b8ae9c>{u.role.ToLowerInvariant()}</color>", cell, GUILayout.Height(22)))
            { if (u.biome != null) PickBiome(u.biome); else OpenLevel(u.level, -1); }
            var choice = u.biome?.roomTemplates?.FirstOrDefault(c => c != null && c.template == room);
            if (choice != null)
            {
                GUILayout.Label("weight", dim, GUILayout.Width(56));
                float w = EditorGUILayout.DelayedFloatField(choice.weight, GUILayout.Width(40));
                if (w != choice.weight) { Undo.RecordObject(u.biome, "Pool weight"); choice.weight = Mathf.Max(0, w); EditorUtility.SetDirty(u.biome); AssetDatabase.SaveAssetIfDirty(u.biome); }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play from this room", GUILayout.Height(28))) Toast(GameLauncher.PlayRoom(room));
        if (GUILayout.Button("Open to build", GUILayout.Height(28))) AssetDatabase.OpenAsset(room);
        if (GUILayout.Button("+ Put it in a pool", GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            foreach (var (group, levels) in World.Groups(false))
                foreach (var l in levels)
                    foreach (var st in l.RunStages.Where(x => x.biome != null))
                        menu.AddItem(new GUIContent($"{group}/{l.data.name}/{st.biome.name}"), false, () => { AssetStudioWorld.AddToPool(st.biome, room); Toast($"Put {room.name} in {st.biome.name}'s pool."); RefreshWorld(); });
            menu.ShowAsContext();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
    }

    // Everything that points at it (Unity's dependency data turned around), named as the library names it.
    void InTheGame(UnityEngine.Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        if (asset is DungeonRoomStyle style)
        {
            GUILayout.Label("Used by", gold);
            GUILayout.Label(AssetStudioStyles.Describe(style), dim);
            foreach (var u in AssetStudioStyles.Users(style))
            {
                string name = u.biome.isLevelBase && u.level != null ? u.level.name : u.biome.name;
                if (GUILayout.Button($"{name}  <color=#b8ae9c>{u.use}</color>", cell, GUILayout.Height(22))) { if (u.level != null) OpenLevel(u.level, -1); else PickBiome(u.biome); }
            }
            return;
        }
        if (pageUsedFor != path) { pageUsedFor = path; pageUsedBy = AssetStudioUsage.Find(AssetDatabase.AssetPathToGUID(path), categories).Where(u => u.path != path).ToList(); }
        GUILayout.Label(asset is AudioClip ? "Where it plays" : "Used by", gold);
        if (pageUsedBy.Count == 0) { GUILayout.Label(selected.inGame ? "Nothing in the library points at it (a scene or code does)." : "Nothing in the game uses it.", dim); return; }
        foreach (var (p, e) in pageUsedBy.Take(40))
        {
            string name = e != null ? e.name : Path.GetFileNameWithoutExtension(p);
            string where = e != null ? e.category + " · " + e.kind : Path.GetExtension(p) == ".unity" ? "scene" : Path.GetDirectoryName(p).Replace('\\', '/').Replace("Assets/Game/", "");
            if (GUILayout.Button($"{name}  <color=#b8ae9c>{where}</color>", cell, GUILayout.Height(22)))
            {
                var used = AssetDatabase.LoadMainAssetAtPath(p);
                if (used is TableLevelData l) OpenLevel(l, -1);
                else if (used is DungeonBiome b) PickBiome(b);
                else if (e != null) ShowEntry(e);
                else { Selection.activeObject = used; EditorGUIUtility.PingObject(used); }
            }
        }
        if (pageUsedBy.Count > 40) GUILayout.Label($"and {pageUsedBy.Count - 40} more", dim);
    }
}
