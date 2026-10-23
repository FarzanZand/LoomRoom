using UnityEngine;
using UnityEngine.EventSystems;

// One stat line of the character panel (health, stamina, mana, damage, armour, an attribute): hovering it explains what the number
// does, with the player's own numbers (EquipmentPanelUI.Explain).
public class StatTooltipUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum Kind { Health = 0, Stamina = 1, Mana = 2, Damage = 3, Armor = 4, Strength = 5, Dexterity = 6, Constitution = 7, Intelligence = 8 }
    public EquipmentPanelUI panel;
    public Kind kind;
    bool hovering;

    public void OnPointerEnter(PointerEventData data)
    {
        hovering = true;
        if (panel == null || !TooltipUI.HasInstance || !panel.Explain(kind, out var title, out var subtitle, out var body)) return;
        TooltipUI.Instance.ShowText(title, subtitle, body);
    }

    public void OnPointerExit(PointerEventData data) { hovering = false; if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    void OnDisable() { if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide(); hovering = false; }
}
