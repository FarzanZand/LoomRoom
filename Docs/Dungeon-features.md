# Dungeon features

Everything below is authored data; code only reads it. Reload the dungeon to see edits. Paths are under `Assets/Game`.

## Biomes and guardians: `Levels/Dungeon1/Dungeon1.asset`

- **Run** tab: Cellars (floors 1 to 3), Crypt (4 to 20). See [Dungeon authoring](Dungeon-authoring.md#biomes-and-the-run-plan) for what a biome holds.
- **Biomes** (`Levels/Dungeon1/Biomes`): Dungeon1 has two, Cellars and Crypt, each with its own loot profile next to it (`Cellars loot`, `Crypt loot`). Room styles and their materials are in `Dungeon1/Styles`. The arrival line only posts when the biome changes; otherwise the log says "You descend to floor N".
- **Guardians**: each biome's Guardian tab. Cellars: a Crypt Warden at x1.8 health and x1.2 damage, with `Items/LootTables/Guardian rewards`. Crypt: the Crypt Lord in the `Crypt Lord arena` template, with an intro sting and the `Crypt Lord loot` profile. **Seal Exit** keeps the stairs shut until the guardian dies; **Arena Size Scale** enlarges the exit room.
- Boss: `Characters/Enemies/Crypt/Crypt Lord.prefab`, a variant of the Crypt Warden with its own data and behaviour (waits idle until the fight starts).

## Hand-built rooms: `Levels/Dungeon1/Rooms/Templates`

Templates: `Starting room` (the level's `startingRoom`), `Merchant shop` (the level's **Shop Room**), `Shrine of Ashes` and `Crypt Lord arena`. A room profile's **Room Template** is optional; empty generates the room as usual. The generator still builds walls, floor, ceiling and doors in the room's style, then places the template at the room centre.

- `DungeonRoomTemplate` on the root: footprint (a Room Shape asset or painted in place), minimum size, style, height, **Open Ceiling**, whether it replaces random props and enemies, and lighting (**Replace Generated Torches**, **Override Light Color**).
- Children with `DungeonTemplatePiece` are removed if they would block a walkway or fall outside a smaller room.
- `DungeonSocket` children place things. Types: Enemy, Chest, Breakable, Boss, Merchant, Feature, Wall Light. Each has a chance and an optional override prefab. A Wall Light socket sits on a floor cell by a wall and points at it; it snaps to the wall face and uses the biome's (or level's) lighting prefabs unless it has its own prefab or colour.
- **Shrine of Ashes** is the example: altar, fountain, pillars, braziers, pots and a merchant spot. It is used by `Rooms/Profiles/Shrine.asset` (Rest role, weight 0.6), the only room profile Dungeon1 has.

## Breakables and features

- `Levels/Dungeon1/Prefabs/Props`: Breakable barrel, Breakable crate, Clay pot, Cobweb (`DungeonDestructible`). Cobwebs use **Corner** placement. The level's **Destructibles** list is weighted; **Destructibles Per Room** adds extras. Supply spots that are not chests use a floor breakable. Debris and puffs in `Props/Effects` are pooled.
- `Levels/Dungeon1/Prefabs/Features`:
  - **Fountain**: sips roll weighted outcomes.
  - **Altar**: pay gold for a blessing. The price grows with depth.
  - **Grave**: dig for loot; the occupant may rise.
  - **Bookshelf**: a lore line, sometimes a find.
- Fountain and altar outcomes are `DungeonBoon` lists that reuse ordinary item effects. Stat buffs are tagged as run blessings, so duration 0 lasts until the run ends.
- Level and biome **Features** set a chance per room, the room roles allowed, a placement, a minimum floor and a per-floor cap.

## Gold and the merchant

