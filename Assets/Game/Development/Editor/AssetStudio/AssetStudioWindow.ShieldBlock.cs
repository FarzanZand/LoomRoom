using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Shields block a flat amount (the user, 2026-11-11): a raised shield takes its Block off a blocked physical hit
// (CharacterStats.AfterBlock), and the Blocking skill takes its share off what is left. Each tier's shield is set so a
// Common one stops `share` (80%) of the average enemy hit on the floors where that tier is the usual gear (the level's Gear
// by floor; enemies weighted as the floor picks them, with the balance the Balance page reckons with: the level's
// DungeonBalance, each stage's damage, ProgressionManager > Enemy balance). A tier no floor of the level has is carried on
// from the last two tiers' step. From an eval, with Room.unity open:
// AssetStudioWindow.SetShieldBlocks("Assets/Game/Levels/Dungeon1/Dungeon1.asset", true) (false only prints the table).
// Run it again after enemy damage or the gear table changes.
public partial class AssetStudioWindow
{
    public static string SetShieldBlocks(string levelPath, bool apply, float share = .8f)
    {
        var d = AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (d == null) return "no level " + levelPath;
        var pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var im = InventoryManager.HasInstance ? InventoryManager.Instance : null;
        if (pm == null || im == null) return "open Room.unity: the managers are read from it";
        var w = CreateInstance<AssetStudioWindow>();
        var was = LootSource.FloorGear;
        LootSource.FloorGear = f => d.GearAt(f);
        var sb = new StringBuilder($"{d.name}: a Common shield of a floor's usual tier stops {share:P0} of its average enemy hit\n");
        try
        {
            w.wideDamage = pm.enemyBaseDamage; w.wideHealth = pm.enemyBaseHealth;
            var byTier = new Dictionary<int, List<float>>();
            for (int f = 1; f <= d.FloorCount; f++)
            {
                float sum = 0, weights = 0;
                foreach (var g in FloorEnemies(d, f).GroupBy(e => e.enemy))
                {
                    float weight = g.Sum(e => e.weight);
                    sum += w.NumbersAt(d, g.Key, f, null).damage * weight; weights += weight;
                }
                if (weights <= 0) continue;
                int tier = UsualTier(d, f);
                float average = sum / weights;
                sb.Append($"  floor {f,2}: tier {tier,2}, average hit {average:0.0}\n");
                if (!byTier.TryGetValue(tier, out var list)) byTier[tier] = list = new List<float>();
                list.Add(average);
            }
            if (byTier.Count == 0) return sb.Append("no enemies").ToString();
            // Each tier's hit; tiers in between and past the last carried on (step ratio of the nearest known tiers).
            var known = byTier.Keys.OrderBy(t => t).ToList();
            var hit = new Dictionary<int, float>();
            foreach (var t in known) hit[t] = byTier[t].Average();
            for (int t = 1; t <= LootFloorTiers.Names.Length; t++)
            {
                if (hit.ContainsKey(t)) continue;
                int below = known.LastOrDefault(k => k < t), above = known.FirstOrDefault(k => k > t);
                if (below > 0 && above > 0) hit[t] = Mathf.Lerp(hit[below], hit[above], (t - below) / (float)(above - below));
                else if (below > 0)
                {
                    int before = known.LastOrDefault(k => k < below);
                    float step = before > 0 ? Mathf.Pow(hit[below] / hit[before], 1f / (below - before)) : 1.15f;
                    hit[t] = hit[below] * Mathf.Pow(step, t - below);
                }
                else hit[t] = hit[above];
            }
            var shields = im.itemCatalog.Where(i => i != null && i.itemType == ItemType.Shield && i.tier > 0).OrderBy(i => i.tier).ToList();
            // A higher tier always blocks at least one more than the tier under it (two tiers' floors can hit alike).
            int last = 0, lastTier = 0;
            foreach (var s in shields)
            {
                if (!hit.TryGetValue(s.tier, out float h)) continue;
                int block = Mathf.Max(1, Mathf.RoundToInt(h * share));
                if (s.tier > lastTier) block = Mathf.Max(block, last + 1);
                last = block; lastTier = s.tier;
                float oldArmour = Modifier(s, StatType.Armor), oldBlock = Modifier(s, StatType.Block);
                sb.Append($"  {s.name,-18} tier {s.tier,2}: hit {h:0.0}{(byTier.ContainsKey(s.tier) ? "" : " (carried on)")}, block {block}  (was armour {oldArmour:0.#}, block {oldBlock:0.#})\n");
                if (!apply) continue;
                Undo.RecordObject(s, "Shield block");
                var mods = (s.statModifiers ?? new StatModifierEntry[0]).Where(m => m != null && !((m.stat == StatType.Armor || m.stat == StatType.Block) && m.type == ModifierType.Flat)).ToList();
                mods.Insert(0, new StatModifierEntry { stat = StatType.Block, type = ModifierType.Flat, value = block });
                s.statModifiers = mods.ToArray();
                EditorUtility.SetDirty(s);
            }
            if (apply) AssetDatabase.SaveAssets();
        }
        finally { LootSource.FloorGear = was; Object.DestroyImmediate(w); }
        return sb.ToString();
    }
}
