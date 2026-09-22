# Stamina and blocking

Edit `Assets/Game/Players/Table/TablePlayer.asset` for stamina capacity (30), regeneration (8/second), sprint drain (6/second), regeneration delay (1 second), and exhaustion recovery fraction (25%). Mana remains a separate resource.

Edit `Assets/Game/Combat/Prefabs/CombatManager.prefab` for raised-shield drain (2/second), blocked-hit cost (6), and heavy-release cost (8). The Room scene uses this manager. Costs are paid only for the relevant action: idle sprint input does not drain stamina, rear hits are not blocked, and light attacks are free. A charged release with insufficient stamina becomes a light attack. Exhaustion prevents sprinting and guarding until 25% stamina returns. No timed parries.

The pool allows five seconds of continuous sprinting, three heavy attacks without recovery, or fewer than five blocked hits once shield-hold costs are included. Resting recovers from empty in roughly 4.75 seconds, including the delay. Full recovery is deliberately quicker than sustained sprint depletion; retreating, lowering the shield, and light attacks create recovery opportunities.

Shield Armor is conditional: it contributes only while guarding, and only to successfully blocked frontal hits when resolving damage. Other armor stays active. Damage still uses Damage minus Armor, followed by the existing block damage reduction. Stat enum value 1 is now named Armor; existing serialized item values are preserved.

HUD order is Health, Stamina, Mana. Reusable bar prefabs are in `Assets/Game/UI/Prefabs/Vitals/`. Edit these for bar artwork and sizing. The character panel also displays stamina. Shield tooltips explain their conditional armor.

Explicit editor checks: `Temp/StaminaSetup.request` authors missing HUD content without running on normal imports. `Temp/StaminaValidation.request` runs the action/resource/armor checks in Play mode and saves a HUD capture to `Temp/StaminaHUD.png`.

Failed stamina actions show a centered "Not enough stamina" notification. Edit the wording and repeat interval on the scene PlayerVitalsUI component; presentation comes from the existing Pickup notification prefab under UI/Prefabs/Feedback. The cooldown prevents held inputs from continually restarting the fade.
