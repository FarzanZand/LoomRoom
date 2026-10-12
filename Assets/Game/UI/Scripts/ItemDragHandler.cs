using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Draws the ghost icon while an ItemSlotUI is being dragged. The ghost Image is
// authored in the scene as a child of the canvas.
public class ItemDragHandler : Singleton<ItemDragHandler>
{
    [Tooltip("Image that follows the pointer while dragging. Authored in the scene, disabled by default.")]
    [SerializeField] Image ghostImage;

    Canvas rootCanvas;
    RectTransform ghostRect;

    public bool       IsDragging  { get; private set; }
    public ItemSlotUI Source      { get; private set; }
    // The dragged stack's slot (a grid item's anchor) and the cell within it that was grabbed.
    public int        SourceIndex { get; private set; }
    public Vector2Int GrabOffset  { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;
        if (ghostImage != null)
        {
            ghostRect = ghostImage.rectTransform;
            ghostImage.raycastTarget = false;
            ghostImage.gameObject.SetActive(false);
        }
    }

    public void Begin(ItemSlotUI source, Vector2 size, int sourceIndex = -1, Vector2Int grab = default)
    {
        Source      = source;
        IsDragging  = true;
        SourceIndex = sourceIndex >= 0 ? sourceIndex : source.Index;
        GrabOffset  = grab;
        grabbed     = grab;
        if (ghostImage == null) return;
        ghostImage.sprite   = source.Item != null ? source.Item.icon : null;
        ghostImage.color    = new Color(1f, 1f, 1f, 0.8f);
        ghostImage.preserveAspect = true;
        var cells = source.Container != null && source.Item != null ? source.Container.SizeOf(source.Item) : Vector2Int.one;
        ghostRect.sizeDelta = size;
        ItemGridUI.UprightIcon(ghostRect, size, cells);
        footprint = size;
        ghostImage.gameObject.SetActive(true);
        ghostImage.transform.SetAsLastSibling();
        Follow();
    }

    public void End()
    {
        IsDragging = false;
        Source     = null;
        if (ghostImage != null) ghostImage.gameObject.SetActive(false);
    }

    void Update()
    {
        if (IsDragging) Follow();
    }

    void Follow()
    {
        if (ghostRect == null || rootCanvas == null || Mouse.current == null) return;
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? rootCanvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(), Mouse.current.position.ReadValue(), cam, out var pos);
        // Held by the grabbed cell: the ghost's top-left sits up and left of the pointer by that much.
        var size = footprint;
        var cell = Source != null ? Source.GetComponent<RectTransform>().rect.size : Vector2.zero;
        ghostRect.localPosition = pos + new Vector2(size.x * .5f - cell.x * (grabbed.x + .5f), -size.y * .5f + cell.y * (grabbed.y + .5f));
    }
    Vector2Int grabbed;
    Vector2 footprint;
}
