using UnityEngine;

// The hobby table in front of the trophy wall: your character sheet lies on it among paints and miniatures, and using
// it opens the sheet (CharacterSheetUI). Until the sheet has been opened once after the DM's first mention, and again
// for as long as the spend-spoils quest is open (train Blocking: the run waits for it), a light on it calls the player
// over and the sheet itself wears the quest's soft glow (QuestGlow on the sheet, not round the whole table: the user,
// 2026-11-16). The hard light blue border that lay round the sheet is switched off: the glow took its place.
public class CharacterSheetTable : MonoBehaviour, IInteractable
{
    [Tooltip("A small light over the sheet, on while the DM is waiting for the player to look at it.")] public Light hint;
    [Min(0)] public float hintIntensity = 140f;
    [Tooltip("The old light blue border round the sheet (a slightly larger quad under it): switched off at the start, the quest's glow round the sheet took its place. The sheet is this quad's parent.")]
    public Renderer questOutline;
    [Min(0f), Tooltip("How strong the glow round the sheet is against the usual quest glow (1): the sheet is small, flat and lies in its lamp's light.")]
    public float sheetGlow = 2.6f;
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
        // The glow goes round the sheet lying on the table.
        var sheet = questOutline != null ? questOutline.transform.parent : transform.Find("Character sheet");
        if (questOutline != null) questOutline.enabled = false;
        glow = (sheet != null ? sheet.gameObject : gameObject).AddComponent<QuestGlow>();
        glow.strength = sheetGlow;
        hintIntensity = ReachDown(hint, transform, hintIntensity);
    }

    // A hint light hung over a prop lights it only if its range reaches well past it and it is bright enough at that
    // height (it hung 26 units up with a range of 26 and an intensity of 140, so almost nothing landed). Returns the
    // intensity to pulse around.
    public static float ReachDown(Light light, Transform prop, float intensity)
    {
        if (light == null) return intensity;
        float height = Mathf.Max(1f, light.transform.position.y - prop.position.y);
        light.range = Mathf.Max(light.range, height * 2.5f);
        return Mathf.Max(intensity, height * height * 1.6f);
    }

    void Update()
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        bool firstLook = meta != null && meta.HasFlag(ProgressionManager.LedgerSaidFlag) && !meta.HasFlag(OpenedFlag);
        // Either quest that sends the player here: look at the sheet, or spend the spoils (train Blocking).
        bool quest = meta != null && (meta.QuestState(ProgressionManager.SheetQuest) == 1 || meta.QuestState(ProgressionManager.SpendQuest) == 1);
        bool on = firstLook || quest;
        if (glow != null) glow.Show(on);
        if (hint == null) return;
        hint.enabled = on;
        // Brighter and quicker while the quest holds the next run back.
        if (on) hint.intensity = hintIntensity * (quest ? 1.4f : 1f) * (.65f + .35f * Mathf.Sin(Time.time * (quest ? 3f : 2.2f)));
    }
}
