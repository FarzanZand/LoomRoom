using Sirenix.OdinInspector;
using UnityEngine;

// Drop into a room template (Characters/Enemies/Enemy Spawner, or the template's Add enemy spawner button): one enemy
// stands here when the room is built. Enemy empty = a pick from the room's enemies, else the biome's; set = that enemy.
// It counts toward the room's pack and marks a wave point in an ambush room, like an Enemy socket (it becomes one).
public class EnemySpawner : MonoBehaviour
{
    [AssetsOnly, Tooltip("The enemy to put here. Empty: one from the room's enemies, else the current biome's.")]
    public GameObject enemy;

    void OnDrawGizmos()
    {
        Gizmos.color = enemy != null ? new Color(1f, .55f, .2f) : new Color(1f, .3f, .25f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .5f, .5f);
        Gizmos.DrawLine(transform.position + Vector3.up * .5f, transform.position + Vector3.up * .5f + transform.forward * .8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.3f, enemy != null ? enemy.name : "Enemy (biome)");
#endif
    }
}
