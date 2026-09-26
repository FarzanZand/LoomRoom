using UnityEngine;

// Added automatically to pooled instances. Remembers where the object came from.
[DisallowMultipleComponent]
public class PooledObject : MonoBehaviour
{
    public GameObject Source { get; set; }
    public int Generation { get; set; }
    ParticleSystem[] particles;
    public ParticleSystem[] Particles => particles ??= GetComponentsInChildren<ParticleSystem>(true);
}
