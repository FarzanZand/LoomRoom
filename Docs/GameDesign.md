# LoomRoom

*Game design — 27 September 2026*

## The game

**A first-person roguelike dungeon crawler about fighting, finding better equipment, and growing a character through a long descent.**

Barony is the reference for the dungeon loop, classes, and baseline equipment stats. Around 90% of play happens in the dungeon. The miniature table, Dungeon Master, and room provide an Inscryption-like setting and a small supporting layer between runs.

Start with a small set of mechanics and make them feel good. The current combat scope is one-handed slashing weapons, one-handed bludgeoning weapons, shields, Fireball, and Heal. Loot and skills support those mechanics.

The rules below define the intended design. Numerical values are initial tuning proposals, not a statement of what is already implemented or an exact copy of Barony's formulas.

## The run and dungeon structure

Choose a class and descend through **15–20 floors or more**. The working full-run layout is 20 floors: six biomes of three floors each, then a two-floor finale. Three floors constitute one biome block, not an entire run.

Stay in the dungeon until victory or death. Equipment, character level, skill progress, health, mana, and supplies carry between floors. Quitting the application suspends the run; loading returns directly to the dungeon.

**Explore → fight → loot → equip or recover → train through use → descend.**

Remove the previous 20–40-minute target. A full descent needs time for several equipment upgrades and skill milestones. Measure floor pacing in playtests, then estimate total run duration. Support playing one run across multiple sessions.

### Biome progression

Names below are placeholders. Each biome has its own visual theme, dominant enemy family, encounter layouts, and loot table. Different enemies within the family fill distinct roles, so a block of three floors does not repeat one identical encounter.

| Floors | Biome | Enemy and encounter direction | Loot band |
|---|---|---|---|
| 1–3 | A: Cellars | Vermin and simple melee enemies; space to learn attacks and blocking | 1: basic weapons, light protection, small recovery items |
| 4–6 | B: Crypt | Skeleton fighters and shield users; corridors and flanking routes | 2: stronger weapons and armor, occasional attribute bonuses |
| 7–9 | C: Caves | Fast creatures and durable brutes; more open approaches | 3: clear upgrades, stronger shields, more useful jewelry |
| 10–12 | D: Ruins | Cultists and enemy casters; cover and target prioritization | 4: stronger physical and magic-oriented equipment |
| 13–15 | E: Fortress | Armored guards and coordinated groups; greater stamina pressure | 5: high protection and attack values, stronger attribute bonuses |
| 16–18 | F: Depths | Tougher enemy combinations and elites testing developed builds | 6: strongest regular equipment pool |
| 19–20 | Finale | Final encounters and boss using established combat rules | Final upgrades and supplies before the boss |

Within a biome, its first floor introduces the enemy family, its second combines behaviors, and its third presents a harder encounter and a worthwhile reward. A guardian or elite can cap a biome; six bespoke bosses are not required.

Generate each floor from authored rooms with branches, loops, recognizable landmarks, optional encounters, and stairs. Descending does not require killing every enemy. Avoid padding the floor count with empty corridors or repeated filler fights.

## Combat

### Two weapons, both one-handed

| Weapon | Skill | Examples | Feel |
|---|---|---|---|
| Swords | Swords | Bronze to Crystal Sword, knives | Reliable attacks, moderate stamina cost and recovery |
| Maces | Maces | Bronze to Crystal Mace, Grave Mace | Slower commitment, stronger stagger and guard pressure |

Use the available one-handed animations for light and heavy attacks. No two-handed weapons, bows, ammunition, polearms, throwing weapons, or third physical weapon category in the current scope. Weapon differences come from base Attack, reach, animation timing, and stamina cost. Those handling values are authored per weapon rather than randomly rolled affixes.

The right hand holds a one-handed weapon. The left hand holds either a shield or an equipped spell. Any class can use either weapon and either left-hand option. As in Barony, Swords and Maces are separate skills: each weapon trains and scales with its own skill, so switching to a new weapon type means starting that skill lower.

### Core actions

