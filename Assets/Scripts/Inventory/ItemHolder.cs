using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class ItemHolder : Singleton<ItemHolder>
{
    [SerializeField] private Transform roomPlayerRightHand;
    [SerializeField] private Transform roomPlayerLeftHand;
    [SerializeField] private Transform tablePlayerRightHand;
    [SerializeField] private Transform tablePlayerLeftHand;

    public event System.Action OnHeldItemChanged;

    private readonly Dictionary<EquipmentSlot, GameObject> heldObjects = new();
    private readonly Dictionary<EquipmentSlot, ItemData>   heldItems   = new();

    bool IsTablePlayer => PlayerManager.Instance.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer;

    Transform GetAnchor(EquipmentSlot slot)
    {
        bool table = IsTablePlayer;
        return slot switch
        {
            EquipmentSlot.LeftHand => table ? tablePlayerLeftHand : roomPlayerLeftHand,
            _                      => table ? tablePlayerRightHand : roomPlayerRightHand,
        };
    }

    public ItemData GetHeldItem(EquipmentSlot slot) =>
        heldItems.TryGetValue(slot, out var item) ? item : null;

    public void HoldItem(ItemData item)
    {
        if (item?.worldPrefab == null) { Debug.LogWarning($"[ItemHolder] {item?.itemName} has no worldPrefab assigned."); return; }

        ClearSlot(item.equipSlot);

        var anchor = GetAnchor(item.equipSlot);
        if (anchor == null) { Debug.LogWarning($"[ItemHolder] No hand anchor for active player ({PlayerManager.Instance.CurrentPlayer})."); return; }

        var obj = Instantiate(item.worldPrefab, anchor);
        foreach (var col in obj.GetComponentsInChildren<Collider>())
            col.enabled = false;

        heldObjects[item.equipSlot] = obj;
        heldItems[item.equipSlot]   = item;
        OnHeldItemChanged?.Invoke();
    }

    public void ClearSlot(EquipmentSlot slot)
    {
        if (heldObjects.TryGetValue(slot, out var obj) && obj != null)
            Destroy(obj);
        heldObjects.Remove(slot);
        heldItems.Remove(slot);
        OnHeldItemChanged?.Invoke();
    }

    public void ClearAll()
    {
        foreach (var slot in new List<EquipmentSlot>(heldObjects.Keys))
            ClearSlot(slot);
    }
}
