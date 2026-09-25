using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// What a character is wearing and holding. Lives on the character, so each player
// keeps their own hands. Spawns the world prefab under the slot's anchor, applies the
// item's stat modifiers, fires OnEquip/OnUnequip effects and feeds the weapon Hitbox.
[RequireComponent(typeof(Character))]
public class Equipment : MonoBehaviour
{
    [Serializable]
    public class Anchor
    {
        public EquipmentSlot slot;
        public Transform     transform;
    }

    [Tooltip("Where each slot's item prefab is parented. Slots without an anchor still equip (stats only).")]
    [ListDrawerSettings(ShowFoldout = true)]
    [SerializeField] List<Anchor> anchors = new();

    [Tooltip("The hitbox that receives the right-hand weapon's hit profile. Leave empty to search children.")]
    [SerializeField] Hitbox weaponHitbox;

    public event Action Changed;

    public Character Character { get; private set; }

    static readonly EquipmentSlot[] AllSlots = (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));

    readonly Dictionary<EquipmentSlot, ItemData>   items   = new();
    readonly Dictionary<EquipmentSlot, GameObject> objects = new();
    readonly Dictionary<EquipmentSlot, object>     sources = new();
    readonly HeldItemEating eating = new();

    public bool IsUsingItem => eating.IsPlaying;

    public bool PlayEating(ItemData item, EquipmentSlot slot, Action complete)
    {
        if (IsUsingItem || !isActiveAndEnabled || !item.canBeEquipped || Get(slot) != item ||
            Character is not Player player || !objects.TryGetValue(slot, out var held)) return false;
        return eating.Play(player, item, GetAnchor(slot), held, complete);
    }

    // Restore the eating pose before the Animator evaluates the next frame.
    void Update() => eating.RestorePose();

    void LateUpdate()
    {
        if (Character is Player owner)
        {
            // Resolve after inventory transfers finish so swaps cannot leave ghost equipment.
            foreach (var slot in AllSlots)
            {
                var item = Get(slot);
                if (item == null) continue;
                bool hand = slot == EquipmentSlot.RightHand || slot == EquipmentSlot.LeftHand;
                if (owner.Hotbar.IndexOf(item) < 0 && (hand || owner.Bag.IndexOf(item) < 0)) Unequip(slot);
            }
        }
        eating.Tick();
    }

    void OnDisable() => eating.Clear();
    public IEnumerable<ItemData> EquippedItems => items.Values;

    bool initialized;
    void Awake() => EnsureInitialized();

    // A level can equip the inactive table player before Unity calls this component's Awake.
    void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        Character = GetComponent<Character>();
        if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<Hitbox>(true);
        EffectDispatcher.Ensure(gameObject);
        foreach (var s in AllSlots)
            sources[s] = new object();
    }

    public Transform GetAnchor(EquipmentSlot slot)
    {
        foreach (var a in anchors)
            if (a.slot == slot) return a.transform;
        return null;
    }

    public ItemData Get(EquipmentSlot slot) => items.TryGetValue(slot, out var i) ? i : null;
    public bool     Has(EquipmentSlot slot) => items.ContainsKey(slot);
    public bool     IsEquipped(ItemData item) => item != null && Get(item.equipSlot) == item;

    public bool IsShieldSource(object source)
    {
        foreach (var pair in sources)
            if (Get(pair.Key) is ItemData item && item.itemType == ItemType.Shield &&
                (ReferenceEquals(pair.Value, source) || source is ItemBuffSource buff && buff.Item == item)) return true;
        return false;
    }

    public bool CanEquip(ItemData item) => item != null && item.canBeEquipped;

    public bool Equip(ItemData item, bool playSound = true)
    {
        EnsureInitialized();
        if (!CanEquip(item)) return false;
        if (IsEquipped(item)) return true;

        if(Character is Player owner && owner.Bag.IndexOf(item)<0 && owner.Hotbar.IndexOf(item)<0)return false;
        var slot = item.equipSlot;
        if(Character is Player p && (slot==EquipmentSlot.RightHand || slot==EquipmentSlot.LeftHand) && p.Hotbar.IndexOf(item)<0) {
            int from=p.Bag.IndexOf(item);int destination=Get(slot)!=null?p.Hotbar.IndexOf(Get(slot)):-1;
            if(destination<0)destination=p.Hotbar.FirstEmpty();
            if(from<0 || destination<0 || !Inventory.Move(p.Bag,from,p.Hotbar,destination)){NotificationUI.Show("Make room in the hotbar to equip a hand item");return false;}
        }
        Unequip(slot);

        var anchor = GetAnchor(slot);
        if (anchor != null && item.worldPrefab != null)
        {
            var obj = Instantiate(item.worldPrefab, anchor);
            obj.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
            obj.transform.localScale *= Mathf.Max(0.01f, item.heldScale);
            foreach (var col in obj.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var rb in obj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            objects[slot] = obj;
        }

        items[slot] = item;

        if (Character.Stats != null && item.statModifiers != null)
            foreach (var m in item.statModifiers)
                Character.Stats.AddModifier(new StatModifier(m, sources[slot]));

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
        {
            if (item.IsWeapon) weaponHitbox.SetProfile(item.weapon);
            else               weaponHitbox.ClearProfile();
        }

        var equipContext = EffectContext.For(Character, item);
        equipContext.ModifierSource = sources[slot];   // removed with the slot's stat modifiers on unequip
        ItemEffectProcessor.Fire(item, EffectTrigger.OnEquip, equipContext);

        if(playSound && Character is Player){var style=UIFeedbackSettings.Shared;style?.Play(style.equipKey);}
        Changed?.Invoke();
        return true;
    }

    public ItemData Unequip(EquipmentSlot slot)
    {
        EnsureInitialized();
        if (!items.TryGetValue(slot, out var item)) return null;
        if (item == eating.Item) eating.Clear();

        if (objects.TryGetValue(slot, out var obj) && obj != null) Destroy(obj);
        objects.Remove(slot);
        items.Remove(slot);

        if (Character.Stats != null)
            Character.Stats.RemoveAllFromSource(sources[slot]);

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
            weaponHitbox.ClearProfile();

        ItemEffectProcessor.Fire(item, EffectTrigger.OnUnequip, EffectContext.For(Character, item));

        Changed?.Invoke();
        return item;
    }

    public bool Unequip(ItemData item)
    {
        if (!IsEquipped(item)) return false;
        Unequip(item.equipSlot);
        return true;
    }

    public void UnequipAll()
    {
        foreach (var slot in new List<EquipmentSlot>(items.Keys))
            Unequip(slot);
    }
}
