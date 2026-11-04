using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Asset Studio's Balance view (the user, 2026-11-04: "a section for armor balancing where you can tweak things like armor,
// scaling of items per tier / rarity, enemy scaling per floor, drop rates"; then "make the UX on all balance pages easier
// to work with, and understand"). Five tabs, each answering one question: inputs on the left (the real values, edited in
// place and undoable, each with its unit, the value it had when the page first showed it and a reset, and a link to where
// it lives), results on the right (said per what, totals that add up, "1 in N" by small chances, the outcome a designer
// looks for in big letters), the cells an edit changed lit until the next edit with their old value, and the last edit
// said under the tabs. Previews are the picked dungeon's floors (the Dungeon view's pick, Dungeon1). The managers are the
// room's scene objects (Save scene after), the rest are assets saved at once. The Weapons tab and the fights live in
// AssetStudioWindow.Balance.Weapons.cs.
public partial class AssetStudioWindow
{
    Vector2 balanceScroll;
    string balanceTab = "Armour";
    int balanceKind;
    bool showingBalance => studioView == StudioView.Balance;
    static readonly string[] BalanceTabs = { "Armour", "Weapons", "Items", "Enemies", "Drops" };
    static readonly ItemRarity[] GearRarities = { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Unique, ItemRarity.Legendary };
    const string CombatManagerPath = "Assets/Game/Combat/Prefabs/CombatManager.prefab";
    const string TablePlayerPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";

    void OpenBalance() { studioView = StudioView.Balance; GUI.FocusControl(null); }

    // ── The page's kit: inputs that remember, results that light up ──

    enum BalUnit { None, Percent, PercentPerFloor, Times, PerFloor, Seconds }
    static string UnitText(BalUnit u) => u switch { BalUnit.Percent => "%", BalUnit.PercentPerFloor => "% a floor", BalUnit.Times => "x", BalUnit.PerFloor => "a floor", BalUnit.Seconds => "s", _ => "" };
    static bool Percentish(BalUnit u) => u == BalUnit.Percent || u == BalUnit.PercentPerFloor;
    static string Shown(float v, BalUnit u) => Percentish(u) ? $"{v * 100f:0.##}%" : u == BalUnit.Times ? $"x{v:0.###}" : v.ToString("0.###");

    // Each field's value when the page first showed it (this editor session): shown as "was" and reset to.
    static readonly Dictionary<string, float> balanceBaseline = new();
    // Result cells: this repaint's, the last repaint's, and the ones just before the last edit.
    readonly Dictionary<string, string> cellsNow = new(), cellsShown = new();
    Dictionary<string, string> cellsBefore = new();
    string lastEdit;
    static AssetStudioWindow balanceWindow;

    // The group a field is drawn in (Group): two fields of one name in different groups are different fields.
    string balanceGroup = "";
    string KeyOf(UnityEngine.Object o, string label) => (o != null ? o.name + "/" + o.GetType().Name : "-") + "/" + balanceGroup + "/" + label;

    // An edit is about to be written: the results as they were are kept to light up what it changes.
    static int balanceVersion;
    static void Edited(string what)
    {
        balanceVersion++;
        // Rolled copies are kept by their roll, not by the item's numbers: forget them so the next roll reads the edit.
        if (!EditorApplication.isPlaying) ItemData.ForgetVariants();
        var w = balanceWindow;
        if (w == null) return;
        w.cellsBefore = new Dictionary<string, string>(w.cellsShown);
        w.lastEdit = what;
    }

    static void Changed(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        if (o is Component c && !EditorUtility.IsPersistent(c)) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        else AssetDatabase.SaveAssetIfDirty(o);
    }

    // A labelled input: the field with its unit, and "was" with a reset once it differs from the value the page first showed.
    void In(string label, float value, UnityEngine.Object owner, Action<float> set, BalUnit unit = BalUnit.None, float min = 0f, float max = float.MaxValue, float labelWidth = 150, string tip = null)
    {
        string key = KeyOf(owner, label);
        if (!balanceBaseline.ContainsKey(key)) balanceBaseline[key] = value;
        float was = balanceBaseline[key];
        GUILayout.BeginHorizontal(GUILayout.Height(20));
        GUILayout.Label(new GUIContent(label, tip), GUILayout.Width(labelWidth));
        NumField(value, owner, set, Percentish(unit), min, max, label, 56);
        GUILayout.Label(UnitText(unit), Unit, GUILayout.Width(unit == BalUnit.PercentPerFloor ? 62 : 34));
        if (!Mathf.Approximately(was, value))
        {
            GUILayout.Label($"<color=#e0a85a>was {Shown(was, unit)}</color>", new GUIStyle(StudioSkin.Faint) { richText = true, wordWrap = false }, GUILayout.Width(80));
            if (GUILayout.Button(new GUIContent("↺", "Back to " + Shown(was, unit)), GUILayout.Width(24), GUILayout.Height(18))) { Edited($"{label} {Shown(value, unit)} → {Shown(was, unit)}"); Undo.RecordObject(owner, label); set(was); Changed(owner); }
        }
        GUILayout.EndHorizontal();
    }

    // A small link to where a group's values live (selects it).
    void Where(UnityEngine.Object o, string text = null)
    {
        if (o == null) return;
        if (GUILayout.Button(new GUIContent("in " + (text ?? o.name), "Select it"), new GUIStyle(StudioSkin.Faint) { normal = { textColor = new Color(.6f, .75f, .9f) }, wordWrap = false }, GUILayout.ExpandWidth(false)))
        { Selection.activeObject = o; EditorGUIUtility.PingObject(o); }
    }

