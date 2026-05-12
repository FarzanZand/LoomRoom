using System.Collections.Generic;
using MFPC;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class ItemHolder : Singleton<ItemHolder>
{
    [SerializeField] private Transform roomPlayerRightHand;
    [SerializeField] private Transform roomPlayerLeftHand;
    [SerializeField] private Transform tablePlayerRightHand;
    [SerializeField] private Transform tablePlayerLeftHand;
    [SerializeField] private Animator tablePlayerArmsAnimator;

    public event System.Action OnHeldItemChanged;

    private readonly Dictionary<EquipmentSlot, GameObject> heldObjects = new();
    private readonly Dictionary<EquipmentSlot, ItemData>   heldItems   = new();

    bool IsTablePlayer => PlayerManager.Instance == null || PlayerManager.Instance.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer;

    Transform GetAnchor(EquipmentSlot slot)
    {
        bool table = IsTablePlayer;
        return slot switch
        {
            EquipmentSlot.LeftHand => table ? tablePlayerLeftHand : roomPlayerLeftHand,
            _                      => table ? tablePlayerRightHand : roomPlayerRightHand,
        };
    }

    private void Start()
    {
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnPlayerSwapped += OnPlayerSwapped;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnPlayerSwapped -= OnPlayerSwapped;
    }

    private void OnPlayerSwapped(PlayerManager.ActivePlayer _)
    {
        foreach (var slot in new List<EquipmentSlot>(heldItems.Keys))
            NotifyAnimator(slot, true);
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
        NotifyAnimator(item.equipSlot, true);
        UpdateHitbox(item.equipSlot, item);
    }

    public void ClearSlot(EquipmentSlot slot)
    {
        if (heldObjects.TryGetValue(slot, out var obj) && obj != null)
            Destroy(obj);
        heldObjects.Remove(slot);
        heldItems.Remove(slot);
        OnHeldItemChanged?.Invoke();
        NotifyAnimator(slot, false);
        UpdateHitbox(slot, null);
    }

    private void NotifyAnimator(EquipmentSlot slot, bool equipped)
    {
        if (IsTablePlayer)
        {
            if (tablePlayerArmsAnimator == null) return;
            switch (slot)
            {
                case EquipmentSlot.RightHand: tablePlayerArmsAnimator.SetBool("RightHandEquipped", equipped); break;
                case EquipmentSlot.LeftHand:  tablePlayerArmsAnimator.SetBool("LeftHandEquipped",  equipped); break;
            }
        }
        else
        {
            var pc = PlayerController.instance;
            if (pc == null) return;
            switch (slot)
            {
                case EquipmentSlot.RightHand: pc.SetRightHandEquipped(equipped); break;
                case EquipmentSlot.LeftHand:  pc.SetLeftHandEquipped(equipped);  break;
            }
        }
    }

    private void UpdateHitbox(EquipmentSlot slot, ItemData item)
    {
        if (slot != EquipmentSlot.RightHand) return;

        Transform hand = IsTablePlayer ? tablePlayerRightHand : roomPlayerRightHand;
        var hitbox = hand.GetComponent<WeaponHitbox>();
        if (hitbox == null) return;

        if (item != null && item.itemType == ItemType.Weapon)
            hitbox.SetWeapon(item.attackRange, item.attackDamage);
        else
            hitbox.ClearWeapon();
    }

    public void ClearAll()
    {
        foreach (var slot in new List<EquipmentSlot>(heldObjects.Keys))
            ClearSlot(slot);
    }
}
