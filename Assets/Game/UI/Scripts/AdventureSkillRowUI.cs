using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One row of the skill sheet: icon, rank and name, coloured by tier. Hover or focus shows the
// skill's details in the sheet; click pins it. Without an owner sheet (the character panel next to the
// inventory) hovering shows the skill's tooltip itself.
public class AdventureSkillRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public AdventureSkill skill;
    public Image icon;
    public TMP_Text rank, label;
    public Button button;
    [Tooltip("Shown while this row is the one described in the detail panel.")]
    public GameObject selected;
    [Tooltip("Optional: a thin bar under the row, filled with the progress to the next rank (UIBar: Filled, or stretched in its frame).")]
    public Image progressFill;
    [HideInInspector] public AdventureCharacterUI owner;

    public void OnPointerExit(PointerEventData data) => Hide();
    public void OnDeselect(BaseEventData data) => Hide();
    void OnDisable() => Hide();
    void Hide() { if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    public void OnPointerEnter(PointerEventData data) => Inspect();
    public void OnSelect(BaseEventData data) => Inspect();
    AdventurerProgress progress;
    void Inspect()
    {
        if (owner != null) { owner.Inspect(skill, false); return; }
        if (progress != null) ShowTooltip(progress, skill);
    }

    // What the skill does for each point and what it gives now (how it is trained is left for the player to find out).
    public static void ShowTooltip(AdventurerProgress progress, AdventureSkill skill)
    {
        if (progress == null || !TooltipUI.HasInstance) return;
        var def = progress.Definition(skill);
        if (def == null) return;
        int rank = progress.Rank(skill);
        var text = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(def.bonusText) && def.bonusAt100 > 0)
        {
            text.Append(string.Format(def.bonusText, (def.bonusAt100).ToString("0.##"))).Append(" per point").AppendLine();
            float now = progress.Bonus(skill) * 100;
            text.Append("Now: ").Append(now < .05f ? "none" : string.Format(def.bonusText, now.ToString("0.#")));
        }
        else text.Append(def.description);
        TooltipUI.Instance.ShowText(def.displayName, $"{AdventureSkills.Tier(rank).ToUpperInvariant()}  {rank} / 100", text.ToString());
    }

    public void Refresh(AdventurerProgress character, AdventureSkill shown)
    {
        progress = character;
        var def = character.Definition(skill);
        int value = character.Rank(skill);
        if (icon != null) { icon.sprite = def != null ? def.icon : null; icon.enabled = icon.sprite != null; }
        rank.text = value.ToString();
        rank.color = TierColour(value);
        label.text = def != null ? def.displayName : skill.ToString();
        if (progressFill != null) { UIBar.Set(progressFill, character.SkillProgress(skill)); progressFill.color = TierColour(Mathf.Max(20, value)); }
        if (selected != null) selected.SetActive(false);
    }

    // Barony-like progression of colours: grey for untrained through gold for Legendary.
    public static Color TierColour(int rank) =>
        rank >= 100 ? new Color(1f, .82f, .35f) : rank >= 80 ? new Color(.86f, .6f, 1f) : rank >= 60 ? new Color(.45f, .7f, 1f)
        : rank >= 40 ? new Color(.5f, .9f, .55f) : rank >= 20 ? new Color(.93f, .9f, .82f) : rank >= 1 ? new Color(.7f, .66f, .6f) : new Color(.42f, .39f, .35f);
}

