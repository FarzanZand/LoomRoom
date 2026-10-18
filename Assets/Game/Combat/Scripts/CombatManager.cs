using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

// Central toggles and tuning for combat feel. Hit stop lives here too, so there is
// exactly one entry point and a hit can never freeze the game twice.
public class CombatManager : Singleton<CombatManager>
{
    // Inspector: tabs (Player, Enemies, Hits, Death) of foldouts. Field names are what the prefab and scene save;
    // grouping them never changes a value.

    // ── Player: Attacks ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Range(.5f,2f)] public float playerAttackSpeed = 1.12f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(0)] public float attackInputBuffer = .22f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Range(1,16), Tooltip("Samples per frame along the weapon's path, so a fast strike can't skip past an enemy.")] public int weaponSweepSteps = 12;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(0), Tooltip("Shortest time between the starts of two light swings, at attack speed 1 (about 0.6 s). A faster weapon shortens it.")]
    public float swingInterval = .6f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Tooltip("Attacking while the shield is up lowers it for the swing and raises it again afterwards if block is still held.")]
    public bool attackFromBlock = true;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Tooltip("Pressing block during a windup (before the blade can hit) cancels the swing: no damage, the shield comes up at once. The next swing still waits for Swing Interval. Off (the user found it odd): a swing once started always strikes.")]
    public bool blockCancelsWindup = false;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Range(0f, 1f), Tooltip("How much of an attack's animation plays before block can raise the shield again (0.75 = the last quarter). 1 = only once the swing is over.")]
    public float blockFromAttackAt = .75f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Min(0f), Tooltip("Seconds after the shield comes up before it stops hits (0 = at once). Raise it if swing-block-swing feels too safe.")]
    public float blockRaiseDelay = 0f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(.1f), Tooltip("Speed of the windup before a swing (light or charged), 1 = as the clip. Times the attack speed stat.")] public float windupSpeed = 1.2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(.1f), Tooltip("Speed of a light swing's strike (Simple swing set), 1 = as the clip. Times the attack speed stat.")] public float releaseSpeed = 1.25f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(.1f), Tooltip("Speed of the charged strike (the heavy swing once let go), 1 = as the clip, higher is faster. Times the attack speed stat and Heavy Release Speed Curve.")] public float heavyReleaseSpeed = .95f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Attacks")]
    [Min(.1f)] public float enemyHurtAnimationSpeed = 1.3f;

    // ── Player: Swing set ──
    // Which light attack the player has (serialized by number, append only).
    public enum SwingSet { Simple = 0, IkSwings = 1, Combo = 2, Classic = 3, Classic2 = 4, Combo2 = 5 }
    // One swing set per weapon type: the held weapon's category picks it (SwingSetFor).
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), FormerlySerializedAs("swingSet")]
    [Tooltip("For swords. Simple: the original light swings (Attack_Swing_A/B), with charge and heavy swings. IK Swings: the baked IK " +
        "set (FirstPersonPlayer/IK swings): a visible windup, a strike in a few frames and a held follow-through, " +
        "forehand and backhand. Combo: the three-hit combo converted from the Unreal game (Combo swing left, swing right, " +
        "stab; FirstPersonPlayer/Combo), one hit per press, no charge or heavy swing. Classic: the same combo with its " +
        "left and right swings reworked into horizontal blows with a held windup (FirstPersonPlayer/Classic). Classic 2: " +
        "fast, low, flat swipes from screen edge to screen edge with no visible windup (FirstPersonPlayer/Classic2). Combo 2: " +
        "the Combo's two swings, then the Simple light swing as the third hit (FirstPersonPlayer/Combo2).")]
    public SwingSet swordSwingSet = SwingSet.Combo;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set")]
    [Tooltip("For maces (the same sets as for swords).")]
    public SwingSet maceSwingSet = SwingSet.Simple;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set")]
    [Min(0f), Tooltip("How hard a sword's hits make an enemy flinch: times its body's recoil and lean (Hits > Impact) and how long it is held (Heavy Stagger Duration, Light Flinch Duration); the push is not changed. 1 = as those say, 0 = no flinch (no recoil or hurt animation).")]
    public float swordStaggerStrength = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set")]
    [Min(0f), Tooltip("The same for maces.")]
    public float maceStaggerStrength = 1f;

    static bool IsMace(ItemData weapon) => weapon != null && weapon.IsWeapon && weapon.weaponCategory == WeaponCategory.Mace;
    public float StaggerStrengthFor(ItemData weapon) => weapon == null || !weapon.IsWeapon ? 1f : IsMace(weapon) ? maceStaggerStrength : swordStaggerStrength;

    public SwingSet SwingSetFor(ItemData weapon) => IsMace(weapon) ? maceSwingSet : swordSwingSet;
    // The table player's swing set now: the one for the weapon in their right hand.
    public SwingSet swingSet
    {
        get
        {
            var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) as Player : null;
            return SwingSetFor(player != null && player.Equipment != null ? player.Equipment.Get(EquipmentSlot.RightHand) : null);
        }
    }
    static bool IsCombo(SwingSet set) => set == SwingSet.Combo || set == SwingSet.Classic || set == SwingSet.Classic2 || set == SwingSet.Combo2;
    public bool simpleSwings => swingSet != SwingSet.IkSwings;
    public bool comboAttack => IsCombo(swingSet);
    // For the inspector: show the combo and IK settings when any weapon type uses them.
    bool AnyCombo => IsCombo(swordSwingSet) || IsCombo(maceSwingSet);
    bool AnyIk => swordSwingSet == SwingSet.IkSwings || maceSwingSet == SwingSet.IkSwings;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Range(.3f, 1.5f), Tooltip("How fast the combo's hits play (1 = as in the Unreal clips). The attack speed stat multiplies it.")]
    public float comboSpeed = .7f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Range(.3f, 2f), Tooltip("Speed of the combo's first hit, times Combo Speed (1 = as Combo Speed).")]
    public float comboFirstHitSpeed = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Range(.3f, 2f), Tooltip("Speed of the combo's second hit, times Combo Speed.")]
    public float comboSecondHitSpeed = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Range(.3f, 2f), Tooltip("Speed of the combo's third hit (the finisher), times Combo Speed.")]
    public float comboFinisherSpeed = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Range(0f, 3f), Tooltip("How much the view moves with each combo hit, times the camera curves in its clip (SwingCamPitch, SwingCamYaw, SwingCamRoll: worked out from the hand's motion by UnrealComboConverter, so a left swing turns and leans the view left, a right one right, a stab nods it, a chop drops it; edit them in the Animation window). 0 = none. Also sets which way each hit's strike kick goes.")]
    public float comboCameraStrength = 1f;
    public float ComboHitSpeed(int hit) => hit <= 0 ? comboFirstHitSpeed : hit == 1 ? comboSecondHitSpeed : comboFinisherSpeed;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [FormerlySerializedAs("comboChainAfter"), Min(0f), Tooltip("Seconds from the start of a combo hit before the next one can start (a press before then waits for it). Plain seconds: attack speed and Combo Speed don't change it.")]
    public float comboHitCooldown = .43f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("Seconds from the start of the finisher (the third hit) before the combo can start again.")]
    public float comboFinisherCooldown = .9f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Tooltip("The thin streaks (SlashLines) along the blade during combo hits. Off: they looked like a rake on the long combo arcs.")]
    public bool comboSlashLines = false;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Tooltip("The combo's last hit (the stab) lands as a heavy hit: heavy damage, a heavy stagger, a harder push, the heavy swing sound, a longer pause and a bigger camera kick.")]
    public bool comboFinisherHeavy = true;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(1f), Tooltip("Damage multiplier of the combo's finisher (instead of Heavy Damage Multiplier, which charged heavy attacks keep).")]
    public float comboFinisherDamage = 1.3f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(.05f), Tooltip("A press released within this many seconds is a combo hit; held longer, it becomes the simple swing set's charged attack (windup, hold, heavy release after Heavy Charge Time).")]
    public float comboHoldForHeavy = .12f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Tooltip("A short pause when a combo hit lands on an enemy, so the blade is felt to bite (whatever Hit Stop Enabled says).")]
    public bool comboHitStop = true;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("Seconds of the pause for the combo's first two hits.")]
    public float comboHitStopDuration = .065f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("Seconds of the pause for the finisher.")]
    public float comboFinisherHitStop = .13f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("Times the strike lunge and camera kicks for combo hits: the body follows the swing.")]
    public float comboStrikeScale = 1.8f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("Times the strike lunge and camera kicks for the finisher (it was Heavy Strike Scale x Combo Strike Scale, which shook the view too hard). Its pause shakes like a light hit and it has no heavy impact shudder.")]
    public float comboFinisherStrikeScale = 2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyCombo))]
    [Min(0f), Tooltip("A pause this long after a hit starts the combo again from its first hit.")]
    public float comboResetTime = 1.1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyIk))]
    [Tooltip("Camera turn (pitch, yaw, roll in degrees) at the peak of an IK forehand; the backhand mirrors yaw and roll.")]
    public Vector3 ikSwingCameraRotation = new Vector3(.4f, -1.2f, .6f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing set"), ShowIf(nameof(AnyIk))]
    [Tooltip("IK swing camera motion over the release clip: peaks with the strike, eases off through the follow-through.")]
    public AnimationCurve ikSwingCameraCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.12f, 1f), new Keyframe(.6f, .35f), new Keyframe(1f, 0f));

    // ── Player: Strike feel ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Min(0), Tooltip("Metres the player steps into a swing when its hitbox opens (personal space still stops them at an enemy). 0: no lunge.")]
    public float strikeLunge = .25f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Min(.01f), Tooltip("Seconds the lunge takes.")]
    public float strikeLungeTime = .12f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Min(0), Tooltip("Times the lunge and the camera kicks for a heavy swing.")]
    public float heavyStrikeScale = 2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Tooltip("Camera kick in degrees (pitch, yaw, roll) when a swing's hitbox opens; the alternate swing mirrors yaw and roll.")]
    public Vector3 strikeKick = new Vector3(.8f, 0f, .4f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Tooltip("Extra camera kick when the swing lands on an enemy.")]
    public Vector3 hitKick = new Vector3(1.2f, 0f, .6f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Strike feel")]
    [Min(1), Tooltip("How fast a camera kick settles (higher is snappier).")]
    public float kickRecovery = 14f;

    // ── Player: Swing camera ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    public bool swingCameraMotionEnabled = true;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Local pitch, yaw and roll in degrees for each authored swing.")]
    public Vector3 lightSwingCameraRotation = new Vector3(.3f, .8f, -.35f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    public Vector3 alternateSwingCameraRotation = new Vector3(.5f, .8f, -.55f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    public Vector3 heavySwingCameraRotation = new Vector3(2f, .4f, -.65f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Camera motion over normalized release animation time, returning to zero at the end.")]
    public AnimationCurve swingCameraCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.28f, 1f), new Keyframe(1f, 0f));
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Heavy release: anticipation, strike, then a slower recovery.")]
    public AnimationCurve heavySwingCameraCurve = new AnimationCurve(new Keyframe(0f, 0f),
        new Keyframe(.18f, -.03f), new Keyframe(.4f, 1f), new Keyframe(.47f, 1f), new Keyframe(1f, 0f));
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("How quickly camera motion follows the animation and returns to neutral.")]
    [Min(1)] public float swingCameraBlendSpeed = 18f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Speed multiplier over the light release by normalized clip time: fast in, slower follow-through.")]
    public AnimationCurve releaseSpeedCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(.45f, 1.15f), new Keyframe(1f, .7f));
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Speed multiplier over the heavy release. The heavy swing clip already carries its coil / strike / hang / recovery timing, so keep this near 1.")]
    public AnimationCurve heavyReleaseSpeedCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.4f, 1.1f), new Keyframe(1f, .9f));
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Swing camera")]
    [Tooltip("Tension-layer spike when a heavy hit lands, read as the arm shuddering from the impact.")]
    [Range(0,1)] public float heavyImpactShudder = .7f;

    // ── Player: Charged strike ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(.1f)] public float heavyChargeTime = .7f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(1)] public float heavyDamageMultiplier = 1.65f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(1)] public float heavyKnockbackMultiplier = 1.35f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(0), Tooltip("Stamina spent when releasing a fully charged heavy attack. Insufficient stamina releases a light attack instead.")] public float heavyStaminaCost = 8f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Tooltip("Extra recovery after a heavy hit. Does not interrupt an already committed enemy swing.")]
    [Min(0)] public float heavyStaggerDuration = .45f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(0), Tooltip("After a stagger, seconds the enemy can't be staggered again, so charged hits can't chain it into never attacking.")]
    public float enemyStaggerImmunity = 1.5f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charged strike")]
    [Min(0), Tooltip("Seconds an enemy stands still after a light hit. 0: the hurt reaction plays but never delays its next attack, so spamming light hits can't stunlock it.")]
    public float lightFlinchDuration = .25f;

    // ── Player: Charge feel ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Seconds an attack must be held before charge zoom and shake begin. Capped below the full-charge time.")]
    [Min(0f)] public float chargeCameraDelay = .2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Normalized heavy release time when charge zoom starts returning. Matches the end of anticipation.")]
    [Range(0f, .95f)] public float heavyZoomReturnStart = .18f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Normalized heavy release time when charge zoom reaches normal. Scales with attack animation speed.")]
    [Range(.01f, 1f)] public float heavyZoomReturnEnd = .58f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Zoom smoothing time for a cancelled charge or light release, in seconds.")]
    [Min(.01f)] public float chargeZoomReturnSmoothing = .12f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Hold-loop speed at full charge: the arm settles into tension instead of breathing normally.")]
    [Range(.05f,1f)] public float holdSpeedAtFullCharge = .3f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Weight of the ChargeAdditive layer at full charge (arm pulled further back, trembling).")]
    [Range(0,1)] public float chargeTension = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Field-of-view narrowing at full charge, in degrees.")]
    [Range(0,15)] public float chargeFovPull = 4f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Extra camera noise amplitude at full charge.")]
    [Range(0,3)] public float chargeShake = .45f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    [Tooltip("Camera kick the moment the charge completes.")]
    [Range(0,2)] public float chargeReadyKick = .3f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Charge feel")]
    public AudioData chargeReadyAudio;

    // ── Player: Block ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Tooltip("Dot product threshold: a hit is frontal (blockable) when dot(facing, hitDir) is below this.")]
    [Range(-1f, 1f)] public float blockFrontalDot = -0.3f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Min(0), Tooltip("Stamina spent on each successfully blocked hit.")]
    public float blockStaminaCost = 6f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Block")]
    [Min(0), Tooltip("Stamina drained each second while holding the shield up.")]
    public float shieldStaminaPerSecond = 2f;

    // ── Player: Shield bash ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Tooltip("Holding block and pressing attack bashes forward with the shield: a fast dash that stops at the first enemy in the way, hits it and stuns it. Needs a shield in hand. While it cools down, block and attack swings as usual.")]
    public bool enableShieldBash = true;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Seconds before the bash can be used again.")]
    public float shieldBashCooldown = 5f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(.5f), Tooltip("Dash speed in metres per second.")]
    public float shieldBashSpeed = 15f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(.5f), Tooltip("Longest dash in metres, when nothing is in the way.")]
    public float shieldBashDistance = 6f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Seconds the dash waits after the press before it moves (the shield coming forward).")]
    public float shieldBashWindup = .06f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Seconds after the hit (or the end of the dash) before you can attack or block again.")]
    public float shieldBashRecovery = .3f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Range(0f, 90f), Tooltip("How far off straight ahead (degrees) an enemy can be and still be hit by the dash.")]
    public float shieldBashAngle = 35f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Damage of the hit before armour: Holy damage, like a spell it ignores the enemy's armour.")]
    public float shieldBashDamage = 6f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Extra damage per point of damage the player's own armour stops (armour x Armor Per Point, so 5 armour counts as 1).")]
    public float shieldBashDamagePerArmor = 1.5f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Seconds the struck enemy stands stunned (its swing is cancelled). Its usual stagger immunity follows.")]
    public float shieldBashStun = 1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Push on the struck enemy. Small: it stays standing in front of you for the follow-up.")]
    public float shieldBashKnockback = 1.5f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Min(0f), Tooltip("Stamina the bash costs.")]
    public float shieldBashStaminaCost = 10f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [ColorUsage(false, true), Tooltip("The shield's glow during the dash (HDR: above 1 glows).")]
    public Color shieldBashGlow = new(1.8f, 1.55f, 1.05f);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    [Tooltip("Gold streaks that rush past during the dash, made for it (Combat/Effects/Shield bash streaks).")]
    public GameObject shieldBashStreaks;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    public AudioData shieldBashAudio;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Shield bash")]
    public AudioData shieldBashHitAudio;

    // ── Player: Getting hit ──
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Range(0,4)] public float landedCameraKick = .65f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Range(0,6)] public float hurtCameraKick = 2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Range(0,4)] public float blockCameraKick = .8f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Range(0,1)] public float hurtScreenAlpha = .22f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Min(.05f), Tooltip("Seconds the hurt screen takes to fade out.")]
    public float hurtScreenDuration = .28f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Min(0), Tooltip("Seconds the hurt screen stays at full strength before it fades.")]
    public float hurtScreenHold = .1f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    public Color hurtScreenColor = new(.8f,.12f,.1f,1);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    public Color blockScreenColor = new(.55f,.7f,.8f,1);
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    public AudioData blockAudio;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    public AudioData playerHurtAudio;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    public GameObject blockParticlePrefab;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Min(.1f)] public float impactParticleLifetime = 2f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Min(0)] public float blockHitStopScale = .65f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Tooltip("Maximum horizontal knockback speed, in world units per second. Never changes vertical velocity.")]
    [Min(0)] public float playerKnockbackSpeedLimit = 2.5f;
    [TabGroup("Tabs", "Player"), FoldoutGroup("Tabs/Player/Getting hit")]
    [Tooltip("How quickly player knockback loses speed. A 2.5 speed kick at 12 decay travels about 0.26 units.")]
    [Min(.1f)] public float playerKnockbackDecay = 12f;

    // ── Enemies: Attack rhythm ──
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("Seconds after the swing starts during which the enemy still turns to track the target (windupTrackSpeed). After this the swing direction is committed.")]
    [Min(.1f)] public float enemyAttackCommitTime = .4f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("Seconds the enemy stands after a swing before it moves again.")]
    [Min(0)] public float enemyRecoveryTime = .18f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("Multiplier on every enemy attack cooldown.")]
    [Min(.1f)] public float enemyCooldownMultiplier = 1f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("The hit can never land sooner than this after the swing starts, even if the clip's event is earlier.")]
    [Min(0)] public float enemyMinimumWindup = .3f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("An enemy's body glows briefly when it starts an attack, so the windup reads at any distance (CharacterFX.Tell).")]
    public bool enemyAttackTell = true;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm"), ShowIf(nameof(enemyAttackTell))]
    [ColorUsage(false, true)] public Color enemyTellColor = new Color(1.8f, 1.15f, .6f);
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm"), ShowIf(nameof(enemyAttackTell))]
    [Min(0)] public float enemyTellDuration = .14f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm"), ShowIf(nameof(enemyAttackTell)), Tooltip("Optional sound at the start of every enemy attack.")]
    public AudioData enemyTellAudio;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("Max angle between where the enemy committed to swing and the target for the hit to land.")]
    [Range(10,100)] public float enemyHitFacingAngle = 60f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Tooltip("Playback speed of enemy attack animations (the AttackSpeed animator parameter).")]
    [Range(.5f,2.5f)] public float enemyAttackAnimationSpeed = 1.35f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Min(0), Tooltip("How far the sound of a landed hit carries: enemies within this (plus their hearing) come to look, so fights draw in the next room. 0: fights are silent.")]
    public float fightNoiseRadius = 8f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Attack rhythm")]
    [Range(0, 1), Tooltip("Share of a sound's reach that carries through a wall or closed door. Low keeps enemies in other rooms from hearing fights and footsteps through stone.")]
    public float wallMuffle = .3f;

    // ── Enemies: Interrupts ──
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Interrupts")]
    [Tooltip("A hit early in an enemy's windup cancels that attack. After an interrupt the enemy's next attack can't be interrupted, so it can't be stunlocked.")]
    public bool hitsInterruptWindup = false;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Interrupts")]
    [Min(0), Tooltip("Seconds from the start of an enemy's swing during which a hit interrupts it.")]
    public float interruptWindow = .45f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Interrupts")]
    [Min(1)] public float heavyRecoilMultiplier = 1.6f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Interrupts")]
    [Min(1)] public float heavyRecoilDurationMultiplier = 1.5f;

    // ── Enemies: Stealth ──
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Stealth")]
    [SerializeField, Range(0, 1), Tooltip("How close the player can get behind an enemy before it notices, as a share of its close radius.")] float unseenCloseScale = .6f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Stealth")]
    [SerializeField, Range(0, 1), Tooltip("The same while sneaking.")] float sneakCloseScale = .2f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Stealth")]
    [SerializeField, Range(0, 1), Tooltip("Enemy sight range while the player sneaks, as a share of normal.")] float sneakSightScale = .6f;

    // ── Enemies: Spacing ──
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Spacing")]
    [Min(0), Tooltip("No one stands closer than this to an enemy (centre to centre): the player cannot walk further in, and an enemy that is not swinging steps back out.")]
    public float personalSpace = 1.1f;

    // ── Enemies: Knockback ──
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Knockback")]
    public bool knockbackEnabled = true;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Knockback")]
    [Tooltip("Off: the player's hits never push enemies back. Their hits on the player still do.")]
    public bool enemiesKnockedBack = true;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Knockback")]
    [Min(0), Tooltip("Scales the push enemies take from the player's hits (on top of Knockback Force Multiplier). Small, so a hit jolts them without carrying them out of their attack range.")]
    public float enemyKnockbackScale = .25f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Knockback")]
    [Tooltip("Scales every knockback force in the game.")]
    public float knockbackForceMultiplier = 1f;
    [TabGroup("Tabs", "Enemies"), FoldoutGroup("Tabs/Enemies/Knockback")]
    [Tooltip("Seconds a knocked-back NavMesh character slides before regaining control.")]
    public float knockbackDuration = 0.25f;

    // ── Hits: Reach ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Reach")]
    [Min(1f), Tooltip("Reach multiplier for player/enemy melee queries and enemy attack selection.")]
    public float meleeReachMultiplier = 1.2f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Reach")]
    [Min(1f), Tooltip("Thickness multiplier for melee hit queries; also widens fallback enemy hit arcs.")]
    public float meleeWidthMultiplier = 1.25f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Reach")]
    [Min(0f), Tooltip("Player swings: every swing also hits anything out to this far from the player's chest, in the direction the blade is sweeping, so every swing and every weapon reaches the same distance. 0 = only where the blade goes.")]
    public float playerMeleeReach = 2.5f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Reach")]
    [Min(.05f), Tooltip("Thickness of that reach (radius of the capsule out from the chest).")]
    public float playerMeleeReachRadius = .35f;

    // ── Hits: Critical hits and backstabs ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    public bool criticalHitsEnabled = true;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Range(0f, 1f)] public float critChance = .08f;
    [Range(0f, 1f), Tooltip("The most a critical chance can be, whatever gear, Dexterity and skills add.")] public float critChanceCap = .6f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Min(1f)] public float critMultiplier = 1.75f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Min(1f), Tooltip("Hit stop length multiplier for a critical hit.")]
    public float critHitStopScale = 2.5f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    public AudioData critAudio;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Tooltip("Hitting an enemy that is not chasing or attacking, or one facing away, is a backstab.")]
    public bool backstabsEnabled = true;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Tooltip("The spell gear with \"chance on hit to cast Fire Circle\" casts under the target (its own power).")]
    public SpellDefinition fireCircleOnHit;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Min(1f)] public float backstabMultiplier = 2.5f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Range(-1f, 1f), Tooltip("A hit counts as from behind when dot(victim forward, attack direction) is above this.")]
    public float backstabBehindDot = .35f;

    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Min(1f)] public float backstabHitStopScale = 3f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    public AudioData backstabAudio;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Critical hits and backstabs")]
    [Min(1f), Tooltip("Size multiplier of the damage number for critical hits and backstabs.")]
    public float critNumberScale = 1.6f;

    // ── Hits: Armor ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Armor")]
    [Tooltip("Armor effectiveness: armor is taken off this share of a hit, the rest always lands, so armor never makes anyone untouchable. A successful block uses the full hit (1).")]
    [Range(0f, 1f)] public float armorEffectiveness = .65f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Armor")]
    [Min(0f), Tooltip("Damage one point of armour stops. Armour numbers are five times what they were before 2026-10-15 (a Leather Helm is 5), so 0.2 keeps the old balance and each rarity step's +20% shows as whole points.")]
    public float armorPerPoint = .2f;
    public static float ArmorPerPoint => HasInstance ? Instance.armorPerPoint : .2f;

    // ── Hits: Hit stop ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Tooltip("Off: hits pass straight through. On: the game slows for a moment on each hit.")]
    public bool hitStopEnabled = false;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [FormerlySerializedAs("hitStopDuration"), Min(0), Tooltip("Seconds the game slows when a light attack lands.")]
    public float lightHitStopDuration = 0.06f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [FormerlySerializedAs("hitStopTimeScale"), Range(0f, 0.5f)]
    [Tooltip("Time scale during a light hit stop. A hair above zero keeps animations creeping, which reads better than a hard freeze.")]
    public float lightHitStopTimeScale = 0.05f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Min(0), Tooltip("Seconds the game slows when a heavy (charged) attack lands.")]
    public float heavyHitStopDuration = 0.11f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Range(0f, 0.5f), Tooltip("Time scale during a heavy hit stop.")]
    public float heavyHitStopTimeScale = 0.02f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Min(0), Tooltip("Seconds time takes to ramp back to full speed after the stop, instead of snapping. Softens the release.")]
    public float hitStopRecovery = 0.06f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Min(0), Tooltip("How far the struck character's model shakes during the stop, in metres at its own scale. Heavy hits shake twice as far.")]
    public float hitStopShake = 0.035f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit stop")]
    [Tooltip("Also hit-stop when an enemy lands a hit on the player.")]
    public bool hitStopOnPlayerHurt = false;

    // ── Hits: Hit reaction ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    public bool hitReactionEnabled = true;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    [Tooltip("Peak rotation angle (degrees) applied to the bone closest to the hit.")]
    public float hitReactionAngle = 20f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    [Tooltip("How quickly the bones move into the reaction pose. Lower = smoother snap-in.")]
    public float hitReactionAttackSpeed = 20f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    [Tooltip("How quickly the offset decays back to the animated pose.")]
    public float hitReactionDamping = 8f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    [Tooltip("How many parent bones above the hit bone also receive an offset.")]
    public int hitReactionInfluenceDepth = 3;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit reaction")]
    [Tooltip("Fraction of strength passed to each successive parent bone.")]
    [Range(0f, 1f)] public float hitReactionParentFalloff = 0.45f;

    // ── Hits: Impact ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Impact")]
    [Range(0f, 3f), Tooltip("How hard a hit lands on an enemy: scales its bone recoil, its body lean and the push it takes (1 = as before).")]
    public float hitImpactStrength = 1.6f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Impact")]
    [Range(0f, 1f), Tooltip("How much the blade's travel decides which way the enemy reels (0 = straight away from the attacker, 1 = along the swing): a swing from the left throws it to the right.")]
    public float impactFromSwing = .65f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Impact")]
    [Range(0f, 30f), Tooltip("Degrees the enemy's upper body leans away with the blow, whatever part was struck (times Hit Impact Strength).")]
    public float impactBodySway = 9f;

    // Which way a hit throws its victim: away from the attacker, turned toward the blade's travel (Impact From Swing).
    public Vector3 ImpactDirection(DamageInfo info)
    {
        var away = Vector3.ProjectOnPlane(info.Direction, Vector3.up).normalized;
        var swing = Vector3.ProjectOnPlane(info.SwingDirection, Vector3.up).normalized;
        if (swing == Vector3.zero) return away;
        if (away == Vector3.zero) return swing;
        var d = Vector3.Slerp(away, swing, impactFromSwing);
        return d.sqrMagnitude > .0001f ? d.normalized : away;
    }

    // ── Hits: Hit flash ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit flash")]
    public bool hitFlashEnabled = true;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit flash")]
    [ColorUsage(false, true), Tooltip("Tint every character flashes when hit. Values above 1 glow.")]
    public Color hitFlashColor = new Color(2.4f, 1.9f, 1.45f);
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit flash")]
    [Tooltip("Seconds the flash tint stays on the character's renderers.")]
    public float hitFlashDuration = 0.1f;

    // ── Hits: Hit effects ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit effects")]
    [Tooltip("Pool of hit particle prefabs. One is chosen at random when the weapon uses default effects.")]
    public List<GameObject> hitParticlePrefabs = new();
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit effects")]
    [Tooltip("Hit audio used when the weapon uses default effects.")]
    public AudioData defaultHitAudio;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit effects")]
    [Tooltip("Swing audio used when the weapon uses default effects.")]
    public AudioData defaultSwingAudio;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Hit effects")]
    [Tooltip("Heavy swing audio used by weapons with default effects.")]
    public AudioData defaultHeavySwingAudio;

    // ── Hits: Blood on the blade ──
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Blood on the blade")]
    [Tooltip("The player's weapon gets spattered with what it cuts (BladeBlood on the table player), and the stains slowly dry and fade.")]
    public bool bladeBlood = true;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Blood on the blade")]
    [Min(1f), Tooltip("Seconds a stain on the blade takes to fade away.")]
    public float bladeBloodFadeSeconds = 45f;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Blood on the blade")]
    [Range(1, 40), Tooltip("Most stains on one weapon at a time.")]
    public int bladeBloodMax = 18;

    // ── Death: Death ──
    [TabGroup("Tabs", "Death"), FoldoutGroup("Tabs/Death/Death")]
    [Tooltip("Enemies collapse as physics ragdolls. Off plays the death animation instead.")]
    public bool ragdollDeath = true;
    [TabGroup("Tabs", "Death"), FoldoutGroup("Tabs/Death/Death")]
    [Tooltip("Speed (m/s) a body is thrown along the killing blow and the swing. Heavy hits and criticals x1.4.")]
    [Min(0)] public float ragdollImpulse = 5.5f;
    [TabGroup("Tabs", "Death"), FoldoutGroup("Tabs/Death/Death")]
    [Tooltip("Upward speed (m/s) of a thrown body.")]
    [Min(0)] public float ragdollUpwardImpulse = 3f;
    [TabGroup("Tabs", "Death"), FoldoutGroup("Tabs/Death/Death")]
    [Tooltip("Enemy loot stays on the corpse until the player searches it. Off scatters it on death.")]
    public bool lootableCorpses = true;
    [TabGroup("Tabs", "Death"), FoldoutGroup("Tabs/Death/Death")]
    [Tooltip("Seconds before a corpse is removed. 0 keeps corpses for the whole floor.")]
    [Min(0)] public float corpseLifetime = 0f;

    readonly RaycastHit[] obstructionHits = new RaycastHit[64];
    public bool HasMeleeLineOfSight(Character source,Character victim,Vector3 contact) =>
        HasMeleeLineOfSight(source,victim!=null ? victim.transform : null,contact);

    // victim is the root of whatever is being hit (a Character, or the Component behind a prop's IDamageable);
    // its own colliders never count as obstructions.
    public bool HasMeleeLineOfSight(Character source,Transform victim,Vector3 contact)
    {
        if(source==null)return true;
        // The player's eye, so a stab at something on the floor isn't blocked by the floor; others from the chest.
        Vector3 origin=source is Player p && p.CameraRig!=null && p.CameraRig.Camera!=null
            ? p.CameraRig.Camera.transform.position : source.transform.position+Vector3.up*.9f;
        Vector3 delta=contact-origin;
        if(delta.sqrMagnitude<.001f)return true;
        int count=Physics.RaycastNonAlloc(origin,delta.normalized,obstructionHits,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==obstructionHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var t=obstructionHits[i].transform;
            if(t.IsChildOf(source.transform) || (victim!=null && t.IsChildOf(victim)))continue;
            // Loose debris on the floor (splinters, gore, a knocked-off helmet) never stops a blow.
            // (anything with a rigidbody, settled or not, that isn't a character).
            if(obstructionHits[i].rigidbody!=null && obstructionHits[i].collider.GetComponentInParent<Character>()==null)continue;
            return false;
        }
        return true;
    }

    // How close an enemy notices the player without looking at them, and how far it can see,
    // as shares of its profile's radii. Sneaking (crouching) and the Stealth skill shrink both.
    public void StealthScales(Transform target, out float close, out float sight)
    {
        close = 1; sight = 1;
        if (target == null) return;
        if (target != stealthTarget) { stealthTarget = target; stealthPlayer = target.GetComponent<Player>(); }
        if (stealthPlayer == null) return;
        bool sneaking = stealthPlayer.Motor != null && stealthPlayer.Motor.IsCrouching;
        float skill = sneaking && stealthPlayer.TryGetComponent<AdventurerProgress>(out var progress) && progress.InRun ? progress.Bonus(AdventureSkill.Stealth) : 0;
        // Never quite unheard or unseen, however good the sneak.
        close = (sneaking ? sneakCloseScale : unseenCloseScale) * Mathf.Clamp(1 - skill, .25f, 1f);
        sight = sneaking ? sneakSightScale * Mathf.Clamp(1 - skill * .5f, .25f, 1f) : 1;
    }
    Transform stealthTarget;
    Player stealthPlayer;

    public void RollCriticalOrBackstab(ref DamageInfo info, Character target)
    {
        if (target == null || info.Amount <= 0f) return;
        var brain = target.GetComponent<EnemyBrain>();
        if (backstabsEnabled && brain != null)
        {
            bool behind = info.Direction.sqrMagnitude > .001f && Vector3.Dot(target.transform.forward, info.Direction) > backstabBehindDot;
            // A backstab is a blow from behind, whether the enemy knows the player is there or not, and never from the
            // front. A stage boss standing ready (Wary) can't be backstabbed.
            if (behind && !brain.Wary)
            {
                info.Backstab = true;
                info.Amount *= backstabMultiplier * (1f + GearStat(info.Source, StatType.BackstabDamage));
                if (info.Source != null && info.Source.TryGetComponent<AdventurerProgress>(out var progress) && progress.InRun)
                {
                    // Stealth's second bonus: backstabs hit harder every rank. Legendary Stealth doubles them.
                    info.Amount *= 1f + progress.Extra(AdventureSkill.Stealth);
                    if (progress.Rank(AdventureSkill.Stealth) >= 100) info.Amount *= 2;
                    // Ambush (Stealth 25): a backstab is a critical hit as well.
                    if (criticalHitsEnabled && progress.FirstMilestone(AdventureSkill.Stealth)) { info.Amount *= critMultiplier; info.Critical = true; }
                }
                return;
            }
        }
        if (criticalHitsEnabled && Random.value < Mathf.Min(critChanceCap, critChance + GearStat(info.Source, StatType.CriticalChance)))
        {
            info.Critical = true;
            info.Amount *= critMultiplier;
        }
    }

    // A gear stat of the attacker (critical chance, backstab damage...), 0 without one.
    static float GearStat(Character who, StatType stat) => who != null && who.Stats != null ? Mathf.Max(0f, who.Stats.GetFinal(stat)) : 0f;

    float lastGearProc = -10f;

    // After a player's melee hit lands: gear that casts on hit (FireCircleOnHit), at most once a second.
    public void GearOnHit(Character attacker, Character target)
    {
        if (attacker == null || target == null || fireCircleOnHit == null || Time.time < lastGearProc + 1f) return;
        float chance = GearStat(attacker, StatType.FireCircleOnHit);
        if (chance <= 0f || Random.value >= chance) return;
        lastGearProc = Time.time;
        GroundAreaSpell.Cast(attacker, fireCircleOnHit, fireCircleOnHit.power, GroundAreaSpell.GroundBelow(target.transform.position), null);
    }

    public void PresentImpact(Character victim, DamageInfo info)
    {
        var profile = info.Profile;
        bool defaults = profile == null || profile.useDefaultEffects;
        AudioData audio = info.Blocked ? blockAudio : victim is Player && playerHurtAudio != null ? playerHurtAudio :
            defaults ? defaultHitAudio : profile.hitAudio;
        var body = !info.Blocked && defaults && victim.data != null ? victim.data.audio : null;
        if (body != null && body.impact != null && body.impact.Length > 0) body.PlayAt(body.impact, info.HitPoint, body.impactVolume);
        else if(audio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(audio,info.HitPoint);
        var special = info.Blocked ? null : info.Backstab ? backstabAudio : info.Critical ? critAudio : null;
        if(special != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(special,info.HitPoint);
        GameObject prefab = info.Blocked ? blockParticlePrefab : defaults ? GetRandomHitParticle() : profile.hitParticle;
        if(prefab != null)
        {
            Vector3 direction = info.Direction.sqrMagnitude > .001f ? -info.Direction : Vector3.up;
            PoolManager.SpawnOrInstantiate(prefab,info.HitPoint,Quaternion.LookRotation(direction),impactParticleLifetime);
        }
        // The player's camera kicks when their swing lands (Strike feel).
        if(!(victim is Player) && !info.Blocked && info.Source is Player striker && striker.Combat != null)
            striker.Combat.Kick(info.Heavy ? hitKick * heavyStrikeScale : hitKick);
        // Combo hits bite: a short pause of their own, longer for the finisher.
        if(!(victim is Player) && !info.Blocked && info.Source is Player && comboAttack && comboHitStop)
            PauseOnHit(info.Heavy ? comboFinisherHitStop : comboHitStopDuration, info.Heavy ? heavyHitStopTimeScale : lightHitStopTimeScale, false, victim);
        else if(!(victim is Player) || hitStopOnPlayerHurt)
            RequestHitStop(info.Heavy, (profile != null ? profile.hitStopScale : 1f) * (info.Blocked ? blockHitStopScale
                : info.Backstab ? backstabHitStopScale : info.Critical ? critHitStopScale : 1f), victim);
    }

    public GameObject GetRandomHitParticle()
    {
        if (hitParticlePrefabs == null || hitParticlePrefabs.Count == 0) return null;
        return hitParticlePrefabs[Random.Range(0, hitParticlePrefabs.Count)];
    }

    public float ScaleKnockback(float baseForce) =>
        knockbackEnabled ? baseForce * knockbackForceMultiplier : 0f;

    // ── Hit stop ──────────────────────────────────────────────────────

    Coroutine hitStopRoutine;
    float hitStopEndsAt, hitStopScaleNow, shakeAmount;
    readonly List<(Transform model, Vector3 rest)> shaking = new();

    // Light and heavy attacks have their own length and depth; scale stretches the length (weapon
    // profile, crit, backstab, block). While a stop is active, a new request only extends or deepens
    // it — it never re-freezes or shortens it.
    public void RequestHitStop(bool heavy, float scale = 1f, Character victim = null)
    {
        if (!hitStopEnabled) return;
        PauseOnHit((heavy ? heavyHitStopDuration : lightHitStopDuration) * Mathf.Max(0f, scale), heavy ? heavyHitStopTimeScale : lightHitStopTimeScale, heavy, victim);
    }

    void PauseOnHit(float duration, float timeScale, bool heavy, Character victim)
    {
        if (duration <= 0f) return;

        float end = Time.unscaledTime + duration;
        AddShake(victim, heavy);
        if (hitStopRoutine != null)
        {
            hitStopEndsAt = Mathf.Max(hitStopEndsAt, end);
            hitStopScaleNow = Mathf.Min(hitStopScaleNow, timeScale);
            Time.timeScale = hitStopScaleNow;
            return;
        }
        hitStopEndsAt = end;
        hitStopScaleNow = timeScale;
        hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    // The pause menu owns Time.timeScale while it is open (it saves and restores the hit stop's value),
    // so the stop holds still and never writes the time scale until the game is unpaused.
    static bool Paused => GameManager.HasInstance && GameManager.Instance.State == GameState.Paused;

    IEnumerator HitStopRoutine()
    {
        while (true)
        {
            Time.timeScale = hitStopScaleNow;
            float start = Time.unscaledTime, length = Mathf.Max(.001f, hitStopEndsAt - start);
            while (Time.unscaledTime < hitStopEndsAt)
            {
                if (Paused) { hitStopEndsAt += Time.unscaledDeltaTime; start += Time.unscaledDeltaTime; yield return null; continue; }
                Shake(1f - (Time.unscaledTime - start) / length);
                yield return null;
            }
            EndShake();

            // Ease back to full speed; a new hit during the ease drops straight back into the stop.
            bool again = false;
            for (float t = 0f; t < hitStopRecovery; )
            {
                if (Paused) { yield return null; continue; }
                if (Time.unscaledTime < hitStopEndsAt) { again = true; break; }
                float k = t / hitStopRecovery;
                Time.timeScale = Mathf.Lerp(hitStopScaleNow, 1f, k * k);
                yield return null;
                if (!Paused) t += Time.unscaledDeltaTime;
            }
            if (!again) break;
        }
        // Restore to 1 explicitly: capturing the previous value would freeze the game
        // permanently if a request fired mid-stop and captured the slowed value.
        Time.timeScale = 1f;
        hitStopRoutine = null;
    }

    // The struck character's model (its "Model" child, never the root, so AI and physics are untouched)
    // jitters while the world is stopped.
    void AddShake(Character victim, bool heavy)
    {
        if (victim == null || victim is Player || hitStopShake <= 0f) return;
        var model = victim.transform.Find("Model");
        if (model == null) return;
        shakeAmount = Mathf.Max(shakeAmount, hitStopShake * (heavy ? 2f : 1f));
        foreach (var s in shaking) if (s.model == model) return;
        shaking.Add((model, model.localPosition));
    }

    void Shake(float strength)
    {
        int frame = Time.frameCount;
        foreach (var (model, rest) in shaking)
        {
            if (model == null || model.parent == null) continue;
            // Alternate sides every frame for a crisp shudder, with a little randomness, fading out.
            Vector3 dir = new Vector3(frame % 2 == 0 ? 1f : -1f, Random.Range(-.4f, .4f), Random.Range(-.4f, .4f));
            float local = shakeAmount * strength / Mathf.Max(.0001f, model.parent.lossyScale.x);
            model.localPosition = rest + model.parent.InverseTransformDirection(Camera.main != null
                ? Camera.main.transform.TransformDirection(dir) : dir) * local;
        }
    }

    void EndShake()
    {
        foreach (var (model, rest) in shaking) if (model != null) model.localPosition = rest;
        shaking.Clear();
        shakeAmount = 0f;
    }

    void OnDisable()
    {
        EndShake();
        if(hitStopRoutine != null) { StopCoroutine(hitStopRoutine); hitStopRoutine=null; Time.timeScale=1f; }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (hitStopRoutine != null) Time.timeScale = 1f;
    }
}
