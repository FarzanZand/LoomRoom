using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One row of the skill sheet: icon, rank and name, coloured by tier. Hover or focus shows the
// skill's details in the sheet; click pins it.
public class AdventureSkillRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public AdventureSkill skill;
    public Image icon;
    public TMP_Text rank, label;
    public Button button;
    [Tooltip("Shown while this row is the one described in the detail panel.")]
    public GameObject selected;
    [HideInInspector] public AdventureCharacterUI owner;

    public void OnPointerExit(PointerEventData data) => Hide();
    public void OnDeselect(BaseEventData data) => Hide();
    void OnDisable() => Hide();
    void Hide() { if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    public void OnPointerEnter(PointerEventData data) => owner?.Inspect(skill, false);
    public void OnSelect(BaseEventData data) => owner?.Inspect(skill, false);

    public void Refresh(AdventurerProgress character, AdventureSkill shown)
    {
        var def = character.Definition(skill);
        int value = character.Rank(skill);
        if (icon != null) { icon.sprite = def != null ? def.icon : null; icon.enabled = icon.sprite != null; }
        rank.text = value.ToString();
        rank.color = TierColour(value);
        label.text = def != null ? def.displayName : skill.ToString();
        if (selected != null) selected.SetActive(false);
    }

    // Barony-like progression of colours: grey for untrained through gold for Legendary.
    public static Color TierColour(int rank) =>
        rank >= 100 ? new Color(1f, .82f, .35f) : rank >= 80 ? new Color(.86f, .6f, 1f) : rank >= 60 ? new Color(.45f, .7f, 1f)
        : rank >= 40 ? new Color(.5f, .9f, .55f) : rank >= 20 ? new Color(.93f, .9f, .82f) : rank >= 1 ? new Color(.7f, .66f, .6f) : new Color(.42f, .39f, .35f);
}

