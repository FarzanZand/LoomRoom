# LoomRoom

First-person 3D RPG. Unity 6000.5, URP, Cinemachine 3, Input System, Odin Inspector, Pixel Crushers Dialogue System, Synty art. Two controllable players: the **Room player** (exploration, never fights) and the **Table player** (first-person arms, melee combat). Rewritten in September 2026 on the conventions used in LoomyDungeons.

## Tooling

- **Use the Unity CLI (`unity-cli` skill) for all editor, scene, prefab and asset work. Never use Unity MCP servers (coplay-mcp, UnityMCP), not even to check editor state.**

## Settled decisions

- **Single `Assembly-CSharp`, no namespaces.** Same rule as LoomyDungeons. Do not propose asmdefs or namespaces.
- **One data asset per thing.** `CharacterData` for players, enemies and NPCs (they differ by which optional sections are filled; movement speeds live here too). `ItemData` per item. `EnemyBehaviourProfile` for shared enemy tuning. Everything lives under `Assets/Game/<Area>/`: character assets in `Characters/Data`, items in `Items/Data`, custom effect assets in `Items/Effects`, audio data in `Audio/Data`, level assets in `Levels/`.
- **Generic effects + custom escape hatch.** `EffectEntry` is one serializable class with an enum `type` and Odin `[ShowIf]` per field; `Custom` routes to an `ItemEffect : ScriptableObject` (scripts in `Items/Scripts/Effects`). `InteractionEffect` / `InteractionAction` mirror this for interactables. Add new effect types by appending to the enum, adding a case in `ItemEffectProcessor.Apply`, and a `Describe` line.
- **Enums are serialized by integer.** Explicit values, append only, retired numbers stay retired (see `StatType`, `ItemType`, `EnemyState`).
- **Singletons never auto-create.** `Singleton<T>` returns null and logs if the manager is missing. Managers sit under the `Systems` object in the scene; most are prefab instances (`Assets/Game/Core/Prefabs` and elsewhere); `WorldManager`, `PlayerManager` and the Lighting Manager are plain scene objects. Use `X.HasInstance` for cheap null-safe reads.
- **Config lives on managers, containers live on players.** `InventoryManager` holds pickup and drop rules; `Inventory` (bag + hotbar, two components with different roles) and `Equipment` sit on each player so each keeps their own items and hands.
- **One owner per concern.** `InputManager` owns the input asset (maps: `Room`, `Table`, `UI`, `Dev`); `GameManager` owns the game-state stack (`Explore`, `Menu`, `Dialogue`, `Cutscene`, `Dead`, `Inventory`) and therefore the cursor, which input map is live, and HUD visibility. Nothing else touches `Cursor` or enables action maps. States are reference-counted: every `Push` needs exactly one matching `Pop` from the same owner (`Dead` is a plain flag).
- **Every hit is a `DamageInfo`.** Built by `Hitbox`, `EnemyBrain` or an effect; consumed by `CharacterStats.TakeDamage`; broadcast through `Character.Damaged` so FX, hit reactions, knockback and item triggers all see the same facts. `FactionRules.IsHostile` decides who can hurt whom.
- **Animator state is read by tag, never by name or layer index.** Arms windup/hold states are tagged `Attack`; the release states are `AttackRelease`, `AttackReleaseAlt` and `AttackHeavyRelease` (all four count as attacking). Arm block states are `Block` (only the LeftArm/RightArm layers; the FullBodyLayer state named "Block" is deliberately untagged). Enemy attack states are tagged `Attack`.
- **UI is authored in the scene.** Slots, the context menu, the drag ghost, notifications and vitals are scene objects; code only binds and fills them. Grandfathered: `ContextMenuUI` clones its authored button template.
- **Plain C# events.** UnityEvents only where a designer wires them (`InteractableTrigger.onInteract`, `CutsceneController.onStarted/onFinished`).
- **Save/load is designed for, not built.** `ItemStack`, `Inventory.slots`, `Equipment` and `ProgressionManager` flags are all plain (key, value) shapes on purpose. Do not add non-serializable runtime state to them.

