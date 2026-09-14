using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// The enemy state machine. Perception supplies what it knows, EnemyMotor moves it,
// CharacterData supplies the attacks and the EnemyBehaviourProfile supplies tuning.
// Variety comes from data; subclass only when the behaviour itself differs.
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(EnemyMotor))]
[RequireComponent(typeof(Perception))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("Override the profile from CharacterData for this instance only.")]
    [SerializeField] bool overrideBehaviour = false;
    [ShowIf("overrideBehaviour")]
    [SerializeField] EnemyBehaviourProfile localProfile;

    [Header("Wander")]
    [Tooltip("Centre of the wander area. Defaults to spawn position when left empty.")]
    [SerializeField] Transform wanderZoneCenter;

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

    public EnemyBehaviourProfile Profile =>
        overrideBehaviour && localProfile != null ? localProfile
        : Character != null && Character.data != null && Character.data.behaviour != null ? Character.data.behaviour
        : FallbackProfile;

    static EnemyBehaviourProfile fallback;
    static EnemyBehaviourProfile FallbackProfile =>
        fallback != null ? fallback : (fallback = ScriptableObject.CreateInstance<EnemyBehaviourProfile>());

    Dictionary<EnemyState, Action> handlers;
    Vector3 spawnPosition;
    int     waypointIndex;
    float   wanderTimer;
    float   loseSightTimer;
    float   giveUpTimer = -1f;
    float   investigateTimer;
    bool    wasProvoked;
    Vector3 investigatePoint;

    // Attacks
    readonly List<float> attackCooldowns = new();
    EnemyAttack currentAttack;
    bool        fallbackHitPending;
    float       fallbackHitAt;
    bool        attackWasPlaying;

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
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

        var attacks = Attacks;
        for (int i = 0; i < attacks.Count; i++) attackCooldowns.Add(0f);
    }

    void OnEnable()
    {
        Character.Damaged += OnDamaged;
        Character.Died    += OnDied;
    }

    void OnDisable()
    {
        Character.Damaged -= OnDamaged;
        Character.Died    -= OnDied;
    }

    void Start()
    {
        SetState(Profile.defaultState);
    }

    List<EnemyAttack> Attacks =>
        Character != null && Character.data != null ? Character.data.attacks : emptyAttacks;
    static readonly List<EnemyAttack> emptyAttacks = new();

    void Update()
    {
        if (State == EnemyState.Dead) return;

        Perception.Profile = Profile;
        for (int i = 0; i < attackCooldowns.Count; i++) attackCooldowns[i] -= Time.deltaTime;

        if (fallbackHitPending && Time.time >= fallbackHitAt) DealFallbackHit();

        if (Motor.IsKnockedBack) { UpdateAnimator(); return; }

        Perception.Tick();

        if (handlers.TryGetValue(State, out var handler)) handler?.Invoke();

        UpdateAnimator();
    }

    // ── Events ────────────────────────────────────────────────────────

    void OnDamaged(DamageInfo info)
    {
        if (State == EnemyState.Dead) return;
        if (info.Source != null) Perception.NotifyAttackedFrom(info.Source.transform.position);

        if (Profile.aggressionMode == AggressionMode.AggressiveWhenHit && Perception.Target != null
            && State != EnemyState.Chase && State != EnemyState.Attack)
            EnterCombat();
        else if (State != EnemyState.Chase && State != EnemyState.Attack && Profile.aggressionMode != AggressionMode.Passive)
            StartInvestigate(Perception.StimulusPosition);
    }

    void OnDied()
    {
        fallbackHitPending = false;
        Motor.Stop();
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

    void HandleIdle()
    {
        TryNoticeTarget();
    }

    void HandleWander()
    {
        if (TryNoticeTarget()) return;
        HandleWanderMovement();
    }

    void HandlePatrol()
    {
        if (TryNoticeTarget()) return;
        HandlePatrolMovement();
    }

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

        if (Motor.IsNear(investigatePoint, 0.5f) || Motor.ReachedDestination(0.5f))
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
        Perception.ConsumeStimulus();
        SetState(EnemyState.Chase);
    }

    void HandleChase()
    {
        var target = Perception.Target;
        if (target == null) { SetState(EnemyState.ReturnToPost); return; }

        float dist = Perception.HorizontalDist(transform.position, target.transform.position);
        if (Perception.TargetVisible && PickAttack(dist) != null)
        {
            SetState(EnemyState.Attack);
            return;
        }

        if (Perception.TargetVisible)
        {
            loseSightTimer = Profile.loseSightGracePeriod;
            giveUpTimer    = -1f;
            Motor.MoveTo(target.transform.position);
        }
        else
        {
            loseSightTimer -= Time.deltaTime;
            Motor.MoveTo(Perception.LastKnownTargetPosition);

            if (loseSightTimer <= 0f)
            {
                if (giveUpTimer < 0f) giveUpTimer = Profile.maxSearchTime;
                giveUpTimer -= Time.deltaTime;

                bool reached = Motor.IsNear(Perception.LastKnownTargetPosition, 0.5f);
                if (reached || giveUpTimer <= 0f) SetState(EnemyState.ReturnToPost);
            }
        }
    }

    void HandleAttack()
    {
        var target = Perception.Target;
        if (target == null) { SetState(Profile.defaultState); return; }

        // Stay planted mid-swing — no chasing or turning until the attack animation ends.
        if (IsPlayingAttack()) { attackWasPlaying = true; return; }
        if (attackWasPlaying)
        {
            // Swing finished without its hit event firing — drop the pending hit.
            attackWasPlaying   = false;
            fallbackHitPending = false;
        }

        float dist = Perception.HorizontalDist(transform.position, target.transform.position);
        var attack = PickAttack(dist);
        if (attack == null)
        {
            SetState(EnemyState.Chase);
            return;
        }

        Motor.Face(target.transform.position, Profile.attackFaceSpeed);
        if (!Motor.IsFacing(target.transform.position, attack.facingAngle)) return;

        Motor.SnapFace(target.transform.position);
        StartAttack(attack);
    }

    EnemyAttack PickAttack(float dist)
    {
        var attacks = Attacks;
        float totalWeight = 0f;
        for (int i = 0; i < attacks.Count; i++)
        {
            var a = attacks[i];
            if (a == null || dist < a.minRange || dist > a.maxRange || attackCooldowns[i] > 0f) continue;
            totalWeight += a.weight;
        }
        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        for (int i = 0; i < attacks.Count; i++)
        {
            var a = attacks[i];
            if (a == null || dist < a.minRange || dist > a.maxRange || attackCooldowns[i] > 0f) continue;
            roll -= a.weight;
            if (roll <= 0f) return a;
        }
        return null;
    }

    void StartAttack(EnemyAttack attack)
    {
        currentAttack = attack;
        int index = Attacks.IndexOf(attack);
        if (index >= 0) attackCooldowns[index] = attack.cooldown;

        var hitbox = attackRelay != null ? attackRelay.GetHitbox(attack.hitboxIndex) : null;
        if (hitbox != null) hitbox.SetProfile(attack.hit);

        Character.TriggerAnimation(attack.animatorTrigger);

        // Without a hitbox the hit lands either on the clip's OnAttackHit animation event
        // or, if the clip has none, after fallbackHitDelay seconds.
        if (hitbox == null)
        {
            fallbackHitPending = true;
            fallbackHitAt = attack.fallbackHitDelay >= 0f ? Time.time + attack.fallbackHitDelay : float.MaxValue;
        }
    }

    // Animation event on the attack clip: the moment the swing connects.
    public void OnAttackHit()
    {
        if (fallbackHitPending) DealFallbackHit();
    }

    // Direct hit for enemies without a Hitbox.
    void DealFallbackHit()
    {
        fallbackHitPending = false;
        var target = Perception.Target;
        if (currentAttack == null || target == null || !Character.IsAlive) return;

        // Facing was already required to start the swing; the clip may turn the model
        // by the time the hit event fires, so only distance is checked here.
        float dist = Perception.HorizontalDist(transform.position, target.transform.position);
        if (dist > currentAttack.maxRange * 1.2f) return;

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        float force = currentAttack.hit.knockbackForce >= 0f ? currentAttack.hit.knockbackForce
                    : (Character.FX != null ? Character.FX.KnockbackForce : 1f);
        if (CombatManager.HasInstance) force = CombatManager.Instance.ScaleKnockback(force);

        float damage = Character.Stats != null ? Character.Stats.GetFinal(StatType.AttackDamage) : 0f;
        var info = new DamageInfo
        {
            Amount         = damage * currentAttack.hit.damageMultiplier,
            Source         = Character,
            HitPoint       = target.transform.position + Vector3.up * 1f,
            Direction      = dir,
            KnockbackForce = force,
        };

        var damageable = target.GetComponent<IDamageable>();
        if (damageable == null) return;
        damageable.TakeDamage(info);
        Character.NotifyHitLanded(info);
        if (CombatManager.HasInstance && CombatManager.Instance.hitStopOnPlayerHurt)
            CombatManager.Instance.RequestHitStop(currentAttack.hit.hitStopScale);
    }

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
        if (def == EnemyState.Wander && wanderZoneCenter != null)
            return wanderZoneCenter.position;
        return spawnPosition;
    }

    // ── Wander / patrol ───────────────────────────────────────────────

    void HandleWanderMovement()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        var p = Profile;
        Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position : spawnPosition;
        Vector3 dir = UnityEngine.Random.insideUnitSphere; dir.y = 0f; dir.Normalize();
        Vector3 target = center + dir * UnityEngine.Random.Range(p.minWanderDistance, p.wanderRadius);

        if (p.wanderZoneRadius > 0f)
        {
            Vector3 offset = target - center; offset.y = 0f;
            if (offset.magnitude > p.wanderZoneRadius) target = center + offset.normalized * p.wanderZoneRadius;
        }

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, p.wanderRadius, NavMesh.AllAreas))
            Motor.MoveTo(hit.position);

        wanderTimer = UnityEngine.Random.Range(p.minIdleTime, p.maxIdleTime);
    }

    void HandlePatrolMovement()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (!Motor.ReachedDestination(0.4f)) return;
        waypointIndex++;
        if (waypointIndex >= waypoints.Length) waypointIndex = loopPatrol ? 0 : waypoints.Length - 1;
        if (waypoints[waypointIndex] != null) Motor.MoveTo(waypoints[waypointIndex].position);
    }

    // ── State plumbing ────────────────────────────────────────────────

    public void SetState(EnemyState newState)
    {
        State = newState;
        wanderTimer = 0f;

        if (newState == EnemyState.Chase) loseSightTimer = Profile.loseSightGracePeriod;

        if (newState == EnemyState.Idle || newState == EnemyState.Attack || newState == EnemyState.Dead)
            Motor.Stop();
        else
            Motor.Resume();

        if (newState == EnemyState.Patrol && waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            Motor.MoveTo(waypoints[waypointIndex].position);

        var p = Profile;
        bool isCombat = newState == EnemyState.Chase || newState == EnemyState.Attack;
        if (isCombat && p.useChaseSpeed)        Motor.SetSpeed(p.chaseSpeed);
        else if (!isCombat && p.useWanderSpeed) Motor.SetSpeed(p.wanderSpeed);
        else                                    Motor.ResetSpeed();
        Motor.SetAngularSpeed(isCombat ? p.chaseAngularSpeed : p.passiveAngularSpeed);
    }

    bool IsPlayingAttack()
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).IsTag("Attack")) return true;
            if (anim.IsInTransition(layer) && anim.GetNextAnimatorStateInfo(layer).IsTag("Attack")) return true;
        }
        return false;
    }

    void UpdateAnimator()
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return;
        float speed = Motor.Velocity.magnitude;
        anim.SetFloat("Speed",       speed, 0.1f, Time.deltaTime);
        anim.SetFloat("MotionSpeed", Motor.HasPath ? 1f : 0f, 0.1f, Time.deltaTime);
        anim.SetBool("Grounded", Motor.IsGrounded);
        anim.SetBool("FreeFall", !Motor.IsGrounded && Motor.Velocity.y < -1f);
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
        var p = Application.isPlaying ? Profile : (overrideBehaviour && localProfile != null ? localProfile
                : GetComponent<Character>()?.data?.behaviour);
        if (p == null) return;

        Vector3 pos = Application.isPlaying ? spawnPosition : transform.position;

        Gizmos.color = Color.yellow;
        var data = GetComponent<Character>()?.data;
        if (data != null)
            foreach (var a in data.attacks)
                if (a != null) Gizmos.DrawWireSphere(transform.position, a.maxRange);

        if (p.defaultState == EnemyState.Wander && p.wanderZoneRadius > 0f)
        {
            Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position : pos;
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.1f);
            Gizmos.DrawSphere(center, p.wanderZoneRadius);
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(center, p.wanderZoneRadius);
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
