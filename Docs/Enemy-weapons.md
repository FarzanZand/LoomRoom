# Enemy weapons

Open an enemy prefab and select its root. **Enemy Weapon Loadout** controls the held weapon:

- **Has Weapon** enables or removes the held model.
- **Weapon** selects an `ItemData` from the item database. It uses the item's **World Prefab**, not its pickup-only pouch.
- **Grip** references the shared attachment profile. The component finds the Animator's humanoid right-hand bone automatically.
- **Refresh weapon preview** updates the model in Prefab Mode after changing the selection. Runtime spawning and weapon changes refresh automatically.

The current dungeon prefabs are `Assets/Game/Levels/Dungeon1/Enemies/Crypt Soldier.prefab` and `Crypt Warden.prefab`. The base `Assets/Game/Characters/Enemies/HumanoidEnemy.prefab` includes the same component, initially unarmed. Duplicate an enemy prefab or add Enemy Weapon Loadout to another humanoid enemy; the default Synty grip is assigned automatically.

Edit `Assets/Game/Characters/Resources/Synty humanoid weapon grip.asset` once to adjust the entire compatible rig family. Its position, rotation and scale are relative to the right-hand bone. The weapon follows that bone during animation and ragdoll movement. First-person player grip offsets are separate and are not changed by this profile. A different rig family or weapon modeling convention may need a different shared profile, not a correction on every enemy.

This component controls presentation. Damage, attack range, timing and animations remain on the enemy's CharacterData/Animator. Drops remain on DungeonLootDrop and the referenced loot table; selecting a visible weapon does not guarantee that item drops or add its player equipment stat bonuses to enemy damage.

Dungeon pickup message appearance and placement are editable in `Assets/Game/UI/Prefabs/Dungeon/Dungeon message.prefab`. Its anchor is horizontally centered at 35% of screen height, above the hotbar. The separate general notification (for example, Inventory full) uses `Assets/Game/UI/Prefabs/Feedback/Pickup notification.prefab` under the main scene UI canvas, also centered above the hotbar.
