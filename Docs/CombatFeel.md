# Combat feel

Tap primary attack for a light strike; consecutive taps alternate between the two light swings (`SwingIndex`). Hold for at least `heavyChargeTime` (0.7 seconds by default), then release for a heavy strike if stamina is available; without enough stamina the release is a light strike. Secondary holds the shield. There is no timed block or parry: a raised shield blocks frontal hits for a stamina cost (see `Stamina-and-blocking.md`).

The pace follows the combat rework (`Combat-rework.md`, steps in `Rework-steps.md`):

- A click still strikes straight from the windup, but a new light swing can't start sooner than `swingInterval` (0.6 s) after the last one began, divided by the AttackSpeed stat. Only the start is held back; a windup in progress keeps reading the button for the charge.
- No hit stop: `hitStopEnabled` and `hitStopOnPlayerHurt` are off, so the blade passes straight through.
- `attackFromBlock` (on): clicking with the shield up lowers it for the swing, and it comes back up afterwards if block is still held. While the swing plays the player is not guarding.
- The table player walks backwards at 0.8 of normal speed (`PlayerData.backwardSpeed` on `TablePlayer.prefab`).
- Light swings use the original clips (`simpleSwings` on). The IK swing set (below) was rejected and stays off.

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

**IK swing set (rejected, kept for later).** `FirstPersonPlayer/IK swings` holds forehand and backhand windup, hold and release clips baked by `Characters/Animations/Editor/IkSwingBaker.cs` (two-bone IK along a blade path written in the file; rebaking overwrites the clips). The controller picks them when the `SimpleSwings` parameter is false (`CombatManager.simpleSwings` off); they run at `SwingSpeed` (the AttackSpeed stat only) and turn the camera with the blade (`ikSwingCameraRotation`, `ikSwingCameraCurve`; the backhand mirrors yaw and roll). The user did not like them; `simpleSwings` stays on until they are revisited.

Enemy attacks, hurt and death use `Assets/Game/Characters/Animations/HumanoidController.controller`; enemy attack states are tagged `Attack`.

## Enemy hit reactions

- Every damaging, unblocked hit adds a small, decaying directional bone rotation around the struck bone (no root displacement). `hitReactionAngle` is 35 degrees. Creatures without a humanoid rig (Crypt Rat) recoil as a whole: the model tips away from the blow.
- A damaging, unblocked hit that lands while the enemy is **not** mid-swing also fires the `Hurt` trigger, plays the full-body hurt clip at `enemyHurtAnimationSpeed`, and stops the enemy for `lightFlinchDuration` (0.25 s).
- A hit during a swing does not interrupt it: no `Hurt`, no knockback, and the swing direction stays committed. With `hitsInterruptWindup` on (off by default), a hit within `interruptWindow` (0.45 s) of the swing's start cancels it (the attack still goes on cooldown) and the enemy's next attack can't be interrupted, so steady hits never stunlock it.
- A heavy hit staggers (the enemy stands still for `heavyStaggerDuration`). If it lands mid-swing, the stagger starts when the swing ends, replacing the normal recovery if longer. After a stagger the enemy can't be staggered again for `enemyStaggerImmunity` (1.5 s).
- An enemy's hit can never land before `enemyMinimumWindup`. A clip event that fires earlier (`EnableHitbox` or `OnAttackHit`) is delayed to that moment, not dropped.
- The enemy's whoosh plays 0.14 s before its blow lands (`EnemyAttackRunner.HitAt`, read from the attack clip's hit event); a clip without a hit event plays it at the commit. `EnemySwingTrail` (Humanoid enemy base) draws a short pale streak on the weapon's head around that moment.
- `HelmetPop` (Humanoid enemy base): a hit within 0.3 m of the head bone knocks the helmet off 30% of the time; it flies off as a loose prop, stays on the floor, and the enemy loses 1 armour.

## Hits, impact and death

