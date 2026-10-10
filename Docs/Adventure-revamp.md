# Adventure revamp

Dungeon1 runs 20 floors: Cellars (3) then Crypt (17). Three starting classes (Warrior, Wizard, Cleric) and one class unlocked in the room (Arcanist) share the same weapons and two spells (Fireball, Heal). The room keeps class unlocks, memorials and story flags, not gold or gear.

## Playing

- Pick a class figure at the table, then the run starts in Dungeon1.
- There is **one hotbar**. Its numbered slots hold weapons, shields, spells and consumables. Drag items and spells between slots in the inventory.
- Select Fireball's or Heal's slot to hold it in the left hand; right click casts. Selecting the shield slot brings blocking back. The right-hand weapon stays equipped.
- Tab opens the character menu. **Equipment** holds the inventory; **Skills & Spells** lists the eight skills (Swords, Maces, Blocking, Sorcery, Thaumaturgy, Athletics, Trading, Stealth) with their rank and current effect.
- Skills train on use: hits and kills with a weapon, blocked hostile attacks, casting, healing real damage, sneaking, buying and selling. Air swings and healing at full health give nothing.
- A run ends in death or victory. Resuming a saved run starts at the beginning of the saved floor.

## Designer assets

Paths are under `Assets/Game`.

| Asset | What to edit |
|---|---|
| `Progression/Data/Adventure Rules.asset` | Level XP, training limits, attribute and growth tuning, starting memorials |
| `Progression/Data/Classes/` | Warrior, Wizard, Cleric, Arcanist: starting equipment, known spells, attributes, resources, skill ranks, unlock flag |
| `Progression/Data/Skills/` | One asset per skill: XP per action, rank cost, the rank 100 bonus |
| `Items/Data/{Weapons,Armor,Shields,Consumables,Trinkets}` | Tiered gear, potions and food |
| `Levels/Dungeon1/Biomes/* loot.asset` | Loot profiles: gear tier range, upgrade chance, gold and drop chances per source |
| `Combat/Spells/Fireball/`, `Combat/Spells/Heal/` | Spell data, hotbar tome, hand and impact effects, projectile |
| `Characters/Animations/Spells/` | Left-arm ready, charge and release clips |
| `UI/Prefabs/Progression/` | Skill rows, the character skills panel, Skill XP Popup, Stealth Eye |
| `Levels/Dungeon1/Dungeon1.asset` | Run (biomes and floor counts), layout, rooms, styles |
| `Progression/Room/Adventure Memorial Table.prefab` | Memorial figures, plaques, light and the class puzzle |

Keep item save IDs unchanged when tuning or renaming items.

## Gear baseline

Tiers are 1 Leather/Bronze, 2 Iron, 3 Steel, 4 Crystal. Swords deal 5/7/9/12, maces 6/8/10/13. Shields and body armour give 2/3/4/5 armour; helms, gloves, boots and leggings 1/2/3/4. Armour works as in Barony: it comes off 75% of a physical hit and the rest always lands (`CombatManager.armorEffectiveness`).

Each biome's loot profile sets which tiers drop: the low tier is most common on the biome's first floor and the high tier on its last. Condition, identification, curses and durability are not in the game.

## Room and saves

Reaching floor 3 for the first time finds the clue "Shield, flame, hand." (`AdventureRoomPuzzle`). From then on the puzzle is in the room; pressing the tiles in that order unlocks Arcanist. A wrong press resets the sequence. The memorial table shows recent deaths; every memorial stays in the save.

`AdventureSave` writes `loomroom-adventure.json` in Unity's persistent data folder, with a `.bak`. A checkpoint holds the floor-entry inventory, equipment, class, skills, XP, health, mana, stamina, gold, food regeneration, run blessings and run totals; memorials and story flags are kept separately. Resuming regenerates the floor from its seed. Death or victory clears the checkpoint.

## Classes

Attribute order: STR / DEX / CON / INT / PER / CHR. Health, mana and attributes follow the Barony class tables; skill ranks are LoomRoom's.

| Class | HP | MP | Attributes | Starting skills |
|---|---:|---:|---|---|
| Warrior | 30 | 20 | 1 / 1 / 0 / -2 / -1 / 1 | Swords 25, Maces 15, Blocking 25, Athletics 10 |
| Wizard | 20 | 50 | 0 / -1 / 0 / 3 / 1 / -1 | Swords 5, Sorcery 50, Thaumaturgy 15 |
| Cleric | 30 | 30 | 0 / -1 / 1 / 0 / 1 / 0 | Maces 25, Blocking 10, Thaumaturgy 40, Trading 10 |
| Arcanist | 25 | 40 | -1 / 1 / -1 / 1 / 1 / -1 | Swords 20, Sorcery 30, Athletics 10 |

Sources: [Warrior](https://barony.wiki.gg/wiki/Warrior), [Wizard](https://barony.wiki.gg/wiki/Wizard), [Cleric](https://barony.wiki.gg/wiki/Cleric), [Arcanist](https://barony.wiki.gg/wiki/Arcanist). The class assets are the source of truth; check them if this table and the game disagree.

## UI and audio

- Kit art follows `UIManager`'s Theme colours through `UI/Sprites/Kit/Kit Palette.mat`.
- Each `SpellDefinition` holds its own clips and volumes for equip, charge, release, flight and impact. One-shots play through AudioManager SFX; the flight loop uses `AudioManager.PlaySFXLoop`.

## Skill XP and notifications

Each skill asset lists the actions that train it and the XP each gives, the XP for the first rank and a cost multiplier that reaches its full value at rank 100. Overflow carries into the next rank. Partial XP is saved in floor checkpoints and reset on a new run.

`UI/Prefabs/Progression/Skill XP Popup.prefab` is the top-centre icon, progress ring and rank-up display. `ExperienceBarUI` (the top XP bar) sets animation, hold and fade times. Awards for the same skill merge; a rank gain fills the ring, then shows the old rank counting up to the new one.

`UI/Prefabs/Progression/Stealth Eye.prefab` shows below the screen centre while crouching: a closed lid when unseen, an open amber eye when a living enemy sees the player. `StealthIndicatorUI` sets colours and timing.
