using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A pillar on a boss stage that the Dungeon Master's beam can be hidden behind, once (the user, 2026-11-16: "if you hide
// behind the pillar to avoid the laser, the pillar breaks after the laser finishes casting, but only if the laser at least
// touched the pillar once"). On each Cover pillar of the Boss stage. DungeonMasterBeam tells it while its stream ends on
// it (BeamOn; the warning arc does not count): the stone heats up in the beam's colour, so the player sees it is taking
// the beam for them. BossStageEncounter breaks every touched pillar as that beam ends (BreakTouched) and has the Dungeon
// Master mock it. Broken, its own model is cut (MeshShatter) into a stump that stays where it stood and glowing lumps
// that come down: they fall free and break up as they reach the stump or the floor, in the dust at its foot (the whole
// top of a pillar this size would bury the stage; stacked as solid bodies it stood in the air for half a second, which
// read as floating shards, not a collapse), a few fall as small stones round the stump and lie there for the rest of the
// floor. The stump is the pier split into columns, each broken off at its own height (one cut across the whole pier
// was a smooth tent), and it smoulders for a while. The stump is solid (it stands in the
// pillar's own footprint, where the floor's navigation already has its hole); the stones are not (nobody trips on
// rubble), and neither is cover from the next beam. The torch on it goes out and falls with it.
public class BossCoverPillar : MonoBehaviour
{
    [Tooltip("The torch on this pillar (a BossStageFire beside it): its flame and light go out and its bracket falls when the pillar breaks.")]
    public GameObject torch;

    [Header("Under the beam")]
    [Min(.1f), Tooltip("Seconds of the beam's stream on it until the stone glows at full strength.")]
    public float heatSeconds = 2.2f;
    [Min(0), Tooltip("How brightly the stone glows at full heat: from inside, strongest in its joints and dark cracks, dull red at first and orange when hot.")]
    public float glow = 1.9f;
    [Min(0), Tooltip("A glow of that colour on the stone and the floor round the spot the beam is on, at full heat (0: none).")]
    public float hotLight = 5f;
    [Min(.1f), Tooltip("Seconds the glow takes to die away, once the beam is off it or it has broken.")]
    public float coolSeconds = 2.5f;

    [Header("Break")]
    [Min(.2f), Tooltip("Metres of the pillar left standing as a stump, about: it breaks off in steps, the lowest corner at half this and the tallest tooth at nearly one and a half times it.")]
    public float stumpHeight = .95f;
    [Min(.2f), Tooltip("The top comes down in lumps at most about this long, metres.")]
    public float lumpSize = .75f;
    [Min(0), Tooltip("How many of those lumps break up into small stones that stay on the stage; the others break up to dust as they land.")]
    public int stoneLumps = 4;
    [Min(.1f), Tooltip("The stones that stay are at most about this long, metres.")]
    public float stoneSize = .32f;
    [Min(0), Tooltip("How fast the lumps are thrown outward as they fall, metres a second (those higher up a little faster).")]
    public float burst = 1.8f;
    [Min(.05f), Tooltip("Seconds a lump takes to break up, once it has landed.")]
    public float crumbleSeconds = .16f;
    [Min(.5f), Tooltip("A stone that comes to rest further than this from where the pillar stood is taken away, metres.")]
    public float keepWithin = 3f;
    [Tooltip("The broken faces are drawn in the pillar's own stone, tinted by this (fresh stone inside is a little darker and duller than the weathered face).")]
    public Color brokenFace = new(.5f, .47f, .45f, 1f);
    [Min(0), Tooltip("Seconds the broken faces go on smouldering after the break (embers in the stone's cracks, dying away).")]
    public float smoulderSeconds = 10f;
    [Min(0), Tooltip("How brightly they smoulder at first.")]
    public float smoulder = 1f;
    [Tooltip("The pillar's dust (Props/Effects/Pillar dust): played once where it stood as it breaks. Stone-dark and low, rolling out from its foot.")]
    public GameObject dust;
    [Tooltip("Played where it stood as it breaks.")]
    public AudioClip breakSound;
    [Range(0, 1)] public float breakVolume = 1f;
    [Tooltip("Played a moment later, as the stones land.")]
    public AudioClip rubbleSound;
    [Range(0, 1)] public float rubbleVolume = .8f;
    [Min(0)] public float rubbleSoundDelay = .35f;
    [Min(0), Tooltip("View shake as it breaks.")] public float shake = 1.1f;
    [Min(0), Tooltip("A flash of the beam's own light where it last touched the pillar, as it breaks (0: none).")]
    public float flash = 14f;
    [Min(.05f)] public float flashSeconds = .35f;

