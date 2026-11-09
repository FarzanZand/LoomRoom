using TMPro;
using UnityEngine;

// The small card next to a hovered place on the campaign map: its name, what kind of place it is, how many floors,
// whether it has been beaten or a run waits there. Authored on the HUD canvas (built by CampaignCardSetup.Build());
// CampaignManager fills it and moves it beside the place. The hint line under the map shows while the map is up.
public class CampaignCardUI : MonoBehaviour
{
    public RectTransform card;
    public CanvasGroup group;
    public TMP_Text title, kind, description, status;
    [Tooltip("The line at the foot of the screen while the map is up.")]
    public TMP_Text hint;
    [Tooltip("Screen offset from the place to the card's corner (canvas units).")]
    public Vector2 offset = new(40f, 30f);

    Canvas canvas;
    float shown, target;

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (group != null) group.alpha = 0f;
        if (hint != null) hint.gameObject.SetActive(false);
    }

    public void ShowHint(bool show, string text = null)
    {
        if (hint == null) return;
        if (text != null) hint.text = text;
        hint.gameObject.SetActive(show);
    }

    public void Show(string name, string kindLine, string body, string statusLine, Vector3 worldPoint, Camera cam)
    {
        if (title != null) title.text = name;
        if (kind != null) kind.text = kindLine;
        if (description != null) { description.text = body; description.gameObject.SetActive(!string.IsNullOrEmpty(body)); }
        if (status != null) { status.text = statusLine; status.gameObject.SetActive(!string.IsNullOrEmpty(statusLine)); }
        target = 1f;
        Place(worldPoint, cam);
    }

    public void Hide() => target = 0f;

    void Place(Vector3 worldPoint, Camera cam)
    {
        if (card == null || cam == null || canvas == null) return;
        var screen = cam.WorldToScreenPoint(worldPoint);
        var parent = (RectTransform)card.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local);
        // To the right of the place, or to the left when that would leave the screen.
        var size = card.rect.size;
        var half = parent.rect.size * .5f;
        float x = local.x + offset.x, y = local.y + offset.y;
        if (x + size.x > half.x - 20f) x = local.x - offset.x - size.x;
        y = Mathf.Clamp(y, -half.y + 20f, half.y - size.y - 20f);
        card.anchoredPosition = new Vector2(x, y);
    }

    void Update()
    {
        shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 8f);
        if (group != null) group.alpha = shown;
    }
}
