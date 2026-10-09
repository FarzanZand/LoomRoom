using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Picking a class at the table: the Dungeon Master sets one pewter figure per unlocked class in front
// of the seat. Hovering a figure with the mouse (or stepping with left/right) lifts it and shows its card; clicking
// it (or Submit) takes it; Cancel stands up. TableManager runs Choose while the player is seated.
public class ClassFigures : MonoBehaviour
{
    [Tooltip("Where the figures stand on the table, in class order. Each faces the seat.")]
    public Transform[] spots = Array.Empty<Transform>();
    [Tooltip("Where a figure set out on its own stands (the intro's practice figure). Empty: the middle of the spots.")]
    public Transform soloSpot;
    [Tooltip("Same pewter as the memorial table.")]
    public Material figureMaterial;
    public float figureScale = 3f;
    [Tooltip("How far the highlighted figure rises.")]
    public float lift = 1.5f;
    [Min(.01f)] public float liftSpeed = 12f;
    [Min(0), Tooltip("Seconds between figures being set down.")]
    public float placeInterval = .3f;
    [Tooltip("Scene-authored class card, filled for the highlighted figure.")]
    public AdventureClassSelectionUI card;
    [Tooltip("The card with its hint lines: shown only while picking.")]
    public GameObject cardPanel;
    [Tooltip("The paper card that lifts off the table while picking (holds Card). Empty: Card Panel is shown on the HUD.")]
    public FloatingCard floatingCard;
    [Tooltip("Said once the figures are down.")]
    public DungeonMaster.Line prompt;
    [Tooltip("Said instead when a saved run is waiting.")]
    public DungeonMaster.Line resumePrompt;
    [Tooltip("Said instead after the character sheet, naming the class of the highest roll. {class} is its name.")]
    public DungeonMaster.Line suggestedPrompt = new() { text = "With those numbers, I'd take the {class}. Your choice.", inPerson = true };
    [Tooltip("The card's hint line, rewritten for the saved run's figure.")]
    public TMPro.TMP_Text how;
    [Tooltip("The card's Pick button: takes the highlighted figure.")]
    public UnityEngine.UI.Button pick;
    AdventurerClass saved; string savedDetail, howDefault;
    // The figure taken plays the saved run.
    public bool ResumeChosen { get; private set; }
    [Tooltip("Said when a figure is taken.")]
    public DungeonMaster.Line taken;

    readonly List<ClassFigure> figures = new();
    ClassFigure highlighted, chosen, lastUnder;
    bool choosing, cancelled, canCancel = true;
    AdventurerProgress progress;

    public bool HasFigures => spots.Length > 0;

