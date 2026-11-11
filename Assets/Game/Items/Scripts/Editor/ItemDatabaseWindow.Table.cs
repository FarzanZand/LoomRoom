using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Item Database's Table view (the user, 2026-11-11): the toolbar's Table button swaps the window to a spreadsheet of
// one group of items side by side, every number editable in place. On the left the group is picked, By kind (all Helms,
// sorted by tier) or By tier (everything of Tier 3, sorted by kind); on the right one row per item: its name (frozen
// while the rest scrolls sideways), tier, price, loot weight, a weapon's hits and spread, then a column for every stat
// any of them has (the main number first: damage, block, armour). A stat the game shows as a share (critical chance,
// gold found, swing speed) is typed as a percent and stored as the fraction. An empty cell is a stat the item lacks:
// typing a number adds it, clearing a number removes it. A header click sorts by that column (again to turn it round),
// a double click on a name opens the item in the List view. Every edit is one undo step and marks the asset to save.
public partial class ItemDatabaseWindow
{
    [SerializeField] bool tableView, tableByTier;
    [SerializeField] string tableKind = "Helms";
    [SerializeField] int tableTier = 1;
    [SerializeField] string tableSort = "";
    [SerializeField] bool tableSortDown;
    Vector2 tableScroll, tablePickScroll;
    ItemData tableRow;
    double lastNameClick;
    static GUIStyle cellStyle, cellRead, headStyle, nameCell, pickRow, pickCount;

    const float TableRowHeight = 24f, TableHeadHeight = 40f, NameWidth = 220f, PickWidth = 230f;

    // ── Columns ──

    sealed class Column
    {
        public string key, title, tip;
        public float width = 76f;
        public bool readOnly, text;
        public Func<ItemData, string> get;
        public Action<ItemData, string> set;
        public Func<ItemData, float> sort;
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static bool Parse(string s, out float v) => float.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // A stat the game writes as a share ("+6% critical chance") is shown and typed as a percent.
    static readonly Dictionary<(StatType, ModifierType), bool> percent = new();
    static bool IsPercent(StatType stat, ModifierType type)
    {
        if (!percent.TryGetValue((stat, type), out bool p)) percent[(stat, type)] = p = ItemData.StatLine(stat, type, 1f).Contains("%");
        return p;
    }

    static StatModifierEntry Mod(ItemData i, StatType stat, ModifierType type) =>
        i.statModifiers?.FirstOrDefault(m => m != null && m.stat == stat && m.type == type);

