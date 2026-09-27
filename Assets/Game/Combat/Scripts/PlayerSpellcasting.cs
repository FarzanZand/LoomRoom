using UnityEngine;

// The left hand holds either a shield or a spell, chosen through the hotbar. With a spell in hand the
// left-hand action winds it up (mana is checked, nothing spent), then releases it when the wind-up
// completes (mana is spent once). A heavy hit or death cancels the wind-up; ordinary damage doesn't.
// Arm poses come from the "Left Hand Spell" animator layer (tags SpellEquip, SpellCharge, SpellRelease).
[DisallowMultipleComponent]
public class PlayerSpellcasting : MonoBehaviour
{
    public SpellDefinition fireball, heal;
    public Animator arms;
    [Tooltip("Palm of the left hand. Held effects and projectiles start here.")]
    public Transform castOrigin;
    public string spellLayer = "Left Hand Spell";
    public string castParameter = "SpellCast", releaseParameter = "SpellRelease", cancelParameter = "SpellCancel";
    public string equippedParameter = "SpellEquipped", typeParameter = "SpellType", speedParameter = "SpellSpeed";
    [Tooltip("Raising or lowering the hand when switching between shield and spell.")]
    [Min(.05f)] public float switchSeconds = .3f;
    public AudioData fizzleSound;

    public LeftHandSpell Equipped { get; private set; }
    public SpellDefinition Selected => player != null ? player.Equipment.Get(EquipmentSlot.LeftHand)?.spell : null;
    public bool Casting => casting;
    public bool Busy => casting || recovery > 0 || switching > 0;
    // 0..1 through the current wind-up, for the HUD.
    public float ChargeProgress => casting && castTime > 0 ? Mathf.Clamp01(elapsed / castTime) : 0;
    public event System.Action Changed;

    Player player;
    AdventurerProgress progress;
    SpellDefinition shown, castingSpell;
    SpellVisual held;
    AudioSource chargeAudio;
    readonly object slowSource = new object();
    int layer = -1;
    float elapsed, castTime, recovery, switching, relightAt = -1;
    bool casting, pendingEquip;

    void Awake()
    {
        player = GetComponent<Player>();
        progress = GetComponent<AdventurerProgress>();
    }

    void Start()
    {
        if (InputManager.HasInstance) InputManager.Instance.SecondaryPressed += Cast;
        player.Equipment.Changed += EquipmentChanged;
        player.Damaged += OnDamaged;
        player.Died += OnDied;
        if (arms != null) layer = arms.GetLayerIndex(spellLayer);
        EquipmentChanged();
    }

    void OnDestroy()
    {
        if (InputManager.HasInstance) InputManager.Instance.SecondaryPressed -= Cast;
        if (player == null) return;
        if (player.Equipment != null) player.Equipment.Changed -= EquipmentChanged;
        player.Damaged -= OnDamaged;
        player.Died -= OnDied;
    }

    void OnDisable() => Cancel(false);

    // ── Rules ──────────────────────────────────────────────────────────────────
    public float Cost(SpellDefinition spell) => spell == null ? 0 : Mathf.Max(1, spell.manaCost);
    public float Power(SpellDefinition spell) => spell == null ? 0 : spell.power * (progress != null ? progress.SpellPower(spell.Skill) : 1);
    public float CastTime(SpellDefinition spell) => spell == null ? 0 : Mathf.Max(.1f, spell.castSeconds * (progress != null ? progress.SpellCastTime(spell.Skill) : 1));

