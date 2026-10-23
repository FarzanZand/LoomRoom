using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Skills page of the character menu: the level plate with XP, every skill with its rank
// (hover for a tooltip), and Attack and Armor. Refreshes when the adventurer changes.
public class AdventureCharacterUI : MonoBehaviour
{
    [Header("Level plate")]
    public TMP_Text title;
    public TMP_Text experience;
    public Image experienceFill;
    [Header("Skills")]
    public AdventureSkillRowUI[] rows;
    [Header("Character")]
    public TMP_Text combat;

    AdventurerProgress progress;



    void Awake() { foreach (var row in rows) if (row != null) row.owner = this; }

    void OnEnable()
    {
        progress = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table)?.GetComponent<AdventurerProgress>() : null;
        if (progress == null) return;
        progress.Changed += Refresh;

        Refresh();
    }

    void OnDisable() { if (progress != null) progress.Changed -= Refresh; }

    public void Inspect(AdventureSkill skill, bool pin) => AdventureSkillRowUI.ShowTooltip(progress, skill);
    public void Refresh()
    {
        if (progress == null || progress.selectedClass == null || progress.rules == null) return;
        var stats = progress.GetComponent<CharacterStats>();

        title.text = $"Level {progress.Level}  {progress.selectedClass.displayName}";
        experience.text = $"{progress.Experience:0} / {progress.NextLevelXp:0} XP";
        if (experienceFill != null) experienceFill.fillAmount = progress.NextLevelXp > 0 ? progress.Experience / progress.NextLevelXp : 0;

        foreach (var row in rows) if (row != null) row.Refresh(progress, default);



        if (combat != null)
            combat.text = $"ATK  <color=#E8DCC4>{stats.GetFinal(StatType.AttackDamage):0}</color>\n\nAC   <color=#E8DCC4>{stats.GetFinal(StatType.Armor):0}</color>";

    }
}

