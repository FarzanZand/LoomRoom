using System;
using UnityEngine;

public enum MoveState { Idle = 0, Walk = 1, Run = 2, CrouchWalk = 3, Airborne = 4 }

// CharacterController movement: walk/sprint/crouch, jump and gravity, external knockback.
// Speeds come from the PlayerData's movement settings multiplied by the MoveSpeed stat.
// Looking is PlayerLook's job.
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

    // ── State ─────────────────────────────────────────────────────────
    public bool  IsCrouching { get; private set; }
    // Held crouched as if the key were held (test drivers: CombatTestBot's crouch).
    public bool  ForceCrouch { get; set; }
    bool forcedCrouch;
    public bool  IsSprinting { get; private set; }
    public bool  IsMoving    { get; private set; }
    public bool  IsGrounded  => Controller != null && Controller.isGrounded;
    public float Speed       { get; private set; }
    // Scales every walking speed on top of the MoveSpeed stat (a co-op ghost moves faster, CoopGhost).
    public float SpeedScale  { get; set; } = 1f;

    // Enemies block like walls: walking toward one inside the personal space goes nowhere.
    void KeepPersonalSpace()
    {
        float space = CombatManager.HasInstance ? CombatManager.Instance.personalSpace : 1.1f;
        foreach (var enemy in EnemyBrain.Active)
        {
            if (enemy == null || enemy.State == EnemyState.Dead) continue;
            Vector3 to = enemy.transform.position - transform.position; to.y = 0f;
            float distance = to.magnitude;
            if (distance >= space || distance < .01f) continue;
            to /= distance;
            float into = movement.x * to.x + movement.z * to.z;
            if (into > 0f)
            {
                movement.x -= to.x * into;
                movement.z -= to.z * into;
            }
        }
    }

    // A dash (the shield bash) replaces walking while it is set: horizontal velocity from its owner, no personal space
    // (the dash stops at the enemy itself). Zero = walk as usual.
    public Vector3 DashVelocity { get; set; }
    Vector3 walkVelocity, lungeVelocity;
    float lungeUntil;
    // A short step along direction (the swing's lunge), added to walking.
    public void Lunge(Vector3 direction, float distance, float seconds)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f || distance <= 0f || seconds <= 0f) return;
        lungeVelocity = direction.normalized * (distance / seconds);
        lungeUntil = Time.time + seconds;
    }
    public bool Dashing => DashVelocity.sqrMagnitude > .01f;

    public float AirborneTime { get; private set; }
    public MoveState State   { get; private set; }
    public Vector3   Velocity => Controller != null ? Controller.velocity : Vector3.zero;
    public CharacterController Controller { get; private set; }
    PlayerData Movement => player != null && player.PlayerData != null ? player.PlayerData : FallbackData;

    public event Action        Jumped;
    public event Action<float> Landed;   // airborne seconds

    Player player;
    AdventurerProgress adventurer;   // Athletics lowers the sprint cost
    CapsuleCollider capsule;
    Vector3 movement;
    Vector3 knockbackVelocity;
    float   targetCharConHeight;
    bool    jumpQueued;
    bool    sprintToggleRequested;
    bool    crouchToggleRequested;
    bool    wasGrounded = true;
    bool    sprintApplied;   // this frame's speed is the sprint speed (intent, not measured velocity)
    PlayerData runtimeFallback;
    readonly Collider[] uncrouchOverlapResults = new Collider[8];

    PlayerData FallbackData
    {
        get
        {
            if (runtimeFallback == null) runtimeFallback = ScriptableObject.CreateInstance<PlayerData>();
            return runtimeFallback;
        }
    }

    void OnDestroy()
    {
        if (runtimeFallback != null) Destroy(runtimeFallback);
    }

    void Awake()
    {
        player     = GetComponent<Player>();
        adventurer = GetComponent<AdventurerProgress>();
        Controller = GetComponent<CharacterController>();
        capsule    = GetComponent<CapsuleCollider>();
        // This actor is moved only by CharacterController.Move. The legacy body exists
        // for trigger callbacks, never for dynamic collision response.
        var body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        if (capsule != null) capsule.isTrigger = true;
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
        DashVelocity = Vector3.zero;
    }

    void OnJump()          { if (IsControllable && (player == null || player.PlayerData == null || player.PlayerData.canJump)) jumpQueued = true; }
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
        bool canCrouch    = player == null || player.PlayerData == null || player.PlayerData.canCrouch;
        bool crouchHeld   = canCrouch && (input != null && input.CrouchHeld || ForceCrouch);
        if (!canCrouch) { crouchToggleRequested = false; if (IsCrouching && CanUncrouch()) IsCrouching = false; }
        bool sprintHeld   = input != null && input.SprintHeld;

        // CROUCH
        // A test driver holding crouch works whether the player's setting is hold or toggle.
        if (ForceCrouch != forcedCrouch) { forcedCrouch = ForceCrouch; if (ForceCrouch) IsCrouching = true; else if (CanUncrouch()) IsCrouching = false; }
        else if (holdToCrouch)
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
            IsSprinting = sprintHeld;
        }
        else
        {
            if (sprintToggleRequested)
            {
                bool wants = !IsSprinting;
                IsSprinting = wants;
            }
            if (autoUnSprint && !IsMoving) IsSprinting = false;
        }

        if (IsSprinting && IsCrouching)
        {
            // Entering crouch cancels an existing sprint. Only a fresh sprint press
            // may stand a toggled crouch back up; a held/toggled sprint is not new input.
            bool crouchLocked = (holdToCrouch && crouchHeld) || crouchToggleRequested || !sprintToggleRequested;
            if (!crouchLocked && CanUncrouch()) IsCrouching = false;
            else IsSprinting = false;
        }

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
            if (hit == null || hit.GetComponentInParent<Player>() == player) continue;
            return false;
        }
        return true;
    }

    // ── Movement ──────────────────────────────────────────────────────

    void ApplyMovement(Vector2 moveInput)
    {
        if (player != null && player.Ghost) { GhostMove(moveInput); return; }
        if (!Controller.enabled) Controller.enabled = true;
        var p = Movement;
        float speedMul = (player != null && player.Stats != null ? player.Stats.GetMultiplier(StatType.MoveSpeed) : 1f) * SpeedScale;

        Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        IsMoving = moveDir.sqrMagnitude > 0.0001f;

        bool leanBlocksSprint = InputManager.HasInstance &&
            (InputManager.Instance.LeanLeftHeld || InputManager.Instance.LeanRightHeld) &&
            (player == null || player.settings == null || player.settings.enableLean);

        // Athletics 25 lets a table player sprint who otherwise can't.
        sprintApplied = (p.canSprint || (adventurer != null && adventurer.SprintUnlocked)) && IsSprinting && moveDir.z > 0f && !leanBlocksSprint && !IsCrouching;

        Speed = p.walkSpeed;
        if (sprintApplied && player != null && player.Stats != null &&
            !player.Stats.DrainStamina(p.sprintStaminaPerSecond * (adventurer != null ? adventurer.SprintCost : 1), p.staminaRegenDelay))
        {
            sprintApplied = false;
            IsSprinting = false;
        }
        if (sprintApplied) Speed = p.sprintSpeed;
        if (IsCrouching)   Speed = p.crouchSpeed;
        Speed *= speedMul;

        Transform orientation = player != null && player.Look != null ? player.Look.YawTransform : transform;
        Vector3 forward = orientation.forward; forward.y = 0f; forward.Normalize();
        Vector3 right   = orientation.right;   right.y   = 0f; right.Normalize();

        float yVel = movement.y;
        // Backing away is slower (PlayerData.backwardSpeed); strafing keeps full speed.
        float back = moveDir.z < 0f ? Mathf.Clamp(p.backwardSpeed, .1f, 1f) : 1f;
        // Speeding up and slowing down take a moment (PlayerData.accelerationTime, stopTime), so stepping in and out
        // of reach has weight instead of snapping to full speed.
        Vector3 target = (forward * moveDir.z * back + right * moveDir.x) * Speed;
        float ramp = target.sqrMagnitude >= walkVelocity.sqrMagnitude ? p.accelerationTime : p.stopTime;
        walkVelocity = ramp > 0f ? Vector3.MoveTowards(walkVelocity, target, Mathf.Max(Speed, p.walkSpeed) / ramp * Time.deltaTime) : target;
        movement = walkVelocity;
        // A swing's lunge steps into the blow (PlayerCombat.OnStrike); personal space below still stops it at an enemy.
        if (Time.time < lungeUntil) movement += lungeVelocity;
        movement.y = yVel;
        if (Dashing) { movement.x = DashVelocity.x; movement.z = DashVelocity.z; }
        else KeepPersonalSpace();

        bool grounded = Controller.isGrounded;
        if (grounded) movement.y = -2f;

        if (jumpQueued && grounded)
        {
            movement.y = p.jumpForce;
            Jumped?.Invoke();
        }
        jumpQueued = false;

        movement.y += Physics.gravity.y * p.gravityMultiplier * Time.deltaTime;

        Vector3 totalVelocity = movement + knockbackVelocity;
        float decay = CombatManager.HasInstance ? CombatManager.Instance.playerKnockbackDecay : knockbackDecay;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, Mathf.Max(.1f, decay) * Time.deltaTime);

        Controller.Move(totalVelocity * Time.deltaTime);

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

    // Dev console ghost mode: fly along the view, through walls (the controller is off), with no gravity.
    const float GhostSpeed = 6f, GhostSprintSpeed = 14f;

    void GhostMove(Vector2 moveInput)
    {
        if (Controller.enabled) Controller.enabled = false;
        var view = player.Look != null ? player.Look.PitchTransform : transform;
        var dir = view.forward * moveInput.y + view.right * moveInput.x;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        IsMoving = dir.sqrMagnitude > .0001f;
        Speed = IsSprinting ? GhostSprintSpeed : GhostSpeed;
        transform.position += dir * Speed * Time.deltaTime;
        movement = Vector3.zero;
        knockbackVelocity = Vector3.zero;
        jumpQueued = false;
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

    // From intent (input and the sprint actually applied), so knockback or a fast MoveSpeed bonus never reads as running.
    void UpdateState()
    {
        if (!Controller.isGrounded && AirborneTime > 0.15f) { State = MoveState.Airborne; return; }
        if (!IsMoving)               { State = MoveState.Idle; return; }
        if (IsCrouching)             { State = MoveState.CrouchWalk; return; }
        State = sprintApplied ? MoveState.Run : MoveState.Walk;
    }

    // ── IKnockbackReceiver ────────────────────────────────────────────

    public void ApplyKnockback(Vector3 direction, float force)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0f) return;
        if (float.IsNaN(force) || float.IsInfinity(force)) return;
        float limit = CombatManager.HasInstance ? CombatManager.Instance.playerKnockbackSpeedLimit : 2.5f;
        knockbackVelocity = direction.normalized * Mathf.Min(force, Mathf.Max(0, limit));
    }
}
