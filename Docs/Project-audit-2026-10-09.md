# Project audit, 2026-10-09

Read-only audit of all game code (`Assets/Game`, ~45k lines), assets, third-party packages, git and docs. Nothing was changed. Paths are relative to `Assets/Game/` unless they start with `Assets/`, `Docs/` or similar. Spot-checked items are marked ✔.

## Status after the cleanup (same day)

Done: every bug in section 1 except where noted below, the performance items in section 2, all dead code in section 3, the redundancy in section 4 (new helpers listed in CLAUDE.md "Shared helpers"), most conventions in section 5, DungeonGenerator and AudioManager split into partials, the docs in section 10, the `.gitignore` gaps and the stale `UNITY_POST_PROCESSING_STACK_V2` define. Licensed music deleted (the Crypt Lord now has no boss music). Compiled cleanly and smoke-tested in Play mode (generated dungeon, kills, XP credit).

Kept on purpose: everything in section 7 except the licensed music (the libraries are for future use).

Still open:
- Room pickups `Items/Prefabs/Pickups/Sword` and `Shield` in `Room.unity` still point to `_Archive` items.
- `DungeonHud` / `DungeonCombatFeedback` still build UI in code (now listed as grandfathered in CLAUDE.md).
- `CurrencyManager`, `CoinPickup`, `ClassFigures` assemble objects in code.
- `PlayerLook` still reads `Cursor.lockState` (needed for focus loss; commented).
- `PixelLook` still scans renderers every 0.25 s (only new renderers are swapped).
- Git LFS / `.gitattributes`, package removals and history rewrite not done.
- `EnemyBrain`, `PlayerCombat`, `CharacterStats` not split.

## 1. Fix first (real bugs, small fixes)

