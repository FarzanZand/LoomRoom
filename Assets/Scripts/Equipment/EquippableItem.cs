using UnityEngine;

public class EquippableItem : MonoBehaviour, IInteractable, IEquippable
{
    [SerializeField] private string itemName = "Item";
    [SerializeField] private GameObject equippedPrefab;
    [SerializeField] private EquipmentSlot slot = EquipmentSlot.RightHand;
    [SerializeField] private string promptMessage = "Press F to pick up";

    public string ItemName => itemName;
    public GameObject EquippedPrefab => equippedPrefab;
    public string PromptMessage => promptMessage;

    public void Interact(GameObject interactor)
    {
        var equipment = interactor.GetComponent<EquipmentController>();
        if (equipment == null)
        {
            Debug.LogWarning($"[EquippableItem] No EquipmentController found on interactor '{interactor.name}'");
            return;
        }

        equipment.Equip(this, slot);
        gameObject.SetActive(false);
    }
}