## Where things are

All game code lives in `Assets/Game/<Area>/Scripts` (paths below are relative to `Assets/Game`). The only scene is `Scenes/Room.unity`.

| Concern | Files |
|---|---|
| Game state, input, players, world | `Core/Scripts/{GameManager,InputManager,PlayerManager,ProgressionManager,WorldManager,Singleton}.cs` |
| Screen effects and fades | `Core/Scripts/{ScreenManager,ScreenEffectSettings}.cs`, shader `Rendering/RetroScreen.shader` (full-screen pass on `Rendering/Desktop Renderer.asset`) |
| Lighting and moods | `World/LightingManager.cs` + `LightingManager.Moods.cs`, presets are `SceneMood` assets (`Core/Scripts/SceneMood.cs`) |
| Character layer | `Characters/Scripts/{Character,CharacterStats,CharacterData,Faction,StatType,StatModifier,CharacterFX}.cs` |
| Enemy / NPC AI | `Characters/Scripts/AI/{EnemyBrain,Perception,EnemyMotor,EnemyAttack,EnemyBehaviourProfile,NpcBrain,NoiseEvents}.cs` |
| Player | `Players/Shared/Scripts/{Player,PlayerMotor,PlayerLook,PlayerFootsteps,PlayerCombat,PlayerCameraRig,PlayerAnimation,PlayerSettings}.cs`, MFPC modules in `Players/Shared/Scripts/Modules`. `PlayerInput.cs` is generated from `Players/Shared/Input/PlayerInput.inputactions`; never hand-edit it |
| Combat | `Combat/Scripts/{CombatManager,Hitbox,WeaponAnimationRelay,IDamageable}.cs` |
| Items | `Items/Scripts/{ItemData,ItemEnums,EffectEntry,ItemEffect,ItemEffectProcessor,EffectDispatcher,ItemStack,Inventory,Equipment,InventoryManager,WorldItem}.cs`, `Items/Scripts/Editor/ItemDatabaseWindow.cs` (`Tools > LoomRoom > Item Database`; `Tools > LoomRoom > Sync Item Catalog` refreshes InventoryManager's catalog) |
| Interactions, dialogue | `Interactions/Scripts/*` (incl. `HingedDoor`, `RoomLoopPortal`), `Dialogue/Scripts/DialogueBridge.cs` (Lua: `GiveItem`, `HasItem`, `SetFlag`, `GetFlag`) |
| Cutscenes | `Cinematics/Scripts/{CutsceneController,WakeUpCutsceneController,DinnerCutsceneController,TableIntroController,TableManager}.cs` |
| Table levels (town, dungeons) | `Levels/_Resources/Shared/Scripts/*` (`TableLevelLoader`, `TableLevelReveal`, `DungeonGenerator`...), catalog `Levels/Resources/TableLevels.asset` |
| UI | `UI/Scripts/*` |
| Runs, log, currency, pooling, settings | `Game/Core/Scripts/{RunManager,MessageLog,PoolManager,UserSettings}.cs`, `Game/Items/Scripts/Currency/*` (CurrencyManager config, Wallet on the player), UI in `Game/UI/Scripts/{MessageLogUI,GoldCounterUI,BossBarUI,RunRecapUI,PauseMenuUI,SettingsUI,ShopUI}.cs` |
| Dungeon themes, bosses, templates, features | `Levels/_Resources/Shared/Scripts/{DungeonTheme,DungeonBossEncounter,DungeonRoomTemplate,DungeonSocket,DungeonDestructible,DungeonCorpse,Merchant,DungeonAmbience}.cs`, `Features/*`; guide in `Docs/Dungeon-features.md` |
| Table player arms animation | `Players/Shared/Animations/FirstPersonTable.controller`, clips in `Players/Shared/Animations/FirstPersonPlayer` (all hand-authored) |

## Known leftovers

- The wake-up timeline's animation track is empty; `WakeUpCutsceneController` only fades and plays music over it.
- `Assets/ThirdParty/StarterAssets` is kept because its animation clips may be referenced by the third-person controllers. Its scripts are unused.
