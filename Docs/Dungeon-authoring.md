# Dungeon authoring guide

Start with `Assets/Game/Levels/Dungeon1/Dungeon1.asset`. Levels appear in the adventure menu through the single catalog `Assets/Game/Levels/Resources/TableLevels.asset`. There is no asset-generation menu; create levels with **Create > Table > Level** and room profiles with **Create > Table > Room profile**, then add the level to the catalog.

## Layout and navigation

- Width/depth are grid cells; cellSize is world units. Room count is exact for supported dimensions. Each room needs a parcel at least 6 by 6 cells; capacity is floor((width-2)/6) * floor((depth-2)/6). Invalid settings fail with a clear Console message and preserve the current floor.
- The seeded partition layout builds a connected room graph. Loop Percent adds short extra connections. Corridors route around unrelated rooms rather than cutting through them. The farthest room by walking distance holds the exit.
- Room centres and doorway approaches are reserved for movement. Prop placement uses other cells. After furnishing, generation checks actual NavMesh paths to every room before accepting the floor. Closed doors are added afterwards as openable obstacles. Soldiers and wardens can operate gates while chasing or investigating; mites cannot. Add/remove DungeonDoorAccess on enemy prefabs to control this. Door opening sound is authored on the door prefab and plays through AudioManager.
- A fixed seed repeats a layout. Every subsequent floor derives a different repeatable seed. The Console logs seed, floor, room/connection counts and generation time.
- Roof/edge percentages select whole rooms/corridor sections, not tiles. Five percent of sections can cover more than five percent of area. Outside walls retain collision. The room-player overview intentionally hides all ceilings.
- Static architecture is combined into material groups in spatial chunks. Doors, containers, pickups and enemies remain independent objects.

## Rooms and floors

`Assets/Game/Levels/Dungeon1/Rooms` contains room profiles for guard chambers, treasuries, supply stores and quiet chambers. Profiles control props, their quantity, optional local lighting, enemy prefab choices and rewards. Add multiple profiles of the same role with different weights and floor ranges to introduce variation. A room without a matching profile inherits level settings.

Entrance is safe; the exit is guarded. The biome's Encounter Chance sets the chance of combat in other rooms; remaining rooms become treasure, rest or storage rooms. Its Max Enemies Per Room controls encounter size. Props live in `Dungeon1/Prefabs/Props`; keep their collider footprint inside one cell. Large custom content requires appropriately wider reserved paths and should be checked with navigation validation.

## Biomes and the run plan

A level's **Run** tab lists its **Biomes** in order with a floor count each; the floor count of the run is their sum. Dungeon1 is Cellars (3 floors) then Crypt (17). A **Dungeon Biome** asset (`Create > LoomRoom > Dungeon Biome`, kept in `Levels/Dungeon1/Biomes`) holds everything that makes a stretch of the run feel different:

