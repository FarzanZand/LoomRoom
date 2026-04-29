using System.Collections.Generic;
using UnityEngine;

public class EquipmentController : MonoBehaviour
{
    [SerializeField] private Transform rightHandAttachment;

    private readonly Dictionary<EquipmentSlot, GameObject> equipped = new();

    public IEquippable GetEquipped(EquipmentSlot slot)
    {
        if (equipped.TryGetValue(slot, out var go))
            return go.GetComponent<IEquippable>();
        return null;
    }

    public void Equip(IEquippable item, EquipmentSlot slot)
    {
        Unequip(slot);

        Transform attachment = GetAttachment(slot);
        if (attachment == null || item.EquippedPrefab == null) return;

        GameObject instance = Instantiate(item.EquippedPrefab, attachment);
        equipped[slot] = instance;
    }

    public void Unequip(EquipmentSlot slot)
    {
        if (equipped.TryGetValue(slot, out var go))
        {
            Destroy(go);
            equipped.Remove(slot);
        }
    }

    private Transform GetAttachment(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.RightHand => rightHandAttachment,
        _ => null
    };
}
