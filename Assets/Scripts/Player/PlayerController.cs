namespace MFPC
{
    using Unity.Cinemachine;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.InputSystem;

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        #region Variables
        [Tooltip("Necessery scriptable component used for controller settings, like mouse sensitivity, movement options.")]
        [SerializeField] GameData gameData;
        [Tooltip("Player data asset driving stats, faction, and base movement speeds.")]
        [SerializeField] PlayerData playerData;
        [Tooltip("Lock the cursor at start frame.")]
        [SerializeField] bool lockCursorOnStart = true;

        StatsComponent stats;
        PlayerFX       playerFX;

        // Inputs variables
        protected PlayerInputActions inputActions;
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }

        // cached input state (set by Input Actions)
        bool sprintPressed;
        bool sprintDown;   // one–frame press (for toggle)
        bool crouchPressed;
        bool crouchDown;   // one–frame press (for toggle)
        float moveForward; // moveInput.y

        private bool usingGamepadLook;
        private bool jumpPressed;
        private bool leanLeftPressed;
        private bool leanRightPressed;


        [Header("Player Movement")]

        [Tooltip("Movement speed while walking (meters per second).")]
        public float walkSpeed = 3f;
        [Tooltip("Movement speed while sprinting forward (meters per second).")]
        public float sprintSpeed = 6f;
        [Tooltip("Movement speed while crouching (meters per second).")]
        public float crouchSpeed = 1.75f;
        [Tooltip("Upward force applied when jumping.")]
        [SerializeField] private float jumpForce = 8f;
        [Tooltip("Multiplier applied to gravity while airborne. Higher values make falling faster.")]
        [SerializeField] private float gravityMultiplier = 2.5f;
        [Tooltip("Camera shake impulse played when landing.")]
        [SerializeField] private CinemachineImpulseSource landShakingImpulseSource;
        [Tooltip("Camera bump impulse played when landing.")]
        [SerializeField] private CinemachineImpulseSource landBumpingImpulseSource;
        [Tooltip("Camera shake impulse played when jumping.")]
        [SerializeField] private CinemachineImpulseSource jumpShakingImpulseSource;

        // variables
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsMoving { get; private set; }
        private Vector3 moveDir;
        private Vector3 movement;
        public float Speed { get; private set; }
        public CharacterController Controller { get; private set; }
        private CapsuleCollider playerCollider;
        private readonly Collider[] uncrouchOverlapResults = new Collider[8];


        [Header("Mouse look & lean")]

        [Tooltip("Root transform used as the camera pivot.")]
        [SerializeField] private Transform viewPoint;
        [Tooltip("Vertical rotation pivot (pitch).")]
        [SerializeField] private Transform verticalNeck;
        [Tooltip("Horizontal rotation and lean pivot (yaw & roll).")]
        [SerializeField] private Transform lateralTorso;
        [Tooltip("Lean smoothing speed while actively leaning.")]
        [SerializeField] private float lateralSmoothTime = 5f;
        [Tooltip("Lean return speed when releasing lean input.")]
        [SerializeField] private float lateralReturnTime = 7f;
        [Tooltip("Maximum lean angle in degrees.")]
        [SerializeField] private float leanAmount = 30f;

        // variables
        private float currentLeanAngle;
        private Transform cameraTransform;


        [Header("Mouse input smoothing")]

        [Tooltip("Higher values result in smoother but less responsive mouse movement: recommended 18-25")]
        [SerializeField] private float mouseSmoothing = 20f;

        private Vector2 smoothedLook;


        [Header("Crouching - camera")]

        [Tooltip("Camera height while standing.")]
        [SerializeField] private float normalCameraHeight = 1f;
        [Tooltip("Camera height while crouching.")]
        [SerializeField] private float crouchCameraHeight = 0.1f;
        [Tooltip("Smooth transition speed between standing and crouching camera heights.")]
        [SerializeField] private float crouchingCameraSmoothTime = 5f;

        // variables
        private Vector3 targetCameraHeight = new Vector3(0, 1, 0);


        [Header("Crouching - character controller")]

        [Tooltip("CharacterController height while standing.")]
        [SerializeField] private float normalCharConHeight = 1.8f;
        [Tooltip("CharacterController height while crouching.")]
        [SerializeField] private float crouchCharConHeight = 1.2f;
        [Tooltip("Smooth transition speed between standing and crouching collider heights.")]
        [SerializeField] private float crouchingCharConSmoothTime = 2.75f;

        // variables
        private float targetCharConHeight = 1.8f;


        [Header("Auto Uncrouch")]

        [Tooltip("Layers that block standing up when crouched.")]
        [SerializeField] private LayerMask uncrouchBlockLayers = ~0; // default: everything


        [Header("Footstep Sounds")]

        [Tooltip("Transform used as the origin point for footstep sounds.")]
        [SerializeField] private Transform feetTransform;
        [Tooltip("Transform used as the origin point for footstep sounds.")]
        [SerializeField] private float footstepVolume = 0.5f;
