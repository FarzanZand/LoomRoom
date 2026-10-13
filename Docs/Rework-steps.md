# Rework: steps

Every change from `Combat-rework.md` and `Dungeon-rework.md`, cut into steps that can be built and tested on their own. Each step lists what changes, how to test it, and the questions to answer before it starts. **Rec** is my recommendation if you just want to say "go".

Test bed for everything unless noted: the Debug Dungeon (toolbar **Debug**), then a generated Dungeon1 floor (toolbar **Dungeon**). Each step that lands updates CLAUDE.md and the matching doc, so a rule we change (Barony pace, no stunlock, "less is more") is rewritten when its step lands, not before.

## Status (2026-10-12)

Most of categories 1 and 3-6 is in (1.3 is built but off by default), plus 7.3, 10.1, 11.2-11.4 and 13.6, mostly as recommended. The swing animations (category 2) were built and rejected; they stay off and are to be revisited. Lighting (8) and dungeon architecture (9) are not started. Not in the plan but added: breakables can hide a Crypt Rat that comes out alerted (`DungeonDestructible.ambushPrefab`, `ambushChance` 0.12). Every value is on `CombatManager` or the asset named, so each can be flipped back. Test with `Development/CombatTestBot.cs` (scripted input, frame captures to `Temp/CombatTest`).

## Before anything

- **Q0.1 Art direction.** Keep the pixel look and take the reference's colours, lighting and gore in that style, or move to the reference's smooth painted look? Rec: keep pixel. This decides how steps 6, 8, 9 and 11 are drawn.
- **Q0.2 How you want to test.** I build a step, play it, record and compare frame sheets with the reference frames, then hand it to you to play before the next step. Or you prefer several small steps per round? Rec: one step per round for categories 1-5, two or three per round after.

---

## 1. Combat tuning (values only, no new art)

Each is a value or a small code switch on `CombatManager`, the enemy bases or `PlayerCombat`, so each can be flipped back.

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 1.1 | Done: `hitStopEnabled` and `hitStopOnPlayerHurt` off | Hit stop off on every hit (light, heavy, crit, backstab, breakables) and when the player is hurt; the victim shake goes with it | Hit soldiers and barrels; the swing should pass straight through | Fully off, or a 1-frame stop on kills only? Rec: fully off. |
| 1.2 | Done: `lightFlinchDuration` 0.25 (never mid-swing) | Enemies flinch on every unblocked hit (short hurt reaction, about 0.25 s) | Hit a soldier between its attacks | Length of the flinch? Rec: 0.25 s. |
| 1.3 | Built, off by default: `hitsInterruptWindup`, `interruptWindow` 0.45 s; the attack after an interrupt can't be interrupted | A hit early in an enemy's windup cancels that attack, at most once per attack | Hit a soldier right as it raises its weapon | Allow interrupts at all? How early counts (first half of the windup)? Rec: yes, first half, once per attack. |
| 1.4 | Done: `enemyKnockbackScale` 0.25 to 1.4 | Bigger push per hit, about 0.3-0.4 m (today about 0.06 m), still none while the enemy swings | Hit a soldier three times in a row | Distance? Rec: 0.35 m. |
| 1.5 | Done: 0.75 | Fights up close: `personalSpace` 1.1 to about 0.75 | Let a soldier walk into you | OK to let enemies press that close? |
| 1.6 | Done: `swingInterval` 0.6 s between swing starts | Cadence about 0.6 s per hit, by timing only (new clips come in step 2) | Spam-click a soldier | Match the reference's 0.6 s, or keep our 0.43 s pace? Rec: try 0.6 s; you'll know in a minute of play. |
| 1.7 | Done: `hurtScreenAlpha` 0.6, `hurtScreenHold` 0.1, fade 0.4; the camera jolts away from the hit | Getting hit: deep red on all edges, held 0.1 s, faded over 0.4 s; camera jolt with a slight roll toward the hit | Let a soldier hit you | How strong? Rec: about 3x today's alpha. |
| 1.8 | Done: `attackFromBlock` on | Attack from block: clicking while blocking drops the shield, swings, raises it again if block is still held | Hold block, click | Re-raise automatically, yes or no? Rec: yes. |
| 1.9 | Done: `backwardSpeed` 0.8 | Walking backwards at 0.8 speed (today 0.5) | Back away from a soldier while swinging | OK? |
| 1.10 | Done: `CrosshairUI` | Crosshair: a dot, a small shield icon while blocking | Look around, block | Want a crosshair at all? Rec: yes, the dot is tiny. |

