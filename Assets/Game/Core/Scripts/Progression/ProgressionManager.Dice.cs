using Sirenix.OdinInspector;
using UnityEngine;

// The symbol puzzle's prize (AdventureRoomPuzzle): a set of dice carried home as a key item (the top row of icons).
// The quest log asks for them to go on the big table's border, and using the border puts them there for good
// (TableDice); while they lie there Trading is DiceTradingBonus ranks higher (AdventurerProgress.Rank). Flag room.dice:
// 1 carried, 2 on the table.
public partial class ProgressionManager
{
    public const string DiceFlag = "room.dice", DiceQuest = "placeDice";

    [FoldoutGroup("Table dice"), Tooltip("The key item the symbol puzzle gives (its name and icon in the key items row).")]
    public ItemData diceSet;
    [FoldoutGroup("Table dice"), Min(0), Tooltip("Trading ranks added while the dice lie on the table's border.")]
    public int diceTradingBonus = 5;

    public int DiceState => GetFlag(DiceFlag);
    public bool DiceCarried => DiceState == 1;
    public bool DicePlaced => DiceState == 2;

    // The puzzle solved: the dice come home (once).
    public void GiveDice()
    {
        if (DiceState != 0) return;
        SetFlag(DiceFlag, 1);
        MetaChanged?.Invoke();
    }

    public void PlaceDice()
    {
        if (!DiceCarried) return;
        SetFlag(DiceFlag, 2);
        MetaChanged?.Invoke();
    }

    // The dev console's addroomitem: carried home now (taken off the table if they were on it).
    public bool CarryDice()
    {
        SetFlag(DiceFlag, 1);
        MetaChanged?.Invoke();
        return true;
    }

    // Skill ranks the room adds while its things are in place (the dice: Trading).
    public int RoomSkillBonus(AdventureSkill skill) => skill == AdventureSkill.Trading && DicePlaced ? diceTradingBonus : 0;

    // The quest is open while the dice are carried and done once they are on the table.
    void UpdateDiceQuest()
    {
        if (Time.frameCount % 15 != 11) return;
        int state = QuestState(DiceQuest);
        if (DiceCarried && state != 1) SetFlag(QuestFlag(DiceQuest), 1);
        else if (!DiceCarried && state == 1) CompleteQuest(DiceQuest);
    }
}
