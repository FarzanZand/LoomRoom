using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// The trophy wall right of the table: one mount per skill milestone. A mounted trophy (ProgressionManager) hangs as its
// item's model; an empty mount shows a faint outline of the trophy to come. A carried trophy's plaque glows in the
// quest outline and is hung by using that plaque (TrophyMountSpot, one at a time; the user, 2026-11-11); with Hang At
// Plaques off, using the wall hangs them all at once as before. Built by Tools > LoomRoom > Meta > Build Trophy Wall.
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
        [NonSerialized] public QuestGlow glow;
    }

    public List<Mount> mounts = new();
    [Tooltip("The faint outline an empty mount shows (a transparent unlit material).")] public Material outlineMaterial;
    [Tooltip("Empty mounts show a faint outline of the trophy to come. Off (the user, 2026-10-30): an empty mount is just the board.")]
    public bool emptyOutlines;
    [Min(.1f), Tooltip("A trophy's longest side, in room units (the room is ~22 units to the metre).")] public float trophySize = 7f;
    [Tooltip("Long trophies (swords, maces, knives) hang at this angle.")] public float longTilt = 45f;
    [Tooltip("How far in front of the mount's face (its backing) a trophy's back rests.")] public float restGap = .3f;
    [Tooltip("Boots (worn boots, and any trophy named boots: the Winged boots) stand upright with their toes pointing out of the wall at the player (the user, 2026-11-02), instead of being laid flat like the rest.")]
    public bool bootsToesOut = true;
    [Tooltip("SFX Library key played as the trophies go up.")] public string hangKey = "trophyHang";
    public AudioClip hangClip;
    [Tooltip("UI Library key played for each trophy as it goes up, one after another.")] public string trophyKey = "equipItem";
    [Tooltip("A short burst on each new trophy (Items/Effects/Chest burst), scaled up to the room.")] public GameObject trophyEffect;
    [Min(.1f), Tooltip("The burst is made for the dungeon (1 unit to the metre); the room is ~22 units to the metre.")] public float trophyEffectScale = 20f;

    [Tooltip("Each carried trophy is hung at its own plaque, which glows in the quest outline until it is (the user, 2026-11-11). Off: using the wall anywhere hangs them all at once.")]
    public bool hangAtPlaques = true;
    [Min(0), Tooltip("How far the plaque's use box reaches out past its front and round its edges, in room units (about 22 to the metre).")]
    public float spotMargin = 1.5f;
    [Min(0), Tooltip("Width of a carried trophy's plaque outline (a flat ring just in front of it), in room units. The plaques stand .5 above their name plates: at the quest outline's usual .7 it ran over the plate (the user, 2026-11-11).")]
    public float plaqueGlowWidth = .3f;

    // One trophy or several (the user, 2026-11-11: it said "trophies" with one carried).
    public string Prompt => Carried == 1 ? "Hang the trophy" : "Hang the trophies";
    public bool CanInteract(Character who) => !hangAtPlaques && who is Player p && p.kind == PlayerKind.Room && Carried > 0;
    // Asked every frame the wall is looked at: counted without making a list.
    static int Carried => ProgressionManager.HasInstance ? ProgressionManager.Instance.TrophyCount(ProgressionManager.TrophyState.Carried) : 0;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        var hung = ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried);
        ProgressionManager.Instance.MountCarried();
        Refresh();
        PlayHang();
        // Each new trophy settles onto its bracket, with a sound and a burst, one after another.
        int n = 0;
        foreach (var (t, m) in hung) Settle(mounts.Find(x => x.trackId == t.id && x.value == m.value), n++ * .18f);
    }

    // ── One plaque at a time (TrophyMountSpot) ─────────────────────────

    (ProgressionManager.MetaTrack track, ProgressionManager.Milestone milestone) Find(Mount mount)
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null && mount != null ? meta.Track(mount.trackId) : null;
        return (track, track?.milestones?.Find(x => x != null && x.value == mount.value));
    }
    public ItemData TrophyOf(Mount mount) => Find(mount).milestone?.trophy;
    public bool IsCarried(Mount mount)
    {
        var (t, m) = Find(mount);
        return m != null && ProgressionManager.Instance.Trophy(t, m) == ProgressionManager.TrophyState.Carried;
    }

    // This one trophy goes up on its plaque, with the hang sound, the chime and the burst.
    public void Hang(Mount mount)
    {
        var (t, m) = Find(mount);
        if (m == null || !ProgressionManager.Instance.MountOne(t, m)) return;
        Refresh();
        PlayHang();
        Settle(mount, 0f);
    }

    void PlayHang()
    {
        if (AudioManager.HasInstance && (AudioManager.Instance.PlayFirstSFX(transform.position, hangKey) == null) && hangClip != null)
            AudioManager.Instance.PlaySFX(hangClip, transform.position, .9f, .05f);
    }

    // A new trophy settles onto its bracket after `delay`, with the chime and a burst.
    void Settle(Mount mount, float delay)
    {
        if (mount?.model == null) return;
        var tr = mount.model.transform;
        var scale = tr.localScale;
        tr.localScale = scale * .85f;
        tr.DOScale(scale, .35f).SetEase(Ease.OutBack).SetDelay(delay).SetLink(mount.model);
        var centre = Measure(mount.model.GetComponentsInChildren<Renderer>(true)) is Bounds b ? b.center : tr.position;
        var forward = mount.anchor != null && mount.anchor.parent != null ? mount.anchor.parent.forward : transform.forward;
        DOVirtual.DelayedCall(delay, () =>
        {
            if (AudioManager.HasInstance && !string.IsNullOrEmpty(trophyKey)) AudioManager.Instance.PlayUI(trophyKey);
            if (trophyEffect != null) Burst(centre + forward * restGap * 2f, forward);
        }).SetLink(gameObject);
    }

    // Each plaque gets a use box (a trigger round the plaque, reaching out past its front so the plaque itself never
    // blocks the look at it) with its TrophyMountSpot.
    void MakeSpots()
    {
        if (!hangAtPlaques) return;
        foreach (var mount in mounts)
        {
            var plaque = mount.anchor != null ? mount.anchor.parent : null;
            if (plaque == null) continue;
            Bounds? local = null;
            foreach (var r in plaque.GetComponentsInChildren<Renderer>(true))
            {
                if (mount.model != null && r.transform.IsChildOf(mount.model.transform)) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = plaque.InverseTransformPoint(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z));
                    if (local == null) local = new Bounds(corner, Vector3.zero); else { var l = local.Value; l.Encapsulate(corner); local = l; }
                }
            }
            if (local == null) continue;
            var spot = new GameObject("Hang spot");
            spot.layer = plaque.gameObject.layer;
            spot.transform.SetParent(plaque, false);
            spot.AddComponent<TrophyMountSpot>().Bind(this, mount);
            var box = spot.AddComponent<BoxCollider>();
            box.isTrigger = true;
            // Margins are in room units; the plaque may be scaled.
            var s = plaque.lossyScale;
            var margin = new Vector3(spotMargin / Mathf.Max(.0001f, Mathf.Abs(s.x)), spotMargin / Mathf.Max(.0001f, Mathf.Abs(s.y)), spotMargin / Mathf.Max(.0001f, Mathf.Abs(s.z)));
            var bounds = local.Value;
            bounds.Expand(new Vector3(margin.x, margin.y, 0f) * 2f);
            // Out past its front (the plaque's forward is out of the wall).
            bounds.center += new Vector3(0, 0, margin.z * .5f);
            bounds.size += new Vector3(0, 0, margin.z);
            box.center = bounds.center; box.size = bounds.size;
            spot.AddComponent<InteractableTrigger>();
            mount.glow = plaque.gameObject.AddComponent<QuestGlow>();
            mount.glow.width = plaqueGlowWidth;
            mount.glow.flatOn = plaque;   // a flat ring in front of the plaque: the shell round it hid its lower half from below
        }
    }

    // The burst sized for the room: every particle system scales with its transform.
    void Burst(Vector3 at, Vector3 forward)
    {
        var fx = Instantiate(trophyEffect, at, Quaternion.LookRotation(forward));
        fx.transform.localScale = trophyEffect.transform.localScale * trophyEffectScale;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        Destroy(fx, 4f);
    }

    void Start()
    {
        if (ProgressionManager.HasInstance)
        {
            ProgressionManager.Instance.MetaChanged += Refresh;
            ProgressionManager.Instance.FlagChanged += OnFlag;
        }
        if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.HostTrophiesChanged += Refresh;
        Refresh();
        MakeSpots();
        Refresh();
    }
    void OnDestroy()
    {
        if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.HostTrophiesChanged -= Refresh;
        if (!ProgressionManager.HasInstance) return;
        ProgressionManager.Instance.MetaChanged -= Refresh;
        ProgressionManager.Instance.FlagChanged -= OnFlag;
    }
    // A save loaded after this Start sets its trophy flags without MetaChanged: hang them as they come in.
    void OnFlag(string key, int value) { if (key.StartsWith(ProgressionManager.TrophyPrefix)) Refresh(); }

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
            // Co-op guest: the host's wall (MultiplayerManager keeps it up to date).
            var state = milestone == null ? ProgressionManager.TrophyState.None
                : MultiplayerManager.HasInstance && MultiplayerManager.Instance.ShowsHostTrophies ? (ProgressionManager.TrophyState)MultiplayerManager.Instance.HostTrophy(meta.TrophyKey(track, milestone))
                : meta.Trophy(track, milestone);
            bool hung = state == ProgressionManager.TrophyState.Mounted;
            // Its place glows while its trophy is carried: this player's own (a co-op guest's wall shows the host's
            // trophies, but the trophy a guest carries is the guest's to hang, TrophyMountSpot).
            var own = milestone == null ? ProgressionManager.TrophyState.None : meta.Trophy(track, milestone);
            if (mount.glow != null) mount.glow.Show(hangAtPlaques && own == ProgressionManager.TrophyState.Carried);
            if (item == null || item.worldPrefab == null || !hung && !emptyOutlines) { if (mount.model != null) Destroy(mount.model); mount.model = null; mount.shownItem = null; continue; }
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
        if (bootsToesOut && anchor.parent != null && Boots(item) && ToesOut(go, renderers, anchor)) bounds = Measure(renderers);
        else if (size.x < size.z && size.x <= size.y) { go.transform.localRotation = Quaternion.Euler(0, 90, 0) * go.transform.localRotation; bounds = Measure(renderers); }
        else if (size.y < size.z && size.y < size.x) { go.transform.localRotation = Quaternion.Euler(90, 0, 0) * go.transform.localRotation; bounds = Measure(renderers); }
        // The wall runs along x: what shows is the width (x) and the height (y).
        size = bounds.Value.size;
        float wide = size.x, tall = size.y;
        bool toesOut = bootsToesOut && Boots(item);
        if (!toesOut && Mathf.Max(wide, tall) / Mathf.Max(.0001f, Mathf.Min(wide, tall)) > 2.5f)
        {
            go.transform.localRotation = Quaternion.Euler(0, 0, longTilt) * go.transform.localRotation;
            bounds = Measure(renderers);
        }
        // Its front to the room: a shield's handle or a buckle's strap is on its back, sparse and reaching further
        // than the face, so the bulk of its surface lies toward the front. Turned round when that is the wall's side
        // (a shield hung showing its handle).
        if (!toesOut && anchor.parent != null && FrontAway(renderers, bounds.Value.center, anchor.parent.forward))
        {
            go.transform.RotateAround(bounds.Value.center, anchor.parent.up, 180f);
            bounds = Measure(renderers);
        }
        // Thick things (rings) stand out of the wall toward the viewer and look bigger, so their depth counts too.
        size = bounds.Value.size;
        float across = Mathf.Max(size.x, size.y) + size.z;
        go.transform.localScale = Vector3.one * (trophySize / Mathf.Max(.0001f, across));
        go.transform.position += anchor.position - Measure(renderers).Value.center;
        // Its back rests just off the mount's face (the backing behind it, however thick).
        if (anchor.parent != null)
        {
            var outward = anchor.parent.forward;
            float back = Reach(Measure(renderers).Value, anchor.parent.position, -outward);
            float face = 0f;
            foreach (var r in anchor.parent.GetComponentsInChildren<Renderer>())
                if (!r.transform.IsChildOf(go.transform)) face = Mathf.Max(face, Reach(r.bounds, anchor.parent.position, outward));
            go.transform.position += outward * (face + restGap + back);
        }
        return go;
    }

    // True when the area-weighted middle of the model's surface lies behind its bounds' middle, along `outward`.
    // Standing boots: the toes are where the foot sticks out furthest past the shaft (the lowest quarter against the
    // top 40%, along the model's own four sides: a pair side by side is as wide low as high, the heel barely sticks out).
    // Turned about the upright so that way points out of the wall. False (left to the usual hanging) when no mesh can
    // be read.
    static bool ToesOut(GameObject go, Renderer[] renderers, Transform anchor)
    {
        var points = new List<Vector3>();
        var baked = new Mesh();
        foreach (var r in renderers)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null && skinned.sharedMesh.isReadable) { skinned.BakeMesh(baked, true); mesh = baked; }
            else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null && filter.sharedMesh.isReadable) mesh = filter.sharedMesh;
            if (mesh == null) continue;
            foreach (var v in mesh.vertices) points.Add(anchor.InverseTransformPoint(r.transform.TransformPoint(v)));
        }
        Destroy(baked);
        if (points.Count < 8) return false;
        float low = float.MaxValue, high = float.MinValue;
        foreach (var p in points) { low = Mathf.Min(low, p.y); high = Mathf.Max(high, p.y); }
        float h = high - low;
        Vector3 toes = Vector3.zero; float most = float.MinValue;
        foreach (var side in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            float foot = float.MinValue, shaft = float.MinValue;
            foreach (var p in points)
            {
                float reach = Vector3.Dot(p, side);
                if (p.y < low + h * .25f) foot = Mathf.Max(foot, reach);
                else if (p.y > low + h * .6f) shaft = Mathf.Max(shaft, reach);
            }
            if (foot == float.MinValue || shaft == float.MinValue) continue;
            if (foot - shaft > most) { most = foot - shaft; toes = side; }
        }
        if (toes == Vector3.zero) return false;
        // Out of the wall is the plaque's forward (the anchor itself may be turned any way), in the anchor's own frame.
        var outward = anchor.InverseTransformDirection(anchor.parent.forward); outward.y = 0f;
        go.transform.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(toes, outward, Vector3.up), Vector3.up) * go.transform.localRotation;
        return true;
    }

    static bool Boots(ItemData item) => item.equipSlot == EquipmentSlot.Boots || item.itemName.IndexOf("boots", System.StringComparison.OrdinalIgnoreCase) >= 0;

    static bool FrontAway(Renderer[] renderers, Vector3 centre, Vector3 outward)
    {
        float sum = 0f, weight = 0f;
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;
            var v = mesh.vertices; var t = mesh.triangles; var m = r.transform.localToWorldMatrix;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                float area = Vector3.Cross(b - a, c - a).magnitude;
                sum += Vector3.Dot((a + b + c) / 3f - centre, outward) * area; weight += area;
            }
        }
        return weight > 0f && sum / weight < 0f;
    }

    // How far the bounds reach from `origin` along `dir`.
    static float Reach(Bounds b, Vector3 origin, Vector3 dir) =>
        Vector3.Dot(b.center - origin, dir) + Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.y) * b.extents.y + Mathf.Abs(dir.z) * b.extents.z;

    static Bounds? Measure(Renderer[] renderers)
    {
        Bounds? bounds = null;
        foreach (var r in renderers) { if (bounds == null) bounds = r.bounds; else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; } }
        return bounds;
    }
}
