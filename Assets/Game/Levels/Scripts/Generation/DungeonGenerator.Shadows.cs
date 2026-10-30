using UnityEngine;
using UnityEngine.Rendering;

public partial class DungeonGenerator
{
    // Small props (lower than 40 cm and under 1.5 m across: candles, skulls, cogs, pebbles), pieces flat against a wall
    // (PropMount.castsNoShadow) and the upper copies of a stacked full-height piece cast no shadow: each is drawn again
    // into every shadow map for a shadow a few texels big. Characters and what they hold keep theirs. Sizes come from
    // the meshes, since a floor built in slices is switched off and its renderers report no bounds.
    void TrimShadows()
    {
        float tile = data.architectureTileSize;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var r)) continue;
            if (r.shadowCastingMode == ShadowCastingMode.Off || r.GetComponentInParent<Character>(true) != null) continue;
            var mount = r.GetComponentInParent<PropMount>(true);
            bool off = mount != null && (mount.castsNoShadow || (mount.kind == PropMount.Kind.FullHeight
                && mount.transform.position.y - Cell(CellOf(mount.transform.position)).y >= tile * .9f));
            if (!off)
            {
                var size = WorldSize(filter);
                off = size.y < .4f && Mathf.Max(size.x, size.z) < 1.5f;
            }
            if (off) r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // The world-space size of a mesh's box as it stands.
    static Vector3 WorldSize(MeshFilter filter)
    {
        var e = filter.sharedMesh.bounds.extents;
        var m = filter.transform.localToWorldMatrix;
        Vector3 x = m.MultiplyVector(new Vector3(e.x, 0, 0)), y = m.MultiplyVector(new Vector3(0, e.y, 0)), z = m.MultiplyVector(new Vector3(0, 0, e.z));
        return 2f * new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
    }
}