| # | Where | Problem |
|---|---|---|
| 1 ✔ | `Core/Scripts/InputManager.cs:122` | `Dev` map (F1/F2 swap players, F5 moods) always enabled, ignores game state, ships in builds. F2 in a cutscene swaps players behind GameManager. Gate on `Debug.isDebugBuild`/`UNITY_EDITOR`. |
| 2 ✔ | `Core/Scripts/PixelLook.cs:139` | `PixelLitObject.shader` has no asset references and is loaded by `Shader.Find`: stripped from player builds, per-object pixelation silently falls back. Reference it from the library asset or Always Included Shaders. |
| 3 ✔ | `Characters/Scripts/CharacterStats.cs:118-135` | Expiring timed modifiers never call `OnStatChanged`: health/mana/stamina can stay above the new max and HUD events don't fire. |
| 4 ✔ | `Characters/Players/Scripts/Player.cs:78` | `spawnRotation == default` is always false (zero quaternion), so the spawn point is never recorded at Start; respawn without a level spawn goes to the origin (Room player has `respawnDelay: 3`). Use a bool. |
| 5 | `Cinematics/Scripts/CutsceneController.cs:34-37` | OnDisable mid-cutscene never calls `Finish()`: Cutscene state stays pushed, camera priority stays raised. |
| 6 | `Cinematics/Scripts/TableManager.cs:252-257` | OnDisable pops Dialogue only if it is on top; `sitting` is cleared after the pops, so a disable during Zoom/menu wait can pop someone else's count. Needs an `ownsDialogue` flag. |
| 7 | `Cinematics/Scripts/IntroController.Tabletop.cs:174,308` | `rolls = new int[3]` but `rolledAttributes` length is free; a 4th attribute throws mid-coroutine with Dialogue pushed, game freezes. |
| 8 | `Characters/Scripts/DeathBurst.cs:349` | One `new Mesh` per body part per death, never destroyed. Leaks over a run. Also `?.`/`??` on Unity objects at :246, :304. |
| 9 | `Levels/Scripts/DungeonGenerator.cs:250,262,270` | Shop template's height/style/openCeiling read via `TemplateOf(i)` before `MerchantRoom` is chosen (:347). Latent (current shop template sets none). |
| 10 | `Levels/IntroDungeon/IntroDungeon.asset:257` | `boardTileTaps: []`; the three `Shared/Board/Board tap *.wav` are unreferenced, so board taps never play. |
| 11 | `Levels/Scripts/BoardReveal.cs:53-65` | Children filed by pivot: `Torches` root and `Dungeon HUD` canvas (both at table centre) get hidden/squashed with whatever room is at the centre. |
| 12 | `Levels/Scripts/DungeonCombatFeedback.cs:59-71` + `TableLevelLoader.cs:280` | Missing HUD prefab throws `ArgumentException`; loader retry only catches `InvalidOperationException`, so the floor fails. |
| 13 | `UI/Scripts/ShopUI.cs:46-50`, `PauseMenuUI`, `InventoryUI.cs:36`, `EndingController.cs:73/98`, `DialogueBridge` | Destroy/disable while open leaks the pushed state (Inventory pops only from a tween's OnComplete). |
| 14 | `Dialogue/Scripts/DialogueBridge.cs:13-19` | Subscribes only if `DialogueManager.hasInstance` already true in OnEnable, no Start retry: depends on component order. |
| 15 | `Characters/Scripts/AI/EnemyBrain.cs:206-209` | Pause cancels the swing every frame without setting a cooldown; enemy can swing instantly after unpause. |
| 16 | `Characters/Scripts/AI/EnemyBrain.cs:217-227` | Personal-space step-back runs before the recovery check, so staggered enemies still walk. |
| 17 | `Characters/Scripts/AI/NpcBrain.cs:79-89` | No `SimulationActive` check: NPCs wander during menus and cutscenes. |
| 18 | `Core/Scripts/MessageLog.cs:161`, `RunManager.cs:232` | Any non-player death (enemy-on-enemy, traps) posts "You killed X!" and grants XP. |
| 19 | `Core/Scripts/PixelObjectFeature.cs:80` | Uses `cameraDepthTexture` without `ConfigureInput(Depth)`; in Clean mode (no SSAO prepass) the seed can be missing, bringing back room bleed. Verify in Frame Debugger. |
| 20 | `Core/Scripts/ScreenManager.cs:190-314` | Writes renderScale/upscaling on the shared `Desktop URP.asset` every frame, restored only in OnDisable: a crash or Save Project in Play leaves the asset modified. |
| 21 | Statics with domain reload off | `EnemyVoice.lastUnseenReport`, `EnemyBrain.Active`, `RoomClock.nightMinutes`, `ItemData.honedCopies` (stale stats after tuning), `LootSource.BiomeProgress` need `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` resets. |
| 22 | `Items/Scripts/ItemData.cs:248` | Trinket tooltip compares only against Trinket1. |
| 23 | `Dialogue/Scripts/DialogueBridge.cs:86-95` | Lua `HasItem` misses Honed copies (reference match on base asset). |
| 24 | `Characters/Players/TablePlayer.prefab`, `Room.unity` | Default starting items are `_Archive/DebugSword`/`DebugShield`; room pickups `Sword`/`Shield` point to `_Archive` items not in the catalog (would break checkpoint restore if ever saved). |
| 25 | `UI/Scripts/PauseMenuUI.cs` | Sets `Time.timeScale = 1` on close, cancelling an active hit stop. |

Lower-priority bugs: torch placement ignores the floor seed (`DungeonGenerator.Growth.cs:162`); `MakeContainer` uses the main RNG stream despite the comment (`DungeonGenerator.cs:115`); lintel loop can hang with non-integer tile size (`:532-545`); `??`/`?.` on serialized Unity refs in ~12 places in Levels and `TableManager.cs:56`; `EnemyAttackRunner` cooldown list sized once in Awake; revive doesn't clear the Animator `Dead` bool; hit flash misses weapons spawned in Start (`CharacterFX.cs:200`); `PoolManager.Release` drops inactive objects; `Equipment.cs:173` drops armour on the floor when the bag is full instead of refusing; `AdventureSave` rewrites the whole file synchronously on every flag change and overwrites a corrupt save without keeping it.

## 2. Performance (hot-path work)

- **Per-frame allocations:** `Equipment.cs:55` (array every LateUpdate), `DungeonMaster.cs:225-243` (closure + string rebuild every frame), `ExperienceBarUI`, `EquipmentPanelUI`, `TooltipUI.cs:76`, `BuffBarUI`, `InteractUI.cs:24`, `EnemyAttackRunner.cs:350` (`SphereCastAll` per frame for archers), `EnemyBrain.Attacks` getter rebuilds the archer list many times per frame.
- **Scene searches at runtime:** `PixelLook.cs:103` (`FindObjectsByType<Renderer>` every 0.25 s), `GlowInDark.cs:36` (all lights every second + GetComponent per light per frame), `BoardReveal.cs:118` (every frame until found), `LightingManager` found by search in 4 places (not a Singleton), `Singleton.Instance` falls back to `FindAnyObjectByType` + log on every access once missing.
- **AI:** every chasing enemy calls `SetDestination` (and `SamplePosition`) every frame; `EnemyWeaponLoadout` rewrites grip transforms every LateUpdate; `InteractController.cs:56` scores every interactable in the world every frame.
- **Player:** `PlayerCombat` does ~8 full animator-layer tag scans per frame with string `IsTag`; hash once, compute "attacking" once per frame.
- **Leaks:** PixelLook twin materials never destroyed; DeathBurst meshes (above); fallback `PlayerSettings` instance.

## 3. Dead code (safe to remove after a final check)

- **Characters:** `FootIK.cs`, `IgnorePelvisRotation.cs`; MFPC `SlopeHandler`, `SteepSlopeSlideModule`, `ObjectLayer` (on no prefab, so their PlayerMotor/PlayerFootsteps branches never run); `PlayerMotor.InWater/SetInWater` + water footstep branches; `Player.BodyPresentation`, `CharacterStats.DrainMana/HasMana/ReloadFromData`, `Character.SetFaction/FactionChanged/Healed`, `PlayerData.jumpStaminaCost`, `PlayerCombat.guardPressedAt` + its subscription.
- **Levels:** Partition layout code (~250 lines, no level uses it; keep the enum value); `DungeonLever` (unused, also a UnityEvent); `RoomDistances` (computed 3×, never read), `NearestRoom`; `DungeonRoomTemplate.keepRoomLight` (both branches identical); `DungeonRoomProfile.lightIntensity/lightRange`; `TableLevelData.hotbarFrame`, `hudFont`, dead `message` label in DungeonCombatFeedback; `DecorateRoom`'s `lighting` param and the Resources load feeding it; `Loot(source)` ignores its parameter; `ShowCeilings(false)` never used; `TableLevelLoader` inspector fields can never be set (added at runtime).
- **Core/Cinematics:** `WorldManager.FadeDirectionalLight*` (both overloads + colour); `roomStartLight*` overwritten every frame by LightingManager (two owners of the sun); `GameManager.StateChanged` (no subscribers); `SkylightCover.Configure/SetCovered`; `DinnerCutsceneController` (inspector button only); `TableIntroController` survives only as a list of town objects; `PixelLookLibrary.PixelFor`, `PlayerManager.HasActive`.
- **Combat/UI:** `CombatManager.timedBlockWindow`; `DungeonMaster.prefix`.
- **Note:** `EnemyRagdoll` is *not* dead (Humanoid enemy base + `DungeonCorpse`); only three enemies use `DeathBurst`. CLAUDE.md is wrong about this.

## 4. Redundancy

- Weighted random pick written 8 times in Levels (`DungeonWeightedPrefab`, `DungeonWallLight`, `DungeonShapeChoice`, `DungeonRoomStyle`, `DungeonBiome.ChooseEnemy`, `ChooseProfile`, `DungeonBoon`, Growth `Pick`): one generic helper.
- Spawn enemy + balance + loot drop written 3×; `OpenCell` == `Open`; direction arrays reallocated ~15×; layout finishing steps copied 3×; "roll loot, roll gold, spill" in 5 features; Prefab Mode preview re-implements wall-light placement.
- `DeathBurst` and `EnemyRagdoll` share ~half their code; line-of-sight raycast with child filter written 4× (+ CombatManager); animator "any layer has tag" helper in 3 places; personal-space logic in both PlayerMotor and EnemyBrain; wander/patrol duplicated in NpcBrain and EnemyBrain.
- Camera ease helpers (`Zoom`, `LookAt`, `Turn`) duplicated between IntroController and TableManager; "warp + rotate + SetYaw" block in 4 places (belongs on `Player`); killer article logic in IntroController and RunRecapUI.
- AudioManager's five `PlaySFX*` variants repeat the pooled-source setup; pitch-variance formula 4×; item audio has a 3-way AudioData/Clip/Key switch plus a second decision in InventoryManager.
- Slot names defined twice and inconsistent ("Leggings" vs "PANTS"); `ShopRowUI.Info` is a second item summary next to `ItemData.BuildTooltip`; shop colours hard-coded instead of `UIManager`.
- ~9 UI scripts do the subscribe-in-OnEnable-then-resubscribe-in-Start dance for manager order.
- `IntroDungeon.asset` and `Debug Dungeon.asset` carry generator-only settings Authored layouts ignore.
- PixelLit/PixelLitObject properties are identical (✔ as required); their ShadowCaster pass and `Bayer4` could move to a shared include.

## 5. Convention issues

- Runtime-built UI: `DungeonHud` (Canvas + minimap in code), `DungeonCombatFeedback` (instantiates enemy bars), `CurrencyManager`, `CoinPickup`, `ClassFigures` assemble objects in code.
- `IntroController.Tabletop.cs:325` reads `Mouse.current`/`Keyboard.current` directly, bypassing InputManager.
- `PlayerLook.cs:57` gates look on `Cursor.lockState` (read only, but couples to cursor).
- `Character` checks for `EnemyBrain` to pick hurt animation and corpse lifetime.
- Singletons used without `HasInstance` in `TableLevelLoader`, `TableLevelReveal`, `DungeonCombatFeedback`, `DungeonHud` (every frame), `TableLevelMenu`, `EndingController`, `AudioClipController`.
- `AudioManager.Channel` enum has no explicit values.
- Gameplay rules in UI: `HotbarUI.TryUseHeldConsumable`, `EquipmentSlotUI` equips.
- `EffectEntry.Describe` can show a raw `story.*` key in tooltips.
- Plain-writing rule: em dash in `DungeonBookshelf.cs:13`, edgy merchant greetings in `Merchant.cs:15-17`.
- Style drift: compressed one-liners in `PlayerCombat`, `PlayerMotor`, `CharacterStats`.

## 6. Oversized files

`DungeonGenerator.cs` (1,285 lines: split Openings/Walls, Spawning, Exits partials), `IntroController` (~1,055 across two files), `EnemyBrain.cs` (634), `AudioManager.cs` (551), `PlayerCombat.cs` (446), `ScreenManager.cs` (427), `ObjectController.cs` (423, generic tween used in two places), `CharacterStats.cs` (417), `TableLevelLoader` (mixes loading, town snapshots, lighting, music).

## 7. Assets and disk (about 1.6 GB reclaimable of ~2.5 GB in Assets)

| Item | Size | Notes |
|---|---|---|
| `Audio/Library/_Sound Effects Library` | 1,063 MB, **986 MB unreferenced** | Full 96 kHz/24-bit source library. Move out of `Assets/`, copy in used clips (48/16). |
| `Characters/Animations/_Archive` | 147 MB | Nothing references it. |
| `Assets/Synty` | 722 MB, ~73 MB reachable | Demo scenes alone ~119 MB; `SyntyLensDirt_01.png` 35 MB. |
| `Assets/ThirdParty/StarterAssets` | 84 MB | Only `Armature.fbx` (5 MB) used: move it to `Characters`, delete rest. |
| `Game/Meshy` | 74 of 94 MB unused | Raw FBX/GLB/blend/concept per DM head; `Barrel/`, `Test_Stool/` fully unused. |
| `Pixel Crushers/Dialogue System/Demo` | 44 MB | Its `Resources/` (1.1 MB ogg) ships in every build. |
| JMO Cartoon FX / VFXPACK_IMPACT | 39 + 28 MB | ~0.5 MB and ~1.7 MB used. |
| `World/Terrain/Woodland/Grounded Meadow *` + `Table Terrain`, `Table Navigation` | 27 MB | Unreferenced (live copies in `Rendering/`). |
| FPC docs | 7.8 MB | Footstep WAVs used, PDF/docs not. |
| `Assets/ProPixelizer` | 18 MB | No references; replaced by PixelLook. Compiles 79 scripts. |
| `Art/PixelItems` | 19 MB + 53 MB of .meta | 11,600 PNGs, ~70 used; big import cost. |
| Loose music | ~40 MB | `Pixel March.wav`, `Mines01.mp3`, several RPG tracks unreferenced. |
| Root clutter | | `Assets/New Terrain.asset` (3.4 MB), `Assets/Settings` URP template leftovers (Mobile/PC RP assets, Readme, TutorialInfo), empty folders `Items/Data/Generic`, `Items/Data/Keys`, `World/Props`. |
| Small leftovers | | Unused `Audio/Data/Poof`, `Weapon_Swing_Default`, five `UI/Audio/*.asset`, `UI/Resources/UIFeedback rows.asset`, `Items/Effects/LogMessage`, `Bronze Shield/Sword` prefabs, `Heal Potion` pickup, `Adventure option`, `Skill Row` prefabs, `Loomy Dungeons Darkened` theme, `PIXEARG_OUT`/`SDFUnderlay` fonts (check TMP fallbacks), `TableArmsMesh*` prefabs, `_Archive/Green Slime`. |

**Licensing:** a Baldur's Gate 3 soundtrack track is reachable from the scene (would ship in a build) and a YouTube rip (Tom Cardy) is committed. Paid Synty packs are in the repo: keep the GitHub repo private.

## 8. Git

- No Git LFS (no `.gitattributes`); pack is 1.4 GB, ~0.6 GB of it history-only (59 revisions of a 4.3 MB `Room.unity`, old UE anims, old FPC). Deleting files won't shrink it without `git lfs migrate`/`filter-repo` + force push.
- Add `.gitattributes`: LFS for `*.wav *.mp3 *.ogg *.fbx *.blend *.glb *.png *.tga *.tif *.psd *.exr`, `*.unity *.prefab *.asset` as text with `merge=unityyamlmerge`.
- `.gitignore` gaps: `Tools/__pycache__/`, `.claude/settings.local.json` (committed, personal), `*.slnx`, `*.dmp`. Six `.blend` files inside `Assets/` force every machine to have Blender.
- No secrets found in repo or history (Meshy key stays in `~/.meshy`).

## 9. Project settings and packages

- Six quality levels all point to the same URP asset; three spare URP assets.
- `UNITY_POST_PROCESSING_STACK_V2` define set but package not installed.
- Likely removable packages: `com.coplaydev.unity-mcp` (unpinned `#main`, and the rules say never use MCP), `com.unity.visualscripting`, `multiplayer.center`, `collab-proxy`, modules `vehicles`, `cloth`, `wind`, `vectorgraphics`, `adaptiveperformance`, `androidjni`, `umbra`, `unityanalytics`.

## 10. Documentation drift

**CLAUDE.md**
- Game states omit `Paused` (and PauseMenuUI sets timeScale itself).
- "DeathBurst replaces EnemyRagdoll": wrong, EnemyRagdoll is the fallback on the humanoid base.
- "Light hits flinch enemies (`lightFlinchDuration`)": value is now 0.
- Lamp opening is not the code default (`Tabletop` is; the scene has Lamp).
- Room lighting omits `Night`; board taps "from `boardTileTaps`" (none assigned); starting room door only for Grown layouts.
- Testing shortcuts miss the toolbar Dungeon button and the Fullscreen toggle.
- WorldManager/PlayerManager are `Singleton<T>` (just not prefab instances); only LightingManager is plain.
- `Items/LootTables/`, `Items/Scripts/Loot/`, `Items/Prefabs/Pickups/` not listed.

**Docs/** (archive or update)
- Archive: `RewriteProposal.md` (7 of 8 paths gone), `Dungeon-review-2026-09-22.md`.
- Stale paths: `ApartmentLayout.md`, `Adventure-revamp.md`, `Table-level-reveal.md` (also wrong about ceilings), `Dungeon-room-styles.md` (wrong asset names, garbled characters), `Dungeon-features.md` (Lever, keepRoomLight, Shrine path, "three biomes", ragdollDeath, no Wall Light socket), `Dungeon-authoring.md` (Partition as default, ceilings hidden, progression tables), `Room-profile-architecture.md`, `Dungeon-inventory-and-balance.md`, `Stamina-and-blocking.md` (sprint now off).
- `Assets/Game/README.md` points to folders and menus that no longer exist.

**Stale code comments:** `PixelLook.cs:19` (owner is ScreenManager), `IntroController.cs:9-10` and Tabletop title ("asleep at the table"; it wakes in bed), `EnemyBrain.cs:14` (stuns "future"; staggers exist), `PlayerMotor.cs:167` (slope modules), `DeathBurst.cs:213`, `DungeonGenerator.cs:115,557`, `TableManager.cs:4-9` (garbled), `ProgressionManager.cs:14`, `WorldManager.cs:13`, `DungeonLootTable.entries` labelled Legacy but still used, "theme" instead of "biome" in several tooltips. No TODO/FIXME markers anywhere.

## Suggested order

1. Bugs 1–8 in section 1 (each is a few lines).
2. Move the sound library out of `Assets/` and delete `_Archive` animations, Meshy raws, Grounded Meadow, root terrain (~1.2 GB).
3. Remove ProPixelizer, StarterAssets (keep Armature), Synty/Pixel Crushers demo content.
4. Add `.gitattributes` with LFS before more binaries land.
5. Delete dead code (section 3), then fold duplicates (weighted pick, LOS, camera eases, AudioManager).
6. Update CLAUDE.md and archive stale docs.
