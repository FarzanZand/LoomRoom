using Unity.Cinemachine;
using UnityEngine;

// The player's death seen from the eyes: the view drops to the floor and tips over onto one side, and the arms fall
// away out of view, while the death moment (RunManager: slow motion, the colour draining) plays. Runs on real time,
// so the slow motion doesn't drag it out. Revived (the recap's Revive, the dev console), it is gone at once. Added to
// the player's camera by PlayerCameraRig; tuned on CombatManager > Death > Player death.
public class PlayerDeathFall : CinemachineExtension
{
    Player player;
    PlayerViewPresentation arms;
    float diedAt = -1f, side = 1f, eyeAbove;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (player == null) { player = GetComponentInParent<Player>(); arms = player != null ? player.GetComponentInChildren<PlayerViewPresentation>(true) : null; }
        var tuning = CombatManager.HasInstance ? CombatManager.Instance : null;
        bool falling = player != null && !player.IsAlive && tuning != null && tuning.playerDeathFall;
        if (!falling)
        {
            if (diedAt >= 0f) { diedAt = -1f; if (arms != null) arms.LowerBy = 0f; }
            return;
        }
        if (diedAt < 0f)
        {
            diedAt = Time.unscaledTime;
            side = Random.value < .5f ? -1f : 1f;
            eyeAbove = state.GetFinalPosition().y - player.transform.position.y;
        }
        float k = Mathf.Clamp01((Time.unscaledTime - diedAt) / Mathf.Max(.05f, tuning.deathFallSeconds));
        // The knees give: slow at first, then down hard, with a small bounce off the floor.
        float drop = k * k;
        if (k >= 1f) drop = 1f;
        else if (k > .82f) drop = 1f - Mathf.Sin((k - .82f) / .18f * Mathf.PI) * .06f;
        float tip = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.15f));
        state.PositionCorrection += Vector3.down * (Mathf.Max(0f, eyeAbove - tuning.deathFallEyeHeight) * drop);
        state.OrientationCorrection *= Quaternion.Euler(tuning.deathFallPitch * tip, 0f, tuning.deathFallRoll * side * tip);
        if (arms != null) arms.LowerBy = tuning.deathFallArmsDrop * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
    }
}
