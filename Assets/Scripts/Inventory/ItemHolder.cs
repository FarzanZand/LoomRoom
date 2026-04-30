using UnityEngine;

public class ItemHolder : Singleton<ItemHolder>
{
    [SerializeField] private Transform roomPlayerRightHand;
    [SerializeField] private Transform roomPlayerLeftHand;
    [SerializeField] private Transform tablePlayerRightHand;
    [SerializeField] private Transform tablePlayerLeftHand;

    private GameObject heldObject;
    public ItemData HeldItem { get; private set; }

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

    public void HoldItem(ItemData item)
    {
        ClearHeld();
        if (item?.worldPrefab == null) return;

        var anchor = GetAnchor(item.equipSlot);
        if (anchor == null) { Debug.LogWarning("[ItemHolder] No hand anchor for active player."); return; }

        HeldItem   = item;
        heldObject = Instantiate(item.worldPrefab, anchor);

        foreach (var col in heldObject.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }

    public void ClearHeld()
    {
        if (heldObject != null) Destroy(heldObject);
        heldObject = null;
        HeldItem   = null;
    }
}
