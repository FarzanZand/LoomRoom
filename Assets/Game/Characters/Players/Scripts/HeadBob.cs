using Unity.Cinemachine;
using UnityEngine;

// Step-timed head bob on the evaluated camera: a dip on every footstep, a sideways sway and a little
// roll over each pair of steps. Walk, run and crouch-walk each have their own gait; standing still or
// being in the air fades it out. Settings live on WorldManager (Head Bob). Added by PlayerCameraRig.
public class HeadBob : CinemachineExtension
{
    Player player;
    float phase, weight, stepsPerSecond, vertical, sideways, roll;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || deltaTime <= 0f) return;
        if (player == null) player = GetComponentInParent<Player>();
        var s = WorldManager.HasInstance ? WorldManager.Instance.headBob : null;
        var motor = player != null ? player.Motor : null;
        if (s == null || motor == null) return;

        bool live = s.enabled && player.IsActive && motor.IsGrounded && motor.IsMoving &&
                    (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        var gait = !live ? null : motor.State switch
        {
            MoveState.Walk => s.walk,
            MoveState.Run => s.run,
            MoveState.CrouchWalk => s.crouch,
            _ => null,
        };

        float t = 1f - Mathf.Exp(-s.blendSpeed * deltaTime);
        weight = Mathf.Lerp(weight, gait != null ? 1f : 0f, t);
        if (gait != null)
        {
            stepsPerSecond = Mathf.Lerp(stepsPerSecond, gait.stepsPerSecond, t);
            vertical = Mathf.Lerp(vertical, gait.vertical, t);
            sideways = Mathf.Lerp(sideways, gait.sideways, t);
            roll = Mathf.Lerp(roll, gait.roll, t);
        }
        if (weight < .001f) { phase = 0f; return; } // next walk starts on a fresh step

        // One step is half a cycle: the sway goes left on one foot and right on the next.
        phase += Mathf.PI * stepsPerSecond * s.tempo * deltaTime;
        float scale = player.transform.lossyScale.y * s.strength * weight;
        float dip = (Mathf.Cos(2f * phase) - 1f) * .5f; // 0 between steps, -1 on each footfall
        var offset = new Vector3(Mathf.Sin(phase) * sideways, dip * vertical, 0f) * scale;

        state.PositionCorrection += state.GetFinalOrientation() * offset;
        state.OrientationCorrection *= Quaternion.Euler(0f, 0f, -Mathf.Sin(phase) * roll * s.strength * weight);
    }
}
