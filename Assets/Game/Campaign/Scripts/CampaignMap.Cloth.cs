using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The fabric the cloth over closed acts is made of (CampaignManager > Cloth picks one; each has its material on the
// map prefab, Cloth > Cloths).
public enum CampaignClothStyle
{
    Plaid = 0,        // a red woollen tartan blanket
    Quilt = 1,        // patchwork squares, stitched
    Embroidered = 2,  // teal velvet with gold thread
    Burlap = 3,       // coarse sackcloth
    Starcloth = 4,    // a night-blue cloth sewn with stars
    Fog = 5,          // plain grey, mottled like the fog of war on an old map
}

// The cloth over closed acts: one blanket per act, covering only that act's land (the land nearer its places than any
// other act's), so opening one act uncovers it alone. It lies over the high ground with its valleys bridged, hangs
// down in folds at its edge, and is rolled back from the side nearest the player when its act opens.
public partial class CampaignMap
{
    [Serializable]
    public class ClothLook
    {
        [HorizontalGroup, HideLabel] public CampaignClothStyle style;
        [HorizontalGroup, HideLabel, Tooltip("Drawn from both sides: its back shows while it rolls up.")] public Material material;
    }

    [Title("Cloth")]
    [Tooltip("The material for each cloth style.")] public List<ClothLook> cloths = new();
    [Tooltip("How wide the cloth's hanging edge is, in local units.")] public float edgeWidth = 4f;
    [Tooltip("How far the cloth reaches past the line between open and closed places.")] public float edgeBias = 1.5f;
    [Tooltip("How much the cloth's edge wavers.")] public float edgeWaver = 3f;
    [Tooltip("How high the cloth lies over the highest ground near it.")] public float clothLift = .45f;
    [Tooltip("How far it bridges valleys: the highest ground within this many local units carries it.")] public float clothReach = 3f;
    [Tooltip("Height of the folds where it hangs over its edge.")] public float edgeFolds = .45f;
    [Tooltip("Radius of the roll it is pulled back into.")] public float clothRoll = 1.5f;
    [Tooltip("How much thicker the roll gets with each turn wound onto it.")] public float rollThickness = .05f;
    [Tooltip("The first reveal's cloth drop, simulated in Blender (Tools/campaign_cloth_drop.py): played back when the map is first shown, and where every act cloth lies (its settled shape). Empty: the cloths lie on the high ground (Cloth Reach) and the first reveal only rolls act 1's back.")]
    public TextAsset clothDropBake;
    [Tooltip("Lifts the simulated cloth this far over the land once it has risen (its triangles are coarser than the ground's).")]
    public float dropClearance = .25f;
    [Tooltip("Seconds the dropped cloth takes to spread out into the act cloths' shape (to the map's edges) once the land has risen.")]
    public float settleSeconds = .6f;
    [Tooltip("Scatter this close to the cloth (local units) is hidden with it, so nothing pokes through its edge.")] public float scatterClearance = 1.4f;

    class ClothLayer
    {
        public GameObject go; public Mesh mesh;
        public Vector3[] rest, work, restNormals, workNormals;
        public float[] cover;                          // how covered each vertex is (0-1)
        public Vector2 pull; public float sMin, sMax;  // the way it is pulled and how far it reaches along it
    }

    readonly Dictionary<int, ClothLayer> fogs = new();
    readonly Dictionary<Transform, (int act, bool on)> scatterAct = new();   // each scatter piece's act, and whether it stands (not on a road)
    int shownAct = 99;
    CampaignClothStyle builtStyle;

    public Material ClothMaterial(CampaignClothStyle style)
    {
        foreach (var c in cloths) if (c != null && c.style == style && c.material != null) return c.material;
        foreach (var c in cloths) if (c != null && c.material != null) return c.material;
        return null;
    }

    // Hides what lies in acts above openAct under the cloth (or plainly, with the cloth off).
    public void ShowActs(int openAct, bool cover, CampaignClothStyle style)
    {
        if (!Application.isPlaying) return;
        if (style != builtStyle) { foreach (var f in fogs.Values) if (f != null) f.go.GetComponent<MeshRenderer>().sharedMaterial = ClothMaterial(style); builtStyle = style; }
        shownAct = openAct;
        if (scatterAct.Count == 0 && scatter != null) foreach (Transform t in scatter) scatterAct[t] = (ActAt(t.localPosition), t.gameObject.activeSelf);
        foreach (var p in Points) p.gameObject.SetActive((p.encounter == null || p.encounter.act <= openAct) && !Hidden(p));
        foreach (var path in Paths) path.gameObject.SetActive(path.Open(openAct) && !Hidden(path));
        for (int act = 1, last = MaxAct; act <= last; act++)
        {
            bool covered = cover && act > openAct;
            if (covered && !fogs.ContainsKey(act)) fogs[act] = BuildCloth(act, style);
            if (fogs.TryGetValue(act, out var f) && f != null) { f.go.SetActive(covered); ResetCloth(f); }
        }
        // Scatter stands where its act is open, and not where a cloth lies over it (the cloth reaches a little past its
        // act's edge, and anything standing there would poke through it).
        foreach (var kv in scatterAct)
        {
            if (kv.Key == null) continue;
            bool show = kv.Value.on && kv.Value.act <= openAct;
            if (show && cover) show = !UnderCloth(kv.Key.localPosition, openAct);
            kv.Key.gameObject.SetActive(show);
        }
    }

    // The cloth of one act is rolled back and its places, roads and land rise into view as the roll passes them.
    public IEnumerator Reveal(int act, float seconds, Vector3 fromLocal)
    {
        fogs.TryGetValue(act, out var cloth);
        if (cloth != null) PrepareRoll(cloth, act, fromLocal);
        var pull = cloth != null ? cloth.pull : Vector2.up;
        float sMin = cloth != null ? cloth.sMin : 0f, span = cloth != null ? Mathf.Max(1f, cloth.sMax - cloth.sMin) : 1f;
        var appearing = new List<(Transform t, Vector3 scale, float at)>();
        void Add(Transform t)
        {
            var p = t.localPosition;
            float s = Vector2.Dot(new Vector2(p.x - fromLocal.x, p.z - fromLocal.z), pull);
            // Its own size, as first seen (a reveal cut short must not leave a piece shrunk for the next one).
            if (!restScale.TryGetValue(t, out var rest)) restScale[t] = rest = t.localScale;
            appearing.Add((t, rest, Mathf.Clamp01((s - sMin) / span)));
        }
        // This act's scatter, and earlier acts' scatter that only this cloth lay over.
        foreach (var kv in scatterAct)
            if (kv.Key != null && kv.Value.on && kv.Value.act <= act && !UnderCloth(kv.Key.localPosition, act) && (kv.Value.act == act || !kv.Key.gameObject.activeSelf))
                Add(kv.Key);
        foreach (var p in Points) if (p.encounter != null && p.encounter.act == act && !Hidden(p)) Add(p.transform);
        foreach (var path in Paths) if (path.Open(act) && !path.Open(act - 1) && !Hidden(path)) Add(path.transform);
        foreach (var a in appearing) { a.t.localScale = Vector3.zero; a.t.gameObject.SetActive(true); }
        // A piece's lights don't shrink with it: still under the cloth, a lantern would light the cloth from below as a
        // bright spot. They come up with their piece.
        var glows = new List<(Light light, float intensity, int piece)>();
        for (int i = 0; i < appearing.Count; i++)
            foreach (var l in appearing[i].t.GetComponentsInChildren<Light>()) { glows.Add((l, l.intensity, i)); l.intensity = 0f; }
        try
        {
            var grown = new float[appearing.Count];
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                float front = cloth != null ? RollCloth(cloth, k, fromLocal) : k;
                for (int i = 0; i < appearing.Count; i++)
                {
                    var a = appearing[i];
                    if (a.t == null) continue;
                    // Each piece pops up just after the roll has passed it.
                    float local = Mathf.Clamp01((front - a.at) / .12f);
                    float s = local < 1 ? Mathf.Sin(local * Mathf.PI * .5f) * (1f + .15f * Mathf.Sin(local * Mathf.PI)) : 1f;
                    a.t.localScale = a.scale * s;
                    grown[i] = local;
                }
                foreach (var g in glows) if (g.light != null) g.light.intensity = g.intensity * grown[g.piece] * grown[g.piece];
                yield return null;
            }
        }
        finally
        {
            foreach (var a in appearing) if (a.t != null) a.t.localScale = a.scale;
            foreach (var g in glows) if (g.light != null) g.light.intensity = g.intensity;
        }
        if (cloth != null) cloth.go.SetActive(false);
        shownAct = Mathf.Max(shownAct, act);
    }

    // Places kept off the map though their act is open (CampaignEncounter.deathsToOpen, decided by CampaignManager), and
    // the roads to them.
    [NonSerialized] public Func<CampaignEncounter, bool> Shut;
    bool Hidden(CampaignPoint p) => p != null && p.encounter != null && Shut != null && Shut(p.encounter);
    bool Hidden(CampaignPath path) => Hidden(path.from) || Hidden(path.to);

    // A place that was shut rises into view where it stands, with the open roads to it.
    public IEnumerator PopIn(CampaignEncounter encounter, float seconds)
    {
        var point = PointFor(encounter);
        if (point == null) yield break;
        var appearing = new List<(Transform t, Vector3 scale, float at)>();
        void Add(Transform t, float at)
        {
            if (!restScale.TryGetValue(t, out var rest)) restScale[t] = rest = t.localScale;
            appearing.Add((t, rest, at));
            t.localScale = Vector3.zero; t.gameObject.SetActive(true);
        }
        Add(point.transform, 0f);
        foreach (var path in Paths) if ((path.from == point || path.to == point) && path.Open(shownAct) && !Hidden(path)) Add(path.transform, .35f);
        try
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                foreach (var a in appearing)
                {
                    if (a.t == null) continue;
                    float local = Mathf.Clamp01((t / seconds - a.at) / .4f);
                    float s = local < 1 ? Mathf.Sin(local * Mathf.PI * .5f) * (1f + .15f * Mathf.Sin(local * Mathf.PI)) : 1f;
                    a.t.localScale = a.scale * s;
                }
                yield return null;
            }
        }
        finally { foreach (var a in appearing) if (a.t != null) a.t.localScale = a.scale; }
    }

    // Whether a cloth of an act above openAct lies over a spot (enough to hide what stands there).
    bool UnderCloth(Vector3 local, int openAct)
    {
        // A little round the spot too: a rock or tree is wide, and the cloth rises from the ground at its edge.
        foreach (var kv in fogs)
        {
            if (kv.Key <= openAct || kv.Value == null) continue;
            if (CoverOf(kv.Value, local.x, local.z) > .005f) return true;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2;
                if (CoverOf(kv.Value, local.x + Mathf.Cos(a) * scatterClearance, local.z + Mathf.Sin(a) * scatterClearance) > .005f) return true;
            }
        }
        return false;
    }

    float CoverOf(ClothLayer f, float lx, float lz)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt((lx / halfX + 1) * .5f * gridX), 0, gridX), z = Mathf.Clamp(Mathf.RoundToInt((lz / halfZ + 1) * .5f * gridZ), 0, gridZ);
        return f.cover[z * (gridX + 1) + x];
    }

    // ── Building a cloth ──

    float LX(int x) => (x / (float)gridX * 2 - 1) * halfX;
    float LZ(int z) => (z / (float)gridZ * 2 - 1) * halfZ;

    // How covered a spot is by this act's cloth: 1 well inside its land, sloping to 0 just past its edge.
    float[] Coverage(int act)
    {
        // Toward the acts that open before this one the cloth slopes down to the ground (its edge is seen once they are
        // open); toward later acts it reaches on, flat, a little under their cloth (which lies higher), so closed acts
        // read as one continuous cloth.
        var own = new List<Vector3>(); var others = new List<Vector3>(); var later = new List<Vector3>();
        foreach (var p in Points)
            if (p.encounter != null) (p.encounter.act == act ? own : p.encounter.act < act ? others : later).Add(p.transform.localPosition);
        static float Nearest(List<Vector3> list, float x, float z)
        {
            float best = 1e6f;
            foreach (var q in list) best = Mathf.Min(best, new Vector2(q.x - x, q.z - z).magnitude);
            return best;
        }
        var cover = new float[(gridX + 1) * (gridZ + 1)];
        if (own.Count == 0) return cover;
        for (int z = 0; z <= gridZ; z++)
            for (int x = 0; x <= gridX; x++)
            {
                float lx = LX(x), lz = LZ(z);
                float dOwn = Nearest(own, lx, lz);
                float side = Nearest(others, lx, lz) - dOwn + edgeBias + (Mathf.PerlinNoise(lx * .11f + 31, lz * .11f) - .5f) * edgeWaver;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(side / edgeWidth + .5f));
                if (later.Count > 0 && Nearest(later, lx, lz) - dOwn + edgeWidth < 0f) k = 0f;
                cover[z * (gridX + 1) + x] = k;
            }
        return cover;
    }

    ClothLayer BuildCloth(int act, CampaignClothStyle style)
    {
        var layer = new ClothLayer { cover = Coverage(act) };
        int w = gridX + 1;
        var drop = Drop;
        if (drop != null && !dropPending) drop.KeepLastFrame();   // only its settled shape is needed (~8 MB less)
        var drape = drop == null ? (drapeCache ??= Drape()) : null;
        var verts = new Vector3[layer.cover.Length]; var uvs = new Vector2[layer.cover.Length]; var tris = new List<int>();
        for (int z = 0; z <= gridZ; z++)
            for (int x = 0; x <= gridX; x++)
            {
                int i = z * w + x;
                float lx = LX(x), lz = LZ(z), k = layer.cover[i];
                // Soft wrinkles over the top.
                float wrinkle = Mathf.Sin(lx * .55f + lz * .2f + Mathf.PerlinNoise(lx * .07f, lz * .07f) * 6f) * .14f * Mathf.PerlinNoise(lx * .09f + 5, lz * .09f)
                                + Mathf.PerlinNoise(lx * .3f, lz * .3f) * .12f;
                // Folds where it hangs over its edge: ripples running down the slope, strongest halfway down.
                float gx = layer.cover[z * w + Mathf.Min(x + 1, gridX)] - layer.cover[z * w + Mathf.Max(x - 1, 0)];
                float gz = layer.cover[Mathf.Min(z + 1, gridZ) * w + x] - layer.cover[Mathf.Max(z - 1, 0) * w + x];
                var down = new Vector2(gx, gz);
                float folds = 0f;
                if (down.sqrMagnitude > 1e-6f)
                {
                    down.Normalize();
                    float along = -down.y * lx + down.x * lz;
                    folds = (Mathf.Sin(along * 1.25f + Mathf.PerlinNoise(lx * .15f, lz * .15f) * 4f) * .5f + .5f) * edgeFolds * 4f * k * (1 - k);
                }
                float top = drop != null ? Mathf.Max(drop.HeightAt(lx, lz, halfX, halfZ), Peak(lx, lz)) + dropClearance + act * .03f : drape[i] + clothLift + wrinkle + act * .03f;
                verts[i] = new Vector3(lx, Mathf.Lerp(HeightAt(lx, lz) - .15f, top, Mathf.Sqrt(k)) + folds, lz);
                uvs[i] = new Vector2(lx, lz) / 8f;
            }
        for (int z = 0; z < gridZ; z++)
            for (int x = 0; x < gridX; x++)
            {
                int a = z * w + x, b = a + 1, c = a + w, d = c + 1;
                if (layer.cover[a] + layer.cover[b] + layer.cover[c] + layer.cover[d] < .01f) continue;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = $"Cloth act {act}" };
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.MarkDynamic();
        layer.mesh = mesh; layer.rest = verts; layer.work = (Vector3[])verts.Clone();
        layer.restNormals = mesh.normals; layer.workNormals = (Vector3[])layer.restNormals.Clone();
        var go = new GameObject(mesh.name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ClothMaterial(style);
        layer.go = go;
        return layer;
    }

    // The highest ground within one cloth cell of a spot (the cloth's triangles span that much).
    float Peak(float lx, float lz)
    {
        float h = HeightAt(lx, lz);
        for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; h = Mathf.Max(h, HeightAt(lx + Mathf.Cos(a) * .6f, lz + Mathf.Sin(a) * .6f)); }
        return h;
    }

    CampaignClothDrop dropCache; bool dropRead;
    [System.NonSerialized] float[] drapeCache;
    readonly Dictionary<Transform, Vector3> restScale = new();
    // Set by the campaign while the first reveal (the drop) is still to come: its frames are kept until then.
    [System.NonSerialized] public bool dropPending;
    // The highest act any place belongs to (at least 4): the acts the cloths cover.
    public int MaxAct { get { int m = 4; foreach (var p in Points) if (p.encounter != null) m = Mathf.Max(m, p.encounter.act); return m; } }
    CampaignClothDrop Drop { get { if (!dropRead) { dropRead = true; dropCache = CampaignClothDrop.Read(clothDropBake); } return dropCache; } }

    // The first reveal (after the first morning's dice): the simulated cloth falls onto the bare table, the land rises
    // under it, then act 1's part is rolled back as any act's is (rollStarts is called as it begins). Without a bake it
    // only rolls act 1's cloth back.
    public IEnumerator DropReveal(CampaignClothStyle style, bool cover, Vector3 fromLocal, float rollSeconds, System.Action rollStarts = null)
    {
        ShowActs(0, cover, style);
        var drop = Drop;
        // Trimmed to its settled shape after an earlier drop (debug reset): read in full again.
        if (drop != null && drop.frames < 2) { dropRead = false; drop = Drop; }
        if (drop != null && cover)
        {
            foreach (var f in fogs.Values) if (f != null) f.go.SetActive(false);
            var go = new GameObject("Cloth drop");
            go.transform.SetParent(transform, false);
            var mesh = drop.BuildMesh(halfX, halfZ);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ClothMaterial(style);
            var verts = new Vector3[drop.nx * drop.nz];
            var rest = transform.localScale;
            try
            {
                // At most a frame's worth of time a step: after a hitch the cloth carries on falling instead of jumping.
                for (float t = 0; t <= drop.Seconds; t += Mathf.Min(Time.deltaTime, 1f / 30f))
                {
                    // The land rises with the simulation's collider (flat until riseStart, full at riseEnd, eased).
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(drop.riseStart, drop.riseEnd, t));
                    float sy = Mathf.Max(.01f, k);
                    transform.localScale = new Vector3(rest.x, rest.y * sy, rest.z);
                    go.transform.localScale = new Vector3(1f, 1f / sy, 1f);
                    drop.Sample(t, verts);
                    // A little clearance as the land rises (the cloth's triangles are coarser than the ground's).
                    // Never under the ground (a sharp peak fits between the cloth's vertices): at least the clearance over it.
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var v = verts[i];
                        v.y = Mathf.Max(v.y + dropClearance * k, (Peak(v.x, v.z) + dropClearance) * sy);
                        verts[i] = v;
                    }
                    mesh.vertices = verts; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    yield return null;
                }
                transform.localScale = rest; go.transform.localScale = Vector3.one;
                // The simulated cloth has drawn in as the land rose under it; the act cloths reach the map's edges and
                // hang over them. It spreads out into their shape before they take over, so nothing snaps.
                var from = (Vector3[])verts.Clone(); var to = new Vector3[verts.Length];
                for (int z = 0; z < drop.nz; z++)
                    for (int x = 0; x < drop.nx; x++)
                    {
                        float lx = (x / (float)(drop.nx - 1) * 2 - 1) * halfX, lz = (z / (float)(drop.nz - 1) * 2 - 1) * halfZ;
                        float y = HeightAt(lx, lz) - .15f;
                        foreach (var f in fogs.Values) if (f != null) y = Mathf.Max(y, RestSurfaceAt(f, lx, lz));
                        to[z * drop.nx + x] = new Vector3(lx, y, lz);
                    }
                for (float t = 0; t < settleSeconds; t += Mathf.Min(Time.deltaTime, 1f / 30f))
                {
                    float e = Mathf.SmoothStep(0f, 1f, t / settleSeconds);
                    for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.LerpUnclamped(from[i], to[i], e);
                    mesh.vertices = verts; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    yield return null;
                }
            }
            finally
            {
                transform.localScale = rest;
                Destroy(go); Destroy(mesh);
            }
            foreach (var kv in fogs) if (kv.Value != null) { kv.Value.go.SetActive(true); ResetCloth(kv.Value); }
            drop.KeepLastFrame();   // ~8 MB of frames no longer needed
        }
        rollStarts?.Invoke();
        yield return Reveal(1, rollSeconds, fromLocal);
        // With the cloth off, closed acts are simply not shown.
        if (!cover) ShowActs(1, false, style);
    }

    // The ground with its valleys bridged: the highest ground within clothReach, smoothed. The cloth lies on this.
    float[] Drape()
    {
        int w = gridX + 1, h = gridZ + 1;
        int reach = Mathf.Max(1, Mathf.RoundToInt(clothReach / (halfX * 2 / gridX))), blur = Mathf.Max(1, reach / 2);
        var g = new float[w * h];
        for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) g[z * w + x] = HeightAt(LX(x), LZ(z));
        float[] Pass(float[] src, bool max, bool alongX, int r)
        {
            var dst = new float[src.Length];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    float acc = max ? float.MinValue : 0f; int n = 0;
                    for (int i = -r; i <= r; i++)
                    {
                        int xx = alongX ? Mathf.Clamp(x + i, 0, w - 1) : x, zz = alongX ? z : Mathf.Clamp(z + i, 0, h - 1);
                        float v = src[zz * w + xx];
                        if (max) acc = Mathf.Max(acc, v); else { acc += v; n++; }
                    }
                    dst[z * w + x] = max ? acc : acc / n;
                }
            return dst;
        }
        g = Pass(Pass(g, true, true, reach), true, false, reach);
        return Pass(Pass(g, false, true, blur), false, false, blur);
    }

    // ── Rolling it back ──

    void ResetCloth(ClothLayer f)
    {
        f.go.transform.localPosition = Vector3.zero;
        Array.Copy(f.rest, f.work, f.rest.Length);
        Array.Copy(f.restNormals, f.workNormals, f.rest.Length);
        f.mesh.vertices = f.work; f.mesh.normals = f.workNormals;
        f.mesh.RecalculateBounds();
    }

    void PrepareRoll(ClothLayer f, int act, Vector3 from)
    {
        // Pulled from the side nearest where the player stands, toward the far side of the act's land.
        var centre = Vector3.zero; int n = 0;
        foreach (var p in Points) if (p.encounter != null && p.encounter.act == act) { centre += p.transform.localPosition; n++; }
        if (n > 0) centre /= n;
        var d = new Vector2(centre.x - from.x, centre.z - from.z);
        f.pull = d.sqrMagnitude > .01f ? d.normalized : Vector2.up;
        f.sMin = float.MaxValue; f.sMax = float.MinValue;
        for (int i = 0; i < f.rest.Length; i++)
        {
            if (f.cover[i] < .01f) continue;
            float s = Vector2.Dot(new Vector2(f.rest[i].x - from.x, f.rest[i].z - from.z), f.pull);
            f.sMin = Mathf.Min(f.sMin, s); f.sMax = Mathf.Max(f.sMax, s);
        }
    }

    // The blanket is rolled up from the side nearest the player: what the roll has passed is wound round it (growing
    // thicker), the rest still lies on the land. Once past the far edge the roll is lifted off the table. Returns how far
    // along the land (0-1) the roll is.
    float RollCloth(ClothLayer f, float k, Vector3 from)
    {
        float span = Mathf.Max(1f, f.sMax - f.sMin);
        float t = Mathf.Clamp01(k / .85f);
        // It starts slowly as the edge is gathered, then runs.
        float eased = t * t * (3f - 2f * t);
        float front = Mathf.Lerp(f.sMin - clothRoll, f.sMax + clothRoll * 2f, eased);
        float away = Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - .82f) / .18f));
        var d = new Vector3(f.pull.x, 0, f.pull.y);
        for (int i = 0; i < f.rest.Length; i++)
        {
            var p = f.rest[i];
            float s = Vector2.Dot(new Vector2(p.x - from.x, p.z - from.z), f.pull);
            if (s >= front) { f.work[i] = p; f.workNormals[i] = f.restNormals[i]; continue; }
            float wound = front - s;
            float theta = Mathf.Min(wound / clothRoll, Mathf.PI * 2 * 8);
            float radius = clothRoll + theta / (Mathf.PI * 2) * rollThickness;
            var atFront = p + d * wound;
            float baseY = Mathf.Max(p.y, RestHeightAt(f, atFront.x, atFront.z));
            var centre = new Vector3(atFront.x, baseY + radius, atFront.z);
            // Lit as the outside of the roll: its turns lie on top of each other, so their own slopes mean nothing.
            var outward = -d * Mathf.Sin(theta) - Vector3.up * Mathf.Cos(theta);
            f.work[i] = centre + outward * radius + Vector3.up * away * 14f;
            f.workNormals[i] = outward;
        }
        f.mesh.vertices = f.work; f.mesh.normals = f.workNormals;
        f.mesh.RecalculateBounds();
        return Mathf.Clamp01((front - f.sMin) / span);
    }

    // A cloth's resting surface at a spot, between its grid's vertices (bilinear).
    float RestSurfaceAt(ClothLayer f, float lx, float lz)
    {
        float fx = Mathf.Clamp01((lx / halfX + 1) * .5f) * gridX, fz = Mathf.Clamp01((lz / halfZ + 1) * .5f) * gridZ;
        int ix = Mathf.Min(gridX - 1, (int)fx), iz = Mathf.Min(gridZ - 1, (int)fz), w = gridX + 1;
        float tx = fx - ix, tz = fz - iz;
        float a = f.rest[iz * w + ix].y, b = f.rest[iz * w + ix + 1].y, c = f.rest[(iz + 1) * w + ix].y, d = f.rest[(iz + 1) * w + ix + 1].y;
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    float RestHeightAt(ClothLayer f, float lx, float lz)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt((lx / halfX + 1) * .5f * gridX), 0, gridX), z = Mathf.Clamp(Mathf.RoundToInt((lz / halfZ + 1) * .5f * gridZ), 0, gridZ);
        return f.rest[z * (gridX + 1) + x].y;
    }
}
