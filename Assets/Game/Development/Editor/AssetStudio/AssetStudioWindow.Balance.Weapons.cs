using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Balance view's Weapons and Enemies tabs and their fight numbers (the user, 2026-11-04: "add weapon balance there
// too, and add average hits needed to kill enemies, and on enemies, average hit to kill player with rare set of that floor
// level"). The player is an assumed one, said on the page: a Warrior with nothing bought on the sheet and no level points
// spent, at the level a run usually has on each floor (kills a floor), light hits only (no heavies, backstabs or
// ripostes). Armour goes through the game's own rules (CharacterStats.AfterArmor, CombatManager.ArmorCut, with this
// level's Gear by floor as the floor's usual gear), attributes through ProgressionManager.AttributeGains, the class's
// pools through AdventurerProgress.StartingPools, a rarity's main number through ItemData.RaisedMain; a hit is rolled as
// Hitbox rolls it (the weapon's spread, the skill's bonus, a critical by chance).
public partial class AssetStudioWindow
{
    int fightKills = 20;
    static AdventureRules adventureRules;
    int simWeaponFloor = 1, simWeaponKind;
    readonly List<ItemData> simWeapons = new();
    string simWeaponMany;

    // The assumed player on the page.
    struct Fighter
    {
        public float baseDamage, strengthDamage, critChance, critMultiplier, critCap;
        public float swordsBonus, macesBonus;   // the skills' damage bonus at the class's starting ranks (0.3 = +30%)
        public float health, healthPerLevel;
        public string about;
    }

    // The floor's usual gear for ArmorCut while the page draws: this level's Gear by floor (in a run the loader sets it).
    void WithFloorGear(TableLevelData d, Action draw)
    {
        if (EditorApplication.isPlaying) { draw(); return; }
        var was = LootSource.FloorGear;
        LootSource.FloorGear = f => d.GearAt(f);
        CombatManager.ForgetTierSets();
        try { draw(); }
        finally { LootSource.FloorGear = was; CombatManager.ForgetTierSets(); }
    }

    Fighter AssumedFighter(CombatManager cm, ProgressionManager pm)
    {
        var f = new Fighter { critMultiplier = cm != null ? cm.critMultiplier : 1.75f, critCap = cm != null ? cm.critChanceCap : .6f, critChance = cm != null ? cm.critChance : .08f };
        var player = AssetDatabase.LoadAllAssetsAtPath(TablePlayerPath).OfType<PlayerData>().FirstOrDefault();
        if (player != null && player.stats != null) foreach (var e in player.stats) if (e.stat == StatType.AttackDamage) f.baseDamage = e.baseValue;
        var rules = pm != null ? pm.classRules : null;
        var warrior = rules?.classes?.FirstOrDefault(c => c != null && c.displayName == "Warrior") ?? rules?.classes?.FirstOrDefault(c => c != null);
        if (pm == null || warrior == null) { f.about = "No ProgressionManager or class: base damage only."; return f; }
        // The attributes' points above their base (the sheet's score, plus the class's), as a run gives them.
        foreach (MetaAttribute a in Enum.GetValues(typeof(MetaAttribute)))
        {
            var track = pm.Track(a);
            if (track == null) continue;
            foreach (var (stat, amount, kind) in ProgressionManager.AttributeGains(track, pm.AttributeScore(a) + warrior.Attribute(a) - track.baseScore))
            {
                if (kind != ModifierType.Flat) continue;
                if (stat == StatType.AttackDamage) f.strengthDamage += amount;
                else if (stat == StatType.CriticalChance) f.critChance += amount;
            }
        }
        float Bonus(AdventureSkill skill)
        {
            int rank = Mathf.Clamp(pm.BaseRank(skill) + warrior.StartingRank(skill), 0, 100);
            var data = rules.skills?.FirstOrDefault(s => s != null && s.skill == skill);
            return rank * .01f * (data != null ? data.bonusAt100 : 0f);
        }
        f.swordsBonus = Bonus(AdventureSkill.Swords); f.macesBonus = Bonus(AdventureSkill.Maces);
        var progress = UnityEngine.Object.FindAnyObjectByType<AdventurerProgress>(FindObjectsInactive.Include);
        f.health = progress != null ? progress.StartingPools(warrior).health : warrior.health;
        f.healthPerLevel = warrior.healthPerLevel;
        int Score(MetaAttribute a) => pm.AttributeScore(a) + warrior.Attribute(a);
        f.about = $"You: {warrior.displayName}, sheet unbought, level points unspent. STR {Score(MetaAttribute.Strength)}, DEX {Score(MetaAttribute.Dexterity)}, CON {Score(MetaAttribute.Constitution)}. " +
                  $"Swords +{f.swordsBonus * 100f:0}%, Maces +{f.macesBonus * 100f:0}%. Crit {Mathf.Min(f.critCap, f.critChance) * 100f:0}% x{f.critMultiplier:0.##}. Light hits.";
        return f;
    }

