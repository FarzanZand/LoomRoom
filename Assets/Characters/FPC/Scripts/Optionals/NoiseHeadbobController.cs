namespace MFPC
{
    using UnityEngine;
    using Unity.Cinemachine;

    public class NoiseHeadbobController : MonoBehaviour
    {
        public GameData gameData;
        void Start()
        {
            if (gameData.EnableNoiseAndHeadbob) return;

            foreach (var vc in GetComponentsInChildren<CinemachineCamera>(true))
            {
                var noise = vc.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>(true);
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