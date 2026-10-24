using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The house rules read up close: the sheet from the door on screen, every pinned rule numbered and in full. Esc, a click
// or the button puts it down. Authored in the scene (HUD canvas > House rules, built by RuleSetup.Build()); code only
// fills it.
public class HouseRulesUI : MonoBehaviour
{
    public GameObject root;
    public TMP_Text list;
    public Button close;

    bool open;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        if (close != null) close.onClick.AddListener(Close);
    }
    void Start() { if (InputManager.HasInstance) InputManager.Instance.CancelPressed += Close; }
    void OnDestroy() { if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= Close; }
    // Switched off while open (a scene unload): its Menu state is popped, or the game would stay in a menu with nothing shown.
    // (Objects can't be switched off while this one is being switched off, so only the state is let go.)
    void OnDisable()
    {
        if (!open) return;
        open = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    public void Open()
    {
        if (root == null || open || !RuleManager.HasInstance) return;
        open = true;
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        var sb = new System.Text.StringBuilder();
        var rm = RuleManager.Instance;
        // A rule that runs onto a second line keeps its text under the text, not under its number.
        foreach (var rule in rm.PinnedRules()) sb.Append(rm.Number(rule)).Append(".<indent=1.6em>").Append(rule.text).Append("</indent>\n\n");
        if (list != null) list.text = sb.ToString().TrimEnd();
        root.SetActive(true);
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (root != null) root.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void Update()
    {
        // A click anywhere (but the button) puts it down too.
        if (open && InputManager.HasInstance && InputManager.Instance.PointerClicked && Time.frameCount > openedFrame + 1) Close();
        if (!open) openedFrame = Time.frameCount;
    }
    int openedFrame;
}
