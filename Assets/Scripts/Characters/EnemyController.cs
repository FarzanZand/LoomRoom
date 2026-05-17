using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Wander, Patrol, Chase, Attack, ReturnToPost }

public enum AggressionMode { Aggressive, AggressiveWhenHit, Passive }

public abstract class EnemyController : MovementBase
{
    [Header("Data")]
    [Tooltip("Assign an EnemyData asset to drive all config from one place. " +
             "If left empty the fallback fields below are used.")]
    [SerializeField] EnemyData enemyData;

    [Header("Fallback — used when no Enemy Data is assigned")]
    [SerializeField] EnemyState    localDefaultState    = EnemyState.Idle;
    [SerializeField] AggressionMode localAggressionMode = AggressionMode.Aggressive;

    [Header("Detection")]
    [SerializeField] LayerMask obstacleMask = ~0;

    // ShowIf helpers read EffectiveDefaultState so the wander/patrol scene-ref
    // fields in MovementBase show or hide correctly in the Inspector.
    EnemyState EffectiveDefaultState    => enemyData != null ? enemyData.defaultState    : localDefaultState;
    AggressionMode EffectiveAggression  => enemyData != null ? enemyData.aggressionMode  : localAggressionMode;

    protected override bool ShowWanderFields() => EffectiveDefaultState == EnemyState.Wander;
    protected override bool ShowPatrolFields() => EffectiveDefaultState == EnemyState.Patrol;

    // ── Runtime config (populated in Awake from EnemyData or hardcoded defaults) ──

    protected float attackRange;
    float detectionRadius, fieldOfView, eyeHeight, closeDetectionRadius;
    float loseSightGracePeriod, maxSearchTime;
    float passiveAngularSpeed, chaseAngularSpeed, attackFaceSpeed;
    float attackCooldown, attackAngleThreshold;
    bool  useWanderSpeed; float wanderSpeed;
    bool  useChaseSpeed;  float chaseSpeed;

    // ── Trigger overrides ─────────────────────────────────────────────

    protected override string HurtTrigger =>
        !string.IsNullOrEmpty(enemyData?.hurtTrigger) ? enemyData.hurtTrigger : base.HurtTrigger;

    protected override string DeathTrigger =>
        !string.IsNullOrEmpty(enemyData?.deathTrigger) ? enemyData.deathTrigger : base.DeathTrigger;

    string AttackTrigger =>
        !string.IsNullOrEmpty(enemyData?.attackTrigger) ? enemyData.attackTrigger : "Attack";

    // ── State ─────────────────────────────────────────────────────────

    protected EnemyState state;
    protected Transform  player;
    protected bool       playerVisible;

    protected Dictionary<EnemyState, Action> stateHandlers;

    float defaultSpeed;
    float attackTimer;
    float loseSightTimer;
    float giveUpTimer = -1f;
    bool  wasProvoked;
    Vector3 lastKnownPlayerPos;

    // ── Lifecycle ─────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        ApplyEnemyData();

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

    void ApplyEnemyData()
    {
        if (enemyData != null)
        {
            // Push stat profile into StatsComponent before Start() initialises health
            stats?.ApplyProfile(enemyData.statProfile);
            stats?.SetFaction(enemyData.faction);

            // Override MovementBase wander params
            wanderRadius      = enemyData.wanderRadius;
            minWanderDistance = enemyData.minWanderDistance;
            minIdleTime       = enemyData.minIdleTime;
            maxIdleTime       = enemyData.maxIdleTime;
            wanderZoneRadius  = enemyData.wanderZoneRadius;

            // Combat & detection config
            detectionRadius      = enemyData.detectionRadius;
            fieldOfView          = enemyData.fieldOfView;
            eyeHeight            = enemyData.eyeHeight;
            closeDetectionRadius = enemyData.closeDetectionRadius;
            loseSightGracePeriod = enemyData.loseSightGracePeriod;
            maxSearchTime        = enemyData.maxSearchTime;
            passiveAngularSpeed  = enemyData.passiveAngularSpeed;
            chaseAngularSpeed    = enemyData.chaseAngularSpeed;
            attackFaceSpeed      = enemyData.attackFaceSpeed;
            attackRange          = enemyData.attackRange;
            attackCooldown       = enemyData.attackCooldown;
            attackAngleThreshold = enemyData.attackAngleThreshold;
            useWanderSpeed       = enemyData.useWanderSpeed;
            wanderSpeed          = enemyData.wanderSpeed;
            useChaseSpeed        = enemyData.useChaseSpeed;
            chaseSpeed           = enemyData.chaseSpeed;
        }
        else
        {
            // Hardcoded sensible defaults when no asset is assigned
            detectionRadius      = 12f;
            fieldOfView          = 120f;
            eyeHeight            = 1.6f;
            closeDetectionRadius = 2f;
            loseSightGracePeriod = 3f;
            maxSearchTime        = 5f;
            passiveAngularSpeed  = 120f;
            chaseAngularSpeed    = 540f;
            attackFaceSpeed      = 720f;
            attackRange          = 1.8f;
            attackCooldown       = 1.5f;
            attackAngleThreshold = 45f;
        }
    }

    void Start()
    {
        InitStateHandlers();
        SetState(EffectiveDefaultState);
    }

