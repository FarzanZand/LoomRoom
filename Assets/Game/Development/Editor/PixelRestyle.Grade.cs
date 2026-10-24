using UnityEngine;

// The restyle's own per-texel grade for textures made outside Build (the room player's Meshy heads, which Build's
// re-unwrap scrambles): the same material kinds, grain, crease dirt and occlusion as every restyled character, so a
// head graded with it matches the body it sits on.
public static partial class PixelRestyle
{
    // baseC: the flat swatch colour under the texel; p and n in the model's own space (the body's mesh space for a head
    // on a body); ao from Occlusion; size: the model's bounds size (the body mesh's, so the grain matches the body's).
    public static Color GradeTexel(Color baseC, Vector3 p, Vector3 n, float ao, float size) => Shade(baseC, Classify(baseC), p, n, ao, size);

    // Occlusion against colliders on the probe layer (31), as Build measures it.
    public static float Occlusion(Vector3 p, Vector3 n, Vector3[] dirs, float dist) => AmbientOcclusion(p, n, dirs, dist);

    public const int GradeProbeLayer = ProbeLayer, GradeAoRays = AoRays;
}