    // ── Equipping ──────────────────────────────────────────────────────────────
    void EquipmentChanged()
    {
        // A swap asked for mid-cast waits until the cast and its recovery are over.
        if (casting || recovery > 0) { pendingEquip = true; return; }
        pendingEquip = false;
        var selected = Selected;
        Equipped = selected != null ? selected.spell : LeftHandSpell.Shield;
        SetBool(equippedParameter, selected != null);
        if (selected == shown) return;

        bool raising = selected != null;
        switching = switchSeconds;
        if (arms != null && raising && layer >= 0)
        {
            SetInt(typeParameter, selected.animation);
            // From one spell to another: raise the hand again rather than snapping between poses.
            if (shown != null) arms.CrossFadeInFixedTime("Spell Raise", .08f, layer, 0);
        }
        shown = selected;
        ShowHeld();
        if (raising && selected.equipSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(selected.equipSound);
        Changed?.Invoke();
    }

    void ShowHeld()
    {
        if (held != null) held.Stop(false);
        held = null; relightAt = -1;
        if (shown == null || shown.heldVisual == null || castOrigin == null) return;
        held = Instantiate(shown.heldVisual, castOrigin);
        held.transform.localPosition = Vector3.zero;
        held.transform.localRotation = Quaternion.identity;
        SetLayer(held.gameObject, castOrigin.gameObject.layer);
    }

    // Class starts and checkpoints select the left hand the same way the hotbar does.
    public void ResetHand(LeftHandSpell hand)
    {
        if (player == null) Awake();
        Cancel(false); recovery = switching = 0;
        foreach (var inventory in new[] { player.Hotbar, player.Bag })
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var item = inventory.ItemAt(i);
                bool match = hand == LeftHandSpell.Shield ? item != null && item.itemType == ItemType.Shield
                                                          : item != null && item.spell != null && item.spell.spell == hand;
                if (!match) continue;
                player.Equipment.Equip(item, false);
                EquipmentChanged();
                return;
            }
        EquipmentChanged();
    }

    public void RestoreEquippedHand() { Cancel(false); recovery = switching = 0; EquipmentChanged(); }

    // ── Casting ────────────────────────────────────────────────────────────────
    public void Cast()
    {
        var spell = Selected;
        if (spell == null || Busy || !player.IsActive || !player.IsAlive) return;
        if (!GameManager.HasInstance || !GameManager.Instance.GameplayActive || player.Combat.IsAttacking) return;
        if (spell.spell == LeftHandSpell.Heal && player.Stats.CurrentHealth >= player.Stats.MaxHealth) { Refuse("Health is full"); return; }
        if (player.Stats.CurrentMana < Cost(spell)) { Refuse("Not enough mana"); return; }

        casting = true; castingSpell = spell; elapsed = 0; castTime = CastTime(spell);
        SetInt(typeParameter, spell.animation);
        SetFloat(speedParameter, spell.chargeClipSeconds / castTime);
        SetTrigger(castParameter);
        player.Stats.AddModifier(new StatModifier(StatType.MoveSpeed, spell.castMoveSpeed - 1, ModifierType.PercentMultiply, slowSource));
        if (spell.chargeSound != null && AudioManager.HasInstance) chargeAudio = AudioManager.Instance.PlaySFXData2D(spell.chargeSound);
        Changed?.Invoke();
    }