    protected virtual void InitStateHandlers()
    {
        stateHandlers = new Dictionary<EnemyState, Action>
        {
            { EnemyState.Idle,         HandleIdle         },
            { EnemyState.Wander,       HandleWander       },
            { EnemyState.Patrol,       HandlePatrol       },
            { EnemyState.Chase,        HandleChase        },
            { EnemyState.Attack,       HandleAttack       },
            { EnemyState.ReturnToPost, HandleReturnToPost }
        };
    }

    void Update()
    {
        if (!IsAlive) return;

        if (player == null && PlayerManager.Instance != null)
            player = PlayerManager.Instance.tablePlayer.transform;

        UpdateGroundCheck();
        attackTimer  -= Time.deltaTime;
        playerVisible = CanSeePlayer();

        if (stateHandlers.TryGetValue(state, out var handler))
            handler();

        UpdateAnimatorParams();
        OnTick();
    }

    // Extension point for subclasses — runs every frame after the state machine
    protected virtual void OnTick() { }

    // ── Damage event ──────────────────────────────────────────────────

    void OnStatsDamageTaken(float amount, Vector3 knockbackDir)
    {
        if (EffectiveAggression == AggressionMode.AggressiveWhenHit
            && IsAlive
            && player != null
            && state != EnemyState.Chase
            && state != EnemyState.Attack)
        {
            EnterCombat();
        }
    }

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
        return EffectiveAggression switch
        {
            AggressionMode.Passive          => false,
            AggressionMode.Aggressive       => true,
            AggressionMode.AggressiveWhenHit => wasProvoked,
            _                               => false
        };
    }

    void EnterCombat()
    {
        if (player == null) return;
        wasProvoked         = true;
        lastKnownPlayerPos  = player.position;
        loseSightTimer      = loseSightGracePeriod;
        giveUpTimer         = -1f;
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
            loseSightTimer     = loseSightGracePeriod;
            giveUpTimer        = -1f;
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
        if (player == null) { SetState(EffectiveDefaultState); return; }

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
            TriggerAnimation(AttackTrigger);
        }
    }

    protected virtual void HandleReturnToPost()
    {
        if (CanEnterCombat() && playerVisible) { EnterCombat(); return; }

        Vector3 returnTarget = GetPostPosition();
        MoveTo(returnTarget);

        if (!agent.pathPending &&
            HorizontalDist(transform.position, returnTarget) < agent.stoppingDistance + 0.1f)
        {
            if (EffectiveAggression == AggressionMode.AggressiveWhenHit)
                wasProvoked = false;

            SetState(EffectiveDefaultState);
        }
    }

    Vector3 GetPostPosition()
    {
        EnemyState def = EffectiveDefaultState;
        if (def == EnemyState.Patrol && waypoints != null && waypoints.Length > 0)
            return waypoints[waypointIndex].position;
        if (def == EnemyState.Wander && wanderZoneCenter != null)
            return wanderZoneCenter.position;
        return spawnPosition;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    protected void SetState(EnemyState newState)
    {
        state      = newState;
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

            if (isPassive && useWanderSpeed)       agent.speed = wanderSpeed;
            else if (isCombat && useChaseSpeed)    agent.speed = chaseSpeed;
            else                                   agent.speed = defaultSpeed;

            agent.angularSpeed = isCombat ? chaseAngularSpeed : passiveAngularSpeed;
        }
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 origin   = transform.position + Vector3.up * eyeHeight;
        Vector3 toPlayer = player.position - origin;
        float   dist     = toPlayer.magnitude;

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
        if (player == null) { Debug.Log($"[{name}] OnAttackHit: player is null"); return; }

        var target = player.GetComponentInParent<IDamageable>();
        Debug.Log($"[{name}] OnAttackHit called — dist={HorizontalDist(transform.position, player.position):F2} range={attackRange * 1.2f:F2} target={target}");

        if (HorizontalDist(transform.position, player.position) > attackRange * 1.2f) return;
        if (target == null) return;

        float damage = stats != null && stats.HasStat(StatType.AttackDamage)
            ? stats.GetFinal(StatType.AttackDamage)
            : 0f;

        target.TakeDamage(damage, (player.position - transform.position).normalized);
    }

    // ── Gizmos ────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 pos    = Application.isPlaying ? spawnPosition : transform.position;
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.1f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Vector3 left  = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward * detectionRadius;
        Vector3 right = Quaternion.Euler(0,  fieldOfView * 0.5f, 0) * transform.forward * detectionRadius;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawLine(eyePos, eyePos + left);
        Gizmos.DrawLine(eyePos, eyePos + right);

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, closeDetectionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (EffectiveDefaultState == EnemyState.Wander && wanderZoneRadius > 0f)
        {
            Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position : pos;
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.1f);
            Gizmos.DrawSphere(center, wanderZoneRadius);
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(center, wanderZoneRadius);
        }

        if (EffectiveDefaultState == EnemyState.Patrol && waypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawSphere(waypoints[i].position, 0.2f);
                int next2 = i + 1;
                if (next2 < waypoints.Length && waypoints[next2] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[next2].position);
                else if (loopPatrol && waypoints[0] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
            }
        }
    }
}
