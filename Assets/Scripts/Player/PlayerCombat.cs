using UnityEngine;

// Table player only: attack and block. The arms animator owns the timing; this feeds it
// the held inputs and reads back state through state TAGS ("Attack", "Block") on any
// layer, never state names or layer indices. Hitbox windows come from animation events
// through WeaponAnimationRelay.
public class PlayerCombat : MonoBehaviour, IBlocker
{
    [Header("Animators")]
    [Tooltip("First-person arms animator that plays attacks and blocks.")]
    [SerializeField] Animator armsAnimator;
    [Tooltip("Optional third-person body animator that mirrors hurt/block hits.")]
    [SerializeField] Animator bodyAnimator;

    [Header("State Tags")]
    [SerializeField] string attackTag = "Attack";
    [SerializeField] string blockTag  = "Block";

    [Header("Animator Parameters")]
    [SerializeField] string attackHeldParam        = "AttackHeld";
    [SerializeField] string blockHeldParam         = "BlockHeld";
    [SerializeField] string rightHandEquippedParam = "RightHandEquipped";
    [SerializeField] string leftHandEquippedParam  = "LeftHandEquipped";
    [SerializeField] string blockHitTrigger        = "BlockHit";
    [SerializeField] string hurtTrigger            = "Hurt";

    public bool IsAttacking => HasTag(attackTag);
    public bool IsBlocking  => HasTag(blockTag);
    public bool WeaponHeld  => player.Equipment != null && player.Equipment.Get(EquipmentSlot.RightHand)?.itemType == ItemType.Weapon;
    public bool ShieldHeld  => player.Equipment != null && player.Equipment.Get(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;

    Player player;
    float  blockLockUntil = -1f;
    float attackBufferedUntil=-1f;
    float pressedAt;
    bool charging;
    float guardPressedAt=-10;
    public bool HeavySwing { get; private set; }

    void Awake()
    {
        player = GetComponent<Player>();
        if (armsAnimator == null)
        {
            var relay = GetComponentInChildren<WeaponAnimationRelay>(true);
            if (relay != null) armsAnimator = relay.GetComponent<Animator>();
        }
    }

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed += OnPrimaryPressed;
        if (player.Equipment != null) player.Equipment.Changed += OnEquipmentChanged;
        player.Damaged += OnDamaged;
        if(InputManager.HasInstance)
        {
            InputManager.Instance.PrimaryReleased += OnPrimaryReleased;
            InputManager.Instance.SecondaryPressed += OnGuardPressed;
        }
        OnEquipmentChanged();
    }

    void OnDisable()
    {
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed -= OnPrimaryPressed;
        if (player.Equipment != null) player.Equipment.Changed -= OnEquipmentChanged;
        player.Damaged -= OnDamaged;
        if(InputManager.HasInstance) InputManager.Instance.PrimaryReleased -= OnPrimaryReleased;
        if(InputManager.HasInstance) InputManager.Instance.SecondaryPressed -= OnGuardPressed;
        HeavySwing=false; charging=false; attackBufferedUntil=-1;
    }

    void OnPrimaryPressed()
    {
        if (!player.IsActive) return;
        if (InputManager.Instance.SecondaryHeld) return;
        pressedAt=Time.time; HeavySwing=false; charging=true;
        attackBufferedUntil=Time.time+(CombatManager.HasInstance ? CombatManager.Instance.attackInputBuffer : .22f);
        float window = CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f;
        blockLockUntil = Time.time + window;
    }

    void OnGuardPressed() { if(player.IsActive && Time.time >= blockLockUntil) guardPressedAt=Time.time; }

    void OnPrimaryReleased()
    {
        if(!charging) return;
        charging=false;
        if(!player.IsActive || !WeaponHeld || !IsAttacking || !CombatManager.HasInstance) return;
        var tuning=CombatManager.Instance;
        HeavySwing=Time.time-pressedAt >= tuning.heavyChargeTime &&
            (player.Stats==null || player.Stats.TryUseStamina(tuning.heavyStaminaCost));
    }

    void Update()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return;

