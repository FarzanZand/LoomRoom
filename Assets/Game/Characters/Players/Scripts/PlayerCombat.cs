using UnityEngine;

// Table player only: attack and block. The arms animator owns the timing; this feeds it
// the held inputs and reads back state through state TAGS on any layer, never state
// names or layer indices. Windup and hold are tagged "Attack"; each release has its own
// tag, and all four count as attacking. Hitbox windows come from animation events
// through WeaponAnimationRelay.
public class PlayerCombat : MonoBehaviour, IBlocker
{
    [Header("Animators")]
    [Tooltip("First-person arms animator that plays attacks and blocks.")]
    [SerializeField] Animator armsAnimator;
    [Tooltip("Optional third-person body animator that mirrors hurt/block hits.")]
    [SerializeField] Animator bodyAnimator;

    [Header("State Tags")]
    [Tooltip("Windup and hold.")]
    [SerializeField] string attackTag             = "Attack";
    [SerializeField] string releaseTag            = "AttackRelease";
    [SerializeField] string alternateReleaseTag   = "AttackReleaseAlt";
    [SerializeField] string heavyReleaseTag       = "AttackHeavyRelease";

    [Header("Animator Parameters")]
    [SerializeField] string attackHeldParam        = "AttackHeld";
    [SerializeField] string blockHeldParam         = "BlockHeld";
    [SerializeField] string rightHandEquippedParam = "RightHandEquipped";
    [SerializeField] string leftHandEquippedParam  = "LeftHandEquipped";
    [SerializeField] string blockHitTrigger        = "BlockHit";
    [SerializeField] string blockStrainParam       = "BlockStrain";
    [SerializeField] string hurtTrigger            = "Hurt";
    [SerializeField] string windupSpeedParam       = "WindupSpeed";
    [SerializeField] string releaseSpeedParam      = "ReleaseSpeed";
    [SerializeField] string heavyReleaseSpeedParam = "HeavyReleaseSpeed";
    [SerializeField] string heavyStrikeParam       = "HeavyStrike";
    [SerializeField] string attackSpeedParam       = "AttackSpeed";
    [SerializeField] string simpleSwingsParam      = "SimpleSwings";
    [SerializeField] string swingSpeedParam        = "SwingSpeed";
    [SerializeField, Tooltip("Combo attack (CombatManager.swingSet Combo): the trigger that starts the next hit, and which hit (0, 1, 2).")]
    string comboTrigger = "ComboStrike", comboIndexParam = "ComboIndex", comboSpeedParam = "ComboSpeed";
    const int ComboHits = 3;

    [Header("Charge feel")]
    [Tooltip("0..1 charge toward a heavy strike, for the animator.")]
    [SerializeField] string chargeParam    = "Charge";
    [Tooltip("Speed multiplier on the hold loop; slows toward CombatManager.holdSpeedAtFullCharge as the charge builds.")]
    [SerializeField] string holdSpeedParam = "HoldSpeed";
    [Tooltip("Additive layer whose weight follows the charge (arm pulled back, trembling) and spikes on a heavy impact.")]
    [SerializeField] string chargeLayer    = "ChargeAdditive";
    [Tooltip("How fast the displayed charge follows the real one, per second.")]
    [SerializeField, Min(1f)] float chargeSmoothing = 10f;

    [Header("Light swings")]
    [Tooltip("Alternates between the light swing variants on each press.")]
    [SerializeField] string swingIndexParam = "SwingIndex";
    [Tooltip("Presses further apart than this restart the swing sequence.")]
    [SerializeField, Min(0f)] float swingSequenceReset = 1.2f;

    // Read many times a frame (input, charge, zoom, spells), so the layer scan runs once per frame.
    public bool IsAttacking
    {
        get
        {
            if (attackingFrame != Time.frameCount)
            {
                attackingFrame = Time.frameCount;
                attacking = AnimatorHelper.AnyLayerHasTag(armsAnimator, attackHash, releaseHash, alternateReleaseHash, heavyReleaseHash);
            }
            return attacking;
        }
    }
    bool IsReleasing => AnyLayerReleasing();
    // With CombatManager.attackFromBlock the shield is lowered for the whole swing and comes back up after it.
    static bool AttackFromBlock => CombatManager.HasInstance && CombatManager.Instance.attackFromBlock;
    static bool BlockCutsRecovery => CombatManager.HasInstance && CombatManager.Instance.blockCutsRecovery;
    static bool BlockCancelsWindup => CombatManager.HasInstance && CombatManager.Instance.blockCancelsWindup;

