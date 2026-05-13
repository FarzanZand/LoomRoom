using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

public enum NPCState { Idle, Wander, Patrol }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class NPCController : CharacterBase
{
    [Header("Behaviour")]
    [SerializeField] NPCState defaultState = NPCState.Idle;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander")]
    [Tooltip("Maximum distance per wander step.")]
    [SerializeField] float wanderRadius = 8f;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander")]
    [Tooltip("Minimum distance per wander step.")]
    [SerializeField] float minWanderDistance = 2f;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander")]
    [Tooltip("Minimum time standing still between wanders.")]
    [SerializeField] float minIdleTime = 2f;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander")]
    [Tooltip("Maximum time standing still between wanders.")]
    [SerializeField] float maxIdleTime = 6f;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander/Zone")]
    [Tooltip("Centre of the allowed wander area. Defaults to spawn position if left empty.")]
    [SerializeField] Transform wanderZoneCenter;

    [ShowIf("defaultState", NPCState.Wander)]
    [BoxGroup("Wander/Zone")]
    [Tooltip("NPC will never pick a destination outside this radius from the zone centre. 0 = unlimited.")]
    [SerializeField] float wanderZoneRadius = 0f;

    [ShowIf("defaultState", NPCState.Patrol)]
    [BoxGroup("Patrol")]
    [SerializeField] Transform[] waypoints;

    [ShowIf("defaultState", NPCState.Patrol)]
    [BoxGroup("Patrol")]
    [SerializeField] bool loopPatrol = true;

    NPCState currentState;
    NPCState stateBeforePause;
    float wanderTimer;
    int waypointIndex;
    Vector3 spawnPosition;

    protected override void Awake()
    {
        base.Awake();
        spawnPosition = transform.position;
    }

    void Start()
    {
        SetState(defaultState);
    }

    void Update()
    {
        UpdateGroundCheck();
        UpdateState();
        UpdateAnimatorParams();
    }

    public void SetState(NPCState state)
    {
        currentState = state;
        wanderTimer = 0f;

        if (state == NPCState.Idle)
            StopMoving();
    }

    public override void Pause()
    {
        stateBeforePause = currentState;
        base.Pause();
        currentState = NPCState.Idle;
    }

    public override void Resume()
    {
        base.Resume();
        SetState(stateBeforePause);
    }

    void UpdateState()
    {
        switch (currentState)
        {
            case NPCState.Wander:
                HandleWander();
                break;

            case NPCState.Patrol:
                HandlePatrol();
                break;
        }
    }

    void HandleWander()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        Vector3 zoneCenter = wanderZoneCenter != null ? wanderZoneCenter.position : spawnPosition;

        Vector3 randomDir = Random.insideUnitSphere;
        randomDir.y = 0f;
        randomDir.Normalize();

        float distance = Random.Range(minWanderDistance, wanderRadius);
        Vector3 targetPoint = transform.position + randomDir * distance;

        if (wanderZoneRadius > 0f)
        {
            Vector3 toTarget = targetPoint - zoneCenter;
            toTarget.y = 0f;
            if (toTarget.magnitude > wanderZoneRadius)
                targetPoint = zoneCenter + toTarget.normalized * wanderZoneRadius;
        }

        if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            MoveTo(hit.position);

        wanderTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    void HandlePatrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance < agent.stoppingDistance)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Length)
                waypointIndex = loopPatrol ? 0 : waypoints.Length - 1;

            MoveTo(waypoints[waypointIndex].position);
        }
    }

    protected override void Die()
    {
        StopMoving();
        agent.isStopped = true;
        enabled = false;
    }

    void OnDrawGizmosSelected()
    {
        if (defaultState != NPCState.Wander || wanderZoneRadius <= 0f) return;

        Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position :
                         (Application.isPlaying ? spawnPosition : transform.position);

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, wanderZoneRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, wanderZoneRadius);
    }
}
