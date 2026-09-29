# Loot duplicate controls

Edit `Assets/Game/Core/Prefabs/InventoryManager.prefab`, under **Loot duplicates**:

- **Prevent Duplicate Tomes** defaults on.
- **Prevent Duplicate Equipment** defaults off; enable it to exclude owned weapons, shields and armor too.

Ownership means the exact item asset is in the dungeon player's bag, hotbar or an equipment slot when loot is rolled. Selling or dropping the last copy makes it eligible again. Different equipment assets remain eligible, including upgrades of the same equipment type.

Biome profiles and explicit reward tables filter owned items before weighted selection. The category's drop chance stays unchanged; if no eligible items remain, it yields nothing. Enemy carried-item rewards respect the same controls. Existing generated rewards, ground items and pickups are not retroactively removed.