- **Look**: room and corridor styles, room shapes, props and furnishing rules, breakables, lighting and room-light tint. Empty lists use the level's.
- **Population**: enemies with a weight and a **From floor** (counted from the biome's first floor), an encounter chance that ramps from the biome's first floor to its last, max enemies per room, and features (their Min Floor also counts from the biome's first floor).
- **Rewards**: the biome's **Loot** and merchant chance.
- **Guardian**: waits in the exit room of the biome's last floor and seals the stairs until killed. Health and damage multipliers let an ordinary enemy serve as a guardian.
- **Audio**: ambience, distant one-shots, music.

A room profile's own enemies or rewards replace the biome's for that room. A per-enemy overrideLevelTable takes precedence over both. The entry line posts when the run enters a new biome.

Descending preserves health, equipment and inventory. Starting equipment is granted on a new run only. The last floor has an exit. Generation failure restores the previous environment and floor number; the run is not discarded.

## Loot

Everyday loot is a **Loot Profile** (`Create > LoomRoom > Loot Profile`, one per biome next to the biome asset). It does not list items. It sets a gear tier range, an upgrade chance, gold growth per floor, and for each source (enemies, breakables, chests/bookshelves/graves) the chance of gold, one piece of gear, one recovery item and one tome. Items are picked from the item catalog by **Tier** (1 Bronze/Leather, 2 Iron, 3 Steel, 4 Crystal, 0 never random) and **Loot Weight**, both on the ItemData. Across a biome the low tier is most common on its first floor and the high tier on its last; the upgrade roll gives one tier above. A new item joins the loot just by getting a tier.

Hand-listed **Loot Tables** (`Assets/Game/Items/LootTables`, `Create > Table > Loot Table`) are for special cases: Guardian rewards, Merchant stock, room profile rewards. Anything that takes loot accepts either kind. Old tables are in `LootTables/_Archive`. The rules below apply to hand-listed tables.

- Enemy Drop Chance is the probability of any table reward on death. Carried gear drops independently. Chest/barrel/floor rewards do not use this enemy gate.
- Guaranteed entries award their quantities when their floor/source restrictions match. For enemies they still obey the overall enemy chance gate.
- A pool first checks its chance, chooses a number of rolls between Min/Max Rolls, then makes a weighted selection per roll. Weights are relative: weights 1 and 3 mean approximately 25% and 75% among eligible entries. Weight zero excludes an entry.
- Entries have quantity ranges, source flags and floor bounds. Max Floor zero means unlimited. Weight Per Floor changes relative frequency deeper into a run.
- Unique Items prevents selecting the same item twice within one pool. Separate pools can deliberately award the same item; results merge into quantities.
- Max Items Per Reward is a safety cap across all pools, with guaranteed entries first. Rolls are bounded. Empty/ineligible pools produce no reward.
- The Inspector's **Simulate 10,000 rewards** button reports empty-reward frequency, how often each item appears and average quantities. It uses a local seed and never spawns or edits inventory.
- World drops split quantities into valid item stacks and scatter on nearby navigation without crossing its edges. A source can reward only once, including repeated interaction or damage calls.
- Pickup routing still uses ItemData.directToHotbar (off by default), with inventory fallback. Item definitions remain under Items/Data; pickup models remain under Items/Prefabs/Pickups.

Current Dungeon1 defaults use the Dungeon enemy/chest/barrel progression tables: enemies have a 45% reward chance (about 11% gear per kill), chests award equipment plus a 60% supply chance, and breakables have a 35% supply and 4% gear chance. Iron gear appears from floor 2. Earlier reward tables remain optional alternatives. See `Dungeon-inventory-and-balance.md` for the complete values.

## UI styling

The inventory is live: opening it releases the cursor and disables player movement/attacks, but does not pause enemies, enemy hitboxes, regeneration or the simulation clock. `GameState.Inventory` separates player input from simulation. Closing it restores the previous state; no inventory code changes `Time.timeScale`.

`Assets/Game/UI/Prefabs/Dungeon` contains Enemy health bar, Damage number and Dungeon message. Edit sizes, fonts, colours, images and child layout there; level data holds the prefab references. Scripts update content, visibility and health fill. Full inventory/menu screens remain scene-authored. Inventory and hotbar slots are connected to shared prefabs in `Assets/Game/UI/Prefabs/Items`. Dungeon-specific slot colours, font sizes and frame artwork are editable on the ItemSlotUI prefab component.

## Verification and limits

There is no automated validation helper in the project. At runtime, generation itself checks NavMesh paths from the entrance to every room and rejects the floor if any is unreachable; a rejected or failed load restores the previous environment and floor number, logs the exception and reopens the adventure menu. Everything else (door behaviour, loot, multi-floor runs) needs a manual play-mode check.

This is a usable dungeon/content system, not a promise that every authored content combination is safe. New large props/enemy types need playtesting. Save/resume across application restarts, locked-door/key puzzles, quests, boss logic and hand-built room geometry are not included. Generation is synchronous behind the fade; the Console timings help identify when to move it to staged generation for larger maps.

BGM volume: on the level data, set **Background Music Volume** from 0 (silent) to 1 (full). It controls the track gain during the AudioManager crossfade and still respects the Music/Master mixer volumes. Existing levels default to 1. Reload the level to apply edits.

NPC interaction ownership: set **Interaction Player** on NpcBrain to Room for apartment NPCs, or Table for tabletop NPCs. The same restriction controls both selection prompts and executing an interaction.

The dungeon no longer spawns a loose healing item at the entrance. Starter inventory and rest-container supplies are configured independently.

Door frequency is evaluated once per connected corridor passage, with at most one door at one of its narrow room entrances. Bends/branches do not add another door; remaining entrances are open. **Door Percent = 0** leaves all passages open, while 100 gives each eligible passage one door. Reload to regenerate.

Audio tuning: AudioManager **UI Library** owns the inventory open/close, hover, equip and unavailable clips/volumes. Nearby spatial SFX remain full volume within **Sfx Min Distance** (3.5 by default), then attenuate toward **Sfx Max Distance**. Active-player footsteps play in 2D through SFX with player-prefab **Footstep Volume** at .85; world sound positions and AI hearing remain independent. Containers expose **Open Volume** (1 by default). These changes leave BGM settings intact.

## Grown layouts, room shapes and variety

**Layout Style** (Layout tab) picks the generator. *Partition* is the original one-rectangle-per-slice layout and still reproduces old seeds. *Grown* (the default) packs shaped rooms outwards from the entrance: each room is placed a few cells off one already on the map and tunnelled to it, and the exit room grows off the room furthest from the entrance. Grown settings: Room Size (cells), Room Spacing (rock between neighbours; 1 often gives a doorway straight through the wall), Corridor Wander, Wide Corridor %, Dead End % (spurs, some ending in a breakable or chest) and Loop Reach. Rooms that do not fit are dropped with a Console warning.

**Room shapes** are a weighted list on the level, replaceable per theme and per room profile: Rectangle, Cut corners, L, T, Cross, Pillar hall, Ring, Room in room, Cave, or Authored. Pillars and inner walls are solid cells inside the room, walled like any other wall.

**Painting your own room:** Create > Table > Room Shape. Set width and height (up to 24), then paint with the brushes: Floor, Pillar (solid), Door (floor cells a corridor may attach to; with none painted, corridors attach anywhere), Centre (lights, stairs, enemies, template pivot) and Erase (outside the room). Add it to a shape list as kind Authored. Examples are in `Dungeon1/Rooms/Shapes`. A Room Template can use a painted shape as its **Footprint**; its pivot sits on the Centre cell and it turns with the room (templates are never mirrored).

**Wall and floor variety** (Room Style): every wall tile and floor cell draws its material from the style's own material (Base Weight, default 10) and the weighted Wall/Floor Variants. Variants can be limited to the first wall tile or the tiles above it. Styles can also hang optional wall decorations and repeat Corridor Dressing (e.g. the mine supports) along straight corridors.

**Furnishing** (Rooms tab, also on themes and profiles) places props by rule, scaled by floor area: Against Wall, Corner, Centre or Anywhere, with a footprint in cells, props per 10 cells, a per-room clamp, a chance and the room roles it applies to. Props are authored facing +Z into the room with the pivot at the middle of the footprint; ready-made ones are in `Dungeon1/Prefabs/Furnishing`. Walkways from doorways to the room centre are always kept clear. Features (fountains, altars, graves, bookshelves, levers) have the same **Placement** choice; Against Wall and Corner slide the object back until it touches the wall, whatever its pivot. Anywhere keeps the old behaviour (any free cell, facing the room centre).
