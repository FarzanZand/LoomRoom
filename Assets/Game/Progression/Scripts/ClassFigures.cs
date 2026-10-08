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
    [Tooltip("Said once the figures are down.")]
    public DMLine prompt;
    [Tooltip("Said instead when a saved run is waiting.")]
    public DMLine resumePrompt;
    [Tooltip("The card's hint line, rewritten for the saved run's figure.")]
    public TMPro.TMP_Text how;
    AdventurerClass saved; string savedDetail, howDefault;
    // The figure taken plays the saved run.
    public bool ResumeChosen { get; private set; }
    [Tooltip("Said when a figure is taken.")]
    public DMLine taken;

    readonly List<ClassFigure> figures = new();
    ClassFigure highlighted, chosen, lastUnder;
    bool choosing, cancelled;
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

    void Awake()
    {
        ShowCard(false);
    }

    void ShowCard(bool show)
    {
        if (cardPanel != null) cardPanel.SetActive(show);
        if (card != null) card.gameObject.SetActive(show);
    }

    // Sets the figures down, waits for a choice and clears them. done gets null when cancelled.
    // savedClass: a run saved on quit, played by that figure. It is picked first and the card says so.
    public IEnumerator Choose(Action<AdventurerClass> done, AdventurerClass savedClass = null, string savedDetail = null)
    {
        saved = savedClass; this.savedDetail = savedDetail;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (progress == null || progress.rules == null) { done?.Invoke(null); yield break; }

        chosen = null; cancelled = false; highlighted = null;
        int spot = 0;
        foreach (var c in progress.rules.classes)
        {
            if (c == null || !c.Unlocked || c.miniature == null || spot >= spots.Length) continue;
            figures.Add(Place(c, spots[spot++]));
            if (UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
            yield return new WaitForSecondsRealtime(placeInterval);
        }
        if (figures.Count == 0) { done?.Invoke(null); yield break; }
        DungeonMaster.Say(saved != null && resumePrompt != null ? resumePrompt : prompt);

        Highlight(figures.Find(f => f.adventurer == (saved != null ? saved : progress.selectedClass)) ?? figures[0]);
        ShowCard(true);
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

    ClassFigure Place(AdventurerClass c, Transform at)
    {
        var root = new GameObject(c.displayName + " figure");
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(at.position, at.rotation);
        var model = Instantiate(c.miniature, root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * figureScale;
        foreach (var anim in model.GetComponentsInChildren<Animator>()) anim.enabled = false;
        var bounds = new Bounds(at.position, Vector3.zero);
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
        if (card != null && card.isActiveAndEnabled) card.Refresh();
        if (how != null)
        {
            howDefault ??= how.text;
            how.text = saved != null && figure.adventurer == saved ? $"Click to continue your run: {savedDetail}." : saved != null ? "Click to start a new run. Your saved run is lost. Esc to step back." : howDefault;
        }
        if (choosing && UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
    }

    public void Take(ClassFigure figure)
    {
        if (!choosing || figure == null) return;
        Highlight(figure);
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
    void OnCancel() { if (choosing) cancelled = true; }

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