- **Light attack:** quick commitment and recovery; useful for safe openings.
- **Heavy attack:** more stamina and longer commitment, with greater damage and stagger or guard pressure.
- **Block:** protects against blockable attacks from the front and consumes stamina on impact. A raised shield cannot protect every direction.
- **Move:** step out of reach, circle a defender, use cover, or retreat to a doorway.
- **Cast:** with Fireball or Heal equipped in the left hand, use the left-hand action to cast it. With a shield equipped instead, that action blocks.

Stamina links attacking and defense. Reckless attacking leaves too little to absorb the next hit. An exhausted block breaks the player's guard briefly, with clear sound and animation feedback. Recovery should allow a careful player to regain control without long periods of waiting.

Tune attacks, blocking, spacing, and hit feedback before adding parries, dodge rolls, kicks, or new action inputs. Skills improve these existing actions.

### Enemies and fairness

Build encounters around slow attackers, shielded enemies, fast enemies, and occasional casters. Introduce a behavior before combining it with another. A shield user protecting a caster should demand a different approach from two slow attackers in a corridor.

Every attack needs a readable wind-up, understandable reach, and a recovery opening. Threats behind the player need audible cues. Hit sound and reaction distinguish a normal hit, blocked hit, and stagger. Damage types can influence enemy matchups, but avoid complete immunity to either starting weapon category or Fireball in the initial version.

Difficulty increases through enemy behavior, combinations, and terrain as well as stats. Bosses must be beatable with either weapon category and with magic-supported builds. They use the actions the dungeon has already taught.

## Simple loot

### Included items

| Category | Purpose |
|---|---|
| One-handed weapons | Improve Attack or offer different handling within slashing and bludgeoning |
| Shields and armor | Improve Armor Class and defensive capability |
| Rings and amulets | Supply straightforward attribute bonuses |
| Food | Slow health recovery between fights |
| Health and mana potions | Immediate recovery with a short use action |
| Gold | Buy equipment and recovery items inside the current run |

No ammunition, one-use scrolls, tools, crafting materials, or consumable keys in this version. Spells come as **tomes** found in the dungeon (see Spellcasting). Optional treasure is reached through exploration, encounters, or an in-dungeon interaction such as a lever. There is no need to find and carry a special tool.

Use the existing equipment slots where suitable. Do not add slots just to expand the loot pool. Ordinary items improve their core value; special items initially add a clear attribute bonus. Avoid triggered effects, elaborate affix combinations, item sets, and custom loot mechanics until the baseline feels right.

### Barony stat baseline

Attributes are kept in the data but are not shown in the menus for now, since they only nudge a few numbers. Use Barony's recognizable attribute vocabulary and core equipment values: **Attack, Armor Class, STR, DEX, CON, INT, PER, and CHR**. Attack and Armor Class are derived combat values; the six attributes describe the character and can receive gear bonuses. Gear does not need to display or roll every stat.

| Stat | Initial LoomRoom use |
|---|---|
| Attack (ATK) | Base physical power on weapons; combined with Strength, the Swords or Maces skill, and the attack used |
| Armor Class (AC) | Physical protection from armor, shields, and Constitution; active blocking is handled separately |
| Strength (STR) | Improves melee damage |
| Dexterity (DEX) | Improves movement and attack speed within limits supported by the animations |
| Constitution (CON) | Improves physical protection and maximum health |
| Intelligence (INT) | Improves maximum mana, Fireball damage, and Heal strength |
| Perception (PER) | Improves visibility range in dark areas, with a readability cap |
| Charisma (CHR) | Improves merchant prices, with limits that prevent buying and reselling for profit |

These names and the ATK/AC foundation follow Barony. The mappings above are the proposed reduced LoomRoom implementation; exact coefficients need tuning against our combat. Do not claim numerical parity with Barony. Health, mana, and stamina remain visible resources; stamina supports the existing combat design.

Examples: a sword displays its category and Attack; a breastplate displays AC; a ring might display +1 INT. A stronger sword should first mean better Attack, not an unrelated special effect. Show the resulting equipped character values in comparison tooltips.

Keep condition degradation, identification, blessing/curse management, random critical-chance rolls, lifesteal, and custom proc stats outside the current scope. Start with understandable numbers and reliable equipment.

