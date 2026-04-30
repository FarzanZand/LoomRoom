using UnityEngine;
using UnityEngine.UI;

public class InventorySlot : MonoBehaviour
{
    [SerializeField] public Image iconImage;
    [SerializeField] public Image background;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    public void Init(int index)
    {
        SlotIndex = index;
        Clear();
    }

    public void SetItem(ItemData data)
    {
        item = data;
        iconImage.sprite = data.icon;
        iconImage.enabled = data.icon != null;
        background.color = FilledColor;
    }

    public void Clear()
    {
        item = null;
        iconImage.enabled = false;
        background.color = EmptyColor;
    }

    public void OnClick()
    {
        if (item == null) return;
        InventorySystem.Instance.UseItem(SlotIndex);
        ItemHolder.Instance.HoldItem(item);
    }
}
