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

        protected override void Awake()
        {
            base.Awake();
            roomInstance = this;
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
