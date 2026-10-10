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
    [SerializeField] string hurtTrigger            = "Hurt";
    [SerializeField] string windupSpeedParam       = "WindupSpeed";
    [SerializeField] string releaseSpeedParam      = "ReleaseSpeed";
    [SerializeField] string heavyReleaseSpeedParam = "HeavyReleaseSpeed";
    [SerializeField] string heavyStrikeParam       = "HeavyStrike";
    [SerializeField] string attackSpeedParam       = "AttackSpeed";

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
    public bool IsGuarding => player != null && player.IsActive && ShieldHeld &&
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

    // Stamina pause after blocking, guarding or a heavy swing: the player's own setting.
    float StaminaRegenDelay => player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f;

    WeaponAnimationRelay relay;

    void Awake()
    {
        player = GetComponent<Player>();
        spells = GetComponent<PlayerSpellcasting>();
        progress = GetComponent<AdventurerProgress>();
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
        queuedPresses = Mathf.Max(0, queuedPresses - 1);
        windingUp = true;
        // Cleared here, not on press: a press during a heavy release must not turn its hit light.
        HeavySwing = false;
        // Alternate the light swing per actual swing; a pause restarts the sequence.
        swingIndex = Time.time - lastSwingAt <= swingSequenceReset ? (swingIndex + 1) % 2 : 0;
        lastSwingAt = Time.time;
        AnimatorHelper.SetInteger(armsAnimator, swingIndexParam, swingIndex);
    }
    void OnSwingStarted() { windingUp = false; }

    void OnEnable()
    {
        if (relay != null)
        {
            relay.AttackStarted += OnAttackStarted;
            relay.SwingStarted  += OnSwingStarted;
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

    void OnPrimaryPressed()
    {
        if (!player.IsActive || (spells != null && spells.Busy)) return;
        if (InputManager.Instance.SecondaryHeld) return;
        pressedAt = Time.time;
        charging = true;
        chargeAnnounced = false;
        releasingHeavyZoom = false;
        queuedPresses = Mathf.Min(queuedPresses + 1, 2);
        attackBufferedUntil = Time.time + (CombatManager.HasInstance ? CombatManager.Instance.attackInputBuffer : .22f);
        float window = CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f;
        blockLockUntil = Time.time + window;
    }

    void OnPrimaryReleased()
    {
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
        if (!AnimatorHelper.Ready(armsAnimator)) return;

        bool gameplay = !GameManager.HasInstance || GameManager.Instance.GameplayActive;
        bool primary   = gameplay && (spells == null || !spells.Busy) && InputManager.HasInstance && InputManager.Instance.PrimaryHeld;
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
            UpdateCharge(tuning);
        }

        bool attackingNow = IsAttacking;
        if (!attackingNow) windingUp = false;
        // A queued press stays valid while a swing is in progress (it chains at the recovery's cancel point);
        // when idle it expires after the buffer window like any late press.
        if (!attackingNow && Time.time >= attackBufferedUntil) queuedPresses = 0;
        if (!gameplay) queuedPresses = 0;
        bool buffered = gameplay && (spells == null || !spells.Busy) && queuedPresses > 0 && !windingUp;
        AnimatorHelper.SetBool(armsAnimator, attackHeldParam, (primary || buffered) && WeaponHeld && !secondary);
        if (secondary && !blockLocked && ShieldHeld && player.Stats != null &&
            player.Stats.HasStat(StatType.MaxStamina) && (player.Stats.IsExhausted || player.Stats.CurrentStamina <= 0))
            player.Stats.ReportInsufficientStamina();
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

    void OnDamaged(DamageInfo info)
    {
        string trigger = info.Blocked ? blockHitTrigger : hurtTrigger;
        // The arms own attack timing and hitbox events. Keep windup, charge and
        // release playing through hits, including transitions into an attack.
        if (!IsAttacking) AnimatorHelper.SetTrigger(armsAnimator, trigger);
        // Character already plays Hurt on its own animator; only add what it doesn't.
        if (info.Blocked || bodyAnimator != player.Animator) AnimatorHelper.SetTrigger(bodyAnimator, trigger);
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
        return true;
    }

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
