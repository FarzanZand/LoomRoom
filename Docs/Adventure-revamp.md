# Adventure revamp — playable prototype

The current biome is configured for 20 floors. Existing melee, shield and movement animations remain in use. Three starting classes and one room-unlocked class share the same weapons and two spells. The room stores class unlocks and memorials, not dungeon currency or gear.

## Playing

- Choose Warrior, Wizard or Cleric at the adventure table, then enter Dungeon1.
- There is **one hotbar**. Its numbered slots hold weapons, shields, reusable spells and consumables. Drag items and spells between slots in the inventory.
- Select Fireball or Heal's slot to equip it in the left hand. Right click casts. Selecting the shield slot restores blocking. The right-hand weapon remains equipped. There are no separate F/G/V spell shortcuts.
- Tab opens the paused character menu. Equipment handles inventory; Skills & Spells shows all five skills, training progress, attributes and the four milestone perks for each skill. Hover or select a skill row for details.
- Melee and offensive magic train on hostile hits; blocking trains on blocked hostile attacks; defensive magic trains on healing damage taken from enemies. Athletics trains while exploring new ground. Air swings, healing at full health and walking repeatedly over the same patch do not give training.
- A run ends in death or victory. Its entrance stays sealed. Resume saved floor returns to the start of the saved floor, not the exact position where play stopped.

## Designer assets

| Asset or component | What to edit |
|---|---|
| `Assets/Game/Progression/Data/Adventure Rules.asset` | Level XP, use-training limits, attribute coefficients and growth tuning |
| `Assets/Game/Progression/Data/Classes/` | Starting equipment, known spells, attributes, resources and skill ranks; Arcanist unlock flag |
| `Assets/Game/Progression/Data/Skills/` | Five skill definitions, XP costs, continuous benefit and perks at 25/50/75/100 |
| `Assets/Game/Items/Data/Adventure/` | Tiered swords, maces, shields and armor; Mana Potion; Fireball and Heal hotbar entries |
| `Assets/Game/Items/Data/Adventure/Adventure Loot.asset` | Floor eligibility, equipment/recovery rolls and level-weight adjustments |
| `Assets/Game/Combat/Spells/Fireball.asset`, `Heal.asset` | Mana, power, timing, radius, speed, sound and visual prefab references |
| `Assets/Game/Combat/Spells/Prefabs/` | Placeholder held spell, projectile and impact visuals |
| `Assets/Game/Characters/Animations/Spells/` | Left-arm ready and flick clips, layered over existing weapon animation |
| `Assets/Game/UI/Prefabs/Progression/` | Reusable skill rows and character panel styling |
| `Assets/Game/UI/Prefabs/Adventure/Table adventures.prefab` | Class-selection layout |
| `Assets/Game/Levels/Dungeon1/Dungeon1.asset` | 20 floors, current biome, ten rooms per floor, final boss and loot references |
| `Assets/Game/Progression/Room/Adventure Memorial Table.prefab` | Six recent memorial miniatures, plaques, light and three-button class puzzle |
| TablePlayer / AdventurerProgress | Perception light and its base visibility range |

The setup scripts were temporary and are removed after authoring. Importing or playing does not regenerate these assets. Keep existing item save IDs unchanged when tuning or renaming items.

## Gear baseline