    // A group of inputs: its title and where it lives.
    void Group(string title, UnityEngine.Object where = null, string whereText = null)
    {
        balanceGroup = title;
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, gold);
        GUILayout.FlexibleSpace();
        Where(where, whereText);
        GUILayout.EndHorizontal();
    }

    // Just the field (for table cells); an edit is said and lights up what it changes.
    static float NumField(float value, UnityEngine.Object owner, Action<float> set, bool percent = false, float min = 0f, float max = float.MaxValue, string undo = "Balance", float width = 52)
    {
        float shown = percent ? Mathf.Round(value * 10000f) / 100f : value;
        float entered = EditorGUILayout.DelayedFloatField(shown, GUILayout.Width(width));
        // Only what was typed is written: a value shown rounded is never written back as its rounding.
        if (owner == null || Mathf.Approximately(entered, shown)) return value;
        float typed = Mathf.Clamp(percent ? entered / 100f : entered, min, max);
        Edited($"{undo} {(percent ? shown + "%" : shown.ToString("0.###"))} → {(percent ? typed * 100f + "%" : typed.ToString("0.###"))}");
        Undo.RecordObject(owner, undo); set(typed); Changed(owner);
        return typed;
    }

    static string Pct(float p) => p <= 0f ? "-" : p < .001f ? "<0.1%" : $"{p * 100f:0.#}%";
    // A small chance as "1 in N" too.
    static string OneIn(float p) => p <= 0f || p >= .5f ? "" : $"1 in {Mathf.Max(1f, 1f / p):0}";
    static string Every(float perFloor) => perFloor <= 0f ? "none" : perFloor >= 1f ? $"{perFloor:0.#} a floor" : $"1 every {1f / perFloor:0.#} floors";

    // A heading cell, optionally in a rarity's colour.
    void Head(string text, float width, ItemRarity? rarity = null)
    {
        var style = new GUIStyle(gold) { fontSize = StudioSkin.Size - 2, alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip, richText = true };
        if (rarity.HasValue) text = $"<color=#{RarityHex(rarity.Value)}>{text}</color>";
        GUILayout.Label(text, style, GUILayout.Width(width), GUILayout.Height(20));
    }
    static string RarityHex(ItemRarity r) => r == ItemRarity.Common ? "e6e6e6" : ColorUtility.ToHtmlStringRGB(UIManager.RarityColor(r));

    // A result cell: right-aligned; lit (with its old value after it in grey) when the last edit changed it.
    void Out(string key, string text, float width, bool strong = false)
    {
        if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) cellsNow[key] = text;
        bool changed = cellsBefore.TryGetValue(key, out var old) && old != text;
        var r = GUILayoutUtility.GetRect(width, 20, GUILayout.Width(width), GUILayout.Height(20));
        if (changed && Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, new Color(.72f, .565f, .24f, .22f));
        var style = new GUIStyle(strong ? StudioSkin.Strong : StudioSkin.Label) { alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip, richText = true };
        GUI.Label(r, new GUIContent(changed ? $"{text} <color=#8f8a80>{old}</color>" : text, changed ? "was " + old : null), style);
    }

    // The tab's question, in big letters.
    void Question(string text) => GUILayout.Label(text, new GUIStyle(StudioSkin.Title) { fontSize = StudioSkin.Size + 6 });

    // An outcome in big letters.
    void Headline(string text) => GUILayout.Label(text, new GUIStyle(StudioSkin.Label) { fontSize = StudioSkin.Size + 4, richText = true, wordWrap = true });

    // ── Shared lookups ──

    enum GearKind { Sword, Mace, Shield, Helm, Body, Gloves, Boots, Legs }
    static readonly string[] GearKindNames = { "Sword", "Mace", "Shield", "Helm", "Body", "Gloves", "Boots", "Legs" };

    static GearKind? KindOf(ItemData i)
    {
        if (i == null || !i.IsGear) return null;
        if (i.IsWeapon) return i.weaponCategory == WeaponCategory.Mace ? GearKind.Mace : GearKind.Sword;
        if (i.itemType == ItemType.Shield) return GearKind.Shield;
        return i.equipSlot switch
        {
            EquipmentSlot.Head => (GearKind?)GearKind.Helm, EquipmentSlot.Body => GearKind.Body, EquipmentSlot.Gloves => GearKind.Gloves,
            EquipmentSlot.Boots => GearKind.Boots, EquipmentSlot.Legs => GearKind.Legs, _ => null,
        };
    }

    // The scene's manager when the room is open (the one the game reads), else its prefab's.
    static T BalanceManager<T>(string prefabPath, out bool inScene) where T : Component
    {
        var found = UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        inScene = found != null && !EditorUtility.IsPersistent(found);
        return inScene ? found : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)?.GetComponent<T>();
    }

    // A main number raised to a rarity, the game's own rule (ItemData.RaisedMain: whole numbers, weapons a share of the bonus).
    static MethodInfo raisedMain;
    static float Raised(float value, ItemRarity rarity, bool weapon)
    {
        if (rarity == ItemRarity.Common) return value;
        raisedMain ??= typeof(ItemData).GetMethod("RaisedMain", BindingFlags.NonPublic | BindingFlags.Static);
        if (raisedMain == null) return value;
        var args = raisedMain.GetParameters().Length == 3 ? new object[] { value, rarity, weapon } : new object[] { value, rarity };
        return (float)raisedMain.Invoke(null, args);
    }

    // The modifier holding the item's main number (its damage or armour).
    static int MainIndex(ItemData i)
    {
        var main = i != null ? i.MainStatType : null;
        if (main == null || i.statModifiers == null) return -1;
        for (int k = 0; k < i.statModifiers.Length; k++) if (i.statModifiers[k] != null && i.statModifiers[k].stat == main && i.statModifiers[k].type == ModifierType.Flat) return k;
        return -1;
    }

    // A tier's name for a kind of gear: tier 1 is Bronze for weapons and shields, Leather for worn armour (both when the
    // kind is not given).
    static string TierName(int tier, GearKind? kind) =>
        tier != 1 ? LootFloorTiers.Names[tier - 1] : kind == null ? "Bronze/Leather" : kind is GearKind.Sword or GearKind.Mace or GearKind.Shield ? "Bronze" : "Leather";

    // The plain (Common) item of each kind and tier in the catalog.
    static Dictionary<(GearKind, int), ItemData> TierItems(InventoryManager im)
    {
        var map = new Dictionary<(GearKind, int), ItemData>();
        if (im == null || im.itemCatalog == null) return map;
        foreach (var i in im.itemCatalog)
        {
            if (i == null || i.tier <= 0 || i.rarity != ItemRarity.Common) continue;
            var k = KindOf(i);
            if (k == null || map.ContainsKey((k.Value, i.tier))) continue;
            map[(k.Value, i.tier)] = i;
        }
        return map;
    }

    // ── The page ──

    void DrawBalance(Rect r)
    {
        balanceWindow = this;
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        // Up and down only: a page that also scrolled sideways hid its right edge.
        balanceScroll = GUILayout.BeginScrollView(balanceScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
        if (Event.current.type == EventType.Layout) cellsNow.Clear();
        try { BalancePage(); }
        finally
        {
            GUILayout.EndScrollView(); GUILayout.EndArea();
            if (Event.current.type == EventType.Repaint) { cellsShown.Clear(); foreach (var kv in cellsNow) cellsShown[kv.Key] = kv.Value; }
        }
    }

    void BalancePage()
    {
        ReadGameWide();
        var d = DungeonLevel;
        var l = d != null ? World.LevelOf(d) : null;
        var im = BalanceManager<InventoryManager>(InventoryManagerPath, out bool imScene);
        var cm = BalanceManager<CombatManager>(CombatManagerPath, out _);
        var pm = BalanceManager<ProgressionManager>(ProgressionManagerPath, out _);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Balance", titleStyle);
        GUILayout.FlexibleSpace();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (imScene && scene.isDirty)
        {
            GUILayout.Label("<color=#e0a85a>● Room has unsaved changes</color>", new GUIStyle(StudioSkin.Label) { richText = true, wordWrap = false }, GUILayout.Height(28));
            if (GUILayout.Button("Save scene", GUILayout.Width(110), GUILayout.Height(28))) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var t in BalanceTabs)
            if (GUILayout.Toggle(balanceTab == t, t, StudioSkin.Tab, GUILayout.Width(110), GUILayout.Height(28)) && balanceTab != t) { balanceTab = t; cellsBefore.Clear(); lastEdit = null; GUI.FocusControl(null); }
        GUILayout.Space(16);
        GUILayout.Label(d != null ? $"Floors of {d.name}" : "", dim, GUILayout.Height(28));
        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(lastEdit)) GUILayout.Label($"Last change: {lastEdit}", new GUIStyle(StudioSkin.Label) { wordWrap = false, normal = { textColor = new Color(.86f, .7f, .38f) } }, GUILayout.Height(28));
        GUILayout.EndHorizontal();
        if (!imScene) GUILayout.Label("<color=#e0a85a>Room.unity isn't open: prefab values shown.</color>", new GUIStyle(dim) { richText = true });
        GUILayout.Space(8);
        if (d == null || im == null) { GUILayout.Label("No dungeon or InventoryManager found.", dim); return; }
        balanceGroup = balanceTab;
        switch (balanceTab)
        {
            case "Armour": WithFloorGear(d, () => ArmourTab(d, im, cm)); break;
            case "Weapons": WithFloorGear(d, () => WeaponsTab(d, im, cm, pm)); break;
            case "Items": ItemsTab(d, im); break;
            case "Enemies": WithFloorGear(d, () => EnemiesTab(d, l, im, cm, pm)); break;
            default: DropsTab(d, im, pm); break;
        }
    }

    // Inputs on the left, results on the right (one under the other when narrow).
    void TwoColumns(float leftWidth, Action left, Action right)
    {
        bool wide = pageWidth >= leftWidth + 520;
        if (wide) GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(wide ? leftWidth : pageWidth));
        left();
        GUILayout.EndVertical();
        if (wide) GUILayout.Space(12); else GUILayout.Space(8);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(wide ? pageWidth - leftWidth - 12 : pageWidth));
        right();
        GUILayout.EndVertical();
        if (wide) GUILayout.EndHorizontal();
    }

    // ── Armour ───────────────────────────────────────────────────────

    // A full set (helm, body, gloves, boots, leggings) of a tier at a rarity.
    static float SetArmour(Dictionary<(GearKind, int), ItemData> items, int tier, ItemRarity rarity)
    {
        float sum = 0f;
        foreach (var kind in new[] { GearKind.Helm, GearKind.Body, GearKind.Gloves, GearKind.Boots, GearKind.Legs })
        {
            int t = tier;
            while (t > 1 && !items.ContainsKey((kind, t))) t--;
            if (!items.TryGetValue((kind, t), out var i)) continue;
            int k = MainIndex(i);
            if (k >= 0) sum += Raised(i.statModifiers[k].value, rarity, false);
        }
        return sum;
    }

    // A full set of a floor's usual gear (its Gear by floor shares) at a rarity.
    static float FloorSetArmour(TableLevelData d, Dictionary<(GearKind, int), ItemData> items, int floor, ItemRarity rarity)
    {
        var row = d.GearAt(floor);
        if (row == null) return SetArmour(items, 1, rarity);
        var shares = row.Shares;
        float total = 0f, armour = 0f;
        for (int t = 0; t < shares.Length; t++) { if (shares[t] <= 0f) continue; total += shares[t]; armour += shares[t] * SetArmour(items, t + 1, rarity); }
        return total > 0f ? armour / total : SetArmour(items, 1, rarity);
    }

    void ArmourTab(TableLevelData d, InventoryManager im, CombatManager cm)
    {
        if (cm == null) { GUILayout.Label("No CombatManager found.", dim); return; }
        var items = TierItems(im);
        bool scaled = cm.playerArmorRule == CombatManager.ArmorRule.Scaled;
        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
        Question("How much of a hit a full set of armour stops, by floor");
        TwoColumns(470, () =>
        {
            Group("THE CURVE", cm, "CombatManager");
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            GUILayout.Label("Rule", GUILayout.Width(150));
            var rule = (CombatManager.ArmorRule)EditorGUILayout.EnumPopup(cm.playerArmorRule, GUILayout.Width(90));
            if (rule != cm.playerArmorRule) { Edited($"Armour rule {cm.playerArmorRule} → {rule}"); Undo.RecordObject(cm, "Armour rule"); cm.playerArmorRule = rule; Changed(cm); }
            GUILayout.EndHorizontal();
            if (scaled)
            {
                In("Common set stops", cm.armorTargetCut, cm, v => cm.armorTargetCut = v, BalUnit.Percent, .05f, .6f, tip: "A full Common set of the floor's usual gear.");
                In("Legendary set stops", cm.armorLegendaryCut, cm, v => cm.armorLegendaryCut = v, BalUnit.Percent, .05f, .65f, tip: "The same set at Legendary.");
            }
            In("Most it stops", cm.armorEffectiveness, cm, v => cm.armorEffectiveness = v, BalUnit.Percent, 0f, 1f);
            Group("FLAT RULE");
            In("Damage a point stops", cm.armorPerPoint, cm, v => cm.armorPerPoint = v, BalUnit.None, 0f, 5f, tip: scaled ? "Blocked hits and enemies' armour." : "Every hit.");
        }, () =>
        {
            int last = d.FloorCount;
            float first = CombatManager.ArmorCut(FloorSetArmour(d, items, 1, ItemRarity.Common), 1, NumbersAt(d, soldier, 1, null).damage);
            float top = CombatManager.ArmorCut(FloorSetArmour(d, items, last, ItemRarity.Legendary), last, NumbersAt(d, soldier, last, null).damage);
            Headline($"Floor 1: a Common set stops <b>{Pct(first)}</b>.   Floor {last}: a Legendary set stops <b>{Pct(top)}</b>.");
            GUILayout.Label(scaled ? "PER HIT, A FULL SET OF THE FLOOR'S USUAL GEAR" : "PER CRYPT SOLDIER'S HIT, A FULL SET OF THE FLOOR'S USUAL GEAR", gold);
            GUILayout.BeginHorizontal();
            Head("Floor", 44); Head("Armour", 60);
            foreach (var rar in GearRarities) Head(rar.ToString(), 92, rar);
            GUILayout.EndHorizontal();
            for (int f = 1; f <= d.FloorCount; f++)
            {
                float hit = scaled ? 0f : NumbersAt(d, soldier, f, null).damage;
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                Out($"a{f}f", f.ToString(), 44); Out($"a{f}s", FloorSetArmour(d, items, f, ItemRarity.Common).ToString("0"), 60);
                foreach (var rar in GearRarities)
                {
                    float a = FloorSetArmour(d, items, f, rar);
                    Out($"a{f}{rar}", $"{Pct(CombatManager.ArmorCut(a, f, hit))} <color=#a9a49a>{a:0}</color>", 92);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Grey: the set's armour.", dim);
        });
        GUILayout.Space(8);
        ArmourSimulator(d, im, cm, items);
    }

    // ── The set simulator: random armour of a floor, as its loot rolls it ──

    int simFloor = 1, simSeed = 1;
    float simHit = 20f;
    readonly List<ItemData> simSet = new();
    string simMany;

    // One piece of a kind for a floor, rolled as a chest rolls gear: the tier by the floor's Gear by floor shares (and its
    // upgrade chance), the rarity by InventoryManager > Rarity at the chest's multipliers, its properties by the table.
    static ItemData RollPiece(TableLevelData d, InventoryManager im, Dictionary<(GearKind, int), ItemData> items, GearKind kind, int floor, System.Random random)
    {
        var row = d.GearAt(floor);
        int tier = row != null ? row.Roll(random) : 1;
        if (row != null && random.NextDouble() < row.upgradeChance) tier = Mathf.Min(LootFloorTiers.Count, tier + 1);
        while (tier > 1 && !items.ContainsKey((kind, tier))) tier--;
        if (!items.TryGetValue((kind, tier), out var plain)) return null;
        var rarity = im.RollRarity(random, floor, im.rarityChanceMultiplier * (im.chestDrops != null ? im.chestDrops.rarityMultiplier : 1f));
        var table = ItemPropertyTable.Current;
        return rarity == ItemRarity.Common || table == null ? plain : plain.Rolled(rarity, table.Roll(plain, rarity, floor, random));
    }

    static float ArmourOf(ItemData i)
    {
        float sum = 0f;
        if (i?.statModifiers != null) foreach (var m in i.statModifiers) if (m != null && m.stat == StatType.Armor && m.type == ModifierType.Flat) sum += m.value;
        return sum;
    }

    // The share of a hit this much armour stops on a floor: the game's own (CombatManager.ArmorCut; the page sets the floor's
    // usual gear from this level's Gear by floor while it draws, WithFloorGear).
    static float CutOn(TableLevelData d, CombatManager cm, Dictionary<(GearKind, int), ItemData> items, float armour, int floor, float hit) => CombatManager.ArmorCut(armour, floor, hit);

    void ArmourSimulator(TableLevelData d, InventoryManager im, CombatManager cm, Dictionary<(GearKind, int), ItemData> items)
    {
        var kinds = new[] { GearKind.Helm, GearKind.Body, GearKind.Gloves, GearKind.Boots, GearKind.Legs };
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.Label("TRY A RANDOM SET", gold);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Floor", GUILayout.Width(44));
        simFloor = Mathf.Clamp(EditorGUILayout.IntField(simFloor, GUILayout.Width(40)), 1, Mathf.Max(1, d.FloorCount));
        GUILayout.Space(12);
        GUILayout.Label("Hit", GUILayout.Width(30));
        simHit = Mathf.Max(1f, EditorGUILayout.FloatField(simHit, GUILayout.Width(50)));
        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
        if (GUILayout.Button(new GUIContent("Soldier's", "A Crypt Soldier's hit on this floor."), GUILayout.Width(80))) simHit = Mathf.Round(NumbersAt(d, soldier, simFloor, null).damage * 10f) / 10f;
        GUILayout.Space(12);
        bool one = GUILayout.Button("Roll a set", GUILayout.Width(100), GUILayout.Height(24));
        bool many = GUILayout.Button("Roll 1000", GUILayout.Width(100), GUILayout.Height(24));
        GUILayout.EndHorizontal();
        if (one)
        {
            var random = new System.Random(unchecked(simSeed++ * 486187739 + simFloor));
            simSet.Clear();
            foreach (var kind in kinds) simSet.Add(RollPiece(d, im, items, kind, simFloor, random));
        }
        if (many)
        {
            var random = new System.Random(unchecked(simSeed++ * 486187739 + simFloor));
            var cuts = new List<float>(); var armours = new List<float>();
            for (int n = 0; n < 1000; n++)
            {
                float a = 0f;
                foreach (var kind in kinds) a += ArmourOf(RollPiece(d, im, items, kind, simFloor, random));
                armours.Add(a); cuts.Add(CutOn(d, cm, items, a, simFloor, simHit));
            }
            cuts.Sort(); armours.Sort();
            simMany = $"1000 sets, floor {simFloor}: armour {armours[100]:0}-{armours[900]:0}, stops {Pct(cuts[100])}-{Pct(cuts[900])} (middle 80%). Middle {Pct(cuts[500])}, best {Pct(cuts[999])}.";
        }
        if (simSet.Count > 0)
        {
            GUILayout.Space(4);
            float total = 0f;
            foreach (var piece in simSet)
            {
                float a = ArmourOf(piece); total += a;
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                if (piece == null) { GUILayout.Label("-", dim, GUILayout.Width(260)); GUILayout.EndHorizontal(); continue; }
                GUILayout.Label($"<color=#{RarityHex(piece.rarity)}>{piece.DisplayName}</color>", StudioSkin.Label, GUILayout.Width(260));
                GUILayout.Label(a.ToString("0.#"), new GUIStyle(StudioSkin.Label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
                GUILayout.Space(10);
                float extra = a - (MainIndex(piece) >= 0 ? piece.statModifiers[MainIndex(piece)].value : 0f);
                GUILayout.Label(extra > .01f ? $"<color=#a9a49a>+{extra:0.#} rolled</color>" : "", StudioSkin.Label, GUILayout.Width(110));
                GUILayout.EndHorizontal();
            }
            float cut = CutOn(d, cm, items, total, simFloor, simHit), usual = CutOn(d, cm, items, FloorSetArmour(d, items, simFloor, ItemRarity.Common), simFloor, simHit);
            GUILayout.Space(2);
            Headline($"Armour {total:0.#} stops <b>{Pct(cut)}</b>: a {simHit:0.#} hit lands as <b>{simHit * (1f - cut):0.#}</b>.  <color=#a9a49a>Common set: {Pct(usual)}</color>");
        }
        if (!string.IsNullOrEmpty(simMany)) { GUILayout.Space(4); GUILayout.Label(simMany, StudioSkin.Label); }
        GUILayout.EndVertical();
    }

    // ── Items ────────────────────────────────────────────────────────

    void ItemsTab(TableLevelData d, InventoryManager im)
    {
        var items = TierItems(im);
        Question("Each tier's damage and armour, and what rarity adds");
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginHorizontal();
        GUILayout.Label("RARITY", gold);
        GUILayout.FlexibleSpace();
        Where(im, "InventoryManager");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Head("", 100); Head("Main number +", 110); Head("Properties", 96); Head("Price x", 70);
        GUILayout.Space(16);
        if (GUILayout.Button(new GUIContent("Chances: Drops tab", "How often each rarity drops."), new GUIStyle(StudioSkin.Faint) { normal = { textColor = new Color(.6f, .75f, .9f) } }, GUILayout.ExpandWidth(false))) balanceTab = "Drops";
        GUILayout.EndHorizontal();
        foreach (var s in im.rarities.Where(x => x != null))
        {
            var step = s;
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label($"<color=#{RarityHex(step.rarity)}>{step.rarity}</color>", new GUIStyle(StudioSkin.Strong) { richText = true }, GUILayout.Width(100));
            GUILayout.Space(46); NumField(step.baseBonus, im, v => step.baseBonus = v, true, 0f, 2f, step.rarity + " main +"); GUILayout.Label("%", Unit, GUILayout.Width(14));
            GUILayout.Space(14);
            int lo = EditorGUILayout.DelayedIntField(step.properties.x, GUILayout.Width(34));
            int hi = EditorGUILayout.DelayedIntField(step.properties.y, GUILayout.Width(34));
            if (lo != step.properties.x || hi != step.properties.y) { Edited($"{step.rarity} properties {step.properties.x}-{step.properties.y} → {lo}-{hi}"); Undo.RecordObject(im, "Rarity properties"); lo = Mathf.Clamp(lo, 0, 6); step.properties = new Vector2Int(lo, Mathf.Clamp(hi, lo, 6)); Changed(im); }
            GUILayout.Space(18); NumField(step.priceMultiplier, im, v => step.priceMultiplier = v, false, 0f, 50f, step.rarity + " price x");
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.Space(8);

        TwoColumns(Mathf.Max(300f, Mathf.Min(pageWidth - 520, 790)), () =>
        {
            GUILayout.Label("MAIN NUMBER BY TIER, COMMON", gold);
            GUILayout.BeginHorizontal();
            Head("Tier", 124);
            foreach (var n in GearKindNames) Head(n, 60);
            GUILayout.EndHorizontal();
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label($"{t} {TierName(t, null)}", StudioSkin.Strong, GUILayout.Width(124));
                foreach (GearKind kind in Enum.GetValues(typeof(GearKind)))
                {
                    GUILayout.Space(8);
                    if (!items.TryGetValue((kind, t), out var item) || MainIndex(item) < 0) { GUILayout.Label("-", dim, GUILayout.Width(52)); continue; }
                    int k = MainIndex(item);
                    var it = item;
                    NumField(it.statModifiers[k].value, it, v => it.statModifiers[k].value = v, false, 0f, 999f, it.itemName, 52);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Weapons: damage. The rest: armour. Tiers 5-12: GearSets.Build() writes them again.", dim);
        }, () =>
        {
            GUILayout.BeginHorizontal();
            string[] shown = { "Sword", "Mace", "Shield", "Helm", "Body" };
            for (int i = 0; i < shown.Length; i++)
                if (GUILayout.Toggle(balanceKind == i, shown[i], StudioSkin.Tab, GUILayout.Width(70), GUILayout.Height(24)) && balanceKind != i) balanceKind = i;
            GUILayout.EndHorizontal();
            var kind = (GearKind)Array.IndexOf(GearKindNames, shown[Mathf.Clamp(balanceKind, 0, shown.Length - 1)]);
            GUILayout.Label($"A {kind.ToString().ToLowerInvariant()}'s main number, by tier and rarity", gold);
            GUILayout.BeginHorizontal();
            Head("Tier", 96);
            foreach (var rar in GearRarities) Head(rar.ToString(), 80, rar);
            GUILayout.EndHorizontal();
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                if (!items.TryGetValue((kind, t), out var item) || MainIndex(item) < 0) continue;
                float v = item.statModifiers[MainIndex(item)].value;
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label($"{t} {TierName(t, kind)}", StudioSkin.Strong, GUILayout.Width(96));
                foreach (var rar in GearRarities) Out($"i{kind}{t}{rar}", Raised(v, rar, kind == GearKind.Sword || kind == GearKind.Mace).ToString("0.#"), 80);
                GUILayout.EndHorizontal();
            }
        });
    }

    // ── Drops ────────────────────────────────────────────────────────

    int dropChests = 6;
    readonly Dictionary<ItemRarity, float> wantPerFloor = new();

    // Each rarity's share of a piece of gear on a floor, as InventoryManager.RollRarity rolls it (Common first).
    static float[] RaritySplit(InventoryManager im, int floor, float scale)
    {
        var split = new float[GearRarities.Length];
        float total = 0f;
        foreach (var s in im.rarities) if (s != null) total += s.ChanceOn(floor) * scale;
        float fit = total > 1f ? 1f / total : 1f, rest = 1f;
        foreach (var s in im.rarities)
        {
            if (s == null) continue;
            int at = Array.IndexOf(GearRarities, s.rarity);
            float p = Mathf.Min(rest, s.ChanceOn(floor) * scale * fit);
            if (at > 0) split[at] += p;
            rest -= p;
        }
        split[0] = Mathf.Max(0f, rest);
        return split;
    }

    // The average rolls a chest gives: the level's chest tiers by weight, each 1 + its extra rolls.
    static float ChestRolls(TableLevelData d)
    {
        float total = 0f, rolls = 0f;
        foreach (var t in d.chestTiers ?? Array.Empty<DungeonWeightedPrefab>())
        {
            if (t == null || t.prefab == null || t.weight <= 0f) continue;
            var c = t.prefab.GetComponentInChildren<DungeonContainer>(true);
            total += t.weight; rolls += t.weight * (1 + (c != null ? Mathf.Max(0, c.extraRolls) : 0));
        }
        return total > 0f ? rolls / total : 1f;
    }

    // One source's odds: the chance of gear, the tome among it, and each rarity of the rest, all of one kill (or roll).
    struct DropOdds { public float gear, tome; public float[] rarity; }
    static DropOdds OddsOf(InventoryManager im, SourceDrops s, float dropScale, int floor)
    {
        float gear = Mathf.Clamp01(s.gearChance * dropScale);
        var split = RaritySplit(im, floor, im.rarityChanceMultiplier * s.rarityMultiplier);
        var o = new DropOdds { gear = gear, tome = gear * im.tomeShare, rarity = new float[split.Length] };
        for (int i = 0; i < split.Length; i++) o.rarity[i] = gear * (1f - im.tomeShare) * split[i];
        return o;
    }

    void DropsTab(TableLevelData d, InventoryManager im, ProgressionManager pm)
    {
        Question("How often each rarity drops: per kill, per chest roll, per floor");
        float rolls = ChestRolls(d);
        float killScale = im.dropChanceMultiplier * im.enemyDropMultiplier;
        float PerFloor(int f, int r) => fightKills * OddsOf(im, im.enemyDrops, killScale, f).rarity[r] + dropChests * rolls * OddsOf(im, im.chestDrops, 1f, f).rarity[r];
        TwoColumns(470, () =>
        {
            Group("RARITY CHANCES", im, "InventoryManager");
            GUILayout.BeginHorizontal();
            Head("", 100); Head("Floor 1", 80); Head("More a floor", 110);
            GUILayout.EndHorizontal();
            foreach (var s in im.rarities.Where(x => x != null))
            {
                var step = s;
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label($"<color=#{RarityHex(step.rarity)}>{step.rarity}</color>", new GUIStyle(StudioSkin.Strong) { richText = true }, GUILayout.Width(100));
                GUILayout.Space(18); NumField(step.chance, im, v => step.chance = v, true, 0f, 1f, step.rarity + " chance", 56); GUILayout.Label("%", Unit, GUILayout.Width(14));
                GUILayout.Space(30); NumField(step.chancePerFloor, im, v => step.chancePerFloor = v, true, 0f, .2f, step.rarity + " a floor", 56); GUILayout.Label("%", Unit, GUILayout.Width(14));
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Common: the rest. Before the multipliers below.", dim);
            Group("RARITY MULTIPLIERS");
            In("Whole game", im.rarityChanceMultiplier, im, v => im.rarityChanceMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Kills", im.enemyDrops.rarityMultiplier, im, v => im.enemyDrops.rarityMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Chests", im.chestDrops.rarityMultiplier, im, v => im.chestDrops.rarityMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Breakables", im.breakableDrops.rarityMultiplier, im, v => im.breakableDrops.rarityMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Merchants", im.merchantRarity, im, v => im.merchantRarity = v, BalUnit.Times, 0f, 10f);
            Group("HOW OFTEN GEAR DROPS");
            In("A kill", im.enemyDrops.gearChance, im, v => im.enemyDrops.gearChance = v, BalUnit.Percent, 0f, 1f, tip: "Before the multipliers below.");
            In("A chest roll", im.chestDrops.gearChance, im, v => im.chestDrops.gearChance = v, BalUnit.Percent, 0f, 1f);
            In("A breakable", im.breakableDrops.gearChance, im, v => im.breakableDrops.gearChance = v, BalUnit.Percent, 0f, 1f);
            In("Kills and breakables", im.dropChanceMultiplier, im, v => im.dropChanceMultiplier = v, BalUnit.Times, 0f, 10f, tip: "Gear and potions; chests keep their own.");
            In("Kills, on top", im.enemyDropMultiplier, im, v => im.enemyDropMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Spell tomes", im.tomeShare, im, v => im.tomeShare = v, BalUnit.Percent, 0f, 1f, tip: "Share of the gear that drops.");
            Group("GOLD AND POTIONS");
            GUILayout.BeginHorizontal();
            Head("", 90); Head("Gold", 66); Head("Amount", 76); Head("Potions", 70);
            GUILayout.EndHorizontal();
            void Source(string name, SourceDrops s)
            {
                if (s == null) return;
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label(name, StudioSkin.Strong, GUILayout.Width(90));
                GUILayout.Space(4); NumField(s.goldChance, im, v => s.goldChance = v, true, 0f, 1f, name + " gold", 46); GUILayout.Label("%", Unit, GUILayout.Width(12));
                int lo = EditorGUILayout.DelayedIntField(s.goldMin, GUILayout.Width(32)), hi = EditorGUILayout.DelayedIntField(s.goldMax, GUILayout.Width(32));
                if (lo != s.goldMin || hi != s.goldMax) { Edited($"{name} gold {s.goldMin}-{s.goldMax} → {lo}-{hi}"); Undo.RecordObject(im, name + " gold amount"); s.goldMin = Mathf.Max(0, lo); s.goldMax = Mathf.Max(s.goldMin, hi); Changed(im); }
                GUILayout.Space(6); NumField(s.recoveryChance, im, v => s.recoveryChance = v, true, 0f, 1f, name + " potions", 46); GUILayout.Label("%", Unit, GUILayout.Width(12));
                GUILayout.EndHorizontal();
            }
            Source("Kill", im.enemyDrops); Source("Breakable", im.breakableDrops); Source("Chest", im.chestDrops);
            In("Gold", im.goldMultiplier, im, v => im.goldMultiplier = v, BalUnit.Times, 0f, 10f);
            if (pm != null)
            {
                Group("SPOILS", pm, "ProgressionManager");
                In("A kill", pm.enemySpoilsChance, pm, v => pm.enemySpoilsChance = v, BalUnit.Percent, 0f, 1f);
                In("A chest", pm.chestSpoilsChance, pm, v => pm.chestSpoilsChance = v, BalUnit.Percent, 0f, 1f);
            }
            Group("A FLOOR HAS");
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            GUILayout.Label("Kills", GUILayout.Width(150)); fightKills = Mathf.Clamp(EditorGUILayout.IntField(fightKills, GUILayout.Width(56)), 0, 500);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            GUILayout.Label("Chests", GUILayout.Width(150)); dropChests = Mathf.Clamp(EditorGUILayout.IntField(dropChests, GUILayout.Width(56)), 0, 100);
            GUILayout.Label($"x {rolls:0.##} rolls", Unit, GUILayout.Width(100));
            GUILayout.EndHorizontal();
        }, () =>
        {
            // The headline: each rarity a floor, on floor 1 and the last.
            int last = d.FloorCount;
            GUILayout.BeginHorizontal();
            for (int r = 1; r < GearRarities.Length; r++)
            {
                GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(170));
                GUILayout.Label($"<color=#{RarityHex(GearRarities[r])}>{GearRarities[r]}</color>", new GUIStyle(StudioSkin.Strong) { richText = true });
                float a = PerFloor(1, r), b = PerFloor(last, r);
                Headline(Every(a));
                if (!Mathf.Approximately(a, b)) GUILayout.Label($"floor {last}: {Every(b)}", dim);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"A floor: {fightKills} kills and {dropChests} chests ({rolls:0.##} rolls each).", dim);

            void PerSource(string title, SourceDrops s, float scale, string per)
            {
                GUILayout.Space(8);
                GUILayout.Label(title, gold);
                GUILayout.BeginHorizontal();
                Head("Floor", 40);
                foreach (var rar in GearRarities) Head(rar.ToString(), 96, rar);
                Head("Tome", 86); Head("= Gear", 70);
                GUILayout.EndHorizontal();
                for (int f = 1; f <= d.FloorCount; f++)
                {
                    var o = OddsOf(im, s, scale, f);
                    GUILayout.BeginHorizontal(GUILayout.Height(20));
                    Out($"{per}{f}f", f.ToString(), 40);
                    for (int r = 0; r < GearRarities.Length; r++) Out($"{per}{f}{r}", $"{Pct(o.rarity[r])} <color=#a9a49a>{OneIn(o.rarity[r])}</color>", 96);
                    Out($"{per}{f}t", $"{Pct(o.tome)} <color=#a9a49a>{OneIn(o.tome)}</color>", 86);
                    Out($"{per}{f}g", Pct(o.gear), 70, true);
                    GUILayout.EndHorizontal();
                }
            }
            PerSource("PER KILL  (THE ROW ADDS UP TO = GEAR)", im.enemyDrops, killScale, "k");
            PerSource("PER CHEST ROLL  (THE ROW ADDS UP TO = GEAR)", im.chestDrops, 1f, "c");

            GUILayout.Space(8);
            GUILayout.Label("OF THE GEAR DROPPED  (EACH ROW 100%)", gold);
            GUILayout.BeginHorizontal();
            Head("", 120);
            foreach (var rar in GearRarities) Head(rar.ToString(), 80, rar);
            Head("Tome", 70);
            GUILayout.EndHorizontal();
            void ShareRow(string label, SourceDrops s, float scale, int f)
            {
                var o = OddsOf(im, s, scale, f);
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label(label, StudioSkin.Label, GUILayout.Width(120));
                for (int r = 0; r < GearRarities.Length; r++) Out($"s{label}{r}", o.gear > 0f ? Pct(o.rarity[r] / o.gear) : "-", 80);
                Out($"s{label}t", o.gear > 0f ? Pct(o.tome / o.gear) : "-", 70);
                GUILayout.EndHorizontal();
            }
            ShareRow("Kill, floor 1", im.enemyDrops, killScale, 1);
            ShareRow($"Kill, floor {last}", im.enemyDrops, killScale, last);
            ShareRow("Chest, floor 1", im.chestDrops, 1f, 1);
            ShareRow($"Chest, floor {last}", im.chestDrops, 1f, last);

            GUILayout.Space(8);
            GUILayout.Label($"PER FLOOR  ({fightKills} KILLS, {dropChests} CHESTS)", gold);
            GUILayout.BeginHorizontal();
            Head("Floor", 40);
            foreach (var rar in GearRarities) Head(rar.ToString(), 110, rar);
            GUILayout.EndHorizontal();
            for (int f = 1; f <= d.FloorCount; f++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                Out($"p{f}f", f.ToString(), 40);
                for (int r = 0; r < GearRarities.Length; r++) { float n = PerFloor(f, r); Out($"p{f}{r}", n >= 1f ? n.ToString("0.#") : n > 0f ? $"{n:0.##} <color=#a9a49a>1 in {1f / n:0.#} fl.</color>" : "-", 110); }
                GUILayout.EndHorizontal();
            }

            // Target: how many of a rarity a floor should give, and the floor-1 chance that gives it.
            GUILayout.Space(8);
            GUILayout.Label("TARGET ON FLOOR 1", gold);
            GUILayout.BeginHorizontal();
            Head("", 100); Head("Want a floor", 100); Head("Chance", 80);
            GUILayout.EndHorizontal();
            foreach (var s in im.rarities.Where(x => x != null && x.rarity != ItemRarity.Legendary))
            {
                var step = s;
                int r = Array.IndexOf(GearRarities, step.rarity);
                if (r <= 0) continue;
                // A rarity's count follows its chance while the steps' chances stay under 100% together.
                float now = PerFloor(1, r), per = step.chance > 0f ? now / step.chance : 0f;
                if (!wantPerFloor.ContainsKey(step.rarity)) wantPerFloor[step.rarity] = Mathf.Round(now * 100f) / 100f;
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label($"<color=#{RarityHex(step.rarity)}>{step.rarity}</color>", new GUIStyle(StudioSkin.Strong) { richText = true }, GUILayout.Width(100));
                GUILayout.Space(40);
                wantPerFloor[step.rarity] = Mathf.Max(0f, EditorGUILayout.FloatField(wantPerFloor[step.rarity], GUILayout.Width(56)));
                float need = per > 0f ? Mathf.Clamp01(wantPerFloor[step.rarity] / per) : 0f;
                GUILayout.Label(per > 0f ? Pct(need) : "-", new GUIStyle(StudioSkin.Label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(80));
                GUILayout.Space(8);
                if (per > 0f && Mathf.Abs(need - step.chance) > .0001f && GUILayout.Button("Set", GUILayout.Width(50)))
                { Edited($"{step.rarity} chance {Pct(step.chance)} → {Pct(need)}"); Undo.RecordObject(im, "Rarity chance"); step.chance = need; Changed(im); }
                GUILayout.EndHorizontal();
            }
        });
        GUILayout.Space(8);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GearByFloorTable(d);
        GUILayout.EndVertical();
    }

    // The level's Gear by floor rows, each tier's share and the upgrade chance, edited in place.
    void GearByFloorTable(TableLevelData d)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("WHICH TIERS DROP ON EACH FLOOR", gold);
        GUILayout.FlexibleSpace();
        Where(d, d.name + " > Gear by floor");
        GUILayout.EndHorizontal();
        if (d.gearByFloor == null || d.gearByFloor.Count == 0) { GUILayout.Label("No rows: the loot profiles' own tiers.", dim); return; }
        int top = 1;
        foreach (var row in d.gearByFloor) if (row != null) top = Mathf.Max(top, row.HighestTier);
        top = Mathf.Min(LootFloorTiers.Count, top + 1);
        GUILayout.BeginHorizontal();
        Head("From floor", 70);
        for (int t = 1; t <= top; t++) Head(LootFloorTiers.Names[t - 1], 70);
        Head("One tier up", 84);
        GUILayout.EndHorizontal();
        foreach (var row in d.gearByFloor)
        {
            if (row == null) continue;
            var r = row;
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            GUILayout.Space(26);
            int from = EditorGUILayout.DelayedIntField(r.fromFloor, GUILayout.Width(40));
            if (from != r.fromFloor) { Edited($"Gear row from floor {r.fromFloor} → {from}"); Undo.RecordObject(d, "Gear by floor"); r.fromFloor = Mathf.Max(1, from); Changed(d); }
            var shares = r.Shares;
            for (int t = 1; t <= top; t++)
            {
                int tier = t;
                GUILayout.Space(12);
                NumField(shares[t - 1], d, v => r.SetShare(tier, v), false, 0f, 1000f, $"Floor {r.fromFloor}+ {LootFloorTiers.Names[t - 1]} share", 58);
            }
            GUILayout.Space(26);
            NumField(r.upgradeChance, d, v => r.upgradeChance = v, true, 0f, 1f, $"Floor {r.fromFloor}+ one tier up", 50); GUILayout.Label("%", Unit, GUILayout.Width(12));
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Shares by weight.", dim);
    }
}
