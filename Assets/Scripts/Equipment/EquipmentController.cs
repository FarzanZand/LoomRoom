using System.Collections.Generic;
using UnityEngine;
using MFPC;

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

        NotifyAnimator(slot, true);
    }

    public void Unequip(EquipmentSlot slot)
    {
        if (equipped.TryGetValue(slot, out var go))
        {
            Destroy(go);
            equipped.Remove(slot);
        }

        NotifyAnimator(slot, false);
    }

    private void NotifyAnimator(EquipmentSlot slot, bool equipped)
    {
        var pc = PlayerController.instance;
        if (pc == null) { Debug.LogWarning("[EquipmentController] PlayerController.instance is null"); return; }
        if (pc.armsAnimator == null) { Debug.LogWarning("[EquipmentController] armsAnimator is null on PlayerController"); return; }

        Debug.Log($"[EquipmentController] NotifyAnimator slot={slot} equipped={equipped}");
        switch (slot)
        {
            case EquipmentSlot.RightHand: pc.SetRightHandEquipped(equipped); break;
            case EquipmentSlot.LeftHand:  pc.SetLeftHandEquipped(equipped);  break;
        }
    }

    private Transform GetAttachment(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.RightHand => rightHandAttachment,
        _ => null
    };
}
