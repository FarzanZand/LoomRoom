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
    // The slot the drag started on (null when it started on the paper doll).
    public ItemSlotUI Source      { get; private set; }
    // Where the dragged stack lives: a container and its slot (a grid item's anchor), or a worn item's
    // equipment slot (no container). A hand item dragged off the doll lives in the hotbar.
    public Inventory  SourceContainer { get; private set; }
    public int        SourceIndex { get; private set; }
    public EquipmentSlot? WornSlot { get; private set; }
    public ItemData   Item        { get; private set; }
    // The cell within the item that was grabbed.
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
        var cell = source.GetComponent<RectTransform>().rect.size;
        BeginDrag(source, source.Container, sourceIndex >= 0 ? sourceIndex : source.Index, null, source.Item, size, cell, grab);
    }

    // From the shop (ShopUI): only the ghost; the shop decides what a drop does.
    public void BeginGhost(ItemData item, Vector2 size)
    {
        BeginDrag(null, null, -1, null, item, size, size, Vector2Int.zero);
    }

    // From the paper doll: a hand item is dragged out of its hotbar slot, a worn item out of its equipment slot.
    public void BeginEquipped(Inventory container, int index, EquipmentSlot? worn, ItemData item, Vector2 size)
    {
        BeginDrag(null, container, index, worn, item, size, size, Vector2Int.zero);
    }

    void BeginDrag(ItemSlotUI source, Inventory container, int index, EquipmentSlot? worn, ItemData item, Vector2 size, Vector2 cell, Vector2Int grab)
    {
        Source          = source;
        SourceContainer = container;
        SourceIndex     = index;
        WornSlot        = worn;
        Item            = item;
        IsDragging      = true;
        GrabOffset      = grab;
        grabbed         = grab;
        cellSize        = cell;
        if (ghostImage == null) return;
        ghostImage.sprite   = item != null ? item.icon : null;
        ghostImage.color    = new Color(1f, 1f, 1f, 0.8f);
        ghostImage.preserveAspect = true;
        var cells = container != null && item != null ? container.SizeOf(item) : Vector2Int.one;
        if (worn.HasValue && item != null) cells = item.GridSize;
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
        SourceContainer = null;
        WornSlot   = null;
        Item       = null;
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
        var cell = cellSize;
        ghostRect.localPosition = pos + new Vector2(size.x * .5f - cell.x * (grabbed.x + .5f), -size.y * .5f + cell.y * (grabbed.y + .5f));
    }
    Vector2Int grabbed;
    Vector2 footprint, cellSize;
}