### Where loot comes from

Enemies provide modest gold, recovery items, or occasional equipment. Chests and guarded side rooms offer more substantial finds. A biome's final encounter offers a reward appropriate to the transition ahead. Merchants let players choose between recovery now and a better item for later.

Changing biome changes the drop pool as well as its strength. Early areas emphasize simple physical equipment; later areas introduce stronger versions and more attribute-bearing armor and jewelry. Every biome still supports melee and magic builds with relevant equipment and mana or health recovery.

### Better loot as the character grows

Use dungeon depth to establish the loot band, with character level influencing quality within that band. Higher-level characters are more likely to receive the stronger entries available there. The next biome advances the available band, giving the descent clear equipment progression.

Each biome loot asset defines its level range, lower and upper equipment strengths, and weighted entries. Below the range, use the lower-quality weighting; above it, cap at the upper weighting. Do not let grinding the first biome produce final-biome gear. Existing drops are rolled once and never rerolled by leveling, reopening a chest, or reloading.

Most equipment should come from the current band, with some overlap and occasional stronger finds. Later floors should not routinely reward starter equipment. Do not guarantee every drop is an upgrade: players still compare protection, attributes, and weapon preference. Ensure several meaningful upgrades across a complete descent without replacing every slot on every floor.

## Classes and character level

Begin with **Warrior, Wizard, and Cleric**, all available from the start. This roster exercises the current weapons, blocking, Fireball, and Heal. Room puzzles can permanently unlock additional classes with different starting combinations of the same supported mechanics.

| Class | Starting gear | Attribute emphasis | Starting skills | Spells |
|---|---|---|---|---|
| Warrior | Sword, shield, basic armor | STR and CON | Swords 25, Blocking 25, Maces 15, Athletics 10 | None |
| Wizard | Weak sword, robes | INT | Sorcery 50, Thaumaturgy 15, Swords 5 | Fireball Tome (in hand) |
| Cleric | Mace, shield, light armor | CON and INT | Thaumaturgy 40, Maces 25, Blocking 10, Trading 10 | Heal Tome (on the hotbar) |

Unlisted skills start at 0. Class assets define starting attributes, resource values, equipment, and proficiencies. The Wizard has the largest mana reserve; the Warrior has stronger physical resources; the Cleric sits between them. Exact starting values are listed in Corrected Barony starting stats below.

All classes can use every supported weapon and train every skill. Spells are found in the dungeon as tomes; only the classes built around a spell start with one. Class identity comes from starting competence, gear, and resources.

### Character level versus skill rank

Character level measures overall progress through this longer dungeon. Start at level 1. Defeating enemies and completing a floor grant character XP; stronger encounters grant more. Spawned or endlessly replenished enemies have limited XP rewards. The XP curve and class-specific attribute growth are authored data.

On a level-up, apply the class's visible attribute-growth schedule and modest maximum-resource growth. The schedule favors the class's strengths and is shown in its description. Maximum-resource increases do not refill current health or mana. No spendable attribute points or additional perk tree initially.

Skills progress independently through use. Killing an enemy with a sword grants character XP and may raise Swords; gaining a character level does not raise any skill. Loot generation reads character level, never an ambiguous average of skill ranks. Kill experience scales with the enemy: an authored value on its data, or one derived from its maximum health.

Both character level and skills reset for a new run. There is no permanent training between runs.

## Spellcasting: Fireball and Heal

**Spells are equipped in the left hand instead of a shield.** Fireball and Heal are two choices for that same slot; only one can be equipped at a time. The right-hand weapon stays equipped. A visible spell effect in the left hand communicates the active spell.

**Spells are tomes, like Barony's spellbooks.** A tome is an ordinary item: found in chests and on enemies, kept on the hotbar, dropped or sold like anything else. Selecting a tome puts it in the left hand, and the left-hand action casts its spell. Selecting the shield puts the shield back. There are no separate spell keys. (Barony also lets a strong enough character learn a spell from a book; that is not in this version.)


Holding the left-hand action blocks with a shield; pressing it casts an equipped spell once. No casting through an equipped shield and no blocking while a spell is equipped.

