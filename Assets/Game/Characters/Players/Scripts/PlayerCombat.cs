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

    public bool IsAttacking => AnyLayerAttacking();
    bool IsReleasing        => AnyLayerReleasing();
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
    float  blockLockUntil = -1f;
    float attackBufferedUntil=-1f;
    float pressedAt;
    bool charging;
    bool chargeAnnounced;
    float shudder;
    int swingIndex = 1;   // first swing of a sequence becomes 0
    float lastSwingAt = -10f;
    int chargeLayerIndex = -1;
    float guardPressedAt=-10;
    public bool HeavySwing { get; private set; }

    WeaponAnimationRelay relay;

    void Awake()
    {
        player = GetComponent<Player>();
        relay = GetComponentInChildren<WeaponAnimationRelay>(true);
        if (armsAnimator == null && relay != null) armsAnimator = relay.GetComponent<Animator>();
    }

    // Between the windup's AttackBegin event and the swing's PlaySwingAudio event only the real button
    // counts as held: the press that started the swing is consumed, and a tap that lands during the
    // windup or hold is queued for the next swing instead of stretching the current hold.
    bool windingUp;
    int queuedPresses;   // presses not yet turned into a windup, capped so spam can't bank swings
    void OnAttackStarted()
    {
        queuedPresses = Mathf.Max(0, queuedPresses - 1); windingUp = true;
        // Cleared here, not on press: a press during a heavy release must not turn its hit light.
        HeavySwing = false;
        // Alternate the light swing per actual swing; a pause restarts the sequence.
        swingIndex = Time.time - lastSwingAt <= swingSequenceReset ? (swingIndex + 1) % 2 : 0;
        lastSwingAt = Time.time;
        if (Character.HasParameter(armsAnimator, swingIndexParam, AnimatorControllerParameterType.Int)) armsAnimator.SetInteger(swingIndexParam, swingIndex);
    }
    void OnSwingStarted()  { windingUp = false; }

    void OnEnable()
    {
        if (relay != null) { relay.AttackStarted += OnAttackStarted; relay.SwingStarted += OnSwingStarted; }
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed += OnPrimaryPressed;
        if (player.Equipment != null) player.Equipment.Changed += OnEquipmentChanged;
        player.Damaged += OnDamaged;
        if(InputManager.HasInstance)
        {
            InputManager.Instance.PrimaryReleased += OnPrimaryReleased;
            InputManager.Instance.SecondaryPressed += OnGuardPressed;
        }
        OnEquipmentChanged();
    }

    void OnDisable()
    {
        if (relay != null) { relay.AttackStarted -= OnAttackStarted; relay.SwingStarted -= OnSwingStarted; }
        windingUp = false; queuedPresses = 0;
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed -= OnPrimaryPressed;
        if (player.Equipment != null) player.Equipment.Changed -= OnEquipmentChanged;
        player.Damaged -= OnDamaged;
        if(InputManager.HasInstance) InputManager.Instance.PrimaryReleased -= OnPrimaryReleased;
        if(InputManager.HasInstance) InputManager.Instance.SecondaryPressed -= OnGuardPressed;
        HeavySwing=false; charging=false; attackBufferedUntil=-1;
        CameraCharge = 0f;
        CameraZoomCharge = zoomVelocity = 0f;
        releasingHeavyZoom = false;
    }

    void OnPrimaryPressed()
    {
        if (!player.IsActive) return;
        if (InputManager.Instance.SecondaryHeld) return;
        pressedAt=Time.time; charging=true; chargeAnnounced=false;
        releasingHeavyZoom = false;
        queuedPresses = Mathf.Min(queuedPresses + 1, 2);
        attackBufferedUntil=Time.time+(CombatManager.HasInstance ? CombatManager.Instance.attackInputBuffer : .22f);
        float window = CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f;
        blockLockUntil = Time.time + window;
    }

    void OnGuardPressed() { if(player.IsActive && Time.time >= blockLockUntil) guardPressedAt=Time.time; }

    void OnPrimaryReleased()
    {
        if(!charging) return;
        charging=false;
        if(!player.IsActive || !WeaponHeld || !IsAttacking || !CombatManager.HasInstance) return;
        // The current swing already released; this press belongs to the next one.
        if(IsReleasing) return;
        var tuning=CombatManager.Instance;
        HeavySwing=Time.time-pressedAt >= tuning.heavyChargeTime &&
            (player.Stats == null || player.Stats.TryUseStamina(tuning.heavyStaminaCost, player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f));
        releasingHeavyZoom = HeavySwing;
        heavyZoomStateSeen = false;
        releaseZoom = CameraZoomCharge;
        zoomReleasedAt = Time.time;
    }

    void Update()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return;

        bool gameplay = !GameManager.HasInstance || GameManager.Instance.GameplayActive;
        bool primary   = gameplay && InputManager.HasInstance && InputManager.Instance.PrimaryHeld;
        bool secondary = gameplay && InputManager.HasInstance && InputManager.Instance.SecondaryHeld;
        bool blockLocked = Time.time < blockLockUntil;
        if(CombatManager.HasInstance)
        {
            var tuning=CombatManager.Instance;
            float attackSpeed=tuning.playerAttackSpeed*(player.Stats!=null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f);
            // Release pacing follows a curve over the swing so it whips through the strike and settles in the recovery.
            float phase = AttackPhase();
            float release = tuning.releaseSpeedCurve != null ? tuning.releaseSpeedCurve.Evaluate(phase) : 1f;
            float heavy   = tuning.heavyReleaseSpeedCurve != null ? tuning.heavyReleaseSpeedCurve.Evaluate(phase) : 1f;
            SetFloat(armsAnimator,windupSpeedParam,tuning.windupSpeed*attackSpeed);
            SetFloat(armsAnimator,releaseSpeedParam,tuning.releaseSpeed*attackSpeed*release);
            SetFloat(armsAnimator,heavyReleaseSpeedParam,tuning.heavyReleaseSpeed*attackSpeed*heavy);
            SetBool(armsAnimator,heavyStrikeParam,HeavySwing);
            UpdateCharge(tuning);
        }

        if (!IsAttacking) windingUp = false;
        // A queued press stays valid while a swing is in progress (it chains at the recovery's cancel point);
        // when idle it expires after the buffer window like any late press.
        if (!IsAttacking && Time.time >= attackBufferedUntil) queuedPresses = 0;
        if (!gameplay) queuedPresses = 0;
        bool buffered = gameplay && queuedPresses > 0 && !windingUp;
        SetBool(armsAnimator, attackHeldParam, (primary || buffered) && WeaponHeld && !secondary);
        if (secondary && !blockLocked && ShieldHeld && player.Stats != null &&
            player.Stats.HasStat(StatType.MaxStamina) && (player.Stats.IsExhausted || player.Stats.CurrentStamina <= 0))
            player.Stats.ReportInsufficientStamina();
        bool guarding = IsGuarding;
        if (guarding && player.Stats != null)
            guarding = player.Stats.DrainStamina(CombatManager.HasInstance ? CombatManager.Instance.shieldStaminaPerSecond : 2f,
                player.PlayerData != null ? player.PlayerData.staminaRegenDelay : .6f);
        SetBool(armsAnimator, blockHeldParam, guarding);

        if (player.Stats != null)
            SetFloat(armsAnimator, attackSpeedParam, player.Stats.GetMultiplier(StatType.AttackSpeed) * (CombatManager.HasInstance ? CombatManager.Instance.playerAttackSpeed : 1f));
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
    }

    void OnEquipmentChanged()
    {
        bool right = player.Equipment != null && player.Equipment.Has(EquipmentSlot.RightHand);
        bool left  = player.Equipment != null && player.Equipment.Has(EquipmentSlot.LeftHand);
        SetBool(armsAnimator, rightHandEquippedParam, right);
        SetBool(armsAnimator, leftHandEquippedParam,  left);
        if (!ShieldHeld) SetBool(armsAnimator, blockHeldParam, false);
    }

    void OnDamaged(DamageInfo info)
    {
        string trigger = info.Blocked ? blockHitTrigger : hurtTrigger;
        // The arms own attack timing and hitbox events. Keep windup, charge and
        // release playing through hits, including transitions into an attack.
        if (!IsAttacking) Trigger(armsAnimator, trigger);
        // Character already plays Hurt on its own animator; only add what it doesn't.
        if (info.Blocked || bodyAnimator != player.Animator) Trigger(bodyAnimator, trigger);
    }

    // ── Charge feel ───────────────────────────────────────────────────

    // Charge builds while the button stays held in an attack state, eases out otherwise. It drives the animator
    // param, the hold-loop speed and the additive tension layer's weight; a heavy impact adds a decaying shudder.
    void UpdateCharge(CombatManager tuning)
    {
        float target = charging && IsAttacking && WeaponHeld ? Mathf.Clamp01((Time.time - pressedAt) / Mathf.Max(0.01f, tuning.heavyChargeTime)) : 0f;
        Charge = Mathf.MoveTowards(Charge, target, chargeSmoothing * Time.deltaTime * (target > Charge ? 1f : 2.5f));
        float duration = Mathf.Max(.01f, tuning.heavyChargeTime);
        float delay = Mathf.Clamp(tuning.chargeCameraDelay, 0f, duration * .9f);
        float cameraTarget = charging && IsAttacking && WeaponHeld
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(delay, duration, Time.time - pressedAt))
            : 0f;
        CameraCharge = Mathf.MoveTowards(CameraCharge, cameraTarget,
            chargeSmoothing * Time.deltaTime * (cameraTarget > CameraCharge ? 1f : 2.5f));
        UpdateCameraZoom(tuning, cameraTarget);
        if (charging && !chargeAnnounced && target >= 1f) { chargeAnnounced = true; ChargeReady?.Invoke(); }
        shudder = Mathf.MoveTowards(shudder, 0f, 4f * Time.deltaTime);

        if (Character.HasParameter(armsAnimator, chargeParam, AnimatorControllerParameterType.Float)) armsAnimator.SetFloat(chargeParam, Charge);
        if (Character.HasParameter(armsAnimator, holdSpeedParam, AnimatorControllerParameterType.Float)) armsAnimator.SetFloat(holdSpeedParam, Mathf.Lerp(1f, tuning.holdSpeedAtFullCharge, Charge));
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
                if (HasTag(next, heavyReleaseTag)) state = next;
                else if (HasTag(state, heavyReleaseTag)) continue;
            }
            if (HasTag(state, heavyReleaseTag))
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
        if (!isActiveAndEnabled || !player.IsActive || !WeaponHeld || armsAnimator == null ||
            armsAnimator.runtimeAnimatorController == null) return Vector3.zero;
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
        bool heavy = HasTag(state, heavyReleaseTag);
        bool alternate = HasTag(state, alternateReleaseTag);
        if (!heavy && !alternate && !HasTag(state, releaseTag)) return Vector3.zero;
        var rotation = heavy ? tuning.heavySwingCameraRotation
            : alternate ? tuning.alternateSwingCameraRotation : tuning.lightSwingCameraRotation;
        var curve = heavy ? tuning.heavySwingCameraCurve : tuning.swingCameraCurve;
        return rotation * (curve != null ? curve.Evaluate(Mathf.Clamp01(state.normalizedTime)) : 0f);
    }

    // Normalized time of the attack state (windup, hold or release) currently playing, clamped to one pass.
    float AttackPhase()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return 0f;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            var info = armsAnimator.GetCurrentAnimatorStateInfo(layer);
            if (armsAnimator.IsInTransition(layer)) { var next = armsAnimator.GetNextAnimatorStateInfo(layer); if (IsAttackState(next)) return Mathf.Clamp01(next.normalizedTime); }
            if (IsAttackState(info)) return Mathf.Clamp01(info.normalizedTime);
        }
        return 0f;
    }

    // ── IBlocker ──────────────────────────────────────────────────────

    public bool TryBlock(ref DamageInfo info)
    {
        if(GameManager.HasInstance && !GameManager.Instance.GameplayActive)return false;
        if (!IsGuarding) return false;

        if (info.Direction.sqrMagnitude > 0.001f)
        {
            float threshold = CombatManager.HasInstance ? CombatManager.Instance.blockFrontalDot : -0.3f;
            Vector3 facing = player.Look != null ? player.Look.YawTransform.forward : transform.forward;
            if (Vector3.Dot(facing, info.Direction.normalized) >= threshold) return false;
        }

        float cost = CombatManager.HasInstance ? CombatManager.Instance.blockStaminaCost : 6f;
        if (player.Stats != null && !player.Stats.TryUseStamina(cost, player.PlayerData != null ? player.PlayerData.staminaRegenDelay : .6f))
        {
            player.Stats.ExhaustStamina(player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f);
            SetBool(armsAnimator, blockHeldParam, false);
            return false;
        }
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    bool IsReleaseState(AnimatorStateInfo state) =>
        HasTag(state, releaseTag) || HasTag(state, alternateReleaseTag) || HasTag(state, heavyReleaseTag);

    bool IsAttackState(AnimatorStateInfo state) => HasTag(state, attackTag) || IsReleaseState(state);

    // An empty tag would match every untagged state.
    static bool HasTag(AnimatorStateInfo state, string tag) => !string.IsNullOrEmpty(tag) && state.IsTag(tag);

    // True if the current or incoming state on any layer is an attack state.
    bool AnyLayerAttacking()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return false;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            if (IsAttackState(armsAnimator.GetCurrentAnimatorStateInfo(layer))) return true;
            if (armsAnimator.IsInTransition(layer) && IsAttackState(armsAnimator.GetNextAnimatorStateInfo(layer))) return true;
        }
        return false;
    }

    // A release is playing or starting, and the next windup has not begun blending in.
    bool AnyLayerReleasing()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return false;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            if (armsAnimator.IsInTransition(layer))
            {
                var next = armsAnimator.GetNextAnimatorStateInfo(layer);
                if (HasTag(next, attackTag)) continue;
                if (IsReleaseState(next)) return true;
            }
            if (IsReleaseState(armsAnimator.GetCurrentAnimatorStateInfo(layer))) return true;
        }
        return false;
    }

    static void SetFloat(Animator anim, string param, float value)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, param, AnimatorControllerParameterType.Float)) anim.SetFloat(param, value);
    }

    static void SetBool(Animator anim, string param, bool value)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, param, AnimatorControllerParameterType.Bool)) anim.SetBool(param, value);
    }

    static void Trigger(Animator anim, string param)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, param, AnimatorControllerParameterType.Trigger)) anim.SetTrigger(param);
    }
}