    void Release()
    {
        var spell = castingSpell;
        EndCharge();
        recovery = spell.recoverySeconds;
        // Checked again at release: health may have filled or mana drained during the wind-up.
        if (spell.spell == LeftHandSpell.Heal && player.Stats.CurrentHealth >= player.Stats.MaxHealth) { SetTrigger(cancelParameter); Refuse("Health is full"); return; }
        if (!player.Stats.TryUseMana(Cost(spell))) { SetTrigger(cancelParameter); Refuse("Not enough mana"); return; }

        SetTrigger(releaseParameter);
        if (spell.releaseSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(spell.releaseSound);
        if (spell.spell == LeftHandSpell.Heal)
        {
            float before = player.Stats.CurrentHealth;
            player.Stats.Heal(Power(spell));
            progress?.PractiseHeal(player.Stats.CurrentHealth - before);
            if (spell.impact != null) Instantiate(spell.impact, player.transform.position, player.transform.rotation, player.transform);
            if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(spell.impactSound);
        }
        else if (spell.projectile != null) Launch(spell);
        Changed?.Invoke();
    }

    void Launch(SpellDefinition spell)
    {
        var view = player.CameraRig != null && player.CameraRig.Camera != null ? player.CameraRig.Camera.transform : transform;
        var origin = castOrigin != null ? castOrigin.position : view.position;
        // Aim where the crosshair points, but leave from the hand. If the hand is inside a wall
        // (standing right against it), leave from the eye instead.
        var aim = view.position + view.forward * 60f;
        if (Physics.Raycast(view.position, view.forward, out var hit, 60f, spell.projectile.collisionMask, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(player.transform))
            aim = hit.point;
        if (Physics.Linecast(view.position, origin, out var blocked, spell.projectile.collisionMask, QueryTriggerInteraction.Ignore) && !blocked.transform.IsChildOf(player.transform))
            origin = view.position;
        var direction = aim - origin;
        if (direction.sqrMagnitude < .01f) direction = view.forward;
        var projectile = Instantiate(spell.projectile, origin, Quaternion.LookRotation(direction.normalized));
        projectile.Launch(player, spell, Power(spell), progress);

        // The flame leaves the hand and relights once the throw has recovered.
        if (held != null) { held.Stop(); held = null; }
        relightAt = Time.time + spell.recoverySeconds + .1f;
    }

    void Cancel(bool animate)
    {
        if (!casting) return;
        EndCharge();
        if (animate) SetTrigger(cancelParameter);
        Changed?.Invoke();
    }

    void EndCharge()
    {
        casting = false; castingSpell = null; elapsed = 0;
        if (player != null && player.Stats != null) player.Stats.RemoveAllFromSource(slowSource);
        if (held != null) held.SetCharge(0);
        if (chargeAudio != null) { chargeAudio.Stop(); chargeAudio = null; }
    }

    void Refuse(string message)
    {
        NotificationUI.Show(message);
        if (fizzleSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(fizzleSound);
    }

    void OnDamaged(DamageInfo info)
    {
        // Only a staggering blow breaks concentration.
        if (casting && !info.Blocked && info.Heavy && info.Amount > 0) { Cancel(true); NotificationUI.Show("Your spell was interrupted"); }
    }

    void OnDied() => Cancel(true);

    void Update()
    {
        if (!player.IsAlive) { Cancel(false); return; }
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        float dt = Time.deltaTime;
        recovery = Mathf.Max(0, recovery - dt);
        switching = Mathf.Max(0, switching - dt);
        UpdateLayerWeight(dt);
        if (pendingEquip && !casting && recovery <= 0) EquipmentChanged();
        if (relightAt > 0 && Time.time >= relightAt && shown != null && Selected == shown) ShowHeld();

        if (!casting) return;
        if (Selected != castingSpell) { Cancel(true); return; }
        elapsed += dt;
        if (held != null) held.SetCharge(ChargeProgress);
        if (elapsed >= castTime) Release();
    }

    // The spell layer overrides the left arm only while a spell is in hand; it fades out as the
    // shield comes back so the shield's own raise animation shows through.
    void UpdateLayerWeight(float dt)
    {
        if (arms == null || layer < 0) return;
        // An animator that is switched off and on (player swaps) forgets its parameters; keep them true.
        if (arms.GetBool(equippedParameter) != (shown != null)) SetBool(equippedParameter, shown != null);
        if (shown != null && arms.GetInteger(typeParameter) != shown.animation && !casting) SetInt(typeParameter, shown.animation);
        float target = shown != null ? 1 : 0;
        float weight = Mathf.MoveTowards(arms.GetLayerWeight(layer), target, dt / Mathf.Max(.05f, switchSeconds * .6f));
        arms.SetLayerWeight(layer, weight);
    }

    // ── Animator helpers ───────────────────────────────────────────────────────
    void SetTrigger(string name) { if (arms != null && Character.HasParameter(arms, name, AnimatorControllerParameterType.Trigger)) arms.SetTrigger(name); }
    void SetBool(string name, bool value) { if (arms != null && Character.HasParameter(arms, name, AnimatorControllerParameterType.Bool)) arms.SetBool(name, value); }
    void SetInt(string name, int value) { if (arms != null && Character.HasParameter(arms, name, AnimatorControllerParameterType.Int)) arms.SetInteger(name, value); }
    void SetFloat(string name, float value) { if (arms != null && Character.HasParameter(arms, name, AnimatorControllerParameterType.Float)) arms.SetFloat(name, value); }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }
}
