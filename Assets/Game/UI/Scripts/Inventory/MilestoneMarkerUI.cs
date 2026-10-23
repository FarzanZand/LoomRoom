using UnityEngine;
using UnityEngine.EventSystems;

// One milestone marker on a skill row of the character panel (the unlock's icon or "?"): hovering it shows what the
// milestone unlocks, at which rank, and whether this run has it. Leaving it for the row brings back the skill's tooltip.
public class MilestoneMarkerUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public AdventureSkillRowUI row;
    [Min(0), Tooltip("Which milestone of the skill's track (0 = the first).")] public int index;
    bool hovering;

    public void OnPointerEnter(PointerEventData data) { hovering = true; if (row != null) row.ShowMilestone(index); }

    public void OnPointerExit(PointerEventData data)
    {
        hovering = false;
        // Still over the row: its own tooltip again; anywhere else: nothing.
        if (row != null && data.pointerCurrentRaycast.gameObject != null && data.pointerCurrentRaycast.gameObject.transform.IsChildOf(row.transform)) row.ReshowTooltip();
        else if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
    }

    void OnDisable() { if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide(); hovering = false; }
}