    // The beam's stream ended on it at least once in the beam that is on (BossStageEncounter clears it as each begins).
    public bool Touched { get; private set; }
    public bool Broken { get; private set; }
    Vector3 lastHit;
    float heat, lastBeam = -10f;
    Color beamColour = Color.white;

    // Colliders of broken pillars (the stump, pieces still falling): never cover from the beam.
    static readonly HashSet<Collider> rubble = new();
    // The loose stones lying about, to be cleared from under the boss chest and the way on.
    static readonly List<GameObject> stones = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { rubble.Clear(); stones.Clear(); cracks.Clear(); }

    public static bool IsRubble(Collider c) => c != null && rubble.Contains(c);

    // The beam's stream ends on this collider right now: its pillar, if it is one, heats up and is marked for breaking.
    public static void BeamOn(Collider cover, Vector3 point, Color colour)
    {
        var pillar = cover != null ? cover.GetComponentInParent<BossCoverPillar>() : null;
        if (pillar == null || pillar.Broken) return;
        pillar.Touched = true; pillar.lastHit = point; pillar.lastBeam = Time.time;
        pillar.beamColour = Normalised(colour);
        pillar.Heat();
        pillar.heat = Mathf.Min(1f, pillar.heat + Time.deltaTime / pillar.heatSeconds);
        pillar.enabled = true;
        // Its break is worked out from now on, a little each frame, so it is ready when the beam ends.
        if (pillar.plan == null) { pillar.plan = pillar.Plan(); pillar.StartCoroutine(pillar.Prepare()); }
    }

    // A new beam begins: nothing on this stage is marked yet.
    public static void ClearTouched(Component stage)
    {
        if (stage == null) return;
        foreach (var p in stage.GetComponentsInChildren<BossCoverPillar>()) p.Touched = false;
    }

    // Every pillar of the stage the beam touched breaks; returns how many did.
    public static int BreakTouched(Component stage, Color colour, Vector3 keepFrom)
    {
        if (stage == null) return 0;
        int broke = 0;
        foreach (var p in stage.GetComponentsInChildren<BossCoverPillar>())
            if (p.Touched && !p.Broken) { p.Break(colour, keepFrom); broke++; }
        return broke;
    }

    public static int BrokenOn(Component stage)
    {
        int n = 0;
        if (stage != null) foreach (var p in stage.GetComponentsInChildren<BossCoverPillar>()) if (p.Broken) n++;
        return n;
    }

    // Loose stones within radius of a spot are taken away (the boss chest comes up there, or the way on opens).
    public static void ClearRubble(Vector3 centre, float radius)
    {
        for (int i = stones.Count - 1; i >= 0; i--)
        {
            var stone = stones[i];
            if (stone == null) { stones.RemoveAt(i); continue; }
            var flat = stone.transform.position - centre; flat.y = 0f;
            if (flat.magnitude > radius) continue;
            Destroy(stone); stones.RemoveAt(i);
        }
    }

    static Color Normalised(Color c) { float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return m > 0f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white; }

    readonly List<Object> made = new();   // meshes and materials made for it, freed with the pillar
    // The pillar's materials as copies that can glow (each lit from inside through its own texture), by the original;
    // faces: the broken faces' copies, which glow twice as bright (the stone's hot inside).
    readonly Dictionary<Material, Material> hot = new();
    readonly List<Material> faces = new();
    // Lumps coming down: each falls free (no body, no collider: it meets nothing on its way) until its foot reaches where
    // it lands, or its time is up, and from then shrinks to nothing. from: when it began to (under 0: not yet).
    struct Crumb { public Transform piece; public Vector3 velocity, spin; public float land, half, at, from; public bool falls; }
    readonly List<Crumb> crumbs = new();
    readonly HashSet<GameObject> kept = new();   // the torch's bracket: it stays wherever it comes to rest
    Transform holder;
    readonly List<Bounds> stumpBoxes = new();   // the stump's colliders, as they stand
    Light hotSpot;
    float brokeAt = -100f;

    void Awake()
    {
        foreach (var clip in new[] { breakSound, rubbleSound })
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
    }

