namespace MFPC
{
    using UnityEngine;

    public class PlayerControllerRoom : PlayerController
    {
        public static PlayerControllerRoom roomInstance;

        [Header("Virtual Cameras and Aim")]
        [SerializeField] GameObject normalVCs;
        [SerializeField] GameObject aimVCs;
        [SerializeField] GameObject[] idleVC;
        [SerializeField] GameObject[] walkVC;
        [SerializeField] GameObject[] crouchWalkVC;
        [SerializeField] GameObject[] runVC;

        public Animator armsAnimator;
        public bool RightHandEquipped { get; private set; }
        public bool LeftHandEquipped { get; private set; }

        private bool primaryActionHeld;
        private bool secondaryActionHeld;

        protected override void Awake()
        {
            base.Awake();
            roomInstance = this;
            if (ItemHolder.Instance != null)
                ItemHolder.Instance.OnHeldItemChanged += OnHeldItemChanged;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            inputActions.Player.PrimaryAction.performed += _ => primaryActionHeld = true;
            inputActions.Player.PrimaryAction.canceled  += _ => primaryActionHeld = false;
            inputActions.Player.SecondaryAction.performed += _ => secondaryActionHeld = true;
            inputActions.Player.SecondaryAction.canceled  += _ => secondaryActionHeld = false;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (ItemHolder.Instance != null)
                ItemHolder.Instance.OnHeldItemChanged -= OnHeldItemChanged;
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

        bool IsAttacking()
        {
            if (armsAnimator == null) return false;
            var cur  = armsAnimator.GetCurrentAnimatorStateInfo(1);
            var next = armsAnimator.GetNextAnimatorStateInfo(1);
            return cur.IsName("Attack_Windup")  || cur.IsName("Attack_Hold")  || cur.IsName("Attack_Release")
                || next.IsName("Attack_Windup") || next.IsName("Attack_Hold") || next.IsName("Attack_Release");
        }

        bool IsBlocking()
        {
            if (armsAnimator == null) return false;
            var cur  = armsAnimator.GetCurrentAnimatorStateInfo(2);
            var next = armsAnimator.GetNextAnimatorStateInfo(2);
            return cur.IsName("Block") || next.IsName("Block");
        }

        protected override void UpdateAnimator()
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
                armsAnimator.SetBool("AttackHeld", primaryActionHeld  && !IsBlocking()  && !menuOpen);
                armsAnimator.SetBool("BlockHeld",  secondaryActionHeld && !IsAttacking() && !menuOpen && shieldEquipped);
            }

            wasGrounded = grounded;
        }

        protected override void AimCheck()
        {
            normalVCs.SetActive(true);
            aimVCs.SetActive(false);
            IsAiming = false;
        }

        protected override void VirtualCameras()
        {
            if (!IsMoving)
            {
                idleVC[0].SetActive(true);
                idleVC[1].SetActive(true);
            }
            else
            {
                if (Speed == walkSpeed)
                {
                    idleVC[0].SetActive(false);
                    walkVC[0].SetActive(true);

                    idleVC[1].SetActive(false);
                    walkVC[1].SetActive(true);
                }
                else if (Speed == crouchSpeed)
                {
                    idleVC[0].SetActive(false);
                    walkVC[0].SetActive(false);
                    crouchWalkVC[0].SetActive(true);

                    idleVC[1].SetActive(false);
                    walkVC[1].SetActive(false);
                    crouchWalkVC[1].SetActive(true);
                }
                else if (Speed == sprintSpeed)
                {
                    idleVC[0].SetActive(false);
                    walkVC[0].SetActive(false);
                    crouchWalkVC[0].SetActive(false);
                    runVC[0].SetActive(true);

                    idleVC[1].SetActive(false);
                    walkVC[1].SetActive(false);
                    crouchWalkVC[1].SetActive(false);
                    runVC[1].SetActive(true);
                }
            }
        }
    }
}
