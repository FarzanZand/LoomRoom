using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Stats view (the user, 2026-10-30): the attributes and skills in one overview to tweak, over the whole
// width. Attributes: each one's score on the character sheet (base, levels to buy), what a point above the base gives
// (ProgressionManager > Meta progression > Tracks) and each class's own points. Skills: each one's ranks on the sheet,
// what a rank gives (its AdventureSkillData: the rank bonus and the second bonus at 100), its milestones and their
// abilities, and each class's starting ranks with each class's total. Then each class's health, mana and stamina, the
// sheet's prices level by level, and the levelling rules (AdventureRules). Everything is edited in place; the tracks are
// on the room's ProgressionManager (a scene object: save the scene after), the rest are assets saved at once.
public partial class AssetStudioWindow
{
    Vector2 statsScroll;
    bool showingStats => studioView == StudioView.Stats;

    void OpenStats() { studioView = StudioView.Stats; GUI.FocusControl(null); }

    // The room's manager when the scene is open (the one the game reads), else its prefab.
    ProgressionManager StatsManager(out bool inScene)
    {
        inScene = ProgressionManager.HasInstance && !EditorUtility.IsPersistent(ProgressionManager.Instance);
        return inScene ? ProgressionManager.Instance : AssetDatabase.LoadAssetAtPath<GameObject>(ProgressionManagerPath)?.GetComponent<ProgressionManager>();
    }

    void DrawStats(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        statsScroll = GUILayout.BeginScrollView(statsScroll);
        try { StatsPage(); }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    void StatsPage()
    {
        var pm = StatsManager(out bool inScene);
        var rules = pm != null ? pm.classRules : null;
        Title("Stats", "The attributes and skills, what each gives, and what every class starts with. Edit any number in place.");
        if (pm == null || rules == null) { GUILayout.Label("<color=#e0a85a>No ProgressionManager with class rules found.</color>", dim); return; }
        GUILayout.Label(inScene ? "The tracks (sheet levels, what a point gives, prices) are on the room's ProgressionManager in the open scene: save the scene after changing them. Classes and skills are assets, saved at once." : "<color=#e0a85a>The room scene isn't open: the tracks shown are the ProgressionManager prefab's, which the scene's own manager doesn't follow. Open Room.unity to edit the ones the game reads.</color>", Wrapped, GUILayout.Width(pageWidth));
        var classes = (rules.classes ?? Array.Empty<AdventurerClass>()).Where(c => c != null).ToList();
        void TrackChanged() { EditorUtility.SetDirty(pm); if (inScene) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pm.gameObject.scene); }
        void ClassChanged(AdventurerClass c) { EditorUtility.SetDirty(c); AssetDatabase.SaveAssetIfDirty(c); }
        const float nameW = 150, classW = 76;
        var head = new GUIStyle(gold) { fontSize = StudioSkin.Size - 2, wordWrap = false, clipping = TextClipping.Clip };

        // ── Attributes ──
        Section("ATTRIBUTES");
        GUILayout.Label("A score on the character sheet: its base, plus a point a level bought with spoils, plus the class's own points in a run and one a character level. Each point above the base gives what the next columns say.", Wrapped, GUILayout.Width(pageWidth));
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Attribute", head, GUILayout.Width(nameW));
        GUILayout.Label(new GUIContent("Base", "The score with nothing bought."), head, GUILayout.Width(50));
        GUILayout.Label(new GUIContent("Levels", "Levels on the sheet (given ones included): the most the sheet raises it."), head, GUILayout.Width(56));
        GUILayout.Label("A point above the base gives", head, GUILayout.Width(420));
        foreach (var c in classes) GUILayout.Label(new GUIContent(c.displayName, $"The {c.displayName}'s own points in this attribute, in its runs."), head, GUILayout.Width(classW));
        GUILayout.EndHorizontal();
        foreach (var t in pm.tracks.Where(t => t != null && t.kind == MetaTrackKind.Attribute))
        {
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            TrackIcon(t.icon);
            GUILayout.Label(t.displayName, StudioSkin.Strong, GUILayout.Width(nameW - 26));
            int bs = EditorGUILayout.DelayedIntField(t.baseScore, GUILayout.Width(44)); GUILayout.Space(6);
            int lv = Mathf.Max(1, EditorGUILayout.DelayedIntField(t.levels, GUILayout.Width(44))); GUILayout.Space(12);
            float b1 = EditorGUILayout.DelayedFloatField(t.bonusPerPoint, GUILayout.Width(40)); GUILayout.Label(new GUIContent(t.unit, t.unit), Unit, GUILayout.Width(96));
            float b2 = EditorGUILayout.DelayedFloatField(t.secondBonusPerPoint, GUILayout.Width(40)); GUILayout.Label(new GUIContent(string.IsNullOrEmpty(t.secondUnit) ? "-" : t.secondUnit, t.secondUnit), Unit, GUILayout.Width(96));
            float b3 = EditorGUILayout.DelayedFloatField(t.thirdBonusPerPoint, GUILayout.Width(40)); GUILayout.Label(new GUIContent(string.IsNullOrEmpty(t.thirdUnit) ? "-" : t.thirdUnit, t.thirdUnit), Unit, GUILayout.Width(96));
            if (bs != t.baseScore || lv != t.levels || !Mathf.Approximately(b1, t.bonusPerPoint) || !Mathf.Approximately(b2, t.secondBonusPerPoint) || !Mathf.Approximately(b3, t.thirdBonusPerPoint))
            { Undo.RecordObject(pm, "Attribute"); t.baseScore = bs; t.levels = lv; t.bonusPerPoint = b1; t.secondBonusPerPoint = b2; t.thirdBonusPerPoint = b3; TrackChanged(); }
            foreach (var c in classes)
            {
                int v = c.Attribute(t.attribute);
                int typed = EditorGUILayout.DelayedIntField(v, new GUIStyle(EditorStyles.numberField) { normal = { textColor = v > 0 ? new Color(.45f, .85f, .4f) : v < 0 ? new Color(.9f, .4f, .35f) : StudioSkin.Text } }, GUILayout.Width(classW - 8));
                GUILayout.Space(8);
                if (typed != v) { Undo.RecordObject(c, "Class attribute"); SetAttribute(c, t.attribute, typed); ClassChanged(c); }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(nameW + 50 + 56 + 420 + 18));
        foreach (var c in classes) { int sum = c.strength + c.dexterity + c.constitution + c.intelligence; GUILayout.Label($"sum {(sum > 0 ? "+" : "")}{sum}", dimLine, GUILayout.Width(classW)); }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Skills ──
        Section("SKILLS");
        GUILayout.Label("Ranks 0-100: the sheet gives levels of a few ranks each (some free), the class adds its starting ranks, and a run raises them by use. What a rank gives grows with it up to its value at 100.", Wrapped, GUILayout.Width(pageWidth));
        var data = (rules.skills ?? Array.Empty<AdventureSkillData>()).Where(x => x != null).ToList();
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Skill", head, GUILayout.Width(nameW));
        GUILayout.Label(new GUIContent("On the sheet", "Free levels / levels / ranks a level: it starts at free x ranks and reaches levels x ranks."), head, GUILayout.Width(150));
        GUILayout.Label(new GUIContent("At rank 100", "The rank bonus (and its second bonus) at rank 100; it grows with the rank."), head, GUILayout.Width(400));
        GUILayout.Label("Milestones", head, GUILayout.Width(170));
        foreach (var c in classes) GUILayout.Label(new GUIContent(c.displayName, $"The {c.displayName}'s starting ranks, on top of the sheet's."), head, GUILayout.Width(classW));
        GUILayout.EndHorizontal();
        foreach (var t in pm.tracks.Where(t => t != null && t.kind == MetaTrackKind.Skill))
        {
            var s = data.FirstOrDefault(x => x.skill == t.skill);
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            TrackIcon(t.icon != null ? t.icon : s?.icon);
            if (GUILayout.Button(t.displayName, cell, GUILayout.Width(nameW - 26), GUILayout.Height(20)) && s != null) { Selection.activeObject = s; EditorGUIUtility.PingObject(s); }
            int given = Mathf.Max(0, EditorGUILayout.DelayedIntField(t.givenLevels, GUILayout.Width(30)));
            int levels = Mathf.Max(1, EditorGUILayout.DelayedIntField(t.levels, GUILayout.Width(30)));
            int per = Mathf.Max(1, EditorGUILayout.DelayedIntField(t.perLevel, GUILayout.Width(30)));
            GUILayout.Label($"{t.givenLevels * t.perLevel}-{t.levels * t.perLevel}", Unit, GUILayout.Width(52));
            if (given != t.givenLevels || levels != t.levels || per != t.perLevel) { Undo.RecordObject(pm, "Skill track"); t.givenLevels = given; t.levels = levels; t.perLevel = per; TrackChanged(); }
            if (s != null)
            {
                float a = EditorGUILayout.DelayedFloatField(s.bonusAt100, GUILayout.Width(40));
                GUILayout.Label(new GUIContent(Shown(s.bonusText, s.bonusAt100 * 100f), Shown(s.bonusText, s.bonusAt100 * 100f)), Unit, GUILayout.Width(150));
                float e = EditorGUILayout.DelayedFloatField(s.extraAt100, GUILayout.Width(40));
                string extra = string.IsNullOrEmpty(s.extraText) ? "-" : Shown(s.extraText, s.extraFlat ? s.extraAt100 : s.extraAt100 * 100f);
                GUILayout.Label(new GUIContent(extra, extra), Unit, GUILayout.Width(150));
                if (!Mathf.Approximately(a, s.bonusAt100) || !Mathf.Approximately(e, s.extraAt100)) { Undo.RecordObject(s, "Skill bonus"); s.bonusAt100 = a; s.extraAt100 = e; EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); }
            }
            else GUILayout.Label("<color=#e0a85a>no skill data</color>", new GUIStyle(dim) { richText = true }, GUILayout.Width(400));
            string ms = t.milestones == null || t.milestones.Count == 0 ? "-" : string.Join(", ", t.milestones.Select(m => $"{m.value} {m.Name}"));
            GUILayout.Label(new GUIContent(ms, ms), new GUIStyle(dimLine) { clipping = TextClipping.Clip }, GUILayout.Width(170));
            foreach (var c in classes)
            {
                int v = c.StartingRank(t.skill);
                int typed = Mathf.Clamp(EditorGUILayout.DelayedIntField(v, GUILayout.Width(classW - 8)), 0, 100);
                GUILayout.Space(8);
                if (typed != v)
                {
                    Undo.RecordObject(c, "Starting rank");
                    if (c.skills == null || c.skills.Length < AdventureSkills.Count) Array.Resize(ref c.skills, AdventureSkills.Count);
                    c.skills[(int)t.skill] = typed; ClassChanged(c);
                }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(nameW + 150 + 400 + 170 + 18));
        var totals = classes.Select(c => c.skills?.Sum() ?? 0).ToList();
        int usual = totals.GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        for (int i = 0; i < classes.Count; i++) GUILayout.Label(totals[i] == usual ? $"total {totals[i]}" : $"<color=#e0a85a>total {totals[i]}</color>", new GUIStyle(dimLine) { richText = true }, GUILayout.Width(classW));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // ── Classes' pools ──
        Section("EACH CLASS STARTS WITH");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        GUILayout.Label("", head, GUILayout.Width(nameW));
        foreach (var c in classes)
        {
            bool locked = !string.IsNullOrEmpty(c.unlockFlag);
            if (GUILayout.Button(new GUIContent(c.displayName + (locked ? " *" : ""), locked ? "Unlocked in play (Unlockables)." : "There from the start."), cell, GUILayout.Width(classW), GUILayout.Height(20))) { Selection.activeObject = c; EditorGUIUtility.PingObject(c); }
        }
        GUILayout.EndHorizontal();
        void PoolRow(string label, Func<AdventurerClass, float> get, Action<AdventurerClass, float> set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(nameW));
            foreach (var c in classes)
            {
                float v = get(c), typed = Mathf.Max(0f, EditorGUILayout.DelayedFloatField(v, GUILayout.Width(classW - 8)));
                GUILayout.Space(8);
                if (!Mathf.Approximately(typed, v)) { Undo.RecordObject(c, label); set(c, typed); ClassChanged(c); }
            }
            GUILayout.EndHorizontal();
        }
        PoolRow("Health", c => c.health, (c, v) => c.health = v);
        PoolRow("Mana", c => c.mana, (c, v) => c.mana = v);
        PoolRow("Stamina", c => c.stamina, (c, v) => c.stamina = v);
        PoolRow("Health a level", c => c.healthPerLevel, (c, v) => c.healthPerLevel = v);
        PoolRow("Mana a level", c => c.manaPerLevel, (c, v) => c.manaPerLevel = v);
        GUILayout.Label("* unlocked in play. A level's health and mana are added from level 2 on. The run's totals also count the sheet's attributes and the rules below.", dim);
        GUILayout.EndVertical();

        // ── The sheet's prices ──
        Section("THE SHEET'S PRICES");
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("First level", "The spoils the first level costs on any track."), GUILayout.Width(110));
        int first = Mathf.Max(1, EditorGUILayout.DelayedIntField(pm.firstPrice, GUILayout.Width(50)));
        GUILayout.Space(16);
        GUILayout.Label(new GUIContent("Each level x", "Each level costs this much more than the one before."), GUILayout.Width(100));
        float mult = Mathf.Max(1f, EditorGUILayout.DelayedFloatField(pm.priceMultiplier, GUILayout.Width(50)));
        GUILayout.EndHorizontal();
        if (first != pm.firstPrice || !Mathf.Approximately(mult, pm.priceMultiplier)) { Undo.RecordObject(pm, "Sheet prices"); pm.firstPrice = first; pm.priceMultiplier = mult; TrackChanged(); }
        int most = pm.tracks.Where(t => t != null).Select(t => t.levels - t.givenLevels).DefaultIfEmpty(8).Max();
        float total = 0;
        var line = new System.Text.StringBuilder();
        for (int k = 0; k < most; k++) { float price = Mathf.Round(pm.firstPrice * Mathf.Pow(pm.priceMultiplier, k)); total += price; line.Append($"level {k + 1}: {price:0}   "); }
        GUILayout.Label(line.ToString(), Wrapped, GUILayout.Width(pageWidth - 20));
        GUILayout.Label($"A whole track bought: {total:0} spoils.", dim);
        GUILayout.EndVertical();

        // ── Levelling and attribute rules ──
        Section("RULES");
        GUILayout.BeginVertical(StudioSkin.Inset);
        using (var so = new SerializedObject(rules))
        {
            so.Update();
            // plain rows in two columns (the fields' own headers would repeat over each one)
            void Rule(string name, string label)
            {
                var p = so.FindProperty(name); if (p == null) return;
                GUILayout.BeginHorizontal(GUILayout.Width(330));
                GUILayout.Label(new GUIContent(label, p.tooltip), GUILayout.Width(200));
                p.floatValue = EditorGUILayout.DelayedFloatField(p.floatValue, GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.Label("Character levels", head);
            Rule("levelXp", "XP to the first level"); Rule("levelXpGrowth", "More XP each level"); Rule("xpPerEnemy", "XP an enemy"); Rule("xpPerFloor", "XP a new floor");
            GUILayout.Label("At rank 100 (legendary)", head);
            Rule("legendaryAttack", "Attack"); Rule("legendaryStamina", "Stamina");
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.Label("Each attribute point also gives", head);
            Rule("strengthAttack", "Strength: attack"); Rule("constitutionArmor", "Constitution: armour"); Rule("constitutionHealth", "Constitution: health");
            Rule("intelligenceMana", "Intelligence: mana"); Rule("intelligenceSpell", "Intelligence: spell power"); Rule("dexteritySpeed", "Dexterity: attack speed");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(rules);
        }
        GUILayout.EndVertical();
    }

    void TrackIcon(Sprite icon)
    {
        var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
        if (Event.current.type == EventType.Repaint && icon != null) GUI.DrawTexture(r, icon.texture, ScaleMode.ScaleToFit);
        GUILayout.Space(4);
    }

    static string Shown(string text, float value)
    {
        if (string.IsNullOrEmpty(text)) return value.ToString("0.#");
        try { return string.Format(text, value.ToString("0.#")); } catch (FormatException) { return text; }
    }

    static void SetAttribute(AdventurerClass c, MetaAttribute a, int v)
    {
        switch (a)
        {
            case MetaAttribute.Strength: c.strength = v; break;
            case MetaAttribute.Dexterity: c.dexterity = v; break;
            case MetaAttribute.Constitution: c.constitution = v; break;
            default: c.intelligence = v; break;
        }
    }
}
