using UnityEngine;
using UnityEngine.AI;

// Idle mutters, the alert bark, pain grunts, death sounds and footsteps, all positioned
// in 3D so the player hears what is coming before it rounds the corner. Clips and ranges
// come from AudioManager's Creature Library, looked up by this character's CharacterData.
[RequireComponent(typeof(Character))]
public class EnemyVoice : MonoBehaviour
{
    [Tooltip("Speed below which no footsteps play.")]
    [SerializeField, Min(0)] float minStepSpeed = .35f;
    [Tooltip("Post \"You hear something...\" when this enemy spots the player from out of sight.")]
    [SerializeField] bool reportUnseenAlerts = true;

    Character character;
    EnemyBrain brain;
    NavMeshAgent agent;
    CreatureAudioEntry entry;
    float nextIdle, nextStep, nextPain;
    Vector3 lastPosition;

    static float lastUnseenReport = -99f;
    static readonly RaycastHit[] sightHits = new RaycastHit[16];

    void Awake()
    {
        character = GetComponent<Character>();
        brain = GetComponent<EnemyBrain>();
        agent = GetComponent<NavMeshAgent>();
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
        entry = AudioManager.HasInstance ? AudioManager.Instance.GetCreatureAudio(character.data) : null;
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

        float speed = agent != null && agent.enabled ? agent.velocity.magnitude
            : (transform.position - lastPosition).magnitude / Mathf.Max(Time.deltaTime, .0001f);
        lastPosition = transform.position;
        bool alerted = brain != null && brain.IsAlerted;

        if (speed > minStepSpeed && Time.time >= nextStep)
        {
            Play(entry.footsteps, entry.footstepVolume);
            nextStep = Time.time + (alerted ? entry.runStepInterval : entry.walkStepInterval);
        }

        if (!alerted && Time.time >= nextIdle)
        {
            Play(entry.idle, entry.voiceVolume * .8f);
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
        var clip = CreatureAudioEntry.Pick(clips);
        if (clip == null || entry == null || !AudioManager.HasInstance) return;
        AudioManager.Instance.PlaySFX(clip, transform.position + Vector3.up, volume, entry.pitch, entry.pitchVariance,
            entry.minDistance, entry.maxDistance);
    }

    bool PlayerCanSee()
    {
        var camera = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (camera == null) return true;
        Vector3 head = transform.position + Vector3.up * 1.2f;
        Vector3 delta = head - camera.transform.position;
        if (Vector3.Dot(camera.transform.forward, delta) <= 0f) return false;
        int count = Physics.RaycastNonAlloc(camera.transform.position, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var t = sightHits[i].transform;
            if (t.IsChildOf(transform)) continue;
            if (PlayerManager.Instance.Active != null && t.IsChildOf(PlayerManager.Instance.Active.ActivationRoot.transform)) continue;
            return false;
        }
        return true;
    }
}
