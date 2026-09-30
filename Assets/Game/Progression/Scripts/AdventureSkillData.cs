using System;
using Sirenix.OdinInspector;
using UnityEngine;

// What a skill can be practised with. Serialized by integer; append only.
public enum SkillAction { Hit = 0, Kill = 1, Block = 2, Cast = 3, Heal = 4, Sprint = 5, Swim = 6, Buy = 7, Sell = 8, Backstab = 9, Sneak = 10 }

// Skills earn XP from qualifying actions; rank benefits and Legendary bonuses stay unchanged.
[CreateAssetMenu(menuName = "LoomRoom/Adventure Skill")]
public class AdventureSkillData : ScriptableObject
{
    public AdventureSkill skill;
    public string displayName;
    [PreviewField(48)] public Sprite icon;
    [Tooltip("The attribute this skill is associated with, shown in the skill sheet.")]
    public StatType associatedStat = StatType.Strength;
    [TextArea(2, 4)] public string description;

    [Serializable]
    public class Practice
    {
        [HorizontalGroup, HideLabel] public SkillAction action;
        [HorizontalGroup, LabelText("XP"), Min(0)] public float experience = 10;
        [Tooltip("Shown in the skill sheet, e.g. \"Hitting an enemy with a sword\".")] public string text;
    }
    [Title("Practice")]
    [InfoBox("Each qualifying action awards XP. Reaching the threshold raises the skill one rank.")]
    public Practice[] practice = new Practice[0];
    [Min(1), Tooltip("XP required to advance from rank zero.")] public float rankExperience = 100;
    [Min(1), Tooltip("XP cost multiplier near rank 100.")] public float masterExperienceMultiplier = 2;
    [Tooltip("Most points one enemy can teach (0 = no limit). Stops farming a single foe.")]
    [Min(0)] public int perEnemyLimit;

    [Title("Benefits")]
    [Tooltip("Continuous bonus at rank 100, scaled linearly by rank (0.5 = +50%).")]
    public float bonusAt100 = .5f;
    [Tooltip("How the continuous bonus reads in the sheet. {0} is the current percentage.")]
    public string bonusText = "+{0}% damage";
    public string legendaryText;

    public float ExperienceRequired(int rank) => Mathf.Max(1, rankExperience * Mathf.Lerp(1, Mathf.Max(1, masterExperienceMultiplier), Mathf.Clamp01(rank / 100f)));

    public float ExperienceFor(SkillAction action)
    {
        if (practice != null) foreach (var p in practice) if (p != null && p.action == action) return p.experience;
        return 0;
    }
}