Switching has a short, visible equip transition, initially 0.3 seconds. During it, the left hand cannot block or cast. Swapping cannot cancel an attack or spell recovery, and a swap requested during a committed action waits until it finishes. This keeps switching responsive without eliminating the defensive tradeoff.

Casting uses a left-handed gesture or compatible existing presentation. Attack, block, and cast commitments cannot overlap; movement remains possible at a reduced speed. No two-handed animations are required. The HUD shows the equipped left-hand option, mana cost, and ready/casting/swapping state.

### Fireball

- Aim a projectile with the crosshair. It collides with the first enemy or solid surface.
- On impact, deal fire damage in a small radius. Walls block the blast. A target receives damage once, rather than separate full direct-hit and explosion damage.
- Scale damage with INT and Sorcery. No damage-over-time burning, chaining, or extra debuff system initially.
- A cast that damages a hostile target may raise Sorcery. Misses still spend mana.
- Suggested first tuning: 12 mana, 0.6-second cast, 0.35-second recovery, and a 1.5-metre explosion radius. Damage is tuned against early enemy health.

### Heal

- Restore the caster's health, capped at maximum health. Self-targeted only.
- Scale the amount with INT, CON and Thaumaturgy. It does not add a ward, regeneration effect, or revival.
- Only health restored from wounds dealt by enemies can raise Thaumaturgy.
- Suggested first tuning: 15 mana, 1-second cast, and 0.4-second recovery. Heal should be useful under pressure but leave an opening for enemies.
- At full health, reject the cast before spending mana. If no health is missing at release, cancel without charging mana.

### Mana and cast rules

Check mana before starting and again at release. Spend mana once on successful release; no cost for a canceled wind-up. A stagger or death cancels the wind-up. Damage without stagger does not cancel it. Once released, a spell's effect remains valid even if the caster is subsequently hit.

Insufficient mana gives a clear HUD response and a short sound without trapping the player in an animation. Mana costs have a minimum of 1 after modifiers. No random casting failure.

Restore mana through mana potions and occasional dungeon fountains. No passive mana regeneration initially, so waiting in safety cannot supply unlimited Fireballs and healing. Tune potion and fountain availability across all biomes so magic is sustainable through exploration. Fountains have limited uses; casting alone never creates a net gain of mana.

## Skills (Barony style)

Seven skills cover the current mechanics. Each runs from **0 to 100** and rises **one point at a time, by chance, when the player successfully does what the skill is about**. There are no experience bars and no skill points to spend. Ranks carry Barony's tier names: None (0), Novice (1-19), Basic (20-39), Skilled (40-59), Expert (60-79), Master (80-99) and Legendary (100). Each skill gives a continuous bonus that grows with its rank, and a Legendary bonus at 100.

| Skill | Stat | Practised by (chance per success) | Bonus at 100 | Legendary |
|---|---|---|---|---|
| Swords | STR | Hitting an enemy with a sword (1 in 10), killing one (1 in 8) | +50% sword damage | +5 Attack with swords; fully charged strikes deal 25% more |
| Maces | STR | Hitting an enemy with a mace (1 in 10), killing one (1 in 8) | +50% mace damage | +5 Attack with maces; fully charged strikes stagger twice as long |
| Blocking | CON | Blocking an enemy's attack (1 in 6) | -50% stamina per blocked hit | Blocked hits deal no damage |
| Sorcery | INT | Hitting an enemy with Fireball (1 in 4 per cast) | +60% spell damage, 30% faster casting | +40% power from INT; casts twice as fast |
| Thaumaturgy | CON | Healing wounds dealt by enemies (1 in 3 per cast) | +60% healing, 30% faster casting | +40% power from INT and CON; casts twice as fast |
| Athletics | DEX | Sprinting over new ground on a floor (1 in 5 per 12 m); swimming once water exists | -50% sprint stamina | +20 maximum stamina |
| Trading | CHR | Buying from a merchant (1 in 3), selling (1 in 5) | Prices -20%, sales +30% | Prices a further 10% lower |

