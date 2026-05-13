using UnityEngine;
using UnityEngine.AI;

public enum NPCState { Idle, Wander, Patrol }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class NPCController : CharacterBase
{
    [Header("Behaviour")]
    [SerializeField] NPCState defaultState = NPCState.Idle;

    [Header("Wander")]
    [SerializeField] float wanderRadius = 8f;
    [SerializeField] float wanderInterval = 4f;

    [Header("Patrol")]
    [SerializeField] Transform[] waypoints;
    [SerializeField] bool loopPatrol = true;

    NPCState currentState;
    NPCState stateBeforePause;
    float wanderTimer;
    int waypointIndex;

    protected override void Awake()
    {
        base.Awake();
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

        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            MoveTo(hit.position);

        wanderTimer = wanderInterval;
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
}
