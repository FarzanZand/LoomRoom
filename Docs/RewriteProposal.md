# LoomRoom rewrite proposal

**Status (2026-09-14): implemented.** All eight phases are in the project; decisions from section 14 were answered (slots reserved for head/body/trinkets, Room player never fights, save designed for but not built, table intro as beat steps, hit stop kept and improved, no gamepad, stamina as stats, no namespaces). `CLAUDE.md` at the repo root records the settled conventions.

Written 2026-09-14 after reading every script under `Assets/Scripts`, the `Room.unity` scene and prefab wiring, and the LoomyDungeons conventions (`CLAUDE.md`, `PORTING.md`, `ItemData` / `EffectEntry` / `CharacterData` / `Singleton`).

The short version: the game already has the right *pieces* (stats with modifiers, an FSM enemy, trigger-based interaction, Cinemachine impulses, hit reactions, Pixel Crushers dialogue). What it is missing is the *authoring layer* LoomyDungeons has: one data asset per thing, generic effect lists with a custom-effect escape hatch, per-player containers instead of singletons with `[2][]` arrays, and one owner for input and game state. Most of the work below is restructuring, not inventing.

Each section: what is there now, what I propose, why it will feel better or be easier to work with.

---

## 0. Foundation and conventions

**Now.** No namespaces except the forked MFPC controller (`namespace MFPC` on `PlayerController`, `PlayerControllerRoom`, and the generated input wrapper). `Singleton<T>` in `Assets/Scripts/Tools/Singleton.cs` auto-creates a blank instance if none is in the scene, so a missing `AudioManager` silently runs with no mixer. Managers live in three places in the hierarchy: root (`PlayerManager`, `ProgressionManager`, `WorldManager`), `Managers/`, and `Environment/TableRoom/TableManager`. No project CLAUDE.md.