    // Middle of the spots, for turning the view to the figures.
    public Vector3 Centre
    {
        get
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var s in spots) if (s != null) { sum += s.position; n++; }
            return n > 0 ? sum / n : transform.position;
        }
    }

    // What to look at: the lone figure's spot or the middle of the row.
    public Vector3 FocusPoint(bool solo) => solo && soloSpot != null ? soloSpot.position : Centre;

    void Awake()
    {
        ShowCard(false);
        if (pick != null) pick.onClick.AddListener(() => Take(highlighted));
    }

    bool showCard = true;

    // showCard false (the tabletop intro, where the character sheet is the card): nothing is shown.
    void ShowCard(bool show)
    {
        if (!showCard) show = false;
        if (floatingCard != null) { if (show) floatingCard.Show(); else floatingCard.Hide(); }
        if (cardPanel != null) cardPanel.SetActive(show);
        if (card != null) card.gameObject.SetActive(show);
    }

    // Sets the figures down, waits for a choice and clears them. done gets null when cancelled.
    // savedClass: a run saved on quit, played by that figure. It is picked first and the card says so.
    // only: just this figure (the intro's practice board), with promptOverride said instead of Prompt.
    // canCancel false: Esc does nothing. offered: only these figures, in this order (the character sheet's
    // two best rolls); the first is lifted and the Dungeon Master recommends it.
    public IEnumerator Choose(Action<AdventurerClass> done, AdventurerClass savedClass = null, string savedDetail = null,
        AdventurerClass only = null, DungeonMaster.Line promptOverride = null, bool canCancel = true, bool showCard = true,
        IReadOnlyList<AdventurerClass> offered = null)
    {
        this.showCard = showCard;
        this.canCancel = canCancel;
        saved = savedClass; this.savedDetail = savedDetail;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (progress == null || progress.rules == null) { done?.Invoke(null); yield break; }

        chosen = null; cancelled = false; highlighted = null;
        if (only != null || saved != null) offered = null;
        var classes = new List<AdventurerClass>();
        foreach (var c in offered ?? progress.rules.classes)
            if (c != null && (only != null ? c == only : c.Unlocked) && c.miniature != null && !classes.Contains(c) && classes.Count < spots.Length) classes.Add(c);
        var suggested = offered != null && classes.Count > 0 ? classes[0] : null;
        // Offered figures stand in the middle of the row: the ends are under the sheet and the card.
        int spot = offered != null ? (spots.Length - classes.Count) / 2 : 0;
        foreach (var c in classes)
        {
            // A figure on its own stands in the middle of the spots.
            if (only != null) { figures.Add(soloSpot != null ? Place(c, soloSpot.position, soloSpot.rotation) : Place(c, Centre, spots[spots.Length / 2].rotation)); spot++; }
            else figures.Add(Place(c, spots[spot].position, spots[spot++].rotation));
            if (UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
            yield return new WaitForSecondsRealtime(placeInterval);
        }
        if (figures.Count == 0) { done?.Invoke(null); yield break; }
        DungeonMaster.Say(promptOverride != null && !promptOverride.IsEmpty ? promptOverride : saved != null && resumePrompt != null && !resumePrompt.IsEmpty ? resumePrompt
            : suggested != null && suggestedPrompt != null && !suggestedPrompt.IsEmpty ? suggestedPrompt.With(suggestedPrompt.text.Replace("{class}", suggested.displayName)) : prompt);

        Highlight(figures.Find(f => f.adventurer == (saved != null ? saved : suggested != null ? suggested : progress.selectedClass)) ?? figures[0]);
        if (card != null) card.Single = only != null;
        ShowCard(true);
        // Without the card there is no hint on it: say it in the feed.
        if (!showCard) MessageLog.Post(only != null ? "Click the figure to take it." : "Click a figure to take it.", MessageKind.Info);
        if (card != null) card.Refresh();
        choosing = true;
        if (InputManager.HasInstance)
        {
            InputManager.Instance.NavigatePressed += OnNavigate;
            InputManager.Instance.SubmitPressed += OnSubmit;
            InputManager.Instance.CancelPressed += OnCancel;
        }
        while (chosen == null && !cancelled)
        {
            // The card's own arrows change the class; follow them.
            if (progress.selectedClass != null && (highlighted == null || highlighted.adventurer != progress.selectedClass))
            {
                var f = figures.Find(x => x.adventurer == progress.selectedClass);
                if (f != null) Highlight(f);
            }
            yield return null;
        }
        choosing = false;
        if (InputManager.HasInstance)
        {
            InputManager.Instance.NavigatePressed -= OnNavigate;
            InputManager.Instance.SubmitPressed -= OnSubmit;
            InputManager.Instance.CancelPressed -= OnCancel;
        }
        ShowCard(false);
        var result = chosen != null ? chosen.adventurer : null;
        ResumeChosen = result != null && result == saved;
        if (how != null && howDefault != null) how.text = howDefault;
        if (result != null)
        {
            DungeonMaster.Say(taken);
            // The rest go back in the box; the taken one stays a moment.
            foreach (var f in figures) if (f != chosen) Destroy(f.gameObject);
            yield return new WaitForSecondsRealtime(1.2f);
        }
        Clear();
        done?.Invoke(result);
    }

    public void Clear()
    {
        foreach (var f in figures) if (f != null) Destroy(f.gameObject);
        figures.Clear();
        highlighted = chosen = null;
    }

    ClassFigure Place(AdventurerClass c, Vector3 position, Quaternion rotation)
    {
        var root = new GameObject(c.displayName + " figure");
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(position, rotation);
        var model = Instantiate(c.miniature, root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * figureScale;
        foreach (var anim in model.GetComponentsInChildren<Animator>()) anim.enabled = false;
        var bounds = new Bounds(position, Vector3.zero);
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(r.bounds);
            if (figureMaterial == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = figureMaterial;
            r.sharedMaterials = mats;
        }
        // A generous box so the small figure is easy to hover.
        var box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = root.transform.InverseTransformPoint(bounds.center);
        box.size = Vector3.Scale(bounds.size, new Vector3(1.6f, 1.2f, 1.6f));
        var figure = root.AddComponent<ClassFigure>();
        figure.owner = this; figure.adventurer = c;
        return figure;
    }

    public void Highlight(ClassFigure figure)
    {
        if (figure == null || figure == highlighted) return;
        highlighted = figure;
        if (progress != null && !progress.InRun && progress.selectedClass != figure.adventurer) progress.SelectClass(figure.adventurer);
        // The paper card turns over to the new class; the HUD card just changes.
        if (card != null && card.isActiveAndEnabled)
        {
            if (floatingCard != null && choosing) floatingCard.Flip(card.Refresh);
            else card.Refresh();
        }
        if (how != null)
        {
            howDefault ??= how.text;
            how.text = saved != null && figure.adventurer == saved ? $"Click to continue your run: {savedDetail}." : saved != null ? "Click to start a new run. Your saved run is lost. Esc to step back."
                : !canCancel ? "Click a figure to take it." : howDefault;
        }
        if (choosing && UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
    }

    public void Take(ClassFigure figure)
    {
        if (!choosing || figure == null) return;
        Highlight(figure);
        // The card's arrows can leave the selection on a class that has no figure out.
        if (progress != null && !progress.InRun && progress.selectedClass != figure.adventurer) progress.SelectClass(figure.adventurer);
        chosen = figure;
    }

    void OnNavigate()
    {
        if (!choosing || figures.Count == 0 || !InputManager.HasInstance) return;
        float x = InputManager.Instance.Actions.UI.Navigate.ReadValue<Vector2>().x;
        if (Mathf.Abs(x) < .5f) return;
        int i = Mathf.Max(0, figures.IndexOf(highlighted));
        Highlight(figures[(i + (x > 0 ? 1 : -1) + figures.Count) % figures.Count]);
    }

    void OnSubmit() { if (choosing) Take(highlighted); }
    void OnCancel() { if (choosing && canCancel) cancelled = true; }

    readonly RaycastHit[] hits = new RaycastHit[32];

    // The figure under the mouse. Only figures count: the table's own triggers and props are ignored.
    ClassFigure UnderPointer()
    {
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam == null || !InputManager.HasInstance) return null;
        var ray = cam.ScreenPointToRay(InputManager.Instance.PointerPosition);
        int n = Physics.RaycastNonAlloc(ray, hits, 400f, ~0, QueryTriggerInteraction.Collide);
        ClassFigure best = null; float bestDistance = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var f = hits[i].collider.GetComponent<ClassFigure>();
            if (f != null && f.owner == this && hits[i].distance < bestDistance) { best = f; bestDistance = hits[i].distance; }
        }
        return best;
    }

    void Update()
    {
        if (choosing && InputManager.HasInstance)
        {
            bool overUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            var under = overUI ? null : UnderPointer();
            if (under != null && under != lastUnder) Highlight(under);
            lastUnder = under;
            if (under != null && InputManager.Instance.PointerClicked) Take(under);
            // A figure on its own is taken by a click anywhere off the card.
            else if (!overUI && figures.Count == 1 && InputManager.Instance.PointerClicked) Take(figures[0]);
        }

        // Highlighted figure rises a little; the rest settle. Kept off the Animator: these are props.
        foreach (var f in figures)
        {
            if (f == null || f.transform.childCount == 0) continue;
            var model = f.transform.GetChild(0);
            float target = f == highlighted || f == chosen ? lift : 0f;
            var p = model.localPosition;
            p.y = Mathf.Lerp(p.y, target / Mathf.Max(.001f, f.transform.lossyScale.y), 1f - Mathf.Exp(-liftSpeed * Time.unscaledDeltaTime));
            model.localPosition = p;
        }
    }

    void OnDisable()
    {
        if (!InputManager.HasInstance) return;
        InputManager.Instance.NavigatePressed -= OnNavigate;
        InputManager.Instance.SubmitPressed -= OnSubmit;
        InputManager.Instance.CancelPressed -= OnCancel;
    }
}
