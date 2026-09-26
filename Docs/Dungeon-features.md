# Dungeon features (September 2026 pass)

Everything below is authored data; code only reads it. Reload the dungeon to see edits.

## Floors, themes and bosses — `Assets/Game/Levels/Dungeon1/Dungeon1.asset`

- **Dungeon Floors**: 10 floors. Each entry has a **Theme**: Crypt (1–3), Catacombs (4–5), Flooded sewer (6–7), Mines (8–10). Resolution for anything both set: floor override > theme > level.
- **Themes** (`Levels/Dungeon1/Themes`): room/corridor styles, props, breakables, features, lighting mood and room-light tint, enemy pool, reward tables, ambience loop plus random distant one-shots, optional music, merchant chance, and the arrival line posted to the log. The arrival line only posts when the theme changes; otherwise the log says "You descend to floor N".
- **Milestones**: one entry per boss floor (3, 7, 10: Crypt Lord). Each has a boss prefab, an arena template, a title, an intro line, a sting, boss music, a reward table and bonus gold. **Seal Exit** keeps the stairs shut until the boss dies. The exit room is enlarged by **Arena Size Scale**. **Fill milestones every N floors** copies the first entry down the run.
- Boss: `Levels/Dungeon1/Enemies/Crypt Lord.prefab`, a variant of the Warden. It uses its own data and behaviour (waits Idle until the fight starts) and `Items/LootTables/Boss rewards.asset`.

## Hand-built rooms — `Levels/Dungeon1/Rooms/Templates`

A room profile's **Room Template** is optional; empty generates the room as before. The generator still builds walls, floor, ceiling and doors in the room's style, then places the template at the room centre.

- `DungeonRoomTemplate` on the root: minimum size, whether it replaces random props/enemies, and whether to keep the room light.
- Children with `DungeonTemplatePiece` are removed if they land on the centre cross, on a doorway approach, or outside a smaller room.
- `DungeonSocket` children place things. Types: Enemy, Chest, Breakable, Boss, Merchant, Feature. Each has a chance and an optional override prefab.
- **Shrine of Ashes** is the example: altar, fountain, pillars, braziers, pots and a merchant spot. It's used by `Rooms/Shrine.asset` (Rest role, weight .6). **Crypt Lord arena** is the boss room.

## Breakables and features

- `Prefabs/Props`: Breakable barrel, Breakable crate, Clay pot, Cobweb (`DungeonDestructible`). Cobwebs use **Corner** placement. The level's **Destructibles** list is weighted; **Destructibles Per Room** adds extras. Supply spots that aren't chests use a Floor breakable. Debris and puffs in `Props/Effects` are pooled.
- `Prefabs/Features`:
  - **Fountain**: sips roll weighted outcomes.
  - **Altar**: pay gold for a blessing. The price grows with depth.
  - **Grave**: dig for loot; the occupant may rise.
  - **Bookshelf**: a lore line, sometimes a find.
  - **Lever**: opens linked `DungeonDoor`s or toggles objects, with a UnityEvent for templates.
- Fountain and altar outcomes are `DungeonBoon` lists that reuse ordinary item effects. Stat buffs are tagged as run blessings, so duration 0 lasts until the run ends.
- Level/theme **Features** set a chance per eligible room role, a minimum floor and a per-floor cap.

## Gold and the merchant

- `Systems/CurrencyManager` holds the coin icon, pickup sound, and coin models by pile size (1+, 12+, 40+). It also sets touch-to-collect and the sell fraction.
- Gold lives on the player's `Wallet` (Table player) and resets each run.
- Loot tables have a **Gold** section, rolled after items so existing item results for a seed are unchanged. Items have a **Value** (price).
- `Characters/NPCs/Dungeon merchant.prefab` has staples plus rolled stock and a markup that grows with depth. The player can sell anything that isn't equipped. The level's **Merchant Prefab / Merchant Stock / Merchant Chance** (or theme/floor override) decide where one appears. The shop screen is `UI/Canvas/Shop`.

## Combat and enemies

- `CombatManager`:
  - Critical hits and backstabs: chance, multipliers, hit-stop, sounds (`Audio/Data/CombatCritical`, `CombatBackstab`) and number size. A backstab hits an unaware enemy, or one facing away.
  - **Death**: `ragdollDeath` (on), impulse, `lootableCorpses` (on), `corpseLifetime` (0 keeps bodies for the floor).
- Every enemy prefab has `EnemyRagdoll` and `EnemyVoice`. Voices and footsteps come from `AudioManager > Creature Library`, one entry per CharacterData plus a fallback: idle, alert, pain, death, footsteps, pitch, range. The alert bark plays when an enemy starts chasing; if the player can't see it, the log says "You hear something in the dark."

## Run, log, pause and settings

- `Systems/RunManager` tracks kills, gold, time, floor, seed, bosses and the killer. It runs the death moment (slow motion, and ScreenManager's **Death Effects** drain the colour) and the recap (`UI/Canvas/Run recap`), which is also used for victory. **New run** restarts the same level.
- `Systems/MessageLog` owns the history and colours and narrates combat and pickups. Items can set a **Use Message**. `UI/Canvas/Message log` has 8 authored rows.
- `Esc` opens `UI/Canvas/Pause menu` on either player (time stops). **Adventures** (Table only) opens the adventure menu that `Esc` used to open. Settings cover volume, sensitivity, FOV, pixelation and rebinding for 11 actions. Rebinds apply to the Room and Table maps together and persist in PlayerPrefs.
- Pixelation: `ScreenManager > Pixelation` has the master **Pixelation Enabled** toggle and a **Strength** multiplier on top of each player's Pixel Lines. The Table player now uses 320 lines.

## Pooling

`Systems/PoolManager` recycles hit/block particles, debris and effects (`PoolManager.SpawnOrInstantiate`). Add prefabs to **Prewarm** to avoid first-hit hitches; projectiles should use it too. Damage numbers were already pooled by `DungeonCombatFeedback`.

## Not included

Item identification (the altar blesses; Barony's identify needs unknown items first), enemy variants unique to each theme (themes reuse the three crypt enemies with different weights), and a hand-authored arena per boss floor (all three use the Crypt Lord arena).
