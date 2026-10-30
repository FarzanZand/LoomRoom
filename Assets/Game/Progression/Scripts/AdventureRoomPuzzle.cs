using TMPro;
using UnityEngine;

// A symbol puzzle in the room. The clue is found in the dungeon (reaching revealFloor for the first time); the puzzle
// appears in the room from then on. Solved, it gives a set of dice (givesDice, ProgressionManager.Dice: a key item that
// goes on the big table's border for +5 Trading), or with givesDice off unlocks a class as it used to (the Arcanist is
// now unlocked by the Arena, ArenaFight.unlocks).
public class AdventureRoomPuzzle : MonoBehaviour, IInteractable
{
    [Header("Progress gate")]
    public int revealFloor = 3;
    public string progressFlag = "adventure.deepestFloor", unlockFlag = "class.spellblade";
    public string unlockedClassName = "Arcanist";
    [Tooltip("The class it unlocks: its sheet portrait and name are on the card that pops up (ClassUnlockedUI).")]
    public AdventurerClass unlockedClass;
    [Tooltip("A burst over the table as it opens (Items/Effects/Chest burst), scaled up to the room.")]
    public GameObject unlockEffect;
    [Min(.1f), Tooltip("The burst is made for the dungeon (1 unit to the metre); the room is ~22 units to the metre.")]
    public float unlockEffectScale = 20f;
    [Tooltip("Solved, it gives the set of dice instead of unlocking a class; its own flag (puzzle.symbols) marks it solved.")]
    public bool givesDice = true;
    public DungeonMaster.Line diceLine = new() { text = "A set of dice was under the tiles. Put them on the table. They bring luck at the market." };
    public const string DiceSolvedFlag = "puzzle.symbols";
    string SolvedFlag => givesDice ? DiceSolvedFlag : unlockFlag;
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
    public bool Solved => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(SolvedFlag);
    public string SolvedText => givesDice ? "Solved" : $"{unlockedClassName} unlocked";
    public string Prompt => Solved ? SolvedText : TileReady ? PlacePrompt : "Puzzle";
    // Carrying the tile home: anything on the memorial table (the symbols, the figures) puts it in.
    public bool TileReady => !Solved && ProgressionManager.HasInstance && ProgressionManager.Instance.TileState == 1;
    public const string PlacePrompt = "Put the tile in";

    [Header("Quest light")]
    [Tooltip("Pulses over the puzzle while the tile is carried (its quest is open). Empty: a copy of the character sheet's hint light is made at start.")]
    public Light hint;
    [Min(0)] public float hintIntensity = 140f;

    void Start()
    {
        if (hint == null) hint = CopySheetHint();
        if (hint != null) hint.enabled = false;
        hintIntensity = CharacterSheetTable.ReachDown(hint, transform, hintIntensity);
        // The puzzle lies on the memorial table: the outline goes round the table, which reads from across the room.
        glow = (transform.parent != null ? transform.parent.gameObject : gameObject).AddComponent<QuestGlow>();
        if (ProgressionManager.HasInstance) { lastProgress = ProgressionManager.Instance.GetFlag(progressFlag); ProgressionManager.Instance.FlagChanged += OnFlag; }
        Refresh();
    }

    // The sheet's hint light, set the same height above the puzzle as it is above the sheet (a prop's light is its child).
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

    QuestGlow glow;

    // Brighter and quicker than a plain hint, like the sheet's while its quest holds the run back.
    void Update()
    {
        bool on = TileReady;
        if (glow != null) glow.Show(on);
        if (hint == null) return;
        if (hint.enabled != on) hint.enabled = on;
        if (on) hint.intensity = hintIntensity * 1.4f * (.65f + .35f * Mathf.Sin(Time.time * 3f));
    }

    void OnDestroy() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag; }

    int lastProgress;

    void OnFlag(string key, int value)
    {
        // Said as the deepest floor first reaches the reveal floor, also when a start past it skips over it.
        if (key == progressFlag)
        {
            if (value >= revealFloor && lastProgress < revealFloor && !Solved) MessageLog.Post(clueFoundMessage, MessageKind.Lore);
            lastProgress = value;
        }
        if (key == progressFlag || key == SolvedFlag || key == ProgressionManager.TileFlag || key == ProgressionManager.DiceFlag) Refresh();
    }

    void Refresh()
    {
        if (puzzleContents != null) puzzleContents.SetActive(Revealed);
        if (missingButton != null) missingButton.SetActive(Complete);
        if (inscription != null) inscription.text = Solved ? (givesDice ? "" : unlockedClassName.ToUpperInvariant()) : Revealed ? $"{step} / {solution.Length}" : "";
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
        string text = Solved ? (givesDice ? "The puzzle is solved." : $"{unlockedClassName} is unlocked.") : Complete ? clue : clue + " " + missingMessage;
        NotificationUI.Show(text);
        MessageLog.Post(text, MessageKind.Lore);
    }

    // The tiles on the table, each told how a press went (AdventurePuzzleButton: a push, green when right, a red flash
    // and every lit tile dark again when wrong).
    AdventurePuzzleButton[] buttons;
    AdventurePuzzleButton[] Buttons
    {
        get
        {
            if (buttons == null || buttons.Length == 0)
                buttons = System.Array.FindAll(FindObjectsByType<AdventurePuzzleButton>(FindObjectsInactive.Include), b => b.puzzle == this);
            return buttons;
        }
    }
    AdventurePuzzleButton ButtonFor(int symbol) => System.Array.Find(Buttons, b => b.symbol == symbol && b.gameObject.activeInHierarchy);

    public void Press(int symbol)
    {
        if (!Revealed || Solved || solution.Length == 0) return;
        var pressed = ButtonFor(symbol);
        if (pressed != null) pressed.Pulse();
        if (!Complete) { NotificationUI.Show(missingMessage); return; }
        if (solution[step] != symbol)
        {
            step = 0;
            Refresh();
            foreach (var b in Buttons) if (b != pressed) b.Light(false);
            if (pressed != null) pressed.Wrong();
            NotificationUI.Show("Wrong order. Start again.");
            return;
        }
        step++;
        Refresh();
        if (pressed != null) pressed.Light(true);
        if (step < solution.Length) { NotificationUI.Show($"{symbols[symbol]}  {step} / {solution.Length}"); return; }
        foreach (var b in Buttons) b.Light(true);
        ProgressionManager.Instance.SetFlag(SolvedFlag);
        Burst();
        if (givesDice)
        {
            ProgressionManager.Instance.GiveDice();
            MessageLog.Post("You found a set of dice under the tiles.", MessageKind.Good);
            if (diceLine != null && !diceLine.IsEmpty) DungeonMaster.Say(diceLine);
            return;
        }
        ClassUnlockedUI.Show(unlockedClass, unlockedClassName);
        MessageLog.Post($"{unlockedClassName} unlocked. You can pick it at the table.", MessageKind.Good);
    }

    // The burst sized for the room: every particle system scales with its transform.
    void Burst()
    {
        if (unlockEffect == null) return;
        var fx = Instantiate(unlockEffect, transform.position + Vector3.up * 4f, Quaternion.identity);
        fx.transform.localScale = unlockEffect.transform.localScale * unlockEffectScale;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        Destroy(fx, 4f);
    }
}
