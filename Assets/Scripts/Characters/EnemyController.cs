using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Wander, Patrol, Chase, Attack, ReturnToPost }

public enum AggressionMode
{
    Aggressive,
    AggressiveWhenHit,
    Passive
}

public abstract class EnemyController : MovementBase
{
    [Header("Default Behaviour")]
    [SerializeField] EnemyState defaultState = EnemyState.Idle;
    [SerializeField] AggressionMode aggressionMode = AggressionMode.Aggressive;

    protected override bool ShowWanderFields() => defaultState == EnemyState.Wander;
    protected override bool ShowPatrolFields() => defaultState == EnemyState.Patrol;

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
    [Tooltip("Always detected within this horizontal distance, ignoring FOV and LOS. Should be >= Attack Range.")]
    [SerializeField] float closeDetectionRadius = 2f;
    [SerializeField] LayerMask obstacleMask;

    [Header("Chase")]
    [SerializeField] float loseSightGracePeriod = 3f;
    [Tooltip("After the grace period, how long to search the last known position before giving up.")]
    [SerializeField] float maxSearchTime = 5f;

    [Header("Rotation")]
    [SerializeField] float passiveAngularSpeed = 120f;
    [SerializeField] float chaseAngularSpeed = 540f;
    [SerializeField] float attackFaceSpeed = 720f;

    [Header("Attack")]
    [SerializeField] protected float attackRange = 1.8f;
    [SerializeField] protected float attackCooldown = 1.5f;
    [SerializeField] protected float attackDamage = 10f;
    [SerializeField] string attackTrigger = "Attack";
    [SerializeField, Range(0f, 180f)] float attackAngleThreshold = 45f;

    protected EnemyState state;
    protected Transform player;
    protected bool playerVisible;

    protected Dictionary<EnemyState, Action> stateHandlers;

    float defaultSpeed;
    float attackTimer;
    float loseSightTimer;
    float giveUpTimer = -1f;
    bool wasProvoked;
    Vector3 lastKnownPlayerPos;

    protected override void Awake()
    {
        base.Awake();
        if (agent != null) defaultSpeed = agent.speed;

        if (stats != null)
            stats.OnDamageTaken += OnStatsDamageTaken;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (stats != null)
            stats.OnDamageTaken -= OnStatsDamageTaken;
    }

    void OnStatsDamageTaken(float amount, Vector3 knockbackDir)
    {
        if (aggressionMode == AggressionMode.AggressiveWhenHit
            && IsAlive
            && player != null
            && state != EnemyState.Chase
            && state != EnemyState.Attack)
        {
            EnterCombat();
        }
    }

    void Start()
    {
        InitStateHandlers();
        SetState(defaultState);
    }

    // Register state handlers here. Subclasses override to add or replace entries.
    protected virtual void InitStateHandlers()
    {
        stateHandlers = new Dictionary<EnemyState, Action>
        {
            { EnemyState.Idle,         HandleIdle },
            { EnemyState.Wander,       HandleWander },
            { EnemyState.Patrol,       HandlePatrol },
            { EnemyState.Chase,        HandleChase },
            { EnemyState.Attack,       HandleAttack },
            { EnemyState.ReturnToPost, HandleReturnToPost }
        };
    }

    void Update()
    {
        if (!IsAlive) return;

        if (player == null && PlayerManager.Instance != null)
            player = PlayerManager.Instance.tablePlayer.transform;

        UpdateGroundCheck();
        attackTimer -= Time.deltaTime;

        // Cache once per frame — avoids redundant raycasts across multiple state handlers
        playerVisible = CanSeePlayer();

        if (stateHandlers.TryGetValue(state, out var handler))
            handler();

        UpdateAnimatorParams();
        OnTick();
    }

    // Extension point for subclasses — called every frame after the state machine
    protected virtual void OnTick() { }

    // ── Passive states ────────────────────────────────────────────────

    protected virtual void HandleIdle()
    {
        if (CanEnterCombat() && playerVisible) EnterCombat();
    }

    protected virtual void HandleWander()
    {
        if (CanEnterCombat() && playerVisible) { EnterCombat(); return; }
        HandleWanderMovement();
    }

    protected virtual void HandlePatrol()
    {
        if (CanEnterCombat() && playerVisible) { EnterCombat(); return; }
        HandlePatrolMovement();
    }

    // ── Combat states ─────────────────────────────────────────────────

    bool CanEnterCombat()
    {
        if (aggressionMode == AggressionMode.Passive) return false;
        if (aggressionMode == AggressionMode.Aggressive) return true;
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

        if (HorizontalDist(transform.position, player.position) <= attackRange)
        {
            SetState(EnemyState.Attack);
            return;
        }

        if (playerVisible)
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
        if (CanEnterCombat() && playerVisible) { EnterCombat(); return; }

        Vector3 returnTarget = GetPostPosition();
        MoveTo(returnTarget);

        if (!agent.pathPending && HorizontalDist(transform.position, returnTarget) < agent.stoppingDistance + 0.1f)
        {
            // AggressiveWhenHit enemies forget the player once they've safely returned
            if (aggressionMode == AggressionMode.AggressiveWhenHit)
                wasProvoked = false;

            SetState(defaultState);
        }
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

        if (newState == EnemyState.Patrol)
            StartPatrol();

        if (agent != null)
        {
            bool isPassive = newState == EnemyState.Idle
                          || newState == EnemyState.Wander
                          || newState == EnemyState.Patrol
                          || newState == EnemyState.ReturnToPost;

            bool isCombat = newState == EnemyState.Chase
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

    bool CanSeePlayer()
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
        return cur.IsTag("Attack") || (animator.IsInTransition(0) && next.IsTag("Attack"));
    }

    protected static float HorizontalDist(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    public virtual void OnAttackHit()
    {
        if (player == null) return;
        if (HorizontalDist(transform.position, player.position) > attackRange * 1.2f) return;

        var target = player.GetComponentInParent<IDamageable>();
        if (target == null) return;

        float damage = stats != null && stats.HasStat(StatType.AttackDamage)
            ? stats.GetFinal(StatType.AttackDamage)
            : attackDamage;

        Vector3 knockDir = (player.position - transform.position).normalized;
        target.TakeDamage(damage, knockDir);
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
