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
    [Header("Equipped (beside a gear tooltip)")]
    [SerializeField, Tooltip("A second panel like the first, shown next to it with what the player has in that slot.")]
    GameObject comparePanel;
    [SerializeField] TextMeshProUGUI compareNameText;
    [SerializeField] TextMeshProUGUI compareTypeText;
    [SerializeField] TextMeshProUGUI compareDescriptionText;
    [SerializeField, Min(0f)] float compareGap = 8f;

    RectTransform rect, compareRect;
    CanvasGroup visibility, compareVisibility;
    Tween fade, compareFade;

    protected override void Awake()
    {
        base.Awake();
        if (panel != null)
        {
            rect = panel.GetComponent<RectTransform>();
            visibility = panel.GetComponent<CanvasGroup>();
            panel.SetActive(false);
        }
        if (comparePanel != null)
        {
            compareRect = comparePanel.GetComponent<RectTransform>();
            compareVisibility = comparePanel.GetComponent<CanvasGroup>();
            comparePanel.SetActive(false);
        }
    }

    void Update()
    {
        if (panel != null && panel.activeSelf) FollowMouse();
    }

    public void Show(ItemData item)
    {
        if (panel == null || item == null) return;
        ShowText(Title(item), Kind(item), item.BuildTooltip());
        var equipped = item.Replaces();
        if (equipped != null && comparePanel != null) ShowCompare(equipped);
    }

    // Honed gear's name in the honed blue; the type line says what it is and gear its tier ("ONE-HANDED SWORD  ·  TIER 1").
    static string Title(ItemData item) => item.HasRarityColour ? $"<color={item.RarityTextColour}>{item.itemName}</color>" : item.itemName;

    static string Kind(ItemData item)
    {
        string kind = item.TypeLine().ToUpperInvariant();
        if (item.canBeEquipped && !item.IsConsumable && item.tier > 0) kind += $"  ·  TIER {item.tier}";
        return kind;
    }

    void ShowCompare(ItemData equipped)
    {
        if (compareNameText != null) compareNameText.text = Title(equipped);
        if (compareTypeText != null) compareTypeText.text = "EQUIPPED  ·  " + Kind(equipped);
        if (compareDescriptionText != null) compareDescriptionText.text = equipped.BuildTooltip();
        comparePanel.SetActive(true);
        var overlay = comparePanel.GetComponent<Canvas>(); if (overlay != null) overlay.overrideSorting = true;
        comparePanel.transform.SetAsLastSibling();
        compareFade?.Kill();
        if (compareVisibility != null) { compareVisibility.alpha = 0; compareFade = compareVisibility.DOFade(1, .12f).SetUpdate(true); }
        LayoutRebuilder.ForceRebuildLayoutImmediate(compareRect);
        FollowMouse();
    }

    public void ShowText(string title, string subtitle, string description)
    {
        if (panel == null) return;
        HideCompare();
        if (nameText != null) nameText.text = title;
        if (typeText != null) typeText.text = subtitle;
        if (descriptionText != null) descriptionText.text = description;
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
        HideCompare();
    }

    void HideCompare()
    {
        compareFade?.Kill();
        if (comparePanel != null) comparePanel.SetActive(false);
    }

    readonly Vector3[] corners = new Vector3[4];

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
        rect.GetWorldCorners(corners);
        float dx=corners[0].x<8?8-corners[0].x:corners[2].x>Screen.width-8?Screen.width-8-corners[2].x:0;
        float dy=corners[0].y<8?8-corners[0].y:corners[2].y>Screen.height-8?Screen.height-8-corners[2].y:0;
        rect.position+=new Vector3(dx,dy);
        if (comparePanel != null && comparePanel.activeSelf) PlaceCompare();
    }

    // Beside the tooltip on the side away from the mouse, tops level; on the other side when that runs off screen.
    void PlaceCompare()
    {
        if (compareRect == null) return;
        rect.GetWorldCorners(corners);
        float left = corners[0].x, right = corners[2].x, top = corners[1].y;
        float scale = compareRect.lossyScale.x;
        float width = compareRect.rect.width * scale, gap = compareGap * scale;
        bool toRight = rect.pivot.x == 0f;
        if (toRight && right + gap + width > Screen.width - 8) toRight = false;
        else if (!toRight && left - gap - width < 8) toRight = true;
        compareRect.pivot = new Vector2(toRight ? 0f : 1f, 1f);
        compareRect.position = new Vector3(toRight ? right + gap : left - gap, top, 0f);
        compareRect.GetWorldCorners(corners);
        float dy = corners[0].y < 8 ? 8 - corners[0].y : 0f;
        compareRect.position += new Vector3(0f, dy);
    }
}
