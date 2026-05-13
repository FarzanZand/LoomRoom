using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Animator))]
public class NPCController : CharacterBase
{
    [Header("Wander")]
    [SerializeField] float wanderRadius = 8f;
    [SerializeField] float wanderInterval = 4f;

    [Header("Ground Check")]
    [SerializeField] float groundCheckDistance = 0.2f;
    [SerializeField] LayerMask groundMask = ~0;

    NavMeshAgent agent;
    Animator animator;
    float wanderTimer;
    bool isGrounded;

    protected override void Awake()
    {
        base.Awake();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        HandleWander();
        UpdateGrounded();
        UpdateAnimator();
    }

    void HandleWander()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);

        wanderTimer = wanderInterval;
    }

    void UpdateGrounded()
    {
        isGrounded = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundCheckDistance + 0.1f,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    void UpdateAnimator()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        float speed = agent.velocity.magnitude;
        float motionSpeed = agent.hasPath ? 1f : 0f;
        bool freeFall = !isGrounded && agent.velocity.y < -1f;

        animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
        animator.SetFloat("MotionSpeed", motionSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("Grounded", isGrounded);
        animator.SetBool("FreeFall", freeFall);
    }

    protected override void Die()
    {
        agent.isStopped = true;
        enabled = false;
    }
}
