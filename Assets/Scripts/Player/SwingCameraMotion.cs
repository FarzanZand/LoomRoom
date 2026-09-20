using Unity.Cinemachine;
using UnityEngine;

// Add rotation to the evaluated camera, never to the mouse-look target or player root.
public class SwingCameraMotion : CinemachineExtension
{
    Player player;
    Vector3 rotation;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (player == null) player = GetComponentInParent<Player>();
        var tuning = CombatManager.HasInstance ? CombatManager.Instance : null;
        if (deltaTime < 0f || tuning == null || !tuning.swingCameraMotionEnabled ||
            player == null || !player.IsActive || player.Combat == null ||
            (GameManager.HasInstance && !GameManager.Instance.GameplayActive))
        {
            rotation = Vector3.zero;
            return;
        }
        rotation = Vector3.Lerp(rotation, player.Combat.SwingCameraRotation(tuning),
            1f - Mathf.Exp(-Mathf.Max(1f, tuning.swingCameraBlendSpeed) * deltaTime));
        state.OrientationCorrection *= Quaternion.Euler(rotation);
    }
}
