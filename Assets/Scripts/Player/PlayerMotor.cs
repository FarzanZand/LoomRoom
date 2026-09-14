using System;
using UnityEngine;

public enum MoveState { Idle = 0, Walk = 1, Run = 2, CrouchWalk = 3, Airborne = 4 }

// CharacterController movement: walk/sprint/crouch, jump and gravity, slope modules,
// stamina drain and external knockback. Speeds come from the CharacterData's
// MovementProfile multiplied by the MoveSpeed stat. Looking is PlayerLook's job.
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(0)]
public class PlayerMotor : MonoBehaviour, IKnockbackReceiver
{
    [Header("Crouching - character controller")]
    [Tooltip("CharacterController height while standing.")]
    [SerializeField] float normalCharConHeight = 1.8f;
    [Tooltip("CharacterController height while crouching.")]
    [SerializeField] float crouchCharConHeight = 1.2f;
    [Tooltip("Smooth transition speed between standing and crouching collider heights.")]
    [SerializeField] float crouchingCharConSmoothTime = 2.75f;
    [Tooltip("Layers that block standing up when crouched.")]
    [SerializeField] LayerMask uncrouchBlockLayers = ~0;

    [Header("Knockback")]
    [Tooltip("How fast received knockback velocity decays (units per second).")]
    [SerializeField] float knockbackDecay = 12f;

    [Header("Optional Modules")]
    [Tooltip("Corrects movement direction on slopes.")]
    [SerializeField] SlopeHandler slopeHandler;
    [Tooltip("Forces downhill sliding on slopes above its configured angle.")]
    [SerializeField] SteepSlopeSlideModule steepSlopeSlideModule;

    [Header("Fallback (used only when CharacterData has no MovementProfile)")]
    [SerializeField] MovementProfile fallbackProfile;

    // ── State ─────────────────────────────────────────────────────────
    public bool  IsCrouching { get; private set; }
    public bool  IsSprinting { get; private set; }
    public bool  IsMoving    { get; private set; }
    public bool  InWater     { get; private set; }
    public bool  IsGrounded  => Controller != null && Controller.isGrounded;
    public float Speed       { get; private set; }
    public float AirborneTime { get; private set; }
    public MoveState State   { get; private set; }
    public Vector3   Velocity => Controller != null ? Controller.velocity : Vector3.zero;
    public CharacterController Controller { get; private set; }
    public MovementProfile Profile =>
        player != null && player.data != null && player.data.movement != null ? player.data.movement : FallbackProfile;

    public event Action        Jumped;
    public event Action<float> Landed;   // airborne seconds

    Player player;
    CapsuleCollider capsule;
    Vector3 movement;
    Vector3 knockbackVelocity;
    float   targetCharConHeight;
    bool    jumpQueued;
    bool    sprintToggleRequested;
    bool    crouchToggleRequested;
    bool    wasGrounded = true;
    MovementProfile runtimeFallback;
    readonly Collider[] uncrouchOverlapResults = new Collider[8];

    MovementProfile FallbackProfile
    {
        get
        {
            if (fallbackProfile != null) return fallbackProfile;
            if (runtimeFallback == null) runtimeFallback = ScriptableObject.CreateInstance<MovementProfile>();
            return runtimeFallback;
        }
    }

    void Awake()
    {
        player     = GetComponent<Player>();
        Controller = GetComponent<CharacterController>();
        capsule    = GetComponent<CapsuleCollider>();
        // This actor is moved only by CharacterController.Move. The legacy body exists
        // for trigger callbacks, never for dynamic collision response.
        var body = GetComponent<Rigidbody>();
        if(body != null) { body.isKinematic=true; body.useGravity=false; }
        if(capsule != null) capsule.isTrigger=true;
        if (!slopeHandler)          slopeHandler          = GetComponent<SlopeHandler>();
        if (!steepSlopeSlideModule) steepSlopeSlideModule = GetComponent<SteepSlopeSlideModule>();
        targetCharConHeight = normalCharConHeight;
    }

    void OnEnable()
    {
        if (!InputManager.HasInstance) return;
        var input = InputManager.Instance;
        input.JumpPressed   += OnJump;
        input.SprintPressed += OnSprintPressed;
        input.CrouchPressed += OnCrouchPressed;
    }

