using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The class half of the table menu: portrait, description, strongest starting skills
// and starting kit of the class that will be played. Arrows (or left/right) cycle unlocked classes;
// locked ones show as dim pips with the hint for unlocking them.
public class AdventureClassSelectionUI : MonoBehaviour
{
    public Image portrait;
    public TMP_Text className, description, resources;
    [Tooltip("Icon + label pairs for the class's best starting skills, strongest first.")]
    public Image[] skillIcons;
    public TMP_Text[] skillLabels;
    public Image[] kitIcons;
    public Button previous, next;
    // One figure only (the intro's practice figure): no arrows, pips or hidden-class hint.
    public bool Single { get; set; }
    [Tooltip("One pip per class, filled for the shown one, dim for locked classes.")]
    public Image[] pips;
    public Color pipOn = new(.87f, .72f, .4f), pipOff = new(.35f, .3f, .25f), pipLocked = new(.18f, .16f, .14f);
    [Tooltip("Colours written into the text: the health, mana and stamina labels, a skill's rank and its tier.")]
    public Color healthColor = new(.85f, .34f, .23f), manaColor = new(.36f, .56f, .85f), staminaColor = new(.36f, .69f, .54f),
        rankColor = new(.88f, .76f, .48f), tierColor = new(.55f, .51f, .45f);

    AdventurerProgress progress;
    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    void Awake()
    {
        previous.onClick.AddListener(() => Cycle(-1));
        next.onClick.AddListener(() => Cycle(1));
    }

    void OnEnable()
    {
        progress = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table)?.GetComponent<AdventurerProgress>() : null;
        if (progress != null && progress.rules != null && (progress.selectedClass == null || !progress.selectedClass.Unlocked))
            progress.SelectClass(progress.rules.classes.FirstOrDefault(c => c != null && c.Unlocked));
        Refresh();
    }

    public void Cycle(int delta)
    {
        if (progress == null || progress.InRun) return;
        var classes = progress.rules.classes;
        int index = System.Array.IndexOf(classes, progress.selectedClass);
        for (int i = 0; i < classes.Length; i++)
        {
            index = (index + delta + classes.Length) % classes.Length;
            if (classes[index] != null && progress.SelectClass(classes[index])) break;
        }
        UIFeedbackSettings.Shared?.Hover();
        Refresh();
    }

    public void Refresh()
    {
        if (progress == null || progress.rules == null || progress.selectedClass == null) return;
        var c = progress.selectedClass;
        var classes = progress.rules.classes;

        portrait.sprite = c.portrait; portrait.enabled = c.portrait != null;
        className.text = c.displayName;
        description.text = c.description;
        resources.text = $"<color=#{Hex(healthColor)}>HP</color> {c.health:0}    <color=#{Hex(manaColor)}>MP</color> {c.mana:0}    <color=#{Hex(staminaColor)}>SP</color> {c.stamina:0}";



        var best = AdventureSkills.All.Where(s => c.StartingRank(s) > 0).OrderByDescending(c.StartingRank).ToArray();
        for (int i = 0; i < skillLabels.Length; i++)
        {
            bool shown = i < best.Length;
            skillLabels[i].gameObject.SetActive(shown);
            if (i < skillIcons.Length) skillIcons[i].gameObject.SetActive(shown);
            if (!shown) continue;
            var def = progress.Definition(best[i]);
            int rank = c.StartingRank(best[i]);
            skillLabels[i].text = $"{def?.displayName ?? best[i].ToString()}  <color=#{Hex(rankColor)}>{rank}</color>  <color=#{Hex(tierColor)}>{AdventureSkills.Tier(rank)}</color>";
            if (i < skillIcons.Length) skillIcons[i].sprite = def != null ? def.icon : null;
        }

        var kit = c.equipment.Concat(c.spells ?? new ItemData[0]).Where(x => x != null && x.icon != null).ToArray();
        for (int i = 0; i < kitIcons.Length; i++)
        {
            bool shown = i < kit.Length;
            kitIcons[i].transform.parent.gameObject.SetActive(shown);
            if (shown) kitIcons[i].sprite = kit[i].icon;
        }

        for (int i = 0; i < pips.Length; i++)
        {
            bool exists = i < classes.Length && classes[i] != null;
            pips[i].gameObject.SetActive(exists && !Single);
            if (!exists) continue;
            pips[i].color = classes[i] == c ? pipOn : classes[i].Unlocked ? pipOff : pipLocked;
        }
        previous.interactable = next.interactable = !progress.InRun;
        previous.gameObject.SetActive(!Single); next.gameObject.SetActive(!Single);
    }
}
