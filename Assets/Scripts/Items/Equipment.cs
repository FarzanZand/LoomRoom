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
    public event Action<EquipmentSlot, ItemData> Equipped;
    public event Action<EquipmentSlot, ItemData> Unequipped;

    public Character Character { get; private set; }

    readonly Dictionary<EquipmentSlot, ItemData>   items   = new();
    readonly Dictionary<EquipmentSlot, GameObject> objects = new();
    readonly Dictionary<EquipmentSlot, object>     sources = new();

    public IEnumerable<ItemData> EquippedItems => items.Values;

    void Awake()
    {
        Character = GetComponent<Character>();
        if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<Hitbox>(true);
        EffectDispatcher.Ensure(gameObject);
        foreach (EquipmentSlot s in Enum.GetValues(typeof(EquipmentSlot)))
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

    public bool CanEquip(ItemData item) => item != null && item.canBeEquipped;

    public bool Equip(ItemData item)
    {
        if (!CanEquip(item)) return false;

        var slot = item.equipSlot;
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

        ItemEffectProcessor.Fire(item, EffectTrigger.OnEquip, EffectContext.For(Character, item));

        Equipped?.Invoke(slot, item);
        Changed?.Invoke();
        return true;
    }

    public ItemData Unequip(EquipmentSlot slot)
    {
        if (!items.TryGetValue(slot, out var item)) return null;

        if (objects.TryGetValue(slot, out var obj) && obj != null) Destroy(obj);
        objects.Remove(slot);
        items.Remove(slot);

        if (Character.Stats != null)
        {
            Character.Stats.RemoveAllFromSource(sources[slot]);
            Character.Stats.RemoveAllFromSource(item);   // permanent buffs the item's OnEquip effects added
        }

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
            weaponHitbox.ClearProfile();

        ItemEffectProcessor.Fire(item, EffectTrigger.OnUnequip, EffectContext.For(Character, item));

        Unequipped?.Invoke(slot, item);
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
