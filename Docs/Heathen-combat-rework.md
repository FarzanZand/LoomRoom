# Heathen combat rework (plan)

Goal: table combat that feels like Heathen (gameplay overview trailer, youtu.be/gULKsVIPNlk from 0:39), everything except the shield bash. Measured by stepping the trailer frame by frame (30 fps, 480p) and comparing with our code and serialized values (CombatManager prefab, Room.unity overrides, TablePlayer and enemy prefabs). Nothing is changed yet.

## What we measured

| | Heathen | Ours now |
|---|---|---|
| Hit to next hit (spamming) | about 0.6 s | about 0.43 s (0.36 s plus hit stop) |
| Click to contact | about 0.2-0.25 s | about 0.29 s |
| Shape of a swing | windup about 0.2 s you can see, strike in 1-2 frames, arm held out about 0.3 s | windup 0.23 s, release clip 0.58 s with an even sweep, hitbox open 0.065 s |
| Swing directions | overhead chop, rising thrust, flat both ways, downward stab when looking down | two lights (A/B) and one heavy |
| Charged attack | none seen | hold 0.7 s for a heavy |
| Hit stop | none (blade passes through, the world keeps moving) | light 0.07 s at time scale 0.015, heavy 0.11 s, crit x2.5, backstab x3, also when the player is hurt, plus a victim shake |
| Where the hit shows | bright flash and blood burst at the contact point, number at that point | cream dust puff of 10 particles, whole-body white flash, number at the top of the head |
| Slash lines | thin white streaks along the arc for 2 frames | none |
| Crit | "CRITICAL!" over a big number that grows over 0.3 s | number x3.2, "n!" in orange, centre-screen "CRITICAL" line |
| Enemy reaction to any hit | head snaps back, steps back | bone recoil only on humanoids (20 deg), no flinch (0 s), push of about 0.06 m |
| Can a hit interrupt an enemy windup | the archer was knocked out of its stance | never |
| Kill | body launched metres up and back, or bursts into gore | skeletons split into bones (force 2.2), rat and slime play a death clip |
| Armour | helmet knocked off by a head hit | stays on |
| Blood | coloured per creature, sprays every hit, sticks to floor, walls and pillars for the whole floor, stains the blade | none (audio only) |
| Hits to kill | 1-3 | rat 2, archer 3, slime 3, soldier 5, warden 8 (light hits at 6.75) |
| Enemies per fight | 3-5 mixed: flyers, floor crawlers, archers, melee | usually 1-2, all on the ground |
| Distance in a fight | enemies right up against the camera | personalSpace 1.1 |
| Getting hit | deep red on all screen edges for about 0.4 s, camera jolt | red vignette alpha 0.22 for 0.28 s, camera impulse 2 |
| Enemy swing | axe raised overhead about 0.3 s, a yellow trail on the swing | windup clip only; the whoosh plays after the hit (bug, see Phase 6) |
| Block rhythm | shield up between every swing, attack drops it, strike lands 0.1 s later | attack is ignored while block is held |
| Crosshair | dot, small shield icon while blocking | |
| Smashed props | planks and hoops stay on the floor | debris pooled away after 3 s |

## Decisions to make first

These go against settled rules in CLAUDE.md and memory, so they need a yes before work starts:

1. **Barony pace** becomes Heathen pace (slower cadence, faster strike). The CLAUDE.md combat row and `Docs/CombatFeel.md` get rewritten.
2. **No stunlock rule** (light hits never flinch, never interrupt a swing) becomes "every hit flinches, a hit early in a windup interrupts it, at most once per enemy attack".
3. **Hit stop** goes away (or down to one frame on kills only).
4. **"Less is more" VFX** gives way to gore: blood, decals, bursts.
5. **The charged heavy** has no Heathen equivalent. Recommendation: keep it, since it costs stamina and is optional, but strip its hit stop and charge zoom down to match. Alternative: remove it and its tension layer.
6. **Art style**: keep the pixel look and take Heathen's colours and lighting, or drop the pixel look (still open from the first comparison). Blood decals and the blade stain must be made for whichever wins.
7. **Inventory**: Diablo-style rework (Phase 9) or keep ours. It's the largest piece and independent of the combat feel.

