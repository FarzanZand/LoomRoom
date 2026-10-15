using TMPro;
using UnityEngine;

// One co-op ping in the world (CoopPing): a thin beam up from the spot and a label over it, in the pinging player's
// colour, both drawn over walls so the others find it; it keeps its size on screen and fades out at the end.
public class CoopPingMarker : MonoBehaviour
{
    static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");
    TextMeshPro label;
    LineRenderer beam;
    Color colour;
    float born, life;

    public static void Spawn(Vector3 at, string text, Color colour, float seconds)
    {
        var go = new GameObject("Ping");
        go.transform.position = at;
        var marker = go.AddComponent<CoopPingMarker>();
        marker.colour = colour; marker.life = seconds; marker.born = Time.time;
        marker.beam = go.AddComponent<LineRenderer>();
        marker.beam.useWorldSpace = true;
        marker.beam.positionCount = 2;
        marker.beam.SetPosition(0, at);
        marker.beam.SetPosition(1, at + Vector3.up * 1.4f);
        marker.beam.widthMultiplier = .035f;
        marker.beam.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 4000 };
        marker.beam.material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        marker.beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(go.transform, false);
        labelObject.transform.localPosition = Vector3.up * 1.6f;
        marker.label = labelObject.AddComponent<TextMeshPro>();
        marker.label.text = text;
        marker.label.fontSize = 2.2f;
        marker.label.alignment = TextAlignmentOptions.Center;
        marker.label.textWrappingMode = TextWrappingModes.NoWrap;
        marker.label.outlineWidth = .2f;
        var m = marker.label.fontMaterial;
        m.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4001;
        marker.Tint(1f);
        Destroy(go, seconds);
    }

    void Tint(float alpha)
    {
        var c = colour; c.a = alpha;
        if (label != null) label.color = c;
        if (beam != null) { beam.startColor = c; c.a *= .2f; beam.endColor = c; beam.material.color = colour; }
    }

    void LateUpdate()
    {
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam == null || label == null) return;
        var t = label.transform;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
        // The same size on screen however far, never smaller than it is close up.
        float far = Vector3.Distance(cam.transform.position, t.position) / 6f;
        t.localScale = Vector3.one * Mathf.Max(1f, far);
        float left = life - (Time.time - born);
        Tint(left < 1f ? Mathf.Clamp01(left) : 1f);
    }
}
