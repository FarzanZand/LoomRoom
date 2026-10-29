# Folder layout

Where things live in `Assets`, and the rules that keep it that way. Reorganised on 2026-10-23.

## The two kinds of folders

- **`Assets/Game`** is ours: everything made for The Table Awaits.
- **Everything else in `Assets`** is bought or third-party and is never edited: `Synty` (models), `Spells Pack` (spell effects),
  `ThirdParty` (`Pixel Items` icons, `Sound Effects Library`, `Generic Fantasy UI` sounds, StarterAssets, the v-tools and others),
  `Plugins`, `ProPixelizer`. To change one of their assets, make a copy in `Assets/Game` (the Synty pixel copies are made this way).
- **Bought packs are not imported whole** (since 2026-10-29). Every pack lives in the library project
  `C:/Game projects/AssetLibrary` (outside git), and only what the game uses is brought into LoomRoom, at the same
  paths and GUIDs, with `python Tools/asset_library.py` (`unpack` a downloaded pack into the library, `find`, `pull`).

## Assets/Game: one folder per part of the game

| Folder | What is in it |
|---|---|
| `Art` | Art made by our tools from bought models: `Synty Pixel` (the pixel copies of every Synty model in the game; generated, never edited by hand) |
| `Audio` | `Clips` (our sounds, by kind: Chests, Kills, Voice, Dialogue lines, Grunts, SFX, Misc ...), `Data` (AudioData assets), `Music` (every track the game plays, bought packs included), `Scripts`, `GameMixer.mixer` |
| `Campaign` | The realm map on the table: `Map` (the map prefab, its textures, the pixel props in `Map/Pixel`), `Encounters/Act N` (places and events by act), `Scripts`, `Prefabs`, `Resources` |
| `Characters` | Players, enemies and NPCs, one prefab each (`Players`, `Enemies/<group>`, `NPCs`), `Skills/<Skill>` (each skill's data and its abilities, Blocking's Shield bash and Deflect), `Animations`, `Scripts` (`AI`, `Data`, `Death`, `Hit feel`, `Editor`) |
| `Cinematics` | Cutscenes and the intro |
| `Combat` | Hits, gore, blocking, spells (`Spells/<Spell>`), arrows, effects |
| `Core` | The managers and the game's own systems (game state, input, players, progression, runs, screen) |
| `Development` | Tools for us, never in a release: the Asset Studio, the dev prompt, debug arenas, test bots |
| `Dialogue` | The Dungeon Master's conversations and lines |
| `Interactions` | Doors, triggers, things to use |
| `Items` | `Data` (one ItemData per item, by kind), `Prefabs` (world models), `Icons`, `Effects`, `LootTables`, `Scripts` |
| `Levels` | The table's levels: `Shared` (everything more than one level uses), one folder per level with only its own things, `Scripts` (by topic), `Resources` |
| `Meshy` | Raw results of `Tools/meshy.py` (models, textures, task ids). The creatures in use (Crypt Rat, Cave Bat, Urn Crawler) are here too |
| `Progression` | Classes, skills, the character sheet and trophy wall in the room |
| `Rendering` | The pixel look: shaders, the Pixel Look library, its generated materials |
| `Rules` | The Dungeon Master's house rules |
| `Scenes` | `Main Menu`, `Room` |
| `UI` | `Scripts` (`Hud`, `Inventory`, `Menus`, `Shop`, `Editor`; the UI's own base classes at the top), `Sprites`, `Fonts`, `Prefabs`, `Main Menu` |
| `World` | The apartment: its art, moods, prefabs, terrain |

## Levels

- **`Levels/Shared`** holds what every level uses: `Rooms` (room templates; `Rooms/Scripted` for rooms with an event of their own: the shop, the Skeleton Party room, Not a witch, the Boss stage; a level's own such rooms go in its `Rooms/Scripted`, like the Arena pit. A room that only holds a feature any room can have (a shrine, spike plates, an ambush, a portal, a guardian) stays in `Rooms`), `Room pieces` (the building blocks placed inside a room
  template: pillars, arches, niches, platforms, galleries, rubble; called "kit" in code, `DungeonKitBlock`/`DungeonKitPiece`),
  `Room shapes` (floor plans for generated rooms), `Room styles` (styles with their textures and materials), `Props` (`Breakables`,
  `Chests`, `Effects`, `Features`, `Furnishing`, `Groups`, `Shop`, `Traps`), `Doors`, `Materials`, `Biomes`, `Board`, `Portal`, `Reveal`.
- **A level's folder** (`Levels/<Level>`) holds only its own things: the level asset, its balance and card, its own `Biomes` and `Rooms`,
  and its own look when it has one (`Styles`, `Materials`, `Prefabs`, as the Arena and the Old Wood do).
- When a second level starts using something from a level's folder, it moves to `Levels/Shared` (the Asset Studio's Tidy folders does this).

## Rules

1. **One folder per thing when a thing has several files** (a spell, an enemy's model, a door). A folder with one file is fine only when
   more of its kind will join it.
2. **Scripts** live in the area's `Scripts`; a folder with more than about 25 scripts is split by topic. Editor-only code goes in an
   `Editor` folder. No namespaces, one assembly.
3. **`Resources`** only for what code loads by name. Everything else is referenced.
4. **Generated folders are never edited by hand**: `Art/Synty Pixel`, `Campaign/Map/Pixel`, `Rendering/PixelLook/Materials`,
   `Items/Icons/Generated`, `Levels/Shared/Room pieces/Previews`. Change the tool and run it again.
5. **Retired assets** go to an `_Archive` folder next to where they lived; the game and the Asset Studio leave `_Archive` out.
6. **Names** are plain words in sentence case ("Room pieces", "Dialogue lines"), never numbered copies ("Props 1").
7. **Moving or renaming** is always done in the editor (the Project window, or `AssetDatabase.MoveAsset` from an eval), never in the
   file system, so every reference keeps working. Afterwards search the code, `Tools`, `Docs`, `CLAUDE.md` and the skills for the old
   path and update it. Create every target folder before moving anything into it.
