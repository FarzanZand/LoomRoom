using UnityEngine;

// A collider that only keeps walkers in (the boss stage's edges, a board's rim): spells and arrows fly through it.
public class InvisibleWall : MonoBehaviour
{
    public static bool Is(Collider collider) => collider != null && collider.GetComponent<InvisibleWall>() != null;
}
