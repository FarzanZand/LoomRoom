using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The name of loot lying close by, floating over it in the beam's colour ("Greaves" over the boots).
// The label is authored in the scene (template, inactive); this clones it per nearby item and places it. Labels
// never overlap each other (a later one, farther away, moves up past a nearer one) and stay above the hotbar.
public class LootLabelsUI : MonoBehaviour
{
    [SerializeField] TMP_Text template;
    [SerializeField, Min(.5f)] float range = 4.5f;
    [SerializeField, Min(1)] int maxLabels = 6;
    [SerializeField, Tooltip("Metres above the item the name floats.")] float height = .55f;
    [SerializeField, Range(0f, 1f), Tooltip("How far the beam's colour is lightened towards white, so dark colours (Rare) still read.")]
    float lighten = .55f;
    [SerializeField, Tooltip("Labels stay above this (the hotbar).")] RectTransform keepAbove;
    [SerializeField, Min(0), Tooltip("Space between labels and above the hotbar (canvas units).")] float gap = 4f;

    readonly List<TMP_Text> labels = new();
    // Nearby beams with their squared distance, sorted nearest first by a comparer that captures nothing.
    readonly List<(LootBeam beam, float distance)> near = new();
    static readonly System.Comparison<(LootBeam beam, float distance)> Nearest = (a, b) => a.distance.CompareTo(b.distance);
    // Each beam's label text and its size, made again only when the item or its count changes.
    class Cached { public ItemData item; public int count; public string text; public Vector2 size; }
    readonly Dictionary<LootBeam, Cached> cache = new();
    readonly List<LootBeam> stale = new();
    RectTransform canvas;
    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly List<Rect> placed = new();
    readonly Vector3[] corners = new Vector3[4];

    void Awake()
    {
        canvas = (RectTransform)GetComponentInParent<Canvas>().transform;
        if (template != null) template.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var camera = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        bool show = template != null && camera != null && player != null && player.kind == PlayerKind.Table && player.IsActive &&
            (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        near.Clear();
        // The item the crosshair is on is named by its own "Pick up" prompt: its label would lie over that.
        var aimed = show && player.Interact != null && player.Interact.Active != null ? player.Interact.Active.GetComponentInParent<WorldItem>() : null;
        if (show)
        {
            Vector3 from = player.transform.position;
            float rangeSqr = range * range;
            foreach (var beam in LootBeam.Active)
            {
                if (beam == null || beam.Item == null || beam.Item == aimed) continue;
                float distance = (beam.Base - from).sqrMagnitude;
                if (distance <= rangeSqr && Visible(camera, beam)) near.Add((beam, distance));
            }
        }
        near.Sort(Nearest);
        int shown = 0;
        placed.Clear();
        float floor = Floor();
        for (int i = 0; i < near.Count && shown < maxLabels; i++)
        {
            var beam = near[i].beam;
            Vector3 screen = camera.WorldToScreenPoint(beam.Base + Vector3.up * height);
            if (screen.z <= 0f) continue;
            var label = Label(shown++);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local);
            var cached = Text(beam, label);
            if (!ReferenceEquals(label.text, cached.text)) label.text = cached.text;
            var color = beam.Color; color.a = 1f;
            label.color = Color.Lerp(color, Color.white, lighten);
            ((RectTransform)label.transform).anchoredPosition = Place(cached.size, local, floor);
        }
        for (int i = shown; i < labels.Count; i++) if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
        if (cache.Count > 32 && Time.frameCount % 120 == 0) Prune();
    }

    // The beam's label text and size, kept until its item or count changes (the labels are all copies of one template).
    Cached Text(LootBeam beam, TMP_Text label)
    {
        var worldItem = beam.Item;
        if (!cache.TryGetValue(beam, out var cached)) cache[beam] = cached = new Cached();
        if (cached.text == null || cached.item != worldItem.Item || cached.count != worldItem.Count)
        {
            cached.item = worldItem.Item; cached.count = worldItem.Count;
            string name = cached.item != null ? cached.item.itemName : "";
            cached.text = cached.count > 1 ? $"{name} x{cached.count}" : name;
            cached.size = label.GetPreferredValues(cached.text);
        }
        return cached;
    }

    // Beams that are gone leave the cache.
    void Prune()
    {
        stale.Clear();
        foreach (var beam in cache.Keys) if (beam == null || !LootBeam.Active.Contains(beam)) stale.Add(beam);
        foreach (var beam in stale) cache.Remove(beam);
    }

    // Nearest first: each label goes where it projects, or up past the labels already placed that it would cover, and
    // never below the floor (the hotbar's top). Positions are the label's centre in the canvas (its anchor is the middle).
    Vector2 Place(Vector2 size, Vector2 at, float floor)
    {
        var rect = new Rect(at.x - size.x * .5f, at.y - size.y * .5f, size.x, size.y);
        if (rect.yMin < floor) rect.y = floor;
        for (bool moved = true; moved;)
        {
            moved = false;
            foreach (var other in placed)
                if (rect.xMin < other.xMax + gap && rect.xMax > other.xMin - gap && rect.yMin < other.yMax + gap && rect.yMax > other.yMin - gap)
                { rect.y = other.yMax + gap; moved = true; }
        }
        placed.Add(rect);
        return rect.center;
    }

    // The top of the hotbar in the canvas, plus the gap; far below the screen without one.
    float Floor()
    {
        if (keepAbove == null || !keepAbove.gameObject.activeInHierarchy) return float.MinValue;
        keepAbove.GetWorldCorners(corners);
        return canvas.InverseTransformPoint(corners[1]).y + gap;
    }

    TMP_Text Label(int index)
    {
        while (labels.Count <= index)
        {
            var label = Instantiate(template, template.transform.parent);
            label.name = "Loot label " + labels.Count;
            labels.Add(label);
        }
        if (!labels[index].gameObject.activeSelf) labels[index].gameObject.SetActive(true);
        return labels[index];
    }

    // Nothing solid between the eye and the item.
    bool Visible(Camera camera, LootBeam beam)
    {
        Vector3 from = camera.transform.position, to = beam.Base + Vector3.up * .15f, d = to - from;
        if (Vector3.Dot(camera.transform.forward, d) <= 0f) return false;
        int count = Physics.RaycastNonAlloc(from, d.normalized, hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var t = hits[i].transform;
            if (t.IsChildOf(beam.Item.transform) || t.GetComponentInParent<Player>() != null || t.GetComponentInParent<Character>() != null || hits[i].rigidbody != null) continue;
            return false;
        }
        return true;
    }
}
