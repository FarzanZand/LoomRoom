using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The text event screen over the campaign map: the event's picture fills the screen, the title and text sit in a panel
// at the right, and the choices are buttons along the bottom left (cloned from an authored template). A choice shows its
// outcome in the panel, then a Continue button closes it. Authored in the scene (CampaignSetup builds it); code only
// fills it.
public class CampaignEventUI : MonoBehaviour
{
    public CanvasGroup group;
    public Image art;
    public TMP_Text title, body, bank;
    [Tooltip("Disabled button cloned for each choice (a TMP label child; an optional child named Cost shows the price).")]
    public Button choiceTemplate;
    public RectTransform choiceRow;
    [Min(0)] public float fadeSeconds = .3f;
    [Tooltip("The way out when no choice can be afforded.")] public string leaveLabel = "Leave";

    public bool Open { get; private set; }
    readonly List<Button> spawned = new();
    [Min(0), Tooltip("Seconds clicks and Enter are ignored after the screen opens and after a choice (so a key pressed to skip a line, or a double click, can't pick or close by accident).")]
    public float inputGrace = .5f;
    float acceptAfter;

    void Awake()
    {
        if (choiceTemplate != null) choiceTemplate.gameObject.SetActive(false);
        Fade(0f);
    }

    // Shows the event until a choice is made and Continue pressed. bankNow reads the banked spoils; chose is called with
    // the choice taken (it applies the costs and gifts).
    public IEnumerator Play(CampaignEvent e, Func<int> bankNow, Action<CampaignEvent.Choice> chose)
    {
        Open = true;
        title.text = e.title;
        body.text = e.text;
        art.sprite = e.art; art.enabled = e.art != null;
        var fit = art.GetComponent<AspectRatioFitter>();
        if (fit != null && e.art != null && e.art.rect.height > 0) fit.aspectRatio = e.art.rect.width / e.art.rect.height;
        CampaignEvent.Choice picked = null; bool closed = false;
        void Fill()
        {
            Clear();
            int have = bankNow != null ? bankNow() : 0;
            if (bank != null) bank.text = $"Spoils in the bank: {have}";
            if (picked == null)
            {
                foreach (var c in e.choices)
                {
                    if (c == null) continue;
                    var choice = c;
                    bool afford = choice.spoilsCost <= have;
                    var b = Add(choice.label, choice.spoilsCost > 0 ? $"{choice.spoilsCost} spoils" : null, () => { picked = choice; chose?.Invoke(choice); Fill(); });
                    b.interactable = afford;
                }
            }
            else Add(e.continueLabel, null, () => closed = true);
            // Nothing can be afforded: a way out all the same (no soft lock).
            if (picked == null && !spawned.Exists(b => b != null && b.interactable)) Add(leaveLabel, null, () => { picked = new CampaignEvent.Choice(); closed = true; });
            // Nothing is pre-selected: Enter or Space pressed to skip a line must not buy anything.
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            acceptAfter = Time.unscaledTime + inputGrace;
        }
        if (e.choices == null || e.choices.Length == 0) { picked = new CampaignEvent.Choice(); Fill(); }
        else Fill();
        // Keys and gamepad: Navigate steps through the buttons (the first press selects one; nothing is selected before,
        // so a key pressed for something else can't buy), Submit presses it (the EventSystem), Cancel goes to the way
        // out (the last choice before one is made, Continue after).
        void OnNavigate()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var live = spawned.FindAll(b => b != null && b.interactable && b.gameObject.activeInHierarchy);
            if (es == null || live.Count == 0) return;
            int at = live.FindIndex(b => b.gameObject == es.currentSelectedGameObject);
            int step = InputManager.HasInstance ? InputManager.Instance.NavigateStep : 1;
            at = at < 0 ? 0 : (at + step + live.Count) % live.Count;
            es.SetSelectedGameObject(live[at].gameObject);
        }
        void OnCancel()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var last = spawned.FindLast(b => b != null && b.interactable && b.gameObject.activeInHierarchy);
            if (es != null && last != null) es.SetSelectedGameObject(last.gameObject);
        }
        var input = InputManager.HasInstance ? InputManager.Instance : null;
        if (input != null) { input.NavigatePressed += OnNavigate; input.CancelPressed += OnCancel; }
        try
        {
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime) { Fade(t / fadeSeconds); yield return null; }
            Fade(1f);
            while (picked == null) yield return null;
            body.text = string.IsNullOrWhiteSpace(picked.outcome) ? e.text : picked.outcome;
            while (!closed) yield return null;
        }
        finally { if (input != null) { input.NavigatePressed -= OnNavigate; input.CancelPressed -= OnCancel; } }
        for (float t = fadeSeconds; t > 0; t -= Time.unscaledDeltaTime) { Fade(t / fadeSeconds); yield return null; }
        Fade(0f);
        Clear();
        Open = false;
    }

    Button Add(string label, string cost, Action onClick)
    {
        var b = Instantiate(choiceTemplate, choiceRow);
        b.gameObject.SetActive(true);
        b.name = label;
        var texts = b.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts) t.text = t.name == "Cost" ? cost ?? "" : label;
        foreach (var t in texts) if (t.name == "Cost") t.gameObject.SetActive(!string.IsNullOrEmpty(cost));
        b.onClick.AddListener(() => { if (Time.unscaledTime < acceptAfter) return; UIFeedbackSettings.Shared?.Hover(); onClick(); });
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        // A button the keys selected shows it like a hovered one.
        var colors = b.colors; colors.selectedColor = colors.highlightedColor; b.colors = colors;
        spawned.Add(b);
        return b;
    }

    // Shut from outside (the map view stopped mid-event): nothing left on screen or catching clicks.
    public void Abort()
    {
        if (!Open) return;
        Clear();
        Fade(0f);
        Open = false;
    }

    void Clear()
    {
        foreach (var b in spawned) if (b != null) Destroy(b.gameObject);
        spawned.Clear();
    }

    void Fade(float k)
    {
        if (group == null) return;
        group.alpha = Mathf.Clamp01(k);
        group.blocksRaycasts = group.interactable = k > .5f;
    }
}
