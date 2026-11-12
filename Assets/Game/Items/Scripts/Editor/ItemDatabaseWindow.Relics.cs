using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Item Database's relics (the user, 2026-11-11): Relics are hand-made gear with names of their own. They are listed
// first, Show > Relics lists only them, New > Relic with the look of makes one from any weapon, shield or armour piece,
// and a picked relic shows a card under its header with what makes it: name, look, main number (damage with its spread,
// or armour or block), Scale With Floor, its substats (stat lines, edited in place) and its unique skill (a Custom
// effect that says so, its text edited in place); the header shows the tooltip as the game does.
public partial class ItemDatabaseWindow
{
    // ── New relic ──

    void AddRelicItems(GenericMenu menu, string prefix)
    {
        var gear = items.Where(i => i != null && !ItemDatabaseAuthoring.IsArchived(i) && i.canBeEquipped && i.rarity != ItemRarity.Relic
                && (i.IsWeapon || i.itemType == ItemType.Shield || i.itemType == ItemType.Equipment) && i.worldPrefab != null)
            .OrderBy(i => i.itemType).ThenBy(i => i.IsWeapon ? (int)i.weaponCategory : 0).ThenBy(i => i.tier).ThenBy(i => i.itemName);
        foreach (var item in gear)
        {
            string group = item.IsWeapon ? (item.weaponCategory == WeaponCategory.Mace ? "Mace" : "Sword") : item.itemType == ItemType.Shield ? "Shield" : ItemData.SlotName(item.equipSlot);
            var captured = item;
            menu.AddItem(new GUIContent($"{prefix}{group}/Tier {item.tier}  {item.itemName}"), false, () =>
            {
                var relic = NewRelic(captured);
                ClearFilters();
                Rebuild(); Select(relic); revealSelected = true;
            });
        }
    }

