using UnityEngine;

// The hobby table in front of the trophy wall: your character sheet lies on it among paints and miniatures, and using
// it opens the sheet (CharacterSheetUI). Until the sheet has been opened once after the DM's first mention, and again
// for as long as the spend-spoils quest is open (train Blocking: the run waits for it), a light on it calls the player
// over: the quest's light (QuestGlow), a cone of warm light falling on the table. Its own small pulsing lamp and the
// light blue border round the sheet are switched off since 2026-11-16 (the user: the highlight "is real ugly and sticks
// out too much in the room ... Especially ugly when the table or desk is highlighted"): one light, not three.
public class CharacterSheetTable : MonoBehaviour, IInteractable
{
    [Tooltip("The table's old pulsing lamp: switched off at the start, the quest's light took its place.")] public Light hint;
    [Tooltip("The old light blue border round the sheet: switched off at the start, the quest's light took its place.")]
    public Renderer questOutline;
    public const string OpenedFlag = "story.sheetOpened";

    public string Prompt => "Character sheet";
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && CharacterSheetUI.HasInstance;
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(OpenedFlag);
        CharacterSheetUI.Instance.Open();
    }

    QuestGlow glow;

    void Start()
    {
        glow = gameObject.AddComponent<QuestGlow>();
        if (hint != null) hint.enabled = false;
        if (questOutline != null) questOutline.enabled = false;
    }

    void Update()
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        bool firstLook = meta != null && meta.HasFlag(ProgressionManager.LedgerSaidFlag) && !meta.HasFlag(OpenedFlag);
        // Either quest that sends the player here: look at the sheet, or spend the spoils (train Blocking).
        bool quest = meta != null && (meta.QuestState(ProgressionManager.SheetQuest) == 1 || meta.QuestState(ProgressionManager.SpendQuest) == 1);
        bool on = firstLook || quest;
        if (glow != null) glow.Show(on);
    }
}