- `Hitbox` samples the weapon's path `weaponSweepSteps` (12) times per frame. The contact point is on the blade's line nearest the body, and `DamageInfo.SwingDirection` is the blade's travel between sweep steps.
- The player's melee line of sight starts at the camera (others from the chest), so the floor doesn't block a stab at something low.
- A contact flash (`Combat/Effects/Gore/Contact flash`) replaces the old dust puff in `hitParticlePrefabs`. `SlashLines` on the table player's hitbox draws a few thin white streaks along the arc while the hitbox is open and briefly after. The whole-body hit flash (`hitFlashEnabled`) is still on.
- Blood: `GoreManager` (prefab under Systems) sprays the victim's `CharacterData.gore` (`BloodKind` None, Blood, Slime, BoneDust; colour; amount) from the hit point along the swing, more for heavy hits and criticals. Drops that land (`GoreDroplets`) leave a stain on floors, walls and pillars that stays for the floor (cap 260). Skeletons spill bone chips and dust, never stains. `burstOnDeath` (the retired Green Slime) bursts the body into a spray and chunks. Each profile's kill sound plays on death (`Audio/Data/Kill bones`, `Kill flesh`, `Kill slime`).
- Kills are thrown: `EnemyRagdoll` gives every body part the same velocity, `ragdollImpulse` (5.5 m/s) along the blow and the swing, x1.4 for heavy hits and criticals, plus `ragdollUpwardImpulse` (3 m/s). Bodies pass through living enemies so none come to rest on a head. Skeletons burst into their own bones (`DeathBurst`: force 4.5, upward 1.8, scatter 1.4).
- Damage numbers appear where the blade struck, white, in `PIXEARG_`, drifting up slowly. Criticals and backstabs show "CRITICAL!" or "BACKSTAB!" above a bigger number that grows in over 0.3 s. The centre-screen result line is off (`DungeonCombatFeedback.resultLine`).
- Getting hit: the screen goes deep red (`hurtScreenAlpha` 0.6), holds for `hurtScreenHold` (0.1 s) and fades over `hurtScreenDuration` (0.4 s); the camera jolts away from the side the hit came from (`PlayerFX`).
- `CrosshairUI`: a small dot while the table player fights, a shield icon while blocking.
- All of the above (gore art, GoreManager, contact flash, slash lines, swing trails, helmets, recoil on rats and slimes, kill sounds, each enemy's blood) was wired once by the impact setup tool, retired on 2026-10-13 (running it again reset hand-tuned blood); tune the prefabs and assets directly.

## CombatManager controls

Defaults below are the values on `Combat/Prefabs/CombatManager.prefab`.

- `playerAttackSpeed` (0.85), `windupSpeed` (1.45), `releaseSpeed` (1.25), `heavyReleaseSpeed` (0.95): Animator playback timing, multiplied by the character AttackSpeed stat.
- `swingInterval` (0.6): shortest time between the starts of two light swings. `attackInputBuffer` (0.4): how long a click is remembered.
- `attackFromBlock` (on), `simpleSwings` (on), `ikSwingCameraRotation`, `ikSwingCameraCurve`: see above.
- `weaponSweepSteps` (12): samples per frame along the weapon's path.
- `heavyChargeTime`, `heavyDamageMultiplier`, `heavyStaminaCost`: charge threshold, damage, and stamina.
- `blockStaminaCost`, `shieldStaminaPerSecond`, `blockFrontalDot`: shield cost and which hits count as frontal. Blocking does not cancel the enemy attack.
- `enemyAttackCommitTime`, `enemyRecoveryTime`, `enemyCooldownMultiplier`, `enemyMinimumWindup`: normal attack cadence.
- `lightFlinchDuration` (0.25), `heavyStaggerDuration`, `enemyStaggerImmunity` (1.5): how long hits stop an enemy.
- `hitsInterruptWindup` (off), `interruptWindow` (0.45): windup interrupts.
- `personalSpace` (0.75): how close players and enemies get.
- `hitReactionAngle` (35), `hitReactionAttackSpeed`, `hitReactionDamping`, `hitReactionInfluenceDepth`, `hitReactionParentFalloff`: size, speed and spread of the directional bone overlay.
- `hitStopEnabled` (off), `hitStopOnPlayerHurt` (off), `lightHitStopDuration`, `lightHitStopTimeScale`, `heavyHitStopDuration`, `heavyHitStopTimeScale`: hit stop, if it is turned back on.
- `hitParticlePrefabs` (the contact flash), audio references: impact feedback.
- `enemyKnockbackScale` (0.25), `playerKnockbackSpeedLimit`, `playerKnockbackDecay`, `knockbackForceMultiplier`: bounded horizontal knockback.
- `ragdollImpulse` (5.5), `ragdollUpwardImpulse` (3): how far kills are thrown (speeds in m/s).
- `hurtScreenAlpha` (0.6), `hurtScreenHold` (0.1), `hurtScreenDuration` (0.4), `hurtScreenColor`: the red screen when hurt.

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
