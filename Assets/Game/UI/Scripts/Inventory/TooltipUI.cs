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
    [Header("Compact: icon slot, rarity line, compare bars")]
    [SerializeField, Tooltip("The item's icon in the header.")] Image icon;
    [SerializeField, Tooltip("The icon's slot; its colour is the rarity border.")] Image iconSlot;
    [SerializeField, Tooltip("The line under the header, in the rarity colour.")] Image rarityLine;
    [SerializeField, Tooltip("The bars' parent: off for tooltips without them, so they leave no gap.")] GameObject compareBars;
    [SerializeField, Tooltip("The main number (damage, armour) against what is worn.")] CompareBar mainBar = new();
    [SerializeField, Tooltip("A weapon's swing speed against the held weapon's.")] CompareBar speedBar = new();
    [SerializeField, Tooltip("Under the bars: what grey stands for (\"Grey: your Iron Sword\").")] TextMeshProUGUI compareNote;
    [SerializeField] Color lineDefault = new(.3f, .3f, .33f);
    [Header("Stone plate: footer and the equipped panel's header")]
    [SerializeField, Tooltip("The footer row (left note, coin and price); hidden for text-only tooltips.")] GameObject footer;
    [SerializeField] TextMeshProUGUI footerNote, footerPrice;
    [SerializeField] Image compareIcon, compareIconSlot, compareRarityLine;
    [SerializeField] GameObject compareFooter;
    [SerializeField] TextMeshProUGUI compareFooterNote, compareFooterPrice;

    // One compare bar: grey up to what is worn, green for what this adds, red for what it would lose.
    [System.Serializable]
    public class CompareBar
    {
        public GameObject root;
        public TextMeshProUGUI label, value;
        public Image worn, gain, loss;
    }

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

    public void Show(ItemData item) => Show(item, null);

    // at: the control a pad or the arrow keys picked (a bag cell): the tooltip sits beside it instead of at the mouse.
    public void Show(ItemData item, RectTransform at)
    {
        if (panel == null || item == null) return;
        bool bars = mainBar.root != null && item.TryMainStat(out _, out _, out _);
        anchor = at;
        ShowLines(Title(item), Kind(item), item.BuildTooltip(!bars, footer == null));
        ShowHeader(item);
        var gear = PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Equipment : null;
        ShowFooter(footer, footerNote, footerPrice, item, gear != null && gear.IsEquipped(item) ? "Equipped" : "");
        if (compareBars != null) compareBars.SetActive(bars);
        if (bars) ShowBars(item);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        FollowMouse();
        var equipped = item.Replaces();
        if (equipped != null && comparePanel != null) ShowCompare(equipped);
    }

    // The name in its rarity colour (Common stays white); the type line says the rarity and what it is ("UNCOMMON  ·  HELM").
    static string Title(ItemData item) => item.HasRarityColour ? $"<color={item.RarityTextColour}>{item.itemName}</color>" : item.itemName;

    static string Kind(ItemData item)
    {
        string kind = item.TypeLine().ToUpperInvariant();
        return item.IsGear ? $"{item.RarityName.ToUpperInvariant()}  ·  {kind}" : kind;
    }

    void ShowHeader(ItemData item)
    {
        Color colour = item.HasSlotFill ? Color.Lerp(item.RarityColour, Color.white, item.HasRarityColour ? .25f : .1f) : lineDefault;
        if (icon != null) { icon.sprite = item.icon; icon.enabled = item.icon != null; icon.preserveAspect = true; }
        if (iconSlot != null) iconSlot.gameObject.SetActive(item.icon != null);
        if (rarityLine != null) rarityLine.color = colour;
    }

    // The main number against what the player wears in that slot, and a weapon's swing speed against the held one's.
    void ShowBars(ItemData item)
    {
        item.TryMainStat(out string label, out float value, out string text);
        var equipped = item.Replaces();
        var gear = PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Equipment : null;
        bool wearing = gear != null && gear.IsEquipped(item);
        float worn = wearing ? value : equipped != null && equipped.TryMainStat(out _, out float w, out _) ? w : 0f;
        float scale = Mathf.Max(value, worn, item.IsWeapon || item.itemType == ItemType.Shield ? 16f : item.equipSlot == EquipmentSlot.Body ? 6f : 5f) * 1.1f;
        Fill(mainBar, label == "Armour when blocking" ? "Block" : label, value / scale, worn / scale, text, value - worn);

        bool speed = item.IsWeapon && speedBar.root != null;
        if (speedBar.root != null) speedBar.root.SetActive(speed);
        if (speed)
        {
            float mine = item.SwingSpeed, theirs = wearing ? mine : equipped != null ? equipped.SwingSpeed : 0f;
            // -0.5 (very slow) to +0.5 (very fast) across the bar.
            Fill(speedBar, "Speed", Mathf.Clamp01(mine + .5f), Mathf.Clamp01(theirs + .5f), ItemData.SwingSpeedWord(mine).ToLowerInvariant(), (mine - theirs) * 100f, "%");
        }
        if (compareNote != null)
        {
            compareNote.gameObject.SetActive(true);
            compareNote.text = wearing ? "Equipped" : equipped != null ? $"Grey: your {equipped.itemName}" : "Nothing worn there";
        }
    }

    static void Fill(CompareBar bar, string label, float mine, float worn, string text, float delta, string unit = "")
    {
        if (bar.root == null) return;
        bar.root.SetActive(true);
        mine = Mathf.Clamp01(mine); worn = Mathf.Clamp01(worn);
        float low = Mathf.Min(mine, worn), high = Mathf.Max(mine, worn);
        Span(bar.worn, 0f, low);
        Span(bar.gain, low, mine > worn ? high : low);
        Span(bar.loss, low, mine < worn ? high : low);
        if (bar.label != null) bar.label.text = label;
        string change = Mathf.Abs(delta) < .01f ? $"<color={ItemData.DimColor}>same</color>"
            : $"<color={(delta > 0 ? ItemData.GoodColor : ItemData.BadColor)}>{(delta > 0 ? "+" : "")}{delta:0.#}{unit}</color>";
        if (bar.value != null) bar.value.text = $"{text}  {change}";
    }

    static void Span(Image image, float from, float to)
    {
        if (image == null) return;
        image.enabled = to - from > .001f;
        var r = image.rectTransform;
        r.anchorMin = new Vector2(from, 0f); r.anchorMax = new Vector2(to, 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    // The footer: a short note on the left (Equipped), the price on the right; hidden when there is neither.
    static void ShowFooter(GameObject row, TextMeshProUGUI note, TextMeshProUGUI price, ItemData item, string text)
    {
        if (row == null) return;
        bool priced = item != null && item.value > 0 && item.itemType != ItemType.Key && item.itemType != ItemType.Spoils;
        row.SetActive(priced || !string.IsNullOrEmpty(text));
        if (note != null) note.text = text;
        if (price != null) { price.text = priced ? $"{item.value} gold" : ""; price.transform.parent.gameObject.SetActive(priced); }
    }

    // Plain text tooltips (skills, buffs) have no item header or bars.
    void HideItemParts()
    {
        if (footer != null) footer.SetActive(false);
        if (compareBars != null) compareBars.SetActive(false);
        if (iconSlot != null) iconSlot.gameObject.SetActive(false);
        if (rarityLine != null) rarityLine.color = lineDefault;
        if (mainBar.root != null) mainBar.root.SetActive(false);
        if (speedBar.root != null) speedBar.root.SetActive(false);
        if (compareNote != null) compareNote.gameObject.SetActive(false);
    }

    void ShowCompare(ItemData equipped)
    {
        if (compareNameText != null) compareNameText.text = Title(equipped);
        // The panel's own tab says EQUIPPED; without one the type line does.
        if (compareTypeText != null) compareTypeText.text = (compareIcon != null ? "" : "EQUIPPED  ·  ") + Kind(equipped);
        if (compareDescriptionText != null) compareDescriptionText.text = equipped.BuildTooltip(true, compareFooter == null);
        if (compareIcon != null) { compareIcon.sprite = equipped.icon; compareIcon.enabled = equipped.icon != null; compareIcon.preserveAspect = true; }
        if (compareIconSlot != null) compareIconSlot.gameObject.SetActive(equipped.icon != null);
        if (compareRarityLine != null) compareRarityLine.color = equipped.HasSlotFill ? Color.Lerp(equipped.RarityColour, Color.white, equipped.HasRarityColour ? .25f : .1f) : lineDefault;
        ShowFooter(compareFooter, compareFooterNote, compareFooterPrice, equipped, "");
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
        anchor = null;
        ShowLines(title, subtitle, description);
    }

    void ShowLines(string title, string subtitle, string description)
    {
        if (panel == null) return;
        HideCompare();
        HideItemParts();
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
        anchor = null;
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
    // Shown for a selected control: placed beside it, not at the mouse (null: the mouse).
    RectTransform anchor;

    void FollowMouse()
    {
        if (rect == null || anchor == null && Mouse.current == null) return;
        Vector2 mouse;
        float halfWidth = 0f;
        if (anchor != null)
        {
            // The control's middle, and half its width so the tooltip clears its side (the HUD is a screen space canvas).
            anchor.GetWorldCorners(corners);
            mouse = (corners[0] + corners[2]) * .5f;
            halfWidth = (corners[2].x - corners[0].x) * .5f;
        }
        else mouse = Mouse.current.position.ReadValue();
        float pivotX = mouse.x < Screen.width  * 0.5f ? 0f : 1f;
        float pivotY = mouse.y < Screen.height * 0.5f ? 0f : 1f;
        rect.pivot = new Vector2(pivotX, pivotY);
        float ox = pivotX == 0f ?  16f + halfWidth : -16f - halfWidth;
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
