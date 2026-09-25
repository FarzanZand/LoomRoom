using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// The enemy state machine. Perception supplies what it knows, EnemyMotor moves it,
// CharacterData supplies the attacks and the EnemyBehaviourSettings supply tuning.
//
// Combat feel, in the order an action game does it:
//   Chase   — run at the target, turning toward it as soon as it is close.
//   Spacing — inside attack range while the attack recharges: hold a preferred
//             distance and circle sideways, always facing the target. Never stand and pivot.
//   Attack  — plant, keep tracking the target during the windup, then commit. The
//             player's hits never interrupt a swing (stuns are a separate, future thing).
//   Recover — a short pause after the swing, still turning toward the target.
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(EnemyMotor))]
[RequireComponent(typeof(Perception))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("Ignore the CharacterData's behaviour profile and use the values below for this instance only. " +
             "Turning it on copies the profile's values as a starting point.")]
    [OnValueChanged("OnOverrideToggled")]
    [SerializeField] bool overrideBehaviour = false;
    [ShowIf("overrideBehaviour"), HideLabel, InlineProperty]
    [SerializeField] EnemyBehaviourSettings localBehaviour = new();

    [Header("Patrol")]
    [SerializeField] Transform[] waypoints;
    [SerializeField] bool loopPatrol = true;

    [Header("Attack Relay")]
    [Tooltip("Relay on the animator object. Leave empty to search children.")]
    [SerializeField] WeaponAnimationRelay attackRelay;

    [ShowInInspector, ReadOnly] public EnemyState State { get; private set; }
    public Character  Character  { get; private set; }
    public EnemyMotor Motor      { get; private set; }
    public Perception Perception { get; private set; }

    public EnemyBehaviourSettings Profile =>
        overrideBehaviour ? localBehaviour
        : Character != null && Character.data != null && Character.data.behaviour != null ? Character.data.behaviour.settings
        : FallbackProfile;

    static EnemyBehaviourSettings fallback;
    static EnemyBehaviourSettings FallbackProfile => fallback ??= new EnemyBehaviourSettings();

    // Editor only: when the override is switched on, start from the shared profile's values.
    void OnOverrideToggled()
    {
        if (!overrideBehaviour) return;
        var data = GetComponent<Character>()?.data;
        if (data != null && data.behaviour != null) localBehaviour.CopyFrom(data.behaviour.settings);
    }

    Dictionary<EnemyState, Action> handlers;
    Vector3 spawnPosition;
    int     waypointIndex;
    float   wanderTimer;
    float   loseSightTimer;
    float   giveUpTimer = -1f;
    float   investigateTimer;
    bool    wasProvoked;
    Vector3 investigatePoint;

    // Spacing
    int   circleDir = 1;
    float circleSwitchAt;
    float circleStuckSince = -1f;

    // Attacks
    EnemyAttackRunner attackRunner;
    float recoveryUntil;
    CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;
    public bool CanOpenHitbox => attackRunner != null && attackRunner.CanOpenHitbox;
    public bool IsSwinging    => attackRunner != null && attackRunner.IsSwinging;
    internal WeaponAnimationRelay AttackRelay => attackRelay;

    // Stand still (a flinch, a stagger, the pause after a swing); only ever extends.
    internal void BeginRecovery(float seconds) => recoveryUntil = Mathf.Max(recoveryUntil, Time.time + seconds);

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake() => Initialize();

    void Initialize()
    {
        Character  = GetComponent<Character>();
        Motor      = GetComponent<EnemyMotor>();
        Perception = GetComponent<Perception>();
        Perception.Profile = Profile;
        if (attackRelay == null) attackRelay = GetComponentInChildren<WeaponAnimationRelay>(true);
        spawnPosition = transform.position;

        handlers = new Dictionary<EnemyState, Action>
        {
            { EnemyState.Idle,         HandleIdle },
            { EnemyState.Wander,       HandleWander },
            { EnemyState.Patrol,       HandlePatrol },
            { EnemyState.Investigate,  HandleInvestigate },
            { EnemyState.Chase,        HandleChase },
            { EnemyState.Attack,       HandleAttack },
            { EnemyState.ReturnToPost, HandleReturnToPost },
            { EnemyState.Dead,         null },
        };

        attackRunner ??= new EnemyAttackRunner(this);
        attackRunner.ResetCooldowns();
    }

    void OnEnable()
    {
        // Unity can reload scripts while retaining scene objects; nonserialized handlers
        // are then lost even though Awake will not run again on those objects.
        if (handlers == null) Initialize();
        Character.Damaged += OnDamaged;
        Character.Died    += OnDied;
    }

    void OnDisable()
    {
        Character.Damaged -= OnDamaged;
        Character.Died    -= OnDied;
        CancelAttack();
    }

    void Start()
    {
        SetState(Profile.defaultState);
    }

    internal List<EnemyAttack> Attacks =>
        Character != null && Character.data != null ? Character.data.attacks : emptyAttacks;
    static readonly List<EnemyAttack> emptyAttacks = new();

    float MaxAttackRange
    {
        get
        {
            float r = 0f;
            foreach (var a in Attacks) if (a != null) r = Mathf.Max(r, a.EffectiveMaxRange);
            return r > 0f ? r : 1.8f;
        }
    }

    float PreferredDistance => MaxAttackRange * Profile.preferredRangeFraction;

    bool AnyAttackCoversDistance(float dist)
    {
        foreach (var a in Attacks) if (a != null && dist >= a.minRange && dist <= a.EffectiveMaxRange) return true;
        return false;
    }

    void Update()
    {
        if (State == EnemyState.Dead) return;
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive)
        {
            CancelAttack(); Motor.Stop(); Motor.ClearLookTarget(); return;
        }

        Perception.Profile = Profile;
        Perception.Tick(State == EnemyState.Chase || State == EnemyState.Attack);
        Motor.SuppressKnockback = IsSwinging;
        attackRunner.Tick();

        if (Time.time < recoveryUntil)
        {
            // Catch breath, but keep the eyes on the target so the next move is instant.
            // A committed swing never tracks.
            Motor.Stop();
            if (Perception.Target != null && State != EnemyState.Attack)
                Motor.LookAt(Perception.Target.transform.position, Profile.attackFaceSpeed);
            UpdateAnimator();
            return;
        }

        if (Motor.IsKnockedBack) { UpdateAnimator(); return; }

        if (handlers.TryGetValue(State, out var handler)) handler?.Invoke();

        UpdateAnimator();
    }

    // ── Events ────────────────────────────────────────────────────────

    void OnDamaged(DamageInfo info)
    {
        if (State == EnemyState.Dead || !Character.IsAlive) return;
        if (info.Source != null) { wasProvoked = true; Perception.NotifyAttackedFrom(info.Source.transform.position); }

        // Flinch only when not mid-swing: a swing is never interrupted by the player's hits.
        // A heavy stagger taken mid-swing is applied when the swing ends.
        if (info.Heavy && !info.Blocked && info.Amount > 0f && Tuning != null)
        {
            if (IsSwinging) attackRunner.QueueStagger(Tuning.heavyStaggerDuration);
            else            BeginRecovery(Tuning.heavyStaggerDuration);
        }
        if (!IsSwinging && !info.Blocked && info.Amount > 0f && Character.data != null)
            Character.TriggerAnimation(Character.data.hurtTrigger);

        bool inCombat = State == EnemyState.Chase || State == EnemyState.Attack;
        if (inCombat) return;

        if (Profile.aggressionMode == AggressionMode.AggressiveWhenHit && Perception.Target != null)
            EnterCombat();
        else if (Profile.aggressionMode != AggressionMode.Passive)
            StartInvestigate(Perception.StimulusPosition);
    }

    void CancelAttack() => attackRunner?.Cancel();

    void OnDied()
    {
        CancelAttack();
        Motor.Stop();
        Motor.ClearLookTarget();
        SetState(EnemyState.Dead);
        if (Motor.Agent != null) Motor.Agent.enabled = false;
        foreach (var col in GetComponents<Collider>()) col.enabled = false;
    }

    // ── Passive states ────────────────────────────────────────────────

    bool TryNoticeTarget()
    {
        if (CanEnterCombat() && Perception.TargetVisible) { EnterCombat(); return true; }
        if (Perception.HasStimulus && Profile.aggressionMode != AggressionMode.Passive)
        {
            StartInvestigate(Perception.StimulusPosition);
            return true;
        }
        return false;
    }

    void HandleIdle()   { TryNoticeTarget(); }
    void HandleWander() { if (!TryNoticeTarget()) HandleWanderMovement(); }
    void HandlePatrol() { if (!TryNoticeTarget()) HandlePatrolMovement(); }

    void HandleInvestigate()
    {
        if (CanEnterCombat() && Perception.TargetVisible) { EnterCombat(); return; }

        if (Perception.HasStimulus)
        {
            investigatePoint = Perception.StimulusPosition;
            Perception.ConsumeStimulus();
            Motor.MoveTo(investigatePoint);
            investigateTimer = Profile.investigateTime;
        }

        bool near = Motor.IsNear(investigatePoint, 0.5f);
        // A stagger or pause stopped the walk before it got there: walk again.
        if (!near && !Motor.HasDestination) { Motor.MoveTo(investigatePoint); return; }

        if (near || Motor.ReachedDestination(0.5f))
        {
            investigateTimer -= Time.deltaTime;
            if (investigateTimer <= 0f) SetState(EnemyState.ReturnToPost);
        }
    }

    void StartInvestigate(Vector3 point)
    {
        investigatePoint = point;
        Perception.ConsumeStimulus();
        SetState(EnemyState.Investigate);
        Motor.MoveTo(point);
        investigateTimer = Profile.investigateTime;
    }

    // ── Combat states ─────────────────────────────────────────────────

    bool CanEnterCombat() => Profile.aggressionMode switch
    {
        AggressionMode.Passive           => false,
        AggressionMode.Aggressive        => true,
        AggressionMode.AggressiveWhenHit => wasProvoked,
        _                                => false,
    };

    void EnterCombat()
    {
        if (Perception.Target == null) return;
        wasProvoked    = true;
        loseSightTimer = Profile.loseSightGracePeriod;
        giveUpTimer    = -1f;
        // Entering combat means we know where they are right now, seen or not.
        Perception.SetLastKnownTargetPosition(Perception.Target.transform.position);
        Perception.ConsumeStimulus();
        SetState(EnemyState.Chase);
    }

    void HandleChase()
    {
        var target = Perception.Target;
        if (target == null) { SetState(EnemyState.ReturnToPost); return; }
        var p = Profile;
        Vector3 targetPos = target.transform.position;
        float dist = Perception.HorizontalDist(transform.position, targetPos);

        if (!Perception.TargetVisible)
        {
            // Lost sight: run to the last known spot, search a bit, then give up.
            Motor.ClearLookTarget();
            loseSightTimer -= Time.deltaTime;
            Motor.MoveTo(Perception.LastKnownTargetPosition);
            if (loseSightTimer <= 0f)
            {
                if (giveUpTimer < 0f) giveUpTimer = p.maxSearchTime;
                giveUpTimer -= Time.deltaTime;
                bool reached = Motor.IsNear(Perception.LastKnownTargetPosition, 0.5f);
                if (reached || giveUpTimer <= 0f) SetState(EnemyState.ReturnToPost);
            }
            return;
        }

        loseSightTimer = p.loseSightGracePeriod;
        giveUpTimer    = -1f;

        // Ready attack in range: face it fast and swing the moment we are lined up.
        // A cancelled animation may still be blending out after a menu or cutscene.
        var attack = attackRunner.IsPlayingAttack() ? null : attackRunner.PickAttack(dist);
        if (attack != null)
        {
            Motor.LookAt(targetPos, p.attackFaceSpeed);
            if (Motor.IsFacing(targetPos, attack.facingAngle)
                && Mathf.Abs(targetPos.y - transform.position.y) <= 1f
                && (Tuning == null || Tuning.HasMeleeLineOfSight(Character, target, targetPos + Vector3.up * 0.9f)))
                attackRunner.StartAttack(attack);
            else HandleSpacing(targetPos, dist);
            return;
        }

        if (AnyAttackCoversDistance(dist) || dist <= PreferredDistance)
        {
            HandleSpacing(targetPos, dist);
            return;
        }

        // Approach. Look at the target once close so the run-in becomes a strafe-run.
        Motor.SetAutoBraking(false);
        Motor.SetStoppingDistance(PreferredDistance);
        var filter = new NavMeshQueryFilter { agentTypeID = Motor.Agent.agentTypeID, areaMask = Motor.Agent.areaMask };
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 1.5f, filter))
            Motor.MoveTo(navHit.position);
        else
        {
            // Target is off the mesh (on a prop, mid-jump): walk straight at it along the mesh
            // instead of pathing to some unrelated nearest point.
            float chaseSpeed = p.useChaseSpeed ? p.chaseSpeed : Motor.DefaultSpeed;
            Vector3 toTarget = targetPos - transform.position; toTarget.y = 0f;
            Motor.Strafe(toTarget, chaseSpeed);
        }
        if (dist < MaxAttackRange * 2.5f) Motor.LookAt(targetPos, p.attackFaceSpeed);
        else Motor.ClearLookTarget();
    }

    // In range, attack recharging: hold the preferred distance and circle, always facing.
    void HandleSpacing(Vector3 targetPos, float dist)
    {
        var p = Profile;
        Motor.LookAt(targetPos, p.attackFaceSpeed);

        float chaseSpeed = p.useChaseSpeed ? p.chaseSpeed : Motor.DefaultSpeed;
        float preferred  = PreferredDistance;
        Vector3 toTarget = targetPos - transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) toTarget = transform.forward;
        toTarget.Normalize();

        if (dist > preferred + 0.2f)
        {
            Motor.Strafe(toTarget, chaseSpeed * p.spacingSpeedFraction);
            return;
        }
        if (dist < preferred - 0.35f)
        {
            Motor.Strafe(-toTarget, chaseSpeed * p.spacingSpeedFraction);
            return;
        }

        if (!p.circleTarget || p.circleSpeedFraction <= 0f)
        {
            Motor.Stop();
            return;
        }

        if (Time.time >= circleSwitchAt)
        {
            circleDir = UnityEngine.Random.value < 0.5f ? -1 : 1;
            circleSwitchAt = Time.time + UnityEngine.Random.Range(p.circleSwitchInterval.x, p.circleSwitchInterval.y);
        }

        float speed = chaseSpeed * p.circleSpeedFraction;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget) * circleDir;
        Motor.Strafe(side, speed);

        // Blocked by a wall or another enemy: flip direction instead of pushing into it.
        float actual = new Vector3(Motor.Velocity.x, 0f, Motor.Velocity.z).magnitude;
        if (actual < speed * 0.25f)
        {
            if (circleStuckSince < 0f) circleStuckSince = Time.time;
            else if (Time.time - circleStuckSince > 0.35f)
            {
                circleDir = -circleDir;
                circleSwitchAt = Time.time + UnityEngine.Random.Range(p.circleSwitchInterval.x, p.circleSwitchInterval.y);
                circleStuckSince = -1f;
            }
        }
        else circleStuckSince = -1f;
    }

    void HandleAttack() => attackRunner.HandleAttackState();

    // Animation event on the attack clip: the moment the swing connects.
    public void OnAttackHit() => attackRunner?.OnAttackHit();
    public void CommitDirection() => attackRunner?.CommitDirection();
    // Called by WeaponAnimationRelay for EnableHitbox / DisableHitbox events.
    public bool DeferHitboxIfEarly(int index) => attackRunner != null && attackRunner.DeferHitboxIfEarly(index);
    public void NotifyHitboxClosed() => attackRunner?.NotifyHitboxClosed();

    void HandleReturnToPost()
    {
        if (TryNoticeTarget()) return;

        Vector3 post = GetPostPosition();
        Motor.MoveTo(post);

        if (Motor.IsNear(post, 0.1f) || Motor.ReachedDestination(0.1f))
        {
            if (Profile.aggressionMode == AggressionMode.AggressiveWhenHit) wasProvoked = false;
            SetState(Profile.defaultState);
        }
    }

    Vector3 GetPostPosition()
    {
        EnemyState def = Profile.defaultState;
        if (def == EnemyState.Patrol && waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            return waypoints[waypointIndex].position;
        return spawnPosition;
    }

    // ── Wander / patrol ───────────────────────────────────────────────

    void HandleWanderMovement()
    {
        var p = Profile;
        Motor.WanderStep(ref wanderTimer, spawnPosition, p.minWanderDistance, p.wanderRadius, p.wanderZoneRadius,
                         p.minIdleTime, p.maxIdleTime);
    }

    void HandlePatrolMovement() => Motor.PatrolStep(waypoints, ref waypointIndex, loopPatrol);

    // ── State plumbing ────────────────────────────────────────────────

    public void SetState(EnemyState newState)
    {
        if (State == EnemyState.Attack && newState != EnemyState.Attack) CancelAttack();
        State = newState;
        wanderTimer = 0f;
        // Hits and noises taken while chasing are old news once the enemy gives up.
        if (newState == EnemyState.ReturnToPost) Perception.ConsumeStimulus();

        var p = Profile;
        bool isCombat = newState == EnemyState.Chase || newState == EnemyState.Attack;

        if (newState == EnemyState.Chase) loseSightTimer = p.loseSightGracePeriod;

        if (newState == EnemyState.Idle || newState == EnemyState.Attack || newState == EnemyState.Dead)
            Motor.Stop();
        else
            Motor.Resume();

        if (!isCombat) Motor.ClearLookTarget();

        if (newState == EnemyState.Patrol && waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            Motor.MoveTo(waypoints[waypointIndex].position);

        if (isCombat && p.useChaseSpeed)        Motor.SetSpeed(p.chaseSpeed);
        else if (!isCombat && p.useWanderSpeed) Motor.SetSpeed(p.wanderSpeed);
        else                                    Motor.ResetSpeed();
        Motor.SetAcceleration(p.acceleration);
        Motor.SetAngularSpeed(isCombat ? p.chaseAngularSpeed : p.passiveAngularSpeed);
        Motor.SetAutoBraking(!isCombat);
        Motor.SetStoppingDistance(isCombat ? PreferredDistance : 0.3f);
    }

    void UpdateAnimator()
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, "HurtSpeed", AnimatorControllerParameterType.Float))
            anim.SetFloat("HurtSpeed", Tuning != null ? Tuning.enemyHurtAnimationSpeed : 1.3f);
        if (Character.HasParameter(anim, "AttackSpeed", AnimatorControllerParameterType.Float))
        {
            float statMul = Character.Stats != null ? Character.Stats.GetMultiplier(StatType.AttackSpeed) : 1f;
            anim.SetFloat("AttackSpeed", (Tuning != null ? Tuning.enemyAttackAnimationSpeed : 1.35f) * statMul);
        }
        Motor.UpdateLocomotionAnimator(anim, true, 0.08f);
    }

    // Kept for cutscene scripts and Odin buttons.
    [Button] public void SnapRotationTowardsPlayer()
    {
        if (!PlayerManager.HasInstance || PlayerManager.Instance.Active == null) return;
        Motor.SnapFace(PlayerManager.Instance.Active.transform.position);
    }

    // ── Gizmos ────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        var p = Application.isPlaying ? Profile
                : overrideBehaviour ? localBehaviour
                : GetComponent<Character>()?.data?.behaviour?.settings;
        if (p == null) return;

        Vector3 pos = Application.isPlaying ? spawnPosition : transform.position;

        Gizmos.color = Color.yellow;
        var data = GetComponent<Character>()?.data;
        if (data != null)
            foreach (var a in data.attacks)
                if (a != null) Gizmos.DrawWireSphere(transform.position, a.EffectiveMaxRange);

        if (p.defaultState == EnemyState.Wander && p.wanderZoneRadius > 0f)
        {
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.1f);
            Gizmos.DrawSphere(pos, p.wanderZoneRadius);
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(pos, p.wanderZoneRadius);
        }

        if (p.defaultState == EnemyState.Patrol && waypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawSphere(waypoints[i].position, 0.2f);
                int next = i + 1;
                if (next < waypoints.Length && waypoints[next] != null) Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
                else if (loopPatrol && waypoints[0] != null)            Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
            }
        }
    }
}
