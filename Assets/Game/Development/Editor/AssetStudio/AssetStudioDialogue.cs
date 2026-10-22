using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Every Dungeon Master line in the game, found where it is kept: each DungeonMaster.Line field (and list of them) and
// each list of plain line strings (a level's Dialogue tab, a room template's Enter Lines) on the game's scripts, in the
// open scenes, the prefabs and the data assets under Assets/Game. Each line knows where it plays (its owner and field,
// the field's tooltip as "when", its place in its list) and writes back through a SerializedObject, so every edit is
// undoable and lands where the line lives. Read by AssetStudioWindow.Dialogue.
public static class AssetStudioDialogue
{
    public class Line
    {
        public UnityEngine.Object owner;
        public string source;        // asset path, or the scene's path for scene objects
        public string category;      // where it plays, broadly: Intro, Room and table, Campaign, Dungeons ...
        public string ownerName;
        public string path;          // property path to the line (a DungeonMaster.Line or a string)
        public bool rich;            // a DungeonMaster.Line (voice, mumble, in person); else a plain string
        public string arrayPath;     // the list it is in, or null
        public int index = -1;       // its place in that list
        public string field;         // the field, readable ("After Lamp")
        public string context;       // the list entry it belongs to ("Kill", "Step 3")
        public string when;          // the field's tooltip
        public int order;            // order within its owner (declaration order)
        public string text;
        public bool mumble, inPerson;
        public UnityEngine.Object voice;
        public string Where => ownerName + " · " + field + (string.IsNullOrEmpty(context) ? "" : " · " + context);
        public string Key => GlobalObjectId.GetGlobalObjectIdSlow(owner) + "|" + path;
    }

    static readonly Regex Element = new(@"\.Array\.data\[(\d+)\]$");

    // ── Which types hold lines ──

