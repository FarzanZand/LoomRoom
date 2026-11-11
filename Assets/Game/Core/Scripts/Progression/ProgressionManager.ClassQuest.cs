using Sirenix.OdinInspector;
using UnityEngine;

// The Change class quest (the user, 2026-11-11): once the hero can be chosen in the room (the map has been seen, the
// first lesson is over, no run going), the quest log asks the player to change their class at the desk by the bed, the
// Dungeon Master points at it, and the desk glows (ClassPosterWall) until the player uses it to choose a hero. Given once.
public partial class ProgressionManager
{
    public const string ClassQuest = "changeClass";

    [FoldoutGroup("Quests"), PropertyOrder(50), LabelText("Says, giving the Change class quest"), Tooltip("Said as the Change class quest is given, back in the room.")]
    public DungeonMaster.Line classQuestLine = new() { text = "Bored of that hero? Pick another at the desk by the bed.", inPerson = true };

    void UpdateClassQuest()
    {
        if (Time.frameCount % 15 != 7 || QuestState(ClassQuest) != 0) return;
        if (!HasFlag(CampaignManager.SeenFlag) || LessonHoldsRuns || !RoomPlayerFree) return;
        if (!CampaignManager.HasInstance || !CampaignManager.Instance.CanChooseHeroInRoom) return;
        if (!GiveQuest(ClassQuest)) return;
        if (classQuestLine != null && !classQuestLine.IsEmpty) DungeonMaster.Say(classQuestLine);
    }

    // The hero select opened from the desk: the quest is done.
    public void ClassDeskUsed() => CompleteQuest(ClassQuest);
}
