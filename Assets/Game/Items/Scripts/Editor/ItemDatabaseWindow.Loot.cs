using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Item Database's Loot view (the user, 2026-11-13: which items can drop, without asking anyone): pick a level and a
// floor and it lists every item that can drop there, grouped by kind (Spell tomes, Weapons, Shields, Armour, Trinkets,
// Consumables), each with its tier, its loot weight (edited in place) and its chance among that kind of drop, then the
// items that never drop on that floor and why. The odds come from DungeonLootProfile.OddsOn, written beside RollDrops
// from the same rules, with the floor's biome, its loot profile and the level's Gear by floor, so they match the game.
// The List view's header card says on which floors an item drops (LootLine), and Show has Drops as loot / Never drops.
public partial class ItemDatabaseWindow
{
    [SerializeField] bool lootView;
    [SerializeField] string lootLevelPath = "";
    [SerializeField] int lootFloor = 1;
    Vector2 lootScroll;
    static GUIStyle lootChance, lootReason, lootHead;

    const float LootRow = 28f;
    static readonly (string title, Func<ItemData, bool> fits)[] LootGroups =
    {
        ("Spell tomes", i => DungeonLootProfile.IsTomeDrop(i)),
        ("Weapons", i => DungeonLootProfile.IsGearDrop(i) && i.itemType == ItemType.Weapon),
        ("Shields", i => DungeonLootProfile.IsGearDrop(i) && i.itemType == ItemType.Shield),
        ("Armour", i => DungeonLootProfile.IsGearDrop(i) && i.itemType == ItemType.Equipment && !IsTrinket(i)),
        ("Trinkets", i => DungeonLootProfile.IsGearDrop(i) && i.itemType == ItemType.Equipment && IsTrinket(i)),
        ("Consumables", i => DungeonLootProfile.IsRecoveryDrop(i)),
    };
    static bool IsTrinket(ItemData i) => i.equipSlot == EquipmentSlot.Trinket1 || i.equipSlot == EquipmentSlot.Trinket2;
    static bool IsLootKind(ItemData i) => i != null && (DungeonLootProfile.IsTomeDrop(i) || DungeonLootProfile.IsGearDrop(i) || DungeonLootProfile.IsRecoveryDrop(i));

    // ── The level, its floors and their odds ──

    TableLevelData[] lootLevels;
    TableLevelData LootLevel
    {
        get
        {
            lootLevels ??= AssetDatabase.FindAssets("t:TableLevelData").Select(g => AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null && l.IsDungeon).OrderBy(l => l.name).ToArray();
            var level = lootLevels.FirstOrDefault(l => AssetDatabase.GetAssetPath(l) == lootLevelPath)
                ?? lootLevels.FirstOrDefault(l => l.name == "Dungeon1") ?? lootLevels.FirstOrDefault();
            if (level != null) lootLevelPath = AssetDatabase.GetAssetPath(level);
            return level;
        }
    }

    // What loot picks from: the scene's InventoryManager (the game's), else the catalog prefab.
    static InventoryManager LootManager()
    {
        if (InventoryManager.HasInstance) return InventoryManager.Instance;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Core/Prefabs/InventoryManager.prefab");
        return prefab != null ? prefab.GetComponent<InventoryManager>() : null;
    }

    class FloorOdds { public DungeonBiome biome; public DungeonLootProfile profile; public LootSource loot; public DungeonLootProfile.Odds odds; }
    List<FloorOdds> lootCache;
    HashSet<ItemData> lootCatalog;
    double lootCachedAt;
    void ForgetLoot() => lootCache = null;

    // Every floor of the level, worked out once a second at most (or at once after an edit here).
    List<FloorOdds> LootFloors()
    {
        if (lootCache != null && EditorApplication.timeSinceStartup - lootCachedAt < 1.0) return lootCache;
        lootCachedAt = EditorApplication.timeSinceStartup;
        lootCache = new List<FloorOdds>();
        var level = LootLevel;
        var manager = LootManager();
        var catalog = manager != null ? manager.itemCatalog : null;
        lootCatalog = catalog != null ? new HashSet<ItemData>(catalog.Where(i => i != null)) : new HashSet<ItemData>();
        if (level == null) return lootCache;
        float share = manager != null ? manager.tomeShare : .1f;
        for (int floor = 1; floor <= level.FloorCount; floor++)
        {
            var biome = level.BiomeAt(floor, out int at, out int count);
            var f = new FloorOdds { biome = biome, loot = biome != null ? biome.loot : null };
            f.profile = f.loot as DungeonLootProfile;
            float progress = count <= 1 ? 1f : (at - 1f) / (count - 1);
            if (f.profile != null) f.odds = f.profile.OddsOn(level.GearAt(floor), progress, catalog, share);
            lootCache.Add(f);
        }
        return lootCache;
    }

