# Dungeon authoring guide

Start with `Assets/Game/Levels/Dungeon1/Dungeon1.asset`. Levels appear in the adventure menu through the catalog `Assets/Game/Levels/Resources/TableLevels.asset`. Create levels with **Create > Table > Level** and room profiles with **Create > Table > Room profile**, then add the level to the catalog.

## Layout styles

**Layout Style** (Layout tab) picks how a floor is built:

- **Grown** (the default, used by Dungeon1) packs shaped rooms outwards from the entrance. Each room is placed a few cells off one already on the map and tunnelled to it; the exit room is placed last, off the room furthest from the entrance. Settings: Room Count, Room Size (cells), Room Spacing (rock between neighbours; 1 often gives a doorway straight through the wall), Corridor Wander, Wide Corridor %, Dead End % (spurs, some ending in a breakable or chest), Loop % and Loop Reach. Rooms that do not fit are dropped with a Console warning, so Room Count is a target, not a promise.
- **Authored** reads a floor plan painted on the level asset (the IntroDungeon).
- **Partition** is retired. A level still set to it is built as Grown, with a Console warning asking you to switch it.

## Navigation and doors

- Room centres and doorway approaches are kept clear for movement; props go on other cells. After furnishing, generation checks a NavMesh path to every room and rejects the floor if one is unreachable. A rejected or failed load restores the previous environment and floor number, logs the exception and reopens the adventure menu.
- Closed doors are added afterwards as openable obstacles. Enemies with `DungeonDoorAccess` (the humanoid base has it) can open doors while chasing or investigating. The door sound is on the door prefab (`Levels/Dungeon1/Doors/Dungeon door.prefab`).
- **Door Percent** is rolled once per connected corridor passage, with at most one door at one of its room entrances. 0 leaves every passage open; 100 gives each one a door. In Grown layouts the starting room (floor 1 and the first floor of each biome) always gets a door on its one doorway.
- A fixed seed repeats a layout. Each later floor derives its own repeatable seed. The Console logs seed, floor, room and connection counts and generation time.
- Static architecture is combined into material groups in spatial chunks. Doors, containers, pickups and enemies stay separate objects.

## Ceilings and openings

