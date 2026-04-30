using System;
using Sirenix.OdinInspector;
using UnityEngine;

public enum ItemType { Generic, Weapon, Tool, Consumable, Key }

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public GameObject worldPrefab;
    public ItemType itemType;

    public bool canBeEquipped = false;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    public bool equipOnPickup = false;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    public EquipmentSlot equipSlot = EquipmentSlot.RightHand;

    [BoxGroup("Consumable"), ShowIf("IsConsumable")]
    public ItemEffect itemEffect;

    [BoxGroup("Consumable"), ShowIf("ShowEffectValue")]
    public float effectValue;

    [BoxGroup("Consumable"), ShowIf("ShowCustomEffect")]
    public ItemEffectBase customEffect;

    private bool IsConsumable => itemType == ItemType.Consumable;
    private bool ShowEffectValue => IsConsumable && itemEffect != ItemEffect.Custom;
    private bool ShowCustomEffect => IsConsumable && itemEffect == ItemEffect.Custom;

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
