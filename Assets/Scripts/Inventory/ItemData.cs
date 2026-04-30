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

    [Header("Consumable")]
    public ItemEffect itemEffect;
    public float effectValue;
    public ItemEffectBase customEffect;

    public void UseEffect()
    {
        if (itemType != ItemType.Consumable) return;

        switch (itemEffect)
        {
            case ItemEffect.Heal:
                Debug.Log($"healed for {effectValue} health");
                break;
            case ItemEffect.Feed:
                Debug.Log($"{itemName} eaten.");
                break;
            case ItemEffect.Custom:
                customEffect?.Apply(this);
                break;
        }
    }
}
