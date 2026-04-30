using UnityEngine;

public enum ItemType { Generic, Weapon, Tool, Consumable, Key }

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public GameObject worldPrefab;
    public ItemType itemType;

    public bool canBeEquipped = false;
    public bool equipOnPickup = false;
    public EquipmentSlot equipSlot = EquipmentSlot.RightHand;
}