## Phase 1: tuning only (no new art, playable in an hour)

A quick test of the direction before any clip work. All values on `CombatManager` (prefab and the Room.unity instance) and the enemy bases.

- Hit stop off for melee: `hitStopEnabled` false, or light/heavy duration 0; `hitStopOnPlayerHurt` false; `DungeonDestructible.hitStopScale` 0. Victim shake goes with it.
- `lightFlinchDuration` 0 to about 0.25 s; the Hurt trigger also fires mid-windup during the first part of the windup (needs the Phase 4 interrupt rule; for the test, flinch only outside swings).
- `enemyKnockbackScale` 0.25 to about 1 (a step of 0.3-0.4 m per hit), still not while swinging.
- `personalSpace` 1.1 to about 0.75.
- Cadence: `playerAttackSpeed` 0.85 and the release curve set so that spammed hits land about 0.6 s apart.
- Hurt screen: `hurtScreenAlpha` 0.22 to about 0.6, `hurtScreenDuration` 0.28 to about 0.45, deeper red.
- Enemy health: roughly halve Soldier (24 to 14) and Warden (36 to 20), so most kills take 2-3 hits. Or raise weapon damage instead (cleaner with our flat armour rule); see Phase 6.

Play the Debug Dungeon, record, cut a frame sheet at 0.033 s like the Heathen sheets and compare.

## Phase 2: swing animations (hand-authored clips)

Clips stay `.anim` assets you can edit (`Characters/Animations/FirstPersonPlayer`); code only drives speed and selection.

