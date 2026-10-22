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

    public void Open()
    {
        if (root == null || open || !RuleManager.HasInstance) return;
        open = true;
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        var sb = new System.Text.StringBuilder();
        var rm = RuleManager.Instance;
        foreach (var rule in rm.PinnedRules()) sb.Append(rm.Number(rule)).Append(".  ").Append(rule.text).Append("\n\n");
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
