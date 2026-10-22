using UnityEngine;

// One of the Dungeon Master's house rules: what it says, how he gives it and how he quotes it when it applies. Listed
// in order (its number) on the RuleManager; a rule once given is pinned on the sheet taped to the bedroom's east door.
[CreateAssetMenu(menuName = "Table/House Rule", fileName = "Rule")]
public class HouseRule : ScriptableObject
{
    [Tooltip("Never change after release: the save keeps which rules were given by it (flag rule.<id>).")]
    public string id = "rule1";
    [TextArea(1, 3), Tooltip("The rule as it is written on the sheet.")]
    public string text = "Nobody leaves the table until I say so.";
    [Tooltip("Said when he gives the rule. Empty: \"Rule one. \" and the text.")]
    public DungeonMaster.Line given = new();
    [Tooltip("Said when the rule applies (he quotes it at you). Empty: \"Rule one.\"")]
    public DungeonMaster.Line quote = new();
    [Tooltip("On the sheet from the start, before he has said it (rule 1: the sheet is on the door when the game begins).")]
    public bool pinnedFromStart;
}
