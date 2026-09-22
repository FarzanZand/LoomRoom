# Editing items

`directToHotbar` is off by default: pickups go straight to inventory. Enable it on an item to try the hotbar first (including available stack space), falling back to inventory if the pickup does not fit. Dungeon starting equipment explicitly prefers the hotbar and equips independently of this pickup setting.

- `Data/Weapons`, `Data/Shields`, `Data/Consumables`, `Data/Tools`, `Data/Keys`, and `Data/Generic`: the item definitions for all levels. Edit names, icons, effects, equipment settings, and use animations here. Tools > Item Database lists these assets.
- `Prefabs/Pickups`: the shared `_ItemPickup` interaction prefab and existing pickup prefabs. Spawning chooses the item's pickup-only visual, then world/hand model, then InventoryManager's default pouch. `Prefabs/Props/Equipment loot pouch.prefab` is the shared Synty fallback. `Prefabs/Equipment` and `Prefabs/Consumables` contain authored models. Imported source models remain in their art packages.
- `LootTables`: drop chances and item references. Levels reference these shared assets rather than owning item definitions.
- `Scripts`: item behavior and editor tools.

For equippable consumables, equip first and use from the hand. `AnimationOnUse = Eat` moves the hand with its existing item toward the camera-relative mouth pose, then applies the effect and consumes one item. `Idle` uses immediately. Non-equippable consumables always use immediately without a hand animation.

Dungeon roof and edge visibility is configured on the level data under Dungeon openings. Enable `hideRoof` or `hideEdges` and choose a percentage of complete rooms and connected corridor sections (rounded to whole regions). Corridor legs keep their identity at junctions; adjoining passage networks are not merged into one region. The seed makes the selection repeatable. Edge hiding only affects outermost walls facing away from the dungeon, with collision retained. Reload the level to apply changes.

Dungeon lighting: choose `moodLightning` on the level asset for Amber Crypt, Moonlit Stone, Emerald Ruins, Rose Sanctuary, or Golden Hall. The editable shared SceneMood presets live in `Assets/Game/Levels/_Resources/Shared/Resources/DungeonLighting`. Enable `overrideLighting` to reveal custom sky, directional, ambient, and fog-color settings directly on that level. LightingManager applies the selected settings on entry; chamber lights also use the selected palette. Reload the level after edits. Fog color only applies when scene fog is enabled. Town continues to use its existing SceneMood reference.

Multiple floors: enable multipleLevels on dungeon data and set levelCount. `floorSettings` entry 0 configures floor 1, entry 1 configures floor 2, etc.; absent entries inherit the dungeon lighting. Each entry supports a preset or overrideLighting. Descending generates a new seed while retaining health, inventory and equipment. Starting gear and healing reset only for a new run. The final floor exits instead of descending.

See `Docs/Dungeon-authoring.md` for the reward-pool system, source/floor overrides, preview simulator and editable UI prefabs. Loot implementation is grouped in `Items/Scripts/Loot`; shared reward assets are in `Items/LootTables`.

See `Docs/Dungeon-inventory-and-balance.md` for the inventory/equipment controls, bronze/iron values, four foods, audio/style assets and current loot tuning.

Open **Tools > Item Database** to search, filter, edit, duplicate, archive and organize items. Organization preserves asset references. See `Docs/Item-database.md` for the workflow and catalog synchronization.

Shared pickup audio: InventoryManager uses `genericPickupSound` from AudioManager’s SFX library for every pickup while **Use Shared Pickup Sound** is enabled (default). Edit that library entry’s clip, volume and pitch variance. Disable the toggle later to return to per-item Pickup Audio / Default Pickup Audio. Pickups play in 2D through the SFX mixer.
