using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Events category's page (the user, 2026-11-12: events apart from the rooms, easy to see, edit and test). An event
// is a hand-built room with a ScriptedEventRoom (the duel, the toll bridge, the bell, the wager, the giant, the dice, the
// Portal hall, the Skeleton Party, Not a witch...). Its page, under the room's preview:
// - Try it: play the event (a new run starting at the way into its room), open the room to build, play from the room;
// - The event: its name for the console and what happens, edited in place;
// - its script's settings (every field of the event's own component: its numbers, its Dungeon Master lines, its
//   prizes), edited in place;
// - Who's in it: the characters the room puts down (sockets and placed characters), each opening in the library;
// - What they say: the conversation assets the script points at (Dialogue/Conversations), their boxes edited in place;
// - Comes up in: the biome pools that deal it, with their weights, and into more pools.
public partial class AssetStudioWindow
{
    // Components of an event room that are not its event (the room itself and its look).
    static readonly HashSet<Type> NotTheEvent = new() { typeof(DungeonRoomTemplate), typeof(ScriptedEventRoom), typeof(DungeonRoomMood), typeof(RoomMusic) };

    // The event's own scripts on the room's root (DuelEvent, TollBridgeEvent, SkeletonParty...).
    static IEnumerable<MonoBehaviour> EventScripts(GameObject room) =>
        room.GetComponents<MonoBehaviour>().Where(m => m != null && !NotTheEvent.Contains(m.GetType()));

    [NonSerialized] readonly Dictionary<string, bool> eventFolds = new();
    bool Fold(string key, string label, bool open = true)
    {
        if (!eventFolds.TryGetValue(key, out bool o)) o = open;
        o = EditorGUILayout.Foldout(o, label, true, new GUIStyle(EditorStyles.foldout) { font = gold.font, fontSize = gold.fontSize, normal = { textColor = gold.normal.textColor }, onNormal = { textColor = gold.normal.textColor } });
        eventFolds[key] = o;
        return o;
    }

    void EventPage(GameObject room)
    {
        var ev = room.GetComponent<ScriptedEventRoom>();
        string path = AssetDatabase.GetAssetPath(room);

        // ── Try it ──
        GUILayout.Label("Try it", gold);
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrWhiteSpace(ev.eventName) && Big("Play this event", $"A new run starting at the way into this room (the dev console's event {ev.eventName})."))
            Toast(GameLauncher.PlayEvent(ev.eventName));
        if (Big("Play from this room", "A new run that opens inside this room (the dev console's loadtemplate).")) Toast(GameLauncher.PlayRoom(room));
        if (Big("Open to build", "The room in Prefab Mode: move its pieces, its people and its markers.")) AssetDatabase.OpenAsset(room);
        GUILayout.EndHorizontal();
        if (room.GetComponent<DiceEvent>() != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Roll a", dim, GUILayout.Width(50));
            for (int f = 1; f <= 6; f++) { int face = f; if (GUILayout.Button(f.ToString(), GUILayout.Width(34), GUILayout.Height(24))) Toast(GameLauncher.PlayEvent(ev.eventName, face)); }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(6);

        // ── The event ──
        GUILayout.Label("The event", gold);
        using (var so = new SerializedObject(ev))
        {
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("eventName"), new GUIContent("Name", "Its name for the dev console's event command and Play this event."));
            var summary = so.FindProperty("summary");
            GUILayout.Label("What happens", dimLine);
            summary.stringValue = EditorGUILayout.TextArea(summary.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.MinHeight(38));
            EditorGUILayout.PropertyField(so.FindProperty("entrance"), new GUIContent("Test start", "Where Play this event puts the player (a marker in the room). Empty: the room's way in."));
            if (so.ApplyModifiedProperties()) Saved(room);
        }
        GUILayout.Space(6);

