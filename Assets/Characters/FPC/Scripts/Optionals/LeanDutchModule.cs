namespace MFPC
{
    using System.Collections.Generic;
    using Unity.Cinemachine;
    using UnityEngine;

    public class LeanDutchModule : MonoBehaviour
    {
        [Tooltip("Multiplier applied to the controller lean angle before it is sent to Cinemachine Dutch. Use a negative value to invert the roll direction.")]
        [SerializeField] private float dutchMultiplier = 0.5f;
        [Tooltip("Maximum absolute Dutch angle applied to the camera.")]
        [SerializeField] private float maxDutchAngle = 15f;
        [Tooltip("Optional explicit list of virtual cameras to affect. If left empty, all child Cinemachine virtual cameras are used.")]
        [SerializeField] private CinemachineVirtualCameraBase[] virtualCameras;

        private PlayerController_Legacy legacyController;
        private PlayerController newInputSystemController;


        private void Reset()
        {
            CacheReferences();
            CacheVirtualCameras();
        }

        private void Awake()
        {
            CacheReferences();
            CacheVirtualCameras();
            EnsureExtensions();
        }

        private void OnEnable()
        {
            EnsureExtensions();
        }

        private void OnValidate()
        {
            maxDutchAngle = Mathf.Clamp(maxDutchAngle, 0f, 89f);
            CacheVirtualCameras();
        }

        private void CacheReferences()
        {
            if (!legacyController)
            {
                legacyController = GetComponent<PlayerController_Legacy>();
            }

            if (!newInputSystemController)
            {
                newInputSystemController = GetComponent<PlayerController>();
            }
        }

        private void CacheVirtualCameras()
        {
            if (virtualCameras != null && virtualCameras.Length > 0)
            {
                return;
            }

            virtualCameras = GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
        }

        private void EnsureExtensions()
        {
            CinemachineVirtualCameraBase[] cameras = GetTargetVirtualCameras();
            for (int i = 0; i < cameras.Length; i++)
            {
                CinemachineVirtualCameraBase virtualCamera = cameras[i];
                if (!virtualCamera)
                {
                    continue;
                }

                LeanDutchCinemachineExtension extension =
                    virtualCamera.GetComponent<LeanDutchCinemachineExtension>();

                if (!extension)
                {
                    extension = virtualCamera.gameObject.AddComponent<LeanDutchCinemachineExtension>();
                }

                extension.SetModule(this);
            }
        }

        internal bool TryGetDutch(out float dutch)
        {
            dutch = 0f;
            if (!isActiveAndEnabled)
            {
                return false;
            }

            float leanAngle = 0f;
            if (newInputSystemController)
            {
                leanAngle = newInputSystemController.CurrentLeanAngle;
            }
            else if (legacyController)
            {
                leanAngle = legacyController.CurrentLeanAngle;
            }
            else
            {
                return false;
            }

            float targetDutch = Mathf.Clamp(
                leanAngle * dutchMultiplier,
                -maxDutchAngle,
                maxDutchAngle);

            if (Mathf.Abs(targetDutch) <= 0.001f)
            {
                return false;
            }

            dutch = targetDutch;
            return true;
        }

        private CinemachineVirtualCameraBase[] GetTargetVirtualCameras()
        {
            if (virtualCameras == null || virtualCameras.Length == 0)
            {
                return GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            }

            List<CinemachineVirtualCameraBase> cameras = new List<CinemachineVirtualCameraBase>(virtualCameras.Length);
            for (int i = 0; i < virtualCameras.Length; i++)
            {
                if (virtualCameras[i])
                {
                    cameras.Add(virtualCameras[i]);
                }
            }

            return cameras.ToArray();
        }
    }
}
