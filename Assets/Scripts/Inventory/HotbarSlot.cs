using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class HotbarSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] public Image iconImage;
    [SerializeField] public Image background;
    [SerializeField] public TextMeshProUGUI keyLabel;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    public void Init(int index)
    {
        SlotIndex = index;
        if (keyLabel != null) keyLabel.text = (index + 1).ToString();
        Clear();
    }

    public void SetItem(ItemData data)
    {
        item = data;
        iconImage.sprite  = data.icon;
        iconImage.enabled = data.icon != null;
        background.color  = FilledColor;
    }

    public void Clear()
    {
        item = null;
        iconImage.enabled = false;
        background.color  = EmptyColor;
    }

    public void Pulse()
    {
        StopAllCoroutines();
        StartCoroutine(PulseRoutine());
    }

    private System.Collections.IEnumerator PulseRoutine()
    {
        float duration = 0.07f;
        Vector3 big    = Vector3.one * 1.18f;

        float t = 0f;
        while (t < duration)
        {
            transform.localScale = Vector3.Lerp(Vector3.one, big, t / duration);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        t = 0f;
        while (t < duration)
        {
            transform.localScale = Vector3.Lerp(big, Vector3.one, t / duration);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        transform.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (item != null) TooltipUI.Instance.Show(item);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (item == null || eventData.button != PointerEventData.InputButton.Right) return;
        if (item.itemType != ItemType.Consumable) return;
        item.UseEffect();
        HotbarSystem.Instance.Remove(SlotIndex);
    }
}
