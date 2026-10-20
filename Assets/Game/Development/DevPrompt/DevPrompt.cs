#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// F9, Editor and development builds only (the friend's tool used Shift+Space; here Shift is sprint and Space jump): a request to Main (the Claude session that works on the game)
// about what is on screen. The screenshot and the game state are taken first, then the game pauses (Menu state,
// time stopped) and the window opens: write what Main should look into, click the screenshot to draw on it, and
// Enter (or Send) saves the request to the outbox (DevReportStore) with the marked screenshot and the state. Shift+Enter
// is a new line, Escape closes the enlarged screenshot first, then the window. Nothing is sent from the game: the Main
// session picks saved requests up and writes back their status, shown on the left. The window is
// Development/Resources/Dev Prompt.prefab (built by DevPromptBuilder.Build() from an eval; edit it freely).
public class DevPrompt : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] TMP_InputField message;
    [SerializeField] RawImage thumbnail;
    [SerializeField] Button thumbnailButton;
    [SerializeField] TMP_Text stateLine;
    [SerializeField] TMP_Text statusLine;
    [SerializeField] TMP_Text requests;
    [SerializeField] TMP_Text counts;
    [SerializeField] Button sendButton, closeButton, outboxButton, closeOnSendButton;
    [SerializeField] TMP_Text closeOnSendLabel;
    [Header("Markup")]
    [SerializeField] GameObject markupPanel;
    [SerializeField] RawImage markupImage;
    [SerializeField] AspectRatioFitter markupAspect;
    [SerializeField] Button[] paletteButtons = new Button[0];
    [SerializeField] Button undoButton, redoButton, clearButton, doneButton;

    const string CloseOnSendPref = "dev.prompt.closeOnSend";

    public static bool IsOpen { get; private set; }
    bool open, capturing;
    float timeScaleBefore;
    DevPromptMarkup markup;
    string stateJson, stateSummary;
    float nextRefresh;
    int submitFrame = -10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!Debug.isDebugBuild || FindAnyObjectByType<DevPrompt>() != null) return;
        var prefab = Resources.Load<GameObject>("Dev Prompt");
        if (prefab == null) { Debug.LogWarning("[DevPrompt] Development/Resources/Dev Prompt.prefab is missing."); return; }
        var go = Instantiate(prefab);
        go.name = "Dev Prompt";
        DontDestroyOnLoad(go);
    }

    static bool ShiftHeld { get { var k = Keyboard.current; return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed); } }

    void Awake()
    {
        panel.SetActive(false);
        if (markupPanel != null) markupPanel.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += CloseOnUnload;
        message.lineType = TMP_InputField.LineType.MultiLineSubmit;   // Enter sends, Shift+Enter is a new line
        message.characterLimit = DevReportStore.MaxMessage;
        message.onSubmit.AddListener(_ => Send());
        // The Space of the chord that opened the window never lands in the text.
        message.onValidateInput = (text, index, c) => capturing || Time.frameCount <= openedFrame + 1 ? '\0' : c;
        if (sendButton != null) sendButton.onClick.AddListener(Send);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (outboxButton != null) outboxButton.onClick.AddListener(OpenOutbox);
        if (closeOnSendButton != null) closeOnSendButton.onClick.AddListener(() => { CloseOnSend = !CloseOnSend; ShowCloseOnSend(); });
        if (thumbnailButton != null) thumbnailButton.onClick.AddListener(OpenMarkup);
        for (int i = 0; i < paletteButtons.Length; i++) { int c = i; if (paletteButtons[i] != null) paletteButtons[i].onClick.AddListener(() => PickColour(c)); }
        if (undoButton != null) undoButton.onClick.AddListener(() => { markup?.Undo(); ShowMarkup(); });
        if (redoButton != null) redoButton.onClick.AddListener(() => { markup?.Redo(); ShowMarkup(); });
        if (clearButton != null) clearButton.onClick.AddListener(() => { markup?.Clear(); ShowMarkup(); });
        if (doneButton != null) doneButton.onClick.AddListener(CloseMarkup);
        if (markupImage != null)
        {
            var surface = markupImage.GetComponent<DevPromptDrawSurface>();
            if (surface != null) surface.Changed = ShowMarkup;
        }
        ShowCloseOnSend();
    }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= CloseOnUnload;
        if (open) Close();
        markup?.Release();
    }

    void CloseOnUnload(UnityEngine.SceneManagement.Scene scene) { if (open) Close(); }

    static bool CloseOnSend
    {
        get => PlayerPrefs.GetInt(CloseOnSendPref, 0) == 1;
        set { PlayerPrefs.SetInt(CloseOnSendPref, value ? 1 : 0); PlayerPrefs.Save(); }
    }
    void ShowCloseOnSend() { if (closeOnSendLabel != null) closeOnSendLabel.text = CloseOnSend ? "Close on send: on" : "Close on send: off"; }

    int openedFrame = -10;

    void Update()
    {
        var k = Keyboard.current;
        if (k == null) return;
        // Once per press, never on key repeat (wasPressedThisFrame), whatever the game was doing.
        if (k.f9Key.wasPressedThisFrame && !capturing)
        {
            if (open) Close(); else StartCoroutine(OpenWithCapture());
            return;
        }
        if (!open) return;
        if (k.escapeKey.wasPressedThisFrame) { if (MarkupOpen) CloseMarkup(); else Close(); return; }
        if (MarkupOpen && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed) && k.zKey.wasPressedThisFrame)
        {
            if (ShiftHeld) markup?.Redo(); else markup?.Undo();
            ShowMarkup();
        }
        if (MarkupOpen)
        {
            if (k.rKey.wasPressedThisFrame) PickColour(0);
            if (k.gKey.wasPressedThisFrame) PickColour(1);
            if (k.bKey.wasPressedThisFrame) PickColour(2);
            if (k.yKey.wasPressedThisFrame) PickColour(3);
        }
        // Middle click on the list: the finished requests leave it (only here; a request that starts again comes back).
        var mouse = Mouse.current;
        if (mouse != null && mouse.middleButton.wasPressedThisFrame && requests != null &&
            RectTransformUtility.RectangleContainsScreenPoint(requests.rectTransform.parent as RectTransform, mouse.position.ReadValue(), null))
        {
            foreach (var e in DevReportStore.List(500))
                if (!e.dismissed && (e.status == DevReportStore.Status.Done || e.status == DevReportStore.Status.Failed)) DevReportStore.Dismiss(e.id);
            RefreshRequests();
        }
        if (Time.unscaledTime >= nextRefresh) RefreshRequests();
    }

    bool MarkupOpen => markupPanel != null && markupPanel.activeSelf;

    // The picture and the state are taken before the window shows, so neither has the window in it.
    IEnumerator OpenWithCapture()
    {
        capturing = true;
        yield return new WaitForEndOfFrame();
        // A marked screenshot from an unsent draft stays with that draft; otherwise this moment is taken.
        if (markup == null || !markup.HasMarks)
        {
            markup?.Release();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            markup = new DevPromptMarkup(shot);
            var state = DevGameSnapshot.Capture();
            stateJson = DevGameSnapshot.ToJson(state);
            stateSummary = Summary(state);
        }
        capturing = false;
        Open();
    }

    static string Summary(DevGameSnapshot.State s)
    {
        var sb = new StringBuilder();
        sb.Append(s.scene);
        if (!string.IsNullOrEmpty(s.active_player)) sb.Append(" · ").Append(s.active_player).Append(" player");
        if (!string.IsNullOrEmpty(s.level)) sb.Append(" · ").Append(s.level).Append(" floor ").Append(s.floor);
        if (!string.IsNullOrEmpty(s.room)) sb.Append(" · ").Append(s.room);
        if (s.looking_at != null) sb.Append("\nLooking at: ").Append(s.looking_at.name).Append($" ({s.looking_at.distance:0.#} m)");
        return sb.ToString();
    }

    void Open()
    {
        if (open) return;
        open = IsOpen = true;
        openedFrame = Time.frameCount;
        timeScaleBefore = Time.timeScale;
        Time.timeScale = 0f;
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        panel.SetActive(true);
        thumbnail.texture = markup.Composed;
        if (stateLine != null) stateLine.text = "Game state attached: " + stateSummary;
        Status("");
        RefreshRequests();
        message.ActivateInputField();
    }

    void Close()
    {
        if (!open) return;
        CloseMarkup();
        open = IsOpen = false;
        panel.SetActive(false);
        Time.timeScale = timeScaleBefore;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void Send()
    {
        if (!open || Time.frameCount == submitFrame) return;   // held Enter sends once
        submitFrame = Time.frameCount;
        string text = message.text;
        var problem = DevReportStore.Validate(text);
        if (problem != null) { Status(problem); message.ActivateInputField(); return; }
        string id;
        try { id = DevReportStore.Save(text, markup != null ? markup.EncodePng() : null, stateJson, "game"); }
        catch (System.Exception e)
        {
            // Nothing is lost: the text and the marks stay as they are.
            Status("Could not save: " + e.Message);
            Debug.LogException(e);
            return;
        }
        message.text = "";
        markup?.Release();
        markup = null;
        Status($"Saved for Main ({id.Substring(0, 8)}). Main picks it up from DevReports.");
        RefreshRequests();
        if (CloseOnSend) { Close(); return; }
        // The next draft starts from an unmarked picture of the same moment.
        StartCoroutine(RetakeWhileOpen());
    }

    IEnumerator RetakeWhileOpen()
    {
        panel.SetActive(false);
        capturing = true;
        yield return new WaitForEndOfFrame();
        markup = new DevPromptMarkup(ScreenCapture.CaptureScreenshotAsTexture());
        capturing = false;
        panel.SetActive(true);
        thumbnail.texture = markup.Composed;
        message.ActivateInputField();
    }

    void Status(string text) { if (statusLine != null) statusLine.text = text; }

    void OpenOutbox()
    {
        System.IO.Directory.CreateDirectory(DevReportStore.Root);
        Application.OpenURL("file:///" + DevReportStore.Root.Replace('\\', '/'));
    }

    // The requests made from here: open ones first, then finished ones until they are dismissed (middle click).
    void RefreshRequests()
    {
        nextRefresh = Time.unscaledTime + 3f;
        if (requests == null) return;
        var list = DevReportStore.List();
        int active = 0, queued = 0;
        var sb = new StringBuilder();
        foreach (var e in list)
        {
            bool finished = e.status == DevReportStore.Status.Done || e.status == DevReportStore.Status.Failed;
            if (e.status == DevReportStore.Status.Saved) queued++;
            else if (!finished) active++;
        }
        foreach (var pass in new[] { false, true })
            foreach (var e in list)
            {
                bool finished = e.status == DevReportStore.Status.Done || e.status == DevReportStore.Status.Failed;
                if (finished != pass || (finished && e.dismissed)) continue;
                string first = e.message.Split('\n')[0];
                if (first.Length > 34) first = first.Substring(0, 33) + "…";
                string colour = e.status switch { DevReportStore.Status.Done => "#8fd18b", DevReportStore.Status.Failed => "#ff8a7a", DevReportStore.Status.Saved => "#c9c2b8", _ => "#ffd27a" };
                sb.Append("<color=").Append(colour).Append('>').Append(DevReportStore.ShortStatus(e)).Append("</color>\n").Append(first).Append('\n');
                // Main's word on it, when there is one.
                if (!string.IsNullOrEmpty(e.note)) sb.Append("<color=#a9a49a><i>").Append(e.note.Length > 60 ? e.note.Substring(0, 59) + "…" : e.note).Append("</i></color>\n");
                sb.Append('\n');
            }
        if (list.Exists(e => !e.dismissed && (e.status == DevReportStore.Status.Done || e.status == DevReportStore.Status.Failed)))
            sb.Append("<color=#8a857c>Middle-click here to clear the done ones.</color>");
        requests.text = sb.Length > 0 ? sb.ToString() : "No requests yet.";
        if (counts != null) counts.text = $"{active} active / {queued} waiting";
    }

    // ── Markup ─────────────────────────────────────────────────────────

    void OpenMarkup()
    {
        if (markupPanel == null || markup == null) return;
        markupPanel.SetActive(true);
        var surface = markupImage.GetComponent<DevPromptDrawSurface>();
        if (surface != null) surface.Markup = markup;
        if (markupAspect != null) markupAspect.aspectRatio = markup.Width / (float)markup.Height;
        PickColour(markup.Colour);
        ShowMarkup();
    }

    void CloseMarkup()
    {
        if (markupPanel == null || !markupPanel.activeSelf) return;
        markupPanel.SetActive(false);
        ShowMarkup();
        message.ActivateInputField();
    }

    void PickColour(int c)
    {
        if (markup == null) return;
        markup.Colour = c;
        for (int i = 0; i < paletteButtons.Length; i++)
            if (paletteButtons[i] != null && paletteButtons[i].TryGetComponent<Outline>(out var o)) o.enabled = i == c;
    }

    void ShowMarkup()
    {
        if (markup == null) return;
        var tex = markup.Composed;
        if (markupImage != null) markupImage.texture = tex;
        if (thumbnail != null) thumbnail.texture = tex;
        if (undoButton != null) undoButton.interactable = markup.CanUndo;
        if (redoButton != null) redoButton.interactable = markup.CanRedo;
        if (clearButton != null) clearButton.interactable = markup.HasMarks;
    }

    // ── For tests (an eval): open, type, send, close without a keyboard ──
    public static DevPrompt Instance => FindAnyObjectByType<DevPrompt>();
    public void TestOpen() => StartCoroutine(OpenWithCapture());
    public void TestType(string text) => message.text = text;
    public void TestSend() { submitFrame = -10; Send(); }
    public void TestClose() => Close();
    public void TestMark(Vector2Int from, Vector2Int to, int colour)
    {
        if (markup == null) return;
        markup.Colour = colour;
        markup.BeginStroke(from); markup.ContinueStroke(to); markup.EndStroke();
        ShowMarkup();
    }
    public DevPromptMarkup TestMarkup => markup;
    public void TestDismissDone() { foreach (var e in DevReportStore.List(500)) if (e.status == DevReportStore.Status.Done) DevReportStore.Dismiss(e.id); RefreshRequests(); }
}
#endif
