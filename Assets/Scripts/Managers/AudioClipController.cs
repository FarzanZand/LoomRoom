using UnityEngine;

public class AudioClipController : MonoBehaviour
{
    public enum Channel { SFX, SFX2D, Music, UI }

    [SerializeField] Channel channel = Channel.SFX2D;
    [SerializeField] AudioData audioData;
    [SerializeField] AudioClip audioClip;
    [Range(0f, 1f)]
    [SerializeField] float clipVolume = 1f;

    public void Play()
    {
        if (audioData != null) PlayData(audioData);
        else if (audioClip != null) PlayClip(audioClip);
    }

    // Called from Animation Events — drag an AudioData asset into the Object field
    public void Play(AudioData data) => PlayData(data);

    // Called from Animation Events — drag an AudioClip into the Object field
    public void Play(AudioClip clip) => PlayClip(clip);

    // Animation Events pass Object; Unity resolves to the correct overload above, 
    // but if it can't, this catches it and routes by type.
    public void Play(UnityEngine.Object obj)
    {
        if (obj is AudioData data) PlayData(data);
        else if (obj is AudioClip clip) PlayClip(clip);
    }

    void PlayData(AudioData data)
    {
        if (data == null) return;
        var am = AudioManager.Instance;
        if (am == null) return;
        switch (channel)
        {
            case Channel.SFX:   am.PlaySFXData(data, transform.position); break;
            case Channel.SFX2D: am.PlaySFXData2D(data);                   break;
            case Channel.Music: am.PlayMusicData(data);                   break;
            case Channel.UI:    am.PlayUIData(data);                      break;
        }
    }

    void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        var am = AudioManager.Instance;
        if (am == null) return;
        switch (channel)
        {
            case Channel.SFX:   am.PlaySFX(clip, transform.position, clipVolume); break;
            case Channel.SFX2D: am.PlaySFX2D(clip, clipVolume);                   break;
            case Channel.Music: am.PlayMusic(clip);                                break;
            case Channel.UI:    am.PlayUI(clip, clipVolume);                       break;
        }
    }
}
