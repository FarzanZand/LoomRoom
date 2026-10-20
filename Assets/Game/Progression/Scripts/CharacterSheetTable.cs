using UnityEngine;

// The hobby table in front of the trophy wall: your character sheet lies on it among paints and miniatures, and using
// it opens the sheet (CharacterSheetUI). Until the sheet has been opened once after the DM's first mention, and again
// for as long as the spend-spoils quest is open (train Blocking: the run waits for it), a light on it calls the player over.
public class CharacterSheetTable : MonoBehaviour, IInteractable
{
    [Tooltip("A small light over the sheet, on while the DM is waiting for the player to look at it.")] public Light hint;
    [Min(0)] public float hintIntensity = 140f;
    [Tooltip("A light blue border round the sheet (a slightly larger quad under it), shown while a quest sends the player to the sheet.")]
    public Renderer questOutline;
    public Color outlineColour = new(.55f, .85f, 1f, 1f);
    Material outlineMaterial;
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
        Outline(on);
        if (glow != null) glow.Show(on);
        if (hint == null) return;
        hint.enabled = on;
        // Brighter and quicker while the quest holds the next run back.
        if (on) hint.intensity = hintIntensity * (quest ? 1.4f : 1f) * (.65f + .35f * Mathf.Sin(Time.time * (quest ? 3f : 2.2f)));
    }

    // The border breathes between the colour and a brighter tint, in step with the light.
    void Outline(bool on)
    {
        if (questOutline == null) return;
        questOutline.enabled = on;
        if (!on) return;
        if (outlineMaterial == null) outlineMaterial = questOutline.material;   // its own copy, so the asset stays as made
        float k = .5f + .5f * Mathf.Sin(Time.time * 3f);
        outlineMaterial.color = Color.Lerp(outlineColour, Color.white, .35f * k);
    }

    void OnDestroy() { if (outlineMaterial != null) Destroy(outlineMaterial); }
}