        bool gameplay = !GameManager.HasInstance || GameManager.Instance.GameplayActive;
        bool primary   = gameplay && InputManager.HasInstance && InputManager.Instance.PrimaryHeld;
        bool secondary = gameplay && InputManager.HasInstance && InputManager.Instance.SecondaryHeld;
        bool blockLocked = Time.time < blockLockUntil;
        if(CombatManager.HasInstance)
        {
            float attackSpeed=CombatManager.Instance.playerAttackSpeed*(player.Stats!=null ? player.Stats.GetMultiplier(StatType.AttackSpeed) : 1f);
            if(Character.HasParameter(armsAnimator,"WindupSpeed",AnimatorControllerParameterType.Float)) armsAnimator.SetFloat("WindupSpeed",CombatManager.Instance.windupSpeed*attackSpeed);
            if(Character.HasParameter(armsAnimator,"ReleaseSpeed",AnimatorControllerParameterType.Float)) armsAnimator.SetFloat("ReleaseSpeed",CombatManager.Instance.releaseSpeed*attackSpeed);
            if(Character.HasParameter(armsAnimator,"HeavyReleaseSpeed",AnimatorControllerParameterType.Float)) armsAnimator.SetFloat("HeavyReleaseSpeed",CombatManager.Instance.heavyReleaseSpeed*attackSpeed);
            SetBool(armsAnimator,"HeavyStrike",HeavySwing);
        }

        SetBool(armsAnimator, attackHeldParam, (primary || Time.time < attackBufferedUntil) && WeaponHeld && !secondary);
        SetBool(armsAnimator, blockHeldParam,  secondary && !blockLocked && ShieldHeld);

        if (player.Stats != null && Character.HasParameter(armsAnimator, "AttackSpeed", AnimatorControllerParameterType.Float))
            armsAnimator.SetFloat("AttackSpeed", player.Stats.GetMultiplier(StatType.AttackSpeed) * (CombatManager.HasInstance ? CombatManager.Instance.playerAttackSpeed : 1f));
    }

    void OnEquipmentChanged()
    {
        bool right = player.Equipment != null && player.Equipment.Has(EquipmentSlot.RightHand);
        bool left  = player.Equipment != null && player.Equipment.Has(EquipmentSlot.LeftHand);
        SetBool(armsAnimator, rightHandEquippedParam, right);
        SetBool(armsAnimator, leftHandEquippedParam,  left);
        if (!ShieldHeld) SetBool(armsAnimator, blockHeldParam, false);
    }

    void OnDamaged(DamageInfo info)
    {
        string trigger = info.Blocked ? blockHitTrigger : hurtTrigger;
        Trigger(armsAnimator, trigger);
        Trigger(bodyAnimator, trigger);
    }

    // ── IBlocker ──────────────────────────────────────────────────────

    public bool TryBlock(ref DamageInfo info)
    {
        bool guardInput=InputManager.HasInstance && InputManager.Instance.SecondaryHeld && Time.time >= blockLockUntil;
        if ((!IsBlocking && !guardInput) || !ShieldHeld) return false;

        if (info.Direction.sqrMagnitude > 0.001f)
        {
            float threshold = CombatManager.HasInstance ? CombatManager.Instance.blockFrontalDot : -0.3f;
            Vector3 facing = player.Look != null ? player.Look.YawTransform.forward : transform.forward;
            if (Vector3.Dot(facing, info.Direction.normalized) >= threshold) return false;
        }

        info.Parried=CombatManager.HasInstance && Time.time-guardPressedAt <= CombatManager.Instance.timedBlockWindow;
        float cost = info.Parried ? 0 : CombatManager.HasInstance ? CombatManager.Instance.blockStaminaCost : 0f;
        if (cost > 0f && player.Stats != null && !player.Stats.TryUseStamina(cost)) return false;

        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    bool HasTag(string tag)
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null || string.IsNullOrEmpty(tag)) return false;
        for (int layer = 0; layer < armsAnimator.layerCount; layer++)
        {
            if (armsAnimator.GetCurrentAnimatorStateInfo(layer).IsTag(tag)) return true;
            if (armsAnimator.IsInTransition(layer) && armsAnimator.GetNextAnimatorStateInfo(layer).IsTag(tag)) return true;
        }
        return false;
    }

    static void SetBool(Animator anim, string param, bool value)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, param, AnimatorControllerParameterType.Bool)) anim.SetBool(param, value);
    }

    static void Trigger(Animator anim, string param)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        if (Character.HasParameter(anim, param, AnimatorControllerParameterType.Trigger)) anim.SetTrigger(param);
    }
}
