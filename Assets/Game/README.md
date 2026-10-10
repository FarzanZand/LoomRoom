# LoomRoom project layout

The only scene is `Scenes/Room.unity`. Project conventions and a fuller map of the code are in `CLAUDE.md` at the repository root; design notes are in `Docs/`.

## Folders (`Assets/Game`)

| Folder | What is in it |
| --- | --- |
| `Core` | Managers (game state, input, players, progression, world, screen, runs, log, pooling, settings) and their prefabs in `Core/Prefabs` |
| `Characters` | `Players/{Room,Table,Scripts,Input,Settings}`, `Enemies/{Base,Crypt,Town,_Archive}`, `NPCs`, `Animations`, shared character scripts |
| `Combat` | `CombatManager.prefab`, hitboxes, spells (`Combat/Spells/<Spell>`), arrows |
| `Items` | Item data, effects, loot tables and profiles scripts, world models, icons |
| `Levels` | Level code (`Scripts`), `Resources/TableLevels.asset`, `Shared` (reveal, board), one folder per level: `Dungeon1`, `IntroDungeon`, `Town` |
| `Progression` | Classes, skills, rules, save, memorial table |
| `Interactions` | Interactables, doors, room loop portal |
| `Dialogue` | Dialogue Database, `DialogueBridge`, the DM dialogue UI |
| `Cinematics` | Cutscene controllers, intro assets, timelines |
| `UI` | UI scripts, prefabs, kit sprites, fonts, themes |
| `Rendering` | URP assets, `Desktop Renderer`, retro screen shader, Pixel Look |
| `World` | Lighting manager, moods, apartment materials, props and prefabs |
| `Audio` | `AudioManager`, audio data (`Audio/Data`) and source clips (`Audio/Library`) |
| `Art` | Paintings, pixel item art, terrain layers |
| `Meshy` | AI-generated models from `Tools/meshy.py` |
| `Development` | Debug Dungeon session and settings, editor tools in `Development/Editor` |
| `Scenes` | `Room.unity` |

Imported packages live in `Assets/ThirdParty`, `Assets/Synty`, `Assets/Plugins`, `Assets/Packages` and `Assets/ProPixelizer`. `Assets/Settings`, `Assets/Resources` and `Assets/Editor Default Resources` keep their Unity-specific locations.

## Editor menus and toolbar

Under **Tools > LoomRoom**:

- **New Enemy**, **New NPC** (also right-click a character prefab > **Create > LoomRoom > Character Variant**)
- **Item Database**, **Sync Item Catalog**
- **Pixel Look > Build Materials**, **Build Materials (Reset Tuning)**, **Select Library**
- **Editor Cutaway** (hides the apartment shell in the Scene view)
- **Fullscreen Game View** (F11), **Fullscreen On Play**

The main toolbar has **Debug** (load the Debug Dungeon arena) and **Dungeon** (generate WorldManager's Debug Dungeon and play it, skipping the room). `PackageToolsMenu` moves the Animation Rigging and Jobs package menus under Tools.

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

Move assets inside Unity (or with their `.meta` files) so GUID references survive. Some editor tools contain asset paths; update those when moving their assets.
