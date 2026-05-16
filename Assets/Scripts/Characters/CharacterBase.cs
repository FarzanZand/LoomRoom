using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterBase : MonoBehaviour, IDamageable
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

    [Header("Combat")]
    [SerializeField] string hurtTrigger = "Hurt";
    [SerializeField] protected float knockbackForce = 2f;

    [Header("Death")]
    [SerializeField] string deathTrigger = "Death";
    [SerializeField] float deathDuration = 2f;

    protected bool IsGrounded { get; private set; }

    protected virtual void Awake()
    {
        Health = maxHealth;
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    // IDamageable — called by WeaponHitbox
    public virtual void TakeDamage(float amount, Vector3 knockbackDirection)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - amount);
        TriggerAnimation(hurtTrigger);
        if (knockbackDirection != Vector3.zero)
            ApplyKnockback(knockbackDirection);
        if (!IsAlive) Die();
    }

    // Convenience overload for internal calls without a direction
    public void TakeDamage(float amount) => TakeDamage(amount, Vector3.zero);

    protected virtual void ApplyKnockback(Vector3 direction)
    {
        if (agent != null && agent.isOnNavMesh)
            agent.Warp(transform.position + direction.normalized * knockbackForce);
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

    protected virtual void Die()
    {
        StopMoving();
        if (agent != null) agent.isStopped = true;
        TriggerAnimation(deathTrigger);
        StartCoroutine(DisableAfterDeath());
    }

    IEnumerator DisableAfterDeath()
    {
        yield return new WaitForSeconds(deathDuration);
        gameObject.SetActive(false);
    }

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

    protected void FaceTo(Transform target, float speed)
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(dir), speed * Time.deltaTime);
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
        if (string.IsNullOrEmpty(triggerName)) return;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetTrigger(triggerName);
    }
}