- **New swing shape** for every light: windup about 0.18 s with the blade raised so you see it; strike in 2-3 frames at 60 fps; follow-through held about 0.25-0.3 s; return. A new click may cut into the follow-through after about 0.15 s, which gives the 0.6 s rhythm.
- **Four light swings** in a cycle instead of A/B: overhead diagonal chop (right to left), rising thrust from bottom right, flat right to left, flat left to right. `SwingIndex` goes 0-3; reset after `swingSequenceReset` 1.2 s as now.
- **Downward stab**, picked instead of the cycle when the camera looks down past about -35 deg, for rats and other floor enemies.
- Controller: `Attack_Release_C`, `Attack_Release_D`, `Attack_Release_Down`. To avoid touching every tag check, all lights keep the `AttackRelease` tag (B keeps `AttackReleaseAlt`), or `AnimatorHelper` gets one "any light release" check. The Release to Windup exit time moves from 0.36 to the start of the follow-through.
- Hitbox events: `EnableHitbox` / `DisableHitbox` span only the strike frames (about 0.04-0.05 s).
- `SwingCameraMotion`: one rotation per swing (array instead of light/alternate), small, following the swing direction (Heathen's camera barely moves on a hit).
- Shield (LeftArm): bigger and more central in the view while raised; block raise fast enough to come back right after a follow-through.
- Enemy clips: the humanoid attack (2.27 s clip, hit at 0.54 s after speed) gets a clear overhead raise of about 0.3 s before the hit.

## Phase 3: hit registration

- **Sweep resolution**: the blade now crosses its arc in 2-3 frames, so `weaponSweepSteps` 6 to about 12 so fast strikes don't skip a target.
- **Hitbox per weapon**: today one capsule (radius 0.4, about 0.65 m after the view-model scale) for every weapon. Give each weapon a blade segment (base and tip sockets on its world prefab) and sweep that, with a small radius. Knives short, maces heavy at the end, two-handers long.
- **Contact point**: `HitPoint` becomes the closest point between the blade segment and the victim's collider (today `ClosestPoint(queryCenter)`).
- **Direction**: `DamageInfo.Direction` becomes the blade's velocity at contact (today flat root to root), so blood sprays and bodies fly the way the swing went.
- **Floor hits**: the melee line-of-sight ray starts at the camera instead of the root plus 0.9, so a stab at a rat on the floor isn't blocked by the floor.
- Multi-hit stays as it is (one hit per target per swing).

## Phase 4: impact and enemy reactions

- **Contact flash**: replace `HitBurst` (cream puff) with a 1-2 frame bright white-orange flash billboard at the hit point.
- **Slash lines**: during the strike frames, draw 3-5 thin white lines along the blade's recent positions (from the sweep samples), gone after 2 frames.
- **Body flash**: Heathen shows none on the body; drop the whole-body `CharacterFX` flash or cut it to one frame (CLAUDE.md names it as the single hit flash, so update that line).
- **Damage numbers**: spawn at the hit point, not the head; white, our title font (`PIXEARG_ SDF`), slower rise. Crits get a "CRITICAL!" line above and a scale punch (small to big over 0.3 s). Drop the centre-screen result line.
- **Flinch and interrupt**: every unblocked hit plays a short hurt reaction (head snaps, half step back). A hit in the first part of a windup (before `enemyAttackCommitTime`) cancels it, at most once per enemy attack, so packs can still hit you. Heavy stagger and its immunity stay.
- **Bone recoil**: `HitReactionController` angle 20 to about 35, and moved onto the Enemy base so rats, slimes and new creatures get it too.
- **Armour pops**: a head hit may knock off helmets and other loose props (the attachments DeathBurst already detaches whole), as physics props that stay on the floor.
- **Kill launch**: the killing blow throws the body along the swing direction, metres not centimetres. Humanoids: ragdoll with a much bigger impulse (about 10-14 plus 4 up), or the skeleton bone burst at about 3-4x its force. Rat and slime stop playing a death clip and get the ragdoll or a gore burst.
- **Audio**: swing whoosh at the start of the strike; a wet slash layer on contact over the existing `Blood_Impact` clips; a crunch on kills.
- No camera kick on landed hits (Heathen has almost none; ours is already off because `PlayerFX.onHitImpulse` is empty).

## Phase 5: blood and gore

- **Blood per creature**: a `bloodColor` (and a burst style: blood, slime, bone dust) on `CharacterData`, set on each enemy's data. Skeletons spray bone dust, slimes green.
- **Hit spray**: a coloured burst of 30-60 droplets along the hit direction with gravity, on every hit. The kill burst is bigger, with a few gib chunks.
- **Decals that stay**: droplets that hit the world leave splats on floor, walls and pillars, kept for the whole floor (pool cap of about 200, cleared on the next floor). Our `LoomRoom/Pixel Lit` shader does not read URP's decal buffer, so either add DBuffer sampling to `PixelLitForward.hlsl` (both Pixel Lit shaders, same properties) or draw splats as small quads laid on the surface. Splat textures are pixel art at the dungeon texel density (`tableTexelsPerUnit` 10.7), point-filtered.
- **Blade stain**: each held weapon gets a stain amount and colour on its material instance (a property on Pixel Lit with a mask), raised by each hit, slowly fading.
- **Corpses** already stay for the floor (`corpseLifetime` 0); keep.
- **Breakables**: debris stops being pooled away after 3 s; pieces settle, turn kinematic and stay for the floor (with a cap).
- Lens splatter: skip unless we confirm Heathen has it.

## Phase 6: enemies and encounters

- **Kill speed**: target 1-3 light hits for normal enemies. Raise weapon damage (Bronze 5 to about 9), or lower enemy HP; enemy damage per hit comes down so packs aren't lethal. Retune `Dungeon balance.asset` floor scaling (+10% HP, +2 damage, +0.2 armour per floor) with it.
- **Packs**: 3-5 enemies per fight, mixed roles. Room profiles and biome spawn lists get pack sizes.
- **New roles**: a flyer (bat: needs a flying motor off the NavMesh, dives and retreats) and a small floor crawler (spider: low, fast, hit by the downward stab). Models through Blender or Meshy, concept approval first.
- **Enemy pressure**: shorter cooldowns for weak enemies; they come right up close (`personalSpace`).
- **Enemy swing trail**: a short bright streak on the enemy weapon during its hit window.
- **Fix**: the enemy whoosh plays after the hit (`enemyAttackCommitTime` 0.65 > humanoid hit at 0.54 s and rat at 0.3 s; `EnemyAttackRunner.cs:207-215`). It should play at the start of the strike.
- Cleanup: Bronze Mace's own hit particle is ignored because `useDefaultEffects` is on.

## Phase 7: player feedback and block

- **Getting hit**: deep red on all screen edges, held about 0.1 s and faded over about 0.4 s; camera jolt with a little roll toward the side the hit came from.
- **Attack from block**: holding block and clicking drops the shield, swings, and raises it again after the follow-through if block is still held (today the click is ignored while blocking).
- **Crosshair**: a dot; a small shield icon while blocking.
- Backpedal: `backward` x0.5 to about 0.8 so kiting backwards works like in the trailer.

## Phase 8: HUD and loot feedback (optional, Heathen-style)

- Pickup list on the right edge ("+20 Experience Points", "20 Gold", item names with icons), stacking and fading. The DM feed keeps the DM's lines.
- Level-up banner ("LEVEL 2" with what you gained) and floor title card when entering a floor.
- Minimap as a line drawing, top left, with the floor name (ours is code-built, top right).

## Phase 9: inventory (separate project)

What Heathen has that we don't, and what it costs here:

- **Multi-cell grid** (boots 1x2, spears 1x3): `Inventory` becomes a grid with a footprint per item (`ItemData.size`), placement and overlap rules, and new slot UI.
- **Per-instance items**: durability, identified or not, and later affixes need data on `ItemStack` (it was made a class for this). `Equipment` and the hand-in-hotbar link compare `ItemData` references today, so they must match instances instead. Save: the checkpoint gets per-instance fields (plain key and value), and `AdventureSave.Data.Current` bumps from 3.
- **Two weapon sets (I / II)** with a swap key; a **belt row** for potions and scrolls feeding the hotbar.
- **3D character preview** in the inventory, wearing your gear. The table player is arms only, so this needs a full-body player model. That same body would show your legs when you look down, as in Heathen.
- **Stat sheet** with per-stat tooltips (base, items, skills); `CharacterStats.Modifiers` already has the sources. STR/DEX/CON/INT exist but are not shown anywhere today.
- **Attribute points** (+5 per level, spent with + buttons) would replace the automatic class growth on level-up; skills can stay use-based (Barony) or become points.
- **Item tooltips**: type line ("Two-Handed Mace"), attack speed in words, durability, requirements in red when unmet, Shift to compare (ours always compares), Ctrl+click to drop.
- **Durability, repair and identify**: repair at the merchant; unidentified magic items and Identify scrolls; a Town Portal scroll would need a reason to exist in our room loop.

## Order and checks

1. Phase 1 tuning, play it, compare frame sheets. Stop here if the direction feels wrong.
2. Phase 2 clips and Phase 3 hit registration together (they depend on each other).
3. Phase 4 impact and reactions.
4. Phase 5 blood and decals (shader work, test with pixel lighting on and off).
5. Phase 6 balance, then new enemies.
6. Phase 7, then 8.
7. Phase 9 only if you decide to.

After each phase: update CLAUDE.md (combat row, hit feel line, Barony pace) and `Docs/CombatFeel.md`; play the Debug Dungeon; record a take and compare at 0.033 s per frame against the Heathen sheets.
