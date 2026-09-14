using System.Text;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.InputSystem;

public class TooltipUI : Singleton<TooltipUI>
{
    [SerializeField] GameObject panel;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI typeText;
    [SerializeField] TextMeshProUGUI descriptionText;

    RectTransform rect;

    protected override void Awake()
    {
        base.Awake();
        if (panel != null)
        {
            rect = panel.GetComponent<RectTransform>();
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
        if (typeText != null)        typeText.text = item.itemType.ToString().ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = FormatBody(item);
        panel.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        FollowMouse();
    }

    static string FormatBody(ItemData item)
    {
        var body=new StringBuilder();
        if (!string.IsNullOrWhiteSpace(item.description))
            body.Append("<size=18><color=#B7BABF>").Append(item.description.Trim()).Append("</color></size>");
        if (item.canBeEquipped && item.statModifiers != null)
            foreach(var modifier in item.statModifiers)
            {
                if(modifier==null) continue;
                if(body.Length>0) body.Append("\n\n");
                string line=modifier.Describe();
                int split=line.IndexOf(' ');
                body.Append("<color=").Append(modifier.value<0 ? "#E78787>" : "#80CEA0>")
                    .Append(line.Substring(0,split)).Append("</color>").Append(line.Substring(split));
            }
        if(item.effects!=null)
            foreach(var effect in item.effects)
            {
                string line=effect?.Describe();
                if(string.IsNullOrWhiteSpace(line)) continue;
                if(body.Length>0) body.Append("\n\n");
                body.Append("<color=#A1C5DE>").Append(line).Append("</color>");
            }
        return body.ToString();
    }

    public void Hide()
    {
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
    }
}