    void OnDisable()
    {
        if (!InputManager.HasInstance) return;
        var input = InputManager.Instance;
        input.JumpPressed   -= OnJump;
        input.SprintPressed -= OnSprintPressed;
        input.CrouchPressed -= OnCrouchPressed;
        knockbackVelocity = Vector3.zero;
    }

    void OnJump()          { if (IsControllable) jumpQueued = true; }
    void OnSprintPressed() { if (IsControllable) sprintToggleRequested = true; }
    void OnCrouchPressed() { if (IsControllable) crouchToggleRequested = true; }

    bool IsControllable => player == null || player.IsActive;

    void Update()
    {
        var input    = InputManager.HasInstance ? InputManager.Instance : null;
        var settings = player != null ? player.settings : null;
        Vector2 move = input != null ? input.Move : Vector2.zero;

        HandleMovementStates(input, settings);
        ApplyMovement(move);
        UpdateCrouchCollider();
        UpdateState();

        sprintToggleRequested = false;
        crouchToggleRequested = false;
    }

    // ── States ────────────────────────────────────────────────────────

    void HandleMovementStates(InputManager input, PlayerSettings settings)
    {
        bool holdToCrouch = settings == null || settings.holdToCrouch;
        bool holdToSprint = settings == null || settings.holdToSprint;
        bool autoUnSprint = settings == null || settings.autoUnSprint;
        bool crouchHeld   = input != null && input.CrouchHeld;
        bool sprintHeld   = input != null && input.SprintHeld;
        bool canSprint    = CanSprintFromStamina();

        // CROUCH
        if (holdToCrouch)
        {
            if (crouchHeld) IsCrouching = true;
            else if (IsCrouching && CanUncrouch()) IsCrouching = false;
        }
        else if (crouchToggleRequested)
        {
            if (IsCrouching && CanUncrouch()) IsCrouching = false;
            else IsCrouching = true;
        }

        // SPRINT
        if (holdToSprint)
        {
            IsSprinting = sprintHeld && canSprint;
        }
        else
        {
            if (sprintToggleRequested)
            {
                bool wants = !IsSprinting;
                IsSprinting = wants && canSprint;
            }
            if (autoUnSprint && !IsMoving) IsSprinting = false;
        }

        if (IsSprinting && IsCrouching)
        {
            bool crouchLocked = holdToCrouch && crouchHeld;
            if (!crouchLocked && CanUncrouch()) IsCrouching = false;
            else IsSprinting = false;
        }

        if (!canSprint) IsSprinting = false;
    }

    bool CanSprintFromStamina()
    {
        var p = Profile;
        if (p.sprintStaminaPerSecond <= 0f || player == null || player.Stats == null) return true;
        return player.Stats.CanSprint(p.sprintRecoveryFraction);
    }

