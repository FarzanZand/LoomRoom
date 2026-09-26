# Combat feel

Tap primary attack for a light strike; consecutive taps alternate between the two light swings (`SwingIndex`). Hold for at least `heavyChargeTime` (0.7 seconds by default), then release for a heavy strike if stamina is available; without enough stamina the release is a light strike. Secondary holds the shield. There is no timed block or parry: a raised shield blocks frontal hits for a stamina cost (see `Stamina-and-blocking.md`).

## Animation editing

The first-person controller is `Assets/Game/Characters/Animations/FirstPersonTable.controller`; its clips live under `Assets/Game/Characters/Animations` (mostly `FirstPersonPlayer`). Code reads the arms animator by state tag only, on any layer:

| State | Tag |
|---|---|
| `Attack_Windup`, `Attack_Hold` | `Attack` |
| `Attack_Release` | `AttackRelease` |
| `Attack_Release_B` | `AttackReleaseAlt` |
| `Attack_HeavyRelease` | `AttackHeavyRelease` |
| Arm block states (LeftArm/RightArm layers) | `Block` |

All four attack tags count as attacking. The tag names are fields on `PlayerCombat`, next to the animator parameter names (`WindupSpeed`, `ReleaseSpeed`, `HeavyReleaseSpeed`, `HeavyStrike`, `AttackSpeed` and the rest).

Windup draws the hand up and slightly back. Release contains the cutting stroke, follow-through, and return to idle. The held pose has a small looping motion. Core motion is stored in clips; the runtime feeds Animator parameters and processes clip hitbox events (`AttackBegin`, `EnableHitbox`, `DisableHitbox`, `PlaySwingAudio`). Edit poses and hitbox event positions in the Animation window.

Enemy attacks, hurt and death use `Assets/Game/Characters/Animations/HumanoidController.controller`; enemy attack states are tagged `Attack`.

## Enemy hit reactions

- Every damaging, unblocked hit adds a small, decaying directional bone rotation around the struck bone (no root displacement, no attack cancellation).
- A damaging, unblocked hit that lands while the enemy is **not** mid-swing also fires the `Hurt` trigger and plays the full-body hurt clip, at `enemyHurtAnimationSpeed`.
- A hit during a swing never interrupts it: no `Hurt`, no knockback, and the swing direction stays committed.
- A heavy hit staggers (the enemy stands still for `heavyStaggerDuration`). If it lands mid-swing, the stagger starts when the swing ends, replacing the normal recovery if longer.
- An enemy's hit can never land before `enemyMinimumWindup`. A clip event that fires earlier (`EnableHitbox` or `OnAttackHit`) is delayed to that moment, not dropped.

## CombatManager controls

- `playerAttackSpeed`, `windupSpeed`, `releaseSpeed`, `heavyReleaseSpeed`: Animator playback timing, multiplied by the character AttackSpeed stat.
- `heavyChargeTime`, `heavyDamageMultiplier`, `heavyStaminaCost`: charge threshold, damage, and stamina.
- `blockStaminaCost`, `shieldStaminaPerSecond`, `blockDamageReduction`, `blockFrontalDot`: shield cost, reduction and which hits count as frontal. Blocking does not cancel the enemy attack.
- `enemyAttackCommitTime`, `enemyRecoveryTime`, `enemyCooldownMultiplier`, `enemyMinimumWindup`: normal attack cadence.
- `heavyStaggerDuration`: how long a heavy hit stops an enemy.
- `hitReactionAngle`, `hitReactionAttackSpeed`, `hitReactionDamping`, `hitReactionInfluenceDepth`, `hitReactionParentFalloff`: size, speed and spread of the directional bone overlay.
- `hitStopDuration`, `hitStopTimeScale`, audio and particle references: impact feedback.
- `playerKnockbackSpeedLimit`, `playerKnockbackDecay`, `knockbackForceMultiplier`: bounded horizontal knockback.

CharacterController owns player displacement. The companion Rigidbody is kinematic and the companion capsule is a trigger. Enemy root motion is disabled; the NavMesh motor owns movement. A knockback pauses an enemy's walk; the motor resumes the same destination when it ends.

## Play Mode validation (before the September 2026 review fixes)

Validated with the sword and shield against a skeleton, including an additional damage collider on the same enemy:

- Light strike: one 7-damage event. Charged strike: one 11.55-damage event and stamina cost.
- Regular shield block: zero damage.
- Incoming 10-damage test: 8 health lost after defense.
- Extreme force 10,000 with an upward direction: approximately 0.27 horizontal displacement and zero vertical displacement.
- Five seconds at rest: no upward drift.
- Clear melee sightline accepted; a temporary solid wall rejected it.
- Normal attack against a weakened enemy: health reached zero and brain entered Dead.
- Lethal damage with active knockback: navigation disabled cleanly, with no new exceptions or NavMesh resume errors.
- A nonlethal hit during Attack1 leaves Attack1 playing while a local bone reaction is visible, without entering Hurt or restarting Attack1.
- Lethal hits clear pending triggers; Dead guards prevent returning to idle.
