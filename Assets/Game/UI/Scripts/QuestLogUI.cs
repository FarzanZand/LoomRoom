using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The quest log on the HUD: the open quests (ProgressionManager > Quests), one line each under a title. A quest that is
// done turns green and crossed out, stays a moment, then fades; the log hides when nothing is open and while the inventory
// is open. Authored in the scene
// (Tools > LoomRoom > UI > Build Quest Log); this binds the title and clones the row template.
public class QuestLogUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField, Tooltip("One line, switched off; cloned per quest.")] TMP_Text rowTemplate;
    [SerializeField] Color openColour = Color.white;
    [SerializeField] Color doneColour = new(.55f, .9f, .5f);
    [SerializeField, Min(0)] float doneSeconds = 3f;
    [SerializeField, Min(.05f)] float fadeSeconds = .6f;

    readonly Dictionary<string, TMP_Text> rows = new();
    readonly Dictionary<string, float> doneAt = new();
    ProgressionManager progression;

    void OnEnable()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        Connect();
    }

    void OnDisable()
    {
        if (progression != null) progression.FlagChanged -= FlagChanged;
        progression = null;
    }

    // The HUD can wake before the managers do: connect as soon as ProgressionManager is there.
    void Connect()
    {
        if (progression != null || !ProgressionManager.HasInstance) return;
        progression = ProgressionManager.Instance;
        progression.FlagChanged += FlagChanged;
        Rebuild();
    }

    void FlagChanged(string key, int value)
    {
        if (!key.StartsWith(ProgressionManager.QuestPrefix)) return;
        string id = key.Substring(ProgressionManager.QuestPrefix.Length);
        if (value == 2 && rows.ContainsKey(id)) doneAt[id] = Time.unscaledTime;
        Rebuild();
    }

    void Rebuild()
    {
        if (progression == null || rowTemplate == null) return;
        foreach (var q in progression.quests)
        {
            if (q == null || string.IsNullOrEmpty(q.id)) continue;
            int state = progression.QuestState(q.id);
            bool show = state == 1 || doneAt.ContainsKey(q.id);
            if (!show) { if (rows.TryGetValue(q.id, out var gone)) { Destroy(gone.gameObject); rows.Remove(q.id); } continue; }
            if (!rows.TryGetValue(q.id, out var row))
            {
                row = Instantiate(rowTemplate, rowTemplate.transform.parent);
                row.gameObject.SetActive(true);
                rows[q.id] = row;
            }
            string text = progression.QuestText(q);
            row.text = state == 2 ? $"<s>{text}</s>" : text;
            row.color = state == 2 ? doneColour : openColour;
        }
    }

    void Update()
    {
        if (progression == null) Connect();
        if (doneAt.Count > 0)
        {
            var finished = new List<string>();
            foreach (var pair in doneAt)
            {
                float t = Time.unscaledTime - pair.Value - doneSeconds;
                if (rows.TryGetValue(pair.Key, out var row)) row.alpha = 1f - Mathf.Clamp01(t / fadeSeconds);
                if (t >= fadeSeconds) finished.Add(pair.Key);
            }
            foreach (var id in finished) doneAt.Remove(id);
            if (finished.Count > 0) Rebuild();
        }
        if (group != null)
        {
            // Out of the way while the inventory is open: it sits where the character panel's title is.
            bool inventory = GameManager.HasInstance && GameManager.Instance.State == GameState.Inventory;
            float target = rows.Count > 0 && !inventory ? 1f : 0f;
            group.alpha = inventory ? 0f : Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }
    }
}
