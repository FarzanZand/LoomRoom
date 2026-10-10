# Inventory, equipment and dungeon balance

## Main editing locations

Paths are under `Assets/Game`.

- `Items/Data/{Weapons,Armor,Shields,Consumables,Trinkets}`: shared item definitions (retired ones in `_Archive`). Edit them in **Tools > LoomRoom > Item Database**; see [Item database](Item-database.md).
- `UI/Prefabs/Items`: Inventory slot, Hotbar slot, Equipment slot. Edit sizes, images, fonts and hover feedback here. The full inventory and character panels are authored in `Room.unity` under **Inventory screen**.
- `UI/Resources/UIFeedback.asset`: panel slide duration and ease, hover and press scale. UI clips and volumes are on **AudioManager > UI Library**.
- `Items/Prefabs/Equipment`: hand models and armour models. `Items/Prefabs/Consumables` holds food models.
- `Items/Prefabs/Props/Equipment loot pouch.prefab`: the pickup visual for armour and other meshless items (InventoryManager's **Default Pickup Visual**). Lookup order: the item's Pickup Visual Prefab, its World Prefab, then the manager's fallback.
- `Levels/Dungeon1/Prefabs/Props/Supply chest.prefab`: the chest the level places. It opens its lid and releases loot once.
- `Levels/Dungeon1/Dungeon balance.asset`: per-floor enemy scaling (damage +2, health +10%, armour +0.2 per floor after the first). Base values are on each enemy prefab's EnemyData and the TablePlayer prefab's PlayerData.
- Starting kit: each class asset in `Progression/Data/Classes` (equipment, spells, supplies).
- Loot: biome loot profiles in `Levels/Dungeon1/Biomes`; see [Dungeon authoring](Dungeon-authoring.md#loot).

## Controls and behaviour

Tab opens and closes the inventory. The cursor is freed and player movement and attacks stop, while enemies, damage and regeneration carry on. The inventory never changes time scale. Death, player swap, rapid toggles and closing clean up menu state and drag visuals.

Click bag gear or drag it onto a matching equipment slot to equip. Click an occupied equipment slot to unequip. Right-click an item for context actions. Hand items (weapon, shield) stay in the hotbar while held: equipping from the bag moves the item into the old hand item's hotbar slot or an empty one, and a full hotbar with no slot to swap refuses the equip without deleting anything. Worn items (armour, leggings, trinkets) leave the bag while equipped and return on unequip.

`directToHotbar` is off by default. When on, pickups try the hotbar first and fall back to the bag. Descending keeps the current loadout.

Equippable consumables must be held first. `AnimationOnUse = Eat` keeps the item in hand while it moves to the mouth, then consumes one and applies its effects. `Idle` uses it at once from the hand. Non-equippable consumables apply straight from the inventory. Food replaces current food regeneration instead of stacking; death and a new run clear it. Tooltips show rate, duration, total healing and equipment comparisons.

## Damage

Armour works as in Barony: it comes off `CombatManager.armorEffectiveness` (0.75) of a physical hit and the rest always lands; a successful block lets armour take the whole hit. Magic ignores armour. Shield armour counts only for blocked hits. Gear values by tier are listed in [Adventure revamp](Adventure-revamp.md#gear-baseline).

## Food

| Food | Healing/sec | Duration | Total |
|---|---:|---:|---:|
| Apple | 1 | 6s | 6 |
| Pear | 0.5 | 16s | 8 |
| Bread | 1 | 10s | 10 |
| Roasted mushroom | 1 | 12s | 12 |
| Cooked meat | 1 | 20s | 20 |

Food stacks to five; Crimson tonic stacks to three. All five foods are tier 1, so the loot profiles can drop any of them.

## Checking changes

There are no inventory or balance validation tools in the project. Check equip, pickup, eating and loot behaviour in Play Mode; the toolbar Debug button gives a quick arena.

The doorway prefab is `Levels/Dungeon1/Doors/Dungeon door.prefab`, with its leaf and lintel meshes and pixel texture in the same folder.
