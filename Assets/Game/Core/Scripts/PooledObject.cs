using UnityEngine;

// Added automatically to pooled instances. Remembers where the object came from.
[DisallowMultipleComponent]
public class PooledObject : MonoBehaviour
{
    public GameObject Source { get; set; }
    public int Generation { get; set; }
    public bool InPool { get; set; }   // sitting in the pool: a second Release must not add it twice
    ParticleSystem[] particles;
    public ParticleSystem[] Particles => particles ??= GetComponentsInChildren<ParticleSystem>(true);
}
