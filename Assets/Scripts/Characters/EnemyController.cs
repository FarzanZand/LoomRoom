using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Wander, Patrol, Chase, Attack, ReturnToPost }

public enum AggressionMode
{
    Aggressive,        // detects and chases the player on sight
    AggressiveWhenHit, // ignores the player until it takes damage, then fights back
    Passive            // never enters combat regardless of what happens
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public abstract class EnemyController : CharacterBase
{
    [Header("Default Behaviour")]
    [SerializeField] EnemyState defaultState = EnemyState.Idle;
    [SerializeField] AggressionMode aggressionMode = AggressionMode.Aggressive;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander")]
    [SerializeField] float wanderRadius = 8f;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander")]
    [SerializeField] float minWanderDistance = 2f;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander")]
    [SerializeField] float minIdleTime = 2f;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander")]
    [SerializeField] float maxIdleTime = 6f;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander/Zone")]
    [SerializeField] Transform wanderZoneCenter;

    [ShowIf("defaultState", EnemyState.Wander)]
    [BoxGroup("Wander/Zone")]
    [SerializeField] float wanderZoneRadius = 0f;

    [ShowIf("defaultState", EnemyState.Patrol)]
    [BoxGroup("Patrol")]
    [SerializeField] Transform[] waypoints;

    [ShowIf("defaultState", EnemyState.Patrol)]
    [BoxGroup("Patrol")]
    [SerializeField] bool loopPatrol = true;

    [Header("Movement")]
    [SerializeField] bool useWanderSpeed;
    [ShowIf("useWanderSpeed")]
    [SerializeField] float wanderSpeed = 2f;
    [SerializeField] bool useChaseSpeed;
    [ShowIf("useChaseSpeed")]
    [SerializeField] float chaseSpeed = 5f;

    [Header("Detection")]
    [SerializeField] float detectionRadius = 12f;
    [SerializeField, Range(0f, 360f)] float fieldOfView = 120f;
    [SerializeField] float eyeHeight = 1.6f;
    [Tooltip("Always detected within this horizontal distance, ignoring FOV and line of sight. Should be >= Attack Range.")]
    [SerializeField] float closeDetectionRadius = 2f;
    [SerializeField] LayerMask obstacleMask;

    [Header("Chase")]
    [SerializeField] float loseSightGracePeriod = 3f;
    [Tooltip("After the grace period, how long to search the last known position before giving up.")]
    [SerializeField] float maxSearchTime = 5f;

    [Header("Rotation")]
    [Tooltip("NavMeshAgent angular speed while wandering/patrolling.")]
    [SerializeField] float passiveAngularSpeed = 120f;
    [Tooltip("NavMeshAgent angular speed while chasing.")]
    [SerializeField] float chaseAngularSpeed = 540f;
    [Tooltip("Manual rotation speed in attack stance between swings.")]
    [SerializeField] float attackFaceSpeed = 720f;

    [Header("Attack")]
    [SerializeField] protected float attackRange = 1.8f;
    [SerializeField] protected float attackCooldown = 1.5f;
    [SerializeField] protected float attackDamage = 10f;
    [SerializeField] string attackTrigger = "Attack";
    [Tooltip("Max angle from forward within which the enemy will swing. Outside this it rotates first.")]
    [SerializeField, Range(0f, 180f)] float attackAngleThreshold = 45f;

    protected EnemyState state;
    protected Transform player;

    float defaultSpeed;
    float attackTimer;
    float wanderTimer;
    float loseSightTimer;
    float giveUpTimer = -1f;
    bool wasProvoked; // true once the enemy has entered combat at least once
    int waypointIndex;
    Vector3 spawnPosition;
    Vector3 lastKnownPlayerPos;

    protected override void Awake()
    {
        base.Awake();
        spawnPosition = transform.position;
        if (agent != null) defaultSpeed = agent.speed;
    }

    void Start()
    {
        SetState(defaultState);
    }

    protected virtual void Update()
    {
        if (!IsAlive) return;

        if (player == null && PlayerManager.Instance != null)
            player = PlayerManager.Instance.tablePlayer.transform;

        UpdateGroundCheck();
        attackTimer -= Time.deltaTime;

        switch (state)
        {
            case EnemyState.Idle:         HandleIdle();         break;
            case EnemyState.Wander:       HandleWander();       break;
            case EnemyState.Patrol:       HandlePatrol();       break;
            case EnemyState.Chase:        HandleChase();        break;
            case EnemyState.Attack:       HandleAttack();       break;
            case EnemyState.ReturnToPost: HandleReturnToPost(); break;
        }

        UpdateAnimatorParams();
    }

    // ── Passive states ────────────────────────────────────────────────

    protected virtual void HandleIdle()
    {
        if (CanEnterCombat() && CanSeePlayer()) EnterCombat();
    }

    protected virtual void HandleWander()
    {
        if (CanEnterCombat() && CanSeePlayer()) { EnterCombat(); return; }

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

    protected virtual void HandlePatrol()
    {
        if (CanEnterCombat() && CanSeePlayer()) { EnterCombat(); return; }

        if (waypoints == null || waypoints.Length == 0) return;

        // Use a minimum arrival threshold so patrol advances even when stoppingDistance is 0
        float arriveThreshold = Mathf.Max(agent.stoppingDistance, 0.5f);
        if (!agent.pathPending && agent.remainingDistance < arriveThreshold)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Length)
                waypointIndex = loopPatrol ? 0 : waypoints.Length - 1;

            MoveTo(waypoints[waypointIndex].position);
        }
    }