    List<Column> TableColumns(List<ItemData> rows)
    {
        var cols = new List<Column>();
        if (tableByTier)
            cols.Add(new Column { key = "kind", title = "Kind", width = 120f, readOnly = true, text = true, get = i => Group(i).title, sort = i => Group(i).order });
        cols.Add(new Column { key = "tier", title = "Tier", width = 48f, tip = "Loot tier, 0 = never dropped at random.", get = i => i.tier.ToString(),
            set = (i, s) => { if (Parse(s, out float v)) Change(i, "Tier", () => i.tier = Mathf.Clamp(Mathf.RoundToInt(v), 0, 12)); }, sort = i => i.tier });
        cols.Add(new Column { key = "price", title = "Price", width = 60f, tip = "Gold at a merchant.", get = i => i.value.ToString(),
            set = (i, s) => { if (Parse(s, out float v)) Change(i, "Price", () => i.value = Mathf.Max(0, Mathf.RoundToInt(v))); }, sort = i => i.value });
        cols.Add(new Column { key = "weight", title = "Loot weight", width = 60f, tip = "How often it is picked among its tier and kind.", get = i => F(i.lootWeight),
            set = (i, s) => { if (Parse(s, out float v)) Change(i, "Loot weight", () => i.lootWeight = Mathf.Max(0f, v)); }, sort = i => i.lootWeight });
        if (rows.Any(i => i.IsWeapon))
        {
            cols.Add(new Column { key = "hits", title = "Hits", width = 64f, readOnly = true, tip = "What a hit rolls: damage minus spread to damage plus spread and up.",
                get = i => i.IsWeapon && i.TryMainStat(out _, out _, out string t) ? t : null, sort = MainNumber });
            cols.Add(new Column { key = "spread", title = "Spread", width = 56f, tip = "Hits land this much below or above the damage.", get = i => i.IsWeapon ? i.damageSpread.ToString() : null,
                set = (i, s) => { if (i.IsWeapon && Parse(s, out float v)) Change(i, "Spread", () => i.damageSpread = Mathf.Max(0, Mathf.RoundToInt(v))); }, sort = i => i.damageSpread });
            cols.Add(new Column { key = "up", title = "Spread up", width = 56f, tip = "This much more at the high end only.", get = i => i.IsWeapon ? i.damageSpreadUp.ToString() : null,
                set = (i, s) => { if (i.IsWeapon && Parse(s, out float v)) Change(i, "Spread up", () => i.damageSpreadUp = Mathf.Max(0, Mathf.RoundToInt(v))); }, sort = i => i.damageSpreadUp });
        }

        // A column per stat any shown item has (null: a cell that doesn't apply to an item is left blank): the main numbers first, then the most common.
        var stats = rows.SelectMany(i => i.statModifiers ?? Array.Empty<StatModifierEntry>()).Where(m => m != null)
            .GroupBy(m => (m.stat, m.type))
            .OrderBy(g => g.Key.stat == StatType.AttackDamage ? 0 : g.Key.stat == StatType.Block ? 1 : g.Key.stat == StatType.Armor ? 2 : 3)
            .ThenByDescending(g => g.Count()).ThenBy(g => g.Key.stat.ToString());
        foreach (var g in stats)
        {
            var (stat, type) = g.Key;
            bool pct = IsPercent(stat, type);
            string name = StatModifierEntry.Label(stat);
            if (name == stat.ToString()) name = ObjectNames.NicifyVariableName(name);
            string title = name + (type == ModifierType.PercentAdd ? " (+%)" : type == ModifierType.PercentMultiply ? " (x%)" : "") + (pct ? " %" : "");
            cols.Add(new Column
            {
                key = $"s:{(int)stat}:{(int)type}", title = title, width = 78f,
                tip = $"{ItemData.StatLine(stat, type, pct ? .1f : 1f)} (example). Empty: the item doesn't have it; clear a number to remove it.",
                get = i => { var m = Mod(i, stat, type); return m == null ? "" : F(pct ? m.value * 100f : m.value); },
                set = (i, s) => SetStat(i, stat, type, pct, s),
                sort = i => { var m = Mod(i, stat, type); return m == null ? float.MinValue : m.value; },
            });
        }
        return cols;
    }

    void SetStat(ItemData i, StatType stat, ModifierType type, bool pct, string s)
    {
        var mods = (i.statModifiers ?? Array.Empty<StatModifierEntry>()).ToList();
        int at = mods.FindIndex(m => m != null && m.stat == stat && m.type == type);
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "-")
        {
            if (at < 0) return;
            Change(i, "Remove " + stat, () => { mods.RemoveAt(at); i.statModifiers = mods.ToArray(); });
            return;
        }
        if (!Parse(s, out float v)) return;
        if (pct) v /= 100f;
        if (at >= 0 && Mathf.Approximately(mods[at].value, v)) return;
        Change(i, stat.ToString(), () =>
        {
            if (at >= 0) mods[at].value = v;
            else mods.Add(new StatModifierEntry { stat = stat, type = type, value = v });
            i.statModifiers = mods.ToArray();
        });
    }

    void Change(ItemData i, string what, Action apply)
    {
        Undo.RecordObject(i, "Item table: " + what);
        apply();
        EditorUtility.SetDirty(i);
        issues[i] = ItemDatabaseAuthoring.Issues(i, items);
    }

