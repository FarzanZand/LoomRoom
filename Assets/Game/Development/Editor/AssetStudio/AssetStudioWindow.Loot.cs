using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Dungeon's Loot page, its table (the user, 2026-11-15: "how come we can't even actually SEE or EDIT what the loot
// table actually has per floor? I can't figure out how to edit weights of tomes or add remove stuff"; and: "a loot
// table view where I can see all items in a row (sortable and filterable) and all floors in a column, and in the same
// view edit the weight of all items"). One row per item that loot can give (spell tomes, weapons, shields, armour,
// trinkets, potions and food), one column per floor of the dungeon. A cell is how often that item comes on that floor:
// its share of the floor's gear drops (a tome or a piece of gear; they share one roll) or of its recovery drops (a
// potion or food). Each row's Tier and Loot weight are edited right there, and every floor's numbers follow at once.
//   Tier: the gear tier it belongs to. Gear drops on the floors whose Gear by floor row gives that tier. A tome or a
//   consumable comes from its tier up: tier 1 is every floor, tier 3 only where tier 3 gear drops and deeper. Tier 0:
//   never as loot (hand-placed things: relics, class kit, keys).
//   Loot weight: how often it is picked among the others of its kind that can come. 0: never.
// The numbers are DungeonLootProfile.OddsOn's, written beside the game's own roll (RollDrops) from the same rules, with
// each floor's biome, its loot profile, the level's Gear by floor and InventoryManager's Tome Share and catalog, so the
// table says what the game does. Click a column's title to sort by it; the search and the two filters narrow the rows.
// Changes are saved at once and go through Undo.
public partial class AssetStudioWindow
{
    string lootSearch = "";
    [SerializeField] int lootKind, lootShow, lootSort = 1;   // lootSort: 0 name, 1 kind, 2 tier, 3 weight, 10+n floor n+1
    [SerializeField] bool lootSortDown;
    static readonly string[] LootKinds = { "Every kind", "Spell tomes", "Weapons", "Shields", "Armour", "Trinkets", "Consumables" };
    static readonly string[] LootShows = { "Drops in this dungeon", "Every item", "Never drops here" };

    class LootFloor { public int floor; public DungeonBiome biome; public DungeonLootProfile profile; public DungeonLootProfile.Odds odds; }
    class LootRow { public ItemData item; public int kind; public float[] chance; public string why; }
    // Read from the assets, never kept through a script reload (a kept list comes back empty, not null).
    [NonSerialized] List<LootFloor> lootFloors;
    [NonSerialized] List<ItemData> lootItems;
    [NonSerialized] HashSet<ItemData> lootCatalog;
    [NonSerialized] TableLevelData lootFor;
    [NonSerialized] double lootReadAt;
    [NonSerialized] InventoryManager lootManager;

