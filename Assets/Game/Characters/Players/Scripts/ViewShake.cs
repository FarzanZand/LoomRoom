using UnityEngine;

// A wobble of the player's view: it rolls, pitches and turns a little back and forth, fast, dying away over its seconds
// (a blow on the world felt in the eyes; sliding the camera alone barely shows against walls metres away). Read by
// SwingCameraMotion on the player camera, added to its finished orientation. The strongest running wobble wins.
public static class ViewShake
{
    static float strength, seconds, startedAt = -10f, seed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { strength = 0f; startedAt = -10f; }

    // degrees: the first swing's size (roll; pitch and turn smaller).
    public static void Add(float degrees, float length)
    {
        if (degrees <= 0f) return;
        if (Current > degrees) return;
        strength = degrees; seconds = Mathf.Max(.1f, length); startedAt = Time.time; seed = Random.value * 10f;
    }

    // How big the running wobble still is (degrees).
    static float Current
    {
        get
        {
            float t = Time.time - startedAt;
            return t < 0f || t >= seconds ? 0f : strength * Decay(t / seconds);
        }
    }

    static float Decay(float n) => (1f - n) * (1f - n);

    // Pitch, turn and roll (degrees) now.
    public static Vector3 Rotation
    {
        get
        {
            float t = Time.time - startedAt;
            if (t < 0f || t >= seconds || strength <= 0f) return Vector3.zero;
            float a = strength * Decay(t / seconds);
            const float Hz = 11f;
            float w = t * Hz * Mathf.PI * 2f;
            return new Vector3(Mathf.Sin(w * .83f + seed) * a * .5f, Mathf.Sin(w * .61f + seed * 1.7f) * a * .35f, Mathf.Sin(w + seed * .3f) * a);
        }
    }
}
