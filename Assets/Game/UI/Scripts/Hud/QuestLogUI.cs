using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The quest log on the HUD, at the right edge: the open quests (ProgressionManager > Quests), one line each under a title.
// A new quest pops in (a gold flash that settles to white, a little bigger at first) and the whole log slides in from the
// right when it appears; a quest that is done turns green and crossed out with a pop, stays a moment, then fades. The log
// hides when nothing is open and whenever the game is not plain exploring (inventory, shop, pause, sheet, recap,
// cutscenes), during a boss fight and while the win ending plays. Authored in the scene (made once by QuestLogSetup:
// backing panel, title, accent line, row template); this binds the title and clones the row template.
public class QuestLogUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField, Tooltip("One line, switched off; cloned per quest.")] TMP_Text rowTemplate;
    [SerializeField] Color openColour = Color.white;
    [SerializeField] Color doneColour = new(.55f, .9f, .5f);
    [SerializeField, Min(0)] float doneSeconds = 3f;
    [SerializeField, Min(.05f)] float fadeSeconds = .6f;

    [Header("Pop")]
    [SerializeField, Tooltip("Slides in from the right as the log appears (the log's own rect).")] RectTransform slide;
    [SerializeField, Tooltip("How far right the log starts when it appears (canvas units).")] float slideDistance = 120f;
    [SerializeField, Min(.05f)] float slideSeconds = .35f;
    [SerializeField, Tooltip("A new quest's line starts in this colour and settles to Open Colour.")] Color newColour = new(1f, .82f, .38f);
    [SerializeField, Min(1f), Tooltip("A new or finished quest's line starts this much bigger and settles back.")] float popScale = 1.25f;
    [SerializeField, Min(.05f)] float popSeconds = .5f;
    [SerializeField, Tooltip("Pulses with every new quest (the title, or anything else); optional.")] RectTransform pulse;
    [SerializeField, Tooltip("UI Library key played when a quest is added; empty = silent.")] string newQuestSound = "";
    [SerializeField, Tooltip("UI Library key played when a quest is done; empty = silent.")] string doneQuestSound = "";

    readonly Dictionary<string, TMP_Text> rows = new();
    readonly Dictionary<string, float> doneAt = new();
    readonly Dictionary<string, float> poppedAt = new();
    ProgressionManager progression;
    InputManager input;
    readonly List<string> finished = new();
    float slideStart = -10f, pulseStart = -10f;
    bool visible, connected;

    void OnEnable()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        Connect();
    }

    void OnDisable()
    {
        if (progression != null) progression.FlagChanged -= FlagChanged;
        progression = null;
        if (input != null) input.BindingsChanged -= Rebuild;
        input = null;
    }

    // The HUD can wake before the managers do: connect as soon as ProgressionManager is there.
    void Connect()
    {
        // Lines name the player's keys: written again after a rebind.
        if (input == null && InputManager.HasInstance) { input = InputManager.Instance; input.BindingsChanged += Rebuild; }
        if (progression != null || !ProgressionManager.HasInstance) return;
        progression = ProgressionManager.Instance;
        progression.FlagChanged += FlagChanged;
        Rebuild();
        connected = true;   // quests open from before (a loaded save) show without a pop
    }

    void FlagChanged(string key, int value)
    {
        if (!key.StartsWith(ProgressionManager.QuestPrefix)) return;
        string id = key.Substring(ProgressionManager.QuestPrefix.Length);
        if (value == 2 && rows.ContainsKey(id))
        {
            doneAt[id] = Time.unscaledTime;
            poppedAt[id] = Time.unscaledTime;
            Sound(doneQuestSound);
        }
        Rebuild();
    }

    void Rebuild()
    {
        if (progression == null || rowTemplate == null) return;
        foreach (var q in progression.quests)
        {
            if (q == null || string.IsNullOrEmpty(q.id)) continue;
            int state = progression.QuestState(q.id);
            bool show = (state == 1 || doneAt.ContainsKey(q.id)) && (!ProgressionManager.DungeonOnly(q.id) || InDungeon) && !(ProgressionManager.RoomOnly(q.id) && InDungeon);
            if (!show) { if (rows.TryGetValue(q.id, out var gone)) { Destroy(gone.gameObject); rows.Remove(q.id); poppedAt.Remove(q.id); } continue; }
            if (!rows.TryGetValue(q.id, out var row))
            {
                row = Instantiate(rowTemplate, rowTemplate.transform.parent);
                row.gameObject.SetActive(true);
                rows[q.id] = row;
                if (connected && state == 1)
                {
                    poppedAt[q.id] = Time.unscaledTime;
                    pulseStart = Time.unscaledTime;
                    Sound(newQuestSound);
                }
            }
            // Done and opened again before its row faded out (the trophies hung, then another one bought): it stays.
            if (state == 1 && doneAt.Remove(q.id)) row.alpha = 1f;
            string text = progression.QuestText(q);
            row.text = state == 2 ? $"<s>{text}</s>" : text;
            row.color = state == 2 ? doneColour : openColour;
        }
    }

    static void Sound(string key)
    {
        if (!string.IsNullOrEmpty(key) && AudioManager.HasInstance && AudioManager.Instance.HasUI(key)) AudioManager.Instance.PlayUI(key);
    }

    // The table player is the one being played: in a dungeon (dungeon-only quests show).
    static bool InDungeon => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
    bool shownInDungeon;

    void Update()
    {
        if (progression == null || input == null) Connect();
        if (InDungeon != shownInDungeon) { shownInDungeon = InDungeon; Rebuild(); }
        float now = Time.unscaledTime;
        if (doneAt.Count > 0)
        {
            finished.Clear();
            foreach (var pair in doneAt)
            {
                float t = now - pair.Value - doneSeconds;
                if (rows.TryGetValue(pair.Key, out var row)) row.alpha = 1f - Mathf.Clamp01(t / fadeSeconds);
                if (t >= fadeSeconds) finished.Add(pair.Key);
            }
            foreach (var id in finished) doneAt.Remove(id);
            if (finished.Count > 0) Rebuild();
        }
        bool hidden = false;
        if (group != null)
        {
            // Out of the way of every menu, of a boss fight and of the ending's walk to the door.
            hidden = (GameManager.HasInstance && GameManager.Instance.State != GameState.Explore)
                || BossStageEncounter.Current != null || TableManager.EndingRunning;
            float target = rows.Count > 0 && !hidden ? 1f : 0f;
            group.alpha = hidden ? 0f : Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }
        bool nowVisible = rows.Count > 0 && !hidden;
        if (nowVisible && !visible) slideStart = now;
        visible = nowVisible;
        Animate(now);
    }

    void Animate(float now)
    {
        if (slide != null)
        {
            float k = Mathf.Clamp01((now - slideStart) / slideSeconds);
            var p = slide.anchoredPosition;
            p.x = Mathf.Round(slideX + slideDistance * (1f - EaseOut(k)));
            slide.anchoredPosition = p;
        }
        if (pulse != null)
        {
            float k = Mathf.Clamp01((now - pulseStart) / popSeconds);
            pulse.localScale = Vector3.one * Mathf.LerpUnclamped(popScale, 1f, EaseOutBack(k));
        }
        List<string> settled = null;
        foreach (var pair in poppedAt)
        {
            if (!rows.TryGetValue(pair.Key, out var row)) continue;
            float k = Mathf.Clamp01((now - pair.Value) / popSeconds);
            row.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(popScale, 1f, EaseOutBack(k));
            bool done = doneAt.ContainsKey(pair.Key);
            if (!done) row.color = Color.Lerp(newColour, openColour, k * k);
            if (k >= 1f) (settled ??= new List<string>()).Add(pair.Key);
        }
        if (settled != null) foreach (var id in settled) poppedAt.Remove(id);
    }

    float slideX;
    void Awake() { if (slide != null) slideX = slide.anchoredPosition.x; }

    static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    static float EaseOutBack(float k) { const float c = 1.7f; float t = k - 1f; return 1f + (c + 1f) * t * t * t + c * t * t; }
}