    // A swing has three parts: windup (until the blade swings), strike (until its hitbox closes) and recovery. The
    // shield is kept down through the windup and the strike; once the hitbox has closed, block raises it at once.
    // A block press during the windup cancels the swing (BlockCancelsWindup).
    Hitbox weaponHitbox;
    bool strikeOpened, strikeClosed;   // this swing's hitbox has opened / opened and closed again
    bool secondaryWasHeld;
    float guardRaisedAt = -10f;
    bool wasGuarding;
    bool InRecovery => strikeClosed;
    bool Committed => IsAttacking && !InRecovery && !cancelling;
    bool cancelling;   // a cancelled windup blending out: already harmless, the shield may rise
    [SerializeField] string attackCancelParam = "AttackCancel";

    void TrackStrike()
    {
        if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<Hitbox>(true);
        if (weaponHitbox == null) { strikeClosed = IsReleasing; return; }   // no hitbox: the release counts as recovery
        if (weaponHitbox.Active) strikeOpened = true;
        else if (strikeOpened) strikeClosed = true;
        // A release whose hitbox never opens (a miss in empty air still opens it; a cancelled clip doesn't) ends as recovery.
        if (!IsAttacking) { strikeOpened = false; strikeClosed = false; cancelling = false; }
    }

    // Block pressed while winding up: the swing is dropped before it can hit.
    void CancelWindup()
    {
        AnimatorHelper.SetTrigger(armsAnimator, attackCancelParam);
        AnimatorHelper.SetBool(armsAnimator, attackHeldParam, false);
        queuedPresses = 0; charging = false; HeavySwing = false; windingUp = false;
        attackBufferedUntil = -1f; blockLockUntil = -1f;
        heldThroughCancel = true;
        cancelling = true;
        cancelSetAt = Time.time;
    }
    float cancelSetAt = -10f;
    bool heldThroughCancel;   // the attack button still down from the cancelled swing: it starts nothing until pressed anew
    public bool IsGuarding => player != null && player.IsActive && ShieldHeld && (bash == null || !bash.Busy) &&
        !(BlockCutsRecovery ? Committed : AttackFromBlock && IsAttacking) &&
        (!CombatManager.HasInstance || Time.time - guardRaisedAt >= CombatManager.Instance.blockRaiseDelay) &&
        (!GameManager.HasInstance || GameManager.Instance.GameplayActive) &&
        InputManager.HasInstance && InputManager.Instance.SecondaryHeld && Time.time >= blockLockUntil &&
        (player.Stats == null || !player.Stats.HasStat(StatType.MaxStamina) || (!player.Stats.IsExhausted && player.Stats.CurrentStamina > 0));
    public bool WeaponHeld  => player.Equipment != null && player.Equipment.Get(EquipmentSlot.RightHand)?.itemType == ItemType.Weapon;
    public bool ShieldHeld  => player.Equipment != null && player.Equipment.Get(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;
    // Smoothed 0..1 progress toward a heavy strike while the attack button is held.
    public float Charge { get; private set; }
    // Delayed camera feedback, independent of animation charge and heavy-strike timing.
    public float CameraCharge { get; private set; }
    public float CameraZoomCharge { get; private set; }
    float zoomVelocity, releaseZoom, zoomReleasedAt;
    bool releasingHeavyZoom, heavyZoomStateSeen;
    // Fired once when a held attack reaches full charge.
    public event System.Action ChargeReady;

    Player player;
    PlayerSpellcasting spells;
    AdventurerProgress progress;
    ShieldBash bash;   // block held + attack, when it is ready (CombatManager > Shield bash)
    float blockLockUntil = -1f;
    float attackBufferedUntil = -1f;
    float pressedAt;
    bool charging;
    bool chargeAnnounced;
    float shudder;
    int swingIndex = 1;   // first swing of a sequence becomes 0
    float lastSwingAt = -10f;
    int chargeLayerIndex = -1;
    int attackHash, releaseHash, alternateReleaseHash, heavyReleaseHash;
    int attackingFrame = -1;
    bool attacking;
    public bool HeavySwing { get; private set; }

    float SwingInterval => !CombatManager.HasInstance ? 0f : CombatManager.Instance.swingInterval /
        Mathf.Max(.1f, player.Stats != null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f);

    // Stamina pause after blocking, guarding or a heavy swing: the player's own setting.
    float StaminaRegenDelay => player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f;

    WeaponAnimationRelay relay;

    void Awake()
    {
        player = GetComponent<Player>();
        spells = GetComponent<PlayerSpellcasting>();
        progress = GetComponent<AdventurerProgress>();
        bash = GetComponent<ShieldBash>();
        relay = GetComponentInChildren<WeaponAnimationRelay>(true);
        if (armsAnimator == null && relay != null) armsAnimator = relay.GetComponent<Animator>();
        attackHash           = AnimatorHelper.TagHash(attackTag);
        releaseHash          = AnimatorHelper.TagHash(releaseTag);
        alternateReleaseHash = AnimatorHelper.TagHash(alternateReleaseTag);
        heavyReleaseHash     = AnimatorHelper.TagHash(heavyReleaseTag);
    }

    // Between the windup's AttackBegin event and the swing's PlaySwingAudio event only the real button
    // counts as held: the press that started the swing is consumed, and a tap that lands during the
    // windup or hold is queued for the next swing instead of stretching the current hold.
    bool windingUp;
    int queuedPresses;   // presses not yet turned into a windup, capped so spam can't bank swings
    void OnAttackStarted()
    {
        if (AttackFromBlock && !BlockCutsRecovery) blockLockUntil = Time.time + (CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f);
        strikeOpened = strikeClosed = false;
        queuedPresses = Mathf.Max(0, queuedPresses - 1);
        windingUp = true;
        // Cleared here, not on press: a press during a heavy release must not turn its hit light.
        // The combo's last hit is a heavy one (CombatManager.comboFinisherHeavy).
        ComboFinisher = Combo && !comboHeavy && comboIndex == ComboHits - 1;
        HeavySwing = ComboFinisher && CombatManager.Instance.comboFinisherHeavy;
        // Alternate the light swing per actual swing; a pause restarts the sequence.
        swingIndex = NextSwingIndex;
        lastSwingAt = Time.time;
        AnimatorHelper.SetInteger(armsAnimator, swingIndexParam, swingIndex);
    }
    void OnSwingStarted() { windingUp = false; }

    // Strike feel (CombatManager > Player > Strike feel): the player steps into the blow and the camera kicks as the
    // hitbox opens; a landed hit kicks again (CombatManager.PresentImpact). Heavy swings do both harder.
    Vector3 kick;
    public Vector3 CameraKick => kick;
    public void Kick(Vector3 degrees) { if (degrees.sqrMagnitude > kick.sqrMagnitude) kick = degrees; }

    void OnStrike()
    {
        var t = CombatManager.HasInstance ? CombatManager.Instance : null;
        if (t == null || !player.IsActive) return;
        bool heavy = false, alternate = false;
        if (AnimatorHelper.Ready(armsAnimator))
            for (int layer = 0; layer < armsAnimator.layerCount; layer++)
            {
                var info = armsAnimator.IsInTransition(layer) ? armsAnimator.GetNextAnimatorStateInfo(layer) : armsAnimator.GetCurrentAnimatorStateInfo(layer);
                heavy |= AnimatorHelper.HasTag(info, heavyReleaseHash);
                alternate |= AnimatorHelper.HasTag(info, alternateReleaseHash);
            }
        float scale = Combo ? (HeavySwing ? t.comboFinisherStrikeScale : t.comboStrikeScale) : heavy || HeavySwing ? t.heavyStrikeScale : 1f;
        var k = t.strikeKick * scale;
        Kick(alternate ? new Vector3(k.x, -k.y, -k.z) : k);
        if (t.strikeLunge > 0f && player.Motor != null)
        {
            var yaw = player.Look != null ? player.Look.YawTransform : transform;
            player.Motor.Lunge(yaw.forward, t.strikeLunge * scale, t.strikeLungeTime);
        }
    }
    int NextSwingIndex => Time.time - lastSwingAt <= swingSequenceReset ? (swingIndex + 1) % 2 : 0;

    void OnEnable()
    {
        if (relay != null)
        {
            relay.AttackStarted += OnAttackStarted;
            relay.SwingStarted  += OnSwingStarted;
            relay.HitboxOpened  += OnStrike;
        }
        if (InputManager.HasInstance)
        {
            InputManager.Instance.PrimaryPressed  += OnPrimaryPressed;
            InputManager.Instance.PrimaryReleased += OnPrimaryReleased;
        }
        if (player.Equipment != null) player.Equipment.Changed += OnEquipmentChanged;
        player.Damaged += OnDamaged;
        OnEquipmentChanged();
    }

    void OnDisable()
    {
        if (relay != null)
        {
            relay.AttackStarted -= OnAttackStarted;
            relay.SwingStarted  -= OnSwingStarted;
            relay.HitboxOpened  -= OnStrike;
        }
        windingUp = false;
        queuedPresses = 0;
        if (InputManager.HasInstance)
        {
            InputManager.Instance.PrimaryPressed  -= OnPrimaryPressed;
            InputManager.Instance.PrimaryReleased -= OnPrimaryReleased;
        }
        if (player.Equipment != null) player.Equipment.Changed -= OnEquipmentChanged;
        player.Damaged -= OnDamaged;
        HeavySwing = false;
        charging = false;
        attackBufferedUntil = -1;
        CameraCharge = 0f;
        CameraZoomCharge = zoomVelocity = 0f;
        releasingHeavyZoom = false;
    }

    // ── Combo attack ──
    static bool Combo => CombatManager.HasInstance && CombatManager.Instance.comboAttack;
    int comboIndex = -1;
    // The hit in progress is the combo's last (its own damage multiplier, CombatManager.comboFinisherDamage).
    public bool ComboFinisher { get; private set; }
    // Combo sets: a press waits to see whether it is a tap (a combo hit, on release) or a hold (the simple charged
    // attack, from comboHoldForHeavy on: the windup, hold and heavy release of the simple swing set).
    bool comboPressPending, comboHeavy;
    // A charged attack let go: combo hits pressed meanwhile wait (queued) until its swing has played out.
    bool comboAfterHeavy;
    float comboPressAt;
    float comboStartedAt = -10f, comboQueuedUntil = -1f;

    // A press queues the next hit; it starts once the current hit's cooldown is over (UpdateCombo), and stays queued
    // until then plus the input buffer.
    void QueueCombo() => comboQueuedUntil = Mathf.Max(Time.time, ComboReadyAt) + (CombatManager.HasInstance ? CombatManager.Instance.attackInputBuffer : .22f);

    // When the next combo hit may start: Combo Hit Cooldown after a hit began, Combo Finisher Cooldown after the
    // finisher began (both in seconds, whatever the attack speed).
    float ComboReadyAt => comboStartedAt + (!CombatManager.HasInstance ? .4f
        : comboIndex == ComboHits - 1 ? CombatManager.Instance.comboFinisherCooldown : CombatManager.Instance.comboHitCooldown);

    void UpdateCombo(bool gameplay, bool primary)
    {
        if (!Combo) { comboPressPending = comboHeavy = false; return; }
        // Held past comboHoldForHeavy: the simple charged attack instead of a combo hit (the combo starts over after it).
        if (comboPressPending && primary && Time.time - comboPressAt >= CombatManager.Instance.comboHoldForHeavy)
        {
            comboPressPending = false; comboHeavy = true; comboQueuedUntil = -1f;
            comboIndex = -1; comboStartedAt = -10f;
        }
        if (comboHeavy && !primary && !IsAttacking && Time.time - comboPressAt > .3f) comboHeavy = false;
        if (comboAfterHeavy && !IsAttacking) comboAfterHeavy = false;
        if (comboHeavy || Time.time > comboQueuedUntil) return;
        if (comboAfterHeavy) { comboQueuedUntil = Mathf.Max(comboQueuedUntil, Time.time + .05f); return; }
        if (!gameplay || !WeaponHeld || (spells != null && spells.Busy) || (bash != null && bash.Busy) ||
            (player.Equipment != null && player.Equipment.IsUsingItem)) { comboQueuedUntil = -1f; return; }
        var tuning = CombatManager.Instance;
        float speed = Mathf.Max(.1f, tuning.comboSpeed * (player.Stats != null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f));
        AnimatorHelper.SetFloat(armsAnimator, comboSpeedParam, speed);
        AnimatorHelper.SetInteger(armsAnimator, "ComboSet", tuning.swingSet == CombatManager.SwingSet.Classic ? 1 : tuning.swingSet == CombatManager.SwingSet.Classic2 ? 2 : 0);
        if (Time.time < ComboReadyAt) return;
        comboIndex = Time.time - comboStartedAt > tuning.comboResetTime / speed ? 0 : (comboIndex + 1) % ComboHits;
        comboStartedAt = Time.time;
        comboQueuedUntil = -1f;
        AnimatorHelper.SetInteger(armsAnimator, comboIndexParam, comboIndex);
        AnimatorHelper.SetTrigger(armsAnimator, comboTrigger);
       
    }

    void OnPrimaryPressed()
    {
        if (!player.IsActive || (spells != null && spells.Busy)) return;
        if (bash != null && bash.Busy) return;
        // Eating or drinking (Equipment.IsUsingItem): no swing, no shield bash until it is done.
        if (player.Equipment != null && player.Equipment.IsUsingItem) return;
        // Block held: a ready shield bash takes the press; the button then starts nothing until pressed again.
        if (InputManager.Instance.SecondaryHeld && bash != null && !IsAttacking && bash.TryStart())
        {
            heldThroughCancel = true; queuedPresses = 0; charging = false; attackBufferedUntil = -1f;
            return;
        }
        if (InputManager.Instance.SecondaryHeld && !AttackFromBlock) return;
        heldThroughCancel = false;
        if (Combo) { comboPressPending = true; comboPressAt = Time.time; }
        pressedAt = Time.time;
        charging = true;
        chargeAnnounced = false;
        releasingHeavyZoom = false;
        queuedPresses = Mathf.Min(queuedPresses + 1, 2);
        // A press stays valid at least until the swing interval lets the next swing start, so a tap is never eaten.
        attackBufferedUntil = Mathf.Max(Time.time + (CombatManager.HasInstance ? CombatManager.Instance.attackInputBuffer : .22f), lastSwingAt + SwingInterval + .05f);
        // Without attack-from-block a press locks the shield out at once; with it, only a swing that starts does (OnAttackStarted).
        if (!AttackFromBlock && !BlockCutsRecovery) blockLockUntil = Time.time + (CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f);
    }

    void OnPrimaryReleased()
    {
        // A tap in a combo set: the next combo hit.
        if (Combo)
        if (Combo && comboPressPending) { comboPressPending = false; charging = false; QueueCombo(); return; }
        // The charged attack is let go: from here presses are combo hits again (they wait for its swing to end).
        if (Combo && comboHeavy) { comboHeavy = false; comboAfterHeavy = true; }
        if (!charging) return;
        charging = false;
        if (!player.IsActive || !WeaponHeld || !IsAttacking || !CombatManager.HasInstance) return;
        // The current swing already released; this press belongs to the next one.
        if (IsReleasing) return;
        var tuning = CombatManager.Instance;
        HeavySwing = Time.time - pressedAt >= tuning.heavyChargeTime &&
            (player.Stats == null || player.Stats.TryUseStamina(tuning.heavyStaminaCost, StaminaRegenDelay));
        releasingHeavyZoom = HeavySwing;
        heavyZoomStateSeen = false;
        releaseZoom = CameraZoomCharge;
        zoomReleasedAt = Time.time;
    }

    void Update()
    {
        if (kick.sqrMagnitude > 0f)
            kick = Vector3.Lerp(kick, Vector3.zero, 1f - Mathf.Exp(-(CombatManager.HasInstance ? CombatManager.Instance.kickRecovery : 14f) * Time.deltaTime));
        if (!AnimatorHelper.Ready(armsAnimator)) return;

        bool gameplay = !GameManager.HasInstance || GameManager.Instance.GameplayActive;
        bool primary   = gameplay && (spells == null || !spells.Busy) && (player.Equipment == null || !player.Equipment.IsUsingItem) && InputManager.HasInstance && InputManager.Instance.PrimaryHeld;
        bool secondary = gameplay && InputManager.HasInstance && InputManager.Instance.SecondaryHeld;
        bool blockLocked = Time.time < blockLockUntil;
        if (CombatManager.HasInstance)
        {
            var tuning = CombatManager.Instance;
            float attackSpeed = tuning.playerAttackSpeed * (player.Stats != null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f);
            // Release pacing follows a curve over the swing so it whips through the strike and settles in the recovery.
            float phase = AttackPhase();
            float release = tuning.releaseSpeedCurve != null ? tuning.releaseSpeedCurve.Evaluate(phase) : 1f;
            float heavy   = tuning.heavyReleaseSpeedCurve != null ? tuning.heavyReleaseSpeedCurve.Evaluate(phase) : 1f;
            AnimatorHelper.SetFloat(armsAnimator, windupSpeedParam, tuning.windupSpeed * attackSpeed);
            AnimatorHelper.SetFloat(armsAnimator, releaseSpeedParam, tuning.releaseSpeed * attackSpeed * release);
            AnimatorHelper.SetFloat(armsAnimator, heavyReleaseSpeedParam, tuning.heavyReleaseSpeed * attackSpeed * heavy);
            AnimatorHelper.SetBool(armsAnimator, heavyStrikeParam, HeavySwing);
            AnimatorHelper.SetBool(armsAnimator, simpleSwingsParam, tuning.simpleSwings);
            // The IK swing clips carry their own timing: only the attack speed stat scales them.
            AnimatorHelper.SetFloat(armsAnimator, swingSpeedParam, player.Stats != null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f);
            UpdateCharge(tuning);
        }

        TrackStrike();
        UpdateCombo(gameplay, primary);
        // An unconsumed cancel (the swing moved on that same frame) must not cancel the next windup.
        if (cancelSetAt > 0f && Time.time - cancelSetAt > .1f) { AnimatorHelper.ResetTrigger(armsAnimator, attackCancelParam); cancelSetAt = -10f; }
        // Block pressed (not held from before) while the swing is still winding up: cancel it.
        bool blockPressed = secondary && !secondaryWasHeld;
        secondaryWasHeld = secondary;
        if (blockPressed && BlockCancelsWindup && ShieldHeld && IsAttacking && !IsReleasing) CancelWindup();
        bool attackingNow = IsAttacking;
        if (!attackingNow) windingUp = false;
        // A queued press stays valid while a swing is in progress (it chains at the recovery's cancel point);
        // when idle it expires after the buffer window like any late press.
        if (!attackingNow && Time.time >= attackBufferedUntil) queuedPresses = 0;
        if (!gameplay) queuedPresses = 0;
        bool buffered = gameplay && (spells == null || !spells.Busy) && queuedPresses > 0 && !windingUp;
        // A new swing can't start sooner than swingInterval after the last one began.
        // Only the start is held back: a windup in progress keeps reading the button for the charge.
        bool startingSwing = !attackingNow || IsReleasing;
        bool intervalReady = !startingSwing || Time.time - lastSwingAt >= SwingInterval;
        // The animator picks the windup (forehand or backhand) from SwingIndex as the swing starts, so it
        // holds the next swing's index before then; AttackBegin confirms it.
        if (startingSwing && !windingUp) AnimatorHelper.SetInteger(armsAnimator, swingIndexParam, NextSwingIndex);
        if (!primary) heldThroughCancel = false;
        AnimatorHelper.SetBool(armsAnimator, attackHeldParam,
            (!Combo || comboHeavy) && !heldThroughCancel && (primary || (buffered && !Combo)) && WeaponHeld && (!secondary || AttackFromBlock) && intervalReady && (bash == null || !bash.Busy));
        if (secondary && !blockLocked && ShieldHeld && player.Stats != null &&
            player.Stats.HasStat(StatType.MaxStamina) && (player.Stats.IsExhausted || player.Stats.CurrentStamina <= 0))
            player.Stats.ReportInsufficientStamina();
        bool raising = secondary && ShieldHeld && !(BlockCutsRecovery ? Committed : AttackFromBlock && IsAttacking);
        if (raising && !wasGuarding) guardRaisedAt = Time.time;
        wasGuarding = raising;
        bool guarding = IsGuarding;
        if (guarding && player.Stats != null)
            guarding = player.Stats.DrainStamina(CombatManager.HasInstance ? CombatManager.Instance.shieldStaminaPerSecond : 2f, StaminaRegenDelay);
        AnimatorHelper.SetBool(armsAnimator, blockHeldParam, guarding);

        if (player.Stats != null)
            AnimatorHelper.SetFloat(armsAnimator, attackSpeedParam, player.Stats.GetMultiplier(StatType.AttackSpeed) * (CombatManager.HasInstance ? CombatManager.Instance.playerAttackSpeed : 1f));
    }

    public void PrepareEquippedPose()
    {
        OnEquipmentChanged();
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled) return;
        var culling = armsAnimator.cullingMode;
        armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        armsAnimator.Update(0f);
        armsAnimator.Update(1f); // Complete the initial equip pose while presentation is hidden.
        armsAnimator.cullingMode = culling;
        attackingFrame = -1;     // the pose changed mid-frame
    }

    void OnEquipmentChanged()
    {
        bool right = player.Equipment != null && player.Equipment.Has(EquipmentSlot.RightHand);
        bool left  = player.Equipment != null && player.Equipment.Has(EquipmentSlot.LeftHand);
        AnimatorHelper.SetBool(armsAnimator, rightHandEquippedParam, right);
        AnimatorHelper.SetBool(armsAnimator, leftHandEquippedParam,  left);
        if (!ShieldHeld) AnimatorHelper.SetBool(armsAnimator, blockHeldParam, false);
    }

    // The raised shield takes a hit it doesn't stop (magic goes through it, the Dungeon Master's beam leans on it): the
    // arms play the same jolt as a blocked blow.
    public void ShieldJolt()
    {
        if (!IsGuarding || IsAttacking) return;
        AnimatorHelper.SetTrigger(armsAnimator, blockHitTrigger);
    }

    // The raised shield held against something that keeps pushing (the Dungeon Master's beam): the arms shake in the
    // BlockStrain state until it is switched off.
    public Animator ArmsAnimator => armsAnimator;
    public void SetShieldStrain(bool on) => AnimatorHelper.SetBool(armsAnimator, blockStrainParam, on);

    // Shield up and facing where a hit came from (the same test TryBlock makes, without its stamina cost).
    bool ShieldFaces(DamageInfo info)
    {
        if (!IsGuarding) return false;
        if (info.Direction.sqrMagnitude <= 0.001f) return true;
        float threshold = CombatManager.HasInstance ? CombatManager.Instance.blockFrontalDot : -0.3f;
        Vector3 facing = player.Look != null ? player.Look.YawTransform.forward : transform.forward;
        return Vector3.Dot(facing, info.Direction.normalized) < threshold;
    }

    void OnDamaged(DamageInfo info)
    {
        // A spell that lands on the raised shield still hurts, but the shield shows the hit rather than the arms flinching.
        string trigger = info.Blocked || (info.Magic && ShieldFaces(info)) ? blockHitTrigger : hurtTrigger;
        // The arms own attack timing and hitbox events. Keep windup, charge and
        // release playing through hits, including transitions into an attack.
        if (!IsAttacking) AnimatorHelper.SetTrigger(armsAnimator, trigger);
        // Character already plays Hurt on its own animator; only add what it doesn't.
        // One body: the body is the arms' animator, which has had its say above.
        if (bodyAnimator != armsAnimator && (info.Blocked || bodyAnimator != player.Animator)) AnimatorHelper.SetTrigger(bodyAnimator, trigger);
    }

    // ── Charge feel ───────────────────────────────────────────────────

    // Charge builds while the button stays held in an attack state, eases out otherwise. It drives the animator
    // param, the hold-loop speed and the additive tension layer's weight; a heavy impact adds a decaying shudder.
    void UpdateCharge(CombatManager tuning)
    {
        bool building = charging && IsAttacking && WeaponHeld;
        float target = building ? Mathf.Clamp01((Time.time - pressedAt) / Mathf.Max(0.01f, tuning.heavyChargeTime)) : 0f;
        Charge = Mathf.MoveTowards(Charge, target, chargeSmoothing * Time.deltaTime * (target > Charge ? 1f : 2.5f));
        float duration = Mathf.Max(.01f, tuning.heavyChargeTime);
        float delay = Mathf.Clamp(tuning.chargeCameraDelay, 0f, duration * .9f);
        float cameraTarget = building
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(delay, duration, Time.time - pressedAt))
            : 0f;
        CameraCharge = Mathf.MoveTowards(CameraCharge, cameraTarget,
            chargeSmoothing * Time.deltaTime * (cameraTarget > CameraCharge ? 1f : 2.5f));
        UpdateCameraZoom(tuning, cameraTarget);
        if (charging && !chargeAnnounced && target >= 1f) { chargeAnnounced = true; ChargeReady?.Invoke(); }
        shudder = Mathf.MoveTowards(shudder, 0f, 4f * Time.deltaTime);

        AnimatorHelper.SetFloat(armsAnimator, chargeParam, Charge);
        AnimatorHelper.SetFloat(armsAnimator, holdSpeedParam, Mathf.Lerp(1f, tuning.holdSpeedAtFullCharge, Charge));
        if (chargeLayerIndex < 0 && !string.IsNullOrEmpty(chargeLayer)) chargeLayerIndex = armsAnimator.GetLayerIndex(chargeLayer);
        if (chargeLayerIndex >= 0) armsAnimator.SetLayerWeight(chargeLayerIndex, Mathf.Clamp01(Charge * tuning.chargeTension + shudder));
    }

    // Zoom follows the heavy release animation; charge noise can settle independently.
    void UpdateCameraZoom(CombatManager tuning, float chargeTarget)
    {
        bool gameplay = player.IsActive && WeaponHeld &&
            (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        if (!gameplay) releasingHeavyZoom = false;
        float target = gameplay ? chargeTarget : 0f;
        float smoothing = target > CameraZoomCharge ? .04f : tuning.chargeZoomReturnSmoothing;
        if (releasingHeavyZoom)
        {
            if (TryHeavyReleasePhase(out float phase))
            {
                heavyZoomStateSeen = true;
                float start = Mathf.Clamp(tuning.heavyZoomReturnStart, 0f, .95f);
                float end = Mathf.Clamp(tuning.heavyZoomReturnEnd, start + .01f, 1f);
                target = releaseZoom * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, phase)));
                smoothing = .04f;
            }
            else if (!heavyZoomStateSeen && IsAttacking && Time.time - zoomReleasedAt < .3f)
            {
                // Preserve the zoom while the animator enters the release clip.
                target = releaseZoom;
                smoothing = .04f;
            }
            else releasingHeavyZoom = false; // Interrupted, unequipped or finished: ease home.
        }
        CameraZoomCharge = Mathf.SmoothDamp(CameraZoomCharge, target, ref zoomVelocity,
            Mathf.Max(.01f, smoothing), Mathf.Infinity, Time.deltaTime);
    }

    bool TryHeavyReleasePhase(out float phase)
    {
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            var state = armsAnimator.GetCurrentAnimatorStateInfo(layer);
            if (armsAnimator.IsInTransition(layer))
            {
                var next = armsAnimator.GetNextAnimatorStateInfo(layer);
                if (AnimatorHelper.HasTag(next, heavyReleaseHash)) state = next;
                else if (AnimatorHelper.HasTag(state, heavyReleaseHash)) continue;
            }
            if (AnimatorHelper.HasTag(state, heavyReleaseHash))
            {
                phase = Mathf.Clamp01(state.normalizedTime);
                return true;
            }
        }
        phase = 0f;
        return false;
    }

    // Spike the tension layer, e.g. when a heavy hit lands; it decays on its own.
    public void Shudder(float amount) => shudder = Mathf.Max(shudder, Mathf.Clamp01(amount));

    // Follow the release clips, including blends to/from windup and idle.
    public Vector3 SwingCameraRotation(CombatManager tuning)
    {
        if (!isActiveAndEnabled || !player.IsActive || !WeaponHeld || !AnimatorHelper.Ready(armsAnimator)) return Vector3.zero;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            var current = CameraRotationForState(armsAnimator.GetCurrentAnimatorStateInfo(layer), tuning);
            if (armsAnimator.IsInTransition(layer))
                current = Vector3.Lerp(current,
                    CameraRotationForState(armsAnimator.GetNextAnimatorStateInfo(layer), tuning),
                    Mathf.Clamp01(armsAnimator.GetAnimatorTransitionInfo(layer).normalizedTime));
            if (current.sqrMagnitude > 0f) return current;
        }
        return Vector3.zero;
    }

    Vector3 CameraRotationForState(AnimatorStateInfo state, CombatManager tuning)
    {
        bool heavy = AnimatorHelper.HasTag(state, heavyReleaseHash);
        bool alternate = AnimatorHelper.HasTag(state, alternateReleaseHash);
        if (!heavy && !alternate && !AnimatorHelper.HasTag(state, releaseHash)) return Vector3.zero;
        var rotation = heavy ? tuning.heavySwingCameraRotation
            : alternate ? tuning.alternateSwingCameraRotation : tuning.lightSwingCameraRotation;
        var curve = heavy ? tuning.heavySwingCameraCurve : tuning.swingCameraCurve;
        if (!heavy && !tuning.simpleSwings)
        {
            // The IK forehand turns the view with the blade (right to left); the backhand mirrors it.
            rotation = tuning.ikSwingCameraRotation;
            if (alternate) rotation = new Vector3(rotation.x, -rotation.y, -rotation.z);
            curve = tuning.ikSwingCameraCurve;
        }
        return rotation * (curve != null ? curve.Evaluate(Mathf.Clamp01(state.normalizedTime)) : 0f);
    }

    // Normalized time of the attack state (windup, hold or release) currently playing, clamped to one pass.
    float AttackPhase()
    {
        if (!AnimatorHelper.Ready(armsAnimator)) return 0f;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            var info = armsAnimator.GetCurrentAnimatorStateInfo(layer);
            if (armsAnimator.IsInTransition(layer))
            {
                var next = armsAnimator.GetNextAnimatorStateInfo(layer);
                if (IsAttackState(next)) return Mathf.Clamp01(next.normalizedTime);
            }
            if (IsAttackState(info)) return Mathf.Clamp01(info.normalizedTime);
        }
        return 0f;
    }

    // ── IBlocker ──────────────────────────────────────────────────────

    public bool TryBlock(ref DamageInfo info)
    {
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return false;
        if (!IsGuarding) return false;

        if (info.Direction.sqrMagnitude > 0.001f)
        {
            float threshold = CombatManager.HasInstance ? CombatManager.Instance.blockFrontalDot : -0.3f;
            Vector3 facing = player.Look != null ? player.Look.YawTransform.forward : transform.forward;
            if (Vector3.Dot(facing, info.Direction.normalized) >= threshold) return false;
        }

        float cost = CombatManager.HasInstance ? CombatManager.Instance.blockStaminaCost : 6f;
        if (progress != null) cost *= progress.BlockCost;
        if (player.Stats != null && !player.Stats.TryUseStamina(cost, StaminaRegenDelay))
        {
            player.Stats.ExhaustStamina(StaminaRegenDelay);
            AnimatorHelper.SetBool(armsAnimator, blockHeldParam, false);
            return false;
        }
        LastBlockTime = Time.time;
        return true;
    }

    // When the last hit was blocked (Riposte, Swords 25).
    public float LastBlockTime { get; private set; } = -10f;

    // ── Helpers ───────────────────────────────────────────────────────

    bool IsReleaseState(AnimatorStateInfo state) =>
        AnimatorHelper.HasTag(state, releaseHash, alternateReleaseHash, heavyReleaseHash);

    bool IsAttackState(AnimatorStateInfo state) =>
        AnimatorHelper.HasTag(state, attackHash, releaseHash, alternateReleaseHash, heavyReleaseHash);

    // A release is playing or starting, and the next windup has not begun blending in.
    bool AnyLayerReleasing()
    {
        if (!AnimatorHelper.Ready(armsAnimator)) return false;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            if (armsAnimator.IsInTransition(layer))
            {
                var next = armsAnimator.GetNextAnimatorStateInfo(layer);
                if (AnimatorHelper.HasTag(next, attackHash)) continue;
                if (IsReleaseState(next)) return true;
            }
            if (IsReleaseState(armsAnimator.GetCurrentAnimatorStateInfo(layer))) return true;
        }
        return false;
    }
}
