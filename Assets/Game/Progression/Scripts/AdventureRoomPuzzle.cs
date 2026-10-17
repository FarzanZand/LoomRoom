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
    [Header("Missing tile")]
    [Tooltip("The symbol whose tile is missing until the player brings it back from the dungeon (ProgressionManager > Missing tile). -1: none missing.")]
    public int missingSymbol = 1;
    [Tooltip("That symbol's button (hidden until the tile is placed).")]
    public GameObject missingButton;
    public string missingMessage = "One tile is missing. It must be somewhere down there.";
    public string placedMessage = "You put the tile in its place.";

    int step;

    public bool Revealed => ProgressionManager.HasInstance && (ProgressionManager.Instance.GetFlag(progressFlag) >= revealFloor || ProgressionManager.Instance.TileState > 0);
    // The missing tile is in (or nothing is missing).
    bool Complete => missingSymbol < 0 || !ProgressionManager.HasInstance || ProgressionManager.Instance.missingTile == null || ProgressionManager.Instance.TileState == 2;
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
        if (key == progressFlag || key == unlockFlag || key == ProgressionManager.TileFlag) Refresh();
    }

    void Refresh()
    {
        if (puzzleContents != null) puzzleContents.SetActive(Revealed);
        if (missingButton != null) missingButton.SetActive(Complete);
        if (inscription != null) inscription.text = Solved ? unlockedClassName.ToUpperInvariant() : Revealed ? $"{step} / {solution.Length}" : "";
    }

    public bool CanInteract(Character who) => Revealed && who is Player player && player.kind == PlayerKind.Room;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        // Carrying the tile: it goes in.
        if (!Solved && ProgressionManager.Instance.TileState == 1)
        {
            ProgressionManager.Instance.PlaceTile();
            NotificationUI.Show(placedMessage);
            MessageLog.Post(placedMessage, MessageKind.Good);
            return;
        }
        string text = Solved ? $"{unlockedClassName} is unlocked." : Complete ? clue : clue + " " + missingMessage;
        NotificationUI.Show(text);
        MessageLog.Post(text, MessageKind.Lore);
    }

    public void Press(int symbol)
    {
        if (!Revealed || Solved || solution.Length == 0) return;
        if (!Complete) { NotificationUI.Show(missingMessage); return; }
        if (solution[step] != symbol) { step = 0; Refresh(); NotificationUI.Show("Wrong order. Start again."); return; }
        step++;
        Refresh();
        if (step < solution.Length) { NotificationUI.Show($"{symbols[symbol]}  {step} / {solution.Length}"); return; }
        ProgressionManager.Instance.SetFlag(unlockFlag);
        NotificationUI.Show($"{unlockedClassName} unlocked");
        MessageLog.Post($"{unlockedClassName} unlocked. You can pick it at the table.", MessageKind.Good);
    }
}
