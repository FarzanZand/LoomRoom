using UnityEngine;

// How a furnishing or wall decor piece sits in a room taller than the one tile it was made for, read by the generator:
// a hanging piece (a chandelier, chains, a beam) is lifted with the ceiling, a full-height piece (a pilaster, a long
// banner) is stacked up the wall to it. A piece flat against its wall casts no shadow.
public class PropMount : MonoBehaviour
{
    public enum Kind { Standing = 0, HangsFromCeiling = 1, FullHeight = 2 }

    [Tooltip("Standing: stays on the floor. Hangs From Ceiling: lifted with a taller room's ceiling. Full Height: made for one tile of wall and stacked up a taller room's wall.")]
    public Kind kind;
    [Tooltip("Flat against its wall (a banner, a crest, chains): it casts no shadow, which would only be a few texels on the wall behind it.")]
    public bool castsNoShadow;

    public static Kind KindOf(GameObject prefab) => prefab != null && prefab.TryGetComponent<PropMount>(out var mount) ? mount.kind : Kind.Standing;
}