    // What loot picks from: the open scene's InventoryManager (the one the game uses), else its prefab.
    static InventoryManager LootManager()
    {
        if (InventoryManager.HasInstance) return InventoryManager.Instance;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryManagerPath);
        return prefab != null ? prefab.GetComponent<InventoryManager>() : null;
    }

    static int LootKindOf(ItemData i)
    {
        if (DungeonLootProfile.IsTomeDrop(i)) return 1;
        if (DungeonLootProfile.IsGearDrop(i))
        {
            if (i.itemType == ItemType.Weapon) return 2;
            if (i.itemType == ItemType.Shield) return 3;
            return i.equipSlot == EquipmentSlot.Trinket1 || i.equipSlot == EquipmentSlot.Trinket2 ? 5 : 4;
        }
        return DungeonLootProfile.IsRecoveryDrop(i) ? 6 : 0;
    }
    static string LootKindName(ItemData i)
    {
        switch (LootKindOf(i))
        {
            case 1: return "Spell tome";
            case 2: return i.weaponCategory.ToString();
            case 3: return "Shield";
            case 4: return ItemData.SlotName(i.equipSlot);
            case 5: return "Trinket";
            default: return "Consumable";
        }
    }

    // Every floor's odds and the items, read again after an edit here and once a second otherwise (edits made elsewhere).
    void ReadLoot(TableLevelData level, bool now = false)
    {
        if (!now && lootFloors != null && lootFor == level && EditorApplication.timeSinceStartup - lootReadAt < 1.0) return;
        lootReadAt = EditorApplication.timeSinceStartup;
        lootFor = level;
        lootManager = LootManager();
        var catalog = lootManager != null ? lootManager.itemCatalog : null;
        lootCatalog = catalog != null ? new HashSet<ItemData>(catalog.Where(i => i != null)) : new HashSet<ItemData>();
        float share = lootManager != null ? lootManager.tomeShare : .1f;
        lootFloors = new List<LootFloor>();
        for (int floor = 1; floor <= level.FloorCount; floor++)
        {
            var biome = level.BiomeAt(floor, out int at, out int count);
            var f = new LootFloor { floor = floor, biome = biome, profile = biome != null ? biome.loot as DungeonLootProfile : null };
            float progress = count <= 1 ? 1f : (at - 1f) / (count - 1);
            if (f.profile != null) f.odds = f.profile.OddsOn(level.GearAt(floor), progress, catalog, share);
            lootFloors.Add(f);
        }
        if (lootItems == null || lootItems.Count == 0 || now || lootItems.Any(i => i == null))
            lootItems = AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game" })
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(i => i != null && !ItemDatabaseAuthoring.IsArchived(i) && LootKindOf(i) != 0).ToList();
    }

    // An item's share of a floor's drops: of its gear drops (tomes and gear share that roll), or of its recovery drops.
    static float LootChance(LootFloor f, ItemData i)
    {
        var o = f.odds;
        if (o == null) return 0f;
        if (o.tomes.TryGetValue(i, out float t)) return t * o.tomeShareOfGear;
        if (o.gear.TryGetValue(i, out float g)) return g * (1f - o.tomeShareOfGear);
        return o.recovery.TryGetValue(i, out float r) ? r : 0f;
    }

    // Why an item comes on no floor of this dungeon.
    string LootWhyNot(ItemData i)
    {
        if (lootCatalog != null && !lootCatalog.Contains(i)) return "not in the item catalog";
        if (i.lootWeight <= 0f) return "loot weight 0";
        if (i.tier <= 0) return "tier 0";
        if (DungeonLootProfile.IsTomeDrop(i) && lootFloors.All(f => f.odds == null || !f.odds.spellTomes)) return "spell tomes are off here";
        int top = 0;
        foreach (var f in lootFloors) if (f.odds != null) for (int t = 13; t > 0; t--) if (f.odds.tierChance[t] > 0f) { top = Mathf.Max(top, t); break; }
        return i.tier > top ? $"tier {i.tier}: these floors give up to tier {top}" : "nothing rolls it here";
    }

    static string LootPercent(float p) => p <= 0f ? "-" : p >= .0995f ? $"{p * 100f:0}%" : p >= .00995f ? $"{p * 100f:0.#}%" : $"{p * 100f:0.##}%";

    void ChangeLootItem(ItemData item, string what, Action change)
    {
        Undo.RecordObject(item, what);
        change();
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssetIfDirty(item);
        ReadLoot(lootFor, true);
    }

    void LootTable(AssetStudioWorld.Level l)
    {
        var d = l.data;
        ReadLoot(d);
        Section("WHAT DROPS, FLOOR BY FLOOR");
        GUILayout.Label("Each item's share of a floor's drops: of its gear drops for tomes and gear, of its potion and food drops for consumables. Edit Tier and Weight in the row; every floor follows at once.", new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(pageWidth));
        GUILayout.Label("<color=#b8ae9c>Tier</color> is where it starts: gear drops on the floors that give its tier; a tome or a consumable comes from its tier's floors on, all the way down. Tier 0 or Weight 0: never as loot.", new GUIStyle(dim) { wordWrap = true, richText = true }, GUILayout.Width(pageWidth));
        GUILayout.Space(4);

        // The filters, and the tomes' share of gear.
        GUILayout.BeginHorizontal(GUILayout.Width(pageWidth));
        GUILayout.Label("Find", dim, GUILayout.Width(34));
        lootSearch = EditorGUILayout.TextField(lootSearch, GUILayout.Width(170));
        GUILayout.Space(8);
        lootKind = EditorGUILayout.Popup(lootKind, LootKinds, GUILayout.Width(130));
        lootShow = EditorGUILayout.Popup(lootShow, LootShows, GUILayout.Width(180));
        GUILayout.FlexibleSpace();
        if (lootManager != null)
        {
            GUILayout.Label(new GUIContent("Tomes are", "InventoryManager > Tome Share: of every gear drop, how often it is a spell tome instead. One share for all tomes together, so more spells don't make tomes more common."), dim, GUILayout.Width(74));
            EditorGUI.BeginChangeCheck();
            float percent = EditorGUILayout.DelayedFloatField(Mathf.Round(lootManager.tomeShare * 1000f) / 10f, GUILayout.Width(44));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(lootManager, "Tome share");
                lootManager.tomeShare = Mathf.Clamp01(percent / 100f);
                EditorUtility.SetDirty(lootManager);
                if (EditorUtility.IsPersistent(lootManager)) AssetDatabase.SaveAssetIfDirty(lootManager);
                else { PrefabUtility.RecordPrefabInstancePropertyModifications(lootManager); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lootManager.gameObject.scene); Toast("Tome share is on the room's InventoryManager: save the scene to keep it."); }
                ReadLoot(d, true);
            }
            GUILayout.Label("% of gear drops", dim, GUILayout.Width(110));
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        // The rows.
        int floors = lootFloors.Count;
        string find = lootSearch.Trim().ToLowerInvariant();
        var rows = new List<LootRow>();
        foreach (var item in lootItems)
        {
            if (item == null) continue;
            int kind = LootKindOf(item);
            if (lootKind != 0 && kind != lootKind) continue;
            if (find.Length > 0 && !item.itemName.ToLowerInvariant().Contains(find) && !LootKindName(item).ToLowerInvariant().Contains(find)) continue;
            var row = new LootRow { item = item, kind = kind, chance = new float[floors] };
            bool any = false;
            for (int f = 0; f < floors; f++) { row.chance[f] = LootChance(lootFloors[f], item); any |= row.chance[f] > 0f; }
            if (lootShow == 0 && !any || lootShow == 2 && any) continue;
            if (!any) row.why = LootWhyNot(item);
            rows.Add(row);
        }
        IEnumerable<LootRow> sorted;
        if (lootSort == 0) sorted = rows.OrderBy(r => r.item.itemName, StringComparer.OrdinalIgnoreCase);
        else if (lootSort == 2) sorted = rows.OrderBy(r => r.item.tier).ThenBy(r => r.kind).ThenBy(r => r.item.itemName, StringComparer.OrdinalIgnoreCase);
        else if (lootSort == 3) sorted = rows.OrderBy(r => r.item.lootWeight).ThenBy(r => r.item.itemName, StringComparer.OrdinalIgnoreCase);
        else if (lootSort >= 10 && lootSort - 10 < floors) { int f = lootSort - 10; sorted = rows.OrderBy(r => r.chance[f]).ThenBy(r => r.item.itemName, StringComparer.OrdinalIgnoreCase); }
        else sorted = rows.OrderBy(r => r.kind).ThenBy(r => LootKindName(r.item)).ThenBy(r => r.item.tier).ThenBy(r => r.item.itemName, StringComparer.OrdinalIgnoreCase);
        rows = sorted.ToList();
        if (lootSortDown) rows.Reverse();

        // Columns: item, kind, tier, weight, then the floors sharing what is left.
        const float rowH = 26, kindW = 96, tierW = 46, weightW = 62, removeW = 26;
        float floorW = Mathf.Clamp((pageWidth - 200 - kindW - tierW - weightW - removeW) / Mathf.Max(1, floors), 44, 120);
        float nameW = Mathf.Max(160, pageWidth - kindW - tierW - weightW - removeW - floorW * floors);
        float total = nameW + kindW + tierW + weightW + removeW + floorW * floors;

        // The floors' own line: biome and the gear tiers that drop there.
        var head = GUILayoutUtility.GetRect(total, 62, GUILayout.Width(total));
        if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(head, new Color(1, 1, 1, .04f));
        var small = new GUIStyle(dimLine) { alignment = TextAnchor.UpperCenter, fontSize = StudioSkin.Size, richText = true };
        float fx = head.x + nameW + kindW + tierW + weightW;
        for (int f = 0; f < floors; f++)
        {
            var lf = lootFloors[f];
            var box = new Rect(fx + f * floorW, head.y + 2, floorW, 18);
            string tiers = "no loot";
            string tip = lf.biome != null ? lf.biome.name : "no biome";
            if (lf.odds != null)
            {
                var names = new List<string>(); var full = new List<string>();
                for (int t = 1; t < 14; t++)
                    if (lf.odds.tierChance[t] > .0005f)
                    {
                        string name = t <= LootFloorTiers.Count ? LootFloorTiers.Names[t - 1] : "Tier " + t;
                        names.Add("T" + t); full.Add($"tier {t} {name} {LootPercent(lf.odds.tierChance[t])}");
                    }
                tiers = string.Join(" ", names);
                tip = $"{lf.biome.name} · loot: {lf.profile.name}\nGear: {string.Join(", ", full)}\n{(lf.odds.spellTomes ? $"Tomes: {LootPercent(lf.odds.tomeShareOfGear)} of gear drops" : "No spell tomes")}";
            }
            else if (lf.biome != null && lf.biome.loot != null) { tiers = "listed"; tip = $"{lf.biome.name} · {lf.biome.loot.name}: a hand-listed table, not shown here."; }
            GUI.Label(box, new GUIContent(Fit(lf.biome != null ? lf.biome.name : "-", small, floorW - 4), tip), small);
            GUI.Label(new Rect(box.x, box.y + 18, floorW, 18), new GUIContent($"<color=#8f8a80>{Fit(tiers, small, floorW - 4)}</color>", tip), small);
        }
        // The titles: click to sort.
        float hy = head.yMax - 22;
        LootTitle(new Rect(head.x + 30, hy, nameW - 30, 20), "ITEM", 0, TextAnchor.MiddleLeft, "Sort by name.");
        LootTitle(new Rect(head.x + nameW, hy, kindW, 20), "KIND", 1, TextAnchor.MiddleLeft, "Sort by kind, then tier.");
        LootTitle(new Rect(head.x + nameW + kindW, hy, tierW, 20), "TIER", 2, TextAnchor.MiddleLeft, "The gear tier it belongs to (a tome or consumable: the tier it starts at). 0 = never as loot. Click to sort.");
        LootTitle(new Rect(head.x + nameW + kindW + tierW, hy, weightW, 20), "WEIGHT", 3, TextAnchor.MiddleLeft, "Loot weight: how often it is picked among the others of its kind that can come. 0 = never. Click to sort.");
        for (int f = 0; f < floors; f++) LootTitle(new Rect(fx + f * floorW, hy, floorW, 20), floorW >= 70 ? "FLOOR " + (f + 1) : "F" + (f + 1), 10 + f, TextAnchor.MiddleCenter, "Sort by the share on this floor.");
        StudioSkin.Divider(new Rect(head.x, head.yMax - 1, total, 1));

        // The rows, drawn only where they show.
        var body = GUILayoutUtility.GetRect(total, Mathf.Max(rowH, rows.Count * rowH), GUILayout.Width(total));
        if (rows.Count == 0) GUI.Label(new Rect(body.x + 8, body.y + 4, total, 20), lootShow == 2 ? "Every item of this kind drops somewhere in this dungeon." : "Nothing matches.", dim);
        float seenTop = dungeonPageScroll.y - rowH, seenBottom = dungeonPageScroll.y + position.height;
        var nameStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleLeft, normal = { textColor = StudioSkin.Text } };
        var kindStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleLeft };
        var cellStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleCenter, richText = true };
        var whyStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleLeft, normal = { textColor = StudioSkin.Warn } };
        var removeStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.5f, .48f, .45f) }, hover = { textColor = new Color(1f, .5f, .4f) } };
        for (int i = 0; i < rows.Count; i++)
        {
            var r = new Rect(body.x, body.y + i * rowH, total, rowH);
            if (r.yMax < seenTop || r.y > seenBottom) continue;
            var row = rows[i];
            var item = row.item;
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (hover) EditorGUI.DrawRect(r, new Color(1, 1, 1, .07f));
                else if (i % 2 == 1) EditorGUI.DrawRect(r, new Color(1, 1, 1, .025f));
                if (item.icon != null) DrawSprite(new Rect(r.x + 4, r.y + 2, 22, 22), item.icon);
            }
            // The name: click to see the item on the right.
            var nameRect = new Rect(r.x + 30, r.y, nameW - 34, rowH);
            nameStyle.normal.textColor = row.why != null ? StudioSkin.Dim * new Color(1, 1, 1, .7f) : StudioSkin.Text;
            GUI.Label(nameRect, new GUIContent(Fit(item.itemName, nameStyle, nameRect.width), "Click to see and edit the item on the right."), nameStyle);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && nameRect.Contains(Event.current.mousePosition)) { ShowAsset(item); GUI.FocusControl(null); Event.current.Use(); }
            GUI.Label(new Rect(r.x + nameW, r.y, kindW - 4, rowH), LootKindName(item), kindStyle);

            EditorGUI.BeginChangeCheck();
            int tier = EditorGUI.DelayedIntField(new Rect(r.x + nameW + kindW, r.y + 4, tierW - 8, 18), item.tier);
            if (EditorGUI.EndChangeCheck()) ChangeLootItem(item, "Tier", () => item.tier = Mathf.Clamp(tier, 0, LootFloorTiers.Count));
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUI.DelayedFloatField(new Rect(r.x + nameW + kindW + tierW, r.y + 4, weightW - 8, 18), item.lootWeight);
            if (EditorGUI.EndChangeCheck()) ChangeLootItem(item, "Loot weight", () => item.lootWeight = Mathf.Max(0f, weight));

            if (row.why != null)
            {
                // Never here: why, and a button that puts it into the loot where that is a number to set.
                float wx = fx;
                bool addable = lootCatalog.Contains(item) && (item.lootWeight <= 0f || item.tier <= 0);
                if (addable)
                {
                    if (GUI.Button(new Rect(wx, r.y + 2, 60, rowH - 4), new GUIContent("+ Add", "Put it into the loot: weight 1 (if it is 0) and tier 1 (if it is 0). Set its tier after.")))
                        ChangeLootItem(item, "Add to loot", () => { if (item.lootWeight <= 0f) item.lootWeight = 1f; if (item.tier <= 0) item.tier = 1; });
                    wx += 66;
                }
                GUI.Label(new Rect(wx, r.y, total - (wx - r.x) - 4, rowH), row.why, whyStyle);
            }
            else
            {
                for (int f = 0; f < floors; f++)
                {
                    float p = row.chance[f];
                    var c = new Rect(fx + f * floorW, r.y, floorW, rowH);
                    if (p > 0f && Event.current.type == EventType.Repaint)
                    {
                        // A bar behind the number: how big a share it is among the rows' largest on that floor.
                        float most = Mathf.Max(.0001f, rows.Max(x => x.chance[f]));
                        EditorGUI.DrawRect(new Rect(c.x + 3, c.yMax - 5, (c.width - 6) * Mathf.Clamp01(p / most), 3), new Color(.91f, .74f, .38f, .55f));
                    }
                    string kindOfDrop = row.kind == 6 ? "potion and food drops" : "gear drops";
                    string text = p > 0f ? LootPercent(p) : "<color=#5d5a55>-</color>";
                    GUI.Label(c, new GUIContent(text, p > 0f ? $"{item.itemName} on floor {f + 1}: {LootPercent(p)} of {kindOfDrop}, 1 in {Mathf.Max(1, Mathf.RoundToInt(1f / p))}." : $"{item.itemName} does not drop on floor {f + 1}."), cellStyle);
                }
                // Out of the loot with one click (weight 0); it stays listed under Never drops here.
                if (GUI.Button(new Rect(r.xMax - removeW, r.y + 3, removeW - 4, rowH - 6), new GUIContent("✕", "Take it out of the loot (weight 0). It stays under Never drops here, where + Add puts it back."), removeStyle))
                    ChangeLootItem(item, "Take out of loot", () => item.lootWeight = 0f);
            }
        }
        if (Event.current.type == EventType.MouseMove) Repaint();   // the hovered row
        GUILayout.Space(4);
        int dropping = rows.Count(r => r.why == null);
        GUILayout.Label($"{rows.Count} item{(rows.Count == 1 ? "" : "s")} shown, {dropping} of them in this dungeon's loot. A gear drop is one roll: a tome ({(lootManager != null ? LootPercent(lootManager.tomeShare) : "10%")}) or a piece of gear of the floor's tier. How often a kill or a chest gives one at all is under Every dungeon, below, and on Balance > Drops.", new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(pageWidth));
    }

    void LootTitle(Rect r, string text, int sort, TextAnchor align, string tip)
    {
        bool on = lootSort == sort;
        var style = new GUIStyle(gold) { alignment = align, fontSize = StudioSkin.Size };
        if (!on) style.normal.textColor = StudioSkin.Dim;
        if (GUI.Button(r, new GUIContent(on ? text + (lootSortDown ? " ▼" : " ▲") : text, tip), style))
        {
            if (on) lootSortDown = !lootSortDown; else { lootSort = sort; lootSortDown = sort >= 10; }
            GUI.FocusControl(null);
        }
    }

    // For tests (an eval): the table's numbers as text, a line per item with its share on each floor.
    public string TestLoot(string find = "")
    {
        var d = DungeonLevel;
        ReadLoot(d, true);
        var sb = new System.Text.StringBuilder();
        foreach (var f in lootFloors) sb.Append($"floor {f.floor}: {(f.biome != null ? f.biome.name : "-")} loot {(f.profile != null ? f.profile.name : "-")} tomes {(f.odds != null ? LootPercent(f.odds.tomeShareOfGear) : "-")}\n");
        foreach (var i in lootItems.OrderBy(LootKindOf).ThenBy(x => x.tier).ThenBy(x => x.itemName))
        {
            if (find.Length > 0 && !i.itemName.ToLowerInvariant().Contains(find.ToLowerInvariant()) && !LootKindName(i).ToLowerInvariant().Contains(find.ToLowerInvariant())) continue;
            sb.Append($"{i.itemName} [{LootKindName(i)} T{i.tier} w{i.lootWeight}]: {string.Join(" ", lootFloors.Select(f => LootPercent(LootChance(f, i))))}\n");
        }
        return sb.ToString();
    }
}