## 2. Swing animations (hand-made clips you can edit)

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 2.1 | Rejected, to revisit: baked as a forehand and backhand set (`FirstPersonPlayer/IK swings`, `IkSwingBaker`); the user didn't like them, `simpleSwings` stays on (original clips) | Re-time swing A to the reference's shape: windup you can see (about 0.18 s), strike in 2-3 frames, follow-through held about 0.3 s. Hitbox only on the strike frames | Swing at air and at a soldier, compare frame sheets | I do A first and you approve it before B, C, D copy the timing? Rec: yes. |
| 2.2 | Rejected with 2.1 | Swing B re-timed the same way (the rising thrust from bottom right) | As above | |
| 2.3 | Rejected with 2.1 | Swings C and D (flat right to left, flat left to right), four in a cycle | Spam-click and watch the cycle | Order? Rec: overhead, rising, flat right-left, flat left-right. |
| 2.4 | Not started | Downward stab when looking down past about 35 degrees | Stab a rat on the floor | Threshold angle? |
| 2.5 | Not started (kept as is, without hit stop) | Charged heavy | Hold and release | Keep as is, keep without hit stop and with less zoom, or remove? Rec: keep, toned down. |
| 2.6 | Not started | Shield raised bigger and more central | Hold block | How much of the screen? Rec: about a third, lower left. |
| 2.7 | Rejected with 2.1 (`ikSwingCameraRotation` only plays with the IK swings) | Swing camera motion per swing, small, following the swing direction | Swing | |

## 3. Hit registration (needed by step 2, no questions except 3.2)

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 3.1 | Done: 12 | Weapon sweep sampled twice as often (6 to 12 steps) so a 2-frame strike can't skip an enemy | Swing at a rat at the edge of reach | |
| 3.2 | Not started | Each weapon gets its own blade (base and tip points) instead of one shared 0.65 m capsule | Knife vs mace vs sword reach | Should reach differ per weapon? Rec: yes, small differences. |
| 3.3 | Done: `DamageInfo.SwingDirection` | The hit point is where the blade touches; the hit direction follows the swing (blood and kills fly that way) | Watch where hit effects appear | |
| 3.4 | Done | Floor hits: the line-of-sight check starts at the camera, so the floor doesn't block a stab | Stab a rat | |

## 4. Impact feedback

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 4.1 | Done: `Combat/Effects/Gore/Contact flash` | A 1-2 frame bright flash at the contact point, replacing the cream dust puff | Hit a soldier | Colour? Rec: white core, orange edge. |
| 4.2 | Done: `SlashLines` | Thin white slash lines along the arc for 2 frames | Swing | Want them in a pixel game? Rec: yes, try them. |
| 4.3 | Not started (`hitFlashEnabled` still on) | Whole-body white flash on hit | Hit a soldier | Remove, or cut to 1 frame? Rec: remove. |
| 4.4 | Done: white, `PIXEARG_`, rise 0.3 m/s | Damage numbers at the hit point, white, title font, slower rise | Hit a soldier | Keep numbers at all? Rec: yes. Title font (`PIXEARG_`) or body font? |
| 4.5 | Done: `resultLine` off | Crits: "CRITICAL!" above a number that grows over 0.3 s; the centre-screen result line goes | Land a crit (raise crit chance to test) | Remove the centre-screen line? Rec: yes. |
| 4.6 | Partly: kill sounds per creature (`Audio/Data/Kill bones`, `Kill flesh`, `Kill slime`); no new swing or slash sounds | Swing sound at the start of the strike, a wet slash layer on contact, a crunch on kills | Fight | Make new sounds, or pick from the Blood Guts Bones library? Rec: library first. |