    // The level a run usually has as it reaches each floor: Kills a floor of the floor's enemies (by weight), each worth its
    // experience x (1 + 12% a floor past the first, as RunManager gives it), and a floor's own experience on the way down.
    int[] UsualLevels(TableLevelData d, int kills)
    {
        if (adventureRules == null) adventureRules = AssetDatabase.FindAssets("t:AdventureRules").Select(g => AssetDatabase.LoadAssetAtPath<AdventureRules>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        var rules = adventureRules;
        var levels = new int[d.FloorCount + 2];
        int level = 1; float xp = 0f;
        float Next() => rules != null ? rules.levelXp + (level - 1) * rules.levelXpGrowth : 100f;
        void Gain(float amount) { xp += amount; while (xp >= Next() && level < 100) { xp -= Next(); level++; } }
        for (int f = 1; f <= d.FloorCount + 1; f++)
        {
            if (f > 1) Gain(rules != null ? rules.xpPerFloor : 0f);
            levels[f] = level;
            if (f > d.FloorCount) break;
            float total = 0f, weight = 0f;
            foreach (var (enemy, w) in FloorEnemies(d, f))
            {
                var data = DataOf(enemy);
                float each = data is EnemyData e && e.experience > 0 ? e.experience : (rules != null ? rules.xpPerEnemy : 15f) * Mathf.Clamp(NumbersAt(d, enemy, f, null).health / 20f, .5f, 6f);
                total += each * w; weight += w;
            }
            if (weight > 0f) Gain(kills * total / weight * (1f + (f - 1) * .12f));
        }
        return levels;
    }

    // The enemies a floor's generated rooms pick from (its stage's biome, and the base biome's), with their weights.
    static List<(GameObject enemy, float weight)> FloorEnemies(TableLevelData d, int floor)
    {
        var list = new List<(GameObject, float)>();
        var biome = d.BiomeAt(floor, out int inBiome, out _);
        foreach (var b in new[] { biome, d.baseBiome }.Distinct())
            if (b != null && b.enemies != null)
                foreach (var s in b.enemies)
                    if (s != null && s.enemy != null && s.weight > 0f && s.fromFloor <= Mathf.Max(1, inBiome)) list.Add((s.enemy, s.weight));
        return list;
    }
    static List<GameObject> EnemiesOn(TableLevelData d, int floor) => FloorEnemies(d, floor).GroupBy(e => e.enemy).Select(g => g.Key).ToList();

    // The floor's most common gear tier (the higher on a tie).
    static int UsualTier(TableLevelData d, int floor)
    {
        var row = d.GearAt(floor);
        if (row == null) return 1;
        var shares = row.Shares;
        int best = 0;
        for (int i = 1; i < shares.Length; i++) if (shares[i] >= shares[best]) best = i;
        return shares[best] > 0f ? best + 1 : 1;
    }

    static float Modifier(ItemData i, StatType stat)
    {
        float sum = 0f;
        if (i?.statModifiers != null) foreach (var m in i.statModifiers) if (m != null && m.stat == stat && m.type == ModifierType.Flat) sum += m.value;
        return sum;
    }

    // How many light hits a weapon needs, on average, to bring down that much health behind that much armour: each hit
    // rolled as Hitbox rolls it and taken as the enemy takes it (CharacterStats.AfterArmor).
    static float HitsToKill(Fighter f, ItemData weapon, float health, float armour, int floor, System.Random random, int fights = 300)
    {
        if (weapon == null || health <= 0f) return 0f;
        float attack = f.baseDamage + f.strengthDamage + Modifier(weapon, StatType.AttackDamage);
        float bonus = weapon.weaponCategory == WeaponCategory.Mace ? f.macesBonus : f.swordsBonus;
        float crit = Mathf.Min(f.critCap, f.critChance + Modifier(weapon, StatType.CriticalChance));
        int spread = Mathf.Max(0, weapon.damageSpread);
        long hits = 0;
        for (int n = 0; n < fights; n++)
        {
            float left = health; int count = 0;
            while (left > 0f && count < 999)
            {
                float hit = spread > 0 ? Mathf.Max(1f, attack + random.Next(-spread, spread + 1)) : attack;
                hit *= 1f + bonus;
                if (random.NextDouble() < crit) hit *= f.critMultiplier;
                left -= CharacterStats.AfterArmor(hit, armour, false, false, false, floor);
                count++;
            }
            hits += count;
        }
        return hits / (float)fights;
    }

    // The average light hit of a weapon before armour (spread, skill, criticals).
    static float AverageHit(Fighter f, ItemData weapon)
    {
        if (weapon == null) return 0f;
        float attack = f.baseDamage + f.strengthDamage + Modifier(weapon, StatType.AttackDamage);
        int spread = Mathf.Max(0, weapon.damageSpread);
        float mean = 0f;
        for (int s = -spread; s <= spread; s++) mean += Mathf.Max(1f, attack + s);
        mean /= 2 * spread + 1;
        float bonus = weapon.weaponCategory == WeaponCategory.Mace ? f.macesBonus : f.swordsBonus;
        float crit = Mathf.Min(f.critCap, f.critChance + Modifier(weapon, StatType.CriticalChance));
        return mean * (1f + bonus) * (1f + crit * (f.critMultiplier - 1f));
    }

    // The typical weapon of a kind and tier at a rarity: its main number raised to the rarity, with the properties that add
    // damage or critical chance at their average over many rolls of the table (one roll decided the whole cell, so a Rare
    // could out-hit a Unique, Zelda), made by the game's own ItemData.Rolled.
    // Kept until the next edit on the page, or a few seconds (the property table may be edited elsewhere).
    const int PropertyRolls = 200;
    static readonly Dictionary<(ItemData, ItemRarity, int), ItemData> typicalWeapons = new();
    static int typicalVersion = -1;
    static double typicalAt;
    static ItemData WeaponAt(Dictionary<(GearKind, int), ItemData> items, GearKind kind, int tier, ItemRarity rarity, int floor)
    {
        while (tier > 1 && !items.ContainsKey((kind, tier))) tier--;
        if (!items.TryGetValue((kind, tier), out var plain)) return null;
        var table = ItemPropertyTable.Current;
        if (rarity == ItemRarity.Common || table == null) return plain;
        if (typicalVersion != balanceVersion || EditorApplication.timeSinceStartup - typicalAt > 3) { typicalWeapons.Clear(); typicalVersion = balanceVersion; typicalAt = EditorApplication.timeSinceStartup; }
        if (typicalWeapons.TryGetValue((plain, rarity, floor), out var known)) return known;
        var sums = new Dictionary<string, float>();
        var random = new System.Random(tier * 31 + (int)rarity * 7 + floor);
        for (int n = 0; n < PropertyRolls; n++)
            foreach (var roll in table.Roll(plain, rarity, floor, random) ?? Array.Empty<RolledProperty>())
            {
                var def = table.Find(roll.id);
                if (def == null || def.stat != StatType.AttackDamage && def.stat != StatType.CriticalChance) continue;
                sums[roll.id] = (sums.TryGetValue(roll.id, out var s) ? s : 0f) + roll.value;
            }
        var average = sums.Select(kv => new RolledProperty(kv.Key, Mathf.Round(kv.Value / PropertyRolls * 100f) / 100f)).ToArray();
        return typicalWeapons[(plain, rarity, floor)] = plain.Rolled(rarity, average);
    }

    // An item's own flat modifier of a stat, edited as a percent; "-" when it has none.
    static void ModField(ItemData item, StatType stat, float width)
    {
        int at = -1;
        if (item.statModifiers != null) for (int i = 0; i < item.statModifiers.Length && at < 0; i++) if (item.statModifiers[i] != null && item.statModifiers[i].stat == stat) at = i;
        if (at < 0) { GUILayout.Label("-", StudioSkin.Faint, GUILayout.Width(width)); return; }
        var m = item.statModifiers[at];
        NumField(m.value, item, v => m.value = v, true, -1f, 1f, item.itemName + " " + stat, width);
    }

    // ── Weapons ──────────────────────────────────────────────────────

    void WeaponsTab(TableLevelData d, InventoryManager im, CombatManager cm, ProgressionManager pm)
    {
        if (cm == null) { GUILayout.Label("No CombatManager found.", dim); return; }
        var items = TierItems(im);
        var fighter = AssumedFighter(cm, pm);
        Question("How hard each weapon hits, by floor");
        GUILayout.Label(fighter.about, Wrapped, GUILayout.Width(pageWidth));
        TwoColumns(Mathf.Max(300f, Mathf.Min(pageWidth - 560, 600)), () =>
        {
            var player = AssetDatabase.LoadAllAssetsAtPath(TablePlayerPath).OfType<PlayerData>().FirstOrDefault();
            Group("EVERY HIT", player, "TablePlayer");
            if (player != null && player.stats != null)
            {
                int at = player.stats.FindIndex(e => e.stat == StatType.AttackDamage);
                if (at >= 0) In("Player base damage", player.stats[at].baseValue, player, v => { var x = player.stats[at]; x.baseValue = v; player.stats[at] = x; }, BalUnit.None, 0f, 100f);
            }
            var strength = pm?.Track(MetaAttribute.Strength);
            if (strength != null) In("Damage a Strength point", strength.bonusPerPoint, pm, v => strength.bonusPerPoint = v, BalUnit.None, 0f, 20f);
            Group("CRITICAL HITS", cm, "CombatManager");
            In("Chance", cm.critChance, cm, v => cm.critChance = v, BalUnit.Percent, 0f, 1f);
            In("Damage", cm.critMultiplier, cm, v => cm.critMultiplier = v, BalUnit.Times, 1f, 10f);
            In("Most chance", cm.critChanceCap, cm, v => cm.critChanceCap = v, BalUnit.Percent, 0f, 1f);
            Group("SWINGS", cm, "CombatManager");
            In("A swing every", cm.swingInterval, cm, v => cm.swingInterval = v, BalUnit.Seconds, .05f, 5f, tip: "Divided by attack speed.");
            Group("BY TIER (COMMON)");
            GUILayout.BeginHorizontal();
            Head("Tier", 96); Head("Sword", 56); Head("±", 44); Head("Crit", 56); Head("Speed", 56); Head("Mace", 56); Head("±", 44); Head("Speed", 56);
            GUILayout.EndHorizontal();
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label($"{t} {LootFloorTiers.Names[t - 1]}", StudioSkin.Strong, GUILayout.Width(96));
                foreach (var kind in new[] { GearKind.Sword, GearKind.Mace })
                {
                    if (!items.TryGetValue((kind, t), out var w)) { GUILayout.Label("-", dim, GUILayout.Width(kind == GearKind.Sword ? 220 : 164)); continue; }
                    var it = w;
                    int k = MainIndex(it);
                    GUILayout.Space(4);
                    if (k >= 0) NumField(it.statModifiers[k].value, it, v => it.statModifiers[k].value = v, false, 0f, 999f, it.itemName, 48); else GUILayout.Label("-", dim, GUILayout.Width(48));
                    GUILayout.Space(4);
                    NumField(it.damageSpread, it, v => it.damageSpread = Mathf.RoundToInt(v), false, 0f, 20f, it.itemName + " spread", 36);
                    if (kind == GearKind.Sword) { GUILayout.Space(4); ModField(it, StatType.CriticalChance, 48); }
                    GUILayout.Space(4); ModField(it, StatType.AttackSpeed, 48);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("±: damage spread. Crit, speed: the item's own, in %. Tiers 5-12: GearSets.Build() writes them again.", dim);
        }, () =>
        {
            int last = d.FloorCount;
            var lastTier = UsualTier(d, last);
            Headline($"Floor 1: a Common sword hits <b>{AverageHit(fighter, WeaponAt(items, GearKind.Sword, UsualTier(d, 1), ItemRarity.Common, 1)):0.#}</b>.   Floor {last}: a Rare sword <b>{AverageHit(fighter, WeaponAt(items, GearKind.Sword, lastTier, ItemRarity.Rare, last)):0.#}</b>.");
            GUILayout.Label("AVERAGE HIT, A WEAPON OF THE FLOOR'S MOST COMMON TIER", gold);
            foreach (var kind in new[] { GearKind.Sword, GearKind.Mace })
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                Head(kind.ToString().ToUpperInvariant(), 40); Head("Tier", 70);
                foreach (var rar in GearRarities) Head(rar.ToString(), 82, rar);
                GUILayout.EndHorizontal();
                for (int f = 1; f <= d.FloorCount; f++)
                {
                    int tier = UsualTier(d, f);
                    GUILayout.BeginHorizontal(GUILayout.Height(20));
                    Out($"w{kind}{f}f", f.ToString(), 40); Out($"w{kind}{f}t", LootFloorTiers.Names[tier - 1], 70);
                    foreach (var rar in GearRarities) Out($"w{kind}{f}{rar}", AverageHit(fighter, WeaponAt(items, kind, tier, rar, f)).ToString("0.#"), 82);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("Per light hit, before the enemy's armour, criticals counted at their chance.", dim);
        });
        GUILayout.Space(8);
        WeaponSimulator(d, im, fighter, items);
    }

    void WeaponSimulator(TableLevelData d, InventoryManager im, Fighter fighter, Dictionary<(GearKind, int), ItemData> items)
    {
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.Label("TRY A RANDOM WEAPON", gold);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Floor", GUILayout.Width(44));
        simWeaponFloor = Mathf.Clamp(EditorGUILayout.IntField(simWeaponFloor, GUILayout.Width(40)), 1, Mathf.Max(1, d.FloorCount));
        GUILayout.Space(12);
        string[] kinds = { "Sword", "Mace", "Either" };
        for (int i = 0; i < kinds.Length; i++)
            if (GUILayout.Toggle(simWeaponKind == i, kinds[i], StudioSkin.Tab, GUILayout.Width(70), GUILayout.Height(24)) && simWeaponKind != i) simWeaponKind = i;
        GUILayout.Space(12);
        bool one = GUILayout.Button("Roll a weapon", GUILayout.Width(120), GUILayout.Height(24));
        bool many = GUILayout.Button("Roll 1000", GUILayout.Width(100), GUILayout.Height(24));
        GUILayout.EndHorizontal();
        GearKind Pick(System.Random r) => simWeaponKind == 0 ? GearKind.Sword : simWeaponKind == 1 ? GearKind.Mace : r.NextDouble() < .5 ? GearKind.Sword : GearKind.Mace;
        if (one)
        {
            var random = new System.Random(unchecked(simSeed++ * 486187739 + simWeaponFloor * 7));
            simWeapons.Clear();
            simWeapons.Add(RollPiece(d, im, items, Pick(random), simWeaponFloor, random));
        }
        if (many)
        {
            var random = new System.Random(unchecked(simSeed++ * 486187739 + simWeaponFloor * 7));
            var hits = new List<float>();
            for (int n = 0; n < 1000; n++) hits.Add(AverageHit(fighter, RollPiece(d, im, items, Pick(random), simWeaponFloor, random)));
            hits.Sort();
            simWeaponMany = $"1000 weapons, floor {simWeaponFloor}: average hit {hits[100]:0.#}-{hits[900]:0.#} (middle 80%). Middle {hits[500]:0.#}, best {hits[999]:0.#}.";
        }
        var weapon = simWeapons.Count > 0 ? simWeapons[0] : null;
        if (weapon != null)
        {
            float attack = fighter.baseDamage + fighter.strengthDamage + Modifier(weapon, StatType.AttackDamage);
            int spread = weapon.damageSpread;
            float crit = Mathf.Min(fighter.critCap, fighter.critChance + Modifier(weapon, StatType.CriticalChance));
            GUILayout.Space(4);
            Headline($"<color=#{RarityHex(weapon.rarity)}>{weapon.DisplayName}</color>: hits <b>{AverageHit(fighter, weapon):0.#}</b> on average  <color=#a9a49a>({Mathf.Max(1f, attack - spread):0}-{attack + spread:0}, crit {crit * 100f:0}%)</color>");
            var rolled = weapon.statModifiers?.Skip(weapon.BaseModifierCount).Where(m => m != null).Select(m => m.Describe()).ToList();
            if (rolled != null && rolled.Count > 0) GUILayout.Label("Rolled: " + string.Join(", ", rolled), dim);
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(150)); Head("Health", 60); Head("Armour", 60); Head("Hits to kill", 90);
            GUILayout.EndHorizontal();
            var random = new System.Random(simWeaponFloor);
            foreach (var enemy in EnemiesOn(d, simWeaponFloor))
            {
                var n = NumbersAt(d, enemy, simWeaponFloor, null);
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                GUILayout.Label(enemy.name, StudioSkin.Label, GUILayout.Width(150));
                Out($"ws{enemy.name}h", n.health.ToString("0"), 60); Out($"ws{enemy.name}a", n.armor.ToString("0.#"), 60);
                Out($"ws{enemy.name}k", HitsToKill(fighter, weapon, n.health, n.armor, simWeaponFloor, random).ToString("0.#"), 90, true);
                GUILayout.EndHorizontal();
            }
        }
        if (!string.IsNullOrEmpty(simWeaponMany)) { GUILayout.Space(4); GUILayout.Label(simWeaponMany, StudioSkin.Label); }
        GUILayout.EndVertical();
    }

    // ── Enemies ──────────────────────────────────────────────────────

    void EnemiesTab(TableLevelData d, AssetStudioWorld.Level l, InventoryManager im, CombatManager cm, ProgressionManager pm)
    {
        if (l == null) { GUILayout.Label("No level.", dim); return; }
        var bal = d.balance;
        var rat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Rat.prefab");
        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
        Question("How hard enemies get, how many hits to kill them, and to die");
        float left = 470;
        bool wide = pageWidth >= left + 520;
        float keep = pageWidth;
        if (wide) GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(wide ? left : pageWidth));
        GUILayout.BeginVertical(StudioSkin.Inset);
        if (bal != null)
        {
            Group("EACH FLOOR DOWN", bal, bal.name);
            In("Damage", bal.damageGrowthPerFloor, bal, v => bal.damageGrowthPerFloor = v, BalUnit.PercentPerFloor, 0f, 1f, tip: "Compounded.");
            In("Health", bal.healthGrowthPerFloor, bal, v => bal.healthGrowthPerFloor = v, BalUnit.PercentPerFloor, 0f, 1f, tip: "Compounded.");
            In("Armour", bal.armorPerFloor, bal, v => bal.armorPerFloor = v, BalUnit.PerFloor, 0f, 20f);
            Group("EVERY FLOOR");
            In("Damage", bal.damageMultiplier, bal, v => bal.damageMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Health", bal.healthMultiplier, bal, v => bal.healthMultiplier = v, BalUnit.Times, 0f, 10f);
            In("Boss health", bal.bossHealthMultiplier, bal, v => bal.bossHealthMultiplier = v, BalUnit.Times, 0f, 10f, tip: "No growth a floor.");
        }
        else GUILayout.Label("<color=#e0a85a>No balance asset.</color>", new GUIStyle(dim) { richText = true });
        if (pm != null)
        {
            Group("ON TOP, EVERY ENEMY", pm, "ProgressionManager");
            In("Damage", pm.enemyBaseDamage, pm, v => pm.enemyBaseDamage = v, BalUnit.Times, 0f, 10f);
            In("Health", pm.enemyBaseHealth, pm, v => pm.enemyBaseHealth = v, BalUnit.Times, 0f, 10f);
        }
        GUILayout.EndVertical();
        GUILayout.Space(6);
        pageWidth = wide ? left : keep;
        StageSteps(l);
        pageWidth = keep;
        GUILayout.EndVertical();
        if (wide) GUILayout.Space(12); else GUILayout.Space(8);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(wide ? pageWidth - left - 12 : pageWidth));
        int last = d.FloorCount;
        var a1 = NumbersAt(d, soldier, 1, null); var a9 = NumbersAt(d, soldier, last, null);
        Headline($"A Crypt Soldier: floor 1 hits <b>{a1.damage:0.#}</b> with <b>{a1.health:0}</b> health; floor {last} hits <b>{a9.damage:0.#}</b> with <b>{a9.health:0}</b>.");
        GUILayout.Label("EACH FLOOR, ENEMIES' NUMBERS", gold);
        GUILayout.BeginHorizontal();
        Head("Floor", 44); Head("Damage", 62); Head("Health", 62); Head("Armour", 58); Head("Rat hit", 62); Head("Rat HP", 58); Head("Soldier hit", 76); Head("Soldier HP", 74);
        GUILayout.EndHorizontal();
        foreach (var f in RunDifficulty(l))
        {
            var a = NumbersAt(d, rat, f.floor, null); var b = NumbersAt(d, soldier, f.floor, null);
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            Out($"e{f.floor}f", (f.bossFloor ? "<color=#e05a5a>" : "") + f.floor + (f.bossFloor ? " boss</color>" : ""), 44);
            Out($"e{f.floor}d", $"x{f.damage * wideDamage:0.00}", 62); Out($"e{f.floor}h", $"x{f.health * wideHealth:0.00}", 62); Out($"e{f.floor}a", $"+{f.armour:0.#}", 58);
            Out($"e{f.floor}rd", a.damage.ToString("0.#"), 62); Out($"e{f.floor}rh", a.health.ToString("0"), 58); Out($"e{f.floor}sd", b.damage.ToString("0.#"), 76); Out($"e{f.floor}sh", b.health.ToString("0"), 74);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        if (wide) GUILayout.EndHorizontal();
        GUILayout.Space(8);
        FightsTable(d, im, cm, pm);
    }

    // Each floor's enemies: the hits a Common and a Rare weapon of the floor's most common tier need to bring each down, and
    // the hits each needs to bring down the player in a Rare set of the floor's usual gear.
    void FightsTable(TableLevelData d, InventoryManager im, CombatManager cm, ProgressionManager pm)
    {
        if (cm == null) return;
        var items = TierItems(im);
        var fighter = AssumedFighter(cm, pm);
        var levels = UsualLevels(d, fightKills);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginHorizontal();
        GUILayout.Label("HITS TO KILL, AND HITS TO DIE", gold);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Kills a floor", GUILayout.Width(90));
        fightKills = Mathf.Clamp(EditorGUILayout.IntField(fightKills, GUILayout.Width(40)), 1, 500);
        GUILayout.EndHorizontal();
        GUILayout.Label(fighter.about + " Level: as you reach the floor. A Rare set of the floor's usual gear. Weapons of its most common tier. No blocks.", Wrapped, GUILayout.Width(pageWidth - 24));
        GUILayout.BeginHorizontal();
        Head("Floor", 40); Head("Level", 44); Head("Your HP", 60); Head("Set", 50);
        GUILayout.Label("", GUILayout.Width(10));
        GUILayout.Label("Enemy", gold, GUILayout.Width(150));
        Head("HP", 50); Head("Armour", 54); Head("Its hit", 54);
        Head("Sword", 60, ItemRarity.Common); Head("Rare sword", 80, ItemRarity.Rare); Head("Mace", 60, ItemRarity.Common); Head("Rare mace", 80, ItemRarity.Rare);
        Head("Hits to die", 84);
        GUILayout.EndHorizontal();
        var random = new System.Random(5);
        for (int f = 1; f <= d.FloorCount; f++)
        {
            int tier = UsualTier(d, f), level = levels[f];
            float health = fighter.health + (level - 1) * fighter.healthPerLevel;
            float set = FloorSetArmour(d, items, f, ItemRarity.Rare);
            var sword = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Common, f); var rareSword = WeaponAt(items, GearKind.Sword, tier, ItemRarity.Rare, f);
            var mace = WeaponAt(items, GearKind.Mace, tier, ItemRarity.Common, f); var rareMace = WeaponAt(items, GearKind.Mace, tier, ItemRarity.Rare, f);
            var enemies = EnemiesOn(d, f);
            if (enemies.Count == 0) enemies.Add(null);
            GUILayout.Space(3);
            StudioSkin.Divider(GUILayoutUtility.GetRect(pageWidth - 24, 1));
            bool first = true;
            foreach (var enemy in enemies)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(20));
                if (first) { Out($"x{f}f", f.ToString(), 40, true); Out($"x{f}l", level.ToString(), 44); Out($"x{f}hp", health.ToString("0"), 60); Out($"x{f}s", set.ToString("0"), 50); }
                else GUILayout.Label("", GUILayout.Width(40 + 44 + 60 + 50 + 12));
                first = false;
                GUILayout.Label("", GUILayout.Width(10));
                if (enemy == null) { GUILayout.Label("no enemies", dim, GUILayout.Width(150)); GUILayout.EndHorizontal(); continue; }
                var n = NumbersAt(d, enemy, f, null);
                string k = $"x{f}{enemy.name}";
                GUILayout.Label(enemy.name, StudioSkin.Label, GUILayout.Width(150));
                Out(k + "h", n.health.ToString("0"), 50); Out(k + "a", n.armor.ToString("0.#"), 54); Out(k + "d", n.damage.ToString("0.#"), 54);
                Out(k + "1", HitsToKill(fighter, sword, n.health, n.armor, f, random).ToString("0.#"), 60, true);
                Out(k + "2", HitsToKill(fighter, rareSword, n.health, n.armor, f, random).ToString("0.#"), 80, true);
                Out(k + "3", HitsToKill(fighter, mace, n.health, n.armor, f, random).ToString("0.#"), 60, true);
                Out(k + "4", HitsToKill(fighter, rareMace, n.health, n.armor, f, random).ToString("0.#"), 80, true);
                float landed = CharacterStats.AfterArmor(n.damage, set, true, false, false, f);
                Out(k + "5", landed > 0f ? Mathf.Ceil(health / landed).ToString("0") : "-", 84, true);
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndVertical();
    }
}
