using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Asset Studio's Balance view (the user, 2026-11-04: "a section for armor balancing where you can tweak things like armor,
// scaling of items per tier / rarity, enemy scaling per floor, drop rates"). Four tabs, each the real values edited in
// place (undoable) beside a preview of what they give on each floor of the picked dungeon (the Dungeon view's pick,
// Dungeon1): Armour (CombatManager > Hits > Armor), Items (InventoryManager > Rarity and each tier's main numbers), Enemies
// (the level's DungeonBalance, its stages' jumps and elites, ProgressionManager > Enemy balance) and Drops (InventoryManager
// > Drops by source and the whole-game multipliers, the spoils, the level's Gear by floor). The managers are the room's
// scene objects (save the scene after), the rest are assets saved at once.
public partial class AssetStudioWindow
{
    Vector2 balanceScroll;
    string balanceTab = "Armour";
    int balanceKind;
    bool showingBalance => studioView == StudioView.Balance;
    static readonly string[] BalanceTabs = { "Armour", "Items", "Enemies", "Drops" };
    static readonly ItemRarity[] GearRarities = { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Unique, ItemRarity.Legendary };
    static readonly EquipmentSlot[] ArmourSlots = { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Gloves, EquipmentSlot.Boots, EquipmentSlot.Legs };
    const string CombatManagerPath = "Assets/Game/Combat/Prefabs/CombatManager.prefab";
    const string TablePlayerPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";

    void OpenBalance() { studioView = StudioView.Balance; GUI.FocusControl(null); }

    // The kinds of gear the tier table and the rarity preview show.
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

    static void Changed(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        if (o is Component c && !EditorUtility.IsPersistent(c)) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        else AssetDatabase.SaveAssetIfDirty(o);
    }

