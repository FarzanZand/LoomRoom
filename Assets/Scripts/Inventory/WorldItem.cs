using UnityEngine;

public class WorldItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;

    public string PromptMessage => itemData != null ? $"Pick up {itemData.itemName}" : "Pick up";

    public void Interact(GameObject interactor)
    {
        if (itemData == null) return;
        if (!InventorySystem.Instance.TryAdd(itemData))
        {
            Debug.Log("[WorldItem] Inventory full.");
            return;
        }
        ItemHolder.Instance.HoldItem(itemData);
        gameObject.SetActive(false);
    }
}
