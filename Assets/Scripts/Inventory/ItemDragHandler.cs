using UnityEngine;
using UnityEngine.UI;

public class ItemDragHandler : MonoBehaviour
{
    public static ItemDragHandler Instance { get; private set; }

    private Image ghostImage;
    private RectTransform ghostRect;
    private Canvas rootCanvas;

    public bool     IsDragging    { get; private set; }
    public bool     WasDropped    { get; private set; }
    public ItemData DraggedItem   { get; private set; }
    public bool     SourceIsHotbar { get; private set; }
    public int      SourceIndex   { get; private set; }
    public int      SourceCount   { get; private set; }

    void Awake()
    {
        Instance = this;
        rootCanvas = GetComponentInParent<Canvas>();

        var go = new GameObject("DragGhost");
        go.transform.SetParent(rootCanvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = Vector2.one * 0.5f;
        rt.sizeDelta = new Vector2(64, 64);
        go.AddComponent<CanvasRenderer>();
        ghostImage = go.AddComponent<Image>();
        ghostImage.raycastTarget = false;
        ghostRect = rt;
        go.SetActive(false);
    }

    public void BeginDrag(ItemData item, bool fromHotbar, int sourceIndex, int sourceCount, Sprite icon, Vector2 size)
    {
        DraggedItem    = item;
        SourceIsHotbar = fromHotbar;
        SourceIndex    = sourceIndex;
        SourceCount    = sourceCount;
        IsDragging     = true;
        WasDropped     = false;
        ghostImage.sprite = icon;
        ghostImage.color  = new Color(1f, 1f, 1f, 0.8f);
        ghostRect.sizeDelta = size;
        ghostImage.gameObject.SetActive(true);
        ghostImage.transform.SetAsLastSibling();
    }

    public void NotifyDropped() => WasDropped = true;

    public void EndDrag()
    {
        IsDragging = false;
        WasDropped = false;
        DraggedItem = null;
        ghostImage.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!IsDragging) return;
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceCamera
            ? rootCanvas.worldCamera
            : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(),
            Input.mousePosition, cam, out var pos);
        ghostRect.localPosition = pos;
    }
}
