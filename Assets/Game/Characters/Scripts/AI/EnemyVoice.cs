using UnityEngine;

// Idle mutters, the alert bark, pain grunts, death sounds and footsteps, all positioned
// in 3D so the player hears what is coming before it rounds the corner. Clips and ranges
// come from this character's CharacterData (Audio tab).
[RequireComponent(typeof(Character))]
public class EnemyVoice : MonoBehaviour
{
    [Tooltip("Speed below which no footsteps play.")]
    [SerializeField, Min(0)] float minStepSpeed = .35f;
    [Tooltip("Post \"You hear something...\" when this enemy spots the player from out of sight.")]
    [SerializeField] bool reportUnseenAlerts = true;
    [Tooltip("Idle mutters only play this close to the player, so a floor full of enemies isn't a constant drone.")]
    [SerializeField, Min(0)] float idleEarshot = 8f;
    [Tooltip("Footsteps only play this close to the player.")]
    [SerializeField, Min(0)] float stepEarshot = 12f;

    Character character;
    EnemyBrain brain;
    EnemyMotor motor;
    CreatureAudio entry;
    float nextIdle, nextStep, nextPain;
    Vector3 lastPosition;

    static float lastUnseenReport = -99f;

    // Enter Play Mode without a domain reload keeps statics.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => lastUnseenReport = -99f;

    void Awake()
    {
        character = GetComponent<Character>();
        brain = GetComponent<EnemyBrain>();
        motor = GetComponent<EnemyMotor>();
    }

    void OnEnable()
    {
        character.Damaged += OnDamaged;
        character.Died += OnDied;
        if (brain != null) brain.StateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        character.Damaged -= OnDamaged;
        character.Died -= OnDied;
        if (brain != null) brain.StateChanged -= OnStateChanged;
    }

    void Start()
    {
        entry = character.data != null ? character.data.audio : null;
        lastPosition = transform.position;
        ScheduleIdle(true);
    }

    void ScheduleIdle(bool first = false)
    {
        if (entry == null) return;
        float min = Mathf.Max(.5f, entry.idleInterval.x), max = Mathf.Max(min, entry.idleInterval.y);
        // Stagger the first mutter so a room of enemies never speaks in unison.
        nextIdle = Time.time + Random.Range(first ? 0f : min, max);
    }

    void Update()
    {
        if (entry == null || !character.IsAlive) return;
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;

        // EnemyMotor measures what actually moved (path, strafe or knockback); bodies without one are measured here.
        float speed = motor != null ? motor.Velocity.magnitude
            : (transform.position - lastPosition).magnitude / Mathf.Max(Time.deltaTime, .0001f);
        lastPosition = transform.position;
        bool alerted = brain != null && brain.IsAlerted;
        var listener = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        float heard = listener != null ? (listener.transform.position - transform.position).sqrMagnitude : float.MaxValue;

        if (speed > minStepSpeed && Time.time >= nextStep && heard <= stepEarshot * stepEarshot)
        {
            Play(entry.footsteps, entry.footstepVolume);
            nextStep = Time.time + (alerted ? entry.runStepInterval : entry.walkStepInterval);
        }

        if (!alerted && Time.time >= nextIdle)
        {
            if (heard <= idleEarshot * idleEarshot) Play(entry.idle, entry.voiceVolume * .8f);
            ScheduleIdle();
        }
    }

    void OnStateChanged(EnemyState previous, EnemyState next)
    {
        bool wasCombat = previous == EnemyState.Chase || previous == EnemyState.Attack;
        if (next != EnemyState.Chase || wasCombat || !character.IsAlive) return;
        Play(entry?.alert, entry != null ? entry.voiceVolume : 1f);
        if (reportUnseenAlerts && Time.unscaledTime - lastUnseenReport > 8f && !PlayerCanSee())
        {
            lastUnseenReport = Time.unscaledTime;
            MessageLog.Post("You hear something in the dark.", MessageKind.Warning);
        }
    }

    void OnDamaged(DamageInfo info)
    {
        if (entry == null || info.Blocked || info.Amount <= 0f || !character.IsAlive || Time.time < nextPain) return;
        nextPain = Time.time + entry.painCooldown;
        Play(entry.pain, entry.voiceVolume);
    }

    void OnDied()
    {
        if (entry != null) Play(entry.death, entry.voiceVolume);
    }

    void Play(AudioClip[] clips, float volume)
    {
        entry?.PlayAt(clips, transform.position + Vector3.up, volume);
    }

    bool PlayerCanSee()
    {
        var camera = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (camera == null) return true;
        Vector3 head = transform.position + Vector3.up * 1.2f;
        Vector3 delta = head - camera.transform.position;
        if (Vector3.Dot(camera.transform.forward, delta) <= 0f) return false;
        var player = PlayerManager.Instance.Active;
        return LineOfSight.Clear(camera.transform.position, head, ~0, transform, player != null ? player.ActivationRoot.transform : null);
    }
}
