using UnityEngine;

[CreateAssetMenu(fileName = "NewAudioData", menuName = "Audio/Audio Data")]
public class AudioData : ScriptableObject
{
    public AudioClip[] clips;
    [Range(0f, 1f)]  public float volume        = 1f;
    [Range(0f, 2f)]  public float pitch         = 1f;
    [Range(0f, 0.5f)] public float pitchVariance = 0f;

    [Header("3D attenuation (world units)")]
    [Min(.01f)] public float minDistance = 1f;
    [Min(.01f)] public float maxDistance = 500f;

    // A random pitch offset in [-variance, variance]; 0 without variance. Shared by every pitch-varied sound.
    public static float PitchOffset(float variance) => variance > 0f ? Random.Range(-variance, variance) : 0f;

    // This asset's pitch with its variance applied.
    public float RandomPitch() => pitch + PitchOffset(pitchVariance);

    public AudioClip GetClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }
}
