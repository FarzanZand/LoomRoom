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

Entrance is safe; the exit is guarded. Encounter Chance sets the chance of combat in other rooms; remaining rooms become treasure, rest or storage rooms. Max Enemies Per Room controls encounter size. Props live in `Dungeon1/Prefabs/Props`; keep their collider footprint inside one cell. Large custom content requires appropriately wider reserved paths and should be checked with navigation validation.

Enable multipleLevels and set levelCount for a run with descending stairs. floorSettings entry 0 is floor 1. Each entry selects mood lighting and optionally replaces encounter density, maximum enemies, enemy choices and reward tables. Empty references inherit level settings. Room reward overrides take precedence over floor rewards. A per-enemy overrideLevelTable takes precedence over both.

Descending preserves health, equipment and inventory. Starting equipment is granted on a new run only. The last floor has an exit. Generation failure restores the previous environment and floor number; the run is not discarded.

## Loot tables

Edit `Assets/Game/Items/LootTables`. Dungeon1 is wired to Enemy rewards, Chest rewards and Barrel rewards. Crypt supplies remains a compatible fallback. Create new tables with **Create > Table > Loot Table**.

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
