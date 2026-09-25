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
    static UIFeedbackSettings cached;
    public static UIFeedbackSettings Shared=>cached!=null?cached:cached=Resources.Load<UIFeedbackSettings>("UIFeedback");
    static float lastHover;
    public void Play(string key){if(!string.IsNullOrEmpty(key) && AudioManager.HasInstance)AudioManager.Instance.PlayUI(key);}
    public void Hover(){if(Time.unscaledTime-lastHover<.08f)return;lastHover=Time.unscaledTime;Play(hoverKey);}
}
