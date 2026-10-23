using UnityEngine;
using UnityEngine.UI;

// Resolution-independent eye artwork. Shape and stroke settings are authored on the prefab.
[RequireComponent(typeof(CanvasRenderer))]
public class StealthEyeGraphic : MaskableGraphic
{
    [SerializeField] AnimationCurve lidShape = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 1), new Keyframe(1, 0));
    [SerializeField, Range(0, 1)] float openness;
    [SerializeField, Min(.1f)] float strokeWidth = 1.5f;
    [SerializeField, Min(0)] float outlineWidth = 1;
    [SerializeField] Color outlineColor = new Color(.03f, .035f, .04f, .85f);
    [SerializeField, Range(0, .5f)] float pupilRadius = .18f;
    [SerializeField, Range(0, .5f)] float closedCurveDepth = .12f;
    [SerializeField, Range(8, 64)] int segments = 32;
    public float Openness { get => openness; set { openness = Mathf.Clamp01(value); SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float halfHeight = r.height * .5f;
        for (int pass = 0; pass < 2; pass++)
        {
            float width = strokeWidth + (pass == 0 ? outlineWidth * 2 : 0);
            Color tint = pass == 0 ? outlineColor : color;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1f) / segments;
                Vector2 Lid(float t, bool upper)
                {
                    float shape = lidShape.Evaluate(t);
                    float y = upper ? Mathf.Lerp(-closedCurveDepth, 1, openness) : Mathf.Lerp(-closedCurveDepth, -.7f, openness);
                    return new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.center.y + shape * halfHeight * y);
                }
                Line(vh, Lid(a, true), Lid(b, true), width, tint);
                if (openness > .01f) Line(vh, Lid(a, false), Lid(b, false), width, tint);
                if (openness > .01f)
                {
                    float radius = r.height * pupilRadius;
                    Vector2 Pupil(float t) => r.center + new Vector2(Mathf.Cos(t * Mathf.PI * 2) * radius, Mathf.Sin(t * Mathf.PI * 2) * radius * openness);
                    Line(vh, Pupil(a), Pupil(b), width * openness, tint);
                }
            }
        }
    }
    static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        int start = vh.currentVertCount;
        vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
        vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}
