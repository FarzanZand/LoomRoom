using System.Text;
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

    RectTransform rect;
    CanvasGroup visibility;
    Tween fade;

    protected override void Awake()
    {
        base.Awake();
        if (panel != null)
        {
            rect = panel.GetComponent<RectTransform>();
            visibility = panel.GetComponent<CanvasGroup>();
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
        if (typeText != null)        typeText.text = item.IsConsumable ? "CONSUMABLE" : item.canBeEquipped ? EquipmentSlotUI.Display(item.equipSlot) : item.itemType.ToString().ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = FormatBody(item);
        panel.SetActive(true);
        // Unity can reset overrideSorting while an authored nested canvas is inactive.
        var overlay=panel.GetComponent<Canvas>();if(overlay!=null)overlay.overrideSorting=true;
        panel.transform.SetAsLastSibling();
        fade?.Kill();
        if(visibility!=null){visibility.alpha=0;fade=visibility.DOFade(1,.12f).SetUpdate(true);}
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
        if(item.itemType == ItemType.Shield) body.Append("\n\nArmor applies only to frontal hits while blocking. Guarding and blocked hits consume stamina.");
        if(item.canBeEquipped && PlayerManager.HasInstance) {
            var equipped=PlayerManager.Instance.Active?.Equipment?.Get(item.equipSlot);
            float Bonus(ItemData data,StatType stat){float sum=0;if(data?.statModifiers!=null)foreach(var m in data.statModifiers)if(m!=null&&m.stat==stat&&m.type==ModifierType.Flat)sum+=m.value;return sum;}
            foreach(var stat in item.IsConsumable ? System.Array.Empty<StatType>() : new[]{StatType.AttackDamage,StatType.Armor}) {
                float delta=Bonus(item,stat)-Bonus(equipped,stat);
                if(Mathf.Abs(delta)>.001f)body.Append($"\n\n<color={(delta>0?"#80CEA0":"#E78787")}>{delta:+0.#;-0.#} {StatModifierEntry.Label(stat)}</color> vs equipped");
            }
            body.Append(item.IsConsumable?"\n\n<size=17>Equip, close inventory, then use from your hand.</size>":"\n\n<size=17>Equip from inventory or drag to its equipment slot.</size>");
        }
        return body.ToString();
    }

    public void Hide()
    {
        fade?.Kill();
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
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        float dx=corners[0].x<8?8-corners[0].x:corners[2].x>Screen.width-8?Screen.width-8-corners[2].x:0;
        float dy=corners[0].y<8?8-corners[0].y:corners[2].y>Screen.height-8?Screen.height-8-corners[2].y:0;
        rect.position+=new Vector3(dx,dy);
    }
}