    // The map of where its stone glows is made as the stage comes up (it reads the texture back from the graphics card
    // and goes over every texel: some milliseconds, which would otherwise fall in the frame the beam first touches it).
    IEnumerator Start()
    {
        yield return null; yield return null;
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty("_EmissionMap") && material.mainTexture != null) Cracks(material.mainTexture);
    }

    void OnDestroy()
    {
        planned = true;   // a cut still being worked out stops here
        foreach (var o in made) if (o != null) Destroy(o);
        rubble.RemoveWhere(c => c == null);
        stones.RemoveAll(s => s == null);
    }

    // The glow dies away once the beam is off the pillar or it has broken; lumps burn away at their moments.
    void Update()
    {
        if (Broken || Time.time - lastBeam > .25f) heat = Mathf.MoveTowards(heat, 0f, Time.deltaTime / coolSeconds);
        ShowHeat();
        float dt = Time.deltaTime;
        for (int i = crumbs.Count - 1; i >= 0; i--)
        {
            var c = crumbs[i];
            if (c.piece == null) { crumbs.RemoveAt(i); continue; }
            if (c.from < 0f)
            {
                if (c.falls)
                {
                    c.velocity += Physics.gravity * dt;
                    c.piece.position += c.velocity * dt;
                    c.piece.Rotate(c.spin * dt, Space.World);
                }
                if (c.falls && c.piece.position.y - c.half <= c.land || Time.time >= c.at) c.from = Time.time;
                crumbs[i] = c;
                continue;
            }
            float u = (Time.time - c.from) / crumbleSeconds;
            if (u >= 1f)
            {
                foreach (var col in c.piece.GetComponents<Collider>()) rubble.Remove(col);
                Destroy(c.piece.gameObject); crumbs.RemoveAt(i);
                continue;
            }
            // Landed: it slumps outward a little as it breaks up.
            if (c.falls) c.piece.position += new Vector3(c.velocity.x, 0f, c.velocity.z) * (dt * .5f);
            c.piece.localScale = Vector3.one * (1f - u * u);
        }
        if (heat <= 0f && crumbs.Count == 0 && Time.time - brokeAt > smoulderSeconds) enabled = false;
    }

    // The pillar's renderers take the copies that can glow (once).
    void Heat()
    {
        if (hot.Count > 0) return;
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = Hot(materials[i]);
            renderer.sharedMaterials = materials;
        }
        if (hotLight > 0f && hotSpot == null)
        {
            var go = new GameObject("Pillar heat");
            go.transform.SetParent(transform, false);
            hotSpot = go.AddComponent<Light>();
            hotSpot.type = LightType.Point; hotSpot.shadows = LightShadows.None; hotSpot.range = 4.5f; hotSpot.intensity = 0f; hotSpot.enabled = false;
        }
    }

    Material Hot(Material source)
    {
        if (source == null) return null;
        if (hot.TryGetValue(source, out var copy)) return copy;
        copy = new Material(source) { name = source.name + " (hot)" };
        if (copy.HasProperty("_EmissionColor"))
        {
            copy.EnableKeyword("_EMISSION");
            copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (copy.HasProperty("_EmissionMap") && copy.mainTexture != null) copy.SetTexture("_EmissionMap", Cracks(copy.mainTexture));
            copy.SetColor("_EmissionColor", Color.black);
        }
        hot.Add(source, copy); made.Add(copy);
        return copy;
    }

    // Where a stone texture glows when the stone is hot: its joints, cracks and dark texels most, its pale faces only a
    // blush. Lit through its own texture the whole pillar just looked floodlit (bright where the stone is pale, which is
    // how a lamp lights it); heat comes out of the cracks. Made once per texture, texel for texel, the texture's own size
    // and filter. The texture is read back from the graphics card (pixel copies are not kept readable).
    static readonly Dictionary<Texture, Texture2D> cracks = new();
    static Texture Cracks(Texture source)
    {
        if (source == null) return null;
        if (cracks.TryGetValue(source, out var map) && map != null) return map;
        int w = source.width, h = source.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var was = RenderTexture.active;
        var keep = source.filterMode; source.filterMode = FilterMode.Point;
        Graphics.Blit(source, rt);
        source.filterMode = keep;
        RenderTexture.active = rt;
        var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
        var pixels = read.GetPixels32();
        Destroy(read);
        // The texture's own dark and pale ends (its 4th and 96th texel in a hundred), so any stone gets the full range.
        var counts = new int[256]; int painted = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i]; if (c.a < 8) continue;
            counts[(c.r * 77 + c.g * 150 + c.b * 29) >> 8]++; painted++;
        }
        int lo = 0, hi = 255, sum = 0;
        for (int v = 0; v < 256; v++) { sum += counts[v]; if (sum >= painted * .04f) { lo = v; break; } }
        sum = 0;
        for (int v = 255; v >= 0; v--) { sum += counts[v]; if (sum >= painted * .04f) { hi = v; break; } }
        float span = Mathf.Max(8, hi - lo);
        var table = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            float dark = 1f - Mathf.Clamp01((v - lo) / span);
            table[v] = (byte)Mathf.RoundToInt(255f * (.03f + .97f * Mathf.Pow(dark, 5f)));
        }
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            byte g = table[(c.r * 77 + c.g * 150 + c.b * 29) >> 8];
            pixels[i] = new Color32(g, g, g, 255);
        }
        map = new Texture2D(w, h, TextureFormat.RGBA32, source.mipmapCount > 1) { name = source.name + " (cracks)", filterMode = source.filterMode, wrapMode = source.wrapMode, anisoLevel = 0 };
        map.SetPixels32(pixels); map.Apply(true, true);
        cracks[source] = map;
        return map;
    }

    // Dull red as it warms, orange once hot (with a little of the beam's own colour), never the beam's colour itself:
    // a yellow beam on cream stone was just a lit pillar.
    Color HeatColour(float at) => Color.Lerp(new Color(1f, .1f, .02f), Color.Lerp(new Color(1f, .24f, .03f), beamColour, .1f), at);

    void ShowHeat()
    {
        var colour = HeatColour(heat) * (heat * heat * glow);
        // The broken faces go on smouldering after the heat has gone: embers that flicker and die away.
        float ember = 0f;
        if (Broken && smoulderSeconds > 0f)
        {
            float left = 1f - Mathf.Clamp01((Time.time - brokeAt) / smoulderSeconds);
            ember = left * left * smoulder * (.8f + .4f * Mathf.PerlinNoise(Time.time * 3.1f, transform.position.x));
        }
        foreach (var m in hot.Values) if (m != null && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour);
        foreach (var m in faces) if (m != null && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour * .55f + HeatColour(.25f) * ember);
        if (hotSpot != null)
        {
            hotSpot.intensity = Broken ? 0f : heat * heat * hotLight;
            hotSpot.color = HeatColour(heat);
            hotSpot.enabled = hotSpot.intensity > .01f;
            // Just off the stone where the beam is on it, on the beam's side.
            var off = lastHit - transform.position; off.y = 0f;
            hotSpot.transform.position = lastHit + (off.sqrMagnitude > .0001f ? off.normalized * .35f : Vector3.zero);
        }
    }

    // ── The cut, worked out ahead ─────────────────────────────────────
    // Cutting the model up takes a tenth of a second and more, far too long for the frame it breaks in. So it is worked
    // out from the moment the beam first touches the pillar, a couple of milliseconds a frame (Prepare), and the break
    // only sets the finished pieces loose. A break that comes before it is done finishes it on the spot. The pillar
    // never moves, and the cuts are drawn from where it stands, so every machine and a reload get the same pieces.
    enum Kind { Stump, Lump, Stone }
    class Part { public Kind kind; public Mesh mesh; public Vector3 centre; public int[] used; public int source; }
    class Source { public MeshFilter filter; public MeshRenderer renderer; public int faceFrom; }
    readonly List<Part> parts = new();
    readonly List<Source> sources = new();
    IEnumerator plan;
    bool planned, hurried;
    float height;
    System.Random random;
    readonly System.Diagnostics.Stopwatch slice = new();
    const double SliceMilliseconds = 2;

    float Roll() => (float)random.NextDouble();
    bool Due() => !hurried && slice.Elapsed.TotalMilliseconds >= SliceMilliseconds;

    IEnumerator Prepare()
    {
        slice.Restart();
        while (!planned && plan.MoveNext()) { yield return null; slice.Restart(); }
    }

    IEnumerator Plan()
    {
        var origin = transform.position;
        random = new System.Random(Mathf.RoundToInt(origin.x * 31f) * 73856093 ^ Mathf.RoundToInt(origin.z * 31f) * 19349663);
        // The stump: the pier split into four columns by two upright cuts (off its middle, a little askew), each column
        // broken off at its own height, the four heights spread from a low corner to a tall tooth, each along a face
        // tipped its own way with a chip off one of its corners. So the crown steps and juts like broken masonry.
        // (Cuts across the whole pier can only leave a rounded tent: all it can do is take away what is above a face.)
        float[] levels = { .55f, .8f, 1.1f, 1.4f };
        for (int k = levels.Length - 1; k > 0; k--) { int j = (int)(Roll() * (k + 1)); if (j > k) j = k; (levels[k], levels[j]) = (levels[j], levels[k]); }
        var used = new List<int>();
        Part Make(Kind kind, MeshShatter.Solid solid, int source)
        {
            var mesh = MeshShatter.ToMesh(solid, out var centre, used);
            made.Add(mesh);
            var part = new Part { kind = kind, mesh = mesh, centre = centre, used = used.ToArray(), source = source };
            parts.Add(part);
            MeshShatter.Release(solid);
            return part;
        }
        foreach (var filter in GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            if (renderer == null || !renderer.enabled || mesh == null || !mesh.isReadable) continue;
            var solid = MeshShatter.From(mesh, filter.transform.localToWorldMatrix, origin);
            var patch = MeshShatter.TopPatch(solid);
            height = Mathf.Max(height, solid.Bounds.max.y);
            int source = sources.Count;
            sources.Add(new Source { filter = filter, renderer = renderer, faceFrom = patch.submesh });
            if (Due()) yield return null;

            var lumps = new List<MeshShatter.Solid>();
            var whole = solid.Bounds;
            var columns = new List<MeshShatter.Solid> { solid };
            foreach (var axis in new[] { Vector3.right, Vector3.forward })
            {
                float across = axis.x > 0f ? whole.size.x : whole.size.z;
                var through = new Vector3(whole.center.x, 0f, whole.center.z) + axis * ((Roll() - .5f) * .36f * across);
                var facing = Quaternion.Euler((Roll() - .5f) * 12f, (Roll() - .5f) * 28f, (Roll() - .5f) * 12f) * axis;
                var halves = new List<MeshShatter.Solid>();
                foreach (var column in columns)
                {
                    MeshShatter.Split(column, through, facing, patch, out var one, out var other, true, true);
                    MeshShatter.Release(column);
                    if (one.Corners >= 9) halves.Add(one); else MeshShatter.Release(one);
                    if (other.Corners >= 9) halves.Add(other); else MeshShatter.Release(other);
                    if (Due()) yield return null;
                }
                columns = halves;
            }
            for (int c = 0; c < columns.Count; c++)
            {
                var stump = columns[c];
                var middle = stump.Bounds.center; middle.y = 0f;
                float tall = stumpHeight * levels[c % levels.Length] + (Roll() - .5f) * .12f;
                float yaw = Roll() * 360f;
                for (int k = 0; k < 2; k++)
                {
                    bool chip = k == 1;
                    var turn = Quaternion.Euler(0f, chip ? yaw + 90f + Roll() * 180f : yaw, 0f);
                    var normal = turn * Quaternion.Euler(chip ? 40f + Roll() * 18f : 10f + Roll() * 22f, 0f, 0f) * Vector3.up;
                    var point = middle + Vector3.up * (chip ? tall - .04f - Roll() * .18f : tall) + (chip ? turn * Vector3.forward * (.22f + Roll() * .12f) : Vector3.zero);
                    var before = stump;
                    MeshShatter.Split(before, point, normal, patch, out var above, out stump);
                    MeshShatter.Release(before);
                    // Each break's lumps their own size, so big blocks come down among small ones.
                    var steps = MeshShatter.Shatter(above, lumpSize * (.7f + Roll() * .9f), random, patch, lumps, Due);
                    while (steps.MoveNext()) yield return null;
                }
                if (stump.Corners >= 9) Make(Kind.Stump, stump, source); else MeshShatter.Release(stump);
            }
            // A few lumps, spread through the pillar, break up into the stones that stay; the rest burn away.
            int every = stoneLumps > 0 ? Mathf.Max(1, lumps.Count / stoneLumps) : int.MaxValue, taken = 0;
            var small = new List<MeshShatter.Solid>();
            for (int i = 0; i < lumps.Count; i++)
            {
                if (taken < stoneLumps && i % every == every / 2)
                {
                    taken++;
                    small.Clear();
                    var steps = MeshShatter.Shatter(lumps[i], stoneSize, random, patch, small, Due);
                    while (steps.MoveNext()) yield return null;
                    // Slivers are left out (thin plates and splinters: sheets of card, not stones).
                    foreach (var stone in small) { if (stone.Sliver) MeshShatter.Release(stone); else Make(Kind.Stone, stone, source); }
                }
                else if (lumps[i].Sliver) MeshShatter.Release(lumps[i]);
                else Make(Kind.Lump, lumps[i], source);
                if (Due()) yield return null;
            }
        }
        MeshShatter.Trim();
        planned = true;
    }

    // colour: the beam's. keepFrom: where the player stands (what bursts their way drops and burns away first, so no
    // stone flies into their face).
    public void Break(Color colour, Vector3 keepFrom)
    {
        if (Broken) return;
        Broken = true; Touched = false; brokeAt = Time.time;
        beamColour = Normalised(colour);
        Heat();
        heat = 1f; enabled = true;   // white-hot as it goes, then cooling
        // The cut, finished now if the beam was on it too short a time.
        plan ??= Plan();
        hurried = true;
        while (!planned && plan.MoveNext()) { }
        var origin = transform.position;
        holder = new GameObject(name + " rubble").transform;
        holder.SetParent(transform.parent, false);
        holder.SetPositionAndRotation(origin, Quaternion.identity);
        var toPlayer = keepFrom - origin; toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > .01f ? toPlayer.normalized : Vector3.zero;

        // Each model's own materials (the glowing copies), and one more for its broken faces: the stone its flat top is
        // drawn with, tinted.
        var materials = new List<Material>[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var list = materials[i] = new List<Material>(source.renderer.sharedMaterials);
            int subs = source.filter.sharedMesh != null ? source.filter.sharedMesh.subMeshCount : 1;
            while (list.Count < subs) list.Add(list.Count > 0 ? list[0] : null);
            var from = list.Count > 0 ? list[Mathf.Clamp(source.faceFrom, 0, list.Count - 1)] : null;
            Material face = null;
            if (from != null)
            {
                face = new Material(from) { name = from.name + " (broken)" };
                if (face.HasProperty("_BaseColor")) face.SetColor("_BaseColor", face.GetColor("_BaseColor") * brokenFace);
                else if (face.HasProperty("_Color")) face.color *= brokenFace;
                // A broken face glows evenly through its own stone (the hot inside), not through the joints' map: its
                // texels are one patch of the top, and a patch that lay on a joint was one flat, bright rectangle.
                if (face.HasProperty("_EmissionMap") && face.mainTexture != null) face.SetTexture("_EmissionMap", face.mainTexture);
                made.Add(face); faces.Add(face);
            }
            list.Add(face);
        }

        var bodies = new List<Rigidbody>();
        var stumps = new List<Collider>();
        foreach (var part in parts)
        {
            var source = sources[part.source];
            var go = Piece(part, materials[part.source], source.renderer);
            var bounds = part.mesh.bounds;
            if (part.kind == Kind.Stump)
            {
                go.layer = source.filter.gameObject.layer;
                var box = go.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = bounds.size;
                rubble.Add(box); stumps.Add(box);
                continue;
            }
            if (part.kind == Kind.Stone)
            {
                bodies.Add(Loose(go, bounds, origin, height, toPlayer, .7f));
                stones.Add(go);
                continue;
            }
            // A lump comes down in free fall, thrown outward a little (the higher up, the faster, so the top topples
            // outward), tumbling; what would go the player's way drops nearly straight. One over the stump breaks up
            // on it, the others on the floor.
            go.layer = DebrisLayer;
            var at = go.transform.position;
            var outward = at - origin; outward.y = 0f;
            float off = outward.magnitude;
            outward = off > .02f ? outward / off : Quaternion.Euler(0f, Roll() * 360f, 0f) * Vector3.forward;
            bool atPlayer = toPlayer != Vector3.zero && Vector3.Dot(outward, toPlayer) > .1f;
            float up = Mathf.Clamp01((at.y - origin.y) / Mathf.Max(.5f, height));
            var velocity = outward * burst * (.35f + .65f * up) * (.6f + .8f * Roll()) * (atPlayer ? .25f : 1f) + Vector3.up * (atPlayer ? 0f : Roll() * .8f * up);
            float reach = off + new Vector2(velocity.x, velocity.z).magnitude * Mathf.Sqrt(2f * Mathf.Max(0f, at.y - origin.y) / 9.81f);
            crumbs.Add(new Crumb
            {
                piece = go.transform, velocity = velocity, spin = new Vector3(Roll() - .5f, Roll() - .5f, Roll() - .5f) * 320f,
                land = origin.y + (reach < .75f ? stumpHeight * .6f : 0f), half = Mathf.Min(bounds.extents.x, Mathf.Min(bounds.extents.y, bounds.extents.z)),
                at = Time.time + 1.6f, from = -1f, falls = true,
            });
        }
        // The pillar itself is gone (its collider with it).
        foreach (var source in sources)
        {
            if (source.filter == null) continue;
            if (source.filter.gameObject != gameObject) Destroy(source.filter.gameObject);
            else { foreach (var c in GetComponents<Collider>()) Destroy(c); Destroy(source.renderer); Destroy(source.filter); }
        }

        // The stones fall past the stump, not onto it: its colliders are boxes round its columns, taller than the stone
        // where a corner is chipped off, and a stone that came to rest on one lay in the air. One that ends up inside
        // the stump is taken away when they settle (it can't be seen there).
        foreach (var body in bodies)
            if (body != null && body.TryGetComponent<Collider>(out var stone))
                foreach (var stump in stumps) Physics.IgnoreCollision(stone, stump, true);
        stumpBoxes.Clear();
        foreach (var stump in stumps) stumpBoxes.Add(stump.bounds);

        if (torch != null) DropTorch(bodies, origin);
        Fall(bodies);
        Effects(origin, Mathf.Max(height, 1f), colour);
        StartCoroutine(Settle(bodies));
    }

    // One piece of the broken pillar under the holder: its mesh about its own middle, drawn as the pillar was.
    GameObject Piece(Part part, List<Material> materials, MeshRenderer like)
    {
        var go = new GameObject(part.kind.ToString());
        go.transform.SetParent(holder, false);
        go.transform.localPosition = part.centre;
        go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
        var r = go.AddComponent<MeshRenderer>();
        var its = new Material[part.used.Length];
        for (int i = 0; i < its.Length; i++) its[i] = materials[Mathf.Min(part.used[i], materials.Count - 1)];
        r.sharedMaterials = its;
        r.shadowCastingMode = like.shadowCastingMode; r.receiveShadows = like.receiveShadows;
        r.renderingLayerMask = like.renderingLayerMask; r.lightProbeUsage = like.lightProbeUsage;
        return go;
    }

    // A piece as a loose body: thrown outward from the pillar's middle (the higher up, the faster, so the top topples),
    // turned aside when that is at the player, tumbling.
    Rigidbody Loose(GameObject go, Bounds bounds, Vector3 origin, float height, Vector3 toPlayer, float speed)
    {
        go.layer = DebrisLayer;
        var box = go.AddComponent<BoxCollider>();
        box.center = bounds.center; box.size = Vector3.Max(bounds.size * .8f, Vector3.one * .05f);
        rubble.Add(box);
        var body = go.AddComponent<Rigidbody>();
        body.mass = Mathf.Max(2f, bounds.size.x * bounds.size.y * bounds.size.z * 1800f);
        body.maxDepenetrationVelocity = 1.5f;
        var at = go.transform.position;
        var outward = at - origin; outward.y = 0f;
        outward = outward.sqrMagnitude > .0004f ? outward.normalized : Quaternion.Euler(0f, Roll() * 360f, 0f) * Vector3.forward;
        float toward = Vector3.Dot(outward, toPlayer);
        if (toward > .35f)
        {
            var side = Vector3.Cross(Vector3.up, toPlayer);
            outward = (outward - toPlayer * toward + side * (Vector3.Dot(outward, side) >= 0f ? .6f : -.6f)).normalized;
        }
        float up = Mathf.Clamp01((at.y - origin.y) / Mathf.Max(.5f, height));
        body.linearVelocity = outward * burst * speed * (.35f + .65f * up) * (.6f + .8f * Roll()) + Vector3.up * (.3f + Roll() * 1f) * up;
        body.angularVelocity = new Vector3(Roll() - .5f, Roll() - .5f, Roll() - .5f) * 7f;
        return body;
    }

    static int DebrisLayer => 2;   // Ignore Raycast, as the breakables' debris: no aim, hit or use ray stops on it

    // The torch: its fire goes out at once, its bracket drops at the stump's foot as one more loose piece.
    void DropTorch(List<Rigidbody> bodies, Vector3 origin)
    {
        foreach (var l in torch.GetComponentsInChildren<Light>(true)) Destroy(l.gameObject);
        foreach (var ps in torch.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // no flame left hanging where the torch was
        }
        foreach (var filter in torch.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.GetComponent<ParticleSystem>() != null || filter.sharedMesh == null) continue;
            var go = filter.gameObject;
            go.transform.SetParent(holder, true);
            foreach (var c in go.GetComponents<Collider>()) Destroy(c);
            go.layer = DebrisLayer;
            var bounds = filter.sharedMesh.bounds;
            var box = go.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = Vector3.Max(bounds.size * .9f, Vector3.one * .05f);
            rubble.Add(box);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 3f; body.maxDepenetrationVelocity = 1.5f;
            var outward = go.transform.position - origin; outward.y = 0f;
            outward = outward.sqrMagnitude > .0004f ? outward.normalized : Vector3.forward;
            // Let go already tipped over, clear of the stump, and dropped with no push: it lands flat at the stump's
            // foot. Left upright it stood there by itself; tipped by a spin it tumbled three metres across the stage.
            go.transform.rotation = Quaternion.AngleAxis(78f + Roll() * 10f, Vector3.Cross(Vector3.up, outward)) * Quaternion.AngleAxis((Roll() - .5f) * 50f, Vector3.up) * go.transform.rotation;
            go.transform.position += outward * .45f;
            body.linearDamping = 1.2f; body.angularDamping = 4f;
            // It falls on its own: the lumps coming down round it are a hundred times its weight and sent it across the stage.
            foreach (var other in bodies) if (other != null && other.TryGetComponent<Collider>(out var stone)) Physics.IgnoreCollision(box, stone, true);
            foreach (var crumb in crumbs) if (crumb.piece != null && crumb.piece.TryGetComponent<Collider>(out var lump)) Physics.IgnoreCollision(box, lump, true);
            bodies.Add(body);
            stones.Add(go); kept.Add(go);
        }
        // What is left of the torch (its empty holders) goes with it.
        Destroy(torch);
    }

    // The loose pieces pass through the players and the living (nobody is shoved or boxed in by falling stone), and
    // through the stage's invisible edges, which only keep walkers in: a stone thrown over the edge falls off the stage.
    void Fall(List<Rigidbody> bodies)
    {
        var all = new List<Rigidbody>(bodies);
        foreach (var c in crumbs) if (c.piece != null && c.piece.TryGetComponent<Rigidbody>(out var body)) all.Add(body);
        DeathPhysics.IgnorePlayers(all);
        var dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon == null) return;
        foreach (var wall in dungeon.GetComponentsInChildren<InvisibleWall>())
            foreach (var edge in wall.GetComponents<Collider>())
                foreach (var body in all)
                    if (body != null && body.TryGetComponent<Collider>(out var c)) Physics.IgnoreCollision(edge, c, true);
    }

    void Effects(Vector3 origin, float height, Color colour)
    {
        // The pillar's own dust, once: low and stone-dark, rolling out from its foot as the lumps land in it, with grit
        // thrown off the breaking stone. (The breakables' pale puff, up the whole pillar and at every lump, was the
        // brightest thing on the dark stage: cream squares four metres up, like confetti.)
        if (dust != null) Destroy(Instantiate(dust, origin, Quaternion.identity, holder), 6f);
        if (AudioManager.HasInstance)
        {
            if (breakSound != null) AudioManager.Instance.PlaySFX(breakSound, origin + Vector3.up, breakVolume, .06f);
            if (rubbleSound != null) StartCoroutine(Later(rubbleSoundDelay, () => { if (AudioManager.HasInstance) AudioManager.Instance.PlaySFX(rubbleSound, origin, rubbleVolume, .06f); }));
        }
        if (shake > 0f)
        {
            var impulse = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            if (impulse == null) impulse = gameObject.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            impulse.GenerateImpulseWithForce(shake);
        }
        if (flash > 0f) StartCoroutine(Flash(lastHit != Vector3.zero ? lastHit : origin + Vector3.up * height * .7f, colour));
    }

    IEnumerator Later(float seconds, System.Action then)
    {
        yield return new WaitForSeconds(seconds);
        then();
    }

    IEnumerator Flash(Vector3 at, Color colour)
    {
        var go = new GameObject("Pillar break flash");
        go.transform.SetParent(holder, false);
        go.transform.position = at + Vector3.up * .2f;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.shadows = LightShadows.None; l.range = 7f;
        l.color = Normalised(colour);
        for (float t = 0f; t < flashSeconds; t += Time.deltaTime)
        {
            float f = 1f - t / flashSeconds;
            l.intensity = flash * f * f;
            yield return null;
        }
        Destroy(go);
    }

    // The stones lie still after a moment: frozen where they are, their colliders off (nothing walks into rubble),
    // casting no shadow, as low props don't. One that rolled too far, or fell off the stage, is taken away.
    IEnumerator Settle(List<Rigidbody> bodies)
    {
        yield return new WaitForSeconds(2.5f);
        for (float t = 0f; t < 6f; t += .5f)
        {
            bool moving = false;
            foreach (var body in bodies) if (body != null && !body.IsSleeping() && body.linearVelocity.sqrMagnitude > .01f) moving = true;
            if (!moving) break;
            yield return new WaitForSeconds(.5f);
        }
        var origin = transform.position;
        foreach (var body in bodies)
        {
            if (body == null) continue;
            var go = body.gameObject;
            var flat = go.transform.position - origin; flat.y = 0f;
            foreach (var c in go.GetComponents<Collider>()) { rubble.Remove(c); Destroy(c); }
            Destroy(body);
            bool inStump = false;
            if (!kept.Contains(go)) foreach (var box in stumpBoxes) if (box.Contains(go.transform.position)) inStump = true;
            if (inStump) { stones.Remove(go); Destroy(go); continue; }
            if (go.transform.position.y < origin.y - .5f || flat.magnitude > keepWithin && !kept.Contains(go))
            {
                stones.Remove(go);
                crumbs.Add(new Crumb { piece = go.transform, at = Time.time + Random.value * .6f, from = -1f }); enabled = true;
                continue;
            }
            if (go.TryGetComponent<MeshRenderer>(out var r) && r.bounds.size.y < .4f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