Weapons use bronze/iron/steel/crystal ATK 5/6/7/11. Shields use AC 2/3/4/5. Leather/iron/steel/crystal torso armor uses AC 2/3/4/5; helmets, gloves and boots use AC 1/2/3/4. These core values follow the [official Barony weapons](https://barony.wiki.gg/wiki/Weapons) and [armor](https://barony.wiki.gg/wiki/Armor) tables. Existing art is reused for the new tier assets.

The initial overlapping floor bands are 1–6, 4–11, 9–18 and 16+. Higher character levels increase the relative weight of the stronger eligible tiers. These floor bands, stamina system, damage formula and XP pacing are LoomRoom prototype tuning, not a claim of exact Barony simulation. Condition, identification, curses and durability remain outside this version.

## Room and saves

Reaching floor 3 records the clue: “First defend. Then burn. Finally mend.” The room's sigils become available after the run; interact with the inscription to read the clue. Press Shield, Flame, Hand to unlock Arcanist. A wrong answer resets the sequence. The miniature table shows the six most recent deaths; all memorial records remain in the save.

`AdventureSave` writes `loomroom-adventure.json` in Unity's persistent data directory, with a backup file. It stores floor-entry inventory, equipment, class, skills, XP, health/mana/stamina, gold, food recovery, run blessings, run totals, story flags and memorials. Resuming regenerates that floor from its seed. Death/victory removes the checkpoint. Gold and equipment do not become room rewards.

## Verified

- Unity compilation with the installed editor.
- Warrior and Wizard initialization and derived resource totals.
- Selection of spells and shield through the same hotbar, preserving the right-hand sword.
- Fireball projectile collision, damage and offensive-magic training; Heal recovery, mana cost and defensive-magic training.
- Skill advancement preserves current health while updating maximum stats.
- Inventory pause and skill/perk display, inspected at 1280×720.
- Save JSON round-trip including empty slots and equipped spell; resume from disk; floor transitions.
- Layout generation for 20 floors × 3 seeds; actual final-floor generation with its boss milestone.
- Puzzle unavailable before its gate, wrong-order rejection and correct class unlock.
- Death records a memorial and clears the checkpoint.

## Still needs playtesting

This is a playable baseline using the existing biome and enemy roster. Spell visuals and hand motion are provisional. Full-run difficulty, mana supply, skill pacing, loot frequency and merchant prices need hands-on tuning. Additional biome art and enemy packs have deliberately not been added.


## Corrected Barony starting stats

Attribute order: STR / DEX / CON / INT / PER / CHR. These are the starting baselines from the official Barony class tables.

| Class | HP | MP | Attributes | Supported starting skills |
|---|---:|---:|---|---|
| Warrior | 30 | 20 | 1 / 1 / 0 / -2 / -1 / 1 | Melee 25, Blocking 25 |
| Wizard | 20 | 50 | 0 / -1 / 0 / 3 / 1 / -1 | Offensive 50, Defensive 15 |
| Cleric | 30 | 30 | 0 / -1 / 1 / 0 / 1 / 0 | Melee 25, Defensive 40, Blocking 10 |
| Arcanist | 25 | 40 | -1 / 1 / -1 / 1 / 1 / -1 | Offensive 30 |

Unlisted skills start at zero. Swords/Maces map to Melee; Sorcery to Offensive Magic; Thaumaturgy to Defensive Magic. Unsupported skills are not redistributed. Negative attributes remain negative, and CON/INT do not inflate starting HP/MP. The one-handed equipment, two spells, stamina, 25-point perks, attribute effects and level-growth sequence remain LoomRoom adaptations. Stable save IDs and the class unlock flag are preserved.

Sources: [Warrior](https://barony.wiki.gg/wiki/Warrior), [Wizard](https://barony.wiki.gg/wiki/Wizard), [Cleric](https://barony.wiki.gg/wiki/Cleric), [Arcanist](https://barony.wiki.gg/wiki/Arcanist).

UI palette: edit Assets/Game/UI/Sprites/Kit/Copper Plum.mat for panel plum, border shadow, copper and highlight colors. Shared kit sprites retain their original pixel layout; the material recolors their authored shading. Tab opens the character menu during dungeon play; Skills opens skill levels and hover details. Skill tooltips show only rank and current effect. The separate skill detail and spell panels have been removed.






Spell audio: each SpellDefinition now holds direct AudioClip references and individual 0–1 volumes for equip, charge, release, flight and impact. One-shots use AudioManager SFX; the moving flight loop uses AudioManager.PlaySFXLoop and the SFX mixer. Existing Fireball and Heal clips and volumes were migrated.

