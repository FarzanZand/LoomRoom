# Heathen rework: steps

Every change from `Heathen-combat-rework.md` and `Heathen-dungeon-rework.md`, cut into steps that can be built and tested on their own. Each step lists what changes, how to test it, and the questions to answer before it starts. **Rec** is my recommendation if you just want to say "go".

Test bed for everything unless noted: the Debug Dungeon (toolbar **Debug**), then a generated Dungeon1 floor (toolbar **Dungeon**). Each step that lands updates CLAUDE.md and the matching doc, so a rule we change (Barony pace, no stunlock, "less is more") is rewritten when its step lands, not before.

## Before anything

- **Q0.1 Art direction.** Keep the pixel look and take Heathen's colours, lighting and gore in that style, or move to Heathen's smooth painted look? Rec: keep pixel. This decides how steps 6, 8, 9 and 11 are drawn.
- **Q0.2 How you want to test.** I build a step, play it, record and compare frame sheets with the Heathen frames, then hand it to you to play before the next step. Or you prefer several small steps per round? Rec: one step per round for categories 1-5, two or three per round after.

---

## 1. Combat tuning (values only, no new art)

Each is a value or a small code switch on `CombatManager`, the enemy bases or `PlayerCombat`, so each can be flipped back.

| # | Change | Test | Questions |
|---|---|---|---|
| 1.1 | Hit stop off on every hit (light, heavy, crit, backstab, breakables) and when the player is hurt; the victim shake goes with it | Hit soldiers and barrels; the swing should pass straight through | Fully off, or a 1-frame stop on kills only? Rec: fully off. |
| 1.2 | Enemies flinch on every unblocked hit (short hurt reaction, about 0.25 s) | Hit a soldier between its attacks | Length of the flinch? Rec: 0.25 s. |
| 1.3 | A hit early in an enemy's windup cancels that attack, at most once per attack | Hit a soldier right as it raises its weapon | Allow interrupts at all? How early counts (first half of the windup)? Rec: yes, first half, once per attack. |
| 1.4 | Bigger push per hit, about 0.3-0.4 m (today about 0.06 m), still none while the enemy swings | Hit a soldier three times in a row | Distance? Rec: 0.35 m. |
| 1.5 | Fights up close: `personalSpace` 1.1 to about 0.75 | Let a soldier walk into you | OK to let enemies press that close? |
| 1.6 | Cadence about 0.6 s per hit, by timing only (new clips come in step 2) | Spam-click a soldier | Match Heathen's 0.6 s, or keep our 0.43 s pace? Rec: try 0.6 s; you'll know in a minute of play. |
| 1.7 | Getting hit: deep red on all edges, held 0.1 s, faded over 0.4 s; camera jolt with a slight roll toward the hit | Let a soldier hit you | How strong? Rec: about 3x today's alpha. |
| 1.8 | Attack from block: clicking while blocking drops the shield, swings, raises it again if block is still held | Hold block, click | Re-raise automatically, yes or no? Rec: yes. |
| 1.9 | Walking backwards at 0.8 speed (today 0.5) | Back away from a soldier while swinging | OK? |
| 1.10 | Crosshair: a dot, a small shield icon while blocking | Look around, block | Want a crosshair at all? Rec: yes, the dot is tiny. |

## 2. Swing animations (hand-made clips you can edit)

| # | Change | Test | Questions |
|---|---|---|---|
| 2.1 | Re-time swing A to Heathen's shape: windup you can see (about 0.18 s), strike in 2-3 frames, follow-through held about 0.3 s. Hitbox only on the strike frames | Swing at air and at a soldier, compare frame sheets | I do A first and you approve it before B, C, D copy the timing? Rec: yes. |
| 2.2 | Swing B re-timed the same way (the rising thrust from bottom right) | As above | |
| 2.3 | Swings C and D (flat right to left, flat left to right), four in a cycle | Spam-click and watch the cycle | Order? Rec: overhead, rising, flat right-left, flat left-right. |
| 2.4 | Downward stab when looking down past about 35 degrees | Stab a rat on the floor | Threshold angle? |
| 2.5 | Charged heavy | Hold and release | Keep as is, keep without hit stop and with less zoom, or remove? Rec: keep, toned down. |
| 2.6 | Shield raised bigger and more central | Hold block | How much of the screen? Rec: about a third, lower left. |
| 2.7 | Swing camera motion per swing, small, following the swing direction | Swing | |

## 3. Hit registration (needed by step 2, no questions except 3.2)

| # | Change | Test | Questions |
|---|---|---|---|
| 3.1 | Weapon sweep sampled twice as often (6 to 12 steps) so a 2-frame strike can't skip an enemy | Swing at a rat at the edge of reach | |
| 3.2 | Each weapon gets its own blade (base and tip points) instead of one shared 0.65 m capsule | Knife vs mace vs sword reach | Should reach differ per weapon? Rec: yes, small differences. |
| 3.3 | The hit point is where the blade touches; the hit direction follows the swing (blood and kills fly that way) | Watch where hit effects appear | |
| 3.4 | Floor hits: the line-of-sight check starts at the camera, so the floor doesn't block a stab | Stab a rat | |

