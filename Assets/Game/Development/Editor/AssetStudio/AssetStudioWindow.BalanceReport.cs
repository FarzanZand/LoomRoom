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

    // Swords against maces, as text (the user, 2026-11-15: "Why would you ever pick maces over swords?"), from an eval:
    // AssetStudioWindow.WeaponKindsReport("Assets/Game/Levels/Dungeon1/Dungeon1.asset"). For every tier a Common sword's
    // and mace's average light hit and its damage over time (the hit x the weapon's swing speed, so in damage a plain
    // swing's time), then for every floor of the level the light hits each needs on the floor's enemies, by weight, and
    // those hits in plain swings' time (hits / swing speed). The page's own sums and its assumed fighter.
    public static string WeaponKindsReport(string levelPath, int kills = 20)
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
            var random = new System.Random(5);
            static float Speed(ItemData i) => Mathf.Max(.1f, 1f + Modifier(i, StatType.AttackSpeed));
            var sb = new StringBuilder($"{d.name}: swords against maces, Common. {fighter.about}\n");
            sb.Append("tier | sword: damage, average hit, swing speed, damage over time | mace: the same | mace over sword\n");
            for (int t = 1; t <= 12; t++)
            {
                if (!items.TryGetValue((GearKind.Sword, t), out var s) || !items.TryGetValue((GearKind.Mace, t), out var m)) continue;
                float sh = AverageHit(fighter, s), mh = AverageHit(fighter, m);
                float st = sh * Speed(s), mt = mh * Speed(m);
                sb.Append($"T{t,-2} | {s.name,-15} {Modifier(s, StatType.AttackDamage),3:0} {sh,5:0.0} x{Speed(s):0.00} {st,5:0.0} | {m.name,-15} {Modifier(m, StatType.AttackDamage),3:0} {mh,5:0.0} x{Speed(m):0.00} {mt,5:0.0} | {(st > 0 ? mt / st * 100f - 100f : 0f),5:+0.0;-0.0}%\n");
            }
            sb.Append("floor | tier | sword: hits to kill, in plain swings' time | mace: the same | mace's time over sword's\n");
            for (int f = 1; f <= d.FloorCount; f++)
            {
                int tier = UsualTier(d, f);
                var sword = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Common, f);
                var mace = WeaponAt(items, GearKind.Mace, tier, ItemRarity.Common, f);
                if (sword == null || mace == null) continue;
                var at = AtFloor(fighter, f, kills);
                float sHits = 0, mHits = 0, sum = 0;
                var lines = new StringBuilder();
                foreach (var g in FloorEnemies(d, f).GroupBy(e => e.enemy))
                {
                    float weight = g.Sum(e => e.weight);
                    var n = w.NumbersAt(d, g.Key, f, null);
                    float a = SimulateHitsToKill(at, sword, n.health, n.armor, f, random, 2000), b = SimulateHitsToKill(at, mace, n.health, n.armor, f, random, 2000);
                    sHits += a * weight; mHits += b * weight; sum += weight;
                    lines.Append($"      {g.Key.name,-22} hp {n.health,5:0} arm {n.armor,4:0.#} | sword {a,4:0.0} hits, {a / Speed(sword),4:0.0} swings | mace {b,4:0.0} hits, {b / Speed(mace),4:0.0} swings\n");
                }
                if (sum <= 0) sum = 1;
                float sTime = sHits / sum / Speed(sword), mTime = mHits / sum / Speed(mace);
                sb.Append($"{f,2} | T{tier,-2} | {sHits / sum,4:0.0} hits, {sTime,4:0.0} swings | {mHits / sum,4:0.0} hits, {mTime,4:0.0} swings | {(sTime > 0 ? mTime / sTime * 100f - 100f : 0f),5:+0.0;-0.0}%\n");
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
