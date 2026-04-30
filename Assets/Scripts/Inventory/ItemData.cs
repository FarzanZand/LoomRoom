using UnityEngine;

public enum ItemType { Generic, Weapon, Tool, Consumable, Key, Quest }

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public GameObject worldPrefab;
    public ItemType itemType;
}
