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
        OnEquipmentChanged();
    }

    void OnDisable()
    {
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed -= OnPrimaryPressed;
        if (player.Equipment != null) player.Equipment.Changed -= OnEquipmentChanged;
        player.Damaged -= OnDamaged;
    }

    void OnPrimaryPressed()
    {
        if (!player.IsActive) return;
        if (InputManager.Instance.SecondaryHeld) return;
        float window = CombatManager.HasInstance ? CombatManager.Instance.blockCancelWindow : 0.5f;
        blockLockUntil = Time.time + window;
    }

    void Update()
    {
        if (armsAnimator == null || armsAnimator.runtimeAnimatorController == null) return;

        bool gameplay = !GameManager.HasInstance || GameManager.Instance.GameplayActive;
        bool primary   = gameplay && InputManager.HasInstance && InputManager.Instance.PrimaryHeld;
        bool secondary = gameplay && InputManager.HasInstance && InputManager.Instance.SecondaryHeld;
        bool blockLocked = Time.time < blockLockUntil;

        SetBool(armsAnimator, attackHeldParam, primary && WeaponHeld && !secondary);
        SetBool(armsAnimator, blockHeldParam,  secondary && !blockLocked && ShieldHeld);

        if (player.Stats != null && Character.HasParameter(armsAnimator, "AttackSpeed", AnimatorControllerParameterType.Float))
            armsAnimator.SetFloat("AttackSpeed", player.Stats.GetMultiplier(StatType.AttackSpeed));
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
        if (!IsBlocking || !ShieldHeld) return false;

        if (info.Direction.sqrMagnitude > 0.001f)
        {
            float threshold = CombatManager.HasInstance ? CombatManager.Instance.blockFrontalDot : -0.3f;
            Vector3 facing = player.Look != null ? player.Look.YawTransform.forward : transform.forward;
            if (Vector3.Dot(facing, info.Direction.normalized) >= threshold) return false;
        }

        float cost = CombatManager.HasInstance ? CombatManager.Instance.blockStaminaCost : 0f;
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
