using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterBase : MonoBehaviour
{
    public bool coreSettings;

    protected NavMeshAgent agent;
    protected Animator     animator;
    protected StatsComponent stats;

    [ShowIf("coreSettings")] [BoxGroup("Ground Check")]
    [SerializeField] float groundCheckDistance = 0.2f;

    [ShowIf("coreSettings")] [BoxGroup("Ground Check")]
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Combat")]
    [SerializeField] string hurtTrigger = "Hurt";
    [SerializeField] protected float knockbackForce = 2f;

    [Header("Death")]
    [SerializeField] string deathTrigger = "Death";
    [SerializeField] float deathDuration = 2f;

    protected bool IsGrounded { get; private set; }

    // Delegate health queries to StatsComponent
    public float Health  => stats != null ? stats.CurrentHealth : 0f;
    public bool IsAlive  => stats != null ? stats.IsAlive : false;
    public void Heal(float amount) => stats?.Heal(amount);

    protected virtual void Awake()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        stats    = GetComponent<StatsComponent>();

        if (stats != null)
        {
            stats.OnDamageTaken += HandleDamageTaken;
            stats.OnDied        += Die;
        }
    }

    protected virtual void OnDestroy()
    {
        if (stats != null)
        {
            stats.OnDamageTaken -= HandleDamageTaken;
            stats.OnDied        -= Die;
        }
    }

    void HandleDamageTaken(float amount, Vector3 knockbackDir)
    {
        TriggerAnimation(hurtTrigger);
        if (knockbackDir != Vector3.zero)
            ApplyKnockback(knockbackDir);
    }

    protected virtual void ApplyKnockback(Vector3 direction)
    {
        if (agent != null && agent.isOnNavMesh)
            agent.Warp(transform.position + direction.normalized * knockbackForce);
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

    // ── Shared helpers ────────────────────────────────────────────────

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

        float speed      = agent != null ? agent.velocity.magnitude : 0f;
        float motionSpeed = agent != null && agent.hasPath ? 1f : 0f;
        bool  freeFall   = !IsGrounded && (agent != null && agent.velocity.y < -1f);

        animator.SetFloat("Speed",       speed,       0.1f, Time.deltaTime);
        animator.SetFloat("MotionSpeed", motionSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("Grounded",  IsGrounded);
        animator.SetBool("FreeFall",  freeFall);
    }

    [Button]
    public void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetTrigger(triggerName);
    }
}