#pragma warning disable CS0414
        [SerializeField] private float crouchFootstepVolume = 0.25f;
#pragma warning restore CS0414
        [Tooltip("Sounds played when the jump starts.")]
        [SerializeField] private AudioClip[] jumpStartSounds;
        [Tooltip("Minimum airborne time required to trigger landing effects (seconds).")]
        [SerializeField] private float jumpingTimeThreshold = 0.3f;
        [Tooltip("Time between footsteps while walking (seconds).")]
        [SerializeField] private float walkInterval = 0.6f;
        [Tooltip("Time between footsteps while sprinting (seconds).")]
        [SerializeField] private float sprintInterval = 0.4f;
        [Tooltip("Time between footsteps while crouching (seconds).")]
        [SerializeField] private float crouchInterval = 0.7f;
        [Tooltip("Minimum velocity required to play footstep sounds.")]
        [SerializeField] private float velocityThreshold = 0.1f;
        [Tooltip("Volume multiplier applied to footsteps while crouching.")]
        [SerializeField] private float crouchVolumeReduction = 0.5f;
        [Tooltip("Footstep interval multiplier while swimming.")]
        [SerializeField] private float swimmingIntervalFactor = 2f;
        [Tooltip("Audio source pool used to play sound effects.")]
        [SerializeField] private AudioSourcePool audioPool;


        [Header("Layers sounds")]

        [Tooltip("Surface sound presets matched against terrain layers and object surfaces.")]
        [SerializeField] SurfaceSFX[] effects;
        [Tooltip("Fallback surface sound preset used when no specific surface is detected.")]
        [SerializeField] SurfaceSFX genericEffect;
        [Tooltip("Surface sound preset used when the player is in water.")]
        [SerializeField] SurfaceSFX waterEffect;

        // variables
        public bool InWater { get; private set; }
        AudioClip[] sfxClips;
        private TerrainChecker checker;
        private float nextStepTime;
        private int footstepIndex;
        private int jumpStartIndex;
        private int jumpLandIndex;
        public float AirborneTime { get; private set; }


        [Header("Camera Rotation")]
        [Tooltip("Minimum vertical look angle in degrees.")]
        [SerializeField] float minClamp = -70f;
        [Tooltip("Maximum vertical look angle in degrees.")]
        [SerializeField] float maxClamp = 70f;

        // variables
        private Vector2 mouseInput;
        private float verticalRotStore;


        [Header("Jump impulse settings")]

        [Tooltip("Maximum airborne time considered when calculating landing impulse strength (seconds).")]
        [SerializeField] float jumpMaxThresholdTime = 1f;
        [Tooltip("Overall intensity multiplier for jump and landing camera impulses.")]
        [SerializeField] float jumpImpulseIntensity = 1f;
        [Tooltip("Minimum impulse strength applied on landing, even for short jumps.")]
        [SerializeField] float minImpulse = 0.3f;


        [Header("Jump event hook")]

        [Tooltip("Invoked when player actively jumps.")]
        public UnityEvent OnJumpStart;





        [Header("Optional Modules")]

        [Tooltip("Optional Slope handler: it corrects movement direction on slopes")]
        [SerializeField] SlopeHandler slopeHandler;
        [Tooltip("Optional stamina module. If assigned, sprint drains stamina and stops when exhausted.")]
        [SerializeField] StaminaModule staminaModule;
        [Tooltip("Optional steep slope slide module. If assigned, slopes above the configured angle force downhill sliding.")]
        [SerializeField] SteepSlopeSlideModule steepSlopeSlideModule;
        #endregion

        // Extension Hooks
        public Vector3 Velocity => Controller ? Controller.velocity : Vector3.zero;
        public bool IsGrounded => Controller && Controller.isGrounded;
        public float VerticalVelocity => Controller ? Controller.velocity.y : 0f;
        public float CurrentLeanAngle => currentLeanAngle;
        public bool IsLeftLeaning => leanLeftPressed;
        public bool IsRightLeaning => leanRightPressed;
        public Animator bodyAnimator;
        public Animator armsAnimator;
        public bool IsAiming { get; protected set; }

        Vector3 knockbackVelocity;
        [SerializeField] float knockbackDecay = 12f;

        protected bool wasGrounded;
        private bool primaryActionHeld;
        private bool secondaryActionHeld;
        private float blockLockUntil = -1f;
        public bool RightHandEquipped { get; private set; }
        public bool LeftHandEquipped { get; private set; }

        // Validation safety
        void OnValidate()
        {
            walkSpeed = Mathf.Max(0.1f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
        }

        protected virtual void Awake()
        {
            Controller = GetComponent<CharacterController>();
            playerCollider = GetComponent<CapsuleCollider>();
            checker = GetComponent<TerrainChecker>();
            stats    = GetComponentInParent<StatsComponent>();
            playerFX = GetComponentInParent<PlayerFX>();

            if (!staminaModule)
                staminaModule = GetComponent<StaminaModule>();
            if (!steepSlopeSlideModule)
                steepSlopeSlideModule = GetComponent<SteepSlopeSlideModule>();

            if (playerData != null)
            {
                stats?.ApplyProfile(playerData.stats);
                stats?.SetFaction(playerData.faction);

                walkSpeed        = playerData.walkSpeed;
                sprintSpeed      = playerData.sprintSpeed;
                crouchSpeed      = playerData.crouchSpeed;
                jumpForce        = playerData.jumpForce;
                gravityMultiplier = playerData.gravityMultiplier;
            }

            inputActions = new PlayerInputActions();
            if (ItemHolder.Instance != null)
                ItemHolder.Instance.OnHeldItemChanged += OnHeldItemChanged;
        }

        #region Input Hook
        protected virtual void OnEnable()
        {
            if (stats != null) stats.OnDamageTaken += OnDamageTaken;

            inputActions.Enable();

            inputActions.Player.Move.performed += ctx => MoveInput = ctx.ReadValue<Vector2>();
            inputActions.Player.Move.canceled += _ => MoveInput = Vector2.zero;

            inputActions.Player.Look.performed += ctx =>
            {
                LookInput = ctx.ReadValue<Vector2>();
                usingGamepadLook = ctx.control.device is Gamepad;
            };
            inputActions.Player.Look.canceled += _ => LookInput = Vector2.zero;

            inputActions.Player.Jump.performed += _ => jumpPressed = true;
            inputActions.Player.Sprint.performed += _ =>
            {
                sprintPressed = true;
                sprintDown = true; // this frame only
            };

            inputActions.Player.Sprint.canceled += _ =>
            {
                sprintPressed = false;
            };

            inputActions.Player.Crouch.performed += _ =>
            {
                crouchPressed = true;
                crouchDown = true; // this frame only
            };

            inputActions.Player.Crouch.canceled += _ =>
            {
                crouchPressed = false;
            };

            inputActions.Player.LeanLeft.performed += _ => leanLeftPressed = true;
            inputActions.Player.LeanLeft.canceled += _ => leanLeftPressed = false;

            inputActions.Player.LeanRight.performed += _ => leanRightPressed = true;
            inputActions.Player.LeanRight.canceled += _ => leanRightPressed = false;

            inputActions.Player.PrimaryAction.performed += _ =>
            {
                primaryActionHeld = true;
                if (!secondaryActionHeld)
                    blockLockUntil = Time.time + (CombatManager.Instance != null ? CombatManager.Instance.blockCancelWindow : 0.5f);
            };
            inputActions.Player.PrimaryAction.canceled += _ => primaryActionHeld = false;
            inputActions.Player.SecondaryAction.performed += _ => secondaryActionHeld = true;
            inputActions.Player.SecondaryAction.canceled  += _ => secondaryActionHeld = false;
        }

        public void SetInputEnabled(bool enabled)
        {
            if (enabled)
                inputActions.Enable();
            else
            {
                inputActions.Disable();
                MoveInput = Vector2.zero;
                LookInput = Vector2.zero;
                sprintPressed = false;
                crouchPressed = false;
                jumpPressed = false;
                leanLeftPressed = false;
                leanRightPressed = false;
                primaryActionHeld = false;
                secondaryActionHeld = false;
            }
        }

        protected virtual void OnDisable()
        {
            if (stats != null) stats.OnDamageTaken -= OnDamageTaken;

            inputActions.Disable();
            if (ItemHolder.Instance != null)
                ItemHolder.Instance.OnHeldItemChanged -= OnHeldItemChanged;
        }

        void OnDamageTaken(float amount, Vector3 knockbackDir)
        {
            if (playerFX == null) playerFX = GetComponentInParent<PlayerFX>();

            if (IsBlocking())
            {
                bool frontal = true;
                if (knockbackDir.sqrMagnitude > 0.001f)
                {
                    Vector3 facing = lateralTorso != null ? lateralTorso.forward : transform.forward;
                    frontal = Vector3.Dot(facing, knockbackDir.normalized) < -0.3f;
                }

                if (frontal)
                {
                    TryTriggerPlayerAnim(bodyAnimator, "BlockHit");
                    TryTriggerPlayerAnim(armsAnimator, "BlockHit");
                    if (knockbackDir != Vector3.zero)
                        knockbackVelocity = knockbackDir.normalized * (playerFX?.ReceivedKnockbackForce ?? 3f);
                    return;
                }
            }

            TryTriggerPlayerAnim(bodyAnimator, "Hurt");
            TryTriggerPlayerAnim(armsAnimator, "Hurt");
            playerFX?.NotifyHurtReceived(amount, knockbackDir);
            if (knockbackDir != Vector3.zero)
                knockbackVelocity = knockbackDir.normalized * (playerFX?.ReceivedKnockbackForce ?? 3f);
        }

        void TryTriggerPlayerAnim(Animator anim, string triggerName)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;
            foreach (var p in anim.parameters)
                if (p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger)
                { anim.SetTrigger(triggerName); return; }
        }
        #endregion

        void Start()
        {
            if (lockCursorOnStart)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            cameraTransform = Camera.main.transform;
            targetCharConHeight = normalCharConHeight;
            targetCameraHeight = new Vector3(0, normalCameraHeight, 0);
            InWater = false;
        }

        void Update()
        {
            moveForward = MoveInput.y;

            HandleMovementStates();
            PlayerMovement();
            CameraLeanAndRotation();
            Crouching();
            AimCheck();
            VirtualCameras();
            Footsteps();

            sprintDown = false;
            crouchDown = false;

            UpdateAnimator();
        }

        protected virtual void UpdateAnimator()
        {
            bool grounded = Controller.isGrounded;
            float horizontalSpeed = new Vector3(Controller.velocity.x, 0f, Controller.velocity.z).magnitude;
            bool jump     = !grounded && wasGrounded && Controller.velocity.y > 0f;
            bool freeFall = !grounded && Controller.velocity.y < -1f;

            ApplyAnimatorParams(bodyAnimator, horizontalSpeed, grounded, jump, freeFall);
            ApplyAnimatorParams(armsAnimator, horizontalSpeed, grounded, jump, freeFall);

            if (armsAnimator != null)
            {
                bool menuOpen       = MenuManager.Instance != null && MenuManager.Instance.AnyMenuOpen;
                bool shieldEquipped = ItemHolder.Instance != null &&
                                      ItemHolder.Instance.GetHeldItem(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;
                bool weaponHeld = ItemHolder.Instance != null &&
                                  ItemHolder.Instance.GetHeldItem(EquipmentSlot.RightHand)?.itemType == ItemType.Weapon;

                bool blockLocked = Time.time < blockLockUntil;
                armsAnimator.SetBool("AttackHeld", primaryActionHeld && weaponHeld && !secondaryActionHeld && !menuOpen);
                armsAnimator.SetBool("BlockHeld",  secondaryActionHeld && !blockLocked && !menuOpen && shieldEquipped);
            }

            wasGrounded = grounded;
        }

        private void OnHeldItemChanged()
        {
            if (armsAnimator == null) return;
            bool shieldEquipped = ItemHolder.Instance != null &&
                                  ItemHolder.Instance.GetHeldItem(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;
            if (!shieldEquipped)
                armsAnimator.SetBool("BlockHeld", false);
        }

        public void SetRightHandEquipped(bool equipped)
        {
            RightHandEquipped = equipped;
            if (armsAnimator != null)
                armsAnimator.SetBool("RightHandEquipped", equipped);
        }

        public void SetLeftHandEquipped(bool equipped)
        {
            LeftHandEquipped = equipped;
            if (armsAnimator != null)
                armsAnimator.SetBool("LeftHandEquipped", equipped);
        }

        protected bool IsAttacking()
        {
            if (armsAnimator == null) return false;
            var cur  = armsAnimator.GetCurrentAnimatorStateInfo(1);
            var next = armsAnimator.GetNextAnimatorStateInfo(1);
            return cur.IsName("Attack_Windup")  || cur.IsName("Attack_Hold")  || cur.IsName("Attack_Release")
                || next.IsName("Attack_Windup") || next.IsName("Attack_Hold") || next.IsName("Attack_Release");
        }

        protected bool IsBlocking()
        {
            if (armsAnimator == null) return false;
            var cur  = armsAnimator.GetCurrentAnimatorStateInfo(2);
            var next = armsAnimator.GetNextAnimatorStateInfo(2);
            return cur.IsName("Block") || cur.IsName("BlockIdle") || cur.IsName("BlockHit")
                || next.IsName("Block") || next.IsName("BlockIdle") || next.IsName("BlockHit");
        }

        protected void ApplyAnimatorParams(Animator anim, float speed, bool grounded, bool jump, bool freeFall)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;

            anim.SetFloat("Speed",       speed,              0.1f, Time.deltaTime);
            anim.SetFloat("MotionSpeed", MoveInput.magnitude, 0.1f, Time.deltaTime);
            anim.SetBool("Grounded",  grounded);
            anim.SetBool("FreeFall",  freeFall);

            if (jump)           anim.SetBool("Jump", true);
            else if (grounded)  anim.SetBool("Jump", false);
        }

        protected virtual void AimCheck()
        {
            IsAiming = false;
        }

        protected virtual void VirtualCameras() { }

        #region Camera and Movement
        void HandleMovementStates()
        {
            bool sprintHeld = sprintPressed;
            bool crouchInputLocksCrouch = gameData.holdToCrouch && crouchPressed;
            bool canSprintFromStamina = CanSprintFromStamina();

            // CROUCH
            if (gameData.holdToCrouch)
            {
                if (crouchPressed)
                {
                    IsCrouching = true;
                }
                else if (IsCrouching && CanUncrouch())
                {
                    IsCrouching = false;
                }
            }
            else if (crouchDown)
            {
                if (IsCrouching && CanUncrouch())
                    IsCrouching = false;
                else
                    IsCrouching = true;
            }

            // SPRINT
            if (gameData.holdToSprint)
            {
                IsSprinting = sprintHeld && canSprintFromStamina;
                if (IsSprinting && IsCrouching)
                {
                    if (!crouchInputLocksCrouch && CanUncrouch())
                    {
                        IsCrouching = false;
                    }
                    else
                    {
                        IsSprinting = false;
                    }
                }
            }
            else
            {
                if (sprintDown)
                {
                    bool wantsToSprint = !IsSprinting;
                    if (wantsToSprint && !canSprintFromStamina)
                    {
                        IsSprinting = false;
                    }
                    else if (wantsToSprint && IsCrouching)
                    {
                        if (!crouchInputLocksCrouch && CanUncrouch())
                        {
                            IsCrouching = false;
                            IsSprinting = true;
                        }
                        else
                        {
                            IsSprinting = false;
                        }
                    }
                    else
                    {
                        IsSprinting = wantsToSprint;
                    }
                }

                if (gameData.autoUnSprint && IsMoving == false)
                {
                    IsSprinting = false;
                }
            }

            if (!canSprintFromStamina)
            {
                IsSprinting = false;
            }

            if (IsCrouching)
            {
                IsSprinting = false;
            }
        }

        bool CanSprintFromStamina()
        {
            return staminaModule == null || staminaModule.AllowsSprint;
        }

        bool TryConsumeJumpStamina()
        {
            return staminaModule == null || staminaModule.TryConsumeJump();
        }

        void UpdateStaminaModule(bool sprintSpeedApplied, bool isAirborne)
        {
            if (staminaModule)
            {
                staminaModule.UpdateStamina(sprintSpeedApplied, isAirborne);
            }
        }

        bool CanUncrouch()
        {
            float radius = Controller.radius;
            float standHeight = normalCharConHeight;

            Vector3 standCenterWorld =
                transform.position + Vector3.up * (standHeight * 0.5f);

            Vector3 bottom = standCenterWorld + Vector3.down * (standHeight * 0.5f - radius);
            Vector3 top = standCenterWorld + Vector3.up * (standHeight * 0.5f - radius);

            int overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                uncrouchOverlapResults,
                uncrouchBlockLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < overlapCount; i++)
            {
                Collider hit = uncrouchOverlapResults[i];
                if (hit == null || hit.transform.root == transform.root)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        void PlayerMovement()
        {
            bool leanEnabled = gameData == null || gameData.enableLean;
            moveDir = new Vector3(MoveInput.x, 0f, MoveInput.y).normalized;
            if (moveDir.magnitude > 0.01f)
            {
                IsMoving = true;
            }
            else
            {
                IsMoving = false;
            }

            Speed = walkSpeed;

            bool canApplySprint =
                IsSprinting &&
                CanSprintFromStamina() &&
                moveDir.z > 0 &&
                !(leanEnabled && leanLeftPressed) &&
                !(leanEnabled && leanRightPressed);

            if (canApplySprint)
            {
                Speed = sprintSpeed;
            }

            if (IsCrouching)
            {
                Speed = crouchSpeed;
            }

            float yVel = movement.y;

            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            // -------- Horizontal movement --------
            movement = (forward * moveDir.z + right * moveDir.x).normalized * Speed;
            movement.y = yVel;

            // -------- Ground adhesion --------
            if (Controller.isGrounded)
            {
                movement.y = -2f;
            }

            // -------- Jump --------
            if (jumpPressed && Controller.isGrounded)
            {
                jumpPressed = false;
                if (TryConsumeJumpStamina())
                {
                    movement.y = jumpForce;
                    PlayJumpSound(); // sound design
                    OnJumpStart?.Invoke();
                    jumpShakingImpulseSource.GenerateImpulse();
                }
            }

            // -------- Gravity --------
            movement.y += Physics.gravity.y * gravityMultiplier * Time.deltaTime;

            // -------- Slope Correction --------
            if(slopeHandler)
            {
                Vector3 horizontal = new Vector3(movement.x, 0f, movement.z);

                if (Controller.isGrounded && slopeHandler.OnSlope(Controller, out RaycastHit hit))
                {
                    horizontal = Vector3.ProjectOnPlane(horizontal, hit.normal);
                }

                movement.x = horizontal.x;
                movement.z = horizontal.z;

            }

            bool slidingOnSteepSlope = false;
            if (steepSlopeSlideModule)
            {
                slidingOnSteepSlope = steepSlopeSlideModule.TryApplySteepSlopeSlide(Controller, ref movement);
                if (slidingOnSteepSlope)
                {
                    Speed = IsCrouching ? crouchSpeed : walkSpeed;
                }
            }

            movement += knockbackVelocity;
            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDecay * Time.deltaTime);

            Controller.Move(movement * Time.deltaTime);
            UpdateStaminaModule(canApplySprint && !slidingOnSteepSlope, !Controller.isGrounded);
        }

        void CameraLeanAndRotation()
        {
            bool leanEnabled = gameData == null || gameData.enableLean;

            // -------- LOOK INPUT --------
            float sensitivity = gameData.mouseSensitivity / 5f;
            if (usingGamepadLook)
            {
                sensitivity *= gameData.gamepadLookMultiplier;
            }

            Vector2 rawLook = Cursor.lockState == CursorLockMode.Locked ? LookInput : Vector2.zero;
            if (Cursor.lockState != CursorLockMode.Locked) smoothedLook = Vector2.zero;

            // invert Y if needed (BEFORE smoothing)
            if (gameData.invertLook)
                rawLook.y = -rawLook.y;

            Vector2 targetLook = rawLook * sensitivity;

            // Exponential smoothing (frame-rate independent)
            float lookT = 1f - Mathf.Exp(-mouseSmoothing * Time.deltaTime);
            smoothedLook = Vector2.Lerp(smoothedLook, targetLook, lookT);

            mouseInput = smoothedLook;

            // -------- ROTATION --------
            float YRot = verticalNeck.rotation.eulerAngles.y + mouseInput.x;

            verticalRotStore += mouseInput.y;
            verticalRotStore = Mathf.Clamp(verticalRotStore, minClamp, maxClamp);

            // -------- LEAN --------
            float targetLean = 0f;
            if (leanEnabled && leanLeftPressed)
                targetLean = leanAmount;
            else if (leanEnabled && leanRightPressed)
                targetLean = -leanAmount;

            float leanT = 1f - Mathf.Exp(-(
                targetLean == 0 ? lateralReturnTime : lateralSmoothTime
            ) * Time.deltaTime);

            currentLeanAngle = Mathf.Lerp(currentLeanAngle, targetLean, leanT);

            // -------- APPLY --------
            lateralTorso.rotation = Quaternion.Euler(0, YRot, currentLeanAngle);
            verticalNeck.rotation = Quaternion.Euler(-verticalRotStore, YRot, -currentLeanAngle);
        }

        void Crouching()
        {
            float desiredCamY = IsCrouching ? crouchCameraHeight : normalCameraHeight;
            float desiredCharHeight = IsCrouching ? crouchCharConHeight : normalCharConHeight;

            // Exponential smoothing (FPS independent)
            float t = 1f - Mathf.Exp(-crouchingCameraSmoothTime * Time.deltaTime);
            targetCameraHeight.y = Mathf.Lerp(targetCameraHeight.y, desiredCamY, t);

            float tc = 1f - Mathf.Exp(-crouchingCharConSmoothTime * Time.deltaTime);
            targetCharConHeight = Mathf.Lerp(targetCharConHeight, desiredCharHeight, tc);

            // Apply camera position
            lateralTorso.localPosition = targetCameraHeight;

            // Apply CharacterController adjustments
            Controller.height = targetCharConHeight;
            playerCollider.height = targetCharConHeight;
            Controller.center = new Vector3(0, targetCharConHeight / 2f, 0);
            playerCollider.center = new Vector3(0, targetCharConHeight / 2f, 0);
        }

        public void SetInWater(bool value)
        {
            InWater = value;
        }
        #endregion

        #region SoundDesign and Jump impulse
        void Footsteps()
        {
            // landing
            if (Controller.isGrounded && AirborneTime > jumpingTimeThreshold) // touching the floor after a time longer than the threshold
            {
                float impulseMagnitude = ((Mathf.Min(AirborneTime, jumpMaxThresholdTime) - jumpingTimeThreshold)) / (jumpMaxThresholdTime - jumpingTimeThreshold) + minImpulse;
                Vector3 impulse = jumpImpulseIntensity * impulseMagnitude * new Vector3(0, -1, 0);
                landBumpingImpulseSource.GenerateImpulseWithVelocity(impulse); // land bump impulse on Camera
                landShakingImpulseSource.GenerateImpulse(); // shake jump impulse on Camera

                PlayLandSound();
                AirborneTime = 0f;
            }
            if (!Controller.isGrounded) // not touching the floor
            {
                AirborneTime += Time.deltaTime;
            }
            else // touching the floor after a time shorter than the threshold
            {
                AirborneTime = 0f;
            }

            float currentInterval = GetCurrentInterval();
            if (currentInterval <= 0f)
                return;

            if (InWater)
            {
                currentInterval *= swimmingIntervalFactor;
            }

            // normal footstep
            if (Controller.isGrounded && IsMoving && Time.time > nextStepTime && Controller.velocity.magnitude > velocityThreshold)
            {
                PlayFootstepSound();
                nextStepTime = Time.time + currentInterval;
            }
        }

        float GetCurrentInterval()
        {
            float horizontalSpeed = new Vector3(
                Controller.velocity.x,
                0f,
                Controller.velocity.z
                ).magnitude;

            float currentInterval;

            if (horizontalSpeed < 0.1f)
            {
                return -1f;
            }
            else if (horizontalSpeed < walkSpeed * 0.8f)
            {
                currentInterval = crouchInterval;
            }
            else if (horizontalSpeed < sprintSpeed * 0.9f)
            {
                currentInterval = walkInterval;
            }
            else if (Speed == sprintSpeed)
            {
                currentInterval = sprintInterval;
            }
            else // trying to run but not going forward ---> force to walk
            {
                currentInterval = walkInterval;
            }

            return currentInterval;
        }

        void PlayJumpSound()
        {
            jumpStartIndex++;
            if (jumpStartIndex >= jumpStartSounds.Length)
            {
                jumpStartIndex = 0;
            }

            AudioClip clip = jumpStartSounds[jumpStartIndex];
            float pitch = Random.Range(0.975f, 1.025f);
            audioPool.Play(clip, feetTransform.position, pitch);

            if (InWater)
            {
                PlayFootstepSound();
            }
        }

        void PlayLandSound()
        {
            if (InWater == false)
            {
                RaycastHit hit;
                if (Physics.Raycast(feetTransform.position, Vector3.down, out hit, 1.2f))
                {
                    if (hit.transform.GetComponent<Terrain>() != null) // for terrain
                    {
                        string layerName = checker.GetDominantLayerAtPosition(transform.position,
                            hit.transform.GetComponent<Terrain>());

                        bool found = false;
                        foreach (SurfaceSFX effect in effects)
                        {
                            foreach (TerrainLayer layer in effect.layers)
                            {
                                if (layerName == layer.name)
                                {
                                    sfxClips = effect.jumpLandSounds;
                                    found = true;
                                    break;
                                }
                            }
                            if (found) break;
                        }
                        if (!found)
                        {
                            sfxClips = genericEffect.jumpLandSounds;
                        }

                    }
                    else if (hit.collider.gameObject.GetComponent<ObjectLayer>())
                    {
                        sfxClips = hit.collider.gameObject.GetComponent<ObjectLayer>().surfaceType.jumpLandSounds;
                    }
                    else // generic
                    {
                        sfxClips = genericEffect.jumpLandSounds;
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                sfxClips = waterEffect.jumpLandSounds;
            }

            // safety check
            if (sfxClips == null || sfxClips.Length == 0)
                return;

            int newjumpLandIndex = Random.Range(0, sfxClips.Length);
            while (newjumpLandIndex == jumpLandIndex)
            {
                newjumpLandIndex = Random.Range(0, sfxClips.Length);
            }
            jumpLandIndex = newjumpLandIndex;

            PlayClip(jumpLandIndex);
        }

        void PlayFootstepSound()
        {
            float volume = (IsCrouching ? crouchVolumeReduction : footstepVolume); // volume of sfx when the player is crouched is reduced

            if (InWater == false)
            {
                RaycastHit hit;
                if (Physics.Raycast(feetTransform.position, Vector3.down, out hit, 1.2f))
                {
                    if (hit.transform.GetComponent<Terrain>() != null) // for terrain
                    {
                        string layerName = checker.GetDominantLayerAtPosition(transform.position,
                            hit.transform.GetComponent<Terrain>());

                        bool found = false;
                        foreach (SurfaceSFX effect in effects)
                        {
                            foreach (TerrainLayer layer in effect.layers)
                            {
                                if (layerName == layer.name)
                                {
                                    sfxClips = effect.walkSounds;
                                    found = true;
                                    break;
                                }
                            }
                            if (found) break;
                        }
                        if (!found)
                        {
                            sfxClips = genericEffect.walkSounds;
                        }
                    }
                    else if (hit.collider.gameObject.GetComponent<ObjectLayer>())
                    {
                        sfxClips = hit.collider.gameObject.GetComponent<ObjectLayer>().surfaceType.walkSounds;
                    }
                    else if (hit.transform != null) // generic
                    {
                        sfxClips = genericEffect.walkSounds;
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                sfxClips = waterEffect.walkSounds;

                volume = 1;
            }

            // safety check
            if (sfxClips == null || sfxClips.Length == 0)
                return;

            int newfootstepIndex = Random.Range(0, sfxClips.Length);
            while (newfootstepIndex == footstepIndex)
            {
                newfootstepIndex = Random.Range(0, sfxClips.Length);
            }
            footstepIndex = newfootstepIndex;

            PlayClip(footstepIndex, volume);
        }

        void PlayClip(int index, float volume = 1f)
        {
            if (sfxClips == null || index < 0 || index >= sfxClips.Length)
                return;

            AudioClip clip = sfxClips[index];
            float pitch = Random.Range(0.95f, 1.05f);
            audioPool.Play(clip, feetTransform.position, volume, pitch);
        }
        #endregion
    }
}
