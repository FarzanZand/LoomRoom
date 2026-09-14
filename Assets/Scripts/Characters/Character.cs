using System;
using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;

// Hub component on every character root: players, enemies, NPCs. Holds the data
// asset, caches the sibling components and turns stat events into character events
// (hurt animation, FX, knockback, death). Everything that needs "a character"
// takes this.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class Character : MonoBehaviour
{
    [Required] public CharacterData data;
    [Tooltip("Animator that plays hurt/death. Leave empty to search children.")]
    [SerializeField] Animator animator;

    public CharacterStats     Stats     { get; private set; }
    public CharacterFX        FX        { get; private set; }
    public Animator           Animator  => animator;
    public IKnockbackReceiver Knockback { get; private set; }

    public string DisplayName => data != null && !string.IsNullOrEmpty(data.characterName) ? data.characterName : name;
    public bool   IsAlive     => Stats == null || Stats.IsAlive;

    Faction? factionOverride;
    public Faction Faction => factionOverride ?? (data != null ? data.faction : Faction.Neutral);
    public event Action FactionChanged;

    public event Action<DamageInfo> Damaged;
    public event Action<DamageInfo> HitLanded;
    public event Action<float>      Healed;
    public event Action             Died;

    public static event Action<Character> Spawned;
    public static event Action<Character> AnyDied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Spawned = null; AnyDied = null; }

    protected virtual void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        Stats     = GetComponent<CharacterStats>();
        FX        = GetComponentInChildren<CharacterFX>(true);
        Knockback = GetComponent<IKnockbackReceiver>();

        if (Stats != null)
        {
            Stats.Damaged += OnDamaged;
            Stats.Healed  += amount => Healed?.Invoke(amount);
            Stats.Died    += OnDied;
        }
    }

    protected virtual void Start()
    {
        Spawned?.Invoke(this);
    }

    protected virtual void OnDestroy()
    {
        if (Stats != null)
        {
            Stats.Damaged -= OnDamaged;
            Stats.Died    -= OnDied;
        }
    }

    public void SetFaction(Faction faction)
    {
        if (factionOverride == faction) return;
        factionOverride = faction;
        FactionChanged?.Invoke();
    }

    protected virtual void OnDamaged(DamageInfo info)
    {
        if (!info.Blocked && data != null && GetComponent<EnemyBrain>() == null)
            TriggerAnimation(data.hurtTrigger);

        FX?.NotifyHurtReceived(info);
        if (info.Amount > 0f || info.Blocked)
        {
            if (CombatManager.HasInstance) CombatManager.Instance.PresentImpact(this, info);
            info.Source?.NotifyHitLanded(info);
        }

        if (!info.Blocked && info.Amount > 0f && info.KnockbackForce > 0f && info.Direction != Vector3.zero)
            Knockback?.ApplyKnockback(info.Direction, info.KnockbackForce);

        Damaged?.Invoke(info);
    }

    protected virtual void OnDied()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // A lethal hit must not leave Hurt or Attack queued behind Death.
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger)
                    animator.ResetTrigger(parameter.name);
            if (HasParameter(animator, "Dead", AnimatorControllerParameterType.Bool))
                animator.SetBool("Dead", true);
        }
        if (data != null) TriggerAnimation(data.deathTrigger);
        Died?.Invoke();
        AnyDied?.Invoke(this);

        float delay = data != null ? data.deathDisableDelay : 0f;
        if (delay > 0f) StartCoroutine(DisableAfterDeath(delay));
    }

    IEnumerator DisableAfterDeath(float delay)
    {
        yield return new WaitForSeconds(delay);
        gameObject.SetActive(false);
    }

    // Called by Hitbox when this character's attack connects.
    public void NotifyHitLanded(DamageInfo info)
    {
        FX?.NotifyHitLanded(info);
        HitLanded?.Invoke(info);
    }

    [Button]
    public void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (HasParameter(animator, triggerName, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(triggerName);
    }

    public static bool HasParameter(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (anim == null) return false;
        foreach (var p in anim.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }
}
