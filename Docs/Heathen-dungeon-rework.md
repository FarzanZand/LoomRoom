# Heathen dungeon rework (plan)

Goal: dungeon floors that look like Heathen's keep (gameplay overview trailer, youtu.be/gULKsVIPNlk). Based on trailer frames, two facts about how Heathen is made (from you: hand-composed 3D chunks stitched by a node graph, and a light radius around the player with darkness outside it) and a map of our generator (Dungeon1 values). Companion to `Heathen-combat-rework.md`. Nothing is changed yet.

## What Heathen's keep looks like

**Architecture**
- Tall rooms, often two or three storeys, with barrel-vaulted ceilings over corridors and halls.
- Walls of big stone blocks modelled with real depth (bevels, deep joints), so torchlight catches every block edge. A carved skirting band and trims run along the walls.
- Thick pillars everywhere: round red-brown columns on square plinths framing doorways, rows of square stone pillars in halls (arcades), octagonal bases.
- Doorways are built features: columns either side, an arched or carved lintel, one or two steps up or down at the threshold.
- Height changes inside a floor: long staircases down into a lower room, raised platforms with steps along hall sides, landings you look down from.
- Corridors are wide (about 3-4 m) and short, opening into big rooms.
- Floors are irregular flagstones with dark grout, not a square grid; some rooms have their own floor colour (teal-green squares in one hall).
- Each big room has a lit focal point on the far wall: a carved gate, a fireplace or shrine, a stained-glass window.

**Light**
- Pitch black outside a small radius around the player.
- Light sources still show from far away: stained-glass windows glowing with a god ray and dust across the room, candles in wall niches, braziers, lit alcoves.
- Strong warm against cool: torchlight orange-gold, stone teal-blue where lit.

**Props**
- Chunky, oversized and saturated: barrels about chest high in bright orange wood, clustered in groups of 3-4, candles stood on top.
- Built-in furniture: shelving and racks along walls, filled with items; wall niches with candles; a stone coffin under a window.
- Barrels and crates burst into planks and iron hoops that stay on the floor.
- Pick-up props: candelabras and similar loot stand on the floor.

**Chests**
- Big, gold-trimmed, a focal object (often in a pool of window light). Smaller silver-trimmed ones too, so there are tiers.
- Opening: the lid swings up, the inside glows gold, light spills out.
- Loot pops out in arcs and lands on the floor around the chest. Each item gets a vertical light beam (colour by rarity) and a floating name label ("Greaves"). Gold goes straight to the pickup feed.

## What our generator does now

- **Everything is built from boxes.** Floor slabs 2x2 m per cell, flat ceiling boxes, walls one 2x3 m box per cell face and tile row, 0.22 m thick, never varying. A cornice and footing 0.04 m proud are the only relief. No arches, columns, door frames or vaults; a doorway is a gap plus the door prefab.
- **Floors are always flat.** Ceilings are 3, 6 or 9 m per room (25% of rooms are 6 m, none 9 m). No stairs inside rooms, no platforms, pits, balconies or vaults. The only stairs are the exit stairwell and entrance ladder.
- **Layout** is Grown: 30 rooms of 8-18 m, corridors 2 m wide (15% widened to 4 m on straights), loops 15%. Room shapes include PillarHall, Chapel and Niched hall, but their "pillars" are solid 2x2 m wall cells.
- **Templates** exist (`DungeonRoomTemplate`): painted footprint, height, light sockets, Prefab Mode preview. Four are used: Starting room, Shrine of Ashes, Crypt Lord arena, Merchant shop.
- **Props** are placed one by one (wall, corner, centre, anywhere), with no groups. Breakables are 1-2 per room and their debris vanishes after 3 s.
- **Lighting**: 1-2 wall torches in 70% of rooms, ambient capped at 0.11, black fog off. No light shafts, stained glass or volumetrics.
- **Biomes** differ only by torch colour.
- **Materials**: 32x32 pixel textures, one repeat per 3 m (10.7 texels per unit). The style variant system and the wall decor and corridor dressing passes exist but have no data.
- **Chests**: a Supply chest; loot spills as `WorldItem`s picked up with Interact ("Pick up X"); no beams, labels or pop-out.

Assets we already have: Synty PolygonDarkFantasy has a modular building kit (walls, archways, huge windows, stairs including half and quarter flights, 32 pillars, railings, ceilings, chains, candles, shelves, rubble, doors). It has no vaults, stained glass, wall niches, fireplaces or coffins. Every Synty piece can go through `PixelRestyle` like the props.

## What changes

### 1. Generator: from boxes to hand-made chunks

This is the main change, and your note about Heathen confirms it: the look comes from hand-composed pieces, not from a generator that builds walls.

