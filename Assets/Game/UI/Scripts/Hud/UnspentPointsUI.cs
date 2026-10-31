using TMPro;
using UnityEngine;

// A yellow "+" at the bottom left of the HUD, over the health bar, while the table player has attribute points to spend
// in a run (AdventurerProgress.UnspentPoints; each character level gives one, spent on the character panel's
// Attributes). It breathes gently so it is seen without nagging. Built on the HUD canvas by UnspentPointsSetup.Build().
public class UnspentPointsUI : MonoBehaviour
{
    public TMP_Text label;
    [Range(0f, 1f)] public float breathe = .25f;
    [Min(.1f)] public float breatheSeconds = 1.6f;

    CanvasGroup group;
    AdventurerProgress progress;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group != null) group.alpha = 0f;
    }

    void Update()
    {
        var players = PlayerManager.HasInstance ? PlayerManager.Instance : null;
        if (progress == null && players != null)
        {
            var table = players.GetPlayer(PlayerKind.Table);
            if (table != null) progress = table.GetComponent<AdventurerProgress>();
        }
        // Only while exploring: in a menu (the inventory shows its own +, the spawn window) it would sit on top.
        bool show = players != null && players.ActiveKind == PlayerKind.Table && progress != null && progress.InRun && progress.UnspentPoints > 0
            && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        if (label != null && show) label.text = progress.UnspentPoints > 1 ? "+" + progress.UnspentPoints : "+";
        if (group == null) return;
        float target = show ? 1f - breathe * .5f * (1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / breatheSeconds)) : 0f;
        group.alpha = show ? target : Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime * 4f);
    }
}
