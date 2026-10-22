using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Choosing a hero over the campaign map (clicking the figure): a card per unlocked class in a column at the left of
// the screen and the chosen one's details at the right (the class card's content, AdventureClassSelectionUI), while the
// figure on the map changes to match. The arrow keys, Previous/Next or a click choose; Enter, Choose or a second click on the
// chosen card takes it; Esc or Back leaves the class as it was. Authored in the scene (CampaignSetup builds it).
public class CampaignHeroSelectUI : MonoBehaviour
{
    public CanvasGroup group;
    [Tooltip("The cards are made from this one (kept switched off) under the row.")]
    public CampaignHeroCard cardTemplate;
    public RectTransform cardRow;
    public AdventureClassSelectionUI details;
    public Button choose, back;

    public bool Open { get; private set; }
    // The class shown changed (the map's figure follows it).
    public event Action<AdventurerClass> Shown;

    readonly List<CampaignHeroCard> cards = new();
    AdventurerProgress progress;
    AdventurerClass shown;
    bool taken, cancelled;

    void Awake()
    {
        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
        if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; }
        choose?.onClick.AddListener(() => taken = true);
        back?.onClick.AddListener(() => cancelled = true);
        // The buttons never keep the selection (a click on Next then Enter would press Next again as the class is taken,
        // and the arrow keys would wander over them while they choose the class).
        foreach (var b in GetComponentsInChildren<Button>(true))
        {
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            b.onClick.AddListener(() => { if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null); });
        }
    }

    // Returns the class taken, or null when cancelled (the class is then put back as it was).
    public IEnumerator Choose(AdventurerProgress who, IReadOnlyList<AdventurerClass> classes, Action<AdventurerClass> done)
    {
        progress = who;
        var before = progress != null ? progress.selectedClass : null;
        if (progress == null || classes == null || classes.Count == 0) { done?.Invoke(null); yield break; }
        foreach (var c in cards) if (c != null) Destroy(c.gameObject);
        cards.Clear();
        foreach (var c in classes)
        {
            var card = Instantiate(cardTemplate, cardRow);
            card.gameObject.SetActive(true);
            card.Fill(c);
            card.Clicked = Clicked;
            cards.Add(card);
        }
        details.Choices = classes;
        shown = null;
        taken = cancelled = false;
        Open = true;
        Sync();
        void OnNavigate()
        {
            // Down the column (or Tab) is the next one, up the one before.
            details.Cycle(InputManager.Instance.NavigateStep);
        }
        void OnSubmit() => taken = true;
        void OnCancel() => cancelled = true;
        var input = InputManager.HasInstance ? InputManager.Instance : null;
        if (input != null) { input.NavigatePressed += OnNavigate; input.SubmitPressed += OnSubmit; input.CancelPressed += OnCancel; }
        try
        {
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / .18f) { Fade(t); Sync(); yield return null; }
            Fade(1f);
            while (!taken && !cancelled) { Sync(); yield return null; }
        }
        finally
        {
            if (input != null) { input.NavigatePressed -= OnNavigate; input.SubmitPressed -= OnSubmit; input.CancelPressed -= OnCancel; }
        }
        var result = taken ? progress.selectedClass : null;
        if (result == null && before != null) progress.SelectClass(before);
        Sync();
        for (float t = 1; t > 0; t -= Time.unscaledDeltaTime / .15f) { Fade(t); yield return null; }
        Fade(0f);
        Open = false;
        done?.Invoke(result);
    }

    void Clicked(CampaignHeroCard card)
    {
        if (card.Class == progress.selectedClass) { taken = true; return; }
        progress.SelectClass(card.Class);
        UIFeedbackSettings.Shared?.Hover();
    }

    // The cards and details follow the selected class, however it was changed (a card, the arrows, the keys).
    void Sync()
    {
        var c = progress != null ? progress.selectedClass : null;
        if (c == shown) return;
        shown = c;
        foreach (var card in cards) card.SetChosen(card.Class == c);
        details.Refresh();
        Shown?.Invoke(c);
    }

    void Fade(float k)
    {
        if (group == null) return;
        group.alpha = Mathf.Clamp01(k);
        group.blocksRaycasts = group.interactable = k > .5f;
    }
}