    // The floors an item drops on in the picked level, and why not when none.
    List<int> DropFloors(ItemData item)
    {
        var floors = new List<int>();
        var all = LootFloors();
        for (int f = 0; f < all.Count; f++) if (all[f].odds != null && all[f].odds.ChanceOf(item) > 0f) floors.Add(f + 1);
        return floors;
    }
    bool DropsAsLoot(ItemData item) => IsLootKind(item) && DropFloors(item).Count > 0;

    string NeverReason(ItemData item, FloorOdds floor)
    {
        if (lootCatalog != null && !lootCatalog.Contains(item)) return "not in the catalog";
        if (item.lootWeight <= 0f) return "loot weight 0";
        if (item.tier <= 0) return "tier 0";
        if (floor == null || floor.odds == null) return "floor has no loot profile";
        var o = floor.odds;
        int rolledTop = 0;
        for (int t = 13; t > 0; t--) if (o.tierChance[t] > 0f) { rolledTop = t; break; }
        if (DungeonLootProfile.IsTomeDrop(item))
        {
            if (!o.spellTomes) return "spell tomes off (" + floor.profile.name + ")";
            int cap = Mathf.Max(rolledTop, o.highest);
            return item.tier > cap ? $"tier {item.tier}, floor gives up to {cap}" : "no chance on this floor";
        }
        if (DungeonLootProfile.IsRecoveryDrop(item))
            return item.tier > o.recoveryTop ? $"tier {item.tier}, floor gives up to {o.recoveryTop}" : "no chance on this floor";
        if (item.tier > rolledTop) return $"tier {item.tier}, floor gives up to {rolledTop}";
        if (item.tier < o.lowest) return $"tier {item.tier}, floor gives {o.lowest}-{rolledTop}";
        return "no chance on this floor";
    }

    // The header card's line: on which floors it drops, or why never.
    string LootLine(ItemData item)
    {
        if (!IsLootKind(item)) return "";
        var level = LootLevel;
        var floors = DropFloors(item);
        string where = level != null && level.name != "Dungeon1" ? " of " + level.name : "";
        if (floors.Count > 0) return $"Drops in the dungeon{where} on {FloorList(floors)}";
        if (lootCatalog != null && !lootCatalog.Contains(item)) return "Never drops as loot (not in the catalog)";
        if (item.lootWeight <= 0f) return "Never drops as loot (loot weight 0)";
        if (item.tier <= 0) return "Never drops as loot (tier 0)";
        return $"Never drops as loot (tier {item.tier} not given in {(level != null ? level.name : "any level")})";
    }
    static string FloorList(List<int> floors)
    {
        var parts = new List<string>();
        for (int i = 0; i < floors.Count;)
        {
            int j = i;
            while (j + 1 < floors.Count && floors[j + 1] == floors[j] + 1) j++;
            parts.Add(i == j ? floors[i].ToString() : $"{floors[i]}-{floors[j]}");
            i = j + 1;
        }
        return (floors.Count == 1 ? "floor " : "floors ") + string.Join(", ", parts);
    }

    // ── Drawing ──

    void LootStyles()
    {
        if (lootChance != null) return;
        lootChance = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        lootChance.normal.textColor = new Color(.82f, .82f, .82f);
        lootReason = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        lootReason.normal.textColor = new Color(.85f, .6f, .45f);
        lootHead = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        lootHead.normal.textColor = new Color(.6f, .6f, .6f);
    }

