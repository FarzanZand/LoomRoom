using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer: append only.
public enum IntroOpening { Lamp = 0, Tabletop = 1 }

// The tabletop opening: no eyes in the dark, no waking in the chair. The player wakes in bed at night; the
// only warm light is the desk lamp at the game table, where the Dungeon Master is waiting. They walk over
// themselves (he calls, and nudges if they wander); sitting down, he slides a pencil character sheet
// across and they roll dice for it, one attribute at a time. The numbers are flavour, except that at the
// first morning's table the figures for the two highest (Sheet Classes) are the ones set out. Then the
// practice figure, the same practice board, and afterwards the figure goes on the memorial shelf.
public partial class IntroController
{
    [Title("Opening", "Lamp: the original, eyes in the dark and a lamp lit by hand. Tabletop: waking in bed at night, walking to the lamp-lit table, a character sheet and dice.")]
    [PropertyOrder(-10), EnumToggleButtons] public IntroOpening opening = IntroOpening.Tabletop;

    [FoldoutGroup("Tabletop opening")] public AudioClip lampClick;
    [FoldoutGroup("Tabletop opening")] public CharacterSheet sheet;
    [FoldoutGroup("Tabletop opening"), Tooltip("The Dungeon Master's animator state as he pushes the sheet across (Characters/Animations/ThirdPerson/Sitting/Sitting Push Sheet).")]
    public string pushGesture = "Sitting Push Sheet";
    [FoldoutGroup("Tabletop opening"), Min(0)] public float pushSeconds = 1.4f;
    [FoldoutGroup("Tabletop opening"), Tooltip("A die (RollingDie) thrown three at a time.")] public RollingDie diePrefab;
    [FoldoutGroup("Tabletop opening"), Tooltip("Where the dice leave the player's hand, and where they are thrown toward.")] public Transform diceFrom, diceTo;
    [FoldoutGroup("Tabletop opening"), Tooltip("Solid only while dice roll: the table top and a low rim round it.")] public GameObject diceTray;
    [FoldoutGroup("Tabletop opening"), Tooltip("Played as dice land.")] public AudioClip[] diceSounds = new AudioClip[0];
    [FoldoutGroup("Tabletop opening"), Tooltip("Where the view turns when the figure goes on the memorial shelf.")] public Transform memorialShelf;
    [FoldoutGroup("Tabletop opening"), Tooltip("How much the view zooms in on the memorial shelf (degrees of field of view).")] public float memorialZoom = 26f;
    [FoldoutGroup("Tabletop opening"), Min(5), Tooltip("Seconds of wandering between nudges to sit down.")] public float nudgeSeconds = 30f;
    [FoldoutGroup("Tabletop opening"), Min(.1f), Tooltip("Seconds to settle into the chair.")] public float sitSeconds = 1.2f;
    [FoldoutGroup("Tabletop opening"), Tooltip("The attributes rolled for, in order (three dice each).")] public string[] rolledAttributes = { "STR", "INT", "CON" };
    [System.Serializable] public class SheetClass { public string attribute; public AdventurerClass adventurerClass; }
    [FoldoutGroup("Tabletop opening"), Tooltip("The class each rolled attribute points to. After the sheet, the figures of the highest rolls come out, the highest recommended.")]
    public SheetClass[] sheetClasses = new SheetClass[0];
    [FoldoutGroup("Tabletop opening"), Min(1), Tooltip("How many figures come out after the sheet.")] public int figuresOffered = 2;
    [FoldoutGroup("Tabletop opening"), Tooltip("With the Lamp opening: the character sheet and dice wait for the first time the player sits at the table on the first morning, before the figures.")]
    public bool sheetOnFirstMorning = true;
    [FoldoutGroup("Tabletop opening"), Tooltip("Written as the class until a figure is picked.")] public string sheetUnknownClass = "Adventurer";
    [FoldoutGroup("Tabletop opening"), Tooltip("What the clocks show at night (hour, minute).")] public Vector2Int nightTime = new(2, 47);

