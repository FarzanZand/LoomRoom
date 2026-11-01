using Unity.Cinemachine;
using UnityEngine;

// The dash's lean, nod and dip on the evaluated camera (PlayerDash), never on the look target. Added by PlayerDash.
public class DashCameraMotion : CinemachineExtension
{
    PlayerDash dash;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (dash == null) dash = GetComponentInParent<PlayerDash>();
        if (dash == null || !dash.isActiveAndEnabled) return;
        var lean = dash.CameraRotation;
        if (lean.sqrMagnitude < 1e-6f && Mathf.Abs(dash.CameraDip) < 1e-5f) return;
        state.PositionCorrection += Vector3.up * dash.CameraDip;
        state.OrientationCorrection *= Quaternion.Euler(lean);
    }
}
