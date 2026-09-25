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
    public bool  IsKnockedBack => knockbackTimer > 0f;
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

    public void MoveTo(Vector3 destination)
    {
        // Remembered even when it can't be issued yet, so the end of a knockback resumes it.
        Destination = destination;
        HasDestination = true;
        if (!NavigationReady || IsKnockedBack) return;
        Agent.speed = movementSpeed;
        Agent.isStopped = false;
        Agent.SetDestination(destination);
    }

    // A short local path preserves agent avoidance while moving independently of facing.
    public void Strafe(Vector3 worldDirection, float speed)
    {
        HasDestination = false;
        if (!NavigationReady || IsKnockedBack) return;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f || speed <= 0f) { Stop(); return; }
        Vector3 destination = transform.position + worldDirection.normalized * Mathf.Max(0.6f, speed * 0.35f);
        if (Agent.Raycast(destination, out var edge)) destination = edge.position;
        Agent.speed = speed;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking = false;
        Agent.isStopped = false;
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

    // Locomotion parameters for the humanoid body. Not locomoting forces an idle speed.
    public void UpdateLocomotionAnimator(Animator anim, bool locomoting, float damping)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        Vector3 v = Velocity; v.y = 0f;
        float speed = v.magnitude;
        if (locomoting) anim.SetFloat("Speed", speed, damping, Time.deltaTime);
        else            anim.SetFloat("Speed", 0f);
        anim.SetFloat("MotionSpeed", locomoting && speed > 0.1f ? 1f : 0f, damping, Time.deltaTime);
        anim.SetBool("Grounded", IsGrounded);
        anim.SetBool("FreeFall", !IsGrounded && Velocity.y < -1f);
    }

    public void SetSpeed(float speed)              { movementSpeed = speed; if (Agent != null) Agent.speed = speed; }
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

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (SuppressKnockback) return;
        if (Agent == null || !Agent.isOnNavMesh) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0.001f) return;

        float duration = CombatManager.HasInstance ? CombatManager.Instance.knockbackDuration : 0.25f;
        knockbackVelocity = direction.normalized * (force / Mathf.Max(duration, 0.01f));
        knockbackTimer    = duration;
        Agent.ResetPath();
        Agent.isStopped = true;
    }

    void OnDisable()
    {
        HasDestination = false;
        knockbackTimer = 0f;
        knockbackVelocity = Vector3.zero;
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
        if (knockbackTimer <= 0f)
        {
            Agent.isStopped = false;
            if (HasDestination) MoveTo(Destination);
        }
    }
}
