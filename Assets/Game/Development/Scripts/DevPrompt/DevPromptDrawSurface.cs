#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The enlarged screenshot in the developer prompt: dragging on it draws on the screenshot (DevPromptMarkup) in its own
// pixels, wherever the picture sits and however big it is drawn (an AspectRatioFitter keeps this rect exactly the
// picture's shape, so a point maps straight across).
[RequireComponent(typeof(RawImage))]
public class DevPromptDrawSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public DevPromptMarkup Markup { get; set; }
    public System.Action Changed;

    RectTransform rect;
    void Awake() => rect = (RectTransform)transform;

    bool ToPixel(PointerEventData e, out Vector2Int pixel)
    {
        pixel = default;
        if (Markup == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local)) return false;
        var r = rect.rect;
        float u = (local.x - r.xMin) / r.width, v = (local.y - r.yMin) / r.height;
        pixel = new Vector2Int(Mathf.FloorToInt(u * Markup.Width), Mathf.FloorToInt(v * Markup.Height));
        return true;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || !ToPixel(e, out var p)) return;
        Markup.BeginStroke(p);
        Changed?.Invoke();
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || !ToPixel(e, out var p)) return;
        Markup.ContinueStroke(p);
        Changed?.Invoke();
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (Markup == null) return;
        Markup.EndStroke();
        Changed?.Invoke();
    }
}
#endif
