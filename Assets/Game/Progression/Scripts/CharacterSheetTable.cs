using UnityEngine;

// The hobby table in front of the trophy wall: your character sheet lies on it among paints and miniatures, and using
// it opens the sheet (CharacterSheetUI). Until the sheet has been opened once after the DM's first mention, a soft
// light on it calls the player over.
public class CharacterSheetTable : MonoBehaviour, IInteractable
{
    [Tooltip("A small light over the sheet, on while the DM is waiting for the player to look at it.")] public Light hint;
    [Min(0)] public float hintIntensity = 140f;
    public const string OpenedFlag = "story.sheetOpened";

    public string Prompt => "Character sheet";
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && CharacterSheetUI.HasInstance;
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(OpenedFlag);
        CharacterSheetUI.Instance.Open();
    }

    void Update()
    {
        if (hint == null) return;
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        bool on = meta != null && meta.HasFlag(ProgressionManager.LedgerSaidFlag) && !meta.HasFlag(OpenedFlag);
        hint.enabled = on;
        if (on) hint.intensity = hintIntensity * (.65f + .35f * Mathf.Sin(Time.time * 2.2f));
    }
}
