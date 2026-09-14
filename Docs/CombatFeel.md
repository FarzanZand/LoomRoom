# Combat feel

Tap primary attack for a light strike. Hold for at least `heavyChargeTime` (0.7 seconds by default), then release for a heavy strike if stamina is available. Secondary holds the shield; raising it just before impact gives a timed block.

## Animation editing

The first-person controller is `Assets/Animations/FirstPersonTable.controller`. Editable clips live in `Assets/Animations/Combat`: `Attack_Windup`, `Attack_Hold`, `Attack_Release`, `Attack_HeavyRelease`, and `Player_Hurt`. Enemy attacks and death use clips in `Assets/Animations/HumanoidController.controller`. Hits use a bounded directional bone overlay, so the attack clip continues uninterrupted. `Enemy_Hurt` remains an optional full-body reaction clip; ordinary enemy hits do not enter it.

Windup draws the hand up and slightly back. Release contains the cutting stroke, follow-through, and return to idle. The held pose has a small looping motion. Core motion is stored in clips; the runtime feeds Animator parameters and processes clip hitbox events. Enemy hit reactions add small, decaying rotations around the struck bone, with no root displacement or attack cancellation. The editor authoring tool uses a pose solver only to bake windup keys, with no runtime IK dependency.

Edit poses and hitbox event positions in the Animation window. `Tools > LoomRoom > Apply combat feel` regenerates the combat assets from the editor recipe, including the clips, so use direct clip editing for subsequent manual animation refinement.

## CombatManager controls

- `playerAttackSpeed`, `windupSpeed`, `releaseSpeed`, `heavyReleaseSpeed`: Animator playback timing, multiplied by the character AttackSpeed stat.
- `heavyChargeTime`, `heavyDamageMultiplier`, `heavyStaminaCost`: charge threshold, damage, and stamina.
- `timedBlockWindow`, `blockStaminaCost`: shield timing and stamina. Blocking does not cancel the enemy attack.
- `enemyAttackCommitTime`, `enemyRecoveryTime`, `enemyCooldownMultiplier`: normal attack cadence. Hit reactions do not change these timings.
- `hitReactionAngle`, `hitReactionAttackSpeed`, `hitReactionDamping`, `hitReactionInfluenceDepth`, `hitReactionParentFalloff`: size, speed and spread of the directional bone overlay.
- `hitStopDuration`, `hitStopTimeScale`, audio and particle references: impact feedback.
- `playerKnockbackSpeedLimit`, `playerKnockbackDecay`, `knockbackForceMultiplier`: bounded horizontal knockback.

CharacterController owns player displacement. The companion Rigidbody is kinematic and the companion capsule is a trigger. Enemy root motion is disabled; the NavMesh motor owns movement.

## Play Mode validation

Validated with the sword and shield against a skeleton, including an additional damage collider on the same enemy:

- Light strike: one 7-damage event. Charged strike: one 11.55-damage event and stamina cost.
- Regular shield block: zero damage. Timed block: zero damage without stamina cost.
- Incoming 10-damage test: 8 health lost after defense.
- Extreme force 10,000 with an upward direction: approximately 0.27 horizontal displacement and zero vertical displacement.
- Five seconds at rest: no upward drift.
- Clear melee sightline accepted; a temporary solid wall rejected it.
- Normal attack against a weakened enemy: health reached zero and brain entered Dead.
- Lethal damage with active knockback: navigation disabled cleanly, with no new exceptions or NavMesh resume errors.
- Inspected windup, held pose, cutting stroke, recovery, enemy recoil, and impact particles in gameplay captures.
- A nonlethal hit during Attack1 leaves Attack1 playing while a local bone reaction is visible. Recorded attack progress increased continuously from 0.123 to 0.325 while recoil decayed from 13.60 degrees to 0.32 degrees, without entering Hurt or restarting Attack1.
- Lethal hits clear pending triggers; Dead guards prevent returning to idle. Death1 remained active through a 5.5-second playback check.

Temporary test automation was removed after validation. Native audio playback was not separately recorded or audited.