## 4. Impact feedback

| # | Change | Test | Questions |
|---|---|---|---|
| 4.1 | A 1-2 frame bright flash at the contact point, replacing the cream dust puff | Hit a soldier | Colour? Rec: white core, orange edge. |
| 4.2 | Thin white slash lines along the arc for 2 frames | Swing | Want them in a pixel game? Rec: yes, try them. |
| 4.3 | Whole-body white flash on hit | Hit a soldier | Remove, or cut to 1 frame? Rec: remove. |
| 4.4 | Damage numbers at the hit point, white, title font, slower rise | Hit a soldier | Keep numbers at all? Rec: yes. Title font (`PIXEARG_`) or body font? |
| 4.5 | Crits: "CRITICAL!" above a number that grows over 0.3 s; the centre-screen result line goes | Land a crit (raise crit chance to test) | Remove the centre-screen line? Rec: yes. |
| 4.6 | Swing sound at the start of the strike, a wet slash layer on contact, a crunch on kills | Fight | Make new sounds, or pick from the Blood Guts Bones library? Rec: library first. |

## 5. Enemy reactions and death

| # | Change | Test | Questions |
|---|---|---|---|
| 5.1 | Bigger bone recoil (20 to about 35 degrees), on every enemy (rats and slimes have none today) | Hit each enemy type | |
| 5.2 | A head hit can knock off helmets and other loose gear, which stays on the floor | Hit armoured soldiers in the head | How often? Does losing the helmet lower its armour? Rec: 30% per head hit, yes. |
| 5.3 | The killing blow throws the body metres along the swing (humanoids) | Kill soldiers | Humanoids: ragdoll thrown, or the bone burst made stronger? Rec: ragdoll for soldiers, bone burst for skeletons. |
| 5.4 | Rats and slimes stop playing a death clip and get thrown too, or burst (with step 6) | Kill rats and slimes | Ragdoll or burst? Rec: burst. |
| 5.5 | Enemy swing sound plays at the start of the swing (it plays after the hit today) | Let a soldier attack | (Bug fix, no question.) |
| 5.6 | Enemy attacks telegraph clearly: weapon raised overhead about 0.3 s, a short bright trail on the swing | Let a soldier attack | Trail colour? Rec: pale yellow. |

## 6. Blood and gore

| # | Change | Test | Questions |
|---|---|---|---|
| 6.1 | Blood colour per creature (on its data) and a coloured spray on every hit | Hit each type | Colours? Rec: soldiers red, slimes green, skeletons bone dust, rats dark red. |
| 6.2 | Kill burst: a bigger spray and a few chunks | Kill each type | How gory? Chunks, or spray only? |
| 6.3 | Splats that stay on floor, walls and pillars for the whole floor (needs shader work in Pixel Lit) | Fight in a room, walk back later | Keep them for the whole floor? Rec: yes, with a cap of about 200. |
| 6.4 | The blade gets stained with what it hit, slowly fading | Fight, look at the weapon | Fade over time, or only when you leave the floor? |
| 6.5 | Lens splatter on screen | | Skip unless you want it? Rec: skip. |

## 7. Enemies and balance

| # | Change | Test | Questions |
|---|---|---|---|
| 7.1 | Normal enemies die in 1-3 light hits | Fight every type | Raise weapon damage (recommended, works better with our armour rule) or lower enemy health? |
| 7.2 | Enemy damage per hit retuned so groups aren't lethal; floor scaling retuned | Play floors 1-3 | |
| 7.3 | Groups of 3-5 mixed enemies per fight | Play a floor | How many per room? Rec: 2-5 by room size. |
| 7.4 | A flying enemy (bat): flies off the NavMesh, dives in, retreats | Fight it | Approve concept images first. A bat, or something else? |
| 7.5 | A small floor crawler (spider), killed with the downward stab | Fight it | Approve concepts. Spider, or rats as they are? |
| 7.6 | Weak enemies attack more often (shorter cooldowns) | Fight a group | |

## 8. Lighting

| # | Change | Test | Questions |
|---|---|---|---|
| 8.1 | Light radius: ambient fades to black with distance from the player; torches and other lamps still light their surroundings; the apartment above the table is not affected. Replaces today's flat ambient cap | Walk a generated floor | Radius? Rec: about 5 m with a soft 2 m edge. |
| 8.2 | The radius changes per biome, and a held torch or lantern widens it | Swap biomes, hold a torch | Do we want a held light item? |
| 8.3 | Colour split: cool teal ambient, warm orange lamps, per biome | Walk both biomes | How strong? Rec: moderate, compare with Heathen frames. |
| 8.4 | Light shafts with dust under windows (after 9.x windows exist) | Stand in a window room | |

## 9. Dungeon architecture

Quick tests on the current generator first (9.1-9.3), then the move to hand-made chunks.

