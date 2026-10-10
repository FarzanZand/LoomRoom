# Stamina and blocking

The Table player cannot sprint: `PlayerData.canSprint` is off on the TablePlayer prefab (Barony has no sprint), so the sprint input does nothing and stamina is spent only on blocking, heavy attacks and jumping. Turn it back on in the prefab's data (Movement tab) to bring sprinting back; the sprint drain and recovery settings are still there.

Edit the TablePlayer prefab's data (`Assets/Game/Characters/Players/Table/TablePlayer.prefab`, Character component):

- Stats: Max Stamina (30) and Stamina Regen (8 per second).
- Movement tab, Stamina: sprint drain (6 per second, used only with sprint on), jump cost (0.5), regeneration delay (1 second) and the exhaustion recovery fraction (25%).

Edit `Assets/Game/Combat/Prefabs/CombatManager.prefab` for raised-shield drain (2 per second), blocked-hit cost (6) and heavy-release cost (8). Costs are paid only for the action itself: rear hits are not blocked, and light attacks are free. A charged release without enough stamina becomes a light attack. Exhaustion stops guarding until 25% stamina has come back. There are no timed parries. Mana is a separate resource.

The pool allows three heavy attacks without recovery, or fewer than five blocked hits once shield-hold costs are included. Recovering from empty takes about 4.75 seconds, including the delay.

Shield armour counts only while guarding, and only against successfully blocked frontal hits. Other armour always counts. See [Inventory and balance](Dungeon-inventory-and-balance.md#damage) for how armour reduces a hit.

HUD order is Health, Stamina, Mana. The bar prefabs are in `Assets/Game/UI/Prefabs/Vitals/`. The character panel also shows stamina. Shield tooltips explain their conditional armour.

There are no stamina validation tools in the project; check stamina, resource and armour behaviour in Play Mode.

Failed stamina actions show a centred "Not enough stamina" notification. Edit the wording and repeat interval on the scene's `PlayerVitalsUI` component; the look comes from `UI/Prefabs/Feedback/Pickup notification.prefab`. The repeat interval stops a held input from restarting the fade over and over.
