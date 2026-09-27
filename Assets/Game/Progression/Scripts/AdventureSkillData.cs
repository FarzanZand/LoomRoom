using System;
using Sirenix.OdinInspector;
using UnityEngine;

// What a skill can be practised with. Serialized by integer; append only.
public enum SkillAction { Hit = 0, Kill = 1, Block = 2, Cast = 3, Heal = 4, Sprint = 5, Swim = 6, Buy = 7, Sell = 8 }

// One Barony-style skill: 0-100, raised one point at a time by chance whenever the player
// successfully does the thing it is about. Benefits grow with the rank and a Legendary bonus
// arrives at 100. The numbers here are read by AdventurerProgress; the text is for the UI.
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
        [HorizontalGroup, LabelText("1 in"), LabelWidth(28), Min(1)] public float oneIn = 10;
        [Tooltip("Shown in the skill sheet, e.g. \"Hitting an enemy with a sword\".")] public string text;
    }
    [Title("Practice")]
    [InfoBox("Each successful action rolls once. A success raises the skill by one point.")]
    public Practice[] practice = new Practice[0];
    [Tooltip("Chance multiplier at rank 100; ranks in between blend toward it, so high ranks take longer.")]
    [Range(.1f, 1)] public float chanceAtMaster = .5f;
    [Tooltip("Most points one enemy can teach (0 = no limit). Stops farming a single foe.")]
    [Min(0)] public int perEnemyLimit;

    [Title("Benefits")]
    [Tooltip("Continuous bonus at rank 100, scaled linearly by rank (0.5 = +50%).")]
    public float bonusAt100 = .5f;
    [Tooltip("How the continuous bonus reads in the sheet. {0} is the current percentage.")]
    public string bonusText = "+{0}% damage";
    public string legendaryText;

    public float OneIn(SkillAction action)
    {
        if (practice != null) foreach (var p in practice) if (p != null && p.action == action) return p.oneIn;
        return 0;
    }
}
