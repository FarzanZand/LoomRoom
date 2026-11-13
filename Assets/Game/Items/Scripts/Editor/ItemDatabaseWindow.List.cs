using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Item Database's list (the user, 2026-11-11: "all items on the list to the left is messy"): a search and filters
// over the items grouped by kind, each group a header with its count that folds, its items sorted (by tier unless the
// Sort says otherwise), each row a slot with the icon and its rarity's bar, the name in its rarity colour, then its tier
// and main number at the right. Rows and headers have fixed heights, so the list is drawn by hand and scrolled to the
// picked item when the arrow keys move it.
public partial class ItemDatabaseWindow
{
    [SerializeField] string search = "", tag = "";
    [SerializeField] int show, sort;
    [SerializeField] bool showArchived, issuesOnly;
    [SerializeField] float listWidth = 380f;
    [SerializeField] List<string> collapsed = new();
    Vector2 listScroll;
    bool resizing, revealSelected;
    static GUIStyle listStyles, rowName, rowRight, groupTitle, groupCount, badge;

    const float RowHeight = 30f, HeaderHeight = 24f;
    // The last two filter by the Loot view's level (ItemDatabaseWindow.Loot.cs), not by kind.
    static readonly string[] Kinds = { "All items", "Relics", "Weapons", "Shields", "Armour", "Trinkets", "Spell tomes", "Consumables", "Other", "Drops as loot", "Never drops" };
    const int ShowDrops = 9, ShowNever = 10;
    static readonly string[] Sorts = { "Tier", "Name", "Price", "Main number" };

    // The group an item is listed under: its order, title and Kinds index.
    static (int order, string title, int kind) Group(ItemData i)
    {
        if (i.rarity == ItemRarity.Relic) return (0, "Relics", 1);
        if (i.spell != null) return (20, "Spell tomes", 6);
        if (i.IsScroll) return (21, "Scrolls", 7);
        switch (i.itemType)
        {
            case ItemType.Weapon: return i.weaponCategory == WeaponCategory.Mace ? (2, "Maces", 2) : (1, "Swords", 2);
            case ItemType.Shield: return (3, "Shields", 3);
            case ItemType.Equipment when i.canBeEquipped:
                return i.equipSlot switch
                {
                    EquipmentSlot.Head => (4, "Helms", 4),
                    EquipmentSlot.Body => (5, "Body armour", 4),
                    EquipmentSlot.Gloves => (6, "Gloves", 4),
                    EquipmentSlot.Boots => (7, "Boots", 4),
                    EquipmentSlot.Legs => (8, "Leggings", 4),
                    _ => (9, "Trinkets", 5),
                };
            case ItemType.Consumable: return (22, "Consumables", 7);
            case ItemType.Spoils: return (30, "Spoils", 8);
            case ItemType.Key: return (31, "Keys", 8);
            case ItemType.Tool: return (32, "Tools", 8);
            default: return (33, "Other", 8);
        }
    }

