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
    [Tooltip("Footstep interval multiplier while in water.")]
    [SerializeField] float swimmingIntervalFactor = 2f;
    [SerializeField, Range(0f, 0.2f)] float pitchVariance = 0.05f;

    [Header("Surfaces")]
    [Tooltip("Surface sound presets matched against terrain layers and ObjectLayer components.")]
    [SerializeField] SurfaceSFX[] effects;
    [Tooltip("Fallback preset when no specific surface is detected.")]
    [SerializeField] SurfaceSFX genericEffect;
    [Tooltip("Preset used while in water.")]
    [SerializeField] SurfaceSFX waterEffect;

    [Header("Jump & Land")]
    [Tooltip("Sounds played when the jump starts.")]
    [SerializeField] AudioClip[] jumpStartSounds;
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

    void Awake()
    {
        motor   = GetComponent<PlayerMotor>();
        player  = GetComponent<Player>();
        checker = GetComponent<TerrainChecker>();
        if (feetTransform == null) feetTransform = transform;
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
        if (!motor.IsGrounded || !motor.IsMoving) return;
        float horizontal = new Vector3(motor.Velocity.x, 0f, motor.Velocity.z).magnitude;
        if (horizontal < velocityThreshold) return;

        float interval = motor.State switch
        {
            MoveState.Run        => sprintInterval,
            MoveState.CrouchWalk => crouchInterval,
            _                    => walkInterval,
        };
        if (motor.InWater) interval *= swimmingIntervalFactor;

        if (Time.time < nextStepTime) return;
        PlayFootstep();
        nextStepTime = Time.time + interval;
    }

    void OnJumped()
    {
        if (jumpStartSounds != null && jumpStartSounds.Length > 0 && AudioManager.HasInstance)
        {
            jumpStartIndex = (jumpStartIndex + 1) % jumpStartSounds.Length;
            AudioManager.Instance.PlaySFX(jumpStartSounds[jumpStartIndex], feetTransform.position, 1f, 0.025f);
        }
        jumpShakingImpulseSource?.GenerateImpulse();
        if (motor.InWater) PlayFootstep();
    }

    void OnLanded(float airTime)
    {
        if (airTime <= jumpingTimeThreshold) return;

        float magnitude = (Mathf.Min(airTime, jumpMaxThresholdTime) - jumpingTimeThreshold)
                          / Mathf.Max(0.01f, jumpMaxThresholdTime - jumpingTimeThreshold) + minImpulse;
        landBumpingImpulseSource?.GenerateImpulseWithVelocity(jumpImpulseIntensity * magnitude * Vector3.down);
        landShakingImpulseSource?.GenerateImpulse();

        var clips = ResolveSurface()?.jumpLandSounds;
        PlayRandom(clips, ref lastLandIndex, 1f);
        ReportNoise(sprintNoiseRadius);
    }

    void PlayFootstep()
    {
        float volume = motor.IsCrouching ? footstepVolume * crouchVolumeReduction : footstepVolume;
        if (motor.InWater) volume = 1f;
        var clips = ResolveSurface()?.walkSounds;
        PlayRandom(clips, ref lastFootstepIndex, volume);
        ReportNoise(motor.State == MoveState.Run ? sprintNoiseRadius : (motor.IsCrouching ? 0f : walkNoiseRadius));
    }

    void ReportNoise(float radius)
    {
        if (radius > 0f) NoiseEvents.Report(transform.position, radius, player);
    }

    SurfaceSFX ResolveSurface()
    {
        if (motor.InWater) return waterEffect != null ? waterEffect : genericEffect;

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
            return genericEffect;
        }

        var objectLayer = hit.collider.GetComponent<ObjectLayer>();
        if (objectLayer != null && objectLayer.surfaceType != null) return objectLayer.surfaceType;

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
