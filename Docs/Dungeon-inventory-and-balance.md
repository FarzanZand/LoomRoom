# Inventory, equipment and dungeon balance

## Main editing locations

- `Assets/Game/Items/Data`: shared item definitions, grouped into Armor, Weapons, Shields and Consumables. No level-owned duplicate item definitions.
- `Assets/Game/UI/Prefabs/Items`: Inventory slot, Hotbar slot, Equipment slot. Edit sizes, images, fonts and hover feedback here. Full inventory and character panels are authored in `Room.unity` under **Inventory screen**.
- `Assets/Game/UI/Resources/UIFeedback.asset`: panel slide duration/ease, hover/press scale and AudioManager UI Library keys. Edit UI clips and volumes on **AudioManager > UI Library** (`inventoryOpen`, `inventoryClose`, `uiHover`, `equipItem`, `uiUnavailable`). The old `UI/Audio` assets remain as compatibility references; their gains no longer control these UI events. Pickup/eating use the SFX route.
- `Assets/Game/Items/Prefabs/Equipment`: hand models and generic armor models. `Prefabs/Consumables` holds food models.
- `Assets/Game/Items/Prefabs/Props/Equipment loot pouch.prefab`: nested Synty `SM_Item_Pouch_01`, with editable glint. Armor uses this as its pickup-only visual. InventoryManager's **Default Pickup Visual** makes meshless items use it automatically. Lookup order: item Pickup Visual Prefab, item World Prefab, manager fallback. Hand visuals are independent.
- `Assets/Game/Levels/Dungeon1/Prefabs/Props/Supply chest.prefab`: nested Synty `SM_Prop_Chest_01`, collider, interaction and editable lid rotation/timing. Level data references this prefab. Opening retains the chest, opens its lid and releases loot once.
- `Assets/Game/Levels/Dungeon1/Dungeon balance.asset`: per-floor enemy scaling. Base character values remain on the TablePlayer prefab's PlayerData and each dungeon enemy prefab's EnemyData.
- `Assets/Game/Items/LootTables/Dungeon * progression.asset`: current enemy, chest and barrel reward pools. Level/floor overrides remain supported.

Setup migrations are explicit editor operations. They do not run during imports or gameplay. The installer refuses to overwrite an already installed inventory setup; subsequent design changes belong in the authored assets.

## Controls and behavior

TAB opens/closes inventory. The bag slides from the left; equipment and stats slide from the right. Cursor is released and player movement/attacks stop, while enemies, damage and regeneration continue. Inventory does not modify time scale. Death, player swap, rapid toggles and closing clean up menu state and drag visuals.

Click bag gear or drag it onto a matching equipment slot to equip. Click an occupied equipment slot to unequip. Right-click an item for context actions. Mainhand and offhand must occupy the hotbar: equipping from the bag transfers into the old hand item's hotbar slot or an empty slot. A full hotbar without a replacement slot rejects the equip without deleting anything. Moving a held item back to the bag unequips it. Other armor remains in the bag while equipped and contributes its stats once.

`directToHotbar` defaults off. When enabled, pickups try the hotbar and fall back to the bag if the whole pickup cannot fit. Starter sword/shield explicitly go into the hotbar and equip on a new dungeon run. Descending preserves the current loadout.

Equippable consumables must first be held. `AnimationOnUse = Eat` keeps the item attached while the hand approaches the mouth, then consumes one and applies effects. `Idle` uses immediately from hand. Non-equippable consumables apply immediately from inventory without that animation. Food replaces current food regeneration instead of stacking. Death/new-run revival clears it. Tooltips show rate, duration, total healing and equipment comparisons.

## Baseline and sources

Player: **30 Health, 20 Mana, 5 base Damage, 0 base Armor**. Starter bronze sword and shield produce **10 Damage / 2 Armor**. Mana regenerates at 0.5/sec. Walking, jumping, blocking and weapon attacks do not consume mana. Table movement remains 4.5 walking and 5.85 sprinting. No timed parries.

| Slot | Bronze | Iron |
|---|---:|---:|
| Helm | 1 Armor | 2 Armor |
| Armor | 2 Armor | 3 Armor |
| Gloves | 1 Armor | 2 Armor |
| Boots | 1 Armor | 2 Armor |
| Sword | +5 Damage | +6 Damage |
| Shield | 2 Armor | 3 Armor |
| Full set | 10 Damage, 7 Armor | 11 Damage, 12 Armor |

