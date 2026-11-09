using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// The Balance page's fights, as text for every floor of a level, so a whole run can be read (and tuned) at once from an
// eval: AssetStudioWindow.BalanceReport("Assets/Game/Levels/Dungeon1/Dungeon1.asset"). Per floor: the level a run has
// there, its health, the Rare set's armour, the floor's usual tier; then each enemy's health, armour and hit, the light
// hits a Common, Rare and Legendary sword of that tier need to bring it down, and the hits it needs to bring down the player
// in the Rare set; and the floor's averages weighted as its enemies are picked. Same sums as the page (its own methods).
public partial class AssetStudioWindow
{
    public static string BalanceReport(string levelPath, int kills = 20, bool perEnemy = true)
    {
        var d = AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
        if (d == null) return "no level " + levelPath;
        var pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var cm = CombatManager.HasInstance ? CombatManager.Instance : null;
        var im = InventoryManager.HasInstance ? InventoryManager.Instance : null;
        if (pm == null || cm == null || im == null) return "open Room.unity: the managers are read from it";
        var w = CreateInstance<AssetStudioWindow>();
        var was = LootSource.FloorGear;
        LootSource.FloorGear = f => d.GearAt(f);
        CombatManager.ForgetTierSets();
        try
        {
            w.wideDamage = pm.enemyBaseDamage; w.wideHealth = pm.enemyBaseHealth;
            var items = TierItems(im);
            var fighter = w.AssumedFighter(cm, pm);
            var levels = w.UsualLevels(d, kills);
            var random = new System.Random(5);
            var sb = new StringBuilder($"{d.name}: {d.FloorCount} floors, {kills} kills a floor. {fighter.about}\n");
            sb.Append("floor | tier | level | hp | set | avg hits Common / Rare / Legendary sword | avg hits to die | strongest hit\n");
            for (int f = 1; f <= d.FloorCount; f++)
            {
                int tier = UsualTier(d, f), level = levels[f];
                float health = fighter.health + (level - 1) * fighter.healthPerLevel;
                float set = FloorSetArmour(d, items, f, ItemRarity.Rare);
                var common = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Common, f);
                var rare = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Rare, f);
                var legendary = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Legendary, f);
                var at = AtFloor(fighter, f, kills);
                float wc = 0, wr = 0, wl = 0, wd = 0, wsum = 0, top = 0;
                var lines = new StringBuilder();
                foreach (var g in FloorEnemies(d, f).GroupBy(e => e.enemy))
                {
                    float weight = g.Sum(e => e.weight);
                    var n = w.NumbersAt(d, g.Key, f, null);
                    float c = HitsToKill(at, common, n.health, n.armor, f, random), r = HitsToKill(at, rare, n.health, n.armor, f, random), l = HitsToKill(at, legendary, n.health, n.armor, f, random);
                    float landed = CharacterStats.AfterArmor(n.damage, set, true, false, false, f);
                    float die = landed > 0 ? Mathf.Ceil(health / landed) : 0;
                    wc += c * weight; wr += r * weight; wl += l * weight; wd += die * weight; wsum += weight; top = Mathf.Max(top, n.damage);
                    if (perEnemy) lines.Append($"      {g.Key.name,-22} hp {n.health,5:0} arm {n.armor,4:0.#} hit {n.damage,5:0.#} | {c:0.#} / {r:0.#} / {l:0.#} | die in {die:0}\n");
                }
                var guardian = d.Milestone(f);
                if (guardian != null && guardian.boss != null)
                {
                    var bossPrefab = guardian.boss;
                    if (bossPrefab != null)
                    {
                        var n = w.NumbersAt(d, bossPrefab, f, guardian);
                        float landed = CharacterStats.AfterArmor(n.damage, set, true, false, false, f);
                        lines.Append($"      BOSS {bossPrefab.name,-17} hp {n.health,5:0} arm {n.armor,4:0.#} hit {n.damage,5:0.#} | {HitsToKill(at, common, n.health, n.armor, f, random):0.#} / {HitsToKill(at, rare, n.health, n.armor, f, random):0.#} / {HitsToKill(at, legendary, n.health, n.armor, f, random):0.#} | die in {(landed > 0 ? Mathf.Ceil(health / landed) : 0):0}\n");
                    }
                }
                if (wsum <= 0) wsum = 1;
                sb.Append($"{f,2} | T{tier,-2} | L{level,-2} | {health,4:0} | {set,4:0} | {wc / wsum:0.0} / {wr / wsum:0.0} / {wl / wsum:0.0} | {wd / wsum:0.0} | {top:0.#}\n");
                sb.Append(lines);
            }
            return sb.ToString();
        }
        finally
        {
            LootSource.FloorGear = was;
            CombatManager.ForgetTierSets();
            DestroyImmediate(w);
        }
    }
}
