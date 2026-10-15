using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// The trophy wall right of the table: one mount per skill milestone. A mounted trophy (ProgressionManager) hangs as its
// item's model; an empty mount shows a faint outline of the trophy to come. Interacting while carrying trophies hangs
// them all at once. Built by Tools > LoomRoom > Meta > Build Trophy Wall.
public class TrophyWall : MonoBehaviour, IInteractable
{
    [Serializable]
    public class Mount
    {
        public string trackId;
        public int value;
        [Tooltip("Where the trophy hangs; its forward points out of the wall.")] public Transform anchor;
        [NonSerialized] public GameObject model;
        [NonSerialized] public bool showingOutline;
        [NonSerialized] public ItemData shownItem;
    }

    public List<Mount> mounts = new();
    [Tooltip("The faint outline an empty mount shows (a transparent unlit material).")] public Material outlineMaterial;
    [Min(.1f), Tooltip("A trophy's longest side, in room units (the room is ~22 units to the metre).")] public float trophySize = 7f;
    [Tooltip("Long trophies (swords, maces, knives) hang at this angle.")] public float longTilt = 45f;
    [Tooltip("How far out from the mount (the plaque's front is at 0.7) a trophy's back rests.")] public float restOffset = 1f;
    [Tooltip("SFX Library key played as the trophies go up.")] public string hangKey = "trophyHang";
    public AudioClip hangClip;

    public string Prompt => "Hang the trophies";
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && Carried > 0;
    static int Carried => ProgressionManager.HasInstance ? ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried).Count : 0;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        var hung = ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried);
        ProgressionManager.Instance.MountCarried();
        Refresh();
        if (AudioManager.HasInstance && (AudioManager.Instance.PlayFirstSFX(transform.position, hangKey) == null) && hangClip != null)
            AudioManager.Instance.PlaySFX(hangClip, transform.position, .9f, .05f);
        // Each new trophy settles onto its bracket.
        foreach (var (t, m) in hung)
        {
            var mount = mounts.Find(x => x.trackId == t.id && x.value == m.value);
            if (mount?.model == null) continue;
            var tr = mount.model.transform;
            var scale = tr.localScale;
            tr.localScale = scale * .85f;
            tr.DOScale(scale, .35f).SetEase(Ease.OutBack).SetLink(mount.model);
        }
    }

    void Start()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.MetaChanged += Refresh;
        Refresh();
    }
    void OnDestroy() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.MetaChanged -= Refresh; }

    public void Refresh()
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        if (meta == null) return;
        foreach (var mount in mounts)
        {
            if (mount.anchor == null) continue;
            var track = meta.Track(mount.trackId);
            var milestone = track?.milestones?.Find(x => x != null && x.value == mount.value);
            var item = milestone?.trophy;
            bool hung = milestone != null && meta.Trophy(track, milestone) == ProgressionManager.TrophyState.Mounted;
            if (item == null || item.worldPrefab == null) { if (mount.model != null) Destroy(mount.model); mount.model = null; continue; }
            if (mount.model != null && mount.shownItem == item && mount.showingOutline == !hung) continue;
            if (mount.model != null) Destroy(mount.model);
            mount.model = Build(item, mount.anchor, !hung);
            mount.shownItem = item; mount.showingOutline = !hung;
        }
    }

    // The item's model, stripped to visuals, fitted to the trophy size and hung on the anchor.
    GameObject Build(ItemData item, Transform anchor, bool outline)
    {
        var go = Instantiate(item.worldPrefab, anchor, false);
        go.name = (outline ? "Outline " : "Trophy ") + item.itemName;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
        foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true)) p.gameObject.SetActive(false);
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        var plaque = anchor.parent != null ? anchor.parent.GetComponentInChildren<Renderer>() : null;
        Bounds? bounds = null;
        foreach (var r in renderers)
        {
            if (plaque != null) r.renderingLayerMask = plaque.renderingLayerMask;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (outline && outlineMaterial != null) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = outlineMaterial; r.sharedMaterials = mats; }
            if (bounds == null) bounds = r.bounds; else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
        }
        if (bounds == null) return go;
        // Fit it as it hangs: long things tilt first, then the longest side across the wall becomes Trophy Size and it is
        // centred on the anchor.
        // Hung flat: its thinnest side goes into the wall, so a ring shows its hole and a blade its flat.
        var size = bounds.Value.size;
        if (size.x < size.z && size.x <= size.y) { go.transform.localRotation = Quaternion.Euler(0, 90, 0) * go.transform.localRotation; bounds = Measure(renderers); }
        else if (size.y < size.z && size.y < size.x) { go.transform.localRotation = Quaternion.Euler(90, 0, 0) * go.transform.localRotation; bounds = Measure(renderers); }
        // The wall runs along x: what shows is the width (x) and the height (y).
        size = bounds.Value.size;
        float wide = size.x, tall = size.y;
        if (Mathf.Max(wide, tall) / Mathf.Max(.0001f, Mathf.Min(wide, tall)) > 2.5f)
        {
            go.transform.localRotation = Quaternion.Euler(0, 0, longTilt) * go.transform.localRotation;
            bounds = Measure(renderers);
        }
        // Thick things (rings) stand out of the wall toward the viewer and look bigger, so their depth counts too.
        size = bounds.Value.size;
        float across = Mathf.Max(size.x, size.y) + size.z;
        go.transform.localScale = Vector3.one * (trophySize / Mathf.Max(.0001f, across));
        go.transform.position += anchor.position - Measure(renderers).Value.center;
        // Its back rests just off the plaque.
        if (anchor.parent != null)
        {
            var b = Measure(renderers).Value;
            var outward = anchor.parent.forward;
            float depth = Mathf.Abs(outward.x) * b.extents.x + Mathf.Abs(outward.y) * b.extents.y + Mathf.Abs(outward.z) * b.extents.z;
            float back = Vector3.Dot(b.center - anchor.parent.position, outward) - depth;
            go.transform.position += outward * (restOffset - back);
        }
        return go;
    }

    static Bounds? Measure(Renderer[] renderers)
    {
        Bounds? bounds = null;
        foreach (var r in renderers) { if (bounds == null) bounds = r.bounds; else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; } }
        return bounds;
    }
}
