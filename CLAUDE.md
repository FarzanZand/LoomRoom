# LoomRoom

First-person 3D RPG. Unity 6000.5, URP, Cinemachine 3, Input System, Odin Inspector, Pixel Crushers Dialogue System, Synty art. Two controllable players: the **Room player** (exploration, never fights) and the **Table player** (first-person arms, melee combat). Rewritten in September 2026 on the conventions used in LoomyDungeons.

## Tooling

- **Use the Unity CLI (`unity-cli` skill) for all editor, scene, prefab and asset work. Never use Unity MCP servers (coplay-mcp, UnityMCP), not even to check editor state.**

## Settled decisions

- **Single `Assembly-CSharp`, no namespaces.** Same rule as LoomyDungeons. Do not propose asmdefs or namespaces.
- **One data asset per thing.** `CharacterData` for players, enemies and NPCs (they differ by which optional sections are filled). `ItemData` per item. `EnemyBehaviourProfile` for shared enemy tuning. `MovementProfile` per player kind. Assets live under `Assets/Data/{Characters,Profiles,Audio,Effects}` and `Assets/Items`.
- **Generic effects + custom escape hatch.** `EffectEntry` is one serializable class with an enum `type` and Odin `[ShowIf]` per field; `Custom` routes to an `ItemEffect : ScriptableObject` under `Assets/Scripts/Items/Effects` (assets in `Assets/Data/Effects`). `InteractionEffect` / `InteractionAction` mirror this for interactables. Add new effect types by appending to the enum, adding a case in `ItemEffectProcessor.Apply`, and a `Describe` line.
- **Enums are serialized by integer.** Explicit values, append only, retired numbers stay retired (see `StatType`, `ItemType`, `EnemyState`).
- **Singletons never auto-create.** `Singleton<T>` returns null and logs if the manager is missing. Managers live as prefabs under `Assets/Prefabs/Managers` and sit under the `Managers` object in the scene. Use `X.HasInstance` for cheap null-safe reads.
- **Config lives on managers, containers live on players.** `InventoryManager` holds pickup and drop rules; `Inventory` (bag + hotbar, two components with different roles) and `Equipment` sit on each player so each keeps their own items and hands.
- **One owner per concern.** `InputManager` owns the input asset (maps: `Room`, `Table`, `UI`, `Dev`); `GameManager` owns the game-state stack (`Explore`, `Menu`, `Dialogue`, `Cutscene`, `Dead`) and therefore the cursor, which input map is live, and HUD visibility. Nothing else touches `Cursor` or enables action maps. Push a state, pop it when done.
- **Every hit is a `DamageInfo`.** Built by `Hitbox`, `EnemyBrain` or an effect; consumed by `CharacterStats.TakeDamage`; broadcast through `Character.Damaged` so FX, hit reactions, knockback and item triggers all see the same facts. `FactionRules.IsHostile` decides who can hurt whom.
- **Animator state is read by tag, never by name or layer index.** Arms attack states are tagged `Attack`, arm block states `Block` (only the LeftArm/RightArm layers; the FullBodyLayer state named "Block" is deliberately untagged). Enemy attack states are tagged `Attack`.
- **UI is authored in the scene.** Slots, the context menu, the drag ghost, notifications and vitals are scene objects; code only binds and fills them. Grandfathered: `ContextMenuUI` clones its authored button template.
- **Plain C# events.** UnityEvents only where a designer wires them (`InteractableTrigger.onInteract`, `CutsceneController.onStarted/onFinished`, `TableIntroController.BeatStep.onStep`).
- **Save/load is designed for, not built.** `ItemStack`, `Inventory.slots`, `Equipment` and `ProgressionManager` flags are all plain (key, value) shapes on purpose. Do not add non-serializable runtime state to them.

## Where things are

| Concern | Files |
|---|---|
| Game state, input, players | `Scripts/Core/{GameManager,InputManager,PlayerManager,ProgressionManager,WorldManager}.cs` |
| Character layer | `Scripts/Characters/{Character,CharacterStats,CharacterData,Faction,StatType,MovementProfile,CharacterFX}.cs` |
| Enemy / NPC AI | `Scripts/Characters/AI/{EnemyBrain,Perception,EnemyMotor,EnemyAttack,EnemyBehaviourProfile,NpcBrain,NoiseEvents}.cs` |
| Player | `Scripts/Player/{Player,PlayerMotor,PlayerLook,PlayerFootsteps,PlayerCombat,PlayerCameraRig,PlayerAnimation,PlayerSettings}.cs`, MFPC modules kept in `Scripts/Player/Modules` |
| Combat | `Scripts/Combat/{CombatManager,Hitbox,WeaponAnimationRelay,IDamageable}.cs` |
| Items | `Scripts/Items/{ItemData,ItemEnums,EffectEntry,ItemEffect,ItemEffectProcessor,EffectDispatcher,ItemStack,Inventory,Equipment,InventoryManager,WorldItem}.cs`, `Items/Editor/ItemDatabaseWindow.cs` (`Tools > Item Database`) |
| Interactions, dialogue | `Scripts/Interactions/*`, `Scripts/Dialogue/DialogueBridge.cs` (Lua: `GiveItem`, `HasItem`, `SetFlag`, `GetFlag`) |
| Cutscenes | `Scripts/Cutscenes/{CutsceneController,WakeUpCutsceneController,DinnerCutsceneController,TableIntroController,TableManager}.cs` |
| UI | `Scripts/UI/*` |
| Table player arms animation | `Animations/FirstPersonTable.controller`, clips in `Animations/FirstPersonPlayer` (authored) and `Animations/FirstPersonPlayer/Generated` (derived by `Scripts/Combat/Editor/CombatFeelSetup.cs`, `Tools > LoomRoom > Apply Combat Feel`; re-run after editing source poses, never hand-edit the generated clips) |

## Known leftovers

- `Assets/Animations/FirstPersonTable.controller` has two transitions whose destination state no longer exists (pre-existing "Broken text PPtr" warnings on import). Open the controller and delete the dangling transitions.
- The wake-up timeline's animation track is empty; `WakeUpCutsceneController` only fades and plays music over it.
- `Assets/Misc/StarterAssets` is kept because its animation clips may be referenced by the third-person controllers. Its scripts are unused.
