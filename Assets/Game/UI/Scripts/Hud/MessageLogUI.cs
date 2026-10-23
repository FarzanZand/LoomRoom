using TMPro;
using UnityEngine;

// Draws the newest MessageLog lines into authored text rows (oldest at the top). Lines
// fade after a few seconds of gameplay. Hidden behind menus and the inventory, whose
// panels share this corner of the screen.
public class MessageLogUI : MonoBehaviour
{
    [Tooltip("Authored rows, top to bottom. The bottom row shows the newest message.")]
    [SerializeField] TMP_Text[] lines;
    [SerializeField] CanvasGroup group;
    [SerializeField, Min(.5f)] float visibleSeconds = 8f;
    [SerializeField, Min(.05f)] float fadeSeconds = 1.5f;
    [Tooltip("Only while the table player is active.")]
    [SerializeField] bool tableOnly = true;

    void OnEnable()
    {
        if (MessageLog.HasInstance) MessageLog.Instance.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (MessageLog.HasInstance) MessageLog.Instance.Changed -= Refresh;
    }

    void Start()
    {
        // MessageLog may wake after this; subscribe once it exists.
        if (MessageLog.HasInstance) { MessageLog.Instance.Changed -= Refresh; MessageLog.Instance.Changed += Refresh; }
        Refresh();
    }

    void Refresh()
    {
        if (lines == null || !MessageLog.HasInstance) return;
        var history = MessageLog.Instance.History;
        for (int i = 0; i < lines.Length; i++)
        {
            int index = history.Count - lines.Length + i;
            var line = lines[i];
            if (line == null) continue;
            if (index < 0) { line.text = ""; continue; }
            var entry = history[index];
            line.text = MessageLog.Format(entry);
            line.color = MessageLog.Instance.ColorOf(entry.kind);
        }
    }

    void Update()
    {
        if (lines == null || !MessageLog.HasInstance) return;
        var state = GameManager.HasInstance ? GameManager.Instance.State : GameState.Explore;
        bool table = !tableOnly || (PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table);
        bool show = table && (state == GameState.Explore || state == GameState.Dead);
        if (group != null) group.alpha = show ? 1f : 0f;
        if (!show) return;
        var history = MessageLog.Instance.History;
        float now = Time.unscaledTime;
        for (int i = 0; i < lines.Length; i++)
        {
            int index = history.Count - lines.Length + i;
            var line = lines[i];
            if (line == null || index < 0) continue;
            float age = now - history[index].time;
            float alpha = Mathf.Clamp01((visibleSeconds + fadeSeconds - age) / fadeSeconds);
            var c = line.color; c.a = alpha; line.color = c;
        }
    }
}
