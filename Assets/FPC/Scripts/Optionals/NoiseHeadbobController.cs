namespace MFPC
{
    using UnityEngine;
    using Unity.Cinemachine;

    public class NoiseHeadbobController : MonoBehaviour
    {
        public GameData gameData;
        private CinemachineVirtualCamera[] virtualCameras;

        void Start()
        {
            if (gameData.EnableNoiseAndHeadbob) return;

            virtualCameras = GetComponentsInChildren<CinemachineVirtualCamera>(true);

            foreach (CinemachineVirtualCamera vc in virtualCameras)
            {
                CinemachineBasicMultiChannelPerlin noise =
                    vc.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();

                if (noise != null)
                {
                    noise.AmplitudeGain = 0f;
                    noise.FrequencyGain = 0f;
                    noise.enabled = false;
                }
            }
        }
    }
}