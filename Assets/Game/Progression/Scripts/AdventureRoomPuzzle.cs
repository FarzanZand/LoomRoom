using TMPro;
using UnityEngine;

// A symbol puzzle in the room that unlocks a class. The clue is found in the dungeon (reaching
// revealFloor for the first time); the puzzle appears in the room from then on. The room keeps
// knowledge and class access, never dungeon money or equipment.
public class AdventureRoomPuzzle : MonoBehaviour, IInteractable
{
    [Header("Progress gate")]
    public int revealFloor = 3;
    public string progressFlag = "adventure.deepestFloor", unlockFlag = "class.spellblade";
    public string unlockedClassName = "Arcanist";
    [Header("Authored presentation")]
    public GameObject puzzleContents;
    public TMP_Text inscription;
    [TextArea] public string clue = "Shield, flame, hand.";
    [TextArea] public string clueFoundMessage = "You found a note: shield, flame, hand. It matches the tiles on the table in the room.";
    public string[] symbols = { "Shield", "Flame", "Hand" };
    public int[] solution = { 0, 1, 2 };

    int step;

    public bool Revealed => ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(progressFlag) >= revealFloor;
    public bool Solved => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(unlockFlag);
    public string Prompt => Solved ? $"{unlockedClassName} unlocked" : "Puzzle";

    void Start()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlag;
        Refresh();
    }

    void OnDestroy() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag; }

    void OnFlag(string key, int value)
    {
        if (key == progressFlag && value == revealFloor && !Solved) MessageLog.Post(clueFoundMessage, MessageKind.Lore);
        if (key == progressFlag || key == unlockFlag) Refresh();
    }

    void Refresh()
    {
        if (puzzleContents != null) puzzleContents.SetActive(Revealed);
        if (inscription != null) inscription.text = Solved ? unlockedClassName.ToUpperInvariant() : Revealed ? $"{step} / {solution.Length}" : "";
    }

    public bool CanInteract(Character who) => Revealed && who is Player player && player.kind == PlayerKind.Room;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        string text = Solved ? $"{unlockedClassName} is unlocked." : clue;
        NotificationUI.Show(text);
        MessageLog.Post(text, MessageKind.Lore);
    }

    public void Press(int symbol)
    {
        if (!Revealed || Solved || solution.Length == 0) return;
        if (solution[step] != symbol) { step = 0; Refresh(); NotificationUI.Show("Wrong order. Start again."); return; }
        step++;
        Refresh();
        if (step < solution.Length) { NotificationUI.Show($"{symbols[symbol]}  {step} / {solution.Length}"); return; }
        ProgressionManager.Instance.SetFlag(unlockFlag);
        NotificationUI.Show($"{unlockedClassName} unlocked");
        MessageLog.Post($"{unlockedClassName} unlocked. You can pick it at the table.", MessageKind.Good);
    }
}
