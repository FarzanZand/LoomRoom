using System;
using UnityEngine;

// Fire-and-forget "something made a sound here". Footsteps, combat and item effects
// report; Perception listens. Radius is how far the sound carries on its own.
public static class NoiseEvents
{
    public static event Action<Vector3, float, Character> Noise;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Noise = null;

    public static void Report(Vector3 position, float radius, Character source = null)
    {
        if (radius <= 0f) return;
        Noise?.Invoke(position, radius, source);
    }
}