## 5. Enemy reactions and death

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 5.1 | Done: 35 degrees; rats and slimes tip as a whole | Bigger bone recoil (20 to about 35 degrees), on every enemy (rats and slimes have none today) | Hit each enemy type | |
| 5.2 | Done: helmets only, 30%, -1 armour (`HelmetPop`) | A head hit can knock off helmets and other loose gear, which stays on the floor | Hit armoured soldiers in the head | How often? Does losing the helmet lower its armour? Rec: 30% per head hit, yes. |
| 5.3 | Done: ragdolls thrown by velocity (`ragdollImpulse` 5.5, upward 3); skeleton bone bursts stronger | The killing blow throws the body metres along the swing (humanoids) | Kill soldiers | Humanoids: ragdoll thrown, or the bone burst made stronger? Rec: ragdoll for soldiers, bone burst for skeletons. |
| 5.4 | Done: slimes burst, rats are thrown as ragdolls | Rats and slimes stop playing a death clip and get thrown too, or burst (with step 6) | Kill rats and slimes | Ragdoll or burst? Rec: burst. |
| 5.5 | Done, changed: the whoosh plays 0.14 s before the blow lands (`EnemyAttackRunner.HitAt`) | Enemy swing sound plays at the start of the swing (it plays after the hit today) | Let a soldier attack | (Bug fix, no question.) |
| 5.6 | Partly: pale yellow trail (`EnemySwingTrail`); no new overhead windup | Enemy attacks telegraph clearly: weapon raised overhead about 0.3 s, a short bright trail on the swing | Let a soldier attack | Trail colour? Rec: pale yellow. |

## 6. Blood and gore

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 6.1 | Done: `CharacterData.gore` (`GoreManager`) | Blood colour per creature (on its data) and a coloured spray on every hit | Hit each type | Colours? Rec: soldiers red, slimes green, skeletons bone dust, rats dark red. |
| 6.2 | Done: bigger spray; slimes burst into chunks | Kill burst: a bigger spray and a few chunks | Kill each type | How gory? Chunks, or spray only? |
| 6.3 | Done: cap 260 | Splats that stay on floor, walls and pillars for the whole floor (needs shader work in Pixel Lit) | Fight in a room, walk back later | Keep them for the whole floor? Rec: yes, with a cap of about 200. |
| 6.4 | Done: `BladeBlood`, `CombatManager.bladeBlood` (stains dry and fade over 45 s) | The blade gets stained with what it hit, slowly fading | Fight, look at the weapon | Fade over time, or only when you leave the floor? |
| 6.5 | Skipped | Lens splatter on screen | | Skip unless you want it? Rec: skip. |

## 7. Enemies and balance

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 7.1 | Partly: Crypt Warden health 36 to 21 | Normal enemies die in 1-3 light hits | Fight every type | Raise weapon damage (recommended, works better with our armour rule) or lower enemy health? |
| 7.2 | Partly: Crypt Soldier damage 8 to 6, Crypt Warden 11 to 8 | Enemy damage per hit retuned so groups aren't lethal; floor scaling retuned | Play floors 1-3 | |
| 7.3 | Done: `minEnemiesPerRoom`, Cellars and Crypt 2-5 per combat room | Groups of 3-5 mixed enemies per fight | Play a floor | How many per room? Rec: 2-5 by room size. |
| 7.4 | Done: Cave Bat with `FlyingEnemy` (cruises at 2.2 m, dives to bite, pulls back) | A flying enemy (bat): flies off the NavMesh, dives in, retreats | Fight it | Approve concept images first. A bat, or something else? |
| 7.5 | Not started | A small floor crawler (spider), killed with the downward stab | Fight it | Approve concepts. Spider, or rats as they are? |
| 7.6 | Not started | Weak enemies attack more often (shorter cooldowns) | Fight a group | |

