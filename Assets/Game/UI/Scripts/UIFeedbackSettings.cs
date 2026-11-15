using DG.Tweening;
using UnityEngine;

[CreateAssetMenu(menuName="UI/Feedback settings")]
public class UIFeedbackSettings : ScriptableObject
{
    [Range(.08f,.6f)] public float panelDuration=.22f;
    public Ease panelEase=Ease.OutCubic;
    [Range(1,1.2f)] public float hoverScale=1.035f,pressScale=1.07f;
    [Range(.03f,.3f)] public float hoverDuration=.1f;
    [Header("AudioManager UI Library keys")]
    public string openKey="inventoryOpen", closeKey="inventoryClose", hoverKey="uiHover", equipKey="equipItem", invalidKey="uiUnavailable";
    [Tooltip("Played when a button is pressed (mouse or Submit), unless the button's own action plays a UI sound in that frame.")]
    public string clickKey="buttonClick";
    [Header("Where there is no AudioManager (the main menu)")]
    [Tooltip("The UI Library's uiHover clip and volume, for scenes without the AudioManager.")] public AudioClip hoverClip;
    [Range(0f,1f)] public float hoverVolume=.15f;
    [Tooltip("The UI Library's buttonClick clip and volume, for scenes without the AudioManager.")] public AudioClip clickClip;
    [Range(0f,1f)] public float clickVolume=.7f;
    static UIFeedbackSettings cached;
    public static UIFeedbackSettings Shared=>cached!=null?cached:cached=Resources.Load<UIFeedbackSettings>("UIFeedback");
    static float lastHover = -1f;
    // Time starts at 0 each Play session (domain reload is off): a stale stamp would silence hover sounds.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHover() => lastHover = -1f;
    public void Play(string key){if(string.IsNullOrEmpty(key))return;if(AudioManager.HasInstance)AudioManager.Instance.PlayUI(key);else PlayLoose(key);}
    public void Hover(){if(Time.unscaledTime-lastHover<.08f)return;lastHover=Time.unscaledTime;Play(hoverKey);}
    // A button pressed: its click, left out when the button's own action plays a UI sound in the same frame (the
    // AudioManager decides at the end of the frame), so a trade or a level-up chime is never doubled by it.
    public void Click(){if(string.IsNullOrEmpty(clickKey))return;if(AudioManager.HasInstance)AudioManager.Instance.PlayUIUnlessOther(clickKey);else PlayLoose(clickKey);}

    // The main menu has no AudioManager: hover and click play from a hidden 2D source at the remembered volumes.
    static AudioSource loose;
    void PlayLoose(string key)
    {
        AudioClip clip=key==hoverKey?hoverClip:key==clickKey?clickClip:null;
        float volume=key==hoverKey?hoverVolume:clickVolume;
        if(clip==null)return;
        if(loose==null){var go=new GameObject("UI sounds (no AudioManager)"){hideFlags=HideFlags.HideAndDontSave};loose=go.AddComponent<AudioSource>();loose.playOnAwake=false;loose.spatialBlend=0f;loose.ignoreListenerPause=true;}
        loose.PlayOneShot(clip,volume*AudioManager.StoredVolume(AudioManager.Channel.Master)*AudioManager.StoredVolume(AudioManager.Channel.UI));
    }
}
