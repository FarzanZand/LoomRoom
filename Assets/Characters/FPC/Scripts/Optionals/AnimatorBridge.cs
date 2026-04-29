namespace MFPC
{
    using UnityEngine;

    public class AnimatorBridge : MonoBehaviour
    {
        [Header("References")]

        [Tooltip("Animator that will receive movement parameters. If left empty, the component will search in children.")]
        [SerializeField] private Animator animator;
        [Tooltip("Automatically find an MFPC controller on this GameObject.")]
        [SerializeField] private bool autoDetectController = true;
        [Tooltip("Reference to the New Input System controller. Used only if auto-detect is disabled or multiple controllers exist.")]
        [SerializeField] private PlayerController_NewInputSystem controller_NewInputSystem;
        [Tooltip("Reference to the Legacy controller. Used only if auto-detect is disabled or multiple controllers exist.")]
        [SerializeField] private PlayerController_Legacy controller_Legacy;
        [Tooltip("CharacterController that is used to read velocity and grounded state.")]
        private CharacterController characterController;


        [Header("Settings")]

        [Tooltip("Update animator parameters in LateUpdate instead of Update. Useful when animations depend on final movement values.")]
        [SerializeField] private bool useLateUpdate = false;


        [Header("Animator Parameters")]

        [Tooltip("Float parameter representing horizontal movement speed. Typically drives Idle/Walk/Run blend trees.")]
        [SerializeField] private string speedParam = "Speed";
        [Tooltip("Bool parameter indicating whether the player is grounded.")]
        [SerializeField] private string groundedParam = "IsGrounded";
        [Tooltip("Bool parameter indicating crouch state.")]
        [SerializeField] private string crouchParam = "IsCrouching";
        [Tooltip("Bool parameter indicating sprint state.")]
        [SerializeField] private string sprintParam = "IsSprinting";
        [Tooltip("Bool parameter indicating aiming state.")]
        [SerializeField] private string aimParam = "IsAiming";
        [Tooltip("Bool parameter indicating falling state (airborne with negative vertical velocity).")]
        [SerializeField] private string fallingParam = "IsFalling";
        [Tooltip("Bool parameter indicating jumping state (airborne with positive vertical velocity).")]
        [SerializeField] private string jumpingParam = "IsJumping";
        [Tooltip("Float parameter representing vertical velocity. Useful for jump/fall blend trees.")]
        [SerializeField] private string verticalVelParam = "VerticalVelocity";


        private void Awake()
        {
            if (!animator)
                animator = GetComponentInChildren<Animator>();

            if (autoDetectController)
            {
                controller_NewInputSystem = GetComponent<PlayerController_NewInputSystem>();
                controller_Legacy = GetComponent<PlayerController_Legacy>();
            }

            characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (!useLateUpdate)
                UpdateAnimator();
        }

        private void LateUpdate()
        {
            if (useLateUpdate)
                UpdateAnimator();
        }

        private void UpdateAnimator()
        {
            if (!animator || !characterController)
                return;

            bool isMoving = false;
            bool isSprinting = false;
            bool isCrouching = false;
            bool isAiming = false;

            if (controller_NewInputSystem)
            {
                isMoving = controller_NewInputSystem.IsMoving;
                isSprinting = controller_NewInputSystem.IsSprinting;
                isCrouching = controller_NewInputSystem.IsCrouching;
                isAiming = controller_NewInputSystem.IsAiming;
            }
            else if (controller_Legacy)
            {
                isMoving = controller_Legacy.IsMoving;
                isSprinting = controller_Legacy.IsSprinting;
                isCrouching = controller_Legacy.IsCrouching;
                isAiming = controller_Legacy.IsAiming;
            }
            else
            {
                Debug.Log("AnimatorBridge: No MFPC controller found.");
                return;
            }

            Vector3 velocity = characterController.velocity;
            float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;

            bool isFalling = !characterController.isGrounded && velocity.y < -0.1f;
            bool isJumping = !characterController.isGrounded && velocity.y > 0.1f;

            animator.SetFloat(speedParam, horizontalSpeed);
            animator.SetBool(groundedParam, characterController.isGrounded);
            animator.SetBool(crouchParam, isCrouching);
            animator.SetBool(sprintParam, isSprinting);
            animator.SetBool(aimParam, isAiming);
            animator.SetBool(fallingParam, isFalling);
            animator.SetBool(jumpingParam, isJumping);
            animator.SetFloat(verticalVelParam, velocity.y);
        }
    }
}