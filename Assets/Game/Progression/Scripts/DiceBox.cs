using DG.Tweening;
using UnityEngine;

// The locked box on the coffee table by the painting (it took the symbol puzzle's place, 2026-11-11). It stands there
// shut from the start; its key comes out of a dungeon chest and is carried home (ProgressionManager.BoxKey). Used with
// the key at home the lid swings open, for good, and the set of dice is in it (ProgressionManager.Dice: a key item that
// then goes on the big table's border). Without the key it only rattles and says it is locked. While the key is carried
// a light pulses over the box and the table under it gets the quest outline, as the character sheet's table does.
// The box is a prop with a Lid child hinged at the back: this turns the lid, nothing else of it moves.
public class DiceBox : MonoBehaviour, IInteractable
{
    [Header("Lid")]
    [Tooltip("The lid, its pivot on the hinge. Shut is how it is saved in the scene.")] public Transform lid;
    [Tooltip("How far the lid turns to stand open, in degrees about its own axes, from shut.")] public Vector3 openTurn = new(-105f, 0f, 0f);
    [Min(.05f)] public float openSeconds = .7f;
    [Tooltip("How far the lid lifts and drops back when the box is tried without the key (degrees, the same way as it opens).")]
    public float rattleTurn = 3f;

    [Header("Words")]
    public string lockedPrompt = "Locked box", openPrompt = "Open the box";
    public string lockedMessage = "Locked. The key must be somewhere down there.";
    public string foundMessage = "You found a set of dice in the box.";
    public DungeonMaster.Line diceLine = new() { text = "A set of dice. Put them on the table. They bring luck at the market." };

    [Header("Sound and effect")]
    [Tooltip("The key turning in the lock, then the lid swinging up (played at the box, the second a moment after the first).")]
    public AudioClip keyClip;
    public AudioClip openClip;
    [Tooltip("Tried without the key: one of these, played at the box.")] public AudioClip[] lockedClips = { };
    [Range(0, 1)] public float volume = .9f;
    [Tooltip("A burst over the box as it opens (Items/Effects/Chest burst), scaled up to the room.")] public GameObject openEffect;
    [Min(.1f), Tooltip("The burst is made for the dungeon (1 unit to the metre); the room is ~22 units to the metre.")] public float effectScale = 12f;
    [Min(0), Tooltip("How long the key takes to turn before the lid moves.")] public float keyTurnSeconds = .35f;
    [Tooltip("How long after the box is used the dice are in hand (the lid is on its way up).")] public float diceDelay = .85f;

    [Header("Quest light")]
    [Tooltip("Pulses over the box while its key is carried. Empty: a copy of the character sheet's hint light is made at start.")]
    public Light hint;
    [Min(0)] public float hintIntensity = 140f;

    Quaternion shut;
    QuestGlow glow;
    bool opening;

    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    bool Opened => Meta != null && Meta.BoxOpened;
    bool KeyReady => Meta != null && Meta.BoxKeyReady;

    public string Prompt => KeyReady ? openPrompt : lockedPrompt;
    public bool CanInteract(Character who) => !Opened && !opening && Meta != null && who is Player player && player.kind == PlayerKind.Room;

    void Awake() { if (lid != null) shut = lid.localRotation; }

    void Start()
    {
        if (hint == null) hint = CopySheetHint();
        if (hint != null) hint.enabled = false;
        hintIntensity = CharacterSheetTable.ReachDown(hint, transform, hintIntensity);
        // The box stands on the coffee table: the outline goes round the table, which reads from across the room.
        glow = (transform.parent != null ? transform.parent.gameObject : gameObject).AddComponent<QuestGlow>();
        if (Meta != null) Meta.FlagChanged += OnFlag;
        Refresh();
    }
    void OnDestroy() { if (Meta != null) Meta.FlagChanged -= OnFlag; }
    void OnFlag(string key, int value) { if (key == ProgressionManager.BoxOpenedFlag && !opening) Refresh(); }

    // The lid as the save has it: open for good once opened (also after the dev tools shut it again).
    void Refresh()
    {
        if (lid == null) return;
        lid.DOKill();
        lid.localRotation = Opened ? shut * Quaternion.Euler(openTurn) : shut;
    }

    // The sheet's hint light, set the same height above the box as it is above the sheet (a prop's light is its child).
    Light CopySheetHint()
    {
        var sheet = FindAnyObjectByType<CharacterSheetTable>();
        if (sheet == null || sheet.hint == null) return null;
        var light = Instantiate(sheet.hint, transform);
        light.name = "Quest light";
        light.transform.position = transform.position + Vector3.up * Mathf.Max(0f, sheet.hint.transform.position.y - sheet.transform.position.y);
        hintIntensity = sheet.hintIntensity;
        return light;
    }

    // Brighter and quicker than a plain hint, like the sheet's while its quest holds the run back.
    void Update()
    {
        bool on = KeyReady && Meta.boxKey != null && !opening;
        if (glow != null) glow.Show(on);
        if (hint == null) return;
        if (hint.enabled != on) hint.enabled = on;
        if (on) hint.intensity = hintIntensity * 1.4f * (.65f + .35f * Mathf.Sin(Time.time * 3f));
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (!KeyReady)
        {
            if (lid != null)
            {
                lid.DOKill();
                lid.localRotation = shut;
                var up = shut * Quaternion.Euler(openTurn.normalized * rattleTurn);
                DOTween.Sequence().SetLink(lid.gameObject)
                    .Append(lid.DOLocalRotateQuaternion(up, .06f)).Append(lid.DOLocalRotateQuaternion(shut, .08f))
                    .Append(lid.DOLocalRotateQuaternion(up, .05f)).Append(lid.DOLocalRotateQuaternion(shut, .09f));
            }
            if (lockedClips.Length > 0 && AudioManager.HasInstance && lockedClips[Random.Range(0, lockedClips.Length)] is AudioClip locked)
                AudioManager.Instance.PlaySFX(locked, transform.position, volume, .06f);
            NotificationUI.Show(lockedMessage);
            return;
        }
        if (!Meta.OpenBox()) return;
        opening = true;
        // The key turns, then the lid swings up.
        if (keyClip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(keyClip, transform.position, volume);
        if (lid != null)
        {
            lid.DOKill();
            lid.DOLocalRotateQuaternion(shut * Quaternion.Euler(openTurn), openSeconds).SetEase(Ease.OutBack, 1.2f).SetDelay(keyTurnSeconds).SetLink(lid.gameObject)
                .OnStart(() => { if (openClip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(openClip, transform.position, volume); });
        }
        // The dice are in hand once the lid is on its way up.
        DOVirtual.DelayedCall(diceDelay, () =>
        {
            opening = false;
            Burst();
            if (Meta == null) return;
            Meta.GiveDice();
            NotificationUI.Show(foundMessage);
            if (diceLine != null && !diceLine.IsEmpty) DungeonMaster.Say(diceLine);
        }).SetLink(gameObject);
    }

    // The burst sized for the room: every particle system scales with its transform.
    void Burst()
    {
        if (openEffect == null) return;
        var fx = Instantiate(openEffect, transform.position + Vector3.up * 4f, Quaternion.identity);
        fx.transform.localScale = openEffect.transform.localScale * effectScale;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        Destroy(fx, 4f);
    }
}
