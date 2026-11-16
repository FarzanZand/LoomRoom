using UnityEngine;
using UnityEngine.AI;

// NavMeshAgent wrapper for enemies and NPCs. The motor owns rotation: the agent's
// own turning is disabled because it turns proportionally to speed, which is what
// made enemies pivot in slow motion when standing still. Here a character can move
// one way and look another (strafing), turn fast in place, and slide from knockback.
[RequireComponent(typeof(NavMeshAgent))]
[DefaultExecutionOrder(50)]
public class EnemyMotor : MonoBehaviour, IKnockbackReceiver
{
    [Header("Ground Check")]
    [SerializeField] float     groundCheckDistance = 0.2f;
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Turning")]
    [Tooltip("Degrees per second when turning toward the movement direction (no look target set).")]
    [SerializeField] float moveTurnSpeed = 540f;

    public NavMeshAgent Agent { get; private set; }
    public bool  IsGrounded    { get; private set; }
    public float DefaultSpeed  { get; private set; }
    // Actual movement this frame, whether it came from a path or from Strafe().
    public Vector3 Velocity    { get; private set; }
    public bool  HasPath       => Agent != null && Agent.hasPath && !Agent.isStopped;
    // A MoveTo is still wanted: set by MoveTo (even mid-knockback), cleared by Stop and Strafe.
    public bool  HasDestination { get; private set; }
    public Vector3 Destination  { get; private set; }

    Vector3 knockbackVelocity;
    float   knockbackTimer;
    Vector3 lastPosition;
    bool hasVelocitySample;
    bool    hasLookTarget;
    Vector3 lookTarget;
    float   lookSpeed;
    float movementSpeed;
    bool NavigationReady => Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh;

