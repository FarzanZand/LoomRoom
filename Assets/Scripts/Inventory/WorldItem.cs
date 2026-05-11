using UnityEngine;

public class WorldItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;

    public void Init(ItemData data) => itemData = data;

    public string PromptMessage => itemData != null ? $"Pick up {itemData.itemName}" : "Pick up";

    public void Interact(GameObject interactor)
    {
        if (itemData == null) return;

        bool hotbarCandidate = itemData.itemType == ItemType.Weapon
                            || itemData.itemType == ItemType.Tool
                            || itemData.itemType == ItemType.Consumable;

        bool added = hotbarCandidate && HotbarSystem.Instance.TryAdd(itemData);
        if (!added) added = InventorySystem.Instance.TryAdd(itemData);

        if (!added)
        {
            Debug.Log("[WorldItem] Inventory full.");
            return;
        }

        if (itemData.equipOnPickup && ItemHolder.Instance.GetHeldItem(itemData.equipSlot) == null)
            ItemHolder.Instance.HoldItem(itemData);
        Destroy(transform.parent != null ? transform.parent.gameObject : gameObject);
    }
}
