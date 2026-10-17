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
    [Tooltip("Optional: one marker per milestone of the skill's track on the character sheet (ProgressionManager > Meta progression): its unlock icon, or \"?\" in the mark, in colour when unlocked for this run, grey when not.")]
    public Image[] milestoneIcons = new Image[0];
    public TMP_Text[] milestoneMarks = new TMP_Text[0];
    [HideInInspector] public AdventureCharacterUI owner;
    static readonly Color LockedTint = new(.42f, .42f, .42f, .55f);

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
    // The row's own tooltip again, after the pointer leaves one of its milestone markers for the row.
    public void ReshowTooltip() { if (owner == null && progress != null) ShowTooltip(progress, skill); }

    // Tooltip colours: labels dim, numbers green, a locked milestone red.
    public const string Dim = ItemData.DimColor, Good = ItemData.GoodColor, Bad = ItemData.BadColor;
    static readonly System.Text.RegularExpressions.Regex Number = new(@"([+\-]?)\u0001([^\u0002]*)\u0002(%?)");

    // A bonus line with its number in colour: "{0}% block stamina" with 12.5 -> "<green>-12.5%</green> block stamina".
    public static string Coloured(string format, string value, string colour = Good) =>
        Number.Replace(string.Format(format, "\u0001" + value + "\u0002"), $"<color={colour}>$1$2$3</color>");

    // What the skill does for each point, what it gives at this rank and how close the next point is (how it is trained
    // is left for the player to find out; the milestones have their own tooltips on their markers).
    public static void ShowTooltip(AdventurerProgress progress, AdventureSkill skill)
    {
        if (progress == null || !TooltipUI.HasInstance) return;
        var def = progress.Definition(skill);
        if (def == null) return;
        int rank = progress.Rank(skill);
        var text = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(def.bonusText) && def.bonusAt100 > 0)
        {
            float now = progress.Bonus(skill) * 100;
            text.Append($"<color={Dim}>Now</color><pos=36%>").Append(now < .05f ? $"<color={Dim}>none</color>" : Coloured(def.bonusText, now.ToString("0.#"))).AppendLine();
            if (!string.IsNullOrEmpty(def.extraText) && def.extraAt100 > 0)
                text.Append("<pos=36%>").Append(rank <= 0 ? $"<color={Dim}>none</color>" : Coloured(def.extraText, def.ExtraShown(rank))).AppendLine();
            text.Append($"<color={Dim}>Per rank</color><pos=36%>").Append(Coloured(def.bonusText, (def.bonusAt100).ToString("0.##")));
            if (!string.IsNullOrEmpty(def.extraText) && def.extraAt100 > 0)
                text.AppendLine().Append("<pos=36%>").Append(Coloured(def.extraText, (def.extraFlat ? def.extraAt100 * .01f : def.extraAt100).ToString("0.##")));
        }
        else text.Append(def.description);
        TooltipUI.Instance.ShowText(def.displayName, $"{AdventureSkills.Tier(rank).ToUpperInvariant()}  {rank} / 100", text.ToString());
    }

    // A milestone marker's tooltip: what it unlocks, at which rank, and whether this run has it.
    public void ShowMilestone(int index)
    {
        if (progress == null || !TooltipUI.HasInstance) return;
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null ? meta.Track(skill) : null;
        if (track == null || track.milestones == null || index < 0 || index >= track.milestones.Count || track.milestones[index] == null) return;
        var m = track.milestones[index];
        var def = progress.Definition(skill);
        string skillName = def != null ? def.displayName : skill.ToString();
        int rank = progress.Rank(skill);
        bool known = !string.IsNullOrEmpty(m.unlock) && m.unlock != "?";
        var text = new System.Text.StringBuilder();
        if (known && !string.IsNullOrEmpty(m.description)) text.Append(m.description).AppendLine().AppendLine();
        if (meta.Trophy(track, m) == ProgressionManager.TrophyState.Mounted) text.Append($"<color={Good}>Learned</color> <color={Dim}>(trophy wall)</color>");
        else if (meta.Unlocked(track, m, rank)) text.Append($"<color={Good}>Learned</color>");
        else if (rank >= m.value) text.Append($"<color={Bad}>Locked</color>");
        else text.Append($"<color={Bad}>Locked</color> <color={Dim}>(rank {rank} / {m.value})</color>");
        TooltipUI.Instance.ShowText(known ? m.unlock : "?", $"{skillName.ToUpperInvariant()}  {m.value}", text.ToString());
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
        RefreshMilestones(value);
    }

    void RefreshMilestones(int rank)
    {
        if (milestoneIcons == null || milestoneIcons.Length == 0) return;
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null ? meta.Track(skill) : null;
        int count = track != null && track.milestones != null ? track.milestones.Count : 0;
        for (int i = 0; i < milestoneIcons.Length; i++)
        {
            var m = i < count ? track.milestones[i] : null;
            var mark = milestoneMarks != null && i < milestoneMarks.Length ? milestoneMarks[i] : null;
            if (milestoneIcons[i] != null) milestoneIcons[i].transform.parent.gameObject.SetActive(m != null);
            if (m == null) continue;
            bool learned = meta.Unlocked(track, m, rank);
            var image = milestoneIcons[i];
            if (image != null)
            {
                image.enabled = m.icon != null;
                if (m.icon != null) image.sprite = m.icon;
                image.color = learned ? Color.white : LockedTint;
            }
            if (mark != null) { mark.text = m.icon != null ? "" : "?"; mark.color = learned ? Color.white : LockedTint; }
        }
    }

    // Progression of colours: grey for untrained through gold for Legendary.
    public static Color TierColour(int rank) =>
        rank >= 100 ? new Color(1f, .82f, .35f) : rank >= 80 ? new Color(.86f, .6f, 1f) : rank >= 60 ? new Color(.45f, .7f, 1f)
        : rank >= 40 ? new Color(.5f, .9f, .55f) : rank >= 20 ? new Color(.93f, .9f, .82f) : rank >= 1 ? new Color(.7f, .66f, .6f) : new Color(.42f, .39f, .35f);
}

