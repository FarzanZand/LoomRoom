using UnityEngine;

[CreateAssetMenu(fileName = "NewAudioData", menuName = "Audio/Audio Data")]
public class AudioData : ScriptableObject
{
    public AudioClip[] clips;
    [Range(0f, 1f)]  public float volume        = 1f;
    [Range(0f, 2f)]  public float pitch         = 1f;
    [Range(0f, 0.5f)] public float pitchVariance = 0f;

    public AudioClip GetClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }
}