        // ── Its script's settings ──
        var scripts = EventScripts(room).ToList();
        if (scripts.Count == 0) GUILayout.Label("No event script on the room's root: it is an event room only for its portal or its kind.", Wrapped);
        foreach (var script in scripts)
        {
            if (!Fold(path + script.GetType().Name, ObjectNames.NicifyVariableName(script.GetType().Name))) continue;
            using var so = new SerializedObject(script);
            so.Update();
            var p = so.GetIterator();
            for (bool enter = true; p.NextVisible(enter); enter = false)
            {
                if (p.name == "m_Script") continue;
                if (!LineRow(p)) EditorGUILayout.PropertyField(p, true);
            }
            if (so.ApplyModifiedProperties()) Saved(room);
            GUILayout.Space(4);
        }

        // ── Who's in it ──
        var people = EventPeople(room);
        if (Fold(path + "people", $"Who's in it  ({people.Count})"))
        {
            if (people.Count == 0) GUILayout.Label("Nobody is placed in it: it takes its people from the floor's biome, or calls them in.", Wrapped);
            foreach (var (prefab, how) in people)
            {
                var data = CharacterDataOf(prefab);
                string numbers = "";
                if (data != null)
                {
                    float Stat(StatType t) { int i = data.stats.FindIndex(s => s.stat == t); return i >= 0 ? data.stats[i].baseValue : 0f; }
                    numbers = $"  <color=#b8ae9c>{Stat(StatType.MaxHealth):0} health · {Stat(StatType.AttackDamage):0.#} damage · {Stat(StatType.Armor):0} armour (before the floor and the event)</color>";
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{(data != null && !string.IsNullOrWhiteSpace(data.characterName) ? data.characterName : prefab.name)}{numbers}", cell, GUILayout.Height(22)))
                    ShowInLibrary(AssetDatabase.GetAssetPath(prefab), $"{prefab.name}: {how}.");
                GUILayout.Label(how, dimLine, GUILayout.Width(150));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
        }

        // ── What they say ──
        var talks = EventTalks(scripts);
        if (Fold(path + "talks", $"What they say  ({talks.Count})"))
        {
            if (talks.Count == 0) GUILayout.Label("No conversation asset: its lines are the script's own (above).", Wrapped);
            foreach (var talk in talks)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(talk.name, StudioSkin.Strong);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Show file", "Ping it in the Project window (Dialogue/Conversations)."), GUILayout.Width(90))) EditorGUIUtility.PingObject(talk);
                GUILayout.EndHorizontal();
                using var so = new SerializedObject(talk);
                so.Update();
                var p = so.GetIterator();
                for (bool enter = true; p.NextVisible(enter); enter = false)
                {
                    if (p.name == "m_Script") continue;
                    if (p.name == "introLines") { BoxRows(p); continue; }
                    // Only what is written: the empty lines of other kinds of speaker stay in the file.
                    if (p.propertyType == SerializedPropertyType.String && string.IsNullOrEmpty(p.stringValue) && p.name != "bossName") continue;
                    EditorGUILayout.PropertyField(p, true);
                }
                if (so.ApplyModifiedProperties()) { EditorUtility.SetDirty(talk); AssetDatabase.SaveAssetIfDirty(talk); }
                GUILayout.Space(6);
            }
        }

        // ── Comes up in ──
        GUILayout.Space(4);
        var dealt = World.LevelsOf(room).Where(d => d.IsDungeon).ToList();
        GUILayout.Label(dealt.Count == 0 ? "Comes up in  ·  no level deals it yet: put it in a pool below." : $"Comes up in  ·  {string.Join(", ", dealt.Select(d => $"{d.name} ({EventsLine(d)})"))}", Wrapped);
        InPools(room);

        // ── The room ──
        if (Fold(path + "room", "The room", false)) Glance(room.GetComponent<DungeonRoomTemplate>(), RoomGlance);
    }

