using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

public enum NPCState { Idle = 0, Wander = 1, Patrol = 2, Talking = 3 }

// Friendly characters: idle, wander, patrol, and turn to face whoever talks to them.
// Shares EnemyMotor with enemies; has no perception or attacks.
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(EnemyMotor))]
public class NpcBrain : MonoBehaviour, IInteractable
{
    [Header("Behaviour")]
    [SerializeField] NPCState defaultState = NPCState.Idle;

    [Header("Interaction")]
    [SerializeField] bool isInteractable = false;
    [ShowIf("isInteractable")]
    [Tooltip("Prompt shown to the player. {name} is replaced with the character name.")]
    [SerializeField] string prompt = "Talk to {name}";
    [ShowIf("isInteractable")]
    [Tooltip("Speed in degrees per second the NPC turns to face the player.")]
    [SerializeField] float faceSpeed = 180f;

    [Header("Wander")]
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float wanderRadius = 8f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float minWanderDistance = 2f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float minIdleTime = 2f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float maxIdleTime = 6f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float wanderZoneRadius = 0f;

    [Header("Patrol")]
    [ShowIf("@defaultState == NPCState.Patrol")]
    [SerializeField] Transform[] waypoints;
    [ShowIf("@defaultState == NPCState.Patrol")]
    [SerializeField] bool loopPatrol = true;

    [ShowInInspector, ReadOnly] public NPCState State { get; private set; }
    public Character  Character { get; private set; }
    public EnemyMotor Motor     { get; private set; }

    NPCState stateBeforePause;
    InteractableTrigger interactTrigger;
    Transform faceTarget;
    Vector3   spawnPosition;
    float     wanderTimer;
    int       waypointIndex;
    Coroutine rotateRoutine;

    public string Prompt => prompt.Replace("{name}", Character != null ? Character.DisplayName : name);
    public bool CanInteract(Character who) => isInteractable && Character.IsAlive;

    void Awake()
    {
        Character = GetComponent<Character>();
        Motor     = GetComponent<EnemyMotor>();
        interactTrigger = GetComponentInChildren<InteractableTrigger>(true);
        spawnPosition = transform.position;
        SetInteractable(isInteractable);
    }

    public void SetInteractable(bool value)
    {
        isInteractable = value;
        if (interactTrigger != null) interactTrigger.gameObject.SetActive(value);
    }

    void Start() => SetState(defaultState);

    void Update()
    {
        if (!Character.IsAlive) return;
        switch (State)
        {
            case NPCState.Wander: HandleWander(); break;
            case NPCState.Patrol: HandlePatrol(); break;
        }
        UpdateFacing();
        UpdateAnimator();
    }

    public void SetState(NPCState state)
    {
        State = state;
        wanderTimer = 0f;
        if (state == NPCState.Idle || state == NPCState.Talking) Motor.Stop();
        else Motor.Resume();
        if (state == NPCState.Patrol && waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            Motor.MoveTo(waypoints[waypointIndex].position);
    }

    // ── Interaction ───────────────────────────────────────────────────

    public void Interact(Character who)
    {
        if (!isInteractable) return;
        Pause();
        faceTarget = who != null ? who.transform : null;
    }

    public void EndInteraction() => Resume();

    public void Pause()
    {
        if (State == NPCState.Talking) return;
        stateBeforePause = State;
        SetState(NPCState.Talking);
    }

    public void Resume()
    {
        faceTarget = null;
        SetState(stateBeforePause);
    }

    void UpdateFacing()
    {
        if (faceTarget == null) return;
        Motor.Face(faceTarget.position, faceSpeed);
        if (Motor.IsFacing(faceTarget.position, 0.5f)) faceTarget = null;
    }

    // Smoothly turns toward the active player (cutscenes).
    [Button]
    public void RotateTowardsPlayer(float rotateSpeed = 360f)
    {
        if (!PlayerManager.HasInstance || PlayerManager.Instance.Active == null) return;
        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
        rotateRoutine = StartCoroutine(RotateRoutine(PlayerManager.Instance.Active.transform, rotateSpeed));
    }

    [Button]
    public void SnapRotationTowardsPlayer()
    {
        if (!PlayerManager.HasInstance || PlayerManager.Instance.Active == null) return;
        SnapRotationTowards(PlayerManager.Instance.Active.transform.position);
    }

    public void SnapRotationTowards(Vector3 position)
    {
        if (rotateRoutine != null) { StopCoroutine(rotateRoutine); rotateRoutine = null; }
        faceTarget = null;
        Motor.SnapFace(position);
    }

    IEnumerator RotateRoutine(Transform target, float speed)
    {
        while (!Motor.IsFacing(target.position, 0.5f))
        {
            Motor.Face(target.position, speed);
            yield return null;
        }
        rotateRoutine = null;
    }

    // ── Wander / patrol ───────────────────────────────────────────────

    void HandleWander()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        Vector3 center = spawnPosition;
        Vector3 dir = Random.insideUnitSphere; dir.y = 0f; dir.Normalize();
        Vector3 target = center + dir * Random.Range(minWanderDistance, wanderRadius);
        if (wanderZoneRadius > 0f)
        {
            Vector3 offset = target - center; offset.y = 0f;
            if (offset.magnitude > wanderZoneRadius) target = center + offset.normalized * wanderZoneRadius;
        }
        var filter = new NavMeshQueryFilter { agentTypeID = Motor.Agent.agentTypeID, areaMask = Motor.Agent.areaMask };
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, wanderRadius, filter))
            Motor.MoveTo(hit.position);
        wanderTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    void HandlePatrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (!Motor.ReachedDestination(0.4f)) return;
        waypointIndex++;
        if (waypointIndex >= waypoints.Length) waypointIndex = loopPatrol ? 0 : waypoints.Length - 1;
        if (waypoints[waypointIndex] != null) Motor.MoveTo(waypoints[waypointIndex].position);
    }

    void UpdateAnimator()
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return;
        // Idle/talking NPCs must not run because of placement or vertical settling.
        bool locomoting = (State == NPCState.Wander || State == NPCState.Patrol) && Motor.HasPath;
        Vector3 horizontalVelocity = Motor.Velocity;
        horizontalVelocity.y = 0f;
        if (locomoting)
            anim.SetFloat("Speed", horizontalVelocity.magnitude, 0.1f, Time.deltaTime);
        else
            anim.SetFloat("Speed", 0f);
        anim.SetFloat("MotionSpeed", locomoting ? 1f : 0f, 0.1f, Time.deltaTime);
        anim.SetBool("Grounded", Motor.IsGrounded);
        anim.SetBool("FreeFall", !Motor.IsGrounded && Motor.Velocity.y < -1f);
    }

    void OnDrawGizmosSelected()
    {
        if (defaultState != NPCState.Wander || wanderZoneRadius <= 0f) return;
        Vector3 center = Application.isPlaying ? spawnPosition : transform.position;
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, wanderZoneRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, wanderZoneRadius);
    }
}
