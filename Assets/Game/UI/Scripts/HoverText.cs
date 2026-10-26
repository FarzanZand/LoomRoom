using UnityEngine;
using UnityEngine.EventSystems;

// A plain-text tooltip on any UI element (the item tooltip's look, TooltipUI.ShowText): a title, a line under it and a
// body. Set by code (the tavern's tabs and cards, the buff row) or in the inspector. Empty title: nothing shows.
public class HoverText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string title;
    public string subtitle;
    [TextArea(2, 5)] public string body;
    bool hovering;

    public void Set(string newTitle, string newSubtitle, string newBody)
    {
        title = newTitle; subtitle = newSubtitle; body = newBody;
        if (hovering) Show();
    }

    public void OnPointerEnter(PointerEventData data) { hovering = true; Show(); }
    public void OnPointerExit(PointerEventData data) { hovering = false; if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    void OnDisable() { if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide(); hovering = false; }

    void Show()
    {
        if (string.IsNullOrEmpty(title) || !TooltipUI.HasInstance) return;
        TooltipUI.Instance.ShowText(title, subtitle, body);
    }
}
