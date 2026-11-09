# Where everything is

The game opens on `Scenes/Main Menu.unity` and is played in `Scenes/Room.unity`. Project conventions and a fuller map of the code are in `CLAUDE.md` at the repository root; the folder rules are in `Docs/Folder-layout.md`.

## Where do I go to change...

| To change | Open |
| --- | --- |
| The table map | `Table map/Map/Realm map.prefab` (ground, scatter, roads, where each place stands) |
| A place on the map (the Crypt's gate, the Arena, the Peddler's Cellar) | `Table map/Map/Places/<Place>.prefab` |
| What a place is (name, act, level, lines) | `Table map/Encounters/Act N` |
| The bedroom and the apartment | `Scenes/Room.unity`; its art is in `World/Art`, its props in `World/Prefabs` |
| A dungeon's floors, biomes and balance | `Levels/<Level>`, or Asset Studio > Levels |
| A hand-built room | `Levels/Shared/Rooms`, or a level's own `Rooms` |
| Room looks (walls, floors) | `Levels/Shared/Room styles` |
| An enemy or NPC | `Characters/Enemies/<Group>`, `Characters/NPCs` |
| A class | `Characters/Classes`, its paintings in `Characters/Classes/Portraits` |
| A skill | `Characters/Skills/<Skill>` |
| An item | `Items/Data/<Kind>`, its model in `Items/Prefabs`, or Asset Studio > Items |
| A spell | `Combat/Spells/<Spell>` |
| A sound or music track | `Audio/Clips`, `Audio/Data`, `Audio/Music` |
| A menu or the HUD | `UI/Prefabs`, `UI/Sprites`; most HUD objects are in `Scenes/Room.unity` |
| Lighting moods | `World/Moods`, `Levels/Resources/DungeonLighting` |

**Asset Studio** (the first toolbar button right of Play) finds anything by name and shows where it is used.

## Editing the table map

- **Move a piece of a place:** open its prefab in `Table map/Map/Places`, move it, save. To see it on the map, open `Realm map.prefab` and click the arrow beside the place in the Hierarchy.
- **Add a prop:** drag it into the place's prefab or under `Scatter`, then press **Bake new props** on the place or on the map's root. It makes the pixel copy at the map's texel size.
- **Move a whole place:** move it under `Points` in `Realm map.prefab`. Its roads follow.
- **Add or bend a road:** `Paths` in `Realm map.prefab` (`CampaignPath`: bend, width, wobble).
- **Trees and rocks:** `Scatter` in `Realm map.prefab`; switch one off to hide it. Those on a road or a place hide themselves.
- **Paint the ground:** Asset Studio > Props > Campaign map > Paint.
- **Ground height:** no brush yet. It is a height grid, changed by scripts (`CryptEntrance.Flatten` is an example).
- **Careful:** `CryptEntrance.Build()` rewrites the Crypt, `ArenaSetup.Build()` the Arena and `CampaignMapBuilder.Build()` the whole map, over any hand edits. Nothing runs them by itself.

## Folders (`Assets/Game`)

Each part of the game has the same subfolders where it needs them: `Scripts`, `Prefabs`, `Data`, `Art`, `Resources` (only what code loads by name).

| Folder | What is in it |
| --- | --- |
| `Art` | What tools make: `Synty Pixel` (pixel copies of bought models, never edited by hand), `Meshy` (AI models from `Tools/meshy.py`) |
| `Audio` | Sound clips, audio data, music |
| `Characters` | Players, enemies, NPCs, classes, skills, animations |
| `Cinematics` | Cutscenes, the intro, timelines |
| `Combat` | Hits, gore, spells, arrows, effects |
| `Core` | The managers and the game's own systems |
| `Development` | Our tools, never in a release: Asset Studio, the dev prompt, debug arenas, test bots |
| `Dialogue` | The Dungeon Master's conversations |
| `Interactions` | Doors and things to use |
| `Items` | Item data, models, icons, loot |
| `Levels` | Dungeon levels: `Shared`, one folder per level, `Scripts` |
| `Progression` | Character sheet, trophy wall, room puzzle |
| `Rendering` | URP assets and settings, shaders, the pixel look |
| `Rules` | The house rules |
| `Scenes` | `Main Menu`, `Room` |
| `Table map` | The map on the table: its prefab, places, encounters |
| `UI` | Menus and the HUD |
| `World` | The apartment: art, props, lighting |

Outside `Assets/Game`, everything is bought and not edited: `Synty` (it has to stay at that path, its Sidekick tool has it written in its code), `ThirdParty` (every other pack), `Plugins` (code plugins), `Editor Default Resources` (Unity needs that name).

## Editor menu and toolbar

**Tools > LoomRoom** holds only Asset Studio. The main toolbar has **Debug** (the Debug Dungeon arena), **Dungeon** (the level and floor picked beside it), **Room** and **Boss**.

## Players

Both player prefabs use the same structure:

```text
RoomPlayer / TablePlayer       activation and scale wrapper (PlayerRig)
├── Controller                Player, movement, look, interaction, inventory, equipment
│   ├── ViewRig
│   │   ├── Yaw
│   │   │   └── Pitch
│   │   │       └── ViewPoint  virtual camera and aiming target
│   │   └── FirstPersonVisuals hands and arms; follow the rendered output camera
│   ├── GroundProbe
│   └── Feedback              jump and landing impulses
└── BodyVisuals               body animator, skeleton and body follower
```

The Table player's Controller also owns combat. The Room wrapper keeps its 20x scale. Movement, stamina and starting kit live on each player's `PlayerData` (stored in the prefab, edited on the Character component); control preferences live on `Players/Settings/PlayerSettings.asset`. One output Camera, AudioListener and Cinemachine Brain sit under `Systems/Cameras`.

## Scene and assets

`Systems`, `Players`, `World`, `Characters`, `Interactables`, `UI`, `Cinematics`, `Lighting` and `Development` are the scene's organising roots. Never assume `transform.root` is a character: find the owning `Player` or `Character` instead.

Move assets inside Unity so GUID references survive. Tools name asset paths in code: update them after a move.
