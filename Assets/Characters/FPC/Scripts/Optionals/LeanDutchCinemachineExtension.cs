namespace MFPC
{
    using Unity.Cinemachine;
    using UnityEngine;

    [AddComponentMenu("")]
    public class LeanDutchCinemachineExtension : CinemachineExtension
    {
        [SerializeField, HideInInspector] private LeanDutchModule leanDutchModule;


        public void SetModule(LeanDutchModule module)
        {
            leanDutchModule = module;
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage,
            ref CameraState state,
            float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || !leanDutchModule)
            {
                return;
            }

            if (!leanDutchModule.TryGetDutch(out float dutch))
            {
                return;
            }

            LensSettings lens = state.Lens;
            lens.Dutch += dutch;
            state.Lens = lens;
        }
    }
}
