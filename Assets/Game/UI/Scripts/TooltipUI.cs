using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

public class TooltipUI : Singleton<TooltipUI>
{
    [SerializeField] GameObject panel;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI typeText;
    [SerializeField] TextMeshProUGUI descriptionText;

    RectTransform rect;
    CanvasGroup visibility;
    Tween fade;

    protected override void Awake()
    {
        base.Awake();
        if (panel != null)
        {
            rect = panel.GetComponent<RectTransform>();
            visibility = panel.GetComponent<CanvasGroup>();
            panel.SetActive(false);
        }
    }

    void Update()
    {
        if (panel != null && panel.activeSelf) FollowMouse();
    }

    public void Show(ItemData item)
    {
        if (panel == null || item == null) return;
        if (nameText != null)        nameText.text = item.itemName;
        if (typeText != null)        typeText.text = item.IsConsumable ? "CONSUMABLE" : item.canBeEquipped ? EquipmentSlotUI.Display(item.equipSlot) : item.itemType.ToString().ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = item.BuildTooltip();
        panel.SetActive(true);
        // Unity can reset overrideSorting while an authored nested canvas is inactive.
        var overlay=panel.GetComponent<Canvas>();if(overlay!=null)overlay.overrideSorting=true;
        panel.transform.SetAsLastSibling();
        fade?.Kill();
        if(visibility!=null){visibility.alpha=0;fade=visibility.DOFade(1,.12f).SetUpdate(true);}
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        FollowMouse();
    }

    public void Hide()
    {
        fade?.Kill();
        if (panel != null) panel.SetActive(false);
    }

    void FollowMouse()
    {
        if (rect == null || Mouse.current == null) return;
        Vector2 mouse = Mouse.current.position.ReadValue();
        float pivotX = mouse.x < Screen.width  * 0.5f ? 0f : 1f;
        float pivotY = mouse.y < Screen.height * 0.5f ? 0f : 1f;
        rect.pivot = new Vector2(pivotX, pivotY);
        float ox = pivotX == 0f ?  16f : -16f;
        float oy = pivotY == 0f ?  16f : -16f;
        rect.position = new Vector3(mouse.x + ox, mouse.y + oy, 0f);
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        float dx=corners[0].x<8?8-corners[0].x:corners[2].x>Screen.width-8?Screen.width-8-corners[2].x:0;
        float dy=corners[0].y<8?8-corners[0].y:corners[2].y>Screen.height-8?Screen.height-8-corners[2].y:0;
        rect.position+=new Vector3(dx,dy);
    }
}
