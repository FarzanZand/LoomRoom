using System.Collections.Generic;
using UnityEngine;

// He watches you play. Outside a boss fight the Dungeon Master never looked at the figure he is running the game
// for: he sat in his idle pose while you fought on his table. Now, during a run, his head and eyes follow your figure
// across the table (he glances away now and then, never while something is after you), and when you stand where you
// can see him and stare up at him, he notices: he looks straight at you and says so ("Eyes on the board."), a few
// lines that go from polite to short over a run. It costs nothing and changes no number; it is there so that the
// giant at the table is someone, not scenery.
// It drives only his look (DungeonMasterHand.Watch, the look-at he already has for boss stages), and stands down
// whenever anything else has him: a boss's stage, his hand reaching into the dungeon, a spell of his.
// It goes with DungeonMasterWatch (the light on his face while you are on a floor with the roofs off, and those
// roofless rooms): that is what lets you see him over the walls; this is what he does once you can.
// It is not in the scene: at Play it is made from Levels/Resources/Dungeon Master gaze (the prefab holds its settings)
// under the RunManager, like the floor's bet.
public class DungeonMasterGaze : MonoBehaviour
{
    [Tooltip("Off: he sits as he always did.")] public bool watchOn = true;
    [Range(0, 1), Tooltip("How far his head turns to the figure (1 = all the way).")] public float watchWeight = .7f;
    [Tooltip("Seconds he keeps his eyes on the figure, then seconds he looks away (each a random time between the two).")]
    public Vector2 watchSeconds = new(8f, 16f);
    public Vector2 restSeconds = new(2.5f, 5f);
    [Min(0), Tooltip("He never looks away while an enemy within this of the figure is after it (metres).")] public float fightReach = 12f;

    [Header("Stared at")]
    [Range(1, 30), Tooltip("The view counts as on him within this many degrees of his head, up close.")] public float stareAngle = 7f;
    [Min(.1f), Tooltip("How big his head is across (world units): from far off the view has to be on the head as it shows from there, not merely near it.")] public float headSize = 6f;
    [Min(1), Tooltip("He answers no stare from further off than this (metres): too small to read.")] public float stareWithin = 90f;
    [Min(.2f), Tooltip("Seconds of looking at him before he says something.")] public float stareSeconds = 1.6f;
    [Min(0), Tooltip("Seconds before he answers another stare.")] public float stareGap = 40f;
    [Tooltip("His first answers of a run, in order.")]
    public List<DungeonMaster.Line> firstLines = new()
    {
        new() { text = "Yes. That's me up here.", inPerson = true },
        new() { text = "Eyes on the board.", inPerson = true },
        new() { text = "I can see you staring.", inPerson = true },
    };
    [Tooltip("After those, one of these.")]
    public List<DungeonMaster.Line> moreLines = new()
    {
        new() { text = "What? Have I got something on my face?", inPerson = true },
        new() { text = "Don't look at me. Look at them.", inPerson = true },
        new() { text = "I'm not part of the dungeon.", inPerson = true },
        new() { text = "The dungeon is down there.", inPerson = true },
        new() { text = "Stop that.", inPerson = true },
    };
    [Tooltip("Stared at while something is after the figure.")]
    public List<DungeonMaster.Line> fightLines = new()
    {
        new() { text = "Not me. Them.", inPerson = true },
        new() { text = "Watch your fight, not me.", inPerson = true },
    };

    [Header("The roof off")]
    [Tooltip("Said the first time the figure stands in a room with its roof off: always on a save's first such run, then by Roof Line Chance a run.")]
    public List<DungeonMaster.Line> roofLines = new()
    {
        new() { text = "I took the roof off that one. I like to watch.", inPerson = true },
        new() { text = "No lid on that room. Don't mind me.", inPerson = true },
        new() { text = "There you are. Much better with the roof off.", inPerson = true },
    };
    [Range(0, 1)] public float roofLineChance = .35f;
    [Min(0), Tooltip("Seconds in the room before he says it.")] public float roofLineDelay = 1.5f;
    public const string RoofSeenFlag = "story.roofsSeen";
    [Min(0), Tooltip("Not said in the first seconds of a run, which have their own lines (the place, the bet).")] public float roofLineAfter = 30f;
    int roofSeed;
    bool roofDue;
    float roofTime, roofRunStart;

