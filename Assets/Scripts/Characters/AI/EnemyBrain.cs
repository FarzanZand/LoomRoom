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
    readonly List<float> attackCooldowns = new();
    EnemyAttack currentAttack;
    bool        fallbackHitPending;
    float       fallbackHitAt;
    bool        attackWasPlaying;
    float       attackStartedAt, recoveryUntil;
    Vector3     committedForward;
    bool        directionCommitted;
    CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;
    public bool CanOpenHitbox => isActiveAndEnabled && Character.IsAlive && State == EnemyState.Attack && currentAttack != null
        && Time.time >= attackStartedAt + (Tuning != null ? Tuning.enemyMinimumWindup : 0.3f)
        && (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
    public bool IsSwinging    => currentAttack != null;

    float CommitTime   => Tuning != null ? Tuning.enemyAttackCommitTime : 0.4f;
    float RecoveryTime => Tuning != null ? Tuning.enemyRecoveryTime : 0.18f;

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

        attackCooldowns.Clear();
        var attacks = Attacks;
        for (int i = 0; i < attacks.Count; i++) attackCooldowns.Add(0f);
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

    List<EnemyAttack> Attacks =>
        Character != null && Character.data != null ? Character.data.attacks : emptyAttacks;
    static readonly List<EnemyAttack> emptyAttacks = new();

    float MaxAttackRange
    {
        get
        {
            float r = 0f;
            foreach (var a in Attacks) if (a != null) r = Mathf.Max(r, a.maxRange);
            return r > 0f ? r : 1.8f;
        }
    }

    float PreferredDistance => MaxAttackRange * Profile.preferredRangeFraction;

    bool AnyAttackCoversDistance(float dist)
    {
        foreach (var a in Attacks) if (a != null && dist >= a.minRange && dist <= a.maxRange) return true;
        return false;
    }

    void Update()
    {
        if (State == EnemyState.Dead) return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive)
        {
            CancelAttack(); Motor.Stop(); Motor.ClearLookTarget(); return;
        }

        Perception.Profile = Profile;
        Perception.Tick(State == EnemyState.Chase || State == EnemyState.Attack);
        Motor.SuppressKnockback = IsSwinging;
        for (int i = 0; i < attackCooldowns.Count; i++) attackCooldowns[i] -= Time.deltaTime;

        if (fallbackHitPending && Time.time >= fallbackHitAt) DealFallbackHit();

        if (Time.time < recoveryUntil)
        {
            // Catch breath, but keep the eyes on the target so the next move is instant.
            Motor.Stop();
            if (Perception.Target != null) Motor.LookAt(Perception.Target.transform.position, Profile.attackFaceSpeed);
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
        if (!IsSwinging && !info.Blocked && info.Amount > 0f && Character.data != null)
            Character.TriggerAnimation(Character.data.hurtTrigger);

        bool inCombat = State == EnemyState.Chase || State == EnemyState.Attack;
        if (inCombat) return;

        if (Profile.aggressionMode == AggressionMode.AggressiveWhenHit && Perception.Target != null)
            EnterCombat();
        else if (Profile.aggressionMode != AggressionMode.Passive)
            StartInvestigate(Perception.StimulusPosition);
    }

    void CancelAttack()
    {
        if (Character != null && Character.Animator != null)
            foreach (var attack in Attacks)
                if (attack != null && Character.HasParameter(Character.Animator, attack.animatorTrigger, AnimatorControllerParameterType.Trigger))
                    Character.Animator.ResetTrigger(attack.animatorTrigger);
        fallbackHitPending = false; currentAttack = null; attackWasPlaying = false;
        if (attackRelay != null) attackRelay.DisableHitbox();
        if (Motor != null) Motor.SuppressKnockback = false;
    }

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
        var attack = IsPlayingAttack() ? null : PickAttack(dist);
        if (attack != null)
        {
            Motor.LookAt(targetPos, p.attackFaceSpeed);
            if (Motor.IsFacing(targetPos, attack.facingAngle)
                && Mathf.Abs(targetPos.y - transform.position.y) <= 1f
                && (Tuning == null || Tuning.HasMeleeLineOfSight(Character, target, targetPos + Vector3.up * 0.9f)))
                StartAttack(attack);
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

    void HandleAttack()
    {
        var target = Perception.Target;
        if (target == null) { CancelAttack(); SetState(Profile.defaultState); return; }
        if (currentAttack == null) { SetState(EnemyState.Chase); return; }

        Motor.Stop();

        // Windup: keep tracking the target so circling does not trivially dodge. Then commit.
        float sinceStart = Time.time - attackStartedAt;
        if (!directionCommitted && sinceStart < CommitTime && Profile.windupTrackSpeed > 0f)
        {
            Motor.LookAt(target.transform.position, Profile.windupTrackSpeed);
        }
        else
        {
            CommitDirection();
            if (!swingAudioPlayed) { swingAudioPlayed = true; PlaySwingAudio(); }
        }

        if (IsPlayingAttack()) { attackWasPlaying = true; return; }

        // Swing finished (or never started because the animator has no such state).
        float startupTimeout = Mathf.Max(0.5f, currentAttack.fallbackHitDelay + 0.1f);
        if (attackWasPlaying || sinceStart > startupTimeout)
        {
            // Cooldown counts from the END of the swing, so there is always an opening
            // between attacks where the enemy spaces and circles instead of chaining swings.
            int index = Attacks.IndexOf(currentAttack);
            if (index >= 0) attackCooldowns[index] = currentAttack.cooldown * (Tuning != null ? Tuning.enemyCooldownMultiplier : 1f);

            CancelAttack();
            recoveryUntil = Time.time + RecoveryTime;
            SetState(EnemyState.Chase);
        }
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

    // The whoosh plays when the windup commits, just before the hit event.
    void PlaySwingAudio()
    {
        if (currentAttack == null || !AudioManager.HasInstance) return;
        var hit = currentAttack.hit;
        AudioData audio = hit.useDefaultEffects ? (Tuning != null ? Tuning.defaultSwingAudio : null) : hit.swingAudio;
        if (audio != null) AudioManager.Instance.PlaySFXData(audio, transform.position + Vector3.up);
    }

    bool swingAudioPlayed;

    void StartAttack(EnemyAttack attack)
    {
        currentAttack = attack;
        attackStartedAt = Time.time;
        committedForward = transform.forward;
        directionCommitted = false;
        attackWasPlaying = false;
        swingAudioPlayed = false;
        var hitbox = attackRelay != null ? attackRelay.GetHitbox(attack.hitboxIndex) : null;
        if (hitbox != null) hitbox.SetProfile(attack.hit);

        SetState(EnemyState.Attack);
        Motor.SuppressKnockback = true;
        // Clear a flinch queued earlier this frame before it can override this swing.
        if (Character.data != null && Character.HasParameter(Character.Animator, Character.data.hurtTrigger, AnimatorControllerParameterType.Trigger))
            Character.Animator.ResetTrigger(Character.data.hurtTrigger);
        Character.TriggerAnimation(attack.animatorTrigger);

        // Without a hitbox the hit lands either on the clip's OnAttackHit animation event
        // or, if the clip has none, after fallbackHitDelay seconds.
        if (hitbox == null)
        {
            fallbackHitPending = true;
            float minWindup = Tuning != null ? Tuning.enemyMinimumWindup : 0.3f;
            fallbackHitAt = attack.fallbackHitDelay >= 0f ? Time.time + Mathf.Max(attack.fallbackHitDelay, minWindup) : float.MaxValue;
        }
    }

    // Animation event on the attack clip: the moment the swing connects.
    public void OnAttackHit()
    {
        float minWindup = Tuning != null ? Tuning.enemyMinimumWindup : 0.3f;
        if (fallbackHitPending && Time.time >= attackStartedAt + minWindup) DealFallbackHit();
    }

    public void CommitDirection()
    {
        if (currentAttack == null || directionCommitted) return;
        directionCommitted = true;
        committedForward = transform.forward;
        Motor.ClearLookTarget();
    }

    // Direct hit for enemies without a Hitbox.
    void DealFallbackHit()
    {
        fallbackHitPending = false;
        var target = Perception.Target;
        if (!CanOpenHitbox || target == null || !target.IsAlive || !FactionRules.IsHostile(Character.Faction, target.Faction)) return;
        CommitDirection();

        // Facing was required to start the swing and tracking ran through the windup; at
        // impact only distance and a generous arc around the committed direction count.
        float dist = Perception.HorizontalDist(transform.position, target.transform.position);
        if (dist > currentAttack.maxRange * 1.1f || Mathf.Abs(target.transform.position.y - transform.position.y) > 1f) return;
        Vector3 toward = target.transform.position - transform.position; toward.y = 0f;
        float arc = Tuning != null ? Tuning.enemyHitFacingAngle : 60f;
        if (Vector3.Angle(committedForward, toward) > arc) return;

        Vector3 dir = toward.sqrMagnitude > 0.0001f ? toward.normalized : transform.forward;

        float force = currentAttack.hit.knockbackForce >= 0f ? currentAttack.hit.knockbackForce
                    : (Character.FX != null ? Character.FX.KnockbackForce : 1f);
        if (CombatManager.HasInstance) force = CombatManager.Instance.ScaleKnockback(force);

        float damage = Character.Stats != null ? Character.Stats.GetFinal(StatType.AttackDamage) : 0f;
        var info = new DamageInfo
        {
            Profile        = currentAttack.hit,
            Amount         = damage * currentAttack.hit.damageMultiplier,
            Source         = Character,
            HitPoint       = target.transform.position + Vector3.up * 1f,
            Direction      = dir,
            KnockbackForce = force,
        };

        var damageable = target.GetComponent<IDamageable>();
        if (damageable == null) return;
        if (Tuning != null && !Tuning.HasMeleeLineOfSight(Character, target, info.HitPoint)) return;
        damageable.TakeDamage(info);
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
        return spawnPosition;
    }

    // ── Wander / patrol ───────────────────────────────────────────────

    void HandleWanderMovement()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        var p = Profile;
        Vector3 center = spawnPosition;
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
        if (State == EnemyState.Attack && newState != EnemyState.Attack) CancelAttack();
        State = newState;
        wanderTimer = 0f;

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

    bool IsPlayingAttack() => IsPlayingTag("Attack");

    bool IsPlayingTag(string tag)
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).IsTag(tag)) return true;
            if (anim.IsInTransition(layer) && anim.GetNextAnimatorStateInfo(layer).IsTag(tag)) return true;
        }
        return false;
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
        Vector3 v = Motor.Velocity; v.y = 0f;
        float speed = v.magnitude;
        anim.SetFloat("Speed",       speed, 0.08f, Time.deltaTime);
        anim.SetFloat("MotionSpeed", speed > 0.1f ? 1f : 0f, 0.08f, Time.deltaTime);
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
        var p = Application.isPlaying ? Profile
                : overrideBehaviour ? localBehaviour
                : GetComponent<Character>()?.data?.behaviour?.settings;
        if (p == null) return;

        Vector3 pos = Application.isPlaying ? spawnPosition : transform.position;

        Gizmos.color = Color.yellow;
        var data = GetComponent<Character>()?.data;
        if (data != null)
            foreach (var a in data.attacks)
                if (a != null) Gizmos.DrawWireSphere(transform.position, a.maxRange);

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