    bool Matches(ItemData i) => i != null &&
        (showArchived || !ItemDatabaseAuthoring.IsArchived(i)) &&
        (!issuesOnly || issues.TryGetValue(i, out var list) && list.Count > 0) &&
        (show == 0 || (show == ShowDrops ? DropsAsLoot(i) : show == ShowNever ? IsLootKind(i) && !DropsAsLoot(i) : Group(i).kind == show)) &&
        (string.IsNullOrEmpty(tag) || i.tag == tag) &&
        (string.IsNullOrWhiteSpace(search) || (i.itemName + " " + i.tag + " " + i.TypeLine() + " " + i.RarityName + " " + AssetDatabase.GetAssetPath(i))
            .IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    IEnumerable<ItemData> Visible() => items.Where(Matches);

    void ClearFilters() { search = ""; tag = ""; show = 0; issuesOnly = false; }

    static float MainNumber(ItemData i) => i.TryMainStat(out _, out float v, out _) ? v : 0f;
    IEnumerable<ItemData> Sorted(IEnumerable<ItemData> group) => sort switch
    {
        1 => group.OrderBy(i => i.itemName),
        2 => group.OrderByDescending(i => i.value).ThenBy(i => i.itemName),
        3 => group.OrderByDescending(MainNumber).ThenBy(i => i.itemName),
        _ => group.OrderBy(i => i.tier == 0 ? 99 : i.tier).ThenBy(i => i.itemName),
    };

    // What the list shows, top to bottom: a header per group, then its rows unless folded.
    struct Entry { public ItemData item; public string group; public int count; public float y; }
    List<Entry> Layout()
    {
        var entries = new List<Entry>();
        float y = 0f;
        foreach (var g in Visible().GroupBy(i => Group(i)).OrderBy(g => g.Key.order))
        {
            entries.Add(new Entry { group = g.Key.title, count = g.Count(), y = y }); y += HeaderHeight;
            if (collapsed.Contains(g.Key.title)) continue;
            foreach (var item in Sorted(g)) { entries.Add(new Entry { item = item, group = g.Key.title, y = y }); y += RowHeight; }
        }
        return entries;
    }

    void ListStyles()
    {
        if (listStyles != null) return;
        listStyles = new GUIStyle();
        rowName = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        rowRight = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
        rowRight.normal.textColor = new Color(.62f, .62f, .62f);
        groupTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        groupTitle.normal.textColor = new Color(.85f, .74f, .45f);
        groupCount = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        groupCount.normal.textColor = new Color(.55f, .55f, .55f);
        badge = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        badge.normal.textColor = new Color(.8f, .8f, .8f);
    }

    // ── The panel ──

    void ListPanel(Rect r)
    {
        ListStyles();
        EditorGUI.DrawRect(r, new Color(.17f, .17f, .17f));
        float y = r.y + 6f;
        const float pad = 8f;
        float w = r.width - pad * 2f;

        // Search.
        GUI.SetNextControlName("ItemSearch");
        search = EditorGUI.TextField(new Rect(r.x + pad, y, w, 20f), search, EditorStyles.toolbarSearchField);
        if (string.IsNullOrEmpty(search) && GUI.GetNameOfFocusedControl() != "ItemSearch")
            GUI.Label(new Rect(r.x + pad + 18f, y + 1f, w, 18f), "Search by name, type, rarity or tag", EditorStyles.centeredGreyMiniLabel.Left());
        y += 26f;

        // Show and Sort.
        float half = (w - 6f) / 2f;
        GUI.Label(new Rect(r.x + pad, y, 40f, 18f), "Show", EditorStyles.miniLabel);
        show = EditorGUI.Popup(new Rect(r.x + pad + 36f, y, half - 36f, 18f), show, Kinds);
        GUI.Label(new Rect(r.x + pad + half + 6f, y, 40f, 18f), "Sort", EditorStyles.miniLabel);
        sort = EditorGUI.Popup(new Rect(r.x + pad + half + 6f + 32f, y, half - 32f, 18f), sort, Sorts);
        y += 24f;

        // Needs attention, archived, tag.
        int attention = items.Count(i => i != null && !ItemDatabaseAuthoring.IsArchived(i) && issues.TryGetValue(i, out var l) && l.Count > 0);
        float third = (w - 8f) / 3f;
        issuesOnly = GUI.Toggle(new Rect(r.x + pad, y, third, 18f), issuesOnly, new GUIContent($"Problems ({attention})", "Only items with something to fix."), EditorStyles.miniButtonLeft);
        showArchived = GUI.Toggle(new Rect(r.x + pad + third, y, third, 18f), showArchived, new GUIContent("Archived", "Show archived items too (dimmed)."), EditorStyles.miniButtonRight);
        var tags = new[] { "Any tag" }.Concat(items.Where(i => i != null).Select(i => i.tag).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t)).ToArray();
        int tagIndex = Mathf.Max(0, Array.IndexOf(tags, tag));
        int nextTag = EditorGUI.Popup(new Rect(r.x + pad + third * 2f + 8f, y, third, 18f), tagIndex, tags);
        tag = nextTag == 0 ? "" : tags[nextTag];
        y += 26f;
        EditorGUI.DrawRect(new Rect(r.x, y - 3f, r.width, 1f), new Color(.1f, .1f, .1f));

        // The list.
        var entries = Layout();
        float footer = 22f;
        var view = new Rect(r.x, y, r.width, r.yMax - y - footer);
        float contentHeight = entries.Count > 0 ? entries[^1].y + (entries[^1].item != null ? RowHeight : HeaderHeight) : 0f;
        Keys(entries, view.height);
        if (revealSelected) { Reveal(entries, view.height); revealSelected = false; }
        listScroll = GUI.BeginScrollView(view, listScroll, new Rect(0, 0, view.width - 14f, contentHeight));
        float rowWidth = contentHeight > view.height ? view.width - 14f : view.width;
        int stripe = 0;
        foreach (var e in entries)
        {
            if (e.y + RowHeight < listScroll.y - 4f || e.y > listScroll.y + view.height + 4f) { if (e.item != null) stripe++; else stripe = 0; continue; }
            if (e.item == null) { GroupHeader(new Rect(0, e.y, rowWidth, HeaderHeight), e); stripe = 0; }
            else Row(new Rect(0, e.y, rowWidth, RowHeight), e.item, stripe++);
        }
        if (entries.Count == 0) GUI.Label(new Rect(0, 20f, view.width, 20f), "No item matches.", EditorStyles.centeredGreyMiniLabel);
        GUI.EndScrollView();

        // Footer.
        int shown = entries.Count(e => e.item != null) + entries.Where(e => e.item == null && collapsed.Contains(e.group)).Sum(e => e.count);
        var foot = new Rect(r.x, r.yMax - footer, r.width, footer);
        EditorGUI.DrawRect(new Rect(foot.x, foot.y, foot.width, 1f), new Color(.1f, .1f, .1f));
        GUI.Label(new Rect(foot.x + pad, foot.y + 2f, foot.width - pad * 2f, 18f), shown == items.Count ? $"{items.Count} items" : $"{shown} of {items.Count} items", EditorStyles.miniLabel);
        if (shown != items.Count && GUI.Button(new Rect(foot.xMax - pad - 80f, foot.y + 2f, 80f, 17f), "Clear filters", EditorStyles.miniButton)) { ClearFilters(); showArchived = false; GUI.FocusControl(null); }
    }

    void GroupHeader(Rect r, Entry e)
    {
        bool folded = collapsed.Contains(e.group);
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, new Color(.21f, .205f, .195f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(.12f, .12f, .12f));
            EditorStyles.foldout.Draw(new Rect(r.x + 6f, r.y + 4f, 14f, 16f), false, false, !folded, false);
        }
        GUI.Label(new Rect(r.x + 22f, r.y, r.width - 80f, r.height), e.group.ToUpperInvariant(), groupTitle);
        GUI.Label(new Rect(r.xMax - 60f, r.y, 50f, r.height), e.count.ToString(), groupCount);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
        {
            if (folded) collapsed.Remove(e.group); else collapsed.Add(e.group);
            Event.current.Use(); Repaint();
        }
    }

    void Row(Rect r, ItemData item, int stripe)
    {
        bool picked = item == selected, hover = r.Contains(Event.current.mousePosition);
        bool archived = ItemDatabaseAuthoring.IsArchived(item);
        bool problem = issues.TryGetValue(item, out var list) && list.Count > 0;
        if (Event.current.type == EventType.Repaint)
        {
            if (picked) EditorGUI.DrawRect(r, new Color(.17f, .33f, .48f));
            else if (hover) EditorGUI.DrawRect(r, new Color(.25f, .25f, .25f));
            else if (stripe % 2 == 1) EditorGUI.DrawRect(r, new Color(.19f, .19f, .19f));
        }
        DrawSlot(new Rect(r.x + 10f, r.y + 2f, RowHeight - 4f, RowHeight - 4f), item, 2f);

        // Right side: main number, then tier.
        float right = r.xMax - 8f;
        if (item.tier > 0)
        {
            var b = new Rect(right - 26f, r.y + 7f, 26f, 16f);
            if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(b, new Color(.12f, .12f, .12f, .8f)); }
            GUI.Label(b, "T" + item.tier, badge);
            right -= 32f;
        }
        string main = MainText(item);
        if (main.Length > 0)
        {
            float mw = rowRight.CalcSize(new GUIContent(main)).x;
            GUI.Label(new Rect(right - mw, r.y, mw, r.height), main, rowRight);
            right -= mw + 8f;
        }
        var nameRect = new Rect(r.x + RowHeight + 14f, r.y, right - (r.x + RowHeight + 14f), r.height);
        var style = NameStyle(rowName, item);
        var old = GUI.color;
        if (archived) GUI.color = new Color(1f, 1f, 1f, .45f);
        GUI.Label(nameRect, (problem ? "<color=#e8a33a>●</color> " : "") + item.itemName + (archived ? "  <i>archived</i>" : ""), RichName(style));
        GUI.color = old;

        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            Select(item); GUI.FocusControl(null);
            if (Event.current.button == 1) RowMenu(item);
            Event.current.Use(); Repaint();
        }
    }

    static readonly Dictionary<GUIStyle, GUIStyle> rich = new();
    static GUIStyle RichName(GUIStyle s) { if (!rich.TryGetValue(s, out var r)) { r = new GUIStyle(s) { richText = true }; rich[s] = r; } return r; }

    // The number that matters most for its kind: a weapon's hits, a shield's block, armour's armour.
    static string MainText(ItemData item)
    {
        if (!item.TryMainStat(out string label, out _, out string text)) return "";
        return item.IsWeapon ? text + " dmg" : label == "Block" ? text + " block" : text + " armour";
    }

    void RowMenu(ItemData item)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Duplicate"), false, () => { Select(ItemDatabaseAuthoring.Duplicate(item)); ItemDatabaseAuthoring.SyncCatalog(); Rebuild(); revealSelected = true; });
        menu.AddItem(new GUIContent("Show in Project"), false, () => { Selection.activeObject = item; EditorGUIUtility.PingObject(item); });
        menu.AddItem(new GUIContent("Make icon"), false, () => GenerateIcons(new List<ItemData> { item }));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Delete..."), false, () => Delete(item));
        menu.ShowAsContext();
    }

    // Up and down move the pick through the shown rows (when no field has the keys), Home and End to the ends.
    void Keys(List<Entry> entries, float viewHeight)
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
        var rows = entries.Where(x => x.item != null).Select(x => x.item).ToList();
        if (rows.Count == 0) return;
        int at = rows.IndexOf(selected);
        int next = e.keyCode switch
        {
            KeyCode.DownArrow => at < 0 ? 0 : Mathf.Min(rows.Count - 1, at + 1),
            KeyCode.UpArrow => at < 0 ? 0 : Mathf.Max(0, at - 1),
            KeyCode.PageDown => Mathf.Min(rows.Count - 1, Mathf.Max(0, at) + Mathf.FloorToInt(viewHeight / RowHeight) - 1),
            KeyCode.PageUp => Mathf.Max(0, Mathf.Max(0, at) - Mathf.FloorToInt(viewHeight / RowHeight) + 1),
            KeyCode.Home => 0,
            KeyCode.End => rows.Count - 1,
            _ => -1,
        };
        if (next < 0) return;
        Select(rows[next]); e.Use();
        Reveal(entries, viewHeight); Repaint();
    }

    // Scroll so the picked row is in view (opening its group if it is folded).
    void Reveal(List<Entry> entries, float viewHeight)
    {
        if (selected == null) return;
        var g = Group(selected).title;
        if (collapsed.Remove(g)) entries = Layout();
        foreach (var x in entries)
        {
            if (x.item != selected) continue;
            if (x.y < listScroll.y) listScroll.y = Mathf.Max(0f, x.y - HeaderHeight);
            else if (x.y + RowHeight > listScroll.y + viewHeight) listScroll.y = x.y + RowHeight - viewHeight + 4f;
            return;
        }
    }

    // The list's right edge drags to make it wider or narrower.
    void Splitter(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(.1f, .1f, .1f));
        EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
        var e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { resizing = true; e.Use(); }
        if (resizing && e.type == EventType.MouseDrag) { listWidth = e.mousePosition.x - 2f; e.Use(); Repaint(); }
        if (resizing && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)) resizing = false;
    }
}

static class ItemDatabaseStyleExtensions
{
    static readonly Dictionary<GUIStyle, GUIStyle> left = new();
    public static GUIStyle Left(this GUIStyle s)
    {
        if (!left.TryGetValue(s, out var l)) { l = new GUIStyle(s) { alignment = TextAnchor.MiddleLeft }; left[s] = l; }
        return l;
    }
}