    [Header("Gestures")]
    [Tooltip("Off: he says his lines with his arms still, as before.")] public bool talksWithHands = true;
    [Tooltip("His animator's gesture triggers (the upper-body gestures every humanoid has): played as a line of his appears during a run.")]
    public string talkTrigger = "Talk";
    [Tooltip("With his first answer to a stare.")] public string waveTrigger = "Wave";
    [Tooltip("With \"Eyes on the board.\" and the lines in a fight.")] public string pointTrigger = "Point";
    [Min(0), Tooltip("Seconds before another gesture starts.")] public float gestureGap = 2.2f;

    static DungeonMasterGaze current;
    const string PrefabName = "Dungeon Master gaze";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        current = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void AfterFirstScene() => OnScene(default, default);
    static void OnScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (current != null || !RunManager.HasInstance) return;
        var prefab = Resources.Load<GameObject>(PrefabName);
        if (prefab != null) Instantiate(prefab, RunManager.Instance.transform).name = prefab.name;
    }

    DungeonMasterHand hand;
    Transform head, body;
    Player player;
    bool mine;            // his look is this script's doing just now
    bool resting;         // looking away for a moment
    float phaseEnds, nextFightCheck, stared, lastAnswer = -999f, attentionUntil;
    bool fighting;
    int answers, answersSeed, lastMore = -1;

    void OnEnable() { current = this; DungeonMaster.Spoke += OnSpoke; }
    void OnDisable() { if (current == this) current = null; DungeonMaster.Spoke -= OnSpoke; LetGo(); }

    // He talks with his hands. During a run every line of his plays the talking gesture as it appears, so when you
    // can see him he is seen saying it; an answer to a stare has its own (a wave, a finger at the board).
    Animator animator;
    DungeonMaster.Line answerLine;
    string answerGesture;
    float gestureAt = -999f;
    void OnSpoke(DungeonMaster.Line line)
    {
        if (!talksWithHands || line == null || !Watching() || animator == null || !animator.isActiveAndEnabled) return;
        string trigger = line == answerLine && !string.IsNullOrEmpty(answerGesture) ? answerGesture : talkTrigger;
        if (line == answerLine) answerLine = null;
        else if (Time.time - gestureAt < gestureGap) return;
        if (string.IsNullOrEmpty(trigger)) return;
        gestureAt = Time.time;
        AnimatorHelper.SetTrigger(animator, trigger);
    }

    void Update()
    {
        if (!Watching())
        {
            // The bag, the pause menu or the level-up panels open over a run: his look stays as it is (letting go and
            // taking it up again turned his head away and back each time the bag was opened).
            if (!MenuOverRun()) LetGo();
            stared = 0f; return;
        }
        var run = RunManager.Instance;
        if (run.Seed != answersSeed) { answersSeed = run.Seed; answers = 0; }
        var figure = player.transform.position + Vector3.up * 1.2f;

        if (Time.time >= nextFightCheck) { nextFightCheck = Time.time + .5f; fighting = SomethingAfter(); }
        RoofLine(run);
        bool staredAt = StaredAt();
        stared = staredAt ? stared + Time.deltaTime : Mathf.Max(0f, stared - Time.deltaTime * 2f);
        if (staredAt) attentionUntil = Time.time + 2f;

        // Watching and looking away take turns; a fight or a stare keeps his eyes on the figure.
        if (Time.time >= phaseEnds)
        {
            resting = !resting;
            var span = resting ? restSeconds : watchSeconds;
            phaseEnds = Time.time + Random.Range(span.x, span.y);
        }
        bool attention = Time.time < attentionUntil;
        bool look = !resting || fighting || attention;
        hand.Watch(figure, look, attention ? 1f : watchWeight);
        mine = true;

        if (stared >= stareSeconds && Time.time - lastAnswer >= stareGap && !DungeonMasterRemarks.Hold
            && (!DungeonMaster.HasInstance || !DungeonMaster.Instance.Speaking)) Answer();
    }

    // The first open room of a run: he says why there is no roof (once; not every run after a save's first).
    void RoofLine(RunManager run)
    {
        if (roofSeed != run.Seed)
        {
            roofSeed = run.Seed; roofTime = 0f; roofRunStart = Time.time;
            bool seen = ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(RoofSeenFlag);
            roofDue = roofLines.Count > 0 && (!seen || Random.value < roofLineChance);
        }
        if (!roofDue) return;
        var dungeon = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        bool open = dungeon != null && dungeon.RoofsOffRooms != null && dungeon.RoofsOffRooms.Contains(dungeon.RoomOf(player.transform.position));
        roofTime = open && !fighting ? roofTime + Time.deltaTime : 0f;
        if (roofTime < roofLineDelay || DungeonMasterRemarks.Hold) return;
        if (roofTime < roofLineDelay || Time.time - roofRunStart < roofLineAfter || DungeonMasterRemarks.Hold || DungeonMaster.QuietFor < 6f) return;
        roofDue = false;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(RoofSeenFlag);
        DungeonMaster.Say(roofLines[Random.Range(0, roofLines.Count)]);
    }

    static bool MenuOverRun()
    {
        if (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended || DungeonMasterReach.Busy) return false;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table || !GameManager.HasInstance) return false;
        var state = GameManager.Instance.State;
        return state == GameState.Inventory || state == GameState.Paused || state == GameState.Menu;
    }

    bool Watching()
    {
        if (!watchOn || !RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended || IntroController.PracticeRunning) return false;
        if (!GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return false;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return false;
        if (DungeonMasterReach.Busy) return false;
        if (player == null) player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (hand == null)
        {
            hand = DungeonMasterReach.Hand;
            animator = hand != null ? hand.GetComponent<Animator>() : null;
            head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            body = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform : hand != null ? hand.transform : null;
        }
        return player != null && player.IsAlive && hand != null && hand.isActiveAndEnabled && head != null;
    }

    // His look goes back to how the animation has it, once, when this script stops steering it.
    void LetGo()
    {
        if (!mine) return;
        mine = false;
        if (hand != null && !DungeonMasterReach.Busy) hand.Watch(Vector3.zero, false);
    }

    bool SomethingAfter()
    {
        float reach = fightReach * fightReach;
        foreach (var b in EnemyBrain.Active)
        {
            if (b == null || b.State != EnemyState.Chase && b.State != EnemyState.Attack) continue;
            if ((b.transform.position - player.transform.position).sqrMagnitude <= reach) return true;
        }
        return false;
    }

    // The view as it is drawn (the camera, not the player's neck: in Update the neck is not yet turned to this frame's look).
    static Transform Eye()
    {
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam == null) cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    // The view is on his head and nothing stands between (a ceiling, a wall).
    bool StaredAt()
    {
        var eye = Eye();
        if (eye == null) return false;
        var to = head.position - eye.position;
        float distance = to.magnitude;
        if (distance > stareWithin) return false;
        // On his head as it shows from here, with a little room round it; never wider than Stare Angle.
        float shows = Mathf.Atan(headSize * .5f / Mathf.Max(1f, distance)) * Mathf.Rad2Deg * 1.3f + 1f;
        if (Vector3.Angle(eye.forward, to) > Mathf.Min(stareAngle, shows)) return false;
        return LineOfSight.Clear(eye.position, head.position, ~0, player.transform, body, charactersBlock: false);
    }

    void Answer()
    {
        lastAnswer = Time.time; stared = 0f;
        attentionUntil = Time.time + 4f;
        DungeonMaster.Line line;
        string gesture = talkTrigger;
        if (fighting && fightLines.Count > 0) { line = fightLines[Random.Range(0, fightLines.Count)]; gesture = pointTrigger; }
        else if (answers < firstLines.Count) { line = firstLines[answers]; gesture = answers == 0 ? waveTrigger : answers == 1 ? pointTrigger : talkTrigger; }
        else if (moreLines.Count > 0)
        {
            int i = Random.Range(0, moreLines.Count);
            if (i == lastMore && moreLines.Count > 1) i = (i + 1) % moreLines.Count;
            lastMore = i; line = moreLines[i];
        }
        else return;
        if (!fighting) answers++;
        if (line == null || line.IsEmpty) return;
        answerLine = line; answerGesture = gesture;
        DungeonMaster.Say(line);
    }

#if UNITY_EDITOR || DEBUG
    // For tests, from an eval in Play on a floor: what it is doing, and where his head is (to turn the view to it).
    public static string TestState()
    {
        if (current == null) return "no DungeonMasterGaze";
        var w = current;
        if (!w.Watching()) return "not watching (no run, not exploring, he is busy, or no head found: hand " + (w.hand != null) + ", head " + (w.head != null) + ")";
        var eye = Eye();
        return "watching, resting " + w.resting + ", fighting " + w.fighting + ", stared " + w.stared.ToString("0.0") + " s, answers " + w.answers
            + ", angle to his head " + Vector3.Angle(eye.forward, w.head.position - eye.position).ToString("0") + " degrees, clear " + LineOfSight.Clear(eye.position, w.head.position, ~0, w.player.transform, w.body, charactersBlock: false)
            + ", his head at " + w.head.position.ToString("0.0") + ", " + (w.head.position - eye.position).magnitude.ToString("0") + " m off";
    }
    public static Vector3 TestHead => current != null && current.head != null ? current.head.position : Vector3.zero;
#endif
}