    void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        DefaultSpeed = Agent.speed;
        movementSpeed = DefaultSpeed;
        Agent.updateRotation = false;
        lastPosition = transform.position;
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        Velocity = Vector3.zero;
        hasVelocitySample = false;
    }

    void Update()
    {
        UpdateGroundCheck();
        if (knockbackTimer > 0f) TickKnockback();
        UpdateRotation();
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        // Establish the baseline after startup placement and NavMesh alignment.
        Velocity = hasVelocitySample && dt > 0f
            ? (transform.position - lastPosition) / dt : Vector3.zero;
        // What this frame's steps into a swing moved it at (Step), for LegVelocity.
        stepVelocity = dt > 0f ? stepDelta / dt : Vector3.zero;
        stepDelta = Vector3.zero;
        lastPosition = transform.position;
        hasVelocitySample = true;
    }

    void UpdateGroundCheck()
    {
        bool rayHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
                                      groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
        IsGrounded = rayHit || (Agent != null && Agent.isOnNavMesh);
    }

    void UpdateRotation()
    {
        Vector3 dir;
        float speed;
        if (hasLookTarget)
        {
            dir = lookTarget - transform.position;
            speed = lookSpeed;
        }
        else
        {
            dir = Agent != null && Agent.isOnNavMesh && !Agent.isStopped ? Agent.desiredVelocity : Vector3.zero;
            speed = moveTurnSpeed;
        }
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), speed * Time.deltaTime);
    }

    // ── Movement ──────────────────────────────────────────────────────

    // Re-issuing a destination this close to the current one, with a path already running, is skipped:
    // chasing calls MoveTo every frame and each SetDestination costs a path query.
    const float RepathDistance = .25f;

    public void MoveTo(Vector3 destination)
    {
        // Remembered even when it can't be issued yet, so the end of a knockback resumes it.
        Destination = destination;
        HasDestination = true;
        if (!NavigationReady) return;
        Agent.speed = movementSpeed * SpeedScale;
        Agent.isStopped = false;
        // Compared flat: the agent's destination is the point snapped onto the navmesh.
        if ((Agent.hasPath || Agent.pathPending) && Perception.HorizontalDist(Agent.destination, destination) < RepathDistance) return;
        Agent.SetDestination(destination);
    }

    // A short local path preserves agent avoidance while moving independently of facing.
    public void Strafe(Vector3 worldDirection, float speed)
    {
        HasDestination = false;
        if (!NavigationReady) return;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f || speed <= 0f) { Stop(); return; }
        Vector3 destination = transform.position + worldDirection.normalized * Mathf.Max(0.6f, speed * 0.35f);
        Agent.speed = speed * SpeedScale;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking = false;
        Agent.isStopped = false;
        // Called every frame while strafing: keep the running path until the wanted point has moved off it.
        if (Agent.hasPath && !Agent.pathPending && Agent.remainingDistance > .2f && Perception.HorizontalDist(Agent.destination, destination) < RepathDistance) return;
        if (Agent.Raycast(destination, out var edge)) destination = edge.position;
        Agent.SetDestination(destination);
    }

    public void Stop()
    {
        HasDestination = false;
        if (Agent == null || !Agent.isOnNavMesh) return;
        if (Agent.hasPath) Agent.ResetPath();
        Agent.isStopped = true;
        Agent.velocity  = Vector3.zero;
    }

    public void Resume()
    {
        if (Agent == null || !Agent.isOnNavMesh) return;
        Agent.isStopped = false;
    }

    public bool ReachedDestination(float extra = 0.1f)
    {
        if (Agent == null || !Agent.isOnNavMesh || Agent.pathPending) return false;
        return Agent.remainingDistance <= Agent.stoppingDistance + extra;
    }

    public bool IsNear(Vector3 point, float extra = 0.1f) =>
        Perception.HorizontalDist(transform.position, point) <= (Agent != null ? Agent.stoppingDistance : 0f) + extra;

    // ── Wander / patrol (shared by EnemyBrain and NpcBrain; the brains own the settings) ──

    // Counts the idle timer down, then walks to a random navmesh point around center.
    public void WanderStep(ref float timer, Vector3 center, float minDistance, float radius, float zoneRadius,
                           float minIdle, float maxIdle)
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        Vector3 dir = Random.insideUnitSphere; dir.y = 0f; dir.Normalize();
        Vector3 target = center + dir * Random.Range(minDistance, radius);
        if (zoneRadius > 0f)
        {
            Vector3 offset = target - center; offset.y = 0f;
            if (offset.magnitude > zoneRadius) target = center + offset.normalized * zoneRadius;
        }
        if (Agent != null)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = Agent.agentTypeID, areaMask = Agent.areaMask };
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, radius, filter))
                MoveTo(hit.position);
        }
        timer = Random.Range(minIdle, maxIdle);
    }

    // Advances to the next waypoint once the current one is reached.
    public void PatrolStep(Transform[] waypoints, ref int index, bool loop)
    {
        if (waypoints == null || waypoints.Length == 0) return;
        index = Mathf.Clamp(index, 0, waypoints.Length - 1);
        // Stopped (menu, stagger, dialogue): resume the current waypoint instead of skipping it.
        if (!HasDestination && waypoints[index] != null) { MoveTo(waypoints[index].position); return; }
        if (!ReachedDestination(0.4f)) return;
        index++;
        if (index >= waypoints.Length) index = loop ? 0 : waypoints.Length - 1;
        if (waypoints[index] != null) MoveTo(waypoints[index].position);
    }

    // ── Legs that match the body's speed ────────────────────────────
    // The locomotion blend (HumanoidController, Directional locomotion) has its walk clip at 0.9 m/s and its run clip at
    // 2.6 and was fed the body's speed in metres a second, whatever the body's size. But the clips cover ground by the
    // length of the legs that play them: for a Blue Hood Skeleton (leg 0.806 m, hip to knee to ankle) the blend covers
    // 0.76 m/s at 0.7, 1.8 at 0.9 and 4.3 from 2.6 up (measured with the foot pinning off, on the foot's speed under the
    // body while it is down). So a body walking at 1.5 m/s cycled its legs for 2.7, a big one (an elite, Little Undead
    // John, the Crypt Lord) ran its legs a third too fast at its chase speed, and a small one too slow; EnemyFootPlant
    // pins a foot that is down, so it showed as short quick steps or dragged ones, never as a slide.
    // Match Stride: the blend is fed the speed at which its clips cover what this body really moves, for this body's own
    // legs (a longer leg takes the same ground at a slower gait), and only past what the run clip covers is the blend
    // played faster (LocoRate, the locomotion state's speed, at most Leg Rate Most; a slow walk plays the walk clip a
    // little slower, see BlendFor). The speed it moves at, its attacks
    // and the guard's own footwork are not touched. Off: fed in metres a second, as before.
    public static bool MatchStride = true;
    public static float LegRateMost = 2f;
    // (A test may switch it off for a comparison; a new Play starts with it on.)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStride() { MatchStride = true; LegRateMost = 2f; }
    const float ReferenceLeg = .806f;
    // (the blend's forward parameter, what its clips cover there for the reference leg, m/s)
    static readonly (float fed, float covers)[] Stride = { (0f, 0f), (.3f, .16f), (.5f, .33f), (.7f, .76f), (.9f, 1.8f), (1.6f, 2.68f), (2.6f, 4.3f) };
    Animator legsOf; float legsAtScale, legLength;

    // Hip to knee to ankle, in the world (so with the body's scale); 0 for a rig without those bones.
    float LegLength(Animator anim)
    {
        float scale = anim.transform.lossyScale.y;
        if (legsOf == anim && Mathf.Approximately(scale, legsAtScale)) return legLength;
        legsOf = anim; legsAtScale = scale; legLength = 0f;
        if (!anim.isHuman) return 0f;
        var hip = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var knee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var ankle = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (hip != null && knee != null && ankle != null) legLength = Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, ankle.position);
        return legLength;
    }

    // What to feed the blend so that its clips cover `covers` m/s (for the reference leg), and the rate to play it at.
    // Past what the run clip covers it is played faster. Just under what the walk clip covers it is the walk clip played
    // a little slower (down to Slow Walk of its rate), not the walk blended with the idle pose: there a tenth more or
    // less fed changes what the legs cover by a third, and rigs differ by that much (the Synty bodies and the demon's
    // came out a quarter slow at a walk). Slower still, the walk is blended toward the idle pose as the tree does.
    const float SlowWalk = .7f;
    static float BlendFor(float covers, out float rate)
    {
        rate = 1f;
        const int walk = 4;   // Stride[walk]: the walk clip alone
        var top = Stride[Stride.Length - 1];
        if (covers >= top.covers) { rate = Mathf.Min(covers / top.covers, LegRateMost); return top.fed; }
        if (covers >= Stride[walk].covers)
        {
            for (int i = walk + 1; i < Stride.Length; i++)
                if (covers <= Stride[i].covers)
                    return Mathf.Lerp(Stride[i - 1].fed, Stride[i].fed, Mathf.InverseLerp(Stride[i - 1].covers, Stride[i].covers, covers));
            return top.fed;
        }
        float slowWalk = Stride[walk].covers * SlowWalk;
        if (covers >= slowWalk) { rate = covers / Stride[walk].covers; return Stride[walk].fed; }
        if (covers > Stride[walk - 1].covers)
        {
            float t = Mathf.InverseLerp(Stride[walk - 1].covers, slowWalk, covers);
            rate = Mathf.Lerp(1f, SlowWalk, t);
            return Mathf.Lerp(Stride[walk - 1].fed, Stride[walk].fed, t);
        }
        for (int i = 1; i < walk; i++)
            if (covers <= Stride[i].covers)
                return Mathf.Lerp(Stride[i - 1].fed, Stride[i].fed, Mathf.InverseLerp(Stride[i - 1].covers, Stride[i].covers, covers));
        return Stride[walk - 1].fed;
    }

    // Locomotion parameters for the humanoid body. Not locomoting forces an idle speed.
    public void UpdateLocomotionAnimator(Animator anim, bool locomoting, float damping)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        Vector3 v = LegVelocity; v.y = 0f;
        float speed = v.magnitude;
        if (locomoting) anim.SetFloat("Speed", speed, damping, Time.deltaTime);
        else            anim.SetFloat("Speed", 0f);
        // Local-space velocity for directional locomotion (strafing and backing off while facing a target), in the
        // blend's own terms for this body's legs (Match Stride).
        float feed = 1f, rate = 1f;
        if (MatchStride && locomoting && speed > .05f)
        {
            float leg = LegLength(anim);
            if (leg > .05f) feed = BlendFor(speed * ReferenceLeg / leg, out rate) / speed;
        }
        if (Character.HasParameter(anim, "MoveX", AnimatorControllerParameterType.Float))
        {
            Vector3 local = locomoting ? transform.InverseTransformDirection(v) * feed : Vector3.zero;
            anim.SetFloat("MoveX", local.x, damping, Time.deltaTime);
            anim.SetFloat("MoveZ", local.z, damping, Time.deltaTime);
        }
        if (Character.HasParameter(anim, "LocoRate", AnimatorControllerParameterType.Float)) anim.SetFloat("LocoRate", rate, damping, Time.deltaTime);
        anim.SetFloat("MotionSpeed", locomoting && speed > 0.1f ? 1f : 0f, damping, Time.deltaTime);
        anim.SetBool("Grounded", IsGrounded);
        anim.SetBool("FreeFall", !IsGrounded && Velocity.y < -1f);
    }

    public void SetSpeed(float speed)              { movementSpeed = speed; if (Agent != null) Agent.speed = speed * SpeedScale; }
    // Slowed (a Chill): every speed it moves at is scaled by this.
    public float SpeedScale { get; set; } = 1f;
    public void ResetSpeed()                       { SetSpeed(DefaultSpeed); }
    public void SetAngularSpeed(float speed)       { moveTurnSpeed = speed; }
    public void SetAcceleration(float accel)       { if (Agent != null) Agent.acceleration = accel; }
    public void SetStoppingDistance(float d)       { if (Agent != null) Agent.stoppingDistance = d; }
    public void SetAutoBraking(bool on)            { if (Agent != null) Agent.autoBraking = on; }

    // ── Facing ────────────────────────────────────────────────────────

    // Keep turning toward a point at this speed until cleared. Movement direction no
    // longer drives rotation while a look target is set (strafing).
    public void LookAt(Vector3 worldPoint, float degreesPerSecond)
    {
        hasLookTarget = true;
        lookTarget = worldPoint;
        lookSpeed = degreesPerSecond;
    }

    public void ClearLookTarget() => hasLookTarget = false;

    // One-frame turn step; prefer LookAt for continuous facing.
    public void Face(Vector3 worldPoint, float degreesPerSecond)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degreesPerSecond * Time.deltaTime);
    }

    public void SnapFace(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    public bool IsFacing(Vector3 worldPoint, float maxAngle) => AngleTo(worldPoint) <= maxAngle;

    public float AngleTo(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude < 0.001f ? 0f : Vector3.Angle(transform.forward, dir);
    }

    public void Warp(Vector3 position)
    {
        if (Agent != null && Agent.isOnNavMesh) Agent.Warp(position);
        else transform.position = position;
        lastPosition = transform.position;
        Velocity = Vector3.zero;
    }

    // ── Knockback ─────────────────────────────────────────────────────

    // Set by the brain while swinging: the player's hits never push an attacking enemy around.
    public bool SuppressKnockback { get; set; }
    // Bosses and elites (set where they are made: DungeonGenerator.SetUpEnemy, EliteEnemy.Make): never pushed, so a pit's
    // edge is a wall to them like any other; they reel harder instead (HitReactionController, CombatManager.steadfastRecoil).
    public bool Steadfast { get; set; }
    // The last blow that would have pushed it (its direction, when), kept even while a swing holds it still: a pit's edge
    // reads it, so a blow toward the drop tips a swinging enemy over too.
    public Vector3 LastShove { get; private set; }
    public float LastShoveTime { get; private set; } = -99f;
    public bool RecentlyShoved(float within) => Time.time - LastShoveTime <= within && LastShove.sqrMagnitude > .0001f;
    // Sliding from a hit right now (a pit edge reads it to tip the enemy over).
    public bool IsKnockedBack => knockbackTimer > 0f;
    public Vector3 KnockbackVelocity => knockbackVelocity;
    // What its legs walk at: its velocity without the slide a hit gives it (with the slide in, a pushed enemy's legs
    // stepped backwards at the push's speed, a moonwalk).
    public Vector3 LegVelocity => (knockbackTimer > 0f ? Velocity - knockbackVelocity : Velocity) - stepVelocity;

    // ── Stepping into a swing ─────────────────────────────────────────

    // A short move made while it stands and swings (EnemyAttackRunner.StepIntoSwing: after a target that backs away).
    // Like a knockback it slides along the navmesh and its legs don't walk it (LegVelocity leaves it out): the swing's
    // foot lock holds, and the feet step after the body (EnemyFootPlant). Returns the metres really moved.
    Vector3 stepDelta, stepVelocity;
    public bool IsStepping => stepVelocity.sqrMagnitude > .0001f || stepDelta.sqrMagnitude > 1e-8f;
    public float Step(Vector3 delta)
    {
        delta.y = 0f;
        if (!NavigationReady || delta.sqrMagnitude < 1e-8f) return 0f;
        Vector3 before = Agent.nextPosition;
        Agent.Move(delta);
        Vector3 moved = Agent.nextPosition - before; moved.y = 0f;
        stepDelta += moved;
        return moved.magnitude;
    }

    // A knockback is only a push: the agent keeps its path and the brain keeps deciding (and attacking)
    // while the enemy slides, so a hit jolts it without stopping it.
    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (Steadfast || (CombatManager.HasInstance && !CombatManager.Instance.enemiesKnockedBack)) return;
        if (force > .001f && new Vector3(direction.x, 0f, direction.z).sqrMagnitude > .0001f) { LastShove = new Vector3(direction.x, 0f, direction.z).normalized; LastShoveTime = Time.time; }
        if (SuppressKnockback) return;
        if (CombatManager.HasInstance) force *= CombatManager.Instance.enemyKnockbackScale;
        if (Agent == null || !Agent.isOnNavMesh) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0.001f) return;

        float duration = CombatManager.HasInstance ? CombatManager.Instance.knockbackDuration : 0.25f;
        knockbackVelocity = direction.normalized * (force / Mathf.Max(duration, 0.01f));
        knockbackTimer    = duration;
    }

    void OnDisable()
    {
        HasDestination = false;
        knockbackTimer = 0f;
        knockbackVelocity = Vector3.zero;
        stepDelta = stepVelocity = Vector3.zero;
        hasLookTarget = false;
    }

    void TickKnockback()
    {
        // Death can disable navigation while the last hit is still decaying.
        if (Agent == null || !Agent.isActiveAndEnabled || !Agent.isOnNavMesh)
        {
            knockbackTimer = 0f;
            knockbackVelocity = Vector3.zero;
            return;
        }
        knockbackTimer -= Time.deltaTime;
        Vector3 delta = knockbackVelocity * Time.deltaTime;
        delta.y = 0f;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero,
            knockbackVelocity.magnitude / Mathf.Max(knockbackTimer, 0.01f) * Time.deltaTime);
        // Move() slides along the navmesh — unlike Warp, it can't pop them upward.
        if (Agent.isOnNavMesh) Agent.Move(delta);
    }
}
