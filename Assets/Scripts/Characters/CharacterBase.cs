using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterBase : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;
    public bool coreSettings;
    
    protected NavMeshAgent agent;
    protected Animator animator;

    [ShowIf("coreSettings")]
    [BoxGroup("Ground Check")]
    [SerializeField] float groundCheckDistance = 0.2f;

    [ShowIf("coreSettings")]
    [BoxGroup("Ground Check")]
    [SerializeField] LayerMask groundMask = ~0;

    protected bool IsGrounded { get; private set; }

    protected virtual void Awake()
    {
        Health = maxHealth;
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    public virtual void TakeDamage(float damage)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - damage);
        if (!IsAlive) Die();
    }

    public virtual void Heal(float amount)
    {
        if (!IsAlive) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    public virtual void Pause()
    {
        if (agent == null) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    public virtual void Resume()
    {
        if (agent == null) return;
        agent.isStopped = false;
    }

    protected virtual void Die() { }

    // Shared helpers

    protected void MoveTo(Vector3 destination)
    {
        if (agent == null) return;
        agent.SetDestination(destination);
    }

    protected void StopMoving()
    {
        if (agent == null) return;
        agent.ResetPath();
    }

    protected void UpdateGroundCheck()
    {
        IsGrounded = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundCheckDistance + 0.1f,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    protected void UpdateAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        float speed = agent != null ? agent.velocity.magnitude : 0f;
        float motionSpeed = agent != null && agent.hasPath ? 1f : 0f;
        bool freeFall = !IsGrounded && (agent != null && agent.velocity.y < -1f);

        animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
        animator.SetFloat("MotionSpeed", motionSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("Grounded", IsGrounded);
        animator.SetBool("FreeFall", freeFall);
    }

    [Button]
    public void TriggerAnimation(string triggerName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetTrigger(triggerName);
    }
}