- `CurrencyManager` (under Systems) holds the coin icon, pickup sound and coin models by pile size (1+, 12+, 40+). It also sets touch-to-collect and the sell fraction.
- Gold lives on the Table player's `Wallet` and resets each run.
- Loot profiles and loot tables both roll gold. Items have a **Value** (price).
- `Characters/NPCs/Dungeon merchant.prefab` offers 2 consumables (from its staples and its stock) and 5 pieces of gear, with a markup that grows with depth. Sold items are not restocked. The player can sell anything that is not equipped. The level's **Merchant Prefab**, **Stock** and **Shop Room**, and the biome's **Merchant Chance**, decide where one appears. The shop screen is the scene's `Shop` object under the UI canvas.

## Combat and enemies

- `CombatManager`:
  - Critical hits and backstabs: chance, multipliers, hit-stop, sounds (`Audio/Data/CombatCritical`, `CombatBackstab`) and number size. A backstab hits an unaware enemy, or one facing away.
  - **Death**: `ragdollDeath` (on), impulse, `lootableCorpses` (off on the prefab: loot scatters where the enemy dies), `corpseLifetime` (0 keeps bodies for the floor).
- How an enemy dies: `EnemyRagdoll` sits on `Enemy base`, so every enemy collapses as a ragdoll when `ragdollDeath` is on. Crypt Archer, Crypt Soldier and Crypt Warden (and so the Crypt Lord) also carry `DeathBurst`, which takes over: the body bursts into its own bones.
- **Enemies are one prefab each** (`Characters/Enemies/Crypt/*.prefab`), variants of `Characters/Enemies/Base`:
  - `Enemy base`: every enemy component (brain, perception, motor, stats, loot, voice, ragdoll, and `EnemyFX`: knockback force their hits deal, 3 by default; Crypt Rat and Green Slime override it to 1.5).
  - `Humanoid enemy base` (a variant of it): adds the Synty skeleton rig, weapon loadout, door use and `HitReactionController` (struck bones recoil). Each humanoid switches on its own body mesh and helmet.
  - Crypt Archer, Crypt Soldier and Crypt Warden are variants of the humanoid base; the Crypt Lord is a variant of the Warden; Crypt Rat and Green Slime are variants of `Enemy base` with their own bodies.
  - `Town/Village Brute`: the Town level's enemy (The Village), a variant of the humanoid base.
- Each enemy's `EnemyData` is stored **inside its prefab**, shown on the Character component: Stats, Enemy (behaviour, attacks), Audio, Animation.
- An optional shared `EnemyBehaviourProfile` replaces the inline behaviour when several enemies must behave alike.
- **New enemy**: `Tools > LoomRoom > New Enemy` (NPCs: `New NPC`), or right-click an enemy or NPC prefab > Create > LoomRoom > Character Variant. Pick a base or an existing enemy as the template. It creates the variant and embeds a copy of the template's data. Add it to a biome's or the level's enemy pool.
- The weapon preview button on `EnemyWeaponLoadout` is editor-only and never saved into the prefab; the weapon spawns at runtime.
- **Hit flash**: one system for every character, in `CharacterFX` (tint and duration on `CombatManager > Hit Flash`).

## Run, log, pause and settings

- `RunManager` tracks kills, gold, time, floor, seed, bosses and the killer. It runs the death moment (slow motion, and ScreenManager's **Death Effects** drain the colour) and the recap (`Run recap`), which is also used for victory. The recap offers "Wake up".
- `MessageLog` owns the history and colours and narrates combat and pickups. Items can set a **Use Message**.
- `Esc` opens the `Pause menu` on either player (time stops). **Adventures** (Table only) opens the adventure menu. Settings cover volume, sensitivity, FOV, pixelation and key rebinding. Rebinds apply to the Room and Table maps together and persist in PlayerPrefs.
- Pixelation: `ScreenManager` has the **Retro pixel grid** (Pixelation Enabled, Strength) and the separate **Pixelator** for the 3D pixel-art look (see CLAUDE.md).

## Pooling

`PoolManager` recycles hit and block particles, debris and effects (`PoolManager.SpawnOrInstantiate`). Add prefabs to **Prewarm** to avoid first-hit hitches. Damage numbers are pooled by `DungeonCombatFeedback`.

## Not included

Item identification (the altar blesses; Barony's identify needs unknown items first).