    // A copy of the gear piece as a Relic: its look and numbers, a name to give it, never in loot (tier 0).
    public static ItemData NewRelic(ItemData look, string name = "New relic")
    {
        var relic = ItemDatabaseAuthoring.Duplicate(look);
        string firstName = relic.name;   // the copy's icon is rendered under this name
        // Its setup is not an undo step of its own: Ctrl+Z takes the whole relic away (RelicUndo), never back to a
        // plain copy of the gear piece.
        var names = new HashSet<string>(ItemDatabaseAuthoring.All().Where(i => i != relic).Select(i => i.itemName), StringComparer.OrdinalIgnoreCase);
        string unique = name; int n = 2;
        while (names.Contains(unique)) unique = name + " " + n++;
        relic.itemName = unique;
        relic.rarity = ItemRarity.Relic;
        relic.tier = 0;
        relic.lootWeight = 0;
        relic.scaleWithFloor = relic.IsWeapon;
        relic.effects = Array.Empty<EffectEntry>();
        EditorUtility.SetDirty(relic);
        AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(relic), Safe(unique));
        ItemDatabaseAuthoring.Save();
        ItemDatabaseAuthoring.SyncCatalog();
        RelicUndo.Made(relic, firstName);
        return relic;
    }

    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    // ── The relic panel ──

    void RelicPanel(ItemData relic)
    {
        using (new EditorGUILayout.VerticalScope(cardStyle))
        using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(760)))   // fields at a width they read at
        {
            EditorGUILayout.LabelField("RELIC", sectionStyle);
            float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 130f;
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.DelayedTextField("Name", relic.itemName);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(name) && name != relic.itemName)
            {
                Undo.RecordObject(relic, "Relic name");
                relic.itemName = name.Trim();
                EditorUtility.SetDirty(relic);
                AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(relic), Safe(relic.itemName));
            }

            // Look: the model it is drawn with, in hand and on the floor (and its icon).
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var model = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Look", "The model it is drawn with, in hand and on the floor."), relic.worldPrefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(relic, "Relic look"); relic.worldPrefab = model; EditorUtility.SetDirty(relic); }
                if (GUILayout.Button(new GUIContent("Icon", "Render its bag icon from the look."), GUILayout.Width(44))) GenerateIcons(new List<ItemData> { relic });
            }

            // The main number.
            relic.TryMainStat(out string label, out _, out _);
            int main = Array.FindIndex(relic.statModifiers ?? Array.Empty<StatModifierEntry>(), m => m != null && m.stat == relic.MainStatType && m.type == ModifierType.Flat);
            if (main >= 0)
            {
                EditorGUI.BeginChangeCheck();
                float value = relic.statModifiers[main].value;
                int spread = relic.damageSpread, up = relic.damageSpreadUp;
                bool scale = relic.scaleWithFloor;
                // Its number, and for a weapon its spread, on one line with the hits they make.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(label);
                    value = EditorGUILayout.FloatField(value, GUILayout.Width(56));
                    if (relic.IsWeapon)
                    {
                        GUILayout.Space(14);
                        GUILayout.Label(new GUIContent("Spread", "Hits land this much below or above the damage."), GUILayout.Width(46));
                        spread = EditorGUILayout.IntField(relic.damageSpread, GUILayout.Width(40));
                        GUILayout.Space(14);
                        GUILayout.Label(new GUIContent("Up", "This much more at the high end only."), GUILayout.Width(22));
                        up = EditorGUILayout.IntField(relic.damageSpreadUp, GUILayout.Width(40));
                        GUILayout.Space(14);
                        GUILayout.Label($"hits {Mathf.Max(1f, value - spread):0}-{value + spread + up:0}", subtitleStyle);
                    }
                    GUILayout.FlexibleSpace();
                }
                if (relic.IsWeapon)
                    scale = EditorGUILayout.Toggle(new GUIContent("Scale with floor", "Picked up in a dungeon, it hits as hard as a Unique weapon of its kind at the lowest tier that floor drops."), relic.scaleWithFloor);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(relic, "Relic numbers");
                    relic.statModifiers[main].value = value; relic.damageSpread = Mathf.Max(0, spread); relic.damageSpreadUp = Mathf.Max(0, up); relic.scaleWithFloor = scale;
                    EditorUtility.SetDirty(relic);
                }
            }

            // Substats: every stat line but the main number.
            GUILayout.Space(8);
            EditorGUILayout.LabelField("SUBSTATS", sectionStyle);
            var mods = (relic.statModifiers ?? Array.Empty<StatModifierEntry>()).ToList();
            int remove = -1;
            for (int i = 0; i < mods.Count; i++)
            {
                if (i == main || mods[i] == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var stat = (StatType)EditorGUILayout.EnumPopup(mods[i].stat, GUILayout.Width(150));
                    var kind = (ModifierType)EditorGUILayout.EnumPopup(mods[i].type, GUILayout.Width(110));
                    float v = EditorGUILayout.FloatField(mods[i].value, GUILayout.Width(60));
                    GUILayout.Label(ItemData.StatLine(stat, kind, v), subtitleStyle, GUILayout.Width(190));
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(relic, "Relic substat"); mods[i].stat = stat; mods[i].type = kind; mods[i].value = v; EditorUtility.SetDirty(relic); }
                    if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
                }
            }
            if (remove >= 0) { Undo.RecordObject(relic, "Remove substat"); mods.RemoveAt(remove); relic.statModifiers = mods.ToArray(); EditorUtility.SetDirty(relic); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("+ Substat", GUILayout.Width(90)))
            {
                Undo.RecordObject(relic, "Add substat");
                mods.Add(new StatModifierEntry { stat = StatType.CriticalChance, type = ModifierType.Flat, value = .05f });
                relic.statModifiers = mods.ToArray(); EditorUtility.SetDirty(relic);
            }

            // Unique skill: a Custom effect that says it is one; its text edited on its asset.
            GUILayout.Space(8);
            EditorGUILayout.LabelField("UNIQUE SKILL", sectionStyle);
            bool any = false;
            foreach (var e in relic.effects ?? Array.Empty<EffectEntry>())
            {
                if (e == null || e.type != EffectType.Custom || e.customEffect == null) continue;
                any = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(e.customEffect, typeof(ItemEffect), false, GUILayout.Width(220));
                    GUILayout.Label(e.trigger.ToString(), EditorStyles.miniLabel, GUILayout.Width(90));
                    if (GUILayout.Button("Select", GUILayout.Width(52))) { Selection.activeObject = e.customEffect; EditorGUIUtility.PingObject(e.customEffect); }
                }
                var so = new SerializedObject(e.customEffect);
                var text = so.FindProperty("skillText");
                if (text != null)
                {
                    EditorGUI.BeginChangeCheck();
                    string t = EditorGUILayout.TextArea(text.stringValue, EditorStyles.textArea, GUILayout.MinHeight(34));
                    if (EditorGUI.EndChangeCheck()) { text.stringValue = t; so.ApplyModifiedProperties(); }
                }
            }
            if (!any) EditorGUILayout.HelpBox("No unique skill: add an effect of type Custom with a skill asset (Items/Effects, Create > Items/Effects/Crit Kill Reward) in Effects below.", MessageType.None);

            EditorGUIUtility.labelWidth = labelWidth;
        }
    }
}