The numbers above live on each skill's asset (`Progression/Data/Skills`) and the skill sheet shows them exactly as authored.

### Training rules and pace

A success only counts when it achieves something: air swings, scenery hits, overhealing and walking into walls never roll. Chances fall toward half their value as a skill approaches 100 (each skill's *chance at master*), so high ranks take longer. One enemy can teach at most three points of a weapon skill and two of Blocking, so a single foe cannot be farmed. Only health lost to enemies can train Thaumaturgy. Each Fireball cast rolls once, however many enemies it catches.

Skill gains are announced in the message log ("Your Swords skill increases to 26") and with a notification when a new tier is reached. Equipment attribute bonuses never raise skills.

## Character, skills, and spell menus

The player must be able to understand their build without a wiki. Use a unified character menu with **Equipment, Skills, and Spells** tabs. Opening it pauses this single-player dungeon; closing it resumes the same state without a room transition.

### Skills tab

Laid out like Barony's sheet: a level plate with the class, level and an XP bar; every skill as a row with its icon, rank (coloured by tier) and name; and a detail panel for the hovered skill, which clicking pins. The panel shows the tier and rank, the associated stat, what the skill does, the current bonus, how it is practised (with its chances), and the Legendary bonus. Attributes, Attack and AC sit under the skills; spells beneath the detail panel.

### Equipment and stats

Show class, character level and XP, health/mana/stamina, the six attributes, Attack, and AC. Attribute tooltips explain their actual implemented effects and separate base, class/level, and gear contributions.

Item tooltips show name, slot, weapon category where applicable, ATK or AC, attribute bonuses, and sell value. Compare against the equipped item with signed changes. Show relevant weapon handling through clear labels. No single overall gear score that hides tradeoffs.

### Spells tab

Show only Fireball and Heal. Each has its icon, effect, current mana cost, cast/recovery time, current damage or healing, associated skill, and quick-access assignment controls. Values include current attributes and skills and update when gear changes. Assign spells to ordinary hotbar slots and select the slot to replace the current shield or spell. The Equipment tab displays the same left-hand slot and explains that shield and spell are mutually exclusive.

## Death, saving, and the room

Death ends the character's equipment, gold, level, and trained skills. Victory ends the descent; another run starts at the selected class baseline. **No Threads, no currency carried into the room, no room shops, and no permanent stat purchases.**

Save character level/XP, attributes, inventory, resources, skill ranks, floor/biome, and encounter progress needed by the resume method. A floor-entry checkpoint is acceptable initially if the entire floor and player restore to that same checkpoint. Never combine a refreshed floor with inventory or XP earned later on it. Explain checkpoint behavior on quit.

Room exploration happens only after victory or death and remains a small supporting layer. Nothing from the room is equipped or carried into runs. No mid-run room visits. Let the player immediately start again.

### The memorial table

A table in the room holds a pewter miniature for each past character who died, on a small base, the most recent first. The miniature matches the class. Looking at one shows its class, level and deepest floor; using it adds the details to the message log. There is no heading or floating text on the table.

Record each death once. Keep the latest miniatures visible when display space fills, with older records available through the same table. Returning to the room should not force the player to inspect a miniature or watch a long placement animation.

### Room puzzles unlock classes

A solved room puzzle can unlock a new class permanently. It appears in class selection for future runs; it does not alter the current character or grant permanent stat bonuses to other classes. Unlocks are earned through discovery, never currency.

First example: reaching floor 3 for the first time gives the clue (shield, flame, hand). Three wooden tiles with those symbols then appear on the memorial table. Pressing them in order unlocks the **Arcanist**, who starts with a sword, a Fireball Tome and Sorcery 30. Its overall starting power should match the other classes; the reward is a different mix.

Clues are recorded when discovered and survive death. Solved puzzles stay solved. Class miniatures remain in the room and serve as displays or selection representations; they are not carried into the dungeon. Ordinary dungeon completion never requires solving a room puzzle.

Save memorial records and class unlocks separately from the current run. Loading a death recap must not duplicate its miniature. A new run resets character power but preserves the player's unlocked class choices.

## Development and validation

1. Prove one-handed melee, shield defense, Fireball, and Heal against a few readable enemy roles.
2. Build one three-floor biome with simple equipment, recovery supplies, class starts, and the complete skills menu. This is a testing slice.
3. Add a second biome and verify a noticeable change in enemies, encounters, and loot strength.
4. Extend to six biome blocks and a finale, then balance the complete 20-floor run for resource supply, gear upgrades, character growth, and skill milestones.
5. Add the room's supporting presentation once the dungeon loop earns repeat play.

Validate that all three classes survive their opening floors, both weapon categories remain useful, mana supply supports casting across the descent, and loot improves without making every find identical. Test every skill's practice actions and Legendary bonus in play. Check menu readability and navigation with mouse and controller, plus reliable save/resume without duplicate rewards.

Keep item stats, class starts/growth, spell values, skill practice chances and bonuses, and biome loot bands in clearly named editable assets. Author reusable UI in prefabs with serialized references. Do not regenerate over designer edits. No new editor tools or menu commands are implied by this design.

## How content is organised

- **Spells:** `Combat/Spells/<Spell>/` holds everything one spell needs: the spell asset, its hotbar item, the projectile, hand and impact effects, and its sounds. Projectiles are **variants of** `Combat/Spells/_Base/Base Projectile`, which carries the logic (flight, collision, blast, light, audio); a variant only adds art under its `Art` child. Arm poses for casting are clips in `Characters/Animations/Spells`, played by the "Left Hand Spell" animator layer.
- **Items:** `Items/Data/{Weapons, Armor, Shields, Consumables}` by type: one tiered set (Leather/Bronze, Iron, Steel, Crystal) plus a few unique weapons. Retired items stay in `_Archive` so old references and saves still resolve. Loot tables are in `Items/LootTables`.
- **Character growth:** `Progression/Data` holds the rules, one asset per class and one per skill.
- **Menus:** built from the pixel kit in `UI/Sprites/Kit` (panel, inset, buttons, plaque, divider, card frames). Each table's card picture is a shot of the level, kept beside its level data.

## References

[Barony character attributes](https://barony.wiki.gg/wiki/Category:Character_Attributes) and [armor](https://barony.wiki.gg/wiki/Armor) provide the reference stat vocabulary. [The official combat update](https://www.baronygame.com/blog/qod-update-launched) explains the distinction between passive armor and active defense. LoomRoom's reduced mechanics, class balance, spells, and perk values above are its own initial implementation proposal.


## Corrected Barony starting stats

Attribute order: STR / DEX / CON / INT / PER / CHR. These are the starting baselines from the official Barony class tables.

| Class | HP | MP | Attributes | Starting skills |
|---|---:|---:|---|---|
| Warrior | 30 | 20 | 1 / 1 / 0 / -2 / -1 / 1 | Swords 25, Blocking 25, Maces 15, Athletics 10 |
| Wizard | 20 | 50 | 0 / -1 / 0 / 3 / 1 / -1 | Sorcery 50, Thaumaturgy 15, Swords 5 |
| Cleric | 30 | 30 | 0 / -1 / 1 / 0 / 1 / 0 | Thaumaturgy 40, Maces 25, Blocking 10, Trading 10 |
| Arcanist | 25 | 40 | -1 / 1 / -1 / 1 / 1 / -1 | Sorcery 30, Swords 20, Athletics 10 |

Unlisted skills start at zero. Swords, Maces, Blocking, Sorcery, Thaumaturgy and Trading are Barony's own skills; Athletics stands in for Barony's old Swimming skill and also covers running. Unsupported skills are not redistributed. Negative attributes remain negative, and CON/INT do not inflate starting HP/MP. The one-handed equipment, two spells, stamina, the Athletics skill, attribute effects and level-growth sequence remain LoomRoom adaptations. Stable save IDs and the class unlock flag are preserved.

Sources: [Warrior](https://barony.wiki.gg/wiki/Warrior), [Wizard](https://barony.wiki.gg/wiki/Wizard), [Cleric](https://barony.wiki.gg/wiki/Cleric), [Arcanist](https://barony.wiki.gg/wiki/Arcanist).

