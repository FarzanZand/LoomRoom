# Item database

Open **Tools > Item Database** in Unity. The database edits the actual `ItemData` assets used by the game; there is no separate copy to keep in sync.

The left list shows icons and equipment categories. Search by name, tag, type or path; filter by item type, equipment slot and tag. **Needs attention** finds missing icons, duplicate names, missing hand models and invalid consumable effects. Select an item to edit all its fields in the right Inspector. **Save items** persists edits; normal Inspector Undo works.

Use **New item** to create an item in its type folder, or **Duplicate** to start from an existing item. Copies receive unique asset identities and names. Change the free-form **Tag** to group items without moving their files. The tooltip preview shows the item's current description, stats and effects.

**Move to type folder** organizes one item. **Organize shown** previews moves for the currently filtered active items in `Assets/Game/Items/Data`, then moves them while preserving GUIDs and references. The folders are Weapons, Shields, Armor, Consumables, Tools, Keys and Generic. Neither button renames item display names or replaces models.

**Archive** moves an item to `Items/Data/_Archive` without deleting it. The Archived toggle reveals these items and Restore returns them to their type folder. Existing loot table and scene references remain intact; remove those references yourself if the item should stop appearing. Archived items are excluded from catalog synchronization.

**Sync runtime catalog** updates the InventoryManager prefab and loaded scene instances with all active items for name-based lookup. Save the scene afterwards. Ordinary loot references already point directly to the item assets. Synchronizing does not add items to loot tables or player starting equipment.

## Models and pickup fallback

- **World Prefab** is the item mesh, also used in the hand.
- **Pickup Visual Prefab** optionally replaces only its dropped-world appearance.
- If both are empty, pickups use InventoryManager's **Default Pickup Visual**, currently `Assets/Game/Items/Prefabs/Props/Equipment loot pouch.prefab`.
- The pouch contains the requested Synty pouch as a nested prefab. It is a pickup fallback, not a substitute for a held sword or shield.
- Armor uses that pouch for pickups; hand weapons, shields and food retain their individual models.

See [Dungeon inventory and balance](Dungeon-inventory-and-balance.md) for gear stats, food tuning, loot tables, UI prefabs and audio locations.

## Validation

**Tools > Items > Validate item database** checks duplication, GUID preservation, referenced prefabs, Undo and authoring diagnostics using temporary test assets, then removes them. The report is written to `Temp/ItemDatabaseValidation.report`. It also synchronizes the active runtime catalog and saves loaded scenes after a successful run; use it with your intended scenes open.