Ceilings are built in the table reveal (after the walls) and stay visible, from inside the dungeon and from the room. **Hide Roof** opens skylights in a percentage of whole rooms and corridor sections; a ceiling frame stays round each opening. **Hide Edges** puts windows in outer walls; the full wall stays as collision. A room template with **Open Ceiling** gets a near-full skylight (the starting room's way in).

## Rooms and floors

`Assets/Game/Levels/Dungeon1/Rooms` holds `Profiles` (room profiles), `Shapes` (painted room shapes) and `Templates` (hand-built rooms). Dungeon1 has one profile, `Shrine` (Rest role, using the Shrine of Ashes template). A profile can force a style, scale its room, give it a template or shapes, and replace props, enemies and rewards for its rooms. Several profiles for the same role, with weights and floor ranges, add variety. A room without a matching profile uses the level and biome settings.

The entrance is safe; the exit is guarded. The biome's Encounter Chance sets the chance of combat in other rooms; the rest become treasure, rest or storage rooms. Max Enemies Per Room sets encounter size. Props live in `Dungeon1/Prefabs/Props`; keep their collider footprint inside one cell.

Descending keeps health, equipment and inventory. Starting equipment is given on a new run only. Generation failure restores the previous environment and floor number; the run is not lost.

## Biomes and the run plan

A level's **Run** tab lists its **Biomes** in order with a floor count each; the run's length is their sum. Dungeon1 is Cellars (3 floors) then Crypt (17). A **Dungeon Biome** asset (`Create > LoomRoom > Dungeon Biome`, kept in `Levels/Dungeon1/Biomes`) holds what makes a stretch of the run feel different:

**Level or biome?** The level holds what every biome shares; a biome holds what makes it different. Lists (styles, room shapes, props, furnishing, breakables, features) combine: the level's plus the biome's. Single settings (lighting, music) change only when the biome sets one. A room profile's own lists replace both for its rooms.

- **Look**: extra room and corridor styles, room shapes, props and furnishing rules, breakables, wall lights and optional lighting.
- **Population**: enemies with a weight and a **From floor** (counted from the biome's first floor), an encounter chance that ramps from the biome's first floor to its last, max enemies per room, and features (their Min Floor also counts from the biome's first floor).
- **Rewards**: the biome's **Loot** (a loot profile) and merchant chance.
- **Guardian**: waits in the exit room of the biome's last floor and seals the stairs until killed. Health and damage multipliers let an ordinary enemy serve as a guardian.
- **Audio**: ambience, distant one-shots, music.

A room profile's own enemies or rewards replace the biome's for that room. An enemy's own loot table wins over both when its `DungeonLootDrop` has **Override Level Table** on. The entry line posts when the run enters a new biome.

## Loot

Everyday loot is a **Loot Profile** (`Create > LoomRoom > Loot Profile`, one per biome next to the biome asset: `Cellars loot`, `Crypt loot`, plus `Crypt Lord loot` for the boss). It does not list items. It sets a gear tier range, an upgrade chance, gold growth per floor, and for each source (enemies, breakables, chests, bookshelves and graves) the chance of gold, one piece of gear, one recovery item and one tome. Items are picked from the item catalog by **Tier** (1 Bronze/Leather, 2 Iron, 3 Steel, 4 Crystal, 0 never random) and **Loot Weight**, both on the ItemData. Across a biome the low tier is most common on its first floor and the high tier on its last; the upgrade roll gives one tier above. A new item joins the loot just by getting a tier.

Hand-listed **Loot Tables** (`Assets/Game/Items/LootTables`, `Create > Table > Loot Table`) are for special cases: `Guardian rewards` (the Cellars guardian), `Merchant stock`, and `Crypt supplies` (the default table on the crypt enemies' loot drop, replaced by the floor's loot unless Override Level Table is on). Anything that takes loot accepts either kind. Rules for hand-listed tables:

- Enemy Drop Chance is the chance of any item reward on death. Chest, breakable and floor rewards do not use this gate. Gold rolls separately.
- Guaranteed entries give their quantities when their floor and source match. For enemies they still obey the enemy chance.
- A pool checks its chance, picks a number of rolls between Min and Max Rolls, then makes a weighted pick per roll. Weights are relative: 1 and 3 mean about 25% and 75%. Weight zero excludes an entry.
- Entries have quantity ranges, source flags and floor bounds. Max Floor zero means no limit. Weight Per Floor changes how common an entry is deeper in.
- Unique Items stops one pool picking the same item twice. Separate pools can give the same item; results merge into quantities.
- Max Items Per Reward caps the total, guaranteed entries first.
- The Inspector's **Simulate 10,000 rewards** button reports empty rewards, how often each item appears and average quantities. It never spawns anything.

World drops split quantities into valid stacks and scatter on nearby navigation. A source rewards only once. Pickup routing uses `ItemData.directToHotbar` (off by default), falling back to the bag. Pickup models are under `Items/Prefabs/Pickups`.

## Room shapes, furnishing and variety

**Room shapes** are a weighted list on the level; a biome's add to it and a room profile's replace it: Rectangle, Cut corners, L, T, Cross, Pillar hall, Ring, Room in room, Cave, or Authored. Pillars and inner walls are solid cells inside the room, walled like any other wall.

**Painting your own room:** Create > Table > Room Shape. Set width and height (up to 24), then paint with the brushes: Floor, Pillar (solid), Door (floor cells a corridor may attach to; with none painted, corridors attach anywhere), Centre (lights, stairs, enemies, template pivot) and Erase (outside the room). Add it to a shape list as kind Authored. Examples are in `Dungeon1/Rooms/Shapes`. A Room Template can use a painted shape as its footprint, or paint one in place; its pivot sits on the Centre cell and it turns with the room (templates are never mirrored).

**Wall and floor variety**: see [Dungeon room styles](Dungeon-room-styles.md).

**Furnishing** (Rooms tab, also on biomes and profiles) places props by rule, scaled by floor area: Against Wall, Corner, Centre or Anywhere, with a footprint in cells, props per 10 cells, a per-room clamp, a chance and the room roles it applies to. Props face +Z into the room with the pivot at the middle of the footprint; ready-made ones are in `Dungeon1/Prefabs/Furnishing`. Walkways from doorways to the room centre are always kept clear. Features (fountains, altars, graves, bookshelves) have the same **Placement** choice; Against Wall and Corner slide the object back until it touches the wall.

## Other settings

- The inventory is live: opening it frees the cursor and stops player movement and attacks, but enemies, regeneration and time carry on. It never changes `Time.timeScale`.
- `Assets/Game/UI/Prefabs/Dungeon` holds Enemy health bar, Damage number and Dungeon message. Inventory and hotbar slots are the shared prefabs in `Assets/Game/UI/Prefabs/Items`.
- **Background Music Volume** on the level (0 to 1) scales the track during the AudioManager crossfade; a biome's music volume multiplies it.
- **Interaction Player** on `NpcBrain` (Room or Table) decides which player can talk to an NPC.
- AudioManager's **UI Library** holds the inventory, hover, equip and unavailable sounds. Spatial SFX play at full volume within **Sfx Min Distance** (3.5) and fade toward **Sfx Max Distance** (40). Containers have an **Open Volume**.

## Checking changes

There is no automated validation helper. Generation itself checks that every room is reachable. Door behaviour, loot and multi-floor runs need a Play Mode check; the toolbar Debug button loads the Debug Dungeon arena for quick combat tests.