    // A labelled number: shown as a percent when percent, edited, written back (undoable) when changed.
    float Num(string label, float value, UnityEngine.Object owner, Action<float> set, bool percent = false, float min = 0f, float max = float.MaxValue, float labelWidth = 150, string tip = null)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(label, tip), GUILayout.Width(labelWidth));
        float typed = NumField(value, owner, set, percent, min, max, label);
        GUILayout.Label(percent ? "%" : "", Unit, GUILayout.Width(16));
        GUILayout.EndHorizontal();
        return typed;
    }

    // Just the field (for table cells).
    static float NumField(float value, UnityEngine.Object owner, Action<float> set, bool percent = false, float min = 0f, float max = float.MaxValue, string undo = "Balance", float width = 52)
    {
        float shown = percent ? Mathf.Round(value * 1000f) / 10f : value;
        float entered = EditorGUILayout.DelayedFloatField(shown, GUILayout.Width(width));
        // Only what was typed is written: a value shown rounded is never written back as its rounding.
        if (owner == null || Mathf.Approximately(entered, shown)) return value;
        float typed = Mathf.Clamp(percent ? entered / 100f : entered, min, max);
        Undo.RecordObject(owner, undo); set(typed); Changed(owner);
        return typed;
    }

    static string Pct(float p) => p <= 0f ? "-" : p < .001f ? "<0.1%" : $"{p * 100f:0.#}%";

    // A preview cell: right-aligned text in a fixed width.
    void Cell(string text, float width, bool head = false)
    {
        var style = head ? new GUIStyle(gold) { fontSize = StudioSkin.Size - 2, alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip }
                         : new GUIStyle(StudioSkin.Label) { alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip };
        GUILayout.Label(text, style, GUILayout.Width(width), GUILayout.Height(20));
    }

    // A main number raised to a rarity, the game's own rule (ItemData.RaisedMain).
    static MethodInfo raisedMain;
    static float Raised(float value, ItemRarity rarity)
    {
        if (rarity == ItemRarity.Common) return value;
        raisedMain ??= typeof(ItemData).GetMethod("RaisedMain", BindingFlags.NonPublic | BindingFlags.Static);
        return raisedMain != null ? (float)raisedMain.Invoke(null, new object[] { value, rarity }) : value;
    }

    // The item's main number (its damage or armour) from its own modifiers, and the modifier holding it.
    static int MainIndex(ItemData i)
    {
        var main = i != null ? i.MainStatType : null;
        if (main == null || i.statModifiers == null) return -1;
        for (int k = 0; k < i.statModifiers.Length; k++) if (i.statModifiers[k] != null && i.statModifiers[k].stat == main && i.statModifiers[k].type == ModifierType.Flat) return k;
        return -1;
    }

    // The plain (Common) item of a kind and tier in the catalog.
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

    void DrawBalance(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        balanceScroll = GUILayout.BeginScrollView(balanceScroll);
        try { BalancePage(); }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    void BalancePage()
    {
        ReadGameWide();
        var d = DungeonLevel;
        var l = d != null ? World.LevelOf(d) : null;
        Title("Balance", d != null ? $"Previews are {d.name}'s floors." : null);
        var im = BalanceManager<InventoryManager>(InventoryManagerPath, out bool imScene);
        var cm = BalanceManager<CombatManager>(CombatManagerPath, out bool cmScene);
        var pm = BalanceManager<ProgressionManager>(ProgressionManagerPath, out bool pmScene);
        GUILayout.BeginHorizontal();
        foreach (var t in BalanceTabs)
            if (GUILayout.Toggle(balanceTab == t, t, StudioSkin.Tab, GUILayout.Width(110), GUILayout.Height(28)) && balanceTab != t) { balanceTab = t; GUI.FocusControl(null); }
        GUILayout.FlexibleSpace();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (imScene && scene.isDirty && GUILayout.Button("Save scene", GUILayout.Width(110), GUILayout.Height(28))) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        GUILayout.EndHorizontal();
        if (!imScene) GUILayout.Label("<color=#e0a85a>Room.unity isn't open: these are the prefabs' values, not the scene's.</color>", new GUIStyle(dim) { richText = true });
        GUILayout.Space(8);
        if (d == null || im == null) { GUILayout.Label("No dungeon or InventoryManager found.", dim); return; }
        switch (balanceTab)
        {
            case "Armour": ArmourTab(d, im, cm); break;
            case "Items": ItemsTab(d, im); break;
            case "Enemies": EnemiesTab(d, l, pm); break;
            default: DropsTab(d, im, pm); break;
        }
    }

    // Side by side when there is room, else one under the other.
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
            if (k >= 0) sum += Raised(i.statModifiers[k].value, rarity);
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

    static float Odds(float cut) { cut = Mathf.Clamp(cut, .01f, .95f); return cut / (1f - cut); }

    void ArmourTab(TableLevelData d, InventoryManager im, CombatManager cm)
    {
        if (cm == null) { GUILayout.Label("No CombatManager found.", dim); return; }
        var items = TierItems(im);
        TwoColumns(330, () =>
        {
            GUILayout.Label("ARMOUR", gold);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rule", GUILayout.Width(150));
            var rule = (CombatManager.ArmorRule)EditorGUILayout.EnumPopup(cm.playerArmorRule, GUILayout.Width(90));
            if (rule != cm.playerArmorRule) { Undo.RecordObject(cm, "Armour rule"); cm.playerArmorRule = rule; Changed(cm); }
            GUILayout.EndHorizontal();
            if (cm.playerArmorRule == CombatManager.ArmorRule.Scaled)
            {
                Num("Common set stops", cm.armorTargetCut, cm, v => cm.armorTargetCut = v, true, .05f, .6f, tip: "A full Common set of the floor's usual gear.");
                Num("Legendary set stops", cm.armorLegendaryCut, cm, v => cm.armorLegendaryCut = v, true, .05f, .65f, tip: "The same set at Legendary.");
            }
            else Num("Per point", cm.armorPerPoint, cm, v => cm.armorPerPoint = v, false, 0f, 5f, tip: "Damage each armour point takes off a hit.");
            Num("Most it stops", cm.armorEffectiveness, cm, v => cm.armorEffectiveness = v, true, 0f, 1f);
            GUILayout.Space(6);
            GUILayout.Label("Blocked hits and enemies' armour: per point.", dim);
            if (cm.playerArmorRule == CombatManager.ArmorRule.Scaled) Num("Per point", cm.armorPerPoint, cm, v => cm.armorPerPoint = v, false, 0f, 5f, tip: "Damage each armour point takes off a blocked hit and an enemy's hit taken.");
        }, () =>
        {
            bool scaled = cm.playerArmorRule == CombatManager.ArmorRule.Scaled;
            GUILayout.Label(scaled ? "SHARE OF A HIT A FULL SET STOPS" : "SHARE OF A CRYPT SOLDIER'S HIT A FULL SET STOPS", gold);
            GUILayout.BeginHorizontal();
            Cell("Floor", 44, true); Cell("Set", 60, true);
            foreach (var rar in GearRarities) Cell(rar.ToString(), 82, true);
            GUILayout.EndHorizontal();
            float power = CombatManager.ArmorPower, cap = cm.armorEffectiveness;
            var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
            for (int f = 1; f <= d.FloorCount; f++)
            {
                float usual = FloorSetArmour(d, items, f, ItemRarity.Common);
                float k = Mathf.Max(1f, usual * Mathf.Pow(Odds(cm.armorTargetCut), -1f / power));
                float hit = scaled ? 0f : NumbersAt(d, soldier, f, null).damage;
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                Cell(f.ToString(), 44); Cell(usual.ToString("0"), 60);
                foreach (var rar in GearRarities)
                {
                    float a = FloorSetArmour(d, items, f, rar), cut;
                    if (scaled) { float o = Mathf.Pow(a / k, power); cut = Mathf.Min(cap, o / (1f + o)); }
                    else cut = hit > 0f ? Mathf.Min(cap * hit, a * cm.armorPerPoint) / hit : 0f;
                    Cell($"{Pct(cut)}  <color=#a9a49a>{a:0}</color>", 82);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(scaled ? "Set: a Common set of the floor's usual tiers (Gear by floor). Grey: the set's armour." : "Grey: the set's armour.", dim);
        });
        GUILayout.Space(8);
        ArmourSimulator(d, im, cm, items);
    }

    // ── The set simulator: random armour of a floor, as its loot rolls it ──

    int simFloor = 1, simSeed = 1;
    float simHit = 20f;
    readonly List<ItemData> simSet = new();
    string simMany;

    // One piece for a slot on a floor, rolled as a chest rolls gear: the tier by the floor's Gear by floor shares (and its
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

    // The share of a hit this much armour stops on a floor, as the game works it out (CombatManager.ArmorCut), with the
    // floor's usual set read from this level's Gear by floor.
    float CutOn(TableLevelData d, CombatManager cm, Dictionary<(GearKind, int), ItemData> items, float armour, int floor, float hit)
    {
        if (armour <= 0f) return 0f;
        if (cm.playerArmorRule != CombatManager.ArmorRule.Scaled) return hit > 0f ? Mathf.Min(cm.armorEffectiveness * hit, armour * cm.armorPerPoint) / hit : 0f;
        float power = CombatManager.ArmorPower;
        float k = Mathf.Max(1f, FloorSetArmour(d, items, floor, ItemRarity.Common) * Mathf.Pow(Odds(cm.armorTargetCut), -1f / power));
        float o = Mathf.Pow(armour / k, power);
        return Mathf.Min(cm.armorEffectiveness, o / (1f + o));
    }

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
            simMany = $"1000 sets on floor {simFloor}: armour {armours[100]:0}-{armours[900]:0} (middle {armours[500]:0}), stops {Pct(cuts[100])}-{Pct(cuts[900])} (middle {Pct(cuts[500])}, best {Pct(cuts[999])}). The middle 80% shown.";
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
                string hex = piece.rarity == ItemRarity.Common ? "e6e6e6" : ColorUtility.ToHtmlStringRGB(UIManager.RarityColor(piece.rarity));
                GUILayout.Label($"<color=#{hex}>{piece.DisplayName}</color>", StudioSkin.Label, GUILayout.Width(260));
                Cell(a.ToString("0.#"), 50);
                GUILayout.Space(10);
                float extra = a - (MainIndex(piece) >= 0 ? piece.statModifiers[MainIndex(piece)].value : 0f);
                GUILayout.Label(extra > .01f ? $"<color=#a9a49a>+{extra:0.#} rolled</color>" : "", StudioSkin.Label, GUILayout.Width(110));
                GUILayout.EndHorizontal();
            }
            float cut = CutOn(d, cm, items, total, simFloor, simHit), usual = CutOn(d, cm, items, FloorSetArmour(d, items, simFloor, ItemRarity.Common), simFloor, simHit);
            GUILayout.Space(2);
            GUILayout.Label($"Armour {total:0.#}: stops {Pct(cut)}. A {simHit:0.#} hit lands as {simHit * (1f - cut):0.#}. <color=#a9a49a>(Common set of the floor: {Pct(usual)})</color>", StudioSkin.Label);
        }
        if (!string.IsNullOrEmpty(simMany)) { GUILayout.Space(4); GUILayout.Label(simMany, Wrapped, GUILayout.Width(pageWidth - 24)); }
        GUILayout.EndVertical();
    }

    // ── Items ────────────────────────────────────────────────────────

    void ItemsTab(TableLevelData d, InventoryManager im)
    {
        var items = TierItems(im);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.Label("RARITY", gold);
        GUILayout.BeginHorizontal();
        Cell("", 100, true); Cell("Main +", 70, true); Cell("Properties", 90, true); Cell("Price x", 70, true); Cell("Chance", 70, true); Cell("A floor +", 76, true);
        GUILayout.EndHorizontal();
        foreach (var s in im.rarities.Where(x => x != null))
        {
            var step = s;
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label(step.rarity.ToString(), StudioSkin.Strong, GUILayout.Width(100));
            GUILayout.Space(18); NumField(step.baseBonus, im, v => step.baseBonus = v, true, 0f, 2f, "Rarity bonus"); GUILayout.Label("%", Unit, GUILayout.Width(14));
            GUILayout.Space(8);
            int lo = EditorGUILayout.DelayedIntField(step.properties.x, GUILayout.Width(34));
            int hi = EditorGUILayout.DelayedIntField(step.properties.y, GUILayout.Width(34));
            if (lo != step.properties.x || hi != step.properties.y) { Undo.RecordObject(im, "Rarity properties"); lo = Mathf.Clamp(lo, 0, 6); step.properties = new Vector2Int(lo, Mathf.Clamp(hi, lo, 6)); Changed(im); }
            GUILayout.Space(18); NumField(step.priceMultiplier, im, v => step.priceMultiplier = v, false, 0f, 50f, "Rarity price");
            GUILayout.Space(18); NumField(step.chance, im, v => step.chance = v, true, 0f, 1f, "Rarity chance"); GUILayout.Label("%", Unit, GUILayout.Width(14));
            GUILayout.Space(8); NumField(step.chancePerFloor, im, v => step.chancePerFloor = v, true, 0f, .2f, "Rarity chance a floor"); GUILayout.Label("%", Unit, GUILayout.Width(14));
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(4);
        // The table player's base Attack Damage (TablePlayer.prefab's PlayerData), added to every weapon's.
        var player = AssetDatabase.LoadAllAssetsAtPath(TablePlayerPath).OfType<PlayerData>().FirstOrDefault();
        if (player != null && player.stats != null)
        {
            int at = player.stats.FindIndex(e => e.stat == StatType.AttackDamage);
            if (at >= 0)
            {
                var e = player.stats[at];
                Num("Player base damage", e.baseValue, player, v => { var x = player.stats[at]; x.baseValue = v; player.stats[at] = x; }, false, 0f, 100f, 150, "Added to every weapon's damage.");
            }
        }
        GUILayout.EndVertical();
        GUILayout.Space(8);

        TwoColumns(Mathf.Max(300f, Mathf.Min(pageWidth - 520, 760)), () =>
        {
            GUILayout.Label("MAIN NUMBER BY TIER (COMMON)", gold);
            GUILayout.BeginHorizontal();
            Cell("Tier", 96, true);
            foreach (var n in GearKindNames) Cell(n, 60, true);
            GUILayout.EndHorizontal();
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label($"{t} {LootFloorTiers.Names[t - 1]}", StudioSkin.Strong, GUILayout.Width(96));
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
            GUILayout.Label("Swords and maces: damage. The rest: armour. Tiers 5-12: GearSets.Build() writes them again.", dim);
        }, () =>
        {
            GUILayout.BeginHorizontal();
            string[] shown = { "Sword", "Mace", "Shield", "Helm", "Body" };
            for (int i = 0; i < shown.Length; i++)
                if (GUILayout.Toggle(balanceKind == i, shown[i], StudioSkin.Tab, GUILayout.Width(70), GUILayout.Height(24)) && balanceKind != i) balanceKind = i;
            GUILayout.EndHorizontal();
            var kind = (GearKind)Array.IndexOf(GearKindNames, shown[Mathf.Clamp(balanceKind, 0, shown.Length - 1)]);
            GUILayout.BeginHorizontal();
            Cell("Tier", 96, true);
            foreach (var rar in GearRarities) Cell(rar.ToString(), 76, true);
            GUILayout.EndHorizontal();
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                if (!items.TryGetValue((kind, t), out var item) || MainIndex(item) < 0) continue;
                float v = item.statModifiers[MainIndex(item)].value;
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label($"{t} {LootFloorTiers.Names[t - 1]}", StudioSkin.Strong, GUILayout.Width(96));
                foreach (var rar in GearRarities) Cell(Raised(v, rar).ToString("0.#"), 76);
                GUILayout.EndHorizontal();
            }
        });
    }

    // ── Enemies ──────────────────────────────────────────────────────

    void EnemiesTab(TableLevelData d, AssetStudioWorld.Level l, ProgressionManager pm)
    {
        if (l == null) { GUILayout.Label("No level.", dim); return; }
        var rat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Rat.prefab");
        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
        float left = 640;
        bool wide = pageWidth >= left + 520;
        float keep = pageWidth;
        if (wide) GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(wide ? left : pageWidth));
        pageWidth = wide ? left : keep;
        GrowthFields(d);
        GUILayout.Space(6);
        if (pm != null)
        {
            GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
            GUILayout.Label("EVERY ENEMY", gold);
            Num("Damage", pm.enemyBaseDamage, pm, v => pm.enemyBaseDamage = v, false, 0f, 10f, 110, "ProgressionManager > Enemy balance, on top of the above.");
            Num("Health", pm.enemyBaseHealth, pm, v => pm.enemyBaseHealth = v, false, 0f, 10f, 110, "ProgressionManager > Enemy balance, on top of the above.");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }
        StageSteps(l);
        pageWidth = keep;
        GUILayout.EndVertical();
        if (wide) GUILayout.Space(12); else GUILayout.Space(8);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(wide ? pageWidth - left - 12 : pageWidth));
        GUILayout.Label("EACH FLOOR", gold);
        GUILayout.BeginHorizontal();
        Cell("Floor", 44, true); Cell("Damage", 62, true); Cell("Health", 62, true); Cell("Armour", 58, true); Cell("Rat hit", 62, true); Cell("Rat HP", 58, true); Cell("Soldier hit", 76, true); Cell("Soldier HP", 74, true);
        GUILayout.EndHorizontal();
        var floors = RunDifficulty(l);
        foreach (var f in floors)
        {
            var a = NumbersAt(d, rat, f.floor, null); var b = NumbersAt(d, soldier, f.floor, null);
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            Cell((f.bossFloor ? "<color=#e05a5a>" : "") + f.floor + (f.bossFloor ? "</color>" : ""), 44);
            Cell($"x{f.damage * wideDamage:0.00}", 62); Cell($"x{f.health * wideHealth:0.00}", 62); Cell($"+{f.armour:0.#}", 58);
            Cell(a.damage.ToString("0.#"), 62); Cell(a.health.ToString("0"), 58); Cell(b.damage.ToString("0.#"), 76); Cell(b.health.ToString("0"), 74);
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("With every enemy's multipliers. Red: a boss floor.", dim);
        GUILayout.EndVertical();
        if (wide) GUILayout.EndHorizontal();
    }

    // ── Drops ────────────────────────────────────────────────────────

    // Each rarity's share of a piece of gear on a floor, as InventoryManager.RollRarity rolls it.
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

    void DropsTab(TableLevelData d, InventoryManager im, ProgressionManager pm)
    {
        TwoColumns(470, () =>
        {
            GUILayout.Label("BY SOURCE", gold);
            GUILayout.BeginHorizontal();
            Cell("", 80, true); Cell("Gold", 60, true); Cell("Amount", 74, true); Cell("Gear", 60, true); Cell("Potions", 60, true); Cell("Rarity x", 60, true);
            GUILayout.EndHorizontal();
            void Source(string name, SourceDrops s)
            {
                if (s == null) return;
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label(name, StudioSkin.Strong, GUILayout.Width(80));
                GUILayout.Space(4); NumField(s.goldChance, im, v => s.goldChance = v, true, 0f, 1f, name + " gold", 46); GUILayout.Label("%", Unit, GUILayout.Width(12));
                int lo = EditorGUILayout.DelayedIntField(s.goldMin, GUILayout.Width(32)), hi = EditorGUILayout.DelayedIntField(s.goldMax, GUILayout.Width(32));
                if (lo != s.goldMin || hi != s.goldMax) { Undo.RecordObject(im, name + " gold amount"); s.goldMin = Mathf.Max(0, lo); s.goldMax = Mathf.Max(s.goldMin, hi); Changed(im); }
                GUILayout.Space(6); NumField(s.gearChance, im, v => s.gearChance = v, true, 0f, 1f, name + " gear", 46); GUILayout.Label("%", Unit, GUILayout.Width(12));
                NumField(s.recoveryChance, im, v => s.recoveryChance = v, true, 0f, 1f, name + " potions", 46); GUILayout.Label("%", Unit, GUILayout.Width(12));
                NumField(s.rarityMultiplier, im, v => s.rarityMultiplier = v, false, 0f, 10f, name + " rarity", 46);
                GUILayout.EndHorizontal();
            }
            Source("Kill", im.enemyDrops); Source("Breakable", im.breakableDrops); Source("Chest", im.chestDrops);
            GUILayout.Space(6);
            GUILayout.Label("WHOLE GAME", gold);
            Num("Rarity chances x", im.rarityChanceMultiplier, im, v => im.rarityChanceMultiplier = v, false, 0f, 10f);
            Num("Drop chances x", im.dropChanceMultiplier, im, v => im.dropChanceMultiplier = v, false, 0f, 10f, tip: "Kills and breakables; chests keep their own.");
            Num("Kill drops x", im.enemyDropMultiplier, im, v => im.enemyDropMultiplier = v, false, 0f, 10f, tip: "On top, kills only.");
            Num("Gold x", im.goldMultiplier, im, v => im.goldMultiplier = v, false, 0f, 10f);
            Num("Spell tome share", im.tomeShare, im, v => im.tomeShare = v, true, 0f, 1f, tip: "Of gear drops.");
            Num("Merchant rarity x", im.merchantRarity, im, v => im.merchantRarity = v, false, 0f, 10f);
            if (pm != null)
            {
                GUILayout.Space(6);
                GUILayout.Label("SPOILS", gold);
                Num("A kill", pm.enemySpoilsChance, pm, v => pm.enemySpoilsChance = v, true, 0f, 1f);
                Num("A chest", pm.chestSpoilsChance, pm, v => pm.chestSpoilsChance = v, true, 0f, 1f);
            }
        }, () =>
        {
            GUILayout.Label("GEAR FROM A KILL / A CHEST ROLL", gold);
            GUILayout.BeginHorizontal();
            Cell("Floor", 44, true); Cell("Kill gear", 70, true);
            foreach (var rar in GearRarities.Take(4)) Cell(rar.ToString(), 70, true);
            Cell("Chest", 60, true);
            foreach (var rar in GearRarities.Skip(1).Take(3)) Cell(rar.ToString(), 70, true);
            GUILayout.EndHorizontal();
            float killGear = Mathf.Clamp01(im.enemyDrops.gearChance * im.dropChanceMultiplier * im.enemyDropMultiplier);
            float chestGear = Mathf.Clamp01(im.chestDrops.gearChance);
            float notTome = 1f - im.tomeShare;
            for (int f = 1; f <= d.FloorCount; f++)
            {
                var kill = RaritySplit(im, f, im.rarityChanceMultiplier * im.enemyDrops.rarityMultiplier);
                var chest = RaritySplit(im, f, im.rarityChanceMultiplier * im.chestDrops.rarityMultiplier);
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                Cell(f.ToString(), 44); Cell(Pct(killGear), 70);
                for (int i = 0; i < 4; i++) Cell(Pct(killGear * notTome * kill[i]), 70);
                Cell(Pct(chestGear), 60);
                for (int i = 1; i < 4; i++) Cell(Pct(chestGear * notTome * chest[i]), 70);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"Of every kill or chest roll. Tomes: {Pct(im.tomeShare)} of gear. Iron and gold chests roll twice.", dim);
            GUILayout.Space(8);
            GearByFloorTable(d);
        });
    }

    // The level's Gear by floor rows, each tier's share and the upgrade chance, edited in place.
    void GearByFloorTable(TableLevelData d)
    {
        GUILayout.Label($"GEAR BY FLOOR ({d.name})", gold);
        if (d.gearByFloor == null || d.gearByFloor.Count == 0) { GUILayout.Label("No rows: the loot profiles' own tiers.", dim); return; }
        int top = 1;
        foreach (var row in d.gearByFloor) if (row != null) top = Mathf.Max(top, row.HighestTier);
        top = Mathf.Min(LootFloorTiers.Count, top + 1);
        GUILayout.BeginHorizontal();
        Cell("From", 44, true);
        for (int t = 1; t <= top; t++) Cell(LootFloorTiers.Names[t - 1], 66, true);
        Cell("Up +1", 66, true);
        GUILayout.EndHorizontal();
        foreach (var row in d.gearByFloor)
        {
            if (row == null) continue;
            var r = row;
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            int from = EditorGUILayout.DelayedIntField(r.fromFloor, GUILayout.Width(40));
            if (from != r.fromFloor) { Undo.RecordObject(d, "Gear by floor"); r.fromFloor = Mathf.Max(1, from); Changed(d); }
            var shares = r.Shares;
            for (int t = 1; t <= top; t++)
            {
                int tier = t;
                GUILayout.Space(8);
                NumField(shares[t - 1], d, v => r.SetShare(tier, v), false, 0f, 1000f, "Gear by floor", 58);
            }
            GUILayout.Space(8);
            NumField(r.upgradeChance, d, v => r.upgradeChance = v, true, 0f, 1f, "Gear upgrade", 44); GUILayout.Label("%", Unit, GUILayout.Width(12));
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Shares by weight. Up +1: chance it comes one tier better.", dim);
    }
}
