using UnityEngine;
using UnityEngine.AI;

// NavMeshAgent wrapper for enemies and NPCs: move, stop, face, speed modes and a
// timed knockback slide. Brains never touch the agent directly.
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMotor : MonoBehaviour, IKnockbackReceiver
{
    [Header("Ground Check")]
    [SerializeField] float     groundCheckDistance = 0.2f;
    [SerializeField] LayerMask groundMask = ~0;

    public NavMeshAgent Agent { get; private set; }
    public bool  IsKnockedBack => knockbackTimer > 0f;
    public bool  IsGrounded    { get; private set; }
    public float DefaultSpeed  { get; private set; }
    public Vector3 Velocity    => Agent != null ? Agent.velocity : Vector3.zero;
    public bool  HasPath       => Agent != null && Agent.hasPath && !Agent.isStopped;

    Vector3 knockbackVelocity;
    float   knockbackTimer;

    void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        DefaultSpeed = Agent.speed;
    }

    void Update()
    {
        UpdateGroundCheck();
        if (knockbackTimer > 0f) TickKnockback();
    }

    void UpdateGroundCheck()
    {
        bool rayHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
                                      groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
        IsGrounded = rayHit || (Agent != null && Agent.isOnNavMesh);
    }

    // ── Movement ──────────────────────────────────────────────────────

    public void MoveTo(Vector3 destination)
    {
        if (Agent == null || !Agent.isOnNavMesh || IsKnockedBack) return;
        Agent.isStopped = false;
        Agent.SetDestination(destination);
    }

    public void Stop()
    {
        if (Agent == null || !Agent.isOnNavMesh) return;
        Agent.ResetPath();
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

    public void SetSpeed(float speed)        { if (Agent != null) Agent.speed = speed; }
    public void ResetSpeed()                 { if (Agent != null) Agent.speed = DefaultSpeed; }
    public void SetAngularSpeed(float speed) { if (Agent != null) Agent.angularSpeed = speed; }

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

    public bool IsFacing(Vector3 worldPoint, float maxAngle)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude < 0.001f || Vector3.Angle(transform.forward, dir) <= maxAngle;
    }

    public void Warp(Vector3 position)
    {
        if (Agent != null && Agent.isOnNavMesh) Agent.Warp(position);
        else transform.position = position;
    }

    // ── Knockback ─────────────────────────────────────────────────────

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (Agent == null || !Agent.isOnNavMesh) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0.001f) return;

        float duration = CombatManager.HasInstance ? CombatManager.Instance.knockbackDuration : 0.25f;
        knockbackVelocity = direction.normalized * (force / Mathf.Max(duration, 0.01f));
        knockbackTimer    = duration;
        Agent.ResetPath();
        Agent.isStopped = true;
    }

    void TickKnockback()
    {
        knockbackTimer -= Time.deltaTime;
        Vector3 delta = knockbackVelocity * Time.deltaTime;
        delta.y = 0f;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero,
            knockbackVelocity.magnitude / Mathf.Max(knockbackTimer, 0.01f) * Time.deltaTime);
        // Move() slides along the navmesh — unlike Warp, it can't pop them upward.
        if (Agent.isOnNavMesh) Agent.Move(delta);
        if (knockbackTimer <= 0f) Agent.isStopped = false;
    }
}