    static List<Type> types;
    public static List<Type> DialogueTypes()
    {
        if (types != null) return types;
        types = new List<Type>();
        foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>()))
            if (!t.IsAbstract && t.Assembly.GetName().Name == "Assembly-CSharp" && Holds(t, 0)) types.Add(t);
        return types;
    }

    static bool Holds(Type t, int depth)
    {
        if (depth > 4 || t == null) return false;
        for (var type = t; type != null && type != typeof(MonoBehaviour) && type != typeof(ScriptableObject); type = type.BaseType)
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsNotSerialized || !f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                var ft = Elem(f.FieldType);
                if (ft == typeof(DungeonMaster.Line)) return true;
                if (ft == typeof(string) && LineName(f.Name)) return true;
                if (ft.IsClass && ft != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(ft) && ft.GetCustomAttribute<SerializableAttribute>() != null && Holds(ft, depth + 1)) return true;
            }
        return false;
    }
    static Type Elem(Type t) => t.IsArray ? t.GetElementType() : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? t.GetGenericArguments()[0] : t;
    // Plain strings that are lines: a field named lines, line, or ending in Lines/Line (enterLines, a level's lines).
    static bool LineName(string name) => name == "lines" || name == "line" || name.EndsWith("Lines") || name.EndsWith("Line");

    // ── Scan ──

    public static List<Line> Scan()
    {
        var lines = new List<Line>();
        var seen = new HashSet<UnityEngine.Object>();
        var dialogueTypes = DialogueTypes();
        // Data assets.
        foreach (var t in dialogueTypes.Where(t => typeof(ScriptableObject).IsAssignableFrom(t)))
            foreach (var guid in AssetDatabase.FindAssets("t:" + t.Name, new[] { "Assets/Game" }))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (o != null && t.IsInstanceOfType(o) && seen.Add(o)) Collect(o, lines);
        // Prefabs that use one of the scripts (found by the script's GUID in the prefab's text, so nothing else loads).
        var behaviours = dialogueTypes.Where(t => typeof(MonoBehaviour).IsAssignableFrom(t)).ToList();
        var guids = new List<string>();
        foreach (var t in behaviours)
            foreach (var g in AssetDatabase.FindAssets("t:MonoScript " + t.Name))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (script != null && script.GetClass() == t) guids.Add(g);
            }
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid), yaml;
            try { yaml = File.ReadAllText(path); } catch { continue; }
            if (!guids.Any(g => yaml.Contains(g))) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            foreach (var t in behaviours)
                foreach (var c in prefab.GetComponentsInChildren(t, true))
                    if (c != null && seen.Add(c)) Collect(c, lines);
        }
        // The open scenes (Room.unity holds the intro, the table, the remarks).
        foreach (var t in behaviours)
            foreach (var c in UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include))
                if (c != null && !EditorUtility.IsPersistent(c) && seen.Add(c)) Collect(c, lines);
        return lines;
    }

    // The lines of scene objects in the open scenes that aren't in the list yet (after a scene was opened again, or Play
    // started or stopped: the objects are new ones).
    public static void CollectScenes(List<Line> into)
    {
        var have = new HashSet<UnityEngine.Object>(into.Select(l => l.owner).Where(o => o != null));
        foreach (var t in DialogueTypes().Where(t => typeof(MonoBehaviour).IsAssignableFrom(t)))
            foreach (var c in UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include))
                if (c != null && !EditorUtility.IsPersistent(c) && have.Add(c)) Collect(c, into);
    }

    // Every line on one object, in declaration order.
    public static void Collect(UnityEngine.Object owner, List<Line> into)
    {
        var so = new SerializedObject(owner);
        var p = so.GetIterator();
        int order = 0;
        string source = EditorUtility.IsPersistent(owner) ? AssetDatabase.GetAssetPath(owner) : (owner as Component)?.gameObject.scene.path;
        string ownerName = OwnerName(owner);
        // A copy of a prefab (in a scene, or nested in another prefab) shows only the lines set on the copy itself: the
        // rest are the prefab's own and are listed there.
        bool copy = PrefabUtility.IsPartOfPrefabInstance(owner);
        bool enter = true;
        while (p.Next(enter))
        {
            enter = true;
            bool rich = p.propertyType == SerializedPropertyType.Generic && p.type == "Line" && p.FindPropertyRelative("text") != null;
            bool plain = p.propertyType == SerializedPropertyType.String && (LineName(FieldName(p.propertyPath)) || owner is HouseRule && p.propertyPath == "text");
            if (!rich && !plain) continue;
            if (copy && !p.prefabOverride) { if (rich) enter = false; continue; }
            var line = new Line { owner = owner, source = source, ownerName = ownerName, path = p.propertyPath, rich = rich, order = order++ };
            var m = Element.Match(p.propertyPath);
            if (m.Success) { line.arrayPath = p.propertyPath.Substring(0, m.Index); line.index = int.Parse(m.Groups[1].Value); }
            line.field = ObjectNames.NicifyVariableName(FieldName(p.propertyPath));
            line.context = Context(so, p.propertyPath);
            line.when = Tooltip(owner.GetType(), p.propertyPath);
            line.category = Category(owner);
            Read(line, p);
            into.Add(line);
            if (rich) enter = false;
        }
    }

    static void Read(Line line, SerializedProperty p)
    {
        if (line.rich)
        {
            line.text = p.FindPropertyRelative("text").stringValue;
            line.mumble = p.FindPropertyRelative("mumble")?.boolValue ?? false;
            line.inPerson = p.FindPropertyRelative("inPerson")?.boolValue ?? false;
            line.voice = p.FindPropertyRelative("voice")?.objectReferenceValue;
        }
        else line.text = p.stringValue;
    }

    // The field a property belongs to, past list elements ("afterLamp.Array.data[2]" -> "afterLamp").
    static string FieldName(string path)
    {
        var parts = path.Split('.').Where(s => s != "Array" && !s.StartsWith("data[")).ToArray();
        return parts.Length > 0 ? parts[parts.Length - 1] : path;
    }

    // The list entry a line sits in, when it is inside one (a level's Dialogue entry, a tutorial step, an event choice).
    static string Context(SerializedObject so, string path)
    {
        var matches = Regex.Matches(path, @"^(.*?)\.Array\.data\[(\d+)\]\.");
        if (matches.Count == 0) return null;
        var m = matches[0];
        var entry = so.FindProperty(m.Groups[1].Value + ".Array.data[" + m.Groups[2].Value + "]");
        string n = ObjectNames.NicifyVariableName(FieldName(m.Groups[1].Value)) + " " + (int.Parse(m.Groups[2].Value) + 1);
        if (entry == null) return n;
        foreach (var name in new[] { "trigger", "label", "title", "id", "name", "kind", "what" })
        {
            var f = entry.FindPropertyRelative(name);
            if (f == null) continue;
            if (f.propertyType == SerializedPropertyType.Enum && f.enumValueIndex >= 0 && f.enumValueIndex < f.enumDisplayNames.Length) return n + ": " + f.enumDisplayNames[f.enumValueIndex];
            if (f.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(f.stringValue)) return n + ": " + f.stringValue;
        }
        return n;
    }

    // The [Tooltip] on the field a property path ends in, found through nested serializable classes.
    static string Tooltip(Type type, string path)
    {
        FieldInfo last = null;
        foreach (var part in path.Split('.'))
        {
            if (part == "Array" || part.StartsWith("data[")) { if (last != null) type = Elem(last.FieldType); continue; }
            FieldInfo f = null;
            for (var t = type; t != null && f == null; t = t.BaseType) f = t.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f == null) break;
            last = f; type = f.FieldType;
        }
        return last?.GetCustomAttribute<TooltipAttribute>()?.tooltip;
    }

    static string OwnerName(UnityEngine.Object o)
    {
        if (o is Component c)
        {
            var root = c.transform.root;
            string go = c.gameObject.name;
            return EditorUtility.IsPersistent(c) ? (root.name == go ? go : root.name + " / " + go) : go + " (scene)";
        }
        return o.name;
    }

    static readonly Dictionary<string, string> Categories = new()
    {
        ["IntroController"] = "Intro",
        ["DungeonMasterRemarks"] = "Room and table", ["DungeonMasterSeat"] = "Room and table", ["TableManager"] = "Room and table",
        ["ClassFigures"] = "Room and table", ["PrototypeEnd"] = "Room and table",
        ["ProgressionManager"] = "Sheet and trophies",
        ["CampaignManager"] = "Campaign", ["CampaignEncounter"] = "Campaign", ["CampaignEvent"] = "Campaign",
        ["HouseRule"] = "House rules", ["RuleManager"] = "House rules",
        ["TableLevelData"] = "Dungeons", ["DungeonBiome"] = "Dungeons", ["DungeonRoomTemplate"] = "Dungeons", ["ArenaFight"] = "Dungeons",
        ["SkeletonParty"] = "Dungeons", ["PortalArch"] = "Dungeons", ["TableLevelLoader"] = "Dungeons",
        ["BossFight"] = "Boss fights", ["BossStageEncounter"] = "Boss fights",
        ["ProvokableNpc"] = "Characters", ["WeepingEnemy"] = "Characters",
        ["RunRecapUI"] = "Runs",
    };
    public static string Category(UnityEngine.Object o) => Categories.TryGetValue(o.GetType().Name, out var c) ? c : "Other";
    public static readonly string[] CategoryOrder = { "Intro", "Room and table", "Sheet and trophies", "House rules", "Campaign", "Dungeons", "Boss fights", "Characters", "Runs", "Other" };

    // ── Writing back ──

    static readonly Dictionary<UnityEngine.Object, SerializedObject> objects = new();
    static SerializedObject For(UnityEngine.Object owner)
    {
        if (!objects.TryGetValue(owner, out var so) || so.targetObject == null) objects[owner] = so = new SerializedObject(owner);
        so.Update();
        return so;
    }

    static void Saved(UnityEngine.Object owner, SerializedObject so)
    {
        so.ApplyModifiedProperties();
        if (EditorUtility.IsPersistent(owner)) { EditorUtility.SetDirty(owner); AssetDatabase.SaveAssetIfDirty(owner); }
        else if (owner is Component c) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
    }

    public static void SetText(Line line, string text)
    {
        var so = For(line.owner);
        var p = so.FindProperty(line.path);
        if (p == null) return;
        if (line.rich) p.FindPropertyRelative("text").stringValue = text; else p.stringValue = text;
        Saved(line.owner, so);
        line.text = text;
    }

    public static void SetDelivery(Line line, bool mumble, bool inPerson)
    {
        if (!line.rich) return;
        var so = For(line.owner);
        var p = so.FindProperty(line.path);
        if (p == null) return;
        p.FindPropertyRelative("mumble").boolValue = mumble; p.FindPropertyRelative("inPerson").boolValue = inPerson;
        Saved(line.owner, so);
        line.mumble = mumble; line.inPerson = inPerson;
    }

    // Moves, adds or removes a line in its list; the owner's lines are read again after.
    public static void Move(Line line, int to) => Change(line, a => a.MoveArrayElement(line.index, Mathf.Clamp(to, 0, a.arraySize - 1)));
    public static void AddAfter(Line line) => Change(line, a =>
    {
        a.InsertArrayElementAtIndex(line.index);
        var copy = a.GetArrayElementAtIndex(line.index + 1);
        if (line.rich) copy.FindPropertyRelative("text").stringValue = ""; else copy.stringValue = "";
    });
    public static void Remove(Line line) => Change(line, a => a.DeleteArrayElementAtIndex(line.index));

    static void Change(Line line, Action<SerializedProperty> change)
    {
        if (line.arrayPath == null) return;
        var so = For(line.owner);
        var a = so.FindProperty(line.arrayPath);
        if (a == null || !a.isArray) return;
        change(a);
        Saved(line.owner, so);
    }

    // ── CSV (to edit many lines in a spreadsheet and bring them back) ──

    // "original text" is the text at export: on import it tells a row edited in the spreadsheet from a line that moved
    // in its list since (a line added, removed or moved puts another line under that key).
    public static string ExportCsv(IEnumerable<Line> lines, string file)
    {
        var sb = new StringBuilder("key,category,where,order,when,original text,text,mumble,in person\n");
        int n = 0;
        foreach (var l in lines)
        {
            sb.Append(Csv(l.Key)).Append(',').Append(Csv(l.category)).Append(',').Append(Csv(l.Where)).Append(',').Append(l.index >= 0 ? (l.index + 1).ToString() : "")
              .Append(',').Append(Csv(l.when ?? "")).Append(',').Append(Csv(l.text ?? "")).Append(',').Append(Csv(l.text ?? "")).Append(',').Append(l.rich ? (l.mumble ? "yes" : "no") : "")
              .Append(',').Append(l.rich ? (l.inPerson ? "yes" : "no") : "").Append('\n');
            n++;
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        return $"{n} lines to {Path.GetFileName(file)}";
    }

    // Text (and delivery) back from a CSV made by Export, matched by key; other columns are only for reading. A row
    // whose line now reads neither its original text nor its new text is skipped (the line under that key changed since
    // the export) and listed in the Console. The whole import is one undo step.
    public static string ImportCsv(List<Line> lines, string file)
    {
        var byKey = new Dictionary<string, Line>();
        foreach (var l in lines) byKey[l.Key] = l;
        var rows = ParseCsv(File.ReadAllText(file));
        if (rows.Count == 0) return "Empty file.";
        var head = rows[0];
        int key = head.IndexOf("key"), text = head.IndexOf("text"), original = head.IndexOf("original text"), mumble = head.IndexOf("mumble"), person = head.IndexOf("in person");
        if (key < 0 || text < 0) return "No key and text columns.";
        int changed = 0, missing = 0;
        var skipped = new List<string>();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Import dialogue");
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count <= Math.Max(key, text) || !byKey.TryGetValue(r[key], out var line)) { missing++; continue; }
            string now = line.text ?? "";
            if (original >= 0 && r.Count > original && now != r[original] && now != r[text])
            {
                skipped.Add($"row {i + 1} ({line.Where}): the line reads \"{now}\", the file had \"{r[original]}\"");
                continue;
            }
            if (r[text] != now) { SetText(line, r[text]); changed++; }
            if (line.rich && mumble >= 0 && person >= 0 && r.Count > Math.Max(mumble, person))
            {
                bool m = r[mumble] == "yes", p = r[person] == "yes";
                if (m != line.mumble || p != line.inPerson) { SetDelivery(line, m, p); changed++; }
            }
        }
        Undo.CollapseUndoOperations(group);
        if (skipped.Count > 0) Debug.LogWarning($"[Asset Studio] Dialogue import skipped {skipped.Count} row{(skipped.Count == 1 ? "" : "s")} whose line changed since the export:\n" + string.Join("\n", skipped));
        return $"{changed} changes" + (missing > 0 ? $", {missing} rows matched no line" : "")
            + (skipped.Count > 0 ? $", {skipped.Count} skipped (their line changed since the export; listed in the Console)" : "")
            + (original < 0 ? " (an older file without original text: rows were not checked)" : "");
    }

    static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else if (c != '\r' && c != '﻿') cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
