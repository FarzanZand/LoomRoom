using System;
using Unity.Cinemachine;
using UnityEngine;

// Cinemachine 3 camera feel. One CinemachineCamera per player; the noise amplitude
// and frequency blend by movement state instead of swapping whole cameras.
public class PlayerCameraRig : MonoBehaviour
{
    [Serializable]
    public class FeelState
    {
        public MoveState state;
        [Tooltip("Noise amplitude gain for this state.")]
        public float amplitude = 0.5f;
        [Tooltip("Noise frequency gain for this state.")]
        public float frequency = 1f;
    }

    [SerializeField] CinemachineCamera cam;
    [SerializeField] CinemachineBasicMultiChannelPerlin noise;
    [Tooltip("How quickly amplitude/frequency blend toward the current state's values.")]
    [SerializeField] float blendSpeed = 6f;
    [SerializeField] FeelState[] states =
    {
        new FeelState { state = MoveState.Idle,       amplitude = 0.3f, frequency = 0.3f },
        new FeelState { state = MoveState.Walk,       amplitude = 0.6f, frequency = 1.2f },
        new FeelState { state = MoveState.Run,        amplitude = 1.0f, frequency = 2.0f },
        new FeelState { state = MoveState.CrouchWalk, amplitude = 0.4f, frequency = 0.8f },
        new FeelState { state = MoveState.Airborne,   amplitude = 0.2f, frequency = 0.5f },
    };

    public CinemachineCamera Camera => cam;

    // Transient feel inputs written by PlayerFX every frame (charge pull-in, charge shake). Zero = none.
    public float FovOffset      { get; set; }
    public float ExtraAmplitude { get; set; }

    Player player;
    float amp, freq, baseFov;

    void Awake()
    {
        player = GetComponentInParent<Player>();
        if (cam == null)   cam   = GetComponentInChildren<CinemachineCamera>(true);
        if (noise == null && cam != null) noise = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise != null) { amp = noise.AmplitudeGain; freq = noise.FrequencyGain; }
        if (cam != null) baseFov = cam.Lens.FieldOfView;
        if (cam != null && cam.GetComponent<SwingCameraMotion>() == null)
            cam.gameObject.AddComponent<SwingCameraMotion>();
    }

    void Update()
    {
        if (cam != null)
        {
            var lens = cam.Lens;
            float user = UserSettings.FieldOfView;
            float fov = (user > 0f ? user : baseFov) + FovOffset;
            if (!Mathf.Approximately(lens.FieldOfView, fov)) { lens.FieldOfView = fov; cam.Lens = lens; }
        }

        if (noise == null || player == null || player.Motor == null) return;

        var target = Find(player.Motor.State);
        float ta = target != null ? target.amplitude : 0f;
        float tf = target != null ? target.frequency : 0f;

        float t = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        amp  = Mathf.Lerp(amp,  ta, t);
        freq = Mathf.Lerp(freq, tf, t);
        noise.AmplitudeGain = amp + ExtraAmplitude;
        noise.FrequencyGain = freq;
    }

    FeelState Find(MoveState s)
    {
        foreach (var f in states) if (f.state == s) return f;
        return null;
    }
}