    // A Dungeon Master line (DungeonMaster.Line) as one row: its name and its words (voice, mumble and in person stay in
    // the inspector on the right). False: not a line.
    static bool LineRow(SerializedProperty p)
    {
        if (p.propertyType != SerializedPropertyType.Generic || p.isArray) return false;
        var text = p.FindPropertyRelative("text");
        if (text == null || text.propertyType != SerializedPropertyType.String || p.FindPropertyRelative("inPerson") == null) return false;
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(p.displayName, p.tooltip), GUILayout.Width(EditorGUIUtility.labelWidth - 4));
        text.stringValue = EditorGUILayout.TextField(text.stringValue);
        GUILayout.EndHorizontal();
        return true;
    }

    // A conversation's boxes as rows: who says it and the words, its answers under it, each with its replies.
    void BoxRows(SerializedProperty lines)
    {
        GUILayout.Label("Boxes, in order", dimLine);
        for (int i = 0; i < lines.arraySize; i++)
        {
            var line = lines.GetArrayElementAtIndex(i);
            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(line.FindPropertyRelative("speaker"), GUIContent.none, GUILayout.Width(120));
            var text = line.FindPropertyRelative("text");
            text.stringValue = EditorGUILayout.TextArea(text.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true });
            if (GUILayout.Button(new GUIContent("✕", "Take this box out."), GUILayout.Width(28))) { lines.DeleteArrayElementAtIndex(i); GUILayout.EndHorizontal(); break; }
            GUILayout.EndHorizontal();
            var choices = line.FindPropertyRelative("choices");
            for (int c = 0; choices != null && c < choices.arraySize; c++)
            {
                var choice = choices.GetArrayElementAtIndex(c);
                GUILayout.BeginHorizontal();
                GUILayout.Space(124);
                GUILayout.Label("answer", dimLine, GUILayout.Width(56));
                var label = choice.FindPropertyRelative("label");
                label.stringValue = EditorGUILayout.TextField(label.stringValue);
                GUILayout.EndHorizontal();
                var reply = choice.FindPropertyRelative("reply");
                for (int k = 0; reply != null && k < reply.arraySize; k++)
                {
                    var rp = reply.GetArrayElementAtIndex(k);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(180);
                    EditorGUILayout.PropertyField(rp.FindPropertyRelative("speaker"), GUIContent.none, GUILayout.Width(110));
                    var rt = rp.FindPropertyRelative("text");
                    rt.stringValue = EditorGUILayout.TextField(rt.stringValue);
                    GUILayout.EndHorizontal();
                }
            }
        }
        if (GUILayout.Button("+ Add a box", GUILayout.Width(120), GUILayout.Height(22))) lines.arraySize++;
        GUILayout.Label("Answers, voices and the speaker's model: in the Dialogue view, or the file's inspector.", dimLine);
    }

    // The prefab saved after an edit of one of its components.
    void Saved(GameObject room) { EditorUtility.SetDirty(room); AssetDatabase.SaveAssetIfDirty(room); MarkStale(); }

    // The characters the room puts down: its Enemy sockets' own enemies and the characters placed in it.
    static List<(GameObject prefab, string how)> EventPeople(GameObject room)
    {
        var list = new List<(GameObject, string)>();
        foreach (var socket in room.GetComponentsInChildren<DungeonSocket>(true))
        {
            var so = new SerializedObject(socket);
            if (so.FindProperty("overridePrefab")?.objectReferenceValue is GameObject p && !list.Any(x => x.Item1 == p)) list.Add((p, "its enemy socket"));
        }
        foreach (var c in room.GetComponentsInChildren<Character>(true))
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);   // the nearest prefab (the troll, not the enemy base he is made from)
            if (source != null && !list.Any(x => x.Item1 == source)) list.Add((source, "placed in the room"));
        }
        return list;
    }

    // The conversation assets the event's scripts point at.
    static List<BossDialogueData> EventTalks(IEnumerable<MonoBehaviour> scripts)
    {
        var list = new List<BossDialogueData>();
        foreach (var s in scripts)
        {
            using var so = new SerializedObject(s);
            var p = so.GetIterator();
            for (bool enter = true; p.Next(enter); enter = false)
                if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue is BossDialogueData d && !list.Contains(d)) list.Add(d);
        }
        return list;
    }
}