    bool CanUncrouch()
    {
        float radius = Controller.radius;
        Vector3 center = transform.position + Vector3.up * (normalCharConHeight * 0.5f);
        Vector3 bottom = center + Vector3.down * (normalCharConHeight * 0.5f - radius);
        Vector3 top    = center + Vector3.up   * (normalCharConHeight * 0.5f - radius);

        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, uncrouchOverlapResults,
                                               uncrouchBlockLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = uncrouchOverlapResults[i];
            if (hit == null || hit.transform.root == transform.root) continue;
            return false;
        }
        return true;
    }

    // ── Movement ──────────────────────────────────────────────────────

    void ApplyMovement(Vector2 moveInput)
    {
        var p = Profile;
        float speedMul = player != null && player.Stats != null ? player.Stats.GetMultiplier(StatType.MoveSpeed) : 1f;

        Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        IsMoving = moveDir.sqrMagnitude > 0.0001f;

        bool leanBlocksSprint = InputManager.HasInstance &&
            (InputManager.Instance.LeanLeftHeld || InputManager.Instance.LeanRightHeld) &&
            (player == null || player.settings == null || player.settings.enableLean);

        bool sprintApplied = IsSprinting && moveDir.z > 0f && !leanBlocksSprint && !IsCrouching;

        Speed = p.walkSpeed;
        if (sprintApplied) Speed = p.sprintSpeed;
        if (IsCrouching)   Speed = p.crouchSpeed;
        Speed *= speedMul;

        Transform orientation = player != null && player.Look != null ? player.Look.YawTransform : transform;
        Vector3 forward = orientation.forward; forward.y = 0f; forward.Normalize();
        Vector3 right   = orientation.right;   right.y   = 0f; right.Normalize();

        float yVel = movement.y;
        movement = (forward * moveDir.z + right * moveDir.x) * Speed;
        movement.y = yVel;

        bool grounded = Controller.isGrounded;
        if (grounded) movement.y = -2f;

        if (jumpQueued && grounded)
        {
            if (player == null || player.Stats == null || player.Stats.TryUseStamina(p.jumpStaminaCost, p.staminaRegenDelay))
            {
                movement.y = p.jumpForce;
                Jumped?.Invoke();
            }
        }
        jumpQueued = false;

        movement.y += Physics.gravity.y * p.gravityMultiplier * Time.deltaTime;

        if (slopeHandler && grounded && slopeHandler.OnSlope(Controller, out RaycastHit hit))
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(new Vector3(movement.x, 0f, movement.z), hit.normal);
            movement.x = horizontal.x;
            movement.z = horizontal.z;
        }

        bool sliding = false;
        if (steepSlopeSlideModule)
        {
            sliding = steepSlopeSlideModule.TryApplySteepSlopeSlide(Controller, ref movement);
            if (sliding) Speed = (IsCrouching ? p.crouchSpeed : p.walkSpeed) * speedMul;
        }

        Vector3 totalVelocity = movement + knockbackVelocity;
        float decay=CombatManager.HasInstance ? CombatManager.Instance.playerKnockbackDecay : knockbackDecay;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, Mathf.Max(.1f,decay) * Time.deltaTime);

        Controller.Move(totalVelocity * Time.deltaTime);

        if (sprintApplied && !sliding && p.sprintStaminaPerSecond > 0f && player != null && player.Stats != null)
            player.Stats.DrainStamina(p.sprintStaminaPerSecond, p.staminaRegenDelay);

        // Airborne / landing bookkeeping
        grounded = Controller.isGrounded;
        if (!grounded) AirborneTime += Time.deltaTime;
        else if (!wasGrounded)
        {
            Landed?.Invoke(AirborneTime);
            AirborneTime = 0f;
        }
        else AirborneTime = 0f;
        wasGrounded = grounded;
    }

    void UpdateCrouchCollider()
    {
        float desired = IsCrouching ? crouchCharConHeight : normalCharConHeight;
        float t = 1f - Mathf.Exp(-crouchingCharConSmoothTime * Time.deltaTime);
        targetCharConHeight = Mathf.Lerp(targetCharConHeight, desired, t);

        Controller.height = targetCharConHeight;
        Controller.center = new Vector3(0f, targetCharConHeight * 0.5f, 0f);
        if (capsule != null)
        {
            capsule.height = targetCharConHeight;
            capsule.center = Controller.center;
        }
    }

    void UpdateState()
    {
        var p = Profile;
        if (!Controller.isGrounded && AirborneTime > 0.15f) { State = MoveState.Airborne; return; }
        if (!IsMoving)               { State = MoveState.Idle; return; }
        if (IsCrouching)             { State = MoveState.CrouchWalk; return; }
        float horizontal = new Vector3(Velocity.x, 0f, Velocity.z).magnitude;
        State = horizontal > p.walkSpeed * 1.15f ? MoveState.Run : MoveState.Walk;
    }

    public float NormalHeight => normalCharConHeight;
    public void SetInWater(bool value) => InWater = value;

    // ── IKnockbackReceiver ────────────────────────────────────────────

    public void ApplyKnockback(Vector3 direction, float force)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0f) return;
        if(float.IsNaN(force) || float.IsInfinity(force))return;
        float limit=CombatManager.HasInstance ? CombatManager.Instance.playerKnockbackSpeedLimit : 2.5f;
        knockbackVelocity = direction.normalized * Mathf.Min(force,Mathf.Max(0,limit));
    }
}
