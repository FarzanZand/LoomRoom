using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's class pages (the user, 2026-11-13: "add a page for classes in the asset studio, so I can edit them
// easily there too"). The Classes category lists every AdventurerClass (Characters/Classes) with its portrait and how it
// unlocks; picking one shows its page in the middle column: the portrait big, then everything the class is made of, each
// edited in place on the asset (saved at once, undoable): name and card text, starting pools and their gain a level, the
// four attribute points, starting ranks, the kit (worn pieces, pieces with set properties, supplies), the spell tomes and
// the readied spell, how it unlocks, its poster, desk statue and poster line, and its portraits. The Stats view edits the
// pools, attributes and ranks of the same assets, so the two always agree. Try it starts the Debug arena as the class
// (GameLauncher.PlayAsClass, DebugDungeonSession.StudioClassKey).
public partial class AssetStudioWindow
{
    Vector2 classScroll;
    const string PosterWallPath = "Assets/Game/World/Art/Class posters/Class poster wall.prefab";
    const string ItemPropertiesPath = "Assets/Game/Items/Data/Item Properties.asset";
    static readonly string[] AttributeShort = { "STR", "DEX", "CON", "INT" };
    static readonly MetaAttribute[] AttributeOrder = { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };

    // The class's page instead of the turntable (a class has no model of its own to turn).
    bool DrawClassPreview(Rect r)
    {
        if (studioView != StudioView.Category || selected == null || !(selected.Load() is AdventurerClass c)) return false;
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        classScroll = GUILayout.BeginScrollView(classScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        try { ClassPage(c); }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
        return true;
    }

    // The list's second line for a class: how it unlocks, then its pools.
    string ClassRowLine(AdventurerClass c, AssetStudioCatalog.Entry e) => ClassUnlockShort(c) + (string.IsNullOrEmpty(e.description) ? "" : " · " + e.description);

    // From the start, or what unlocks it, in a few words (the Unlockables view's reading of the game's data).
    string ClassUnlockShort(AdventurerClass c)
    {
        if (string.IsNullOrEmpty(c.unlockFlag)) return "From the start";
        var u = Unlockables().FirstOrDefault(x => x.type == "Class" && x.assets.Contains(c));
        if (u == null) return "Flag " + c.unlockFlag;
        var ways = new List<string>();
        foreach (var go in u.assets.OfType<GameObject>())
        {
            if (go.GetComponentInChildren<DuelEvent>(true) != null) ways.Add("Win the duel");
            else if (go.GetComponentInChildren<ArenaFight>(true) != null) ways.Add("Win the Arena");
            else ways.Add("Found in " + go.name);
        }
        if (ways.Count == 0 && u.assets.OfType<ItemData>().FirstOrDefault() is ItemData item) ways.Add("Pick up " + item.itemName);
        return ways.Count > 0 ? string.Join(" or ", ways.Distinct()) : "Nothing unlocks it";
    }

    void ClassPage(AdventurerClass c)
    {
        var so = new SerializedObject(c);
        so.Update();
        var pm = StatsManager(out _);
        var rules = pm != null ? pm.classRules : null;
        bool listed = rules != null && rules.classes != null && rules.classes.Contains(c);
        var head = new GUIStyle(gold) { fontSize = StudioSkin.Size - 2, wordWrap = false, clipping = TextClipping.Clip };
        float right = Mathf.Max(260, pageWidth - 254 - 14);

        // ── Portrait, name, card text, unlock, Try it ──
        GUILayout.BeginHorizontal();
        var portrait = GUILayoutUtility.GetRect(240, 320, GUILayout.Width(240), GUILayout.Height(320));
        if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(portrait, new Color(0, 0, 0, .25f)); DrawSprite(portrait, c.portrait); }
        GUILayout.Space(14);
        GUILayout.BeginVertical(GUILayout.Width(right));
        GUILayout.Label(string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName, titleStyle);
        GUILayout.Label($"Save id {c.id}", dim);
        if (!listed) GUILayout.Label("<color=#e0a85a>Not in the class list (ProgressionManager > class rules): not in the game</color>", new GUIStyle(dim) { richText = true });
        GUILayout.Space(6);
        TextRow(so.FindProperty("displayName"), "Name", right);
        GUILayout.Label("Card text", StudioSkin.Strong);
        var desc = so.FindProperty("description");
        string typedDesc = EditorGUILayout.TextArea(desc.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Width(right), GUILayout.Height(84));
        if (typedDesc != desc.stringValue) desc.stringValue = typedDesc;
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Unlock", StudioSkin.Strong, GUILayout.Width(70));
        GUILayout.Label(ClassUnlockShort(c), GUILayout.Width(right - 74));
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button(new GUIContent("Try it in the arena", "The Debug arena as this class, with its kit and spells."), GUILayout.Height(32), GUILayout.Width(240)))
            Toast(GameLauncher.PlayAsClass(c));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        // ── Pools ──
        Section("STARTS WITH");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        FloatRow(so.FindProperty("health"), "Health", 60, 50);
        FloatRow(so.FindProperty("mana"), "Mana", 50, 50);
        FloatRow(so.FindProperty("stamina"), "Stamina", 64, 50);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        FloatRow(so.FindProperty("healthPerLevel"), "Health a level", 110, 50);
        FloatRow(so.FindProperty("manaPerLevel"), "Mana a level", 100, 50);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Attributes ──
        Section("ATTRIBUTES");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        string[] attributeFields = { "strength", "dexterity", "constitution", "intelligence" };
        int sum = 0;
        for (int i = 0; i < attributeFields.Length; i++)
        {
            var p = so.FindProperty(attributeFields[i]);
            int v = p.intValue; sum += v;
            GUILayout.Label(new GUIContent(AttributeShort[i], AttributeOrder[i].ToString()), StudioSkin.Strong, GUILayout.Width(34));
            var style = new GUIStyle(EditorStyles.numberField) { normal = { textColor = v > 0 ? new Color(.45f, .85f, .4f) : v < 0 ? new Color(.9f, .4f, .35f) : StudioSkin.Text } };
            int typedAttribute = EditorGUILayout.DelayedIntField(v, style, GUILayout.Width(40));
            if (typedAttribute != v) p.intValue = typedAttribute;
            GUILayout.Space(22);
        }
        GUILayout.Label($"sum {(sum > 0 ? "+" : "")}{sum}", dim);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Starting ranks ──
        Section("STARTING RANKS");
        GUILayout.BeginVertical(StudioSkin.Inset);
        // Older classes have fewer entries than skills: a missing one reads 0. Drawing never writes: the list only grows
        // when a rank is typed, and the entries it adds are set to 0 (a grown list copies its last entry).
        var skills = so.FindProperty("skills");
        var skillData = rules != null && rules.skills != null ? rules.skills.Where(x => x != null).ToList() : new List<AdventureSkillData>();
        int total = 0;
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int k = row * 4; k < row * 4 + 4 && k < AdventureSkills.Count; k++)
            {
                var skill = AdventureSkills.All[k];
                var data = skillData.FirstOrDefault(x => x.skill == skill);
                TrackIcon(data != null ? data.icon : null);
                GUILayout.Label(data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : skill.ToString(), GUILayout.Width(84));
                int index = (int)skill;
                int v = index < skills.arraySize ? skills.GetArrayElementAtIndex(index).intValue : 0;
                int typed = EditorGUILayout.DelayedIntField(v, GUILayout.Width(36));
                if (typed != v)
                {
                    typed = Mathf.Clamp(typed, 0, 100);
                    for (int grow = skills.arraySize; grow <= index; grow++) { skills.arraySize = grow + 1; skills.GetArrayElementAtIndex(grow).intValue = 0; }
                    skills.GetArrayElementAtIndex(index).intValue = typed;
                    v = typed;
                }
                total += v;
                GUILayout.Space(8);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.Label($"Total {total}", dim);
        GUILayout.EndVertical();

        // ── Kit ──
        Section("KIT");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.Label("Worn and held", head);
        ItemList(so.FindProperty("equipment"), "+ Add piece");
        GUILayout.Space(6);
        GUILayout.Label("With set properties", head);
        RolledPieces(so.FindProperty("rolledEquipment"));
        GUILayout.Space(6);
        GUILayout.Label("Supplies", head);
        Supplies(so.FindProperty("supplies"));
        GUILayout.EndVertical();

        // ── Spells ──
        Section("SPELLS");
        GUILayout.BeginVertical(StudioSkin.Inset);
        ItemList(so.FindProperty("spells"), "+ Add tome");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Readied", StudioSkin.Strong, GUILayout.Width(70));
        EditorGUILayout.PropertyField(so.FindProperty("leftHand"), GUIContent.none, GUILayout.Width(200));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Unlock ──
        Section("UNLOCK");
        GUILayout.BeginVertical(StudioSkin.Inset);
        TextRow(so.FindProperty("unlockFlag"), "Flag", 300, "Empty: there from the start.");
        if (!string.IsNullOrEmpty(c.unlockFlag))
        {
            var u = Unlockables().FirstOrDefault(x => x.type == "Class" && x.assets.Contains(c));
            if (u != null) GUILayout.Label(u.how, new GUIStyle(Wrapped) { richText = true }, GUILayout.Width(pageWidth - 20));
            foreach (var item in u != null ? u.assets.OfType<ItemData>() : Enumerable.Empty<ItemData>())
            {
                GUILayout.BeginHorizontal();
                var ir = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                if (Event.current.type == EventType.Repaint) DrawSprite(ir, item.icon);
                if (GUILayout.Button($"{item.itemName}  <color=#b8ae9c>unlocks it when picked up</color>", cell, GUILayout.Height(22))) ShowItem(item);
                GUILayout.EndHorizontal();
            }
        }
        else GUILayout.Label("From the start", dim);
        GUILayout.EndVertical();

        // ── Poster wall ──
        Section("POSTER AND DESK");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        var poster = PosterTexture(c);
        GUILayout.BeginVertical(GUILayout.Width(150));
        GUILayout.Label("Poster", head);
        var pr = GUILayoutUtility.GetRect(150, 200, GUILayout.Width(150), GUILayout.Height(200));
        if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(pr, new Color(0, 0, 0, .25f)); if (poster != null) GUI.DrawTexture(pr, poster, ScaleMode.ScaleToFit); }
        if (poster == null) GUILayout.Label("none on the wall", dim);
        GUILayout.EndVertical();
        GUILayout.Space(12);
        ModelSlot(so.FindProperty("deskProp"), "Desk statue");
        GUILayout.Space(12);
        ModelSlot(so.FindProperty("miniature"), "Miniature");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label("Poster line", head);
        var line = so.FindProperty("posterLine");
        var text = line.FindPropertyRelative("text");
        string typedLine = EditorGUILayout.TextArea(text.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Width(Mathf.Min(560, pageWidth - 20)), GUILayout.Height(44));
        if (typedLine != text.stringValue) text.stringValue = typedLine;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Voice", GUILayout.Width(44));
        EditorGUILayout.PropertyField(line.FindPropertyRelative("voice"), GUIContent.none, GUILayout.Width(200));
        GUILayout.Space(10);
        var mumble = line.FindPropertyRelative("mumble");
        bool typedMumble = EditorGUILayout.ToggleLeft("Mumble", mumble.boolValue, GUILayout.Width(80));
        if (typedMumble != mumble.boolValue) mumble.boolValue = typedMumble;
        var inPerson = line.FindPropertyRelative("inPerson");
        bool typedInPerson = EditorGUILayout.ToggleLeft("In person", inPerson.boolValue, GUILayout.Width(90));
        if (typedInPerson != inPerson.boolValue) inPerson.boolValue = typedInPerson;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Portraits ──
        Section("PORTRAITS");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        SpriteSlot(so.FindProperty("portrait"), "Card", 120, 160);
        GUILayout.Space(10);
        SpriteSlot(so.FindProperty("sheetPortrait"), "Sheet", 120, 160);
        GUILayout.Space(10);
        SpriteSlot(so.FindProperty("sheetPortraitColour"), "Sheet, colour", 120, 160);
        GUILayout.Space(10);
        SpriteSlot(so.FindProperty("face"), "Face", 120, 120);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        if (so.ApplyModifiedProperties())
        {
            AssetDatabase.SaveAssetIfDirty(c);
            unlockables = null;
            MarkStale();
        }
        so.Dispose();
    }

    // ── Rows ──

    void TextRow(SerializedProperty p, string label, float width, string tip = null)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(label, tip), StudioSkin.Strong, GUILayout.Width(70));
        string typed = EditorGUILayout.DelayedTextField(p.stringValue, GUILayout.Width(Mathf.Max(120, width - 74)));
        if (typed != p.stringValue) p.stringValue = typed;
        GUILayout.EndHorizontal();
    }

    void FloatRow(SerializedProperty p, string label, float labelWidth, float fieldWidth)
    {
        GUILayout.Label(label, StudioSkin.Strong, GUILayout.Width(labelWidth));
        float typed = EditorGUILayout.DelayedFloatField(p.floatValue, GUILayout.Width(fieldWidth));
        if (typed != p.floatValue) p.floatValue = Mathf.Max(0f, typed);
        GUILayout.Space(18);
    }

    // A list of items, each its icon, the item and its kind and main number, a cross to take it out, and + Add.
    void ItemList(SerializedProperty list, string add)
    {
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var p = list.GetArrayElementAtIndex(i);
            var item = p.objectReferenceValue as ItemData;
            GUILayout.BeginHorizontal();
            ItemIcon(item);
            EditorGUILayout.PropertyField(p, GUIContent.none, GUILayout.Width(240));
            GUILayout.Label(ItemNote(item), dim, GUILayout.Width(220));
            if (GUILayout.Button("✕", GUILayout.Width(28))) remove = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        if (list.arraySize == 0) GUILayout.Label("none", dim);
        if (remove >= 0) RemoveAt(list, remove);
        if (GUILayout.Button(add, GUILayout.Width(120), GUILayout.Height(24))) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = null; }
    }

    // The pieces above Common with set properties: item, rarity, then each property's id and value.
    void RolledPieces(SerializedProperty list)
    {
        var table = AssetDatabase.LoadAssetAtPath<ItemPropertyTable>(ItemPropertiesPath);
        var ids = table != null && table.properties != null ? table.properties.Where(d => d != null && !string.IsNullOrEmpty(d.id)).Select(d => d.id).ToArray() : Array.Empty<string>();
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var piece = list.GetArrayElementAtIndex(i);
            var itemProp = piece.FindPropertyRelative("item");
            var item = itemProp.objectReferenceValue as ItemData;
            GUILayout.BeginHorizontal();
            ItemIcon(item);
            EditorGUILayout.PropertyField(itemProp, GUIContent.none, GUILayout.Width(240));
            EditorGUILayout.PropertyField(piece.FindPropertyRelative("rarity"), GUIContent.none, GUILayout.Width(110));
            GUILayout.Label(ItemNote(item), dim, GUILayout.Width(110));
            if (GUILayout.Button("✕", GUILayout.Width(28))) remove = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            var props = piece.FindPropertyRelative("properties");
            int removeProp = -1;
            for (int k = 0; k < props.arraySize; k++)
            {
                var rp = props.GetArrayElementAtIndex(k);
                var id = rp.FindPropertyRelative("id");
                var value = rp.FindPropertyRelative("value");
                GUILayout.BeginHorizontal();
                GUILayout.Space(32);
                int at = Array.IndexOf(ids, id.stringValue);
                if (ids.Length > 0 && (at >= 0 || string.IsNullOrEmpty(id.stringValue)))
                {
                    int picked = EditorGUILayout.Popup(at, ids, GUILayout.Width(160));
                    if (picked != at && picked >= 0) id.stringValue = ids[picked];
                }
                else { string typedId = EditorGUILayout.DelayedTextField(id.stringValue, GUILayout.Width(160)); if (typedId != id.stringValue) id.stringValue = typedId; }
                float typedValue = EditorGUILayout.DelayedFloatField(value.floatValue, GUILayout.Width(56));
                if (typedValue != value.floatValue) value.floatValue = typedValue;
                var def = table != null ? table.Find(id.stringValue) : null;
                GUILayout.Label(def != null ? def.stat.ToString() : "", dim, GUILayout.Width(150));
                if (GUILayout.Button("✕", GUILayout.Width(28))) removeProp = k;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (removeProp >= 0) props.DeleteArrayElementAtIndex(removeProp);
            GUILayout.BeginHorizontal();
            GUILayout.Space(32);
            if (GUILayout.Button("+ Add property", GUILayout.Width(130), GUILayout.Height(22)))
            {
                props.arraySize++;
                var added = props.GetArrayElementAtIndex(props.arraySize - 1);
                added.FindPropertyRelative("id").stringValue = ids.Length > 0 ? ids[0] : "";
                added.FindPropertyRelative("value").floatValue = 1f;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }
        if (list.arraySize == 0) GUILayout.Label("none", dim);
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("+ Add piece", GUILayout.Width(120), GUILayout.Height(24)))
        {
            list.arraySize++;
            var added = list.GetArrayElementAtIndex(list.arraySize - 1);
            added.FindPropertyRelative("item").objectReferenceValue = null;
            added.FindPropertyRelative("rarity").intValue = (int)ItemRarity.Rare;
            added.FindPropertyRelative("properties").arraySize = 0;
        }
    }

    void Supplies(SerializedProperty list)
    {
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var stack = list.GetArrayElementAtIndex(i);
            var itemProp = stack.FindPropertyRelative("item");
            var count = stack.FindPropertyRelative("count");
            GUILayout.BeginHorizontal();
            ItemIcon(itemProp.objectReferenceValue as ItemData);
            EditorGUILayout.PropertyField(itemProp, GUIContent.none, GUILayout.Width(240));
            GUILayout.Label("x", dim, GUILayout.Width(12));
            int typedCount = EditorGUILayout.DelayedIntField(count.intValue, GUILayout.Width(40));
            if (typedCount != count.intValue) count.intValue = Mathf.Max(1, typedCount);
            if (GUILayout.Button("✕", GUILayout.Width(28))) remove = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        if (list.arraySize == 0) GUILayout.Label("none", dim);
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("+ Add supply", GUILayout.Width(120), GUILayout.Height(24)))
        {
            list.arraySize++;
            var added = list.GetArrayElementAtIndex(list.arraySize - 1);
            added.FindPropertyRelative("item").objectReferenceValue = null;
            added.FindPropertyRelative("count").intValue = 1;
            added.FindPropertyRelative("used").intValue = 0;
        }
    }

    static void RemoveAt(SerializedProperty list, int i)
    {
        var p = list.GetArrayElementAtIndex(i);
        if (p.propertyType == SerializedPropertyType.ObjectReference) p.objectReferenceValue = null;
        list.DeleteArrayElementAtIndex(i);
    }

    static string ItemNote(ItemData item)
    {
        if (item == null) return "";
        string main = "";
        try { if (item.TryMainStat(out _, out _, out string text)) main = (item.IsWeapon ? "damage " : "armour ") + text; } catch { }
        string kind = AssetStudioCatalog.ItemKind(item);
        return string.IsNullOrEmpty(main) ? kind : kind + " · " + main;
    }

    void ItemIcon(ItemData item)
    {
        var r = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
        if (Event.current.type == EventType.Repaint && item != null) DrawSprite(r, item.icon);
        GUILayout.Space(4);
    }

    // A prefab field with Unity's preview of it above.
    void ModelSlot(SerializedProperty p, string label)
    {
        GUILayout.BeginVertical(GUILayout.Width(150));
        GUILayout.Label(label, new GUIStyle(gold) { fontSize = StudioSkin.Size - 2 });
        var r = GUILayoutUtility.GetRect(150, 150, GUILayout.Width(150), GUILayout.Height(150));
        var go = p.objectReferenceValue;
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, new Color(0, 0, 0, .25f));
            var tex = go != null ? AssetPreview.GetAssetPreview(go) : null;
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
        }
        if (go != null && AssetPreview.IsLoadingAssetPreviews()) Repaint();
        EditorGUILayout.PropertyField(p, GUIContent.none, GUILayout.Width(150));
        GUILayout.EndVertical();
    }

    void SpriteSlot(SerializedProperty p, string label, float w, float h)
    {
        GUILayout.BeginVertical(GUILayout.Width(w));
        GUILayout.Label(label, new GUIStyle(gold) { fontSize = StudioSkin.Size - 2 });
        var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
        if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(r, new Color(0, 0, 0, .25f)); DrawSprite(r, p.objectReferenceValue as Sprite); }
        var typedSprite = EditorGUILayout.ObjectField(p.objectReferenceValue, typeof(Sprite), false, GUILayout.Width(w));
        if (typedSprite != p.objectReferenceValue) p.objectReferenceValue = typedSprite;
        GUILayout.EndVertical();
    }

    // A sprite drawn whole, fitted to the rect (sprites can be parts of a sheet).
    static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) return;
        var t = s.texture;
        var tr = s.textureRect;
        var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
        float aspect = tr.width / Mathf.Max(1f, tr.height);
        var fit = r;
        if (r.width / r.height > aspect) { fit.width = r.height * aspect; fit.x = r.x + (r.width - fit.width) / 2; }
        else { fit.height = r.width / aspect; fit.y = r.y + (r.height - fit.height) / 2; }
        GUI.DrawTextureWithTexCoords(fit, t, uv);
    }

    // The class's poster picture, from the poster wall prefab's entry for it (its paper, not its tape).
    static Texture PosterTexture(AdventurerClass c)
    {
        var wall = AssetDatabase.LoadAssetAtPath<GameObject>(PosterWallPath)?.GetComponent<ClassPosterWall>();
        var entry = wall != null && wall.posters != null ? wall.posters.FirstOrDefault(x => x != null && x.adventurerClass == c) : null;
        if (entry == null || entry.poster == null) return null;
        foreach (var r in entry.poster.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial;
            if (m != null && m.mainTexture != null && !m.name.ToLowerInvariant().Contains("tape")) return m.mainTexture;
        }
        return null;
    }

    // Shows an item in the Items category.
    void ShowItem(ItemData item)
    {
        var e = categories.SelectMany(x => x.entries).FirstOrDefault(x => x.path == AssetDatabase.GetAssetPath(item));
        if (e != null) ShowEntry(e);
    }
}
