using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One ware in the shop: icon, name, a short info line, count and price. Hover shows details; click buys.
public class ShopRowUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    [SerializeField] Image icon;
    [SerializeField] TMP_Text itemName;
    [SerializeField] TMP_Text info;
    [SerializeField] TMP_Text count;
    [SerializeField] TMP_Text price;
    [SerializeField] Button button;
    [SerializeField] CanvasGroup group;

    ItemData item;
    Action click;
    Action<ItemData> hover;

    void Awake() => button?.onClick.AddListener(() => click?.Invoke());

    public void Bind(ItemData data, int amount, string cost, bool available, Action onClick, Action<ItemData> onHover)
    {
        gameObject.SetActive(true);
        item = data; click = onClick; hover = onHover;
        if (icon != null) { icon.sprite = data != null ? data.icon : null; icon.enabled = icon.sprite != null; }
        if (itemName != null) itemName.text = data != null ? data.itemName : "";
        if (info != null) info.text = Info(data);
        if (count != null) count.text = amount > 1 ? $"x{amount}" : "";
        if (price != null) price.text = cost;
        if (button != null) button.interactable = available;
        if (group != null) group.alpha = available ? 1f : .5f;
    }

    // What kind of thing it is, in a few words: "Helm, +1 armor", "Weapon, +6 damage", "Heals 6 over 6s".
    public static string Info(ItemData data)
    {
        if (data == null) return "";
        string effect = null;
        if (data.effects != null)
            foreach (var e in data.effects) { effect = e?.Describe(); if (!string.IsNullOrWhiteSpace(effect)) break; }
        if (data.IsConsumable) return effect ?? "";
        string kind = data.spell != null ? "Spell tome"
            : data.itemType == ItemType.Weapon ? "Weapon"
            : data.itemType == ItemType.Shield ? "Shield"
            : data.itemType == ItemType.Equipment ? SlotName(data.equipSlot)
            : data.itemType.ToString();
        string stat = null;
        if (data.statModifiers != null)
            foreach (var m in data.statModifiers)
                if (m != null) { stat = ItemData.StatLine(m.stat, m.type, m.value, data.itemType == ItemType.Shield && m.stat == StatType.Armor); break; }
        if (data.spell != null) stat = $"{data.spell.manaCost:0.#} mana";
        stat ??= effect;
        return string.IsNullOrWhiteSpace(stat) ? kind : $"{kind}, {stat}";
    }

    static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => "Helm",
        EquipmentSlot.Body => "Armor",
        EquipmentSlot.Legs => "Leggings",
        EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => "Trinket",
        _ => slot.ToString(),
    };

    public void Clear()
    {
        item = null; click = null;
        gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => hover?.Invoke(item);
    public void OnSelect(BaseEventData eventData) => hover?.Invoke(item);
}
