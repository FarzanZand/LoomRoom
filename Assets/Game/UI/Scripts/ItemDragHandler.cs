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

    public bool       IsDragging { get; private set; }
    public ItemSlotUI Source     { get; private set; }

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

    public void Begin(ItemSlotUI source, Vector2 size)
    {
        Source     = source;
        IsDragging = true;
        if (ghostImage == null) return;
        ghostImage.sprite   = source.Item != null ? source.Item.icon : null;
        ghostImage.color    = new Color(1f, 1f, 1f, 0.8f);
        ghostRect.sizeDelta = size;
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
        ghostRect.localPosition = pos;
    }
}