## 8. Lighting

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 8.1 | Done: `LightingManager` light radius (6.5 m + 2.5 m edge, floor 0.3) | Light radius: ambient fades to black with distance from the player; torches and other lamps still light their surroundings; the apartment above the table is not affected. Replaces today's flat ambient cap | Walk a generated floor | Radius? Rec: about 5 m with a soft 2 m edge. |
| 8.2 | Partly: per-biome radius (`DungeonBiome.lightRadius`); no held torch item | The radius changes per biome, and a held torch or lantern widens it | Swap biomes, hold a torch | Do we want a held light item? |
| 8.3 | Done: `ScreenManager.colourSplit`, per-biome shadow and lamp tints | Colour split: cool teal ambient, warm orange lamps, per biome | Walk both biomes | How strong? Rec: moderate, compare with the reference frames. |
| 8.4 | Not started | Light shafts with dust under windows (after 9.x windows exist) | Stand in a window room | |

## 9. Dungeon architecture

Quick tests on the current generator first (9.1-9.3), then the move to hand-made chunks.

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 9.1 | Not started | New floor textures: irregular flagstones with dark grout, plus 2-3 floor colours per biome using the existing variant system | Walk a floor | Approve texture images first. |
| 9.2 | Not started | Wall textures with block relief (deep joints) at the same density | Walk a floor | Approve images. |
| 9.3 | Done: Dungeon1 50% 2-tile and 10% 3-tile rooms, 45% wide corridors | Taller rooms and wider corridors (more 6 m rooms, some 9 m; more 4 m corridors) | Walk a floor | How tall? Rec: half the rooms 6 m, 10% at 9 m. |
| 9.4 | Partly: kit made of `DungeonKitBlock`s (pillar, broken pillar, arch, niche, platform, galleries, rubble), no concept art | Keep kit concepts: pillars, arches, door frames, vaults, stairs, platforms, stained glass, niches, fireplace | Images only | Approve each piece's concept. |
| 9.5 | Not started | Kit pieces: Synty DarkFantasy restyled (walls, archways, windows, stairs, pillars, railings), Blender for the rest | Look at each piece in a test room | OK with that split? |
| 9.6 | Done: room templates with painted footprints are the chunks; `New Room Template` menu | Chunk format: a room prefab with door sockets, levels, light and prop sockets (grows out of `DungeonRoomTemplate`); one chunk built by hand and played through the Authored layout | Play the single chunk | |
| 9.7 | Partly: chunks join the Grown layout through per-biome pools (`DungeonBiome.roomTemplates`, 70% of rooms) | The Chunks layout style: chunks joined at their doors by a per-level graph | Generate several seeds | Replace Grown for Dungeon1 completely? Rec: yes. |
| 9.8 | Not started | Table reveal, minimap and NavMesh made chunk-aware | Generate a floor and watch the reveal | How should chunks appear on the table: dealt in one by one like the board levels? Rec: yes. |
| 9.9 | Partly: six chunks shared by Cellars and Crypt with their own weights | More chunks per biome, variation sockets, Cellars and Crypt with their own pools | Generate many seeds | How many chunks before it stops feeling repetitive? Rec: start at 8 per biome. |

## 10. Props and breakables

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 10.1 | Done: `debrisStays` (cap 220) | Breakable debris (planks, hoops, shards) stays for the whole floor | Smash barrels, walk back | |
| 10.2 | Done: `Prefabs/Groups` (barrel with candles, barrels huddled, crates and sacks, shelf with pots) | Prop groups: barrels with candles on top, crates with sacks, shelves with pots | Walk a floor | |
| 10.3 | Done: barrels 1.2x and warmer, crates 1.1x | Barrels and crates about 1.2x larger and warmer | Compare screenshots | |
| 10.4 | Done: groups hold breakables with loot (`SeedGroup`) | More breakables, placed in groups | Walk a floor | How many per room? Rec: 2-6. |
| 10.5 | Done: `Floor valuables` (skull, candle, bottle; worth gold) | Cheap pick-up loot standing on the floor (candelabra, goblet) | Pick some up | New items worth gold only? Rec: yes. |

## 11. Chests and loot

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 11.1 | Done: collected on touch, `PickupFeedUI` on the right edge | Gold collected on touch, shown in the pickup feed | Walk over gold | |
| 11.2 | Done: `LootBeam` (colours by kind: normal, Honed, consumable), `LootLabelsUI` within 4.5 m | Items on the floor: a light beam coloured by tier (gold for Honed) and a floating name label when near | Drop items | Beam colours per tier? Labels always, or only near (about 4 m)? Rec: only near. |
| 11.3 | Done: lid clip `Chest lid open.anim`, glow, stings per tier | Chest opening: the lid swings up (editable clip), gold glow inside, light burst, sound sting | Open a chest | |
| 11.4 | Done: `DungeonPickup.PopOut` | Loot pops out in arcs and lands around the chest | Open a chest | |
| 11.5 | Done: wood, iron (+1 roll), gold (+1 roll, one item from 3 floors deeper) | Chest tiers: wood, iron, gold trim, each with its own size and loot roll | Find all three | What each tier gives? Rec: wood = today's chest, iron +1 item, gold one rare item. |

## 12. HUD

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 12.1 | Not started | Pickup list on the right edge (XP, gold, items with icons), stacking and fading | Pick things up | Move pickups out of the DM feed? Rec: yes, the DM feed keeps the DM. |
| 12.2 | Not started | Level-up banner and a floor title card on arrival | Level up, go down stairs | Want both? |
| 12.3 | Not started | Minimap as a line drawing, top left, with the floor name | Walk a floor | Move it, or keep ours top right? |

## 13. Inventory (each step works on its own, in this order)

| # | Status | Change | Test | Questions |
|---|---|---|---|---|
| 13.1 | Not started | Stat sheet: STR, DEX, CON, INT shown, and every stat's tooltip breaks it down (base, items, skills) | Open the inventory | Which stats to show? |
| 13.2 | Partly: type line, damage and swing speed up front, comparison; no Shift-compare or Ctrl+click | Item tooltips: type line ("Two-Handed Mace"), attack speed in words, Shift to compare, Ctrl+click to drop | Hover items | Compare only with Shift  or always (ours)? |
| 13.3 | Not started | Attribute points: +N per level to spend, instead of the automatic class growth | Level up | Replace Barony's automatic growth? How many points per level? Skills stay use-based? |
| 13.4 | Not started | Each item becomes its own instance (needed for 13.5 and 13.8); the save format changes | Save and load a run | OK that old checkpoints are dropped (memorials and flags stay)? |
| 13.5 | Not started | Durability, and repair at the merchant | Fight until gear wears | Does broken gear break for good or stop working until repaired? |
| 13.6 | Done: 10 x 4 grid, paper doll (the inventory setup tool) | A grid bag where items take several cells | Fill the bag | Grid size? Rec: 10x4. |
| 13.7 | Not started | Two weapon sets (I and II) with a swap key, and a belt row for potions and scrolls | Swap sets in a fight | Key for the swap? |
| 13.8 | Not started | Unidentified magic items and Identify scrolls | Find and identify one | Want this system at all? |
| 13.9 | Not started | A full-body player model: shown in the inventory wearing your gear, and your legs show when you look down | Look down; open the inventory | Where does the model come from (Synty character restyled, Meshy, Blender)? |

---

## Suggested start

1.1 (hit stop off) and 8.1 (light radius) are the two smallest steps that change the feel the most, and they don't touch each other. Then the rest of category 1, then 2+3 together, then 4 and 5.