**Proposal.**
- Single `Assembly-CSharp`, no namespaces, matching the settled LoomyDungeons decision. Drop `namespace MFPC` from the forked files and set the input wrapper namespace to empty. Script GUIDs are unchanged so scene references survive.
- Port the LoomyDungeons `Singleton<T>` (no auto-create, `HasInstance`, editor-aware quit guard, a clear "add the prefab" error). Keep the LoomRoom session-id trick for domain-reload-off; the two merge cleanly.
- Every static event gets a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)] ResetStatics()`.
- All managers become prefabs under `Assets/Prefabs/Managers/` and sit under one `Managers` root in the scene.
- Folder layout by domain: `Scripts/{Core, Player, Characters, Combat, Items(+Effects, +Editor), Interactions, Dialogue, Managers, UI, Cutscenes, Tools, Editor}` and `Data/{Characters, Items, Effects, Audio, Profiles}`.
- Enum discipline from LoomyDungeons: explicit integer values, append only, retired numbers listed in a comment.
- Start a LoomRoom `CLAUDE.md` with dated settled decisions as we make them.

---

## 1. Input

**Now.** One `Player` action map in `Assets/PlayerInput.inputactions`, no control schemes, no UI map. Five separate `new PlayerInputActions()` instances live in `PlayerController`, `HotbarUI`, `InventoryUI`, `InteractController`, and `PlayerManager`. Freezing controls only disables the controller's instance, so hotbar keys and the inventory toggle still fire during dialogue and cutscenes. Menu state is a `HashSet<string>` in `MenuManager`, and cursor lock is toggled in three unrelated files. `Debug1`/`Debug2` swap players.

**Proposal.**
- One `InputManager` singleton owns the single `PlayerInputActions` instance and exposes C# events: `Move`, `Look`, `JumpPressed`, `SprintChanged`, `CrouchChanged`, `InteractPressed`, `PrimaryChanged`, `SecondaryChanged`, `HotbarSelected(int)`, `InventoryToggled`, `PausePressed`, `Cancel`.
- Split the asset into action maps that encode the two players directly: `Room` (move, look, interact, inventory, hotbar), `Table` (Room plus attack, block, lean, crouch, sprint), `UI` (navigate, submit, cancel, point, click), `Debug`. Swapping players swaps the enabled map. Opening a menu or dialogue enables `UI` and disables gameplay. This is the whole "different controls per player" story, editable in the asset.
- Add `Keyboard&Mouse` and `Gamepad` control schemes now even if gamepad is not a target, so bindings are grouped.
- Delete `MenuManager`; game state owns menus (section 8).

**Why.** Input leaks are the single most noticeable "does not feel good" issue. One owner also removes four subscription/unsubscription blocks and the `Input.mousePosition` legacy calls in UI.

---

## 2. Player controllers and cameras

**Now.** `PlayerController.cs` is a 1077-line fork of MFPC that mixes movement, camera yaw/pitch/lean, crouch collider resizing, footsteps and surface SFX, jump impulses, combat animator flags, block lockout, knockback, and damage reactions. `PlayerControllerRoom` only overrides which of eight virtual cameras are active, comparing `Speed == walkSpeed` with float equality. Those eight cameras are deprecated Cinemachine 2 `CinemachineVirtualCamera` components running under Cinemachine 3.1.6. The Room player in the scene is a hand-built copy; `Assets/Characters/RoomPlayer/RoomPlayer.prefab` is orphaned and still carries StarterAssets `ThirdPersonController` and `StarterAssetsInputs`. Players are swapped by `SetActive` on whole hierarchies through six raw `GameObject` fields on `PlayerManager`. Enemies always target `tablePlayer`.

**Proposal.**
- Split into small components, each with its own inspector header:
  - `PlayerMotor`: CharacterController movement, crouch, jump, gravity, slope modules, external velocity (knockback). Reads speeds from a `MovementProfile` asset and the `MoveSpeed` stat.
  - `PlayerLook`: yaw/pitch/lean and smoothing. Reads sensitivity, invert, hold/toggle from a `PlayerSettings` asset (the MFPC `GameData` renamed and trimmed, since those are user options, not gameplay).
  - `PlayerFootsteps`: interval and surface SFX, routed through `AudioManager` instead of the MFPC `AudioPool`.
  - `PlayerCombat`: table player only. Attack and block state, arms animator, hitbox windows (section 5).
  - `PlayerCameraRig`: Cinemachine 3. One `CinemachineCamera` per player plus a `CameraFeel` component that lerps noise amplitude and frequency by state (idle, walk, run, crouch). Replaces eight cameras and the SetActive juggling. Keep the impulse sources for jump, land, hit, hurt.
  - `Player`: hub component on the root that caches the above, like `Character` in LoomyDungeons, and is itself a `Character` (section 3).
- Room prefab = Motor, Look, Footsteps, CameraRig. Table prefab = the same plus Combat and the arms rig. Rebuild the Room player as a real prefab.
- `PlayerManager` holds a list of `PlayerContext` (kind, root, `Player` hub, camera, hand anchors, inventory, equipment) instead of six raw fields. `Active` is non-nullable. Swap = activate, switch input map, raise camera priority, fire `PlayerSwapped`. Enemies and NPCs query `PlayerManager.Instance.Active`.
- Keep the MFPC optional modules (`SlopeHandler`, `SteepSlopeSlideModule`, `StaminaModule`), `TerrainChecker`, `SurfaceSFX`, and the landing-impulse maths.

**Why.** Each player becomes a prefab you can tune slot by slot, camera feel becomes two sliders per state instead of eight camera objects, and the movement code stops knowing about shields.

---

## 3. Character data and stats

**Now.** `PlayerData` embeds a `List<StatEntry>` and movement floats. `EnemyData` references a separate `StatProfile` asset and carries thirty flat behaviour fields. `StatsComponent` loads its own profile in `Awake`, then controllers push a different profile in their `Awake`, then health is initialised in `Start`; the order only works by accident. Players are not `CharacterBase`, so hurt, death, and knockback handling is duplicated between `PlayerController.OnDamageTaken` and `CharacterBase.HandleDamageTaken`. `StatType` has ten entries and three are used. `Faction` is checked by equality only. `StatsComponent.Update` allocates a dictionary every frame. `IDamageable.TakeDamage` carries no source, so nothing knows who hit whom.

**Proposal.**
- One `CharacterData : ScriptableObject` for players, enemies, and NPCs, following LoomyDungeons: identity (name, faction, portrait), `StatEntry[] stats` with a `Reset()` that fills sensible defaults, `MovementProfile` reference, `startingItems`, `startingEquipment`, animator trigger names, and an optional `EnemyBehaviourProfile` reference shown only for enemies. Player, enemy, and NPC differ by which optional fields are filled, not by asset type.
- `Character` hub on every character root (both player roots, enemies, NPCs). Holds `data`, caches `CharacterStats`, `CharacterFX`, motor, and raises `Damaged`, `Died`, `Healed`. Static `Spawned` and `Died` events for registries.
- `CharacterStats` keeps the current Flat / PercentAdd / PercentMultiply maths, which is better than the integer version in LoomyDungeons. Fixes: resolve fully from `Character.data` in `Awake`, no push from controllers; tick timed modifiers without allocating; add `RemoveModifiersFromSource(string prefix)` next to the object-keyed removal so "everything from the left hand" is one call.
- Trim `StatType` to what the game uses or will use soon: `MaxHealth`, `Defense`, `AttackDamage`, `MoveSpeed`, `AttackSpeed`, `MaxStamina`. Explicit numbers, never renumbered. Wire `MoveSpeed` into `PlayerMotor` and the NavMeshAgent so buffs actually do something.
- `FactionRules.IsHostile(a, b)` static matrix replaces `==` checks. Enables neutral NPCs, allies, and enemy infighting later without touching combat code.
- `DamageInfo` struct: amount, source `Character`, hit point, direction, knockback force, blocked flag. `IDamageable.TakeDamage(DamageInfo)`. Hurt FX, hit reactions, and knockback all read from it.
- Stamina: leave the MFPC module in place but source its max from `MaxStamina` so gear can affect it. Low priority.

**Why.** One asset type to open when tuning any character, no init-order surprises, and every hit has context for effects and UI.

---

## 4. Items, inventory, equipment

**Now.** `ItemData` has an `ItemEffect` enum of `Heal`, `Feed`, `Custom` plus one `effectValue`; `Heal` and `Feed` only `Debug.Log`. The custom effect slot is `ItemEffectBase : MonoBehaviour`, so custom effects are prefabs, and `Assets/Items/Custom Effects/EffectTemplate.prefab` already has a missing script. Weapon damage is a float that `ItemHolder` turns into a stat modifier by hand; shields and everything else have no stats. `InventorySystem` and `HotbarSystem` are singletons holding `ItemData[2][]` and `int[2][]` indexed by which player is active, with duplicated `Shift`, `Swap`, `Consume`, `TryAdd`. `ItemHolder` is a global dictionary of held items with four hand anchors hard-wired for both players, so after a player swap the held object stays on the inactive player's hand. Hotbar eligibility disagrees between `WorldItem.Interact` and `HotbarSlot.IsHotbarCompatible`. Starting items are equipped rather than added, and only for whichever player happens to be active. `ContextMenuUI`, notifications, stack labels, and the drag ghost are built in code. No runtime item instance, so no per-item state ever.

**Proposal.** The LoomyDungeons authoring shape, sized for a first-person game.

- `ItemData` (keep the Generate Prefab button):
  - Identity: name, description, icon, world prefab, `ItemType` with explicit ints, `maxStack`, free-form `tag`.
  - Equipment section, shown for equippables: `EquipmentSlot` (`RightHand`, `LeftHand`, numbers reserved for more), `StatModifierEntry[] statModifiers` (stat, value, modifier type). This replaces the `attackDamage` hack and lets a shield add Defense or a ring add MoveSpeed without code.
  - Weapon section, shown for weapons: hit and swing `AudioData`, hit particle, `useDefaultHitEffects`, attack animation set.
  - `EffectEntry[] effects`: the generic list. Fields: `type` (`Heal`, `RestoreStamina`, `TimedStatBuff`, `PlayAudio`, `SpawnPrefab`, `SetFlag`, `Custom`), `trigger` (`OnUse`, `OnEquip`, `OnUnequip`, `OnHitLanded`, `OnHurt`), `value`, `duration`, `stat`, `chance`, each shown by Odin `[ShowIf]` only when the type uses it. `Custom` shows an `ItemEffect` slot.
  - `ItemEffect : ScriptableObject { abstract void Apply(EffectContext ctx); virtual string Describe(EffectEntry e); }` in `Scripts/Items/Effects/`, assets in `Data/Effects/`. `EffectContext` = user `Character`, target `Character`, item, hit point. Replaces the MonoBehaviour prefab approach.
  - `ItemEffectProcessor` static runs entries. A small `EffectDispatcher` on `Character` fans combat events to equipped items so `OnHitLanded` and `OnHurt` triggers work with no per-item code.
  - Consumables are `ItemType.Consumable` with `OnUse` effects. "Feed" becomes a `RestoreStamina` or a custom effect.
- Runtime `ItemStack { ItemData item; int count; }` as a class so it can grow per-instance data later (durability, charges).
- `Inventory` MonoBehaviour on each player root, not a singleton: `ItemStack[] slots`, `TryAdd`, `RemoveAt`, `Consume`, `Move(from, to)`, `OnChanged`, and an `ItemFilter` (allowed types). The hotbar is a second `Inventory` with six slots and a filter. One class, two uses, no duplicated shift/swap code, per-player for free.
- `Equipment` MonoBehaviour on each player root: slot to `ItemStack` map, `HandAnchor` entries configured on that player, instantiates the world prefab, applies `statModifiers` with a per-slot source, fires `Equipped` and `Unequipped`, notifies the arms animator through `PlayerCombat`. Fixes the wrong-hand bug.
- `InventoryManager` singleton keeps only config: slot counts, pickup rules (which types go to the hotbar first, equip on pickup policy), drop offset, pickup prefab, slot and context-button prefabs. `WorldItemSpawner` folds into it. Delete `Assets/Resources/ItemPickup.prefab`.
- Starting items: added to the correct player's inventory from that player's `CharacterData`; equipped only if `equipOnStart` is set.
- Editor: `Tools > Item Database` window ported from LoomyDungeons and trimmed. Table of all `ItemData` with inline editing and an `_Archive/` convention.

**Why.** Adding an item becomes: create the asset, fill the sections that show, done. Custom behaviour is one small ScriptableObject subclass, the same shape you already use.

---

## 5. Combat

**Now.** Attack and block state are detected by reading animator state *names* on hard-coded layer indices. Hit stop sets `timeScale` to zero and is triggered twice per hit, from `WeaponHitbox` through `CombatManager` and again from `PlayerFX`. Enemy attacks land by `Invoke` after a delay plus a distance check, with no hitbox and no facing check. Knockback is applied three different ways (player velocity, enemy timed `agent.Move`, `CharacterBase` instant `agent.Move`). Friendly fire is faction equality.

**Proposal.**
- `PlayerCombat` with an explicit `CombatState { Idle, Windup, Active, Recovery, Block, BlockHit }`, driven by animation events (`AttackBegin`, `HitboxOn`, `HitboxOff`, `AttackEnd`) or a `StateMachineBehaviour`, never by state-name strings. Input buffering for attack, block lockout window kept, everything gated by game state.
- `Hitbox` (generalised `WeaponHitbox`) used by the player's weapon and by enemy limbs or weapons. Overlap shape from the collider, faction check through `FactionRules`, one `DamageInfo` path, hit FX from the weapon profile or the `CombatManager` default pools, and `OnHitLanded` effect triggers.
- Enemies get real hit windows through `Hitbox` plus animation events. The timer stays as a fallback for placeholder animations.
- Hit stop: one entry point in `CombatManager`, unscaled timing, ignore re-entry while active, per-weapon multiplier. Consider a very low time scale instead of zero so animations still creep, which reads better.
- Knockback unified behind `IKnockbackReceiver`, implemented by `PlayerMotor` and `EnemyMotor`. Force comes from the attacker's weapon or attack entry, scaled once in `CombatManager`.
- Block: keep the frontal check, move the reduction into `CharacterStats.TakeDamage` via `DamageInfo.blocked`. Optional stamina cost hook.
- Death: `Character.Died` plus an optional loot drop from `CharacterData`. Keep `CharacterFX` (flash, hurt sound) and `HitReactionController` as they are, fed by `DamageInfo`.

---

## 6. Enemy AI

**Now.** `EnemyController` is a good base: FOV cone with raycast, close-range detection, chase with last-known position, search and give-up, return to post, aggression modes, wander and patrol, gizmos. But it is 539 lines in one class, copies thirty fields from `EnemyData` into private fields in `Awake` so every new knob is three edits, has one attack, lands hits on a timer, and hard-codes the table player. `HumanoidEnemy` is an empty subclass. The `DetectionZone` collider on the prefab is unused. NPCs share `MovementBase` but keep a separate state enum.

**Proposal.**
- Keep the enum plus handler-dictionary FSM. Split the class into:
  - `EnemyBrain`: states and transitions. Add `Investigate` (go to last noise or sighting) and `Dead`.
  - `Perception`: sight cone and hearing radius, target selection through `FactionRules`, a static `NoiseEvent` so sprinting and combat can alert enemies.
  - `EnemyMotor`: NavMeshAgent wrapper for move, face, speed by state, knockback.
  - `EnemyAttacks`: a list of `EnemyAttack` entries from data (trigger name, range, cooldown, angle, damage multiplier, hitbox id, weight, telegraph time). Pick by range and weight.
- `EnemyBehaviourProfile : ScriptableObject` replaces the flat fields on `EnemyData`, with Odin tabs: Perception, Movement, Combat, Animation. `EnemyBrain` reads the profile through a property instead of copying, so adding a knob is one line. Keep the per-instance override toggle, but as an asset reference.
- Enemy `CharacterData` = stats, behaviour profile, attacks. No `HumanoidEnemy` subclass; subclass only when behaviour truly differs, such as a ranged caster.
- NPCs: `NpcBrain` reuses `Perception` and `EnemyMotor` with Idle, Wander, Patrol, Talking. Wander and patrol become small reusable modules instead of living in `MovementBase`.
- Debug: keep gizmos, add a read-only current-state field in the inspector.

---

## 7. Interaction, NPCs, dialogue

**Now.** `InteractController` per player with trigger-volume registration and a closest-and-facing pick works well. `IInteractable` has no prompt or availability, so `WorldItem` is special-cased inside `InteractableTrigger`. Dialogue is started by wiring the Pixel Crushers trigger into the `onInteract` UnityEvent, and `DialogueInputController` relies on `SendMessage`.

**Proposal.**
- `IInteractable { string Prompt { get; } bool CanInteract(Character who); void Interact(Character who); }`. Removes the special case, enables "Locked" or "Talk to X" prompts.
- Keep `InteractableTrigger` and its UnityEvent for designer wiring. Add `InteractionEffect[] effects` mirroring items: give item, play audio, set flag, start conversation, `Custom` with an `InteractionAction` ScriptableObject. A door, chest, or table can then be authored without a script.
- Add an optional camera-raycast pick alongside the trigger volume. In first person, looking at the thing you want feels better than standing near it.
- Dialogue: keep Pixel Crushers. A `DialogueBridge` registers Lua functions (`GiveItem`, `HasItem`, `SetFlag`, `GetFlag`) and subscribes to `DialogueManager` start and end events instead of `SendMessage`. Conversation start pushes the `Dialogue` game state.

---

## 8. Game flow, managers, cutscenes

**Now.** `ProgressionManager` is three booleans. `WorldManager` mixes the wake-up cutscene, light fades, and a music cue. `TableIntroController` is a beat-timed coroutine with `object1` through `object8` fields. Controls are frozen through several paths. No pause, no game-state concept, no save.

**Proposal.**
- `GameManager` with a `GameState` stack: `Explore`, `Menu`, `Dialogue`, `Cutscene`, `Dead`. It is the only owner of cursor lock, input map, time scale, and HUD visibility. Replaces `MenuManager`, the cursor code in `DialogueInputController` and `InventoryUI`, `SetControlsFrozen`, and `CutsceneCanvasHider`.
- `ProgressionManager` becomes a flag store: string-keyed bools and ints with a `FlagChanged` event, editable in the inspector with an Odin dictionary. Items, interactions, and dialogue set and read flags; optional sync to Pixel Crushers variables.
- Cutscenes standardise on Timeline plus a `CutsceneController` base (freeze, camera priority, HUD hide, on-finish). The table intro becomes a Timeline with signal tracks, or a `BeatSequence` list of (beat, action) entries if you prefer to keep it as data. No more numbered object fields.
- `WorldManager` keeps only lighting and time-of-day helpers.
- Save/load: design `ItemStack`, flags, and equipment to serialise by asset name so a JSON save is possible later. Not built in this pass unless you want it.

---

## 9. UI

**Now.** `ContextMenuUI` builds its whole hierarchy in code, both `InventoryUI` and `ContextMenuUI` create their own notification labels, slots create stack labels at runtime, and several scripts use legacy `Input.mousePosition`. There is no health or stamina bar for the player, only the hurt flash.

**Proposal.**
- Everything authored in scene or prefab, following the LoomyDungeons rule. Runtime code only fills and binds. `ItemSlotUI` shared prefab used by inventory and hotbar with the stack label authored in.
- `InventoryUI` and `HotbarUI` bind to the active player's containers on `PlayerSwapped`. Drag and drop goes through an `IItemContainer` interface so hotbar to inventory and inventory to inventory are one code path.
- One `NotificationUI` toast for "Inventory full", "Picked up X", and similar.
- `PlayerVitalsUI` reading an `IHealth` interface on the active `Character` (health, stamina). Tooltips list stat modifiers and effect descriptions from `ItemEffect.Describe`.
- Pointer positions from `Mouse.current` or event data, never `Input.mousePosition`.

---

## 10. Audio

**Now.** Three parallel APIs in `AudioManager` (raw clip, string key, `AudioData`), plus a second pool in MFPC `AudioPool` for footsteps. Hurt sounds are `AudioClip[]` on `CharacterFX`, weapon sounds are clip fields on `ItemData`, combat defaults are `WeaponAudioEntry` structs on `CombatManager`, pickup audio is a clip on `WorldItem`.

**Proposal.**
- Standardise on the `AudioData` asset everywhere (clips, volume, pitch, variance). `AudioManager` API shrinks to `Play(AudioData, position)`, `Play2D(AudioData)`, `PlayUI(AudioData)`, `PlayMusic(AudioData)`. Keep the string-key library for music cues only if you like calling them from Timeline. Keep the mixer, volume persistence, and pooling.
- Footsteps route through `AudioManager`; the MFPC pool goes away.

---

## 11. Cleanup

Found while surveying; each is a small win.
- `Assets/Characters/RoomPlayer/RoomPlayer.prefab` is orphaned and carries StarterAssets components.
- `Assets/Items/Custom Effects/EffectTemplate.prefab` has a missing script.
- `Assets/Settings/DEPInputSystem_Actions.inputactions` is dead. `Assets/Misc/StarterAssets` can go once the Room prefab is rebuilt.
- `Assets/_Recovery/` scenes, `Assets/New folder`, `Assets/New Terrain.asset`, `Assets/Test/`, `Assets/Editor/Temp/FindCameras.cs`.
- `Assets/Characters/DungeonMaster/DM.controller` (empty) and `Assets/Characters/RoomPlayer/RoomPlayerParent.controller` are unreferenced; the scene `RoomPlayerParent` Animator has no controller.
- `ThirdPersonRoom.controller` has a stray `New State`. `HumanoidEnemyData.prefab` is unassigned.
- Eight Cinemachine 2 virtual cameras under Cinemachine 3.
- `Basics/Main Camera` is inactive at root; decide whether it is the fallback or delete it.
- Uncommitted `Packages/manifest.json` and `packages-lock.json` changes plus an untracked `PhysicsCoreProjectSettings2D.asset` from the Unity 6000.5 upgrade should be committed or reverted deliberately.

---

## 12. Keep as is

`CharacterStats` modifier maths. Enemy perception, chase, search, and return logic. `InteractController` and `InteractableTrigger`. `HitReactionController`. `CharacterFX` hit flash. `ObjectController`. `ScreenManager`. `AudioManager` mixer and volume code. The `ItemData` Generate Prefab button. Odin everywhere. MFPC optional modules, `TerrainChecker`, `SurfaceSFX`. Pixel Crushers. Timeline for cutscenes. Animation Rigging on the arms.

---

## 13. Suggested order

Each phase leaves the game playable in the editor.

1. **Hygiene and conventions.** Cleanup list, `Singleton` port, namespace removal, managers under one root, `CLAUDE.md`. Half a session.
2. **Input and game state.** `InputManager`, action maps, `GameManager` state stack, `PlayerManager` contexts. Immediately fixes input leaking into menus and dialogue.
3. **Character layer.** `CharacterData`, `Character`, `CharacterStats` init fix, `DamageInfo`, `FactionRules`. Existing `PlayerData`, `EnemyData`, `StatProfile` assets migrate into the new type.
4. **Items.** `ItemData` sections, `EffectEntry`, `ItemEffect`, per-player `Inventory` and `Equipment`, `InventoryManager` config, UI rebinding, Item Database window. Existing ten item assets migrate.
5. **Combat.** `PlayerCombat` states, `Hitbox`, knockback, hit stop.
6. **Enemy AI.** Brain, perception, motor, attacks, behaviour profile.
7. **Player controller split and Cinemachine 3.** Motor, look, footsteps, camera feel, Room prefab rebuilt.
8. **Interactions, dialogue bridge, cutscene base, flags.**

Phases 5 through 8 can be reordered by what you want to feel first.

---

## 14. Decisions I need from you

1. **Equipment slots.** Hands only, or reserve head, body, trinket now? This shapes `EquipmentSlot`, `Equipment`, and the tooltip.
2. **Does the Room player ever fight?** If never, `PlayerCombat` and the arms rig stay table-only and Room bindings stay minimal.
3. **Save/load.** Design for it now and build later, or build a JSON save in phase 8?
4. **Table intro.** Timeline with signals, or keep it as data-driven beats?
5. **Hit stop.** Keep it? If yes, tiny time scale or full zero?
6. **Gamepad.** Target it now (UI navigation, look multiplier) or only reserve the control scheme?
7. **Stamina.** Fold into stats, or leave the MFPC module untouched?
8. **Namespaces.** I assume the LoomyDungeons rule (none, single assembly) applies here too. Say if not.