    // ── Which items ──

    bool TableShows(ItemData i) => i != null && (showArchived || !ItemDatabaseAuthoring.IsArchived(i)) &&
        (tableByTier ? i.tier == tableTier : Group(i).title == tableKind);

    List<ItemData> TableRows(List<Column> cols)
    {
        var rows = items.Where(TableShows);
        var col = cols?.FirstOrDefault(c => c.key == tableSort);
        IOrderedEnumerable<ItemData> sorted;
        if (tableSort == "name") sorted = tableSortDown ? rows.OrderByDescending(i => i.itemName) : rows.OrderBy(i => i.itemName);
        else if (col != null) sorted = tableSortDown ? rows.OrderByDescending(col.sort) : rows.OrderBy(col.sort);
        else sorted = tableByTier ? rows.OrderBy(i => Group(i).order) : rows.OrderBy(i => i.tier == 0 ? 99 : i.tier);
        return sorted.ThenBy(i => i.tier).ThenBy(i => i.itemName).ToList();
    }

    void OpenTable()
    {
        tableView = true;
        if (selected != null) { tableKind = Group(selected).title; if (selected.tier > 0) tableTier = selected.tier; tableRow = selected; }
        tableSort = ""; tableScroll = Vector2.zero;
    }

    // ── Drawing ──

    void TableStyles()
    {
        if (cellStyle != null) return;
        cellStyle = new GUIStyle(EditorStyles.textField) { alignment = TextAnchor.MiddleRight, fontSize = 12 };
        cellRead = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 12 };
        cellRead.normal.textColor = new Color(.75f, .75f, .75f);
        headStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 10 };
        headStyle.normal.textColor = new Color(.85f, .74f, .45f);
        nameCell = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, clipping = TextClipping.Clip };
        pickRow = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        pickCount = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        pickCount.normal.textColor = new Color(.55f, .55f, .55f);
    }

    void TableView(Rect body)
    {
        TableStyles(); ListStyles();
        var pick = new Rect(body.x, body.y, PickWidth, body.height);
        var line = new Rect(pick.xMax, body.y, 5f, body.height);
        var grid = new Rect(line.xMax, body.y, body.width - line.xMax, body.height);
        TablePicker(pick);
        EditorGUI.DrawRect(line, new Color(.1f, .1f, .1f));
        TableGrid(grid);
    }

    void TablePicker(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(.17f, .17f, .17f));
        int mode = GUI.Toolbar(new Rect(r.x + 8f, r.y + 6f, r.width - 16f, 20f), tableByTier ? 1 : 0, new[] { "By kind", "By tier" }, EditorStyles.miniButton);
        if ((mode == 1) != tableByTier) { tableByTier = mode == 1; tableSort = ""; tableScroll = Vector2.zero; }
        showArchived = GUI.Toggle(new Rect(r.x + 8f, r.y + 30f, r.width - 16f, 18f), showArchived, " Archived items too", EditorStyles.toggle);

        var groups = new List<(string label, string sub, int count, bool on, Action pick)>();
        var pool = items.Where(i => i != null && (showArchived || !ItemDatabaseAuthoring.IsArchived(i))).ToList();
        if (tableByTier)
        {
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                int tier = t;
                groups.Add(($"Tier {t}", LootFloorTiers.Names[t - 1], pool.Count(i => i.tier == t), tableTier == t, () => tableTier = tier));
            }
            groups.Add(("No tier", "never dropped", pool.Count(i => i.tier == 0), tableTier == 0, () => tableTier = 0));
        }
        else
            foreach (var g in pool.GroupBy(i => Group(i)).OrderBy(g => g.Key.order))
            {
                string title = g.Key.title;
                groups.Add((title, "", g.Count(), tableKind == title, () => tableKind = title));
            }

        var view = new Rect(r.x, r.y + 54f, r.width, r.height - 54f);
        EditorGUI.DrawRect(new Rect(r.x, view.y - 3f, r.width, 1f), new Color(.1f, .1f, .1f));
        tablePickScroll = GUI.BeginScrollView(view, tablePickScroll, new Rect(0, 0, view.width - 14f, groups.Count * 26f));
        for (int k = 0; k < groups.Count; k++)
        {
            var g = groups[k];
            var row = new Rect(0, k * 26f, view.width - (groups.Count * 26f > view.height ? 14f : 0f), 26f);
            if (Event.current.type == EventType.Repaint)
            {
                if (g.on) EditorGUI.DrawRect(row, new Color(.17f, .33f, .48f));
                else if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row, new Color(.25f, .25f, .25f));
            }
            var old = GUI.color; if (g.count == 0) GUI.color = new Color(1f, 1f, 1f, .4f);
            GUI.Label(new Rect(row.x + 12f, row.y, row.width - 60f, row.height), g.sub.Length > 0 ? $"{g.label}   <color=#8a8a8a>{g.sub}</color>" : g.label, RichName(pickRow));
            GUI.Label(new Rect(row.xMax - 50f, row.y, 40f, row.height), g.count.ToString(), pickCount);
            GUI.color = old;
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            { g.pick(); tableSort = ""; tableScroll = Vector2.zero; GUI.FocusControl(null); Event.current.Use(); Repaint(); }
        }
        GUI.EndScrollView();
    }

    void TableGrid(Rect r)
    {
        var first = TableRows(null);
        var cols = TableColumns(first);
        var rows = TableRows(cols);
        float totalWidth = cols.Sum(c => c.width);

        // Title.
        var title = new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 22f);
        string group = tableByTier ? (tableTier == 0 ? "No tier" : $"Tier {tableTier}  {LootFloorTiers.Names[tableTier - 1]}") : tableKind;
        GUI.Label(title, $"<b>{group}</b>   <color=#8a8a8a>{rows.Count} items</color>", RichName(subtitleStyle));
        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 22f), "Click a header to sort  ·  empty a stat to remove it  ·  double-click a name to open it",
            new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });
        float top = r.y + 30f;

        var corner = new Rect(r.x, top, NameWidth, TableHeadHeight);
        var head = new Rect(r.x + NameWidth, top, r.width - NameWidth, TableHeadHeight);
        var names = new Rect(r.x, top + TableHeadHeight, NameWidth, r.yMax - top - TableHeadHeight);
        var cells = new Rect(r.x + NameWidth, top + TableHeadHeight, r.width - NameWidth, r.yMax - top - TableHeadHeight);
        float contentHeight = rows.Count * TableRowHeight;

        // The names scroll up and down with the cells (a wheel over them too).
        if (Event.current.type == EventType.ScrollWheel && names.Contains(Event.current.mousePosition))
        { tableScroll.y = Mathf.Clamp(tableScroll.y + Event.current.delta.y * 20f, 0f, Mathf.Max(0f, contentHeight - cells.height + 16f)); Event.current.Use(); Repaint(); }

        // Cells.
        tableScroll = GUI.BeginScrollView(cells, tableScroll, new Rect(0, 0, Mathf.Max(totalWidth, cells.width - 16f), contentHeight));
        for (int k = 0; k < rows.Count; k++)
        {
            float y = k * TableRowHeight;
            if (y + TableRowHeight < tableScroll.y || y > tableScroll.y + cells.height) continue;
            var item = rows[k];
            var band = new Rect(0, y, Mathf.Max(totalWidth, cells.width), TableRowHeight);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(band, item == tableRow ? new Color(.17f, .33f, .48f, .55f) : k % 2 == 1 ? new Color(.19f, .19f, .19f) : new Color(.215f, .215f, .215f));
            float x = 0f;
            foreach (var c in cols)
            {
                var cell = new Rect(x + 3f, y + 3f, c.width - 6f, TableRowHeight - 6f);
                string now = c.get(item);
                if (now == null) { x += c.width; continue; }   // not a thing this item has (a shield's spread)
                if (c.readOnly) GUI.Label(cell, now, c.text ? new GUIStyle(cellRead) { alignment = TextAnchor.MiddleLeft } : cellRead);
                else
                {
                    string next = EditorGUI.DelayedTextField(cell, now, cellStyle);
                    if (next != now) { c.set(item, next); tableRow = item; }
                }
                x += c.width;
            }
        }
        GUI.EndScrollView();

        // Header, moving sideways with the cells.
        EditorGUI.DrawRect(new Rect(r.x, top, r.width, TableHeadHeight), new Color(.21f, .205f, .195f));
        GUI.BeginClip(head);
        {
            float x = -tableScroll.x;
            foreach (var c in cols)
            {
                var h = new Rect(x, 0, c.width, TableHeadHeight);
                HeaderCell(h, c.key, c.title, c.tip);
                x += c.width;
            }
        }
        GUI.EndClip();
        HeaderCell(corner, "name", "Item", "Sort by name.");
        EditorGUI.DrawRect(new Rect(r.x, top + TableHeadHeight - 1f, r.width, 1f), new Color(.12f, .12f, .12f));

        // Names, moving up and down with the cells.
        EditorGUI.DrawRect(names, new Color(.19f, .19f, .19f));
        GUI.BeginClip(names);
        for (int k = 0; k < rows.Count; k++)
        {
            float y = k * TableRowHeight - tableScroll.y;
            if (y + TableRowHeight < 0f || y > names.height) continue;
            var item = rows[k];
            var row = new Rect(0, y, NameWidth, TableRowHeight);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, item == tableRow ? new Color(.17f, .33f, .48f) : k % 2 == 1 ? new Color(.19f, .19f, .19f) : new Color(.215f, .215f, .215f));
            DrawSlot(new Rect(6f, y + 2f, TableRowHeight - 4f, TableRowHeight - 4f), item, 1f);
            GUI.Label(new Rect(TableRowHeight + 8f, y, NameWidth - TableRowHeight - 12f, TableRowHeight),
                item.itemName + (ItemDatabaseAuthoring.IsArchived(item) ? "  <i>archived</i>" : ""), RichName(NameStyle(nameCell, item)));
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                bool twice = tableRow == item && EditorApplication.timeSinceStartup - lastNameClick < .35;
                tableRow = item; lastNameClick = EditorApplication.timeSinceStartup;
                if (twice || Event.current.clickCount > 1) { tableView = false; ClearFilters(); Select(item); revealSelected = true; }
                GUI.FocusControl(null); Event.current.Use(); Repaint();
            }
        }
        GUI.EndClip();
        EditorGUI.DrawRect(new Rect(names.xMax - 1f, top, 1f, r.yMax - top), new Color(.12f, .12f, .12f));
        if (rows.Count == 0) GUI.Label(new Rect(cells.x, cells.y + 20f, cells.width, 20f), "No items here.", EditorStyles.centeredGreyMiniLabel);
    }

    void HeaderCell(Rect h, string key, string title, string tip)
    {
        bool sorted = tableSort == key;
        if (Event.current.type == EventType.Repaint)
        {
            if (h.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(h, new Color(.26f, .25f, .23f));
            EditorGUI.DrawRect(new Rect(h.xMax - 1f, h.y + 6f, 1f, h.height - 12f), new Color(.14f, .14f, .14f));
        }
        GUI.Label(new Rect(h.x + 2f, h.y, h.width - 4f, h.height), new GUIContent(title + (sorted ? (tableSortDown ? " ▼" : " ▲") : ""), tip), headStyle);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && h.Contains(Event.current.mousePosition))
        {
            if (sorted) tableSortDown = !tableSortDown; else { tableSort = key; tableSortDown = key != "name" && key != "tier" && key != "kind"; }
            GUI.FocusControl(null); Event.current.Use(); Repaint();
        }
    }
}
