# LoomRoom project layout

Open the game with **LoomRoom > Open Game Scene**. Run **LoomRoom > Validate Scene Setup** after changing player or camera wiring.

## Where to edit

| Feature | Location |
| --- | --- |
| Room player prefab and tuning | `Players/Room` |
| Table player prefab and tuning | `Players/Table` |
| Player scripts, control preferences, input actions and animations | `Players/Shared` |
| Combat timing, camera motion, recoil and default audio references | `Combat/Prefabs/CombatManager.prefab` |
| Audio definitions / source clips | `Audio/Data` / `Audio/Library` |
| Enemy and NPC scripts, definitions and prefabs | `Characters` |
| Items, equipment and item effects | `Items` |
| Apartment materials, props and lighting moods | `World` |
| Timeline assets and cutscene scripts | `Cinematics` |
| HUD and UI scripts | `UI` |
| Editor tools | `Development/Editor` |
| Game scene | `Scenes/Room.unity` |

Imported packages live in `Assets/ThirdParty`. `Assets/Plugins`, `Assets/Packages`, `Assets/Synty`, `Assets/Settings`, and `Assets/Editor Default Resources` retain their package or Unity-specific locations.

## Player ownership

Both prefabs use the same structure:

```text
RoomPlayer / TablePlayer       activation and scale wrapper
├── Controller                Player, movement, look, interaction, inventory, equipment
│   ├── ViewRig
│   │   ├── Yaw
│   │   │   └── Pitch
│   │   │       └── ViewPoint  virtual camera and aiming target
│   │   └── FirstPersonVisuals hands/arms; follows the rendered output camera
│   ├── GroundProbe
│   └── Feedback              jump and landing impulses
└── BodyVisuals               body animator, skeleton and body follower
```

Table's Controller also owns combat. The Room wrapper intentionally retains its 20× scale; moving gameplay components to that wrapper would change collider and camera scale behavior. Skeleton bone names and animator-relative paths are preserved.

`Player` owns its activation root, body follower and view presentation references. `PlayerManager` only selects the player, switches the input map and configures the shared camera. Assign body and view references on the player prefab rather than adding parallel references to PlayerManager.

Select the prefab root to use `PlayerRig`: it exposes the character tuning and control preference assets inline, plus a button to select the gameplay components on Controller.

One output Camera, AudioListener and Cinemachine Brain live under `Systems/Cameras`. Each player retains a virtual camera and its original output channel mask and clipping/culling settings. `PlayerViewPresentation` follows the final camera pose after Cinemachine updates, keeping hands and weapon visuals aligned with charge zoom, swing motion and cutscenes without a second camera.

Movement, jump and stamina values live directly on each player's CharacterData asset. User control preferences live on PlayerSettings. Shared combat feel lives on CombatManager.

## Scene organization

`Systems`, `Players`, `World`, `Characters`, `Interactables`, `UI`, `Cinematics`, `Lighting` and `Development` are organizational roots. Reparent with world position preserved. Never assume `transform.root` is a character: find the owning Player or Character instead.

The dinner set floor remains part of the world because the dinner staging supports authored gameplay/cinematics. Imported demo scenes are excluded from Build Settings.

Move assets through Unity with their `.meta` files so GUID references survive. Editor tools and the input-action wrapper importer also contain paths; update those when reorganizing their assets.

## Cleanup validation

The reorganization preserved all 14,483 GUIDs recorded for the main folder moves. Runtime and editor assemblies compiled successfully. Play-mode checks covered Room and Table movement through the Input System, switching both ways, first-person camera alignment, menu state, full heavy charge/release and zoom recovery, dinner-camera routing, and wake-up playback. No runtime errors were recorded during the final run.

The existing Dinner Timeline has no authored duration. Its camera routing was verified directly; a complete dinner sequence still needs authored Timeline content.