The Health/Mana baseline follows [Barony's Warrior](https://barony.wiki.gg/wiki/Warrior). Sword bonuses use its excellent-quality [weapon values](https://barony.wiki.gg/wiki/Weapons). Iron equipment and both shields use the corresponding [armor values](https://barony.wiki.gg/wiki/Armor). Bronze helm/body/gloves/boots are custom lower-tier counterparts; Barony does not provide this exact full bronze set.

Damage is **max(0, Damage − Armor)**. Ordinary frontal shield guarding then halves that result. Barony's formula retains a portion of incoming damage; our simpler formula does not. Enemy damage is therefore adapted, not copied blindly.

| Enemy | Health | Damage | Armor | Attack cooldown |
|---|---:|---:|---:|---:|
| Crypt Mite | 30 | 8 | 1 | 1.3s |
| Crypt Soldier | 40 | 14 | 2 | 1.6s |
| Crypt Warden | 50 | 18 | 3 | 2.1s |

Mite baseline follows the [Barony rat](https://barony.wiki.gg/wiki/Rat). Soldier/warden damage is adapted for flat armor. Per floor after the first: +2 damage, +12% of base health, +0.25 armor. Heavy threats remain dangerous after upgrades. Complete iron armor can negate weak mite hits at early depths; this is an intentional consequence of the requested flat formula, not a minimum-damage exception.

## Food and reward economy

| Food | Healing/sec | Duration | Total |
|---|---:|---:|---:|
| Apple | 1 | 6s | 6 |
| Pear | 0.5 | 16s | 8 |
| Bread | 1 | 10s | 10 |
| Roasted mushroom | 1 | 12s | 12 |
| Cooked meat | 1 | 20s | 20 |

Food stacks to five; Crimson tonic stacks to three and heals 12 immediately after use completes. New runs start with one tonic and one apple plus equipped bronze sword/shield. Safe supplies use apples rather than a full potion at every rest location.

Enemy reward chance: 45%; successful rewards contain a supply and have a 25% equipment chance (about 11% gear per kill). Chest: one equipment roll and a 60% supply chance. Breakables: 35% supply chance and 4% equipment. Loot drops on the floor where the enemy dies (`CombatManager > Lootable Corpses` is off). All sources share one gear pool: bronze armour pieces are common early, the starting sword and shield are rare duplicates, knives and the mace add variety, bronze fades with depth while iron (floor 2+) and the grave mace (floor 4+) rise.

10,000 seeded enemy rolls yielded 64.9% empty rewards and 3.44 potential healing per kill (before health caps, wasted food or replacement). The previous much more generous supply tables remain available as alternative assets, but are no longer Dungeon1's active defaults.

## Verification

The designer database is available under **Tools > Item Database**. See [Item database](Item-database.md) for filters, editing, safe organization, pickup fallback and runtime catalog synchronization.

Doorways use the editable `Dungeon door.prefab`, including stationary **Doorway masonry lintel**. Its wall texture is cropped to the upper part of a full wall, preserving brick scale rather than compressing the texture into the short header. Door span follows cell width; doorway height stays fixed. The lintel fills to the ceiling and remains solid when the gate opens.

The validation tools this pass originally used (`DungeonInventoryValidation`, `LiveInventoryValidation`, `DungeonReviewValidation` under **Tools > Table Levels**) are no longer in the project; re-check these behaviours manually in Play Mode.

Numerical validation is a starting balance pass, not a substitute for repeated human playthroughs. Encounter density, healing availability and heavy attack pacing remain editable for playtesting.

Food model update: Apple, Pear, Roasted mushroom, Cooked meat and Bread use nested Synty source prefabs in `Items/Prefabs/Consumables`. Bread uses a 64px point-filtered icon rendered from its actual model and supply weight 4 in enemy/chest/barrel progression tables. The 10,000-roll healing figure above predates Bread.

Current food loot is Apple, Bread and Cooked meat. Pear and Roasted mushroom remain editable content but are excluded from loot tables for now.
