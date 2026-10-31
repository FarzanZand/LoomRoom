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
//             player's hits never interrupt a swing; a heavy hit's stagger waits for it to end.
//   Recover — a short pause after the swing (or a stagger), still turning toward the target.
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
    public EnemyWeaponLoadout Loadout { get; private set; }
    // EnemyData.archer: shoots from range and keeps its distance.
    public bool IsArcher => Data is { archer: true };

    // (previous, next). Voices bark on the switch into combat; bosses and logs listen too.
    public event Action<EnemyState, EnemyState> StateChanged;
    // Chasing or attacking: it knows where the player is. Anything else can be backstabbed.
    public bool IsAlerted => State == EnemyState.Chase || State == EnemyState.Attack;
    // Never caught unaware (a boss set down on the stage, watching the player from the start): no backstab while idle.
    public bool Wary { get; set; }
    // Entranced (a skeleton party's dancers): it notices nothing and stays where it is until Provoke.
    public bool Entranced { get; set; }

    // Out of a trance and after the player (or at least to where it happened).
    // Its next attack is this spell of its own (EnemyData.spells), ready or not: how a fight opens.
    public void OpenWith(EnemySpell spell)
    {
        attackRunner ??= new EnemyAttackRunner(this);
        attackRunner.Opener = spell != null && spell.spell != null ? spell.Attack : null;
    }

    public void Provoke(Vector3 at)
    {
        Entranced = false;
        Alert();
        if (State != EnemyState.Chase && State != EnemyState.Attack && State != EnemyState.Dead) StartInvestigate(at);
    }
    // Backing away from its target into something it can't get past, just now (HandleSpacing).
    public bool Cornered => Time.time < corneredUntil;
    float corneredUntil = -1f, backStuckSince = -1f;

    // Lying asleep (a sleeping pack, EnemySleep): it neither sees nor moves until woken, and every hit on it is a
    // backstab. The HumanoidController lies down (its Asleep bool); other rigs just slow their idle.
    public bool Asleep { get; private set; }
    public void Sleep()
    {
        if (State == EnemyState.Dead || !Character.IsAlive) return;
        Asleep = true;
        Motor.Stop();
        SetSleepAnimation(true);
    }

    // Up and fighting: it stands still for riseSeconds (getting up), then hunts whoever woke it.
    public void Wake(float riseSeconds)
    {
        if (!Asleep || NetworkPuppet) return;
        Asleep = false;
        SetSleepAnimation(false);
        BeginRecovery(riseSeconds);
        Alert();
    }

    void SetSleepAnimation(bool asleep)
    {
        var anim = Character.Animator;
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, "Asleep", AnimatorControllerParameterType.Bool)) anim.SetBool("Asleep", asleep);
        else anim.speed = asleep ? .2f : 1f;
    }

    // Force the enemy to hunt the target now (a boss waking, a grave's occupant rising).
    public void Alert()
    {
        if (State == EnemyState.Dead || !Character.IsAlive || NetworkPuppet) return;
        wasProvoked = true;
        Perception.Tick(true);
        if (Perception.Target != null) EnterCombat();
    }

    // The enemy's own data (stored inside its prefab). Null if the Character holds another kind.
    public EnemyData Data
    {
        get
        {
            var character = Character != null ? Character : GetComponent<Character>();
            return character != null ? character.data as EnemyData : null;
        }
    }

    public EnemyBehaviourSettings Profile =>
        overrideBehaviour ? localBehaviour
        : Data != null ? Data.Behaviour
        : FallbackProfile;

    static EnemyBehaviourSettings fallback;
    public void SetBehaviourOverride(EnemyBehaviourSettings settings)
    {
        localBehaviour.CopyFrom(settings);
        overrideBehaviour = true;
        if (Perception != null) Perception.Profile = localBehaviour;
    }
    static EnemyBehaviourSettings FallbackProfile => fallback ??= new EnemyBehaviourSettings();

    // Editor only: when the override is switched on, start from the shared profile's values.
    void OnOverrideToggled()
    {
        if (!overrideBehaviour) return;
        var data = Data;
        if (data != null) localBehaviour.CopyFrom(data.Behaviour);
    }

    Dictionary<EnemyState, Action> handlers;
    Vector3 spawnPosition;
    int     waypointIndex;
    float   wanderTimer;
    float   loseSightTimer;
    float   giveUpTimer = -1f;
    float   investigateTimer;
    bool    wasProvoked;
    bool    flies;
    Vector3 investigatePoint;

    // Spacing
    int   circleDir = 1;
    float circleSwitchAt;
    float circleStuckSince = -1f;

    // Attacks
    EnemyAttackRunner attackRunner;
    float recoveryUntil, staggerImmuneUntil;
    // Footwork: the step back after a swing, and this enemy's own rhythm of stepping in and back while it circles.
    float stepBackUntil, swayPeriod = -1f, swayPhase;
    bool  simulationPaused;
    CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;
    float ChaseSpeed => Profile.useChaseSpeed ? Profile.chaseSpeed : Motor.DefaultSpeed;
    // Footwork walks at the walk clips' own pace, slower for a smaller body (its legs are shorter).
    float FootworkPace => Profile.footworkPace * Mathf.Max(.3f, transform.lossyScale.y);
    public bool CanOpenHitbox => attackRunner != null && attackRunner.CanOpenHitbox;
    public bool IsSwinging    => attackRunner != null && attackRunner.IsSwinging;
    // When the current swing's blow lands (Time.time), or -1 while unknown or not swinging.
    // Returns true while it is steering the enemy itself (this frame the brain doesn't move it).
    public System.Func<bool> MovementOverride { get; set; }
    public float SwingHitAt   => attackRunner != null && attackRunner.IsSwinging ? attackRunner.HitAt : -1f;
    internal WeaponAnimationRelay AttackRelay => attackRelay;

    // Stand still (a flinch, a stagger, the pause after a swing); only ever extends.
    internal void BeginRecovery(float seconds) => recoveryUntil = Mathf.Max(recoveryUntil, Time.time + seconds);

    // The pause after its own swing: the first part of it a step back (Profile.stepBackAfterSwing), then it stands.
    internal void BeginSwingRecovery(float seconds)
    {
        BeginRecovery(seconds);
        if (Profile.stepBackAfterSwing > 0f) stepBackUntil = Time.time + Mathf.Min(Profile.stepBackAfterSwing, seconds);
    }

    // A shield bash: the swing in progress is dropped (it still counts as used) and the enemy stands stunned, then
    // can't be staggered again for CombatManager.enemyStaggerImmunity.
    public void Stun(float seconds)
    {
        if (State == EnemyState.Dead || seconds <= 0f) return;
        // Co-op guest: the host stuns the real enemy; the stun rides along with the strike that follows.
        if (NetworkPuppet) { PendingNetworkStun = Mathf.Max(PendingNetworkStun, seconds); return; }
        if (IsSwinging) { attackRunner.CancelForPause(); SetState(EnemyState.Chase); }
        BeginRecovery(seconds);
        staggerImmuneUntil = Mathf.Max(staggerImmuneUntil, Time.time + seconds + (Tuning != null ? Tuning.enemyStaggerImmunity : 0f));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake() => Initialize();

    void Initialize()
    {
        swayPhase = UnityEngine.Random.value;
        Character  = GetComponent<Character>();
        Motor      = GetComponent<EnemyMotor>();
        Perception = GetComponent<Perception>();
        Loadout    = GetComponent<EnemyWeaponLoadout>();
        flies      = GetComponent<FlyingEnemy>() != null;
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

    // Enabled brains, for cheap scans (stealth training, the seen indicator).
    public static readonly List<EnemyBrain> Active = new();

    // Enter Play Mode without a domain reload keeps statics: drop last session's brains.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();

    void OnEnable()
    {
        // Unity can reload scripts while retaining scene objects; nonserialized handlers
        // are then lost even though Awake will not run again on those objects.
        if (handlers == null) Initialize();
        Character.Damaged += OnDamaged;
        Character.Died    += OnDied;
        Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
        Character.Damaged -= OnDamaged;
        Character.Died    -= OnDied;
        CancelAttack();
    }

    void Start()
    {
        if (Data == null) Debug.LogWarning($"{name}: EnemyBrain needs EnemyData on its Character (has {(Character.data != null ? Character.data.GetType().Name : "none")}).", this);
        // Alerted or provoked in the frame it was put down (the wrathful merchant, the intro's killer): already fighting.
        if (State == EnemyState.Chase || State == EnemyState.Attack || State == EnemyState.Dead) return;
        SetState(Profile.defaultState);
    }

    // The data's melee attacks, plus the bow shot for archers and the spells for casters (that list is built once per frame).
    internal List<EnemyAttack> Attacks
    {
        get
        {
            var data = Data;
            if (data == null) return emptyAttacks;
            bool casts = data.caster && data.spells != null && data.spells.Count > 0;
            if (!data.archer && !casts) return data.attacks;
            if (withBowFrame != Time.frameCount || withBowData != data)
            {
                withBowFrame = Time.frameCount;
                withBowData = data;
                withBow.Clear();
                withBow.AddRange(data.attacks);
                if (data.archer) withBow.Add(data.archery.Shot);
                if (casts) foreach (var s in data.spells) if (s != null && s.spell != null) withBow.Add(s.Attack);
            }
            return withBow;
        }
    }
    static readonly List<EnemyAttack> emptyAttacks = new();
    readonly List<EnemyAttack> withBow = new();
    int withBowFrame = -1;
    EnemyData withBowData;

    float MaxAttackRange
    {
        get
        {
            float r = 0f;
            foreach (var a in Attacks) if (a != null) r = Mathf.Max(r, a.EffectiveMaxRange);
            return r > 0f ? r : 1.8f;
        }
    }

    float PreferredDistance => (CloseIn ? MeleeRange : MaxAttackRange) * Profile.preferredRangeFraction;

    // A caster that fights up close (EnemyData.closesIn) holds the distance of its melee attacks, not its spells'.
    bool CloseIn => Data != null && Data.caster && Data.closesIn && Data.attacks != null && Data.attacks.Count > 0;
    float MeleeRange
    {
        get
        {
            float r = 0f;
            foreach (var a in Data.attacks) if (a != null) r = Mathf.Max(r, a.EffectiveMaxRange);
            return r > 0f ? r : 1.8f;
        }
    }

    bool AnyAttackCoversDistance(float dist)
    {
        foreach (var a in Attacks) if (a != null && dist >= a.minRange && dist <= a.EffectiveMaxRange) return true;
        return false;
    }

    // Co-op guests: the host runs this enemy; here it only shows what the host's snapshots say (CoopEnemyPuppet).
    public bool NetworkPuppet { get; set; }
    public float PendingNetworkStun { get; set; }

    // The host's state and sleep for a puppet, without anything a change here would start (voices still hear it).
    public void ApplyNetworkState(EnemyState state, bool asleep)
    {
        if (State == EnemyState.Dead) return;
        if (Asleep != asleep) { Asleep = asleep; SetSleepAnimation(asleep); }
        if (state != State && state != EnemyState.Dead)
        {
            var previous = State;
            State = state;
            StateChanged?.Invoke(previous, state);
        }
    }

    // In co-op the world keeps going while one player has a menu open; only a load (Cutscene) holds it.
    bool SimulationPaused => GameManager.HasInstance && (Coop.Running ? GameManager.Instance.State == GameState.Cutscene : !GameManager.Instance.SimulationActive);

    void Update()
    {
        if (State == EnemyState.Dead || NetworkPuppet) return;
        if (SimulationPaused)
        {
            // Once per pause: a swing in progress is dropped (and its cooldown starts), the enemy stands still.
            if (!simulationPaused)
            {
                simulationPaused = true;
                attackRunner?.CancelForPause();
                Motor.Stop();
                Motor.ClearLookTarget();
            }
            return;
        }
        simulationPaused = false;

        if (Asleep) { Motor.Stop(); UpdateAnimator(); return; }

        Perception.Profile = Profile;
        Perception.Tick(State == EnemyState.Chase || State == EnemyState.Attack);
        Motor.SuppressKnockback = IsSwinging;
        attackRunner.Tick();

        // Another component steering for a moment (FlyingEnemy pulling back after a dive): it moves, the brain waits.
        if (MovementOverride != null && !IsSwinging && MovementOverride()) { UpdateAnimator(); return; }

        // Recovering or staggered: stand still, even inside the target's personal space.
        if (Time.time < recoveryUntil)
        {
            // Catch breath, but keep the eyes on the target so the next move is instant.
            // A committed swing never tracks. Just after its own swing it steps back first.
            // Only from a target it can see: a veiled player must not be followed by its step back or its eyes.
            if (Time.time < stepBackUntil && Perception.Target != null && Perception.TargetVisible && State != EnemyState.Attack)
            {
                Vector3 back = transform.position - Perception.Target.transform.position; back.y = 0f;
                float pace = Profile.spacingSway > 0f ? FootworkPace : ChaseSpeed * Profile.stepBackSpeedFraction;
                // With footwork it steps back diagonally, toward the side it circles to next: the leg blend then turns
                // from the back-step into the side-step instead of swinging through a straight back-walk (a leg kicked up).
                if (Profile.spacingSway > 0f && back.sqrMagnitude > .0001f)
                    back = back.normalized * .6f + Vector3.Cross(Vector3.up, -back.normalized) * circleDir * .8f;
                if (back.sqrMagnitude > .0001f) Motor.Strafe(back.normalized, pace);
                else Motor.Stop();
            }
            else Motor.Stop();
            if (Perception.Target != null && Perception.TargetVisible && State != EnemyState.Attack)
                Motor.LookAt(Perception.Target.transform.position, Profile.attackFaceSpeed);
            UpdateAnimator();
            return;
        }

        // Personal space: an enemy that is not swinging never stands inside its target; it steps back out.
        if (State == EnemyState.Chase && !IsSwinging && Perception.Target != null && Perception.TargetVisible && Tuning != null)
        {
            Vector3 away = transform.position - Perception.Target.transform.position; away.y = 0f;
            if (away.magnitude < Tuning.personalSpace && away.sqrMagnitude > .0001f)
            {
                Motor.Strafe(away.normalized, ChaseSpeed * Profile.spacingSpeedFraction);
                Motor.LookAt(Perception.Target.transform.position, Profile.attackFaceSpeed);
                UpdateAnimator();
                return;
            }
        }

        if (handlers.TryGetValue(State, out var handler)) handler?.Invoke();

        UpdateAnimator();
    }

    // ── Events ────────────────────────────────────────────────────────

    void OnDamaged(DamageInfo info)
    {
        if (State == EnemyState.Dead || !Character.IsAlive) return;
        // A puppet only flinches; what it does about the hit is the host's.
        if (NetworkPuppet)
        {
            if (!info.Blocked && info.Amount > 0f && Character.data != null) Character.TriggerAnimation(Character.data.hurtTrigger);
            return;
        }
        if (Asleep) Wake(.6f);
        Perception.NoCloseAwareness = false;   // a wall-facing guard snaps out of it
        if (info.Source != null) { wasProvoked = true; Perception.NotifyAttackedFrom(info.Source.transform.position); }

        // Flinch only when not mid-swing: a swing is never interrupted by the player's hits, unless
        // CombatManager.hitsInterruptWindup is on and the hit lands early in the windup.
        // A heavy stagger taken mid-swing is applied when the swing ends.
        if (IsSwinging && !info.Blocked && info.Amount > 0f && attackRunner.TryInterrupt())
            SetState(EnemyState.Chase);
        // A stagger is followed by a moment it can't be staggered again (CombatManager.enemyStaggerImmunity).
        // A mace's heavy hit always staggers, even inside the immunity after the last stagger.
        bool maceHeavy = info.Weapon != null && info.Weapon.IsWeapon && info.Weapon.weaponCategory == WeaponCategory.Mace;
        if (info.Heavy && !info.Blocked && info.Amount > 0f && Tuning != null && (Time.time >= staggerImmuneUntil || maceHeavy))
        {
            var trainee = info.Source != null ? info.Source.Adventurer : null;
            float duration = Tuning.heavyStaggerDuration * Tuning.StaggerStrengthFor(info.Weapon) * (trainee != null ? trainee.StaggerMultiplier(info.Weapon) : 1);
            if (IsSwinging) attackRunner.QueueStagger(duration);
            else            BeginRecovery(duration);
            staggerImmuneUntil = Time.time + duration + Tuning.enemyStaggerImmunity;
        }
        // A weapon type with no Stagger Strength makes no hurt reaction either.
        if (!IsSwinging && !info.Blocked && info.Amount > 0f && Character.data != null && (Tuning == null || Tuning.StaggerStrengthFor(info.Weapon) > 0f))
        {
            // Which stagger plays (HumanoidController: Hurt, Hurt Left, Hurt Right, Hurt Heavy): the way the blow throws
            // the body, or the heavy one.
            AnimatorHelper.SetInteger(Character.Animator, "HurtDir", HurtDirection(info));
            Character.TriggerAnimation(Character.data.hurtTrigger);
            if (!info.Heavy && Tuning != null) BeginRecovery(Tuning.lightFlinchDuration * Tuning.StaggerStrengthFor(info.Weapon));
        }

        bool inCombat = State == EnemyState.Chase || State == EnemyState.Attack;
        if (inCombat || Entranced) return;

        if (Profile.aggressionMode == AggressionMode.AggressiveWhenHit && Perception.Target != null)
            EnterCombat();
        else if (Profile.aggressionMode != AggressionMode.Passive)
            // A hit with no attacker (spikes, a fall) has nothing to follow: look where it hurt, not at an old noise.
            StartInvestigate(info.Source != null && Perception.HasStimulus ? Perception.StimulusPosition : info.HitPoint != Vector3.zero ? info.HitPoint : transform.position);
    }

    // 0 straight back, 1 thrown to its left, 2 thrown to its right, 3 heavy.
    int HurtDirection(DamageInfo info)
    {
        if (info.Heavy) return 3;
        var throwDir = Tuning != null ? Tuning.ImpactDirection(info) : info.Direction;
        float side = Vector3.Dot(throwDir, transform.right);
        return Mathf.Abs(side) < .35f ? 0 : side > 0f ? 2 : 1;
    }

    void CancelAttack() => attackRunner?.Cancel();

    void OnDied()
    {
        // Killed in its sleep (the first blow was enough): no more snoring, and a slowed rig plays its death at speed.
        // A humanoid keeps its Asleep pose; the ragdoll takes it from there.
        if (Asleep)
        {
            Asleep = false;
            var anim = Character.Animator;
            if (anim != null && anim.runtimeAnimatorController != null && !Character.HasParameter(anim, "Asleep", AnimatorControllerParameterType.Bool)) anim.speed = 1f;
        }
        CancelAttack();
        Motor.Stop();
        Motor.ClearLookTarget();
        SetState(EnemyState.Dead);
        SetHoldingBow(false);
        if (Motor.Agent != null) Motor.Agent.enabled = false;
        // Its walking colliders go, but not the body the ragdoll may already have made of it: this runs after the ragdoll's
        // own death once the brain was switched off and on again (a flick's hold, the Dungeon Master setting it down), and
        // a rat whose box went with them fell through the floor.
        if (!(TryGetComponent<EnemyRagdoll>(out var ragdoll) && ragdoll.IsRagdolled))
            foreach (var col in GetComponents<Collider>()) col.enabled = false;
        // Bodies stay for the floor: they stop listening at once, and stop moving once a knockback has played out.
        if (Perception != null) Perception.enabled = false;
        Invoke(nameof(StillBody), 1f);
    }

    void StillBody() { if (Motor != null && !Character.IsAlive) Motor.enabled = false; }

    // ── Passive states ────────────────────────────────────────────────

    bool TryNoticeTarget()
    {
        if (Entranced) return true;
        if (CanEnterCombat() && Perception.TargetVisible) { EnterCombat(); return true; }
        // Only an aggressive enemy goes to look at a noise: one that waits to be hit (or never fights) stays where it is.
        if (Perception.HasStimulus && Profile.aggressionMode == AggressionMode.Aggressive)
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
        Perception.NoCloseAwareness = false;   // a wall-facing guard that has found the player stays aware of them
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
            // Lost sight: run to the last known spot, search a bit, then give up. A veiled target has vanished on the
            // spot (that spot is where it still stands): the enemy stops where it is and looks round instead, so the
            // player can slip behind it (the user, 2026-10-30).
            Motor.ClearLookTarget();
            loseSightTimer -= Time.deltaTime;
            if (ShadowVeil.IsVeiled(target)) Motor.Stop();
            else Motor.MoveTo(Perception.LastKnownTargetPosition);
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
            float height = Mathf.Abs(targetPos.y - transform.position.y);
            bool clear = attack.IsRanged
                ? height <= (attack.IsBowShot ? attack.bowShot.maxHeightDifference : attack.spellCast.maxHeightDifference) && attackRunner.HasArrowLineOfSight(target)
                : height <= 1f && (Tuning == null || Tuning.HasMeleeLineOfSight(Character, target, targetPos + Vector3.up * 0.9f));
            if (Motor.IsFacing(targetPos, attack.facingAngle) && clear)
                attackRunner.StartAttack(attack);
            else if (attack.IsRanged || !HandleHeightGap(targetPos, dist)) HandleSpacing(targetPos, dist);
            return;
        }

        // A fighter who also casts (closesIn) only holds its ground in melee range: a spell that could reach from here
        // but is recharging is no reason to stand and wait. It casts on the way in whenever one is ready (PickAttack).
        if (CloseIn ? dist <= PreferredDistance : AnyAttackCoversDistance(dist) || dist <= PreferredDistance)
        {
            if (!HandleHeightGap(targetPos, dist)) HandleSpacing(targetPos, dist);
            return;
        }

        // Out of reach up a ledge: the way round, or wait off to the side (else the walk in and the backing off take turns).
        if (HandleHeightGap(targetPos, dist)) return;

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
            Vector3 toTarget = targetPos - transform.position; toTarget.y = 0f;
            Motor.Strafe(toTarget, ChaseSpeed);
        }
        if (dist < MaxAttackRange * 2.5f) Motor.LookAt(targetPos, p.attackFaceSpeed);
        else Motor.ClearLookTarget();
    }

    // In range, attack recharging: hold the preferred distance and circle, always facing.
    void HandleSpacing(Vector3 targetPos, float dist)
    {
        var p = Profile;
        Motor.LookAt(targetPos, p.attackFaceSpeed);

        float chaseSpeed = ChaseSpeed;
        float preferred  = PreferredDistance;
        // Footwork: the held distance swells in and out on this enemy's own rhythm, so it steps in and back as it circles
        // (never into the target's personal space, never out of its reach).
        if (p.spacingSway > 0f)
        {
            if (swayPeriod < 0f) swayPeriod = UnityEngine.Random.Range(p.spacingSwayPeriod.x, Mathf.Max(p.spacingSwayPeriod.x, p.spacingSwayPeriod.y));
            // Mostly outward: it steps back to a little past its distance and in only a little short of it.
            float wave = Mathf.Sin((Time.time / Mathf.Max(.5f, swayPeriod) + swayPhase) * Mathf.PI * 2f);
            float sway = p.spacingSway * (wave > 0f ? wave : wave * .3f);
            float least = Tuning != null ? Tuning.personalSpace + .35f : .9f;
            preferred = Mathf.Clamp(preferred + sway, least, Mathf.Max(least, MaxAttackRange - .1f));
        }
        Vector3 toTarget = targetPos - transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) toTarget = transform.forward;
        toTarget.Normalize();

        // Footwork: near its distance it walks, never darts: the circling and the step in or back are one movement at
        // the walk's own pace (the legs whirled when it ran sideways and burst in and out between circling).
        if (p.spacingSway > 0f && p.circleTarget && Mathf.Abs(dist - preferred) < .7f)
        {
            backStuckSince = -1f;
            if (Time.time >= circleSwitchAt)
            {
                circleDir = UnityEngine.Random.value < 0.5f ? -1 : 1;
                circleSwitchAt = Time.time + UnityEngine.Random.Range(p.circleSwitchInterval.x, p.circleSwitchInterval.y);
            }
            float pace = FootworkPace;
            Vector3 around = Vector3.Cross(Vector3.up, toTarget) * circleDir * pace;
            Vector3 inOut = toTarget * Mathf.Clamp((dist - preferred) * 1.6f, -.6f, .6f) * pace;
            Vector3 step = around + inOut;
            Motor.Strafe(step.normalized, Mathf.Min(step.magnitude, pace * 1.15f));
            float went = new Vector3(Motor.Velocity.x, 0f, Motor.Velocity.z).magnitude;
            if (went < pace * .25f)
            {
                if (circleStuckSince < 0f) circleStuckSince = Time.time;
                else if (Time.time - circleStuckSince > .35f) { circleDir = -circleDir; circleStuckSince = -1f; }
            }
            else circleStuckSince = -1f;
            return;
        }

        if (dist > preferred + 0.2f)
        {
            Motor.Strafe(toTarget, chaseSpeed * p.spacingSpeedFraction);
            return;
        }
        if (dist < preferred - 0.35f)
        {
            Motor.Strafe(-toTarget, chaseSpeed * p.spacingSpeedFraction);
            // Backing off but not getting anywhere (a wall behind it, a small room): cornered for a moment, so its
            // ranged attacks may be used up close (EnemyAttackRunner.Usable).
            float moving = new Vector3(Motor.Velocity.x, 0f, Motor.Velocity.z).magnitude;
            if (moving < chaseSpeed * p.spacingSpeedFraction * .25f)
            {
                if (backStuckSince < 0f) backStuckSince = Time.time;
                else if (Time.time - backStuckSince > .4f) corneredUntil = Time.time + 1.5f;
            }
            else backStuckSince = -1f;
            return;
        }
        backStuckSince = -1f;

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

    // The target stands too high or low for a melee blow (a ledge, a stair landing). If the NavMesh leads up to it,
    // walk the way round (up the stairs) instead of circling underneath; if nothing does, back off out of its reach
    // and wait, facing it (closer than LedgeStandOff it steps back, within LedgeHoldBand past that it stands, farther off
    // it walks in as usual). Flyers, archers and casters keep the plain spacing, and a player in the air (a jump) is no
    // ledge. False when this doesn't apply.
    const float MeleeHeightReach = 1f, LedgeStandOff = 3f, LedgeHoldBand = 1.5f;
    float reachCheckedAt = -1f;
    bool targetReachable;
    Vector3 reachPoint;
    NavMeshPath reachPath;

    bool HandleHeightGap(Vector3 targetPos, float dist)
    {
        if (Mathf.Abs(targetPos.y - transform.position.y) <= MeleeHeightReach) return false;
        var data = Data;
        if (data == null || data.archer || data.caster || flies || Motor.Agent == null || !Motor.Agent.isOnNavMesh) return false;
        if (Perception.Target is Player jumper && jumper.Motor != null && !jumper.Motor.IsGrounded) return false;

        // A path query is costly: checked a few times a second.
        if (Time.time - reachCheckedAt > .5f)
        {
            reachCheckedAt = Time.time;
            targetReachable = false;
            var filter = new NavMeshQueryFilter { agentTypeID = Motor.Agent.agentTypeID, areaMask = Motor.Agent.areaMask };
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1.5f, filter) && Mathf.Abs(hit.position.y - targetPos.y) <= MeleeHeightReach)
            {
                reachPath ??= new NavMeshPath();
                if (NavMesh.CalculatePath(transform.position, hit.position, filter, reachPath) && reachPath.status == NavMeshPathStatus.PathComplete)
                {
                    targetReachable = true;
                    reachPoint = hit.position;
                }
            }
        }

        var p = Profile;
        if (targetReachable)
        {
            Motor.SetAutoBraking(false);
            Motor.SetStoppingDistance(Mathf.Min(PreferredDistance, .5f));
            Motor.MoveTo(reachPoint);
            if (dist < MaxAttackRange * 2.5f) Motor.LookAt(targetPos, p.attackFaceSpeed);
            else Motor.ClearLookTarget();
            return true;
        }

        if (dist >= LedgeStandOff + LedgeHoldBand) return false;
        Motor.LookAt(targetPos, p.attackFaceSpeed);
        if (dist < LedgeStandOff)
        {
            Vector3 away = transform.position - targetPos; away.y = 0f;
            if (away.sqrMagnitude < .0001f) away = -transform.forward;
            Motor.Strafe(away, ChaseSpeed * p.spacingSpeedFraction);
        }
        else Motor.Stop();
        return true;
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
        var previous = State;
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
        if (previous != newState) StateChanged?.Invoke(previous, newState);
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
        // The bow goes back to the side as soon as the arrow is away, while the release still plays: one blend, not the
        // release blending to the idle arms and then the bow hold blending in a moment later (the arm swung twice).
        SetHoldingBow(IsArcher && (State != EnemyState.Attack || attackRunner != null && attackRunner.ArrowLoosed));
        // The leg blend eases (a quick one flipped the legs between clips at every change of step).
        Motor.UpdateLocomotionAnimator(anim, true, 0.15f);
    }

    // Left-arm bow carry pose (HumanoidController, LeftArm layer), dropped for the draw and on death.
    void SetHoldingBow(bool holding)
    {
        var anim = Character.Animator;
        if (anim != null && anim.runtimeAnimatorController != null && Character.HasParameter(anim, "HoldingBow", AnimatorControllerParameterType.Bool))
            anim.SetBool("HoldingBow", holding);
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
                : Data != null ? Data.Behaviour : null;
        if (p == null) return;

        Vector3 pos = Application.isPlaying ? spawnPosition : transform.position;

        Gizmos.color = Color.yellow;
        var data = Data;
        if (data != null)
            foreach (var a in data.attacks)
                if (a != null) Gizmos.DrawWireSphere(transform.position, a.EffectiveMaxRange);
        if (data != null && data.archer)
        {
            Gizmos.color = new Color(1f, .5f, .2f);
            Gizmos.DrawWireSphere(transform.position, data.archery.maxRange);
        }

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
