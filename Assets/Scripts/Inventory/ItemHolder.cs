using UnityEngine;

public class ItemHolder : Singleton<ItemHolder>
{
    [SerializeField] private Transform roomPlayerRightHand;
    [SerializeField] private Transform tablePlayerRightHand;

    private GameObject heldObject;
    public ItemData HeldItem { get; private set; }

    Transform ActiveHand => PlayerManager.Instance.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer
        ? tablePlayerRightHand
        : roomPlayerRightHand;

    public void HoldItem(ItemData item)
    {
        ClearHeld();
        if (item?.worldPrefab == null) return;

        var anchor = ActiveHand;
        if (anchor == null) { Debug.LogWarning("[ItemHolder] No hand anchor for active player."); return; }

        HeldItem    = item;
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