    [FoldoutGroup("Tabletop lines"), Tooltip("Said once the player is up, calling them to the table.")] public DungeonMaster.Line[] callLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("Said in turn while they wander instead (the last one repeats).")] public DungeonMaster.Line[] nudgeLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("Said once they are seated.")] public DungeonMaster.Line[] lampLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("The first morning's first seat at the table, before the sheet.")] public DungeonMaster.Line[] morningSheetLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("As the sheet comes across.")] public DungeonMaster.Line[] sheetLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("One per rolled attribute, before its roll.")] public DungeonMaster.Line[] rollLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("After a roll; {n} is the total. Low (8 or less), middling, high (15 or more): one is picked.")]
    public DungeonMaster.Line[] lowRoll = new DungeonMaster.Line[0], midRoll = new DungeonMaster.Line[0], highRoll = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("When all three dice show the same face; {n} is the total. Replaces the usual remark.")]
    public DungeonMaster.Line[] tripleRoll = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("When the sheet is done, before the figure.")] public DungeonMaster.Line[] afterRolls = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("Back at the table after the practice death.")] public DungeonMaster.Line[] tabletopAfterPractice = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("As the view turns to the memorial shelf.")] public DungeonMaster.Line[] memorialLines = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), Tooltip("Before he switches the lamp off.")] public DungeonMaster.Line[] tabletopGoodnight = new DungeonMaster.Line[0];
    [FoldoutGroup("Tabletop lines"), TextArea(1, 2), Tooltip("Posted to the feed while waiting for the roll.")] public string rollHint = "Click to roll the dice.";

    bool Tabletop => opening == IntroOpening.Tabletop;
    readonly List<RollingDie> dice = new();

    void SetupOpening()
    {
        if (diceTray != null) diceTray.SetActive(false);
    }

    bool waitingForSeat;
    // The Dungeon Master is waiting for the player to sit down (the table and he say so).
    public static bool WaitingForSeat => HasInstance && Instance.running && Instance.waitingForSeat;

    // Night: the room dim, the desk lamp lit, the player wakes in bed and is free to walk to the table.
    IEnumerator TabletopNight()
    {
        if (lighting != null)
        {
            roomLight ??= lighting.Capture();
            lighting.lampHeld = true;
            lighting.BlendToMood(lighting.RoomLook(tabletopRoomLighting, tabletopRoomTint), 0f);
        }
        if (dmEyes != null) dmEyes.SetActive(false);
        LightLamp(1f);
        RoomClock.SetNight(nightTime.x, nightTime.y);
        var wake = WorldManager.HasInstance ? WorldManager.Instance.wakeUpCutscene : null;
        if (wake != null)
        {
            wake.Play(false);
            yield return null;
            while (wake.IsPlaying) yield return null;
        }
        else if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(1.5f);
        yield return new WaitForSeconds(1f);
        waitingForSeat = true;
        yield return SayAll(callLines);
        // A nudge now and then while they look around.
        float next = Time.time + nudgeSeconds;
        int nudge = 0;
        while (waitingForSeat)
        {
            if (Time.time >= next && nudgeLines.Length > 0 && GameManager.Instance.State == GameState.Explore)
            {
                yield return SayAll(new[] { nudgeLines[Mathf.Min(nudge++, nudgeLines.Length - 1)] });
                next = Time.time + nudgeSeconds;
            }
            yield return null;
        }
    }

    // The table or the Dungeon Master was used while he waits: sit down and start. False otherwise.
    public bool TakeSeat()
    {
        if (!running || !waitingForSeat) return false;
        waitingForSeat = false;
        StopAllCoroutines();
        StartCoroutine(TabletopSeated());
        return true;
    }

    IEnumerator TabletopSeated()
    {
        PushDialogue();
        yield return GlideIntoSeat();
        yield return SayAll(lampLines);
        yield return SheetAndDice(practiceClass != null ? practiceClass.displayName : "", practiceClass != null ? practiceClass.description : "");
        PopDialogue();
        yield return WaitForTable();
        PlayPractice(false);
    }

    // Into the chair: from wherever they stand to the seat, the view settling on the Dungeon Master.
    IEnumerator GlideIntoSeat()
    {
        var startPos = room.transform.position;
        float y0 = room.Look.YawTransform.eulerAngles.y, p0 = room.Look.Pitch, h0 = room.Look.HeightOverride ?? 1f;
        Sit();
        float y1 = room.Look.YawTransform.eulerAngles.y, p1 = room.Look.Pitch;
        var endPos = room.transform.position;
        for (float t = 0f; t < sitSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / sitSeconds);
            room.Warp(Vector3.Lerp(startPos, endPos, k));
            room.Look.HeightOverride = Mathf.Lerp(h0, eyeHeight, k);
            room.Look.SetYaw(Mathf.LerpAngle(y0, y1, k));
            room.Look.SetPitch(Mathf.Lerp(p0, p1, k));
            yield return null;
        }
        Sit();
    }

    // He pushes the sheet across, the player rolls three dice per attribute; every roll stands.
    IEnumerator SheetAndDice(string className, string blurb)
    {
        if (sheet == null) yield break;
        sheet.Clear(className, blurb, rolledAttributes);
        rolls = new int[rolledAttributes.Length];
        // He pushes it across: the gesture, and the sheet leaves his hand at the reach.
        var dm = FindAnyObjectByType<DungeonMasterSeat>();
        if (dm != null) dm.Gesture(pushGesture, pushSeconds);
        yield return new WaitForSeconds(.3f);
        StartCoroutine(sheet.SlideIn());
        yield return SayAll(sheetLines);
        // Between the sheet and where the dice land, so both are in view.
        var sheetAt = sheet.rest != null ? sheet.rest.position : sheet.transform.position;
        yield return LookAt(diceTo != null ? Vector3.Lerp(sheetAt, diceTo.position, .45f) : sheetAt, .7f);
        // Submit (Space, gamepad South) throws the dice too; GameManager has the UI map live while seated.
        if (InputManager.HasInstance) { InputManager.Instance.SubmitPressed -= OnSubmit; InputManager.Instance.SubmitPressed += OnSubmit; }
        for (int i = 0; i < rolledAttributes.Length; i++)
        {
            if (i < rollLines.Length) yield return SayAll(new[] { rollLines[i] });
            yield return WaitForRoll(i == 0);
            int total = 0;
            yield return Roll(3, t => total = t);
            rolls[i] = total;
            yield return sheet.Write(i, SheetText(total));
            var pool = lastTriple && tripleRoll.Length > 0 ? tripleRoll : total <= 8 ? lowRoll : total >= 15 ? highRoll : midRoll;
            if (pool.Length > 0)
            {
                var line = pool[Random.Range(0, pool.Length)];
                if (line != null && !line.IsEmpty) yield return SayAll(new[] { line.With(line.text.Replace("{n}", total.ToString())) });
            }
            ClearDice();
        }
        if (InputManager.HasInstance) InputManager.Instance.SubmitPressed -= OnSubmit;
        PickFigures();
        yield return SayAll(afterRolls);
    }

    // The two highest rolls pick the figures that come out after the sheet, the highest first (a tie goes
    // to the attribute rolled first).
    void PickFigures()
    {
        offered.Clear();
        var order = new List<int>();
        for (int i = 0; i < rolledAttributes.Length && i < rolls.Length; i++) order.Add(i);
        order.Sort((a, b) => rolls[b] != rolls[a] ? rolls[b].CompareTo(rolls[a]) : a.CompareTo(b));
        foreach (int i in order)
        {
            var entry = System.Array.Find(sheetClasses, e => e != null && e.attribute == rolledAttributes[i]);
            var cls = entry != null ? entry.adventurerClass : null;
            if (cls == null || !cls.Unlocked || offered.Contains(cls)) continue;
            offered.Add(cls);
            if (offered.Count >= figuresOffered) break;
        }
    }

    readonly List<AdventurerClass> offered = new();
    // The figures the first morning's sheet asks for, while they are out. Null: the usual figures.
    public static IReadOnlyList<AdventurerClass> OfferedClasses => SheetAtTable && Instance.offered.Count > 0 ? Instance.offered : null;

    // ── The first table of the first morning (after the Lamp opening) ──

    public const string SheetFlag = "story.characterSheet";
    bool sheetAtTable;

    // The sheet and dice wait for the player's first seat at the table after the intro.
    public static bool SheetPending => HasInstance && Instance.sheetOnFirstMorning && Instance.opening == IntroOpening.Lamp && Instance.sheet != null
        && Flag(RoomOpenFlag) && !Flag(SheetFlag);
    // The sheet event just ran: the figures come out where the player sits.
    public static bool SheetAtTable => HasInstance && Instance.sheetAtTable;

    // Called by TableManager once the player is seated, before the figures.
    public IEnumerator FirstMorningSheet(Player player)
    {
        room = player;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(SheetFlag);
        sheetAtTable = true;
        if (RunManager.HasInstance) { RunManager.Instance.RunStarted -= HideSheetForRun; RunManager.Instance.RunStarted += HideSheetForRun; }
        yield return GlideIntoSeat();
        yield return SayAll(morningSheetLines);
        yield return SheetAndDice(sheetUnknownClass, "");
    }

    // The class picked from the figures goes on the sheet.
    public void WriteClass(AdventurerClass picked)
    {
        if (sheet == null || !sheetAtTable || picked == null) return;
        if (sheet.className != null) sheet.className.text = picked.displayName;
        if (sheet.description != null) sheet.description.text = picked.description;
    }

    // The player leaves the table (a run starts, or they stand up): the sheet is put away.
    public void EndSheetAtTable()
    {
        if (!sheetAtTable) return;
        sheetAtTable = false;
        if (sheet != null) sheet.Hide();
        if (room != null && room.Look != null) room.Look.HeightOverride = null;
        if (room != null && room.CameraRig != null) room.CameraRig.FovOffset = 0f;
    }

    void HideSheetForRun()
    {
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= HideSheetForRun;
        EndSheetAtTable();
    }

    // Back from the practice board: the figure goes on the shelf, the lamp clicks off.
    IEnumerator TabletopGoodnight()
    {
        // The sheet comes back; on his second line he crosses the character out.
        if (sheet != null) sheet.ShowAgain();
        if (sheet != null && room != null) yield return LookAt(sheet.transform.position, .6f);
        for (int i = 0; i < tabletopAfterPractice.Length; i++)
        {
            if (i == 1 && sheet != null)
            {
                if (lampClick != null && AudioManager.HasInstance && diceSounds.Length > 0) AudioManager.Instance.PlaySFX2D(diceSounds[0], .3f, .2f);
                StartCoroutine(sheet.CrossOut());
            }
            yield return SayAll(new[] { tabletopAfterPractice[i] });
        }
        if (tabletopAfterPractice.Length < 2 && sheet != null) yield return sheet.CrossOut();
        yield return new WaitForSeconds(.5f);
        if (memorialShelf != null)
        {
            float yaw = room.Look.YawTransform.eulerAngles.y, pitch = room.Look.Pitch;
            StartCoroutine(CameraEase.Zoom(room.CameraRig, zoom - memorialZoom, 1.3f));
            yield return LookAt(memorialShelf.position, 1.1f);
            yield return SayAll(memorialLines);
            yield return new WaitForSeconds(.8f);
            StartCoroutine(CameraEase.Zoom(room.CameraRig, zoom, .9f));
            yield return CameraEase.Turn(room.Look, yaw, pitch, .9f, eyeHeight);
        }
        yield return SayAll(tabletopGoodnight);
        yield return new WaitForSeconds(.6f);
        yield return LampClick(false);
        if (sheet != null) sheet.Hide();
        RoomClock.SetNight(-1, 0);
        yield return new WaitForSeconds(nightSeconds);
    }

    IEnumerator LampClick(bool on)
    {
        if (lampClick != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(lampClick, .8f);
        LightLamp(on ? 1f : 0f);
        yield return new WaitForSeconds(on ? .7f : .2f);
    }

    bool rollRequested, submitted, lastTriple;
    int[] rolls = new int[3];

    // The rolls are flavour: written on the sheet, never applied to the character. They only choose
    // which figures come out.
    static string SheetText(int total) => total.ToString();

    // Throws the dice now, as a click would (other input, tests).
    public void RequestRoll() => rollRequested = true;

    void OnSubmit() => submitted = true;

    // Waits for a click (or Submit, through OnSubmit); after a while the dice are thrown anyway.
    IEnumerator WaitForRoll(bool first)
    {
        if (first && !string.IsNullOrEmpty(rollHint)) MessageLog.Post(rollHint, MessageKind.Info);
        float until = Time.time + (first ? 20f : 8f);
        yield return null;
        submitted = false; // Submit pressed during the line before does not count
        while (Time.time < until)
        {
            bool clicked = InputManager.HasInstance && InputManager.Instance.PointerClicked;
            if (clicked || submitted || rollRequested) { rollRequested = submitted = false; yield break; }
            yield return null;
        }
    }

    // Three dice from the player's side of the table, toward the middle. Reports their total.
    IEnumerator Roll(int count, System.Action<int> done)
    {
        if (diePrefab == null || diceFrom == null || diceTo == null) { int fake = 0; for (int i = 0; i < count; i++) fake += Random.Range(1, 7); done(fake); yield break; }
        if (diceTray != null) diceTray.SetActive(true);
        var toward = diceTo.position - diceFrom.position; toward.y = 0; toward.Normalize();
        var side = Vector3.Cross(Vector3.up, toward);
        for (int i = 0; i < count; i++)
        {
            var die = Instantiate(diePrefab, diceFrom.position + side * ((i - (count - 1) * .5f) * 1.3f) + Vector3.up * (i * .2f), Random.rotation);
            die.Throw(toward * Random.Range(24f, 30f) + side * Random.Range(-3f, 3f) + Vector3.up * Random.Range(5f, 9f),
                Random.insideUnitSphere * Random.Range(28f, 42f));
            dice.Add(die);
        }
        if (diceSounds != null && diceSounds.Length > 0 && AudioManager.HasInstance)
            StartCoroutine(Rattle());
        float giveUp = Time.time + 4f;
        yield return new WaitForSeconds(.25f);
        while (Time.time < giveUp && dice.Exists(d => d != null && !d.Settled)) yield return null;
        int total = 0, first = -1;
        lastTriple = dice.Count > 1;
        foreach (var d in dice) if (d != null)
        {
            total += d.Value; d.Body.isKinematic = true;
            if (first < 0) first = d.Value; else if (d.Value != first) lastTriple = false;
        }
        yield return new WaitForSeconds(.35f);
        done(total);
    }

    IEnumerator Rattle()
    {
        foreach (var delay in new[] { .16f, .07f, .1f, .14f })
        {
            yield return new WaitForSeconds(delay);
            AudioManager.Instance.PlaySFX2D(diceSounds[Random.Range(0, diceSounds.Length)], .35f, .1f);
        }
    }

    // The dice are scooped up: each shrinks away in turn.
    void ClearDice()
    {
        int i = 0;
        foreach (var d in dice) if (d != null) StartCoroutine(Scoop(d.gameObject, i++ * .06f));
        dice.Clear();
        if (diceTray != null) diceTray.SetActive(false);
    }

    static IEnumerator Scoop(GameObject die, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (die == null) yield break;
        var t = die.transform; var scale = t.localScale; var from = t.position;
        for (float k = 0f; k < 1f; k += Time.deltaTime / .18f)
        {
            if (t == null) yield break;
            t.localScale = scale * (1f - k * k);
            t.position = from + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .6f);
            yield return null;
        }
        Destroy(die);
    }

    // The seated view eases to a point, keeping its eye height (the seat's when none is set).
    IEnumerator LookAt(Vector3 point, float seconds)
        => CameraEase.LookAt(room.Look, point, seconds, room.Look.HeightOverride ?? eyeHeight);
}