    // ── Combat states ─────────────────────────────────────────────────

    public override void TakeDamage(float amount, Vector3 knockbackDirection)
    {
        base.TakeDamage(amount, knockbackDirection);

        if (aggressionMode == AggressionMode.AggressiveWhenHit
            && IsAlive
            && player != null
            && state != EnemyState.Chase
            && state != EnemyState.Attack)
        {
            EnterCombat();
        }
    }

    // Returns true if this enemy should react to seeing the player
    bool CanEnterCombat()
    {
        if (aggressionMode == AggressionMode.Passive) return false;
        if (aggressionMode == AggressionMode.Aggressive) return true;
        // AggressiveWhenHit: only engage on sight after already being provoked
        return wasProvoked;
    }

    void EnterCombat()
    {
        if (player == null) return;
        wasProvoked = true;
        lastKnownPlayerPos = player.position;
        loseSightTimer = loseSightGracePeriod;
        giveUpTimer = -1f;
        SetState(EnemyState.Chase);
    }

    protected virtual void HandleChase()
    {
        if (player == null) return;

        // Attack range is checked before LOS — being within sword reach doesn't require sight
        if (HorizontalDist(transform.position, player.position) <= attackRange)
        {
            SetState(EnemyState.Attack);
            return;
        }

        if (CanSeePlayer())
        {
            loseSightTimer = loseSightGracePeriod;
            giveUpTimer = -1f;
            lastKnownPlayerPos = player.position;
            MoveTo(player.position);
        }
        else
        {
            loseSightTimer -= Time.deltaTime;
            MoveTo(lastKnownPlayerPos);

            if (loseSightTimer <= 0f)
            {
                if (giveUpTimer < 0f) giveUpTimer = maxSearchTime;
                giveUpTimer -= Time.deltaTime;

                bool reachedSpot = !agent.pathPending &&
                    HorizontalDist(transform.position, lastKnownPlayerPos) < agent.stoppingDistance + 0.5f;

                if (reachedSpot || giveUpTimer <= 0f)
                    SetState(EnemyState.ReturnToPost);
            }
        }
    }

    protected virtual void HandleAttack()
    {
        if (player == null) { SetState(defaultState); return; }

        if (HorizontalDist(transform.position, player.position) > attackRange)
        {
            SetState(EnemyState.Chase);
            return;
        }

        if (!IsPlayingAttack())
            FaceTo(player, attackFaceSpeed);

        if (attackTimer <= 0f && IsFacingPlayer())
        {
            Vector3 snapDir = player.position - transform.position;
            snapDir.y = 0f;
            if (snapDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(snapDir);

            attackTimer = attackCooldown;
            TriggerAnimation(attackTrigger);
        }
    }

    protected virtual void HandleReturnToPost()
    {
        // Re-engage if provoked enemy spots the player again, or always if Aggressive
        if (CanEnterCombat() && CanSeePlayer()) { EnterCombat(); return; }

        Vector3 returnTarget = GetPostPosition();
        MoveTo(returnTarget);

        if (!agent.pathPending && HorizontalDist(transform.position, returnTarget) < agent.stoppingDistance + 0.1f)
            SetState(defaultState);
    }

    Vector3 GetPostPosition()
    {
        if (defaultState == EnemyState.Patrol && waypoints != null && waypoints.Length > 0)
            return waypoints[waypointIndex].position;
        if (defaultState == EnemyState.Wander && wanderZoneCenter != null)
            return wanderZoneCenter.position;
        return spawnPosition;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    protected void SetState(EnemyState newState)
    {
        state = newState;
        wanderTimer = 0f;

        // Reset sight timer whenever re-entering chase so a fresh grace period always applies
        if (newState == EnemyState.Chase)
            loseSightTimer = loseSightGracePeriod;

        if (newState == EnemyState.Idle || newState == EnemyState.Attack)
        {
            if (agent != null)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.velocity  = Vector3.zero;
            }
        }
        else
        {
            if (agent != null) agent.isStopped = false;
        }

        if (newState == EnemyState.Patrol && waypoints != null && waypoints.Length > 0)
            MoveTo(waypoints[waypointIndex].position);

        if (agent != null)
        {
            bool isPassive = newState == EnemyState.Idle
                          || newState == EnemyState.Wander
                          || newState == EnemyState.Patrol
                          || newState == EnemyState.ReturnToPost;

            bool isCombat  = newState == EnemyState.Chase
                          || newState == EnemyState.Attack;

            if (isPassive && useWanderSpeed)
                agent.speed = wanderSpeed;
            else if (isCombat && useChaseSpeed)
                agent.speed = chaseSpeed;
            else
                agent.speed = defaultSpeed;

            agent.angularSpeed = isCombat ? chaseAngularSpeed : passiveAngularSpeed;
        }
    }

    protected bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 toPlayer = player.position - origin;
        float dist = toPlayer.magnitude;

        if (dist > detectionRadius) return false;

        if (HorizontalDist(transform.position, player.position) <= closeDetectionRadius) return true;

        if (Vector3.Angle(transform.forward, toPlayer) > fieldOfView * 0.5f) return false;

        return !Physics.Raycast(origin, toPlayer.normalized, dist, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    bool IsFacingPlayer()
    {
        if (player == null) return false;
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        return Vector3.Angle(transform.forward, dir) <= attackAngleThreshold;
    }

    bool IsPlayingAttack()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        var cur  = animator.GetCurrentAnimatorStateInfo(0);
        var next = animator.GetNextAnimatorStateInfo(0);
        return cur.IsTag("Attack")
            || (animator.IsInTransition(0) && next.IsTag("Attack"));
    }

    static float HorizontalDist(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    public virtual void OnAttackHit()
    {
        if (player == null) return;
        if (HorizontalDist(transform.position, player.position) > attackRange * 1.2f) return;

        var target = player.GetComponentInChildren<IDamageable>();
        if (target == null) return;

        Vector3 knockDir = (player.position - transform.position).normalized;
        Debug.Log($"[{name}] hit player for {attackDamage} damage");
        target.TakeDamage(attackDamage, knockDir);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 pos = Application.isPlaying ? spawnPosition : transform.position;
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.1f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Vector3 leftBound  = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward * detectionRadius;
        Vector3 rightBound = Quaternion.Euler(0,  fieldOfView * 0.5f, 0) * transform.forward * detectionRadius;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawLine(eyePos, eyePos + leftBound);
        Gizmos.DrawLine(eyePos, eyePos + rightBound);

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, closeDetectionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (defaultState == EnemyState.Wander && wanderZoneRadius > 0f)
        {
            Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position : pos;
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.1f);
            Gizmos.DrawSphere(center, wanderZoneRadius);
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(center, wanderZoneRadius);
        }

        if (defaultState == EnemyState.Patrol && waypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawSphere(waypoints[i].position, 0.2f);
                if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
                else if (loopPatrol && waypoints[0] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
            }
        }
    }
}
