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

    // A death that throws the body (the Dungeon Master's die, DungeonMasterDie): while it flies the view goes over
    // backwards, head over heels, that many times, and ends as the fall leaves it, lying on its side.
    public static void Tumble(float seconds, float turns) { tumbled = 0f; tumbleSeconds = Mathf.Max(.1f, seconds); tumbleTurns = turns; }
    static float tumbled = 99f, tumbleSeconds = 1f, tumbleTurns;
    static int tumbleFrame = -1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTumble() { tumbled = 99f; tumbleTurns = 0f; tumbleFrame = -1; }

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
        // Thrown: over backwards first (the view swings up and on round, about its own level axis), quick at first and
        // easing out as the body lands; the fall's own tip onto one side comes on top of it.
        // Its clock steps once a frame by the frame's own length with the slow motion taken out, as the thrown body's does.
        if (tumbleFrame != Time.frameCount && Time.timeScale > 0f) { tumbleFrame = Time.frameCount; tumbled += Mathf.Min(Time.deltaTime / Time.timeScale, .05f); }
        float thrown = tumbled / tumbleSeconds;
        if (tumbleTurns != 0f && thrown < 1f)
            state.OrientationCorrection *= Quaternion.Euler(-360f * tumbleTurns * (1f - (1f - thrown) * (1f - thrown)), 0f, 0f);
        state.OrientationCorrection *= Quaternion.Euler(tuning.deathFallPitch * tip, 0f, tuning.deathFallRoll * side * tip);
        if (arms != null) arms.LowerBy = tuning.deathFallArmsDrop * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
    }
}
