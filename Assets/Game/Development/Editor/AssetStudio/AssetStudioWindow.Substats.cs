using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Substats view: the properties rolled gear can get (Uncommon and up; ItemPropertyTable,
// Items/Data/Item Properties), by item type. Left: the item types, with how many substats each can roll. Middle: one
// line per substat to scan and compare (on or off, id, stat, the tooltip line at the preview floor, the types it rolls on,
// its weight and share of the type's rolls). Right: the picked substat to edit, with plain labels (id, stat, shown as a
// percent or a flat number, the types it rolls on, its weight, its value on each floor of a run with a chart and the
// growth after), its odds on each rarity of a drop of that type (the game's own ItemPropertyTable.Roll), what keeps it
// from rolling, and test drops. Every edit goes through Undo and is saved to the table once you stop typing or dragging.
public partial class AssetStudioWindow
{
    const string SubstatTablePath = "Assets/Game/Items/Data/Item Properties.asset";
    static readonly (GearSlots slot, string name)[] SubstatTypes =
    {
        (GearSlots.Weapon, "Weapons"), (GearSlots.Shield, "Shields"), (GearSlots.Body, "Body armour"), (GearSlots.Helm, "Helms"),
        (GearSlots.Gloves, "Gloves"), (GearSlots.Boots, "Boots"), (GearSlots.Legs, "Leggings"), (GearSlots.Ring, "Rings"), (GearSlots.Amulet, "Amulets"),
    };
    static readonly ItemRarity[] SubstatRarities = { ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Unique, ItemRarity.Legendary };
    static readonly string[] SubstatRarityNames = { "Uncommon", "Rare", "Unique", "Legendary" };
    static readonly string[] SubstatSorts = { "Table order", "Weight", "Stat", "Id" };
    static readonly string[] SubstatUnits = { "Percent (7 = +7%)", "Flat number (7 = +7)", "Other scale" };
    const float SubstatRow = 30, SubstatTypesWidth = 200, SubstatDetailWidth = 400;
    const string SubGold = "#e8c46a", SubGrey = "#a9a49a", SubRed = "#ff8c80";

    [System.NonSerialized] ItemPropertyTable substatTable;
    [System.NonSerialized] List<ItemData> substatGear;
    [System.NonSerialized] Dictionary<string, float[]> substatOdds;   // id -> chance on each rarity, for substatOddsKey
    [System.NonSerialized] int substatOddsKey = -1;
    [System.NonSerialized] string substatSignature;
    [System.NonSerialized] List<string> substatRolls = new();
    [System.NonSerialized] float substatDetailHeight = 1200;
    int substatType;            // 0 = every type, else SubstatTypes[i - 1]
    int substatPicked = -1;     // index in the table's list
    int substatSort;
    string substatSearch = "";
    bool substatHideOff;
    int substatFloor = 1;
    int substatRarity = 1;      // SubstatRarities index
    int substatSeed = 1;
    bool substatDirty;
    Vector2 substatScroll, substatDetailScroll;

    bool showingSubstats => studioView == StudioView.Substats;
    GearSlots SubstatSlot => substatType > 0 ? SubstatTypes[substatType - 1].slot : GearSlots.None;
    int SubstatCount => LoadSubstatTable() != null && substatTable.properties != null ? substatTable.properties.Count : 0;

    void OpenSubstats()
    {
        studioView = StudioView.Substats;
        LoadSubstatTable();
        substatGear = null;
        GUI.FocusControl(null);
    }

    ItemPropertyTable LoadSubstatTable()
    {
        if (substatTable != null) return substatTable;
        substatTable = AssetDatabase.LoadAssetAtPath<ItemPropertyTable>(SubstatTablePath);
        if (substatTable == null && InventoryManager.HasInstance) substatTable = InventoryManager.Instance.itemProperties;
        if (substatTable == null)
        {
            var guid = AssetDatabase.FindAssets("t:ItemPropertyTable").FirstOrDefault();
            if (guid != null) substatTable = AssetDatabase.LoadAssetAtPath<ItemPropertyTable>(AssetDatabase.GUIDToAssetPath(guid));
        }
        return substatTable;
    }

    // Every piece of Common gear in the game's item folders (not _Archive), for the per-type notes and the test drops.
    List<ItemData> SubstatGear()
    {
        if (substatGear != null) return substatGear;
        substatGear = new List<ItemData>();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game/Items/Data" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Archive/")) continue;
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null && item.IsGear && item.rarity == ItemRarity.Common && ItemPropertyTable.SlotOf(item) != GearSlots.None) substatGear.Add(item);
        }
        return substatGear;
    }

    void SubstatEdit(string what, System.Action apply)
    {
        Undo.RecordObject(substatTable, what);
        apply();
        EditorUtility.SetDirty(substatTable);
        substatDirty = true;
    }

    static bool SubstatRolls(ItemPropertyDef p, GearSlots slot) => p != null && (slot == GearSlots.None || (p.slots & slot) != 0);
    static bool SubstatInPool(ItemPropertyDef p, GearSlots slot) => p != null && p.enabled && p.weight > 0 && !string.IsNullOrEmpty(p.id) && (p.slots & slot) != 0;
    static string SubstatLine(ItemPropertyDef p, int floor) =>
        p == null ? "" : ItemData.StatLine(p.stat, ModifierType.Flat, p.ValueAt(Mathf.Max(0, floor - 1)) * p.statPerPoint);
    static string SubstatTypesText(ItemPropertyDef p)
    {
        var names = SubstatTypes.Where(t => (p.slots & t.slot) != 0).Select(t => t.name.ToLowerInvariant()).ToArray();
        return names.Length == 0 ? "no item type" : names.Length == SubstatTypes.Length ? "every item type" : string.Join(", ", names);
    }
    // The stat's name as people read it: the game's own label where it has one ("Armour"), else the enum's words spaced.
    static string SubstatStatName(StatType stat)
    {
        string label = StatModifierEntry.Label(stat);
        return label == stat.ToString() ? ObjectNames.NicifyVariableName(label) : label;
    }
    static int SubstatUnit(ItemPropertyDef p) => Mathf.Approximately(p.statPerPoint, .01f) ? 0 : Mathf.Approximately(p.statPerPoint, 1f) ? 1 : 2;

    // Wrapped text at (x, y), moving y past it: nothing drawn after it can overlap it.
    static void SubstatText(float x, ref float y, float w, string text, GUIStyle style, float gap = 4)
    {
        float h = style.CalcHeight(new GUIContent(text), w);
        GUI.Label(new Rect(x, y, w, h), text, style);
        y += h + gap;
    }
    void SubstatHeading(float x, ref float y, float w, string text)
    {
        y += 6; StudioSkin.Divider(new Rect(x, y, w, 1)); y += 8;
        GUI.Label(new Rect(x, y, w, 18), text, gold); y += 22;
    }

    void DrawSubstats(Rect r)
    {
        if (LoadSubstatTable() == null)
        {
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 14, r.y + 14, r.width - 28, 40), "No Item Property Table found (Items/Data/Item Properties).", StudioSkin.Label);
            return;
        }
        var props = substatTable.properties ??= new List<ItemPropertyDef>();
        // Saved once nothing is being typed or dragged; the odds are rolled again whenever the table changed (an undo too).
        if (substatDirty && GUIUtility.hotControl == 0 && !EditorGUIUtility.editingTextField) { AssetDatabase.SaveAssetIfDirty(substatTable); substatDirty = false; }
        string signature = EditorJsonUtility.ToJson(substatTable);
        if (signature != substatSignature) { substatSignature = signature; substatOdds = null; }
        if (substatPicked >= props.Count) substatPicked = props.Count - 1;

        var types = new Rect(r.x, r.y, SubstatTypesWidth, r.height);
        var detail = new Rect(r.xMax - SubstatDetailWidth, r.y, SubstatDetailWidth, r.height);
        var list = new Rect(types.xMax + 8, r.y, detail.xMin - types.xMax - 16, r.height);
        DrawSubstatTypes(types, props);
        DrawSubstatList(list, props);
        DrawSubstatDetail(detail, props);
    }

    void DrawSubstatTypes(Rect r, List<ItemPropertyDef> props)
    {
        GUI.Box(r, GUIContent.none);
        float x = r.x + 10, w = r.width - 20, y = r.y + 8;
        GUI.Label(new Rect(x, y, w, 20), "ITEM TYPE", gold); y += 24;
        for (int i = 0; i <= SubstatTypes.Length; i++)
        {
            var slot = i == 0 ? GearSlots.None : SubstatTypes[i - 1].slot;
            string name = i == 0 ? "Every type" : SubstatTypes[i - 1].name;
            // the same substats the list shows: those that roll there, and how many of them are off
            int all = props.Count(p => SubstatRolls(p, slot)), off = props.Count(p => SubstatRolls(p, slot) && !SubstatInPool(p, slot == GearSlots.None ? p.slots : slot));
            string count = off > 0 ? $"<color={SubGrey}>{all} · {off} off</color>" : $"<color={SubGrey}>{all}</color>";
            bool on = substatType == i;
            if (GUI.Toggle(new Rect(r.x + 4, y, r.width - 8, 28), on, $"  {name}   {count}", on ? cellSelected : cell) && !on)
            { substatType = i; substatScroll = Vector2.zero; substatRolls.Clear(); GUI.FocusControl(null); }
            y += 30;
        }
        // What each rarity rolls (InventoryManager > Rarity), and the table's one setting of its own.
        SubstatHeading(x, ref y, w, "SUBSTATS PER RARITY");
        for (int k = 0; k < SubstatRarities.Length; k++)
        {
            var step = InventoryManager.HasInstance ? InventoryManager.StepFor(SubstatRarities[k]) : null;
            string n = step == null ? "?" : step.properties.x == step.properties.y ? $"{step.properties.x}" : $"{step.properties.x}-{step.properties.y}";
            GUI.Label(new Rect(x + 2, y, w, 18), $"{SubstatRarityNames[k]}   <color={SubGold}>{n}</color>", StudioSkin.Label); y += 20;
        }
        y += 4;
        SubstatText(x + 2, ref y, w - 2, "A Unique's first substat rolls a floor deeper, a Legendary's first two.", StudioSkin.Faint, 10);
        GUI.Label(new Rect(x + 2, y, w, 18), "Rolls a floor lower", StudioSkin.Label); y += 20;
        float lower = EditorGUI.Slider(new Rect(x + 2, y, w - 2, 18), substatTable.lowerChance, 0f, 1f);
        if (!Mathf.Approximately(lower, substatTable.lowerChance)) SubstatEdit("Substat lower chance", () => substatTable.lowerChance = lower);
        y += 22;
        SubstatText(x + 2, ref y, w - 2, "The chance each substat rolls as if the item dropped one floor higher up.", StudioSkin.Faint, 10);
        if (GUI.Button(new Rect(x, y, w, 24), new GUIContent("Select the table", "Ping and select Items/Data/Item Properties."))) { Selection.activeObject = substatTable; EditorGUIUtility.PingObject(substatTable); }
    }

    List<int> SubstatShown(List<ItemPropertyDef> props)
    {
        var slot = SubstatSlot;
        var words = substatSearch.ToLowerInvariant().Split(' ').Where(w => w.Length > 0).ToArray();
        IEnumerable<int> q = Enumerable.Range(0, props.Count).Where(i => SubstatRolls(props[i], slot) && (!substatHideOff || props[i].enabled));
        if (words.Length > 0)
            q = q.Where(i => { var p = props[i]; string s = $"{p.id} {p.stat} {SubstatStatName(p.stat)} {SubstatLine(p, substatFloor)} {SubstatTypesText(p)}".ToLowerInvariant(); return words.All(w => s.Contains(w)); });
        return substatSort switch
        {
            1 => q.OrderByDescending(i => props[i].weight).ToList(),
            2 => q.OrderBy(i => SubstatStatName(props[i].stat)).ThenBy(i => props[i].id).ToList(),
            3 => q.OrderBy(i => props[i].id ?? "", System.StringComparer.OrdinalIgnoreCase).ToList(),
            _ => q.ToList(),
        };
    }

    void DrawSubstatList(Rect r, List<ItemPropertyDef> props)
    {
        GUI.Box(r, GUIContent.none);
        var slot = SubstatSlot;
        // ── toolbar ──
        float x = r.x + 8, y = r.y + 8;
        string s = EditorGUI.TextField(new Rect(x, y, 190, 20), substatSearch, EditorStyles.toolbarSearchField);
        if (s != substatSearch) { substatSearch = s; substatScroll = Vector2.zero; }
        x += 198;
        GUI.Label(new Rect(x, y, 32, 20), "Sort", dim);
        substatSort = EditorGUI.Popup(new Rect(x + 32, y, 104, 20), substatSort, SubstatSorts);
        x += 146;
        substatHideOff = EditorGUI.ToggleLeft(new Rect(x, y, 84, 20), "Hide off", substatHideOff);
        x += 90;
        GUI.Label(new Rect(x, y, 90, 20), new GUIContent("Preview floor", "The floor the item drops on: the lines, odds and test drops are for this floor."), dim);
        substatFloor = EditorGUI.IntSlider(new Rect(x + 92, y, Mathf.Max(100, r.xMax - x - 232), 20), substatFloor, 1, 12);
        if (GUI.Button(new Rect(r.xMax - 128, y - 1, 120, 22), new GUIContent("+ New substat", slot == GearSlots.None ? "A new substat (it rolls on weapons until you pick its types)." : $"A new substat that rolls on {SubstatTypes[substatType - 1].name.ToLowerInvariant()}.")))
            AddSubstat(props, slot);

        var shown = SubstatShown(props);
        float pool = slot == GearSlots.None ? 0 : props.Where(p => SubstatInPool(p, slot)).Sum(p => p.weight);
        string what = slot == GearSlots.None ? "every substat" : $"what {SubstatTypes[substatType - 1].name.ToLowerInvariant()} can roll";
        GUI.Label(new Rect(r.x + 8, y + 26, r.width - 16, 18), $"{shown.Count} of {props.Count} · {what} · as the tooltip shows them on floor {substatFloor} · pick one to edit it on the right", dim);

        // ── column heads ──
        float listW = r.width - 28;
        var cols = SubstatColumns(listW);
        float hy = y + 48;
        GUI.Label(new Rect(r.x + 8 + cols.id, hy, 100, 16), "ID", dim);
        GUI.Label(new Rect(r.x + 8 + cols.stat, hy, 100, 16), "STAT", dim);
        GUI.Label(new Rect(r.x + 8 + cols.line, hy, 200, 16), $"ON FLOOR {substatFloor}", dim);
        GUI.Label(new Rect(r.x + 8 + cols.types, hy, 120, 16), "ROLLS ON", dim);
        GUI.Label(new Rect(r.x + 8 + cols.weight, hy, 80, 16), new GUIContent("WEIGHT", "How often it is picked against the others an item can roll."), dim);
        if (slot != GearSlots.None) GUI.Label(new Rect(r.x + 8 + cols.share, hy, 120, 16), new GUIContent("SHARE", "Its share of the rolls on this item type (its weight over all the type's weights)."), dim);

        // ── rows ──
        var view = new Rect(r.x + 4, hy + 20, r.width - 8, r.yMax - hy - 24);
        substatScroll = GUI.BeginScrollView(view, substatScroll, new Rect(0, 0, view.width - 16, shown.Count * SubstatRow + 8));
        var ids = props.Where(p => p != null).GroupBy(p => p.id ?? "").Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (int n = 0; n < shown.Count; n++)
        {
            int i = shown[n];
            var p = props[i];
            float ry = 2 + n * SubstatRow;
            if (ry + SubstatRow < substatScroll.y - 10 || ry > substatScroll.y + view.height + 10) continue;
            var row = new Rect(4, ry, listW, SubstatRow - 2);
            bool picked = i == substatPicked;
            EditorGUI.DrawRect(row, picked ? new Color(.3f, .25f, .12f, .6f) : new Color(1, 1, 1, n % 2 == 0 ? .02f : .045f));
            if (picked) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), new Color(.91f, .77f, .42f));
            // the row picks its substat; its own controls (on, weight) still work
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition) && !picked)
            { substatPicked = i; substatRolls.Clear(); GUI.FocusControl(null); Repaint(); }
            DrawSubstatRow(row, p, slot, pool, cols, ids.Contains(p.id ?? ""));
        }
        GUI.EndScrollView();
        if (shown.Count == 0) GUI.Label(new Rect(view.x + 12, view.y + 12, view.width - 24, 40), "Nothing here. Make a new substat, or pick Every type.", dim);
    }

    // Where each column starts in a row of width w.
    (float id, float stat, float line, float types, float weight, float share) SubstatColumns(float w)
    {
        float share = w - 92, weight = share - 58, types = weight - Mathf.Clamp(w * .2f, 110, 190);
        float line = 30 + 110 + Mathf.Clamp(w * .18f, 140, 170);
        return (30, 30 + 110, line, Mathf.Max(line + 120, types), weight, share);
    }

    void DrawSubstatRow(Rect row, ItemPropertyDef p, GearSlots slot, float pool, (float id, float stat, float line, float types, float weight, float share) c, bool duplicateId)
    {
        float y = row.y + (row.height - 18) / 2;
        bool on = EditorGUI.Toggle(new Rect(row.x + 8, y, 18, 18), new GUIContent("", "On: it can roll. Off: it doesn't (items that have it keep it)."), p.enabled);
        if (on != p.enabled) SubstatEdit("Substat on", () => p.enabled = on);
        string dimIf(string t) => p.enabled ? t : $"<color=#77736b>{t}</color>";
        string id = string.IsNullOrEmpty(p.id) ? $"<color={SubRed}>no id</color>" : duplicateId ? $"<color={SubRed}>{p.id}</color>" : dimIf(p.id);
        GUI.Label(new Rect(row.x + c.id, y, c.stat - c.id - 6, 18), id, StudioSkin.Strong);
        GUI.Label(new Rect(row.x + c.stat, y, c.line - c.stat - 6, 18), dimIf(SubstatStatName(p.stat)), StudioSkin.Row);
        GUI.Label(new Rect(row.x + c.line, y, c.types - c.line - 8, 18), $"<color={(p.enabled ? SubGold : "#7d786e")}>{SubstatLine(p, substatFloor)}</color>", StudioSkin.Strong);
        GUI.Label(new Rect(row.x + c.types, y, c.weight - c.types - 8, 18), new GUIContent($"<color={SubGrey}>{SubstatTypesText(p)}</color>", SubstatTypesText(p)), StudioSkin.Row);
        float weight = EditorGUI.FloatField(new Rect(row.x + c.weight, y, 46, 18), p.weight);
        if (!Mathf.Approximately(weight, p.weight)) SubstatEdit("Substat weight", () => p.weight = Mathf.Max(0, weight));
        if (slot != GearSlots.None)
        {
            float share = SubstatInPool(p, slot) && pool > 0 ? p.weight / pool : 0;
            var bar = new Rect(row.x + c.share, y + 5, 50, 8);
            EditorGUI.DrawRect(bar, new Color(1, 1, 1, .08f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * share, bar.height), new Color(.91f, .77f, .42f, .85f));
            GUI.Label(new Rect(bar.xMax + 6, y, 40, 18), $"{share * 100:0}%", dim);
        }
    }

    void AddSubstat(List<ItemPropertyDef> props, GearSlots slot)
    {
        string id = "new_substat";
        for (int k = 2; props.Any(p => p != null && p.id == id); k++) id = "new_substat_" + k;
        var def = new ItemPropertyDef { id = id, enabled = true, stat = StatType.MaxHealth, statPerPoint = 1, slots = slot == GearSlots.None ? GearSlots.Weapon : slot, weight = 1, byFloor = new float[] { 1, 2, 3, 4 } };
        int at = substatPicked >= 0 && substatPicked < props.Count ? substatPicked + 1 : props.Count;
        SubstatEdit("New substat", () => props.Insert(at, def));
        substatPicked = at;
        substatHideOff = false; substatSearch = "";
        Toast($"Made {id}: give it an id, a stat and its values on the right.");
    }

    void DrawSubstatDetail(Rect r, List<ItemPropertyDef> props)
    {
        GUI.Box(r, GUIContent.none);
        var p = substatPicked >= 0 && substatPicked < props.Count ? props[substatPicked] : null;
        var view = new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8);
        substatDetailScroll = GUI.BeginScrollView(view, substatDetailScroll, new Rect(0, 0, view.width - 16, substatDetailHeight));
        float w = view.width - 36, x = 10, y = 8, lw = 96;   // lw: the labels' column
        if (p == null)
        {
            SubstatText(x, ref y, w, "Pick a substat in the list to edit it here: its stat, the item types it rolls on, its weight and its value on each floor, with its odds and test drops.", StudioSkin.Label);
            GUI.EndScrollView();
            return;
        }
        // ── what it is ──
        GUI.Label(new Rect(x, y, w, 22), $"<color={SubGold}>{SubstatLine(p, substatFloor)}</color>", StudioSkin.Strong); y += 22;
        SubstatText(x, ref y, w, $"on an item from floor {substatFloor}" + (p.enabled ? "" : $"  <color={SubRed}>(off: it doesn't roll)</color>"), StudioSkin.Faint, 8);

        GUI.Label(new Rect(x, y, lw, 18), "On", StudioSkin.Label);
        bool on = EditorGUI.Toggle(new Rect(x + lw, y, 18, 18), p.enabled);
        if (on != p.enabled) SubstatEdit("Substat on", () => p.enabled = on);
        y += 24;
        GUI.Label(new Rect(x, y, lw, 18), new GUIContent("Id", "Saved in item ids (iron_sword~3:crit=7): never change one after release."), StudioSkin.Label);
        string id = EditorGUI.DelayedTextField(new Rect(x + lw, y, w - lw, 18), p.id ?? "");
        if (id != (p.id ?? "")) SubstatEdit("Substat id", () => p.id = id.Trim());
        y += 20;
        SubstatText(x + lw, ref y, w - lw, "Saved in items: don't rename after release.", StudioSkin.Faint, 6);
        GUI.Label(new Rect(x, y, lw, 18), "Stat", StudioSkin.Label);
        var stat = (StatType)EditorGUI.EnumPopup(new Rect(x + lw, y, w - lw, 18), p.stat);
        if (stat != p.stat) SubstatEdit("Substat stat", () => p.stat = stat);
        y += 24;
        GUI.Label(new Rect(x, y, lw, 18), new GUIContent("Shown as", "A percent stat stores 7 as 0.07 (critical chance, backstab damage); a flat one as 7 (health, kills heal)."), StudioSkin.Label);
        int unit = SubstatUnit(p);
        int newUnit = EditorGUI.Popup(new Rect(x + lw, y, unit == 2 ? w - lw - 60 : w - lw, 18), unit, SubstatUnits);
        if (newUnit != unit && newUnit < 2) SubstatEdit("Substat unit", () => p.statPerPoint = newUnit == 0 ? .01f : 1f);
        if (unit == 2)
        {
            float per = EditorGUI.FloatField(new Rect(x + w - 54, y, 54, 18), new GUIContent("", "Stat value per rolled point."), p.statPerPoint);
            if (!Mathf.Approximately(per, p.statPerPoint)) SubstatEdit("Substat scale", () => p.statPerPoint = per);
        }
        y += 24;

        // ── where it rolls, and how often ──
        SubstatHeading(x, ref y, w, "ROLLS ON");
        float tw = (w - 8) / 3f;
        for (int t = 0; t < SubstatTypes.Length; t++)
        {
            var (slot, name) = SubstatTypes[t];
            bool has = (p.slots & slot) != 0;
            var cellRect = new Rect(x + (t % 3) * (tw + 4), y + (t / 3) * 26, tw, 24);
            bool now = GUI.Toggle(cellRect, has, new GUIContent(name, has ? "Rolls on " + name.ToLowerInvariant() + ": click to stop" : "Click to let it roll on " + name.ToLowerInvariant()), has ? cellSelected : cell);
            if (now != has) SubstatEdit("Substat types", () => p.slots = now ? p.slots | slot : p.slots & ~slot);
        }
        y += 3 * 26 + 6;
        GUI.Label(new Rect(x, y, lw, 18), new GUIContent("Weight", "How often it is picked against the others an item can roll (1 is usual; 0 never)."), StudioSkin.Label);
        float weight = EditorGUI.FloatField(new Rect(x + lw, y, 60, 18), p.weight);
        if (!Mathf.Approximately(weight, p.weight)) SubstatEdit("Substat weight", () => p.weight = Mathf.Max(0, weight));
        y += 24;

        int oddsType = substatType > 0 ? substatType : System.Array.FindIndex(SubstatTypes, t => (p.slots & t.slot) != 0) + 1;
        if (oddsType > 0)
        {
            var odds = SubstatOdds(oddsType);
            SubstatText(x, ref y, w, $"How often a drop of {SubstatTypes[oddsType - 1].name.ToLowerInvariant()} on floor {substatFloor} has it:", StudioSkin.Faint, 4);
            if (odds == null) SubstatText(x, ref y, w, "(open the Room scene: the odds need its Inventory Manager)", StudioSkin.Faint);
            else
            {
                odds.TryGetValue(p.id ?? "", out var mine);
                for (int k = 0; k < SubstatRarities.Length; k++)
                {
                    float chance = mine != null ? mine[k] : 0;
                    GUI.Label(new Rect(x, y, lw, 18), SubstatRarityNames[k], StudioSkin.Label);
                    var bar = new Rect(x + lw, y + 5, w - lw - 46, 9);
                    EditorGUI.DrawRect(bar, new Color(1, 1, 1, .08f));
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * chance, bar.height), new Color(.42f, .83f, .42f, .85f));
                    GUI.Label(new Rect(bar.xMax + 6, y, 40, 18), $"{chance * 100:0}%", dim);
                    y += 20;
                }
            }
        }

        // ── its value on each floor ──
        SubstatHeading(x, ref y, w, "VALUE ON EACH FLOOR");
        SubstatText(x, ref y, w, "What it rolls on an item from that floor of a run. Past the last box it grows by the amount below.", StudioSkin.Faint, 8);
        const int Floors = 12;
        float top = Enumerable.Range(0, Floors).Max(f => Mathf.Abs(p.ValueAt(f)));
        float colW = w / Floors, chartH = 64;
        var small = new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter, fontSize = 10, clipping = TextClipping.Overflow };
        for (int f = 0; f < Floors; f++)
        {
            float v = p.ValueAt(f);
            float h = top > 0 ? chartH * Mathf.Abs(v) / top : 0;
            bool given = p.byFloor != null && f < p.byFloor.Length;
            var colour = f + 1 == substatFloor ? new Color(.91f, .77f, .42f) : given ? new Color(.66f, .62f, .54f) : new Color(.4f, .38f, .35f);
            EditorGUI.DrawRect(new Rect(x + f * colW + 2, y + 14 + chartH - h, colW - 4, h), colour);
            GUI.Label(new Rect(x + f * colW, y, colW, 14), $"{v:0.#}", small);
            GUI.Label(new Rect(x + f * colW, y + chartH + 16, colW, 14), $"{f + 1}", small);
        }
        y += chartH + 34;
        SubstatText(x, ref y, w, $"Light: set below. Dark: grown after them. <color={SubGold}>Gold</color>: the preview floor. Numbers under the bars are floors.", StudioSkin.Faint, 8);
        var floors = p.byFloor ?? new float[0];
        float fw = Mathf.Min(54, (w - 60) / Mathf.Max(4, floors.Length));
        for (int f = 0; f < floors.Length; f++)
        {
            float fx = x + f * (fw + 4);
            GUI.Label(new Rect(fx, y, fw, 14), $"Floor {f + 1}", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            float v = EditorGUI.FloatField(new Rect(fx, y + 16, fw, 18), floors[f]);
            int fi = f;
            if (!Mathf.Approximately(v, floors[f])) SubstatEdit("Substat value", () => p.byFloor[fi] = v);
        }
        float px = x + floors.Length * (fw + 4);
        if (GUI.Button(new Rect(px, y + 16, 24, 18), new GUIContent("+", "Set one more floor's value (starts at what it grew to).")))
            SubstatEdit("Substat floor added", () => { var l = (p.byFloor ?? new float[0]).ToList(); l.Add(l.Count > 0 ? p.ValueAt(l.Count) : 1); p.byFloor = l.ToArray(); });
        if (floors.Length > 1 && GUI.Button(new Rect(px + 28, y + 16, 24, 18), new GUIContent("−", "Stop setting the last floor's value (growth takes over a floor earlier).")))
            SubstatEdit("Substat floor removed", () => p.byFloor = p.byFloor.Take(p.byFloor.Length - 1).ToArray());
        y += 42;
        GUI.Label(new Rect(x, y, 170, 18), new GUIContent($"Then, each floor past {floors.Length}: +", "Added for each floor past the last value, for longer dungeons."), StudioSkin.Label);
        float growth = EditorGUI.FloatField(new Rect(x + 172, y, 54, 18), p.growthPerFloor);
        if (!Mathf.Approximately(growth, p.growthPerFloor)) SubstatEdit("Substat growth", () => p.growthPerFloor = growth);
        y += 26;

        // ── what keeps it from rolling ──
        var notes = SubstatNotes(p, props);
        SubstatHeading(x, ref y, w, notes.Count == 0 ? "NOTHING TO FIX" : "LOOK AT");
        if (notes.Count == 0) SubstatText(x, ref y, w, "It rolls as set.", StudioSkin.Faint);
        foreach (var note in notes) SubstatText(x, ref y, w, "· " + note, StudioSkin.Label);

        // ── test drops ──
        SubstatHeading(x, ref y, w, "TEST DROPS");
        substatRarity = EditorGUI.Popup(new Rect(x, y, 110, 20), substatRarity, SubstatRarityNames);
        string items = oddsType > 0 ? SubstatTypes[oddsType - 1].name.ToLowerInvariant() : "gear";
        if (GUI.Button(new Rect(x + 118, y - 1, w - 118, 22), new GUIContent($"Roll 8 {items} from floor {substatFloor}", "Real drops: a random item of the type, rolled at that rarity and floor as the loot does.")))
            RollSubstatTests(oddsType);
        y += 28;
        foreach (var line in substatRolls) SubstatText(x, ref y, w, line, StudioSkin.Label);

        // ── order and removal ──
        SubstatHeading(x, ref y, w, "IN THE TABLE");
        float bw = (w - 18) / 4;
        if (GUI.Button(new Rect(x, y, bw, 24), new GUIContent("Up", "Earlier in the table (the order is only for reading it).")) && substatPicked > 0)
        { int i = substatPicked; SubstatEdit("Move substat", () => { (props[i - 1], props[i]) = (props[i], props[i - 1]); }); substatPicked--; }
        if (GUI.Button(new Rect(x + bw + 6, y, bw, 24), "Down") && substatPicked < props.Count - 1)
        { int i = substatPicked; SubstatEdit("Move substat", () => { (props[i + 1], props[i]) = (props[i], props[i + 1]); }); substatPicked++; }
        if (GUI.Button(new Rect(x + (bw + 6) * 2, y, bw, 24), new GUIContent("Copy", "A copy right after it, with its own id.")))
        {
            var copy = JsonUtility.FromJson<ItemPropertyDef>(JsonUtility.ToJson(p));
            string cid = (p.id ?? "substat") + "_copy";
            for (int k = 2; props.Any(q => q != null && q.id == cid); k++) cid = (p.id ?? "substat") + "_copy" + k;
            copy.id = cid;
            int at = substatPicked + 1;
            SubstatEdit("Copy substat", () => props.Insert(at, copy));
            substatPicked = at;
        }
        if (GUI.Button(new Rect(x + (bw + 6) * 3, y, bw, 24), new GUIContent("Remove", "Take it out of the table (asks first).")) &&
            EditorUtility.DisplayDialog("Remove substat", $"Remove \"{p.id}\"?\n\nItems already saved with it keep the id in their save but lose the stat when they load. Switching it off keeps old items as they are.", "Remove", "Keep"))
        {
            int i = substatPicked;
            SubstatEdit("Remove substat", () => props.RemoveAt(i));
            substatPicked = Mathf.Min(i, props.Count - 1);
        }
        y += 34;
        if (Event.current.type == EventType.Repaint) substatDetailHeight = y + 8;
        GUI.EndScrollView();
    }

    // Chance of each substat on a drop of one type at each rarity: the game's roll, 600 times a rarity on random items of
    // the type, on the preview floor. Kept until the table, the type or the floor changes.
    Dictionary<string, float[]> SubstatOdds(int type)
    {
        if (!InventoryManager.HasInstance) return null;
        int key = type * 100 + substatFloor;
        if (substatOdds != null && substatOddsKey == key) return substatOdds;
        var slot = SubstatTypes[type - 1].slot;
        var gear = SubstatGear().Where(g => ItemPropertyTable.SlotOf(g) == slot).ToList();
        var odds = new Dictionary<string, float[]>();
        if (gear.Count > 0)
        {
            const int Drops = 600;
            var rng = new System.Random(4242);
            for (int k = 0; k < SubstatRarities.Length; k++)
                for (int n = 0; n < Drops; n++)
                    foreach (var roll in substatTable.Roll(gear[rng.Next(gear.Count)], SubstatRarities[k], substatFloor, rng))
                    {
                        if (!odds.TryGetValue(roll.id, out var c)) odds[roll.id] = c = new float[SubstatRarities.Length];
                        c[k] += 1f / Drops;
                    }
        }
        substatOdds = odds; substatOddsKey = key;
        return odds;
    }

    void RollSubstatTests(int type)
    {
        substatRolls.Clear();
        if (!InventoryManager.HasInstance) { substatRolls.Add("Test drops need the Room scene's Inventory Manager."); return; }
        var slot = type > 0 ? SubstatTypes[type - 1].slot : GearSlots.None;
        var gear = SubstatGear().Where(g => slot == GearSlots.None || ItemPropertyTable.SlotOf(g) == slot).ToList();
        if (gear.Count == 0) { substatRolls.Add("No Common gear of this type in Items/Data."); return; }
        var rng = new System.Random(substatSeed++);
        var rarity = SubstatRarities[substatRarity];
        for (int n = 0; n < 8; n++)
        {
            var item = gear[rng.Next(gear.Count)];
            var rolls = substatTable.Roll(item, rarity, substatFloor, rng);
            var lines = rolls.Select(roll => { var d = substatTable.Find(roll.id); return d == null ? roll.id : ItemData.StatLine(d.stat, ModifierType.Flat, roll.value * d.statPerPoint); });
            substatRolls.Add($"<color={SubGold}>{item.itemName}</color>   {(rolls.Length == 0 ? $"<color={SubGrey}>nothing rolled</color>" : string.Join(" · ", lines))}");
        }
    }

    // Things that keep a substat from rolling, or make it roll oddly.
    List<string> SubstatNotes(ItemPropertyDef p, List<ItemPropertyDef> props)
    {
        var notes = new List<string>();
        if (string.IsNullOrEmpty(p.id)) notes.Add($"<color={SubRed}>No id: it never rolls.</color>");
        else if (props.Count(q => q != null && q.id == p.id) > 1) notes.Add($"<color={SubRed}>Another substat has the id \"{p.id}\": saved items only find the first.</color>");
        if (!p.enabled) notes.Add($"<color={SubGold}>It's off: it doesn't roll (items that have it keep it).</color>");
        if (p.weight <= 0) notes.Add($"<color={SubGold}>Weight 0: it never rolls.</color>");
        if (p.slots == GearSlots.None) notes.Add($"<color={SubRed}>No item type: it never rolls.</color>");
        if (p.byFloor == null || p.byFloor.Length == 0) notes.Add($"<color={SubRed}>No value on any floor: it rolls 0.</color>");
        else
        {
            for (int f = 1; f < p.byFloor.Length; f++)
                if (p.byFloor[f] < p.byFloor[f - 1]) { notes.Add($"<color={SubGold}>Floor {f + 1}'s value is lower than floor {f}'s.</color>"); break; }
            if (p.byFloor.Any(v => v == 0)) notes.Add($"<color={SubGold}>A floor's value is 0: that item gets the line with no effect.</color>");
        }
        if (p.growthPerFloor < 0) notes.Add($"<color={SubGold}>It shrinks past floor {p.byFloor?.Length ?? 0} (negative growth).</color>");
        // another substat on the same stat: one item never gets both
        foreach (var (slot, name) in SubstatTypes)
        {
            if ((p.slots & slot) == 0) continue;
            var twins = props.Where(q => q != p && q != null && q.enabled && q.stat == p.stat && (q.slots & slot) != 0).Select(q => q.id).ToArray();
            if (twins.Length > 0) notes.Add($"{name}: {string.Join(", ", twins)} has the same stat, so one item never gets both.");
        }
        // gear that already has the stat never rolls it (its main number excepted)
        if (p.stat != StatType.AttackDamage && p.stat != StatType.Armor)
            foreach (var (slot, name) in SubstatTypes)
            {
                if ((p.slots & slot) == 0) continue;
                var items = SubstatGear().Where(g => ItemPropertyTable.SlotOf(g) == slot).ToList();
                if (items.Count == 0) { notes.Add($"{name}: no item of this type in the game yet."); continue; }
                var own = items.Where(g => g.BaseItem.statModifiers != null && g.BaseItem.statModifiers.Any(m => m != null && m.stat == p.stat)).ToList();
                if (own.Count == items.Count) notes.Add($"<color={SubGold}>{name}: every one already has {SubstatStatName(p.stat).ToLowerInvariant()}, so it never rolls there.</color>");
                else if (own.Count > 0) notes.Add($"{name}: {own.Count} of {items.Count} already have {SubstatStatName(p.stat).ToLowerInvariant()} (it never rolls on those).");
            }
        return notes;
    }
}
