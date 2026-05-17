using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterBase : MonoBehaviour
{
    public bool coreSettings;

    protected NavMeshAgent   agent;
    protected Animator       animator;
    protected StatsComponent stats;
    protected CharacterFX    fx;

    [ShowIf("coreSettings")] [BoxGroup("Ground Check")]
    [SerializeField] float groundCheckDistance = 0.2f;

    [ShowIf("coreSettings")] [BoxGroup("Ground Check")]
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Combat")]
    [SerializeField] protected float knockbackForce = 2f;

    [Header("Death")]
    [SerializeField] float deathDuration = 2f;

    // Subclasses (or their data assets) override these to change which
    // animator triggers fire on hurt and death.
    protected virtual string HurtTrigger  => "Hurt";
    protected virtual string DeathTrigger => "Death";

    protected bool IsGrounded { get; private set; }

    public float Health  => stats != null ? stats.CurrentHealth : 0f;
    public bool  IsAlive => stats != null ? stats.IsAlive : true;
    public void  Heal(float amount) => stats?.Heal(amount);

    protected virtual void Awake()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        stats    = GetComponent<StatsComponent>();
        fx       = GetComponent<CharacterFX>();

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
        TriggerAnimation(DeathTrigger);
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
        bool rayHit = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundCheckDistance + 0.1f,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
        // NavMeshAgent-driven characters are grounded whenever they're on the mesh
        IsGrounded = rayHit || (agent != null && agent.isOnNavMesh);
    }

    protected void UpdateAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        float speed       = agent != null ? agent.velocity.magnitude : 0f;
        float motionSpeed = agent != null && agent.hasPath ? 1f : 0f;
        bool  freeFall    = !IsGrounded && (agent != null && agent.velocity.y < -1f);

        animator.SetFloat("Speed",       speed,       0.1f, Time.deltaTime);
        animator.SetFloat("MotionSpeed", motionSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("Grounded", IsGrounded);
        animator.SetBool("FreeFall", freeFall);
    }

    [Button]
    public void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters)
            if (p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(triggerName);
                return;
            }
    }

    void HandleDamageTaken(float amount, Vector3 knockbackDir)
    {
        TriggerAnimation(HurtTrigger);
        fx?.NotifyHurtReceived(amount, knockbackDir);
        if (knockbackDir != Vector3.zero)
            ApplyKnockback(knockbackDir);
    }
}
