using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// Shared base for any NavMesh-driven character that can idle, wander, or patrol.
// Both NPCController and EnemyController extend this.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public abstract class MovementBase : CharacterBase
{
    [ShowIf("ShowWanderFields")] [BoxGroup("Wander")]
    [SerializeField] protected float wanderRadius = 8f;

    [ShowIf("ShowWanderFields")] [BoxGroup("Wander")]
    [SerializeField] protected float minWanderDistance = 2f;

    [ShowIf("ShowWanderFields")] [BoxGroup("Wander")]
    [SerializeField] protected float minIdleTime = 2f;

    [ShowIf("ShowWanderFields")] [BoxGroup("Wander")]
    [SerializeField] protected float maxIdleTime = 6f;

    [ShowIf("ShowWanderFields")] [BoxGroup("Wander/Zone")]
    [Tooltip("Centre of the wander area. Defaults to spawn position when left empty.")]
    [SerializeField] protected Transform wanderZoneCenter;

    [ShowIf("ShowWanderFields")] [BoxGroup("Wander/Zone")]
    [SerializeField] protected float wanderZoneRadius = 0f;

    [ShowIf("ShowPatrolFields")] [BoxGroup("Patrol")]
    [SerializeField] protected Transform[] waypoints;

    [ShowIf("ShowPatrolFields")] [BoxGroup("Patrol")]
    [SerializeField] protected bool loopPatrol = true;

    protected float wanderTimer;
    protected int waypointIndex;
    protected Vector3 spawnPosition;

    // Subclasses override these to show/hide wander and patrol fields in the Inspector
    protected virtual bool ShowWanderFields() => false;
    protected virtual bool ShowPatrolFields() => false;

    protected override void Awake()
    {
        base.Awake();
        spawnPosition = transform.position;
    }

    Coroutine rotateToPlayerRoutine;

    // Smoothly turns the character (on the Y axis) until it faces the active player.
    [Button]
    public void RotateTowardsPlayer(float rotateSpeed = 360f)
    {
        var playerObject = PlayerManager.Instance != null ? PlayerManager.Instance.ActivePlayerObject : null;
        if (playerObject == null) return;

        if (rotateToPlayerRoutine != null)
            StopCoroutine(rotateToPlayerRoutine);
        rotateToPlayerRoutine = StartCoroutine(RotateTowardsPlayerRoutine(playerObject.transform, rotateSpeed));
    }

    // Instantly faces the active player (on the Y axis) — e.g. on spawn.
    [Button]
    public void SnapRotationTowardsPlayer()
    {
        var playerObject = PlayerManager.Instance != null ? PlayerManager.Instance.ActivePlayerObject : null;
        if (playerObject == null) return;

        if (rotateToPlayerRoutine != null)
        {
            StopCoroutine(rotateToPlayerRoutine);
            rotateToPlayerRoutine = null;
        }

        Vector3 dir = playerObject.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    IEnumerator RotateTowardsPlayerRoutine(Transform player, float rotateSpeed)
    {
        while (true)
        {
            Vector3 dir = player.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) break;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotateSpeed * Time.deltaTime);

            if (Quaternion.Angle(transform.rotation, targetRot) < 0.5f) break;
            yield return null;
        }
        rotateToPlayerRoutine = null;
    }

    // Call from the subclass's wander state handler
    protected void HandleWanderMovement()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        Vector3 zoneCenter = wanderZoneCenter != null ? wanderZoneCenter.position : spawnPosition;

        Vector3 randomDir = Random.insideUnitSphere;
        randomDir.y = 0f;
        randomDir.Normalize();

        float distance = Random.Range(minWanderDistance, wanderRadius);
        Vector3 targetPoint = zoneCenter + randomDir * distance;

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

    // Call from the subclass's patrol state handler
    protected void HandlePatrolMovement()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        float arriveThreshold = Mathf.Max(agent.stoppingDistance, 0.5f);
        if (!agent.pathPending && agent.remainingDistance < arriveThreshold)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Length)
                waypointIndex = loopPatrol ? 0 : waypoints.Length - 1;

            MoveTo(waypoints[waypointIndex].position);
        }
    }

    // Sends the agent to the current waypoint — call when entering patrol state
    protected void StartPatrol()
    {
        if (waypoints != null && waypoints.Length > 0)
            MoveTo(waypoints[waypointIndex].position);
    }
}
