using System.Collections.Generic;
using UnityEngine;

// Heathen's slash: a pale band swept by the blade (the ribbon, from mid-blade to the tip over the last tenth of a
// second, brightest at the tip and fading with age) plus a few thin white streaks along the arc, only while the
// hitbox is open and for a moment after. Sits next to the player's Hitbox; the blade is the hitbox's capsule.
[RequireComponent(typeof(Hitbox))]
public class SlashLines : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField, Range(1, 8)] int lineCount = 4;
    [Tooltip("Where the streaks run along the blade, from the grip (0) to the tip (1).")]
    [SerializeField] Vector2 bladeSpan = new(.45f, 1f);
    [SerializeField, Min(.001f)] float width = .012f;
    [Tooltip("Seconds of the arc each streak covers.")]
    [SerializeField, Min(.01f)] float trail = .045f;
    [Tooltip("Seconds the streaks stay after the hitbox closes.")]
    [SerializeField, Min(0)] float linger = .07f;
    [SerializeField] Color color = new(1f, 1f, 1f, .9f);
    [Header("Ribbon")]
    [SerializeField, Tooltip("Off: the user found the smooth band weird next to the pixel art (it hid the blade). Kept for a pixel-art version.")] bool ribbon = false;
    [Tooltip("Where the band runs along the blade, from the grip (0) to the tip (1).")]
    [SerializeField] Vector2 ribbonSpan = new(.2f, 1.05f);
    [Tooltip("Seconds of the arc the band covers.")]
    [SerializeField, Min(.01f)] float ribbonTrail = .17f;
    [SerializeField] Color ribbonColor = new(1f, .97f, .9f, .55f);

    Hitbox hitbox;
    CapsuleCollider blade;
    LineRenderer[] lines;
    readonly List<(float time, Vector3 grip, Vector3 tip)> samples = new();
    float lastActive = -10f;
    Mesh ribbonMesh;
    MeshRenderer ribbonRenderer;
    readonly List<Vector3> ribbonVerts = new();
    readonly List<Color> ribbonColors = new();
    readonly List<int> ribbonTris = new();

    void Awake()
    {
        hitbox = GetComponent<Hitbox>();
        blade = GetComponentInChildren<CapsuleCollider>(true);
        lines = new LineRenderer[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            var go = new GameObject("Slash line " + i) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 0;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            line.widthMultiplier = width * Random.Range(.7f, 1.2f);
            var tail = color; tail.a = 0f;
            line.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 1f) },
            };
            line.enabled = false;
            lines[i] = line;
        }
        if (ribbon)
        {
            var go = new GameObject("Slash ribbon", typeof(MeshFilter), typeof(MeshRenderer)) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            ribbonMesh = new Mesh { name = "Slash ribbon" }; ribbonMesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = ribbonMesh;
            ribbonRenderer = go.GetComponent<MeshRenderer>();
            ribbonRenderer.sharedMaterial = material;
            ribbonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbonRenderer.receiveShadows = false;
            ribbonRenderer.enabled = false;
        }
    }

    // The band: two rows of vertices per sample, in world space (the ribbon object stays at the origin of its parent,
    // so vertices are put into its local space).
    void DrawRibbon()
    {
        if (ribbonRenderer == null) return;
        ribbonVerts.Clear(); ribbonColors.Clear(); ribbonTris.Clear();
        var t = ribbonRenderer.transform;
        float now = Time.time;
        for (int k = 0; k < samples.Count; k++)
        {
            float age = now - samples[k].time;
            if (age > ribbonTrail + linger) continue;
            float fade = 1f - Mathf.Clamp01(age / ribbonTrail);
            var tip = ribbonColor; tip.a *= fade;
            var root = ribbonColor; root.a = 0f;
            ribbonVerts.Add(t.InverseTransformPoint(Vector3.LerpUnclamped(samples[k].grip, samples[k].tip, ribbonSpan.x)));
            ribbonVerts.Add(t.InverseTransformPoint(Vector3.LerpUnclamped(samples[k].grip, samples[k].tip, ribbonSpan.y)));
            ribbonColors.Add(root); ribbonColors.Add(tip);
        }
        int rows = ribbonVerts.Count / 2;
        for (int k = 0; k + 1 < rows; k++)
        {
            int a = k * 2;
            ribbonTris.Add(a); ribbonTris.Add(a + 1); ribbonTris.Add(a + 3);
            ribbonTris.Add(a); ribbonTris.Add(a + 3); ribbonTris.Add(a + 2);   // the material draws both faces
        }
        ribbonMesh.Clear();
        if (rows < 2) { ribbonRenderer.enabled = false; return; }
        ribbonMesh.SetVertices(ribbonVerts); ribbonMesh.SetColors(ribbonColors); ribbonMesh.SetTriangles(ribbonTris, 0);
        ribbonMesh.RecalculateBounds();
        ribbonRenderer.enabled = true;
    }

    void LateUpdate()
    {
        if (blade == null || material == null) return;
        if (hitbox.Active) lastActive = Time.time;
        bool show = Time.time - lastActive <= linger;
        if (hitbox.Active)
        {
            // The capsule's axis is the blade.
            var t = blade.transform;
            Vector3 axis = blade.direction == 0 ? Vector3.right : blade.direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 a = t.TransformPoint(blade.center - axis * blade.height * .5f), b = t.TransformPoint(blade.center + axis * blade.height * .5f);
            samples.Add((Time.time, a, b));
        }
        float keep = Mathf.Max(trail, ribbon ? ribbonTrail : 0f) + linger;
        int stale = 0;   // oldest first: drop them from the front, without a delegate every frame
        while (stale < samples.Count && Time.time - samples[stale].time > keep) stale++;
        if (stale > 0) samples.RemoveRange(0, stale);
        if (!show || samples.Count < 2)
        {
            foreach (var l in lines) l.enabled = false;
            if (ribbonRenderer != null) ribbonRenderer.enabled = false;
            if (!show) samples.Clear();
            return;
        }
        DrawRibbon();
        // The streaks cover only the newest part of the arc.
        int first = samples.FindIndex(s => Time.time - s.time <= trail + linger);
        if (first < 0) first = samples.Count;
        int count = samples.Count - first;
        for (int i = 0; i < lines.Length; i++)
        {
            float f = Mathf.Lerp(bladeSpan.x, bladeSpan.y, lines.Length == 1 ? 1f : (float)i / (lines.Length - 1));
            var line = lines[i];
            line.positionCount = count;
            for (int k = 0; k < count; k++) line.SetPosition(k, Vector3.Lerp(samples[first + k].grip, samples[first + k].tip, f));
            line.enabled = count >= 2;
        }
    }

    void OnDestroy() { if (ribbonMesh != null) Destroy(ribbonMesh); }

    void OnDisable()
    {
        samples.Clear();
        if (lines != null) foreach (var l in lines) if (l != null) l.enabled = false;
        if (ribbonRenderer != null) ribbonRenderer.enabled = false;
    }
}
