using Sirenix.OdinInspector;
using UnityEngine;

public enum DungeonSocketType
{
    Enemy     = 0,   // a normal enemy from the room/floor pool (or the override prefab)
    Chest     = 1,   // the level's supply chest
    Breakable = 2,   // a destructible from the level/theme list (or the override prefab)
    Boss      = 3,   // the milestone boss
    Merchant  = 4,   // the merchant, when this floor rolled one
    Feature   = 5,   // a random feature from the level/theme list (or the override prefab)
}

// A placement point inside a room template.
public class DungeonSocket : MonoBehaviour
{
    public DungeonSocketType type = DungeonSocketType.Enemy;
    [Range(0, 1)] public float chance = 1f;
    [AssetsOnly, Tooltip("Spawn this instead of the pool's pick.")]
    public GameObject overridePrefab;

    void OnDrawGizmos()
    {
        Gizmos.color = type switch
        {
            DungeonSocketType.Enemy => new Color(1f, .3f, .25f),
            DungeonSocketType.Boss => new Color(.8f, .1f, .9f),
            DungeonSocketType.Chest => new Color(1f, .85f, .3f),
            DungeonSocketType.Merchant => new Color(.3f, 1f, .5f),
            DungeonSocketType.Feature => new Color(.4f, .7f, 1f),
            _ => new Color(.7f, .55f, .4f),
        };
        float r = type == DungeonSocketType.Boss ? .7f : .35f;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * r, r);
        Gizmos.DrawLine(transform.position + Vector3.up * r, transform.position + Vector3.up * r + transform.forward * r * 1.6f);
    }
}
