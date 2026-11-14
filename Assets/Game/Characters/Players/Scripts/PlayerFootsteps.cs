using Unity.Cinemachine;
using UnityEngine;

// Footstep, jump and landing sounds picked by surface, plus the landing camera
// impulses. Listens to PlayerMotor; plays through AudioManager's pool.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Origin")]
    [Tooltip("Transform used as the origin point for footstep sounds and the surface raycast.")]
    [SerializeField] Transform feetTransform;

    [Header("Volumes & Timing")]
    [SerializeField, Range(0f, 1f)] float footstepVolume = 0.5f;
    [Tooltip("Volume multiplier applied to footsteps while crouching.")]
    [SerializeField, Range(0f, 1f)] float crouchVolumeReduction = 0.5f;
    [Tooltip("Time between footsteps while walking (seconds).")]
    [SerializeField] float walkInterval   = 0.6f;
    [Tooltip("Time between footsteps while sprinting (seconds).")]
    [SerializeField] float sprintInterval = 0.4f;
    [Tooltip("Time between footsteps while crouching (seconds).")]
    [SerializeField] float crouchInterval = 0.7f;
    [Tooltip("Minimum velocity required to play footstep sounds.")]
    [SerializeField] float velocityThreshold = 0.1f;
    [SerializeField, Range(0f, 0.2f)] float pitchVariance = 0.05f;

    [Header("In step with the arms")]
    [Tooltip("Steps land where the first-person arms' walk bob dips (the user, 2026-11-14: the footsteps on the arms' cadence), at every pace: walking, sprinting and crouching alike, since the arms keep one beat. Off, or on a player without that layer (the room player), the intervals above set the pace.")]
    [SerializeField] bool stepWithArms = true;
    [Tooltip("The animator layer whose clip is the arms' walk bob.")]
    [SerializeField] string armsBobLayer = "Walk Bob";
    [Tooltip("Where in the walk bob's cycle a foot lands (normalized time): the two dips of the arms.")]
    [SerializeField] float[] armsStepTimes = { .25f, .75f };

    [Header("Surfaces")]
    [Tooltip("Surface sound presets matched against terrain layers.")]
    [SerializeField] SurfaceSFX[] effects;
    [Tooltip("Fallback preset when no specific surface is detected.")]
    [SerializeField] SurfaceSFX genericEffect;

    [Header("Jump & Land")]
    [Tooltip("Sounds played when the jump starts.")]
    [SerializeField] AudioClip[] jumpStartSounds;
    [Tooltip("Volume of the jump's grunt (Jump Start Sounds); halved from 1 on 2026-11-14 (the user).")]
    [SerializeField, Range(0f, 1f)] float jumpStartVolume = .5f;
    [Tooltip("Minimum airborne time required to trigger landing effects (seconds).")]
    [SerializeField] float jumpingTimeThreshold = 0.3f;
    [Tooltip("Maximum airborne time considered when calculating landing impulse strength (seconds).")]
    [SerializeField] float jumpMaxThresholdTime = 1f;
    [Tooltip("Overall intensity multiplier for landing camera impulses.")]
    [SerializeField] float jumpImpulseIntensity = 1f;
    [Tooltip("Minimum impulse strength applied on landing, even for short jumps.")]
    [SerializeField] float minImpulse = 0.3f;
    [SerializeField] CinemachineImpulseSource landShakingImpulseSource;
    [SerializeField] CinemachineImpulseSource landBumpingImpulseSource;
    [SerializeField] CinemachineImpulseSource jumpShakingImpulseSource;

    [Header("Noise (enemy hearing)")]
    [Tooltip("Radius enemies can hear a walking footstep from. 0 = silent.")]
    [SerializeField] float walkNoiseRadius   = 3f;
    [Tooltip("Radius enemies can hear a sprinting footstep from.")]
    [SerializeField] float sprintNoiseRadius = 8f;

    PlayerMotor    motor;
    Player         player;
    TerrainChecker checker;
    float nextStepTime;
    int   lastFootstepIndex = -1;
    int   lastLandIndex     = -1;
    int   jumpStartIndex;
    Animator armsAnimator;
    int   armsLayer = -1;
    float lastArmsPhase = -1f;

    void Awake()
    {
        motor   = GetComponent<PlayerMotor>();
        player  = GetComponent<Player>();
        checker = GetComponent<TerrainChecker>();
        if (feetTransform == null) feetTransform = transform;
        FindArms();
    }

    // The animator holding the arms' walk bob layer, if this player has one.
    void FindArms()
    {
        armsAnimator = null; armsLayer = -1;
        if (!stepWithArms || string.IsNullOrEmpty(armsBobLayer)) return;
        foreach (var a in GetComponentsInChildren<Animator>(true))
        {
            if (a.runtimeAnimatorController == null) continue;
            int layer = a.GetLayerIndex(armsBobLayer);
            if (layer >= 0) { armsAnimator = a; armsLayer = layer; return; }
        }
    }

    // The walk bob's place in its cycle (0-1), or -1 while it can't be read.
    float ArmsPhase()
    {
        if (armsAnimator == null || armsLayer < 0 || !armsAnimator.isActiveAndEnabled || armsLayer >= armsAnimator.layerCount) return -1f;
        float t = armsAnimator.GetCurrentAnimatorStateInfo(armsLayer).normalizedTime;
        return t - Mathf.Floor(t);
    }

    // True when the phase went past one of the step times since last frame.
    bool CrossedStep(float from, float to)
    {
        foreach (float at in armsStepTimes)
        {
            if (to >= from ? at > from && at <= to : at > from || at <= to) return true;
        }
        return false;
    }

    void OnEnable()
    {
        motor.Jumped += OnJumped;
        motor.Landed += OnLanded;
    }

    void OnDisable()
    {
        motor.Jumped -= OnJumped;
        motor.Landed -= OnLanded;
    }

    void Update()
    {
        // In step with the arms: the phase is followed every frame, walking or not, so a step falls on the beat.
        float phase = stepWithArms && armsAnimator != null ? ArmsPhase() : -1f;
        float lastPhase = lastArmsPhase;
        lastArmsPhase = phase;
        if (!motor.IsGrounded || !motor.IsMoving) return;
        float horizontal = new Vector3(motor.Velocity.x, 0f, motor.Velocity.z).magnitude;
        if (horizontal < velocityThreshold) return;
        if (phase >= 0f)
        {
            if (lastPhase >= 0f && CrossedStep(lastPhase, phase)) PlayFootstep();
            return;
        }

        float interval = motor.State switch
        {
            MoveState.Run        => sprintInterval,
            MoveState.CrouchWalk => crouchInterval,
            _                    => walkInterval,
        };

        if (Time.time < nextStepTime) return;
        PlayFootstep();
        nextStepTime = Time.time + interval;
    }

    void OnJumped()
    {
        if (jumpStartSounds != null && jumpStartSounds.Length > 0 && AudioManager.HasInstance)
        {
            jumpStartIndex = (jumpStartIndex + 1) % jumpStartSounds.Length;
            AudioManager.Instance.PlaySFX(jumpStartSounds[jumpStartIndex], feetTransform.position, jumpStartVolume, 0.025f);
        }
        jumpShakingImpulseSource?.GenerateImpulse();
    }

    void OnLanded(float airTime)
    {
        if (airTime <= jumpingTimeThreshold) return;

        float magnitude = (Mathf.Min(airTime, jumpMaxThresholdTime) - jumpingTimeThreshold)
                          / Mathf.Max(0.01f, jumpMaxThresholdTime - jumpingTimeThreshold) + minImpulse;
        landBumpingImpulseSource?.GenerateImpulseWithVelocity(jumpImpulseIntensity * magnitude * Vector3.down);
        landShakingImpulseSource?.GenerateImpulse();

        var surface = ResolveSurface();
        var clips = surface != null ? surface.jumpLandSounds : null;
        PlayRandom(clips, ref lastLandIndex, 1f);
        ReportNoise(sprintNoiseRadius);
    }

    void PlayFootstep()
    {
        float volume = motor.IsCrouching ? footstepVolume * crouchVolumeReduction : footstepVolume;
        var surface = ResolveSurface();
        var clips = surface != null ? surface.walkSounds : null;
        PlayRandom(clips, ref lastFootstepIndex, volume);
        ReportNoise(motor.State == MoveState.Run ? sprintNoiseRadius : (motor.IsCrouching ? 0f : walkNoiseRadius));
    }

    void ReportNoise(float radius)
    {
        if (radius > 0f) NoiseEvents.Report(transform.position, radius, player);
    }

    SurfaceSFX ResolveSurface()
    {
        if (!Physics.Raycast(feetTransform.position, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            return null;

        var terrain = hit.transform.GetComponent<Terrain>();
        if (terrain != null && checker != null && effects != null)
        {
            string layerName = checker.GetDominantLayerAtPosition(transform.position, terrain);
            foreach (var e in effects)
            {
                if (e == null || e.layers == null) continue;
                foreach (var layer in e.layers)
                    if (layer != null && layer.name == layerName) return e;
            }
        }
        return genericEffect;
    }

    void PlayRandom(AudioClip[] clips, ref int lastIndex, float volume)
    {
        if (clips == null || clips.Length == 0 || !AudioManager.HasInstance) return;
        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1)
            while (index == lastIndex) index = Random.Range(0, clips.Length);
        lastIndex = index;
        if (player != null && player.IsActive)
            AudioManager.Instance.PlaySFX2D(clips[index], volume, pitchVariance);
        else AudioManager.Instance.PlaySFX(clips[index], feetTransform.position, volume, pitchVariance);
    }
}