| # | Change | Test | Questions |
|---|---|---|---|
| 9.1 | New floor textures: irregular flagstones with dark grout, plus 2-3 floor colours per biome using the existing variant system | Walk a floor | Approve texture images first. |
| 9.2 | Wall textures with block relief (deep joints) at the same density | Walk a floor | Approve images. |
| 9.3 | Taller rooms and wider corridors (more 6 m rooms, some 9 m; more 4 m corridors) | Walk a floor | How tall? Rec: half the rooms 6 m, 10% at 9 m. |
| 9.4 | Keep kit concepts: pillars, arches, door frames, vaults, stairs, platforms, stained glass, niches, fireplace | Images only | Approve each piece's concept. |
| 9.5 | Kit pieces: Synty DarkFantasy restyled (walls, archways, windows, stairs, pillars, railings), Blender for the rest | Look at each piece in a test room | OK with that split? |
| 9.6 | Chunk format: a room prefab with door sockets, levels, light and prop sockets (grows out of `DungeonRoomTemplate`); one chunk built by hand and played through the Authored layout | Play the single chunk | |
| 9.7 | The Chunks layout style: chunks joined at their doors by a per-level graph | Generate several seeds | Replace Grown for Dungeon1 completely? Rec: yes. |
| 9.8 | Table reveal, minimap and NavMesh made chunk-aware | Generate a floor and watch the reveal | How should chunks appear on the table: dealt in one by one like the board levels? Rec: yes. |
| 9.9 | More chunks per biome, variation sockets, Cellars and Crypt with their own pools | Generate many seeds | How many chunks before it stops feeling repetitive? Rec: start at 8 per biome. |

## 10. Props and breakables

| # | Change | Test | Questions |
|---|---|---|---|
| 10.1 | Breakable debris (planks, hoops, shards) stays for the whole floor | Smash barrels, walk back | |
| 10.2 | Prop groups: barrels with candles on top, crates with sacks, shelves with pots | Walk a floor | |
| 10.3 | Barrels and crates about 1.2x larger and warmer | Compare screenshots | |
| 10.4 | More breakables, placed in groups | Walk a floor | How many per room? Rec: 2-6. |
| 10.5 | Cheap pick-up loot standing on the floor (candelabra, goblet) | Pick some up | New items worth gold only? Rec: yes. |

## 11. Chests and loot

| # | Change | Test | Questions |
|---|---|---|---|
| 11.1 | Gold collected on touch, shown in the pickup feed | Walk over gold | |
| 11.2 | Items on the floor: a light beam coloured by tier (gold for Honed) and a floating name label when near | Drop items | Beam colours per tier? Labels always, or only near (about 4 m)? Rec: only near. |
| 11.3 | Chest opening: the lid swings up (editable clip), gold glow inside, light burst, sound sting | Open a chest | |
| 11.4 | Loot pops out in arcs and lands around the chest | Open a chest | |
| 11.5 | Chest tiers: wood, iron, gold trim, each with its own size and loot roll | Find all three | What each tier gives? Rec: wood = today's chest, iron +1 item, gold one rare item. |

## 12. HUD

| # | Change | Test | Questions |
|---|---|---|---|
| 12.1 | Pickup list on the right edge (XP, gold, items with icons), stacking and fading | Pick things up | Move pickups out of the DM feed? Rec: yes, the DM feed keeps the DM. |
| 12.2 | Level-up banner and a floor title card on arrival | Level up, go down stairs | Want both? |
| 12.3 | Minimap as a line drawing, top left, with the floor name | Walk a floor | Move it, or keep ours top right? |

## 13. Inventory (each step works on its own, in this order)

| # | Change | Test | Questions |
|---|---|---|---|
| 13.1 | Stat sheet: STR, DEX, CON, INT shown, and every stat's tooltip breaks it down (base, items, skills) | Open the inventory | Which stats to show? |
| 13.2 | Item tooltips: type line ("Two-Handed Mace"), attack speed in words, Shift to compare, Ctrl+click to drop | Hover items | Compare only with Shift (Heathen) or always (ours)? |
| 13.3 | Attribute points: +N per level to spend, instead of the automatic class growth | Level up | Replace Barony's automatic growth? How many points per level? Skills stay use-based? |
| 13.4 | Each item becomes its own instance (needed for 13.5 and 13.8); the save format changes | Save and load a run | OK that old checkpoints are dropped (memorials and flags stay)? |
| 13.5 | Durability, and repair at the merchant | Fight until gear wears | Does broken gear break for good or stop working until repaired? |
| 13.6 | A grid bag where items take several cells | Fill the bag | Grid size? Rec: 10x4. |
| 13.7 | Two weapon sets (I and II) with a swap key, and a belt row for potions and scrolls | Swap sets in a fight | Key for the swap? |
| 13.8 | Unidentified magic items and Identify scrolls | Find and identify one | Want this system at all? |
| 13.9 | A full-body player model: shown in the inventory wearing your gear, and your legs show when you look down | Look down; open the inventory | Where does the model come from (Synty character restyled, Meshy, Blender)? |

---

## Suggested start

1.1 (hit stop off) and 8.1 (light radius) are the two smallest steps that change the feel the most, and they don't touch each other. Then the rest of category 1, then 2+3 together, then 4 and 5.