    void LootView(Rect body)
    {
        ListStyles(); LootStyles();
        EditorGUI.DrawRect(body, new Color(.17f, .17f, .17f));
        var level = LootLevel;
        var floors = LootFloors();

        // Level and floor.
        float x = body.x + 10f, y = body.y + 8f;
        GUI.Label(new Rect(x, y, 40f, 20f), "Level"); x += 40f;
        var names = lootLevels.Select(l => l.name).ToArray();
        int at = Array.IndexOf(lootLevels, level);
        int next = EditorGUI.Popup(new Rect(x, y + 1f, 160f, 18f), Mathf.Max(0, at), names);
        if (next != at && next >= 0 && next < lootLevels.Length) { lootLevelPath = AssetDatabase.GetAssetPath(lootLevels[next]); lootFloor = 1; ForgetLoot(); floors = LootFloors(); level = LootLevel; }
        x += 176f;
        int count = Mathf.Max(1, floors.Count);
        lootFloor = Mathf.Clamp(lootFloor, 1, count);
        GUI.Label(new Rect(x, y, 40f, 20f), "Floor"); x += 40f;
        if (GUI.Button(new Rect(x, y, 22f, 20f), "<", EditorStyles.miniButtonLeft) && lootFloor > 1) lootFloor--;
        lootFloor = EditorGUI.Popup(new Rect(x + 22f, y + 1f, 48f, 18f), lootFloor - 1, Enumerable.Range(1, count).Select(n => n.ToString()).ToArray()) + 1;
        if (GUI.Button(new Rect(x + 70f, y, 22f, 20f), ">", EditorStyles.miniButtonRight) && lootFloor < count) lootFloor++;
        GUI.Label(new Rect(x + 98f, y, 60f, 20f), $"of {count}", EditorStyles.miniLabel);
        y += 26f;

        var floor = lootFloor - 1 < floors.Count ? floors[lootFloor - 1] : null;
        var odds = floor?.odds;
        // The floor in one line: its biome, its gear tiers, the tomes' share.
        string summary;
        if (level == null) summary = "No dungeon level found.";
        else if (floor == null || floor.biome == null) summary = "This floor has no biome.";
        else if (odds == null) summary = $"{floor.biome.displayName}   ·   Loot: {(floor.loot != null ? floor.loot.name + " (hand-listed table)" : "none")}";
        else
        {
            var tiers = new List<string>();
            for (int t = 1; t < 14; t++) if (odds.tierChance[t] > 0.0005f) tiers.Add($"{TierName(t)} {Percent(odds.tierChance[t])}");
            summary = $"{floor.biome.displayName}   ·   Loot: {floor.profile.name}   ·   Gear tiers: {string.Join(", ", tiers)}   ·   "
                + (odds.spellTomes ? $"Tomes: {Percent(odds.tomeShareOfGear)} of gear drops" : "Tomes: off");
        }
        GUI.Label(new Rect(body.x + 10f, y, body.width - 20f, 20f), summary, subtitleStyle);
        y += 26f;
        EditorGUI.DrawRect(new Rect(body.x, y - 3f, body.width, 1f), new Color(.1f, .1f, .1f));

        // The rows: every loot item that is not archived, grouped.
        var pool = items.Where(i => i != null && !ItemDatabaseAuthoring.IsArchived(i) && IsLootKind(i)).ToList();
        var rows = new List<(string head, string headRight, ItemData item, float chance, string text)>();
        var never = new List<ItemData>();
        foreach (var (title, fits) in LootGroups)
        {
            var group = pool.Where(i => fits(i)).ToList();
            var dropping = odds == null ? new List<ItemData>() : group.Where(i => odds.ChanceOf(i) > 0f).OrderBy(i => i.tier).ThenBy(i => i.itemName).ToList();
            never.AddRange(group.Where(i => !dropping.Contains(i)));
            if (dropping.Count == 0) continue;
            string kind = title == "Spell tomes" ? "tome" : title == "Consumables" ? "recovery" : "gear";
            float groupShare = dropping.Sum(i => odds.ChanceOf(i));
            string right = kind == "gear" ? $"{Percent(groupShare)} of gear drops" : $"{dropping.Count} items";
            rows.Add(($"{title.ToUpperInvariant()}  ({dropping.Count})", right, null, 0f, null));
            foreach (var item in dropping) { float c = odds.ChanceOf(item); rows.Add((null, null, item, c, $"1 in {OneIn(c)} {kind} drops")); }
        }
        // Items that never drop anywhere (not in the catalog, loot weight 0, tier 0) first, then the ones of other floors.
        int Never(ItemData i) => lootCatalog != null && !lootCatalog.Contains(i) ? 0 : i.lootWeight <= 0f ? 1 : i.tier <= 0 ? 2 : 3;
        never = never.OrderBy(Never).ThenBy(i => LootGroups.ToList().FindIndex(g => g.fits(i))).ThenBy(i => i.tier).ThenBy(i => i.itemName).ToList();
        if (never.Count > 0)
        {
            rows.Add(($"NEVER DROPS ON FLOOR {lootFloor}  ({never.Count})", "", null, 0f, null));
            foreach (var item in never) rows.Add((null, null, item, 0f, NeverReason(item, floor)));
        }

        // Column titles.
        var columns = new Rect(body.x, y, body.width, 18f);
        float nameX = 10f + LootRow + 6f, tierX = 330f, weightX = 370f, chanceX = 460f;
        GUI.Label(new Rect(columns.x + nameX, y, 200f, 18f), "ITEM", lootHead);
        GUI.Label(new Rect(columns.x + tierX, y, 40f, 18f), "TIER", lootHead);
        GUI.Label(new Rect(columns.x + weightX, y, 90f, 18f), "LOOT WEIGHT", lootHead);
        GUI.Label(new Rect(columns.x + chanceX, y, 200f, 18f), "CHANCE", lootHead);
        y += 20f;

        var view = new Rect(body.x, y, body.width, body.yMax - y);
        float height = rows.Sum(r => r.item == null ? HeaderHeight : LootRow);
        lootScroll = GUI.BeginScrollView(view, lootScroll, new Rect(0, 0, view.width - 14f, height));
        float width = height > view.height ? view.width - 14f : view.width;
        float ry = 0f; int stripe = 0;
        foreach (var row in rows)
        {
            if (row.item == null)
            {
                var h = new Rect(0, ry, width, HeaderHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(h, new Color(.21f, .205f, .195f));
                    EditorGUI.DrawRect(new Rect(h.x, h.yMax - 1f, h.width, 1f), new Color(.12f, .12f, .12f));
                }
                GUI.Label(new Rect(10f, ry, 400f, HeaderHeight), row.head, groupTitle);
                GUI.Label(new Rect(chanceX, ry, 300f, HeaderHeight), row.headRight, groupCount.Left());
                ry += HeaderHeight; stripe = 0;
                continue;
            }
            var r = new Rect(0, ry, width, LootRow);
            ry += LootRow;
            if (r.yMax < lootScroll.y || r.y > lootScroll.y + view.height) { stripe++; continue; }
            if (Event.current.type == EventType.Repaint)
            {
                if (row.item == selected) EditorGUI.DrawRect(r, new Color(.17f, .33f, .48f));
                else if (stripe % 2 == 1) EditorGUI.DrawRect(r, new Color(.19f, .19f, .19f));
            }
            stripe++;
            DrawSlot(new Rect(10f, r.y + 2f, LootRow - 4f, LootRow - 4f), row.item, 2f);
            var nameRect = new Rect(nameX, r.y, tierX - nameX - 8f, r.height);
            GUI.Label(nameRect, row.item.itemName, NameStyle(rowName, row.item));
            GUI.Label(new Rect(tierX, r.y, 40f, r.height), row.item.tier > 0 ? "T" + row.item.tier : "-", rowName);
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUI.DelayedFloatField(new Rect(weightX, r.y + 5f, 60f, 18f), row.item.lootWeight);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(row.item, "Loot weight");
                row.item.lootWeight = Mathf.Max(0f, weight);
                EditorUtility.SetDirty(row.item);
                ForgetLoot();
            }
            string chance = row.chance > 0f ? $"{row.text}   ({Percent(row.chance)})" : row.text;
            GUI.Label(new Rect(chanceX, r.y, width - chanceX - 8f, r.height), chance, row.chance > 0f ? lootChance : lootReason);
            // A click picks it; a double click opens it in the List view.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && nameRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.clickCount > 1) { lootView = false; ClearFilters(); Select(row.item); revealSelected = true; }
                else Select(row.item);
                GUI.FocusControl(null); Event.current.Use(); Repaint();
            }
        }
        if (rows.Count == 0) GUI.Label(new Rect(0, 20f, view.width, 20f), "Nothing drops on this floor.", EditorStyles.centeredGreyMiniLabel);
        GUI.EndScrollView();
    }

    static string TierName(int t) => t >= 1 && t <= LootFloorTiers.Count ? LootFloorTiers.Names[t - 1] : "Tier " + t;
    static string Percent(float p) => p >= .995f ? "100%" : p >= .1f ? $"{p * 100f:0}%" : p >= .01f ? $"{p * 100f:0.#}%" : $"{p * 100f:0.##}%";
    static string OneIn(float p) { float n = 1f / Mathf.Max(1e-6f, p); return n < 10f ? n.ToString("0.#") : Mathf.RoundToInt(n).ToString(); }
}