- **New layout style `Chunks`** (appended to the layout enum; Grown and Authored stay, so IntroDungeon and older levels keep working).
- **A chunk is a prefab of one room or corridor piece**, built by hand: floor, walls, ceiling, pillars, stairs, platforms, windows, its lights, prop sockets, enemy spawn points and loot spots. It grows out of `DungeonRoomTemplate`: footprint on the 2 m cell grid (for the minimap, spawning and overlap checks), plus **door sockets** on its edges (position, width, which level).
- **Stitching**: the generator places chunks so their door sockets meet, rejects overlaps, and fills gaps with corridor chunks. Seeds stay stable through `WeightedPick`.
- **The node graph**: a level asset lists the sequence and branches (entrance, a few combat halls, a treasure room, a side branch, the exit), with each node naming a chunk pool by role and size. Start as an Odin list on the level asset; a visual node editor can come later.
- **Chunk pools per biome**, so Cellars and Crypt finally look different.
- **Variation inside a chunk**: optional sockets (prop groups, extra pillars, a window or a wall) that roll per seed, so the same chunk doesn't repeat exactly.
- **What has to come along**:
  - the NavMesh build already in `DungeonGenerator` has to cover stairs and platforms;
  - the table assembly reveal (`TableLevelReveal`) animates generated walls and ceilings, so it needs a chunk-aware mode (deal in chunk by chunk, like `BoardReveal` deals tiles);
  - the minimap reads cells, so chunks fill their cells, with a level per cell for stairs;
  - the dungeon must still fit on the table (80 x 108 units today).

### 2. A modular keep kit

The chunks are assembled from a kit, so the look is set once:

- Thick walls with modelled stone blocks, two depths so walls step in and out; carved skirting and trims.
- Round columns on square plinths, square pillars, octagonal bases.
- Door frames: columns, arches and carved lintels, with one- and two-step thresholds.
- Barrel-vault and ribbed-vault ceilings, plus flat timber ceilings for low rooms.
- Stairs (stone and timber, straight and turning), platforms with edges and railings, balconies.
- Stained-glass windows (emissive glass in the pixel style), wall niches with candles, a fireplace or shrine as a focal piece, a stone coffin.
- Irregular flagstone floors with dark grout, plus a few floor colours per biome.

Source: Synty DarkFantasy for walls, archways, windows, stairs, pillars and railings (restyled with `PixelRestyle`); Blender for vaults, niches, fireplaces, stained glass and block-relief walls (concept images for approval first). Textures stay at the dungeon density (10.7 texels per unit), so the pixel look holds.

### 3. Lighting: the light radius

- **Player light radius in the shader**: `PixelLitForward.hlsl` (both Pixel Lit shaders, identical properties) multiplies ambient light by a falloff from the player's position. A global position, radius and softness are set each frame. Inside the radius you see the room's ambient; outside it everything is black.
- **Lamps are not faded**: torches, windows, candles and braziers still light their surroundings, so you see lit pools far off in the dark, as in Heathen. This replaces today's flat ambient cap (`darkAmbient` 0.11) and keeps the black distance fog off (fog would also hide distant torches).
- **The room above the table is not affected**: the shader skips rendering layer 8 (Room), as the darkness does today.
- **Radius per biome and per item**: a torch or lantern in hand could widen it; darker biomes shrink it.
- **Light shafts**: a god-ray card plus slow dust particles under each window (additive, pixel-dithered), and window light pools on the floor.
- **Colour split**: biome lighting presets with cool ambient (teal-blue) and warm lamps (orange-gold).
- Torch placement moves into the chunks (sockets), with `PlaceTorches` kept for Grown levels.

### 4. Props

- **Prop groups**: prefabs of 3-5 props arranged together (barrels with candles on top, crates with a sack, a shelf with pots), placed through chunk sockets or a new group rule in `DungeonPropRule`.
- **Scale and colour**: barrels and crates about 1.2x larger and warmer; review the restyled Synty props against the Heathen frames.
- **Built-in furniture**: wall shelving and racks filled with items, niches with candles, a coffin under a window, all part of the chunks.
- **Breakables**: more per room (in groups), debris stays for the floor (planks and hoops; also in the combat plan).
- **Floor props you can pick up** (candelabra, goblet) as cheap loot.

### 5. Chests and loot drops

- **Chest tiers**: wood, iron and gold-trimmed, each a different size and loot roll (Synty has five FantasyKingdom chests and the DarkFantasy and Generic ones to restyle).
- **Opening**: an animated lid (a clip you can edit), a gold glow from inside, a burst of light, and a short sound sting.
- **Pop-out**: items launch in arcs from the chest and land around it, rather than appearing at its foot.
- **Items on the floor**: a vertical light beam coloured by tier (and gold for Honed), and a floating name label when you're near. Gold is collected on touch into the pickup feed (`CoinPickup.collectOnTouch` exists).
- **Placement**: treasure chunks stage the chest as a focal object (window light, a raised dais).

## Order and checks

1. **Light radius first**: it's a shader change, works with today's generator, and you'll see at once whether the darkness is right.
2. **Chest opening, pop-out loot, item beams and labels**: no generator change needed.
3. **Kit concepts** (images for approval), then the first kit pieces.
4. **Chunk layout style** with a small set: 3 halls, 2 corridors, 1 treasure room, the exit. Test it on Dungeon1 Cellars.
5. **Reveal, minimap and NavMesh** support for chunks.
6. More chunks per biome; prop groups; light shafts.

After each step: play the Debug Dungeon and a generated Dungeon1 floor, take screenshots at the same spots, and compare them with the Heathen frames.

## Decisions to make first

1. **Chunks replace Grown for Dungeon1** (Grown stays in the code for other levels), or chunks only for some rooms inside a Grown layout. Recommendation: full chunks, since that is where the look comes from.
2. **Art direction**: the kit keeps the pixel look (my assumption above), or follows Heathen's painted look.
3. **Light radius size**: small and Diablo-tight (about 4-5 m), or larger.
4. **Where kit pieces come from**: Synty restyled where it fits, Blender for the rest (recommended), or all custom.
