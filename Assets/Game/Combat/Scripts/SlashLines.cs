using System.Collections.Generic;
using UnityEngine;

// Heathen's slash: a few thin white streaks along the arc the blade just cut, only while the hitbox is open
// and for a couple of frames after. Sits next to the player's Hitbox; the blade is the hitbox's capsule.
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

    Hitbox hitbox;
    CapsuleCollider blade;
    LineRenderer[] lines;
    readonly List<(float time, Vector3 grip, Vector3 tip)> samples = new();
    float lastActive = -10f;

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
        samples.RemoveAll(s => Time.time - s.time > trail + linger);
        if (!show || samples.Count < 2)
        {
            foreach (var l in lines) l.enabled = false;
            if (!show) samples.Clear();
            return;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            float f = Mathf.Lerp(bladeSpan.x, bladeSpan.y, lines.Length == 1 ? 1f : (float)i / (lines.Length - 1));
            var line = lines[i];
            line.positionCount = samples.Count;
            for (int k = 0; k < samples.Count; k++) line.SetPosition(k, Vector3.Lerp(samples[k].grip, samples[k].tip, f));
            line.enabled = true;
        }
    }

    void OnDisable()
    {
        samples.Clear();
        if (lines != null) foreach (var l in lines) if (l != null) l.enabled = false;
    }
}
