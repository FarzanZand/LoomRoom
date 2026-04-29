namespace MFPC
{
    using UnityEngine;
    using UnityEngine.Events;

    public class EventObserver : MonoBehaviour
    {
        [Header("Controller Reference")]

        [Tooltip("Auto-detect MFPC controller on this GameObject.")]
        [SerializeField] bool autoDetectController = true;
        [Tooltip("Reference to the New Input System controller. Used only if auto-detect is disabled or multiple controllers exist.")]
        [SerializeField] private PlayerController controller_NewInputSystem;
        [Tooltip("Reference to the Legacy controller. Used only if auto-detect is disabled or multiple controllers exist.")]
        [SerializeField] private PlayerController_Legacy controller_Legacy;

        CharacterController characterController;


        [Header("Events")]

        [Tooltip("Invoked when player leaves ground.")]
        public UnityEvent OnAirBorneStart;
        [Tooltip("Invoked when player becomes grounded.")]
        public UnityEvent OnLand;
        [Tooltip("Invoked when sprinting starts.")]
        public UnityEvent OnSprintStart;
        [Tooltip("Invoked when sprinting ends.")]
        public UnityEvent OnSprintEnd;
        [Tooltip("Invoked when crouching starts.")]
        public UnityEvent OnCrouchStart;
        [Tooltip("Invoked when crouching ends.")]
        public UnityEvent OnCrouchEnd;
        [Tooltip("Invoked when aiming starts.")]
        public UnityEvent OnAimStart;
        [Tooltip("Invoked when aiming ends.")]
        public UnityEvent OnAimEnd;
        [Tooltip("Invoked when aiming starts.")]
        public UnityEvent OnMoveStart;
        [Tooltip("Invoked when aiming ends.")]
        public UnityEvent OnMoveEnd;
        [Tooltip("Invoked when aiming starts.")]
        public UnityEvent OnLeanLeftStart;
        [Tooltip("Invoked when aiming ends.")]
        public UnityEvent OnLeanLeftEnd;
        [Tooltip("Invoked when aiming starts.")]
        public UnityEvent OnLeanRightStart;
        [Tooltip("Invoked when aiming ends.")]
        public UnityEvent OnLeanRightEnd;


        [Header("Debug")]

        [Tooltip("Debug events in the console.")]
        [SerializeField] bool debugTransitions = false;

        // Cached previous states
        bool prevGrounded;
        bool prevSprinting;
        bool prevCrouching;
        bool prevAiming;
        bool prevMoving;
        bool prevLeaningLeft;
        bool prevLeaningRight;

        void Awake()
        {
            if (autoDetectController)
            {
                controller_NewInputSystem = GetComponent<PlayerController>();
                controller_Legacy = GetComponent<PlayerController_Legacy>();
            }

            characterController = GetComponent<CharacterController>();
        }

        void Start()
        {
            if (!characterController)
                return;

            prevGrounded = true;

            if (controller_NewInputSystem)
            {
                prevSprinting = controller_NewInputSystem.IsSprinting;
                prevCrouching = controller_NewInputSystem.IsCrouching;
                prevAiming = controller_NewInputSystem.IsAiming;
                prevMoving = controller_NewInputSystem.IsMoving;
                prevLeaningLeft = controller_NewInputSystem.IsLeftLeaning;
                prevLeaningRight = controller_NewInputSystem.IsRightLeaning;
            }
            else if (controller_Legacy)
            {
                prevSprinting = controller_Legacy.IsSprinting;
                prevCrouching = controller_Legacy.IsCrouching;
                prevAiming = controller_Legacy.IsAiming;
                prevMoving = controller_Legacy.IsMoving;
                prevLeaningLeft = controller_Legacy.IsLeftLeaning;
                prevLeaningRight = controller_Legacy.IsRightLeaning;
            }
        }

        void Update()
        {
            if (!characterController)
                return;

            bool isGrounded = characterController.isGrounded;

            bool isSprinting = false;
            bool isCrouching = false;
            bool isAiming = false;
            bool isMoving = false;
            bool isLeaningLeft = false;
            bool isLeaningRight = false;

            if (controller_NewInputSystem)
            {
                isSprinting = controller_NewInputSystem.IsSprinting;
                isCrouching = controller_NewInputSystem.IsCrouching;
                isAiming = controller_NewInputSystem.IsAiming;
                isMoving = controller_NewInputSystem.IsMoving;
                isLeaningLeft = controller_NewInputSystem.IsLeftLeaning;
                isLeaningRight = controller_NewInputSystem.IsRightLeaning;
            }
            else if (controller_Legacy)
            {
                isSprinting = controller_Legacy.IsSprinting;
                isCrouching = controller_Legacy.IsCrouching;
                isAiming = controller_Legacy.IsAiming;
                isMoving = controller_Legacy.IsMoving;
                isLeaningLeft = controller_Legacy.IsLeftLeaning;
                isLeaningRight = controller_Legacy.IsRightLeaning;
            }
            else
            {
                return;
            }

            // -------- Grounded transitions --------
            if (!isGrounded && prevGrounded)
            {
                if (debugTransitions) Debug.Log("Jump detected");
                OnAirBorneStart?.Invoke();
            }
            else if (isGrounded && !prevGrounded)
            {
                if (debugTransitions) Debug.Log("Land detected");
                OnLand?.Invoke();
            }

            // -------- Sprint transitions --------
            if (isSprinting && !prevSprinting)
            {
                if (debugTransitions) Debug.Log("Sprint Start");
                OnSprintStart?.Invoke();
            }
            else if (!isSprinting && prevSprinting)
            {
                if (debugTransitions) Debug.Log("Sprint End");
                OnSprintEnd?.Invoke();
            }

            // -------- Crouch transitions --------
            if (isCrouching && !prevCrouching)
            {
                if (debugTransitions) Debug.Log("Crouch Start");
                OnCrouchStart?.Invoke();
            }
            else if (!isCrouching && prevCrouching)
            {
                if (debugTransitions) Debug.Log("Crouch End");
                OnCrouchEnd?.Invoke();
            }

            // -------- Aim transitions --------
            if (isAiming && !prevAiming)
            {
                if (debugTransitions) Debug.Log("Aim Start");
                OnAimStart?.Invoke();
            }
            else if (!isAiming && prevAiming)
            {
                if (debugTransitions) Debug.Log("Aim End");
                OnAimEnd?.Invoke();
            }

            // -------- Move transitions --------
            if (isMoving && !prevMoving)
            {
                if (debugTransitions) Debug.Log("Move Start");
                OnMoveStart?.Invoke();
            }
            else if (!isMoving && prevMoving)
            {
                if (debugTransitions) Debug.Log("Move End");
                OnMoveEnd?.Invoke();
            }

            // -------- Left lean transitions --------
            if (isLeaningLeft && !prevLeaningLeft)
            {
                if (debugTransitions) Debug.Log("Left Lean Start");
                OnLeanLeftStart?.Invoke();
            }
            else if (!isLeaningLeft && prevLeaningLeft)
            {
                if (debugTransitions) Debug.Log("Left Lean End");
                OnLeanLeftEnd?.Invoke();
            }

            // -------- Right lean transitions --------
            if (isLeaningRight && !prevLeaningRight)
            {
                if (debugTransitions) Debug.Log("Right Lean Start");
                OnLeanRightStart?.Invoke();
            }
            else if (!isLeaningRight && prevLeaningRight)
            {
                if (debugTransitions) Debug.Log("Right Lean End");
                OnLeanRightEnd?.Invoke();
            }

            // Cache states
            prevGrounded = isGrounded;
            prevSprinting = isSprinting;
            prevCrouching = isCrouching;
            prevAiming = isAiming;
            prevMoving = isMoving;
            prevLeaningLeft = isLeaningLeft;
            prevLeaningRight = isLeaningRight;
        }
    }
}