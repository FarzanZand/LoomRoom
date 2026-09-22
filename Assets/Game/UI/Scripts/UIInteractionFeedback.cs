using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIInteractionFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
{
    public UIFeedbackSettings settings;
    Tween tween;
    Vector3 resting;
    bool hovered;
    void Awake()=>resting=transform.localScale;
    void Scale(float factor){var s=settings!=null?settings:UIFeedbackSettings.Shared;tween?.Kill();tween=transform.DOScale(resting*factor,s!=null?s.hoverDuration:.1f).SetUpdate(true);}
    public void OnPointerEnter(PointerEventData e){hovered=true;var s=settings!=null?settings:UIFeedbackSettings.Shared;s?.Hover();Scale(s!=null?s.hoverScale:1.035f);}
    public void OnPointerExit(PointerEventData e){hovered=false;Scale(1);}
    public void OnPointerDown(PointerEventData e){var s=settings!=null?settings:UIFeedbackSettings.Shared;Scale(s!=null?s.pressScale:1.07f);}
    public void OnPointerUp(PointerEventData e){var s=settings!=null?settings:UIFeedbackSettings.Shared;Scale(hovered?(s!=null?s.hoverScale:1.035f):1);}
    public void Pulse(){var s=settings!=null?settings:UIFeedbackSettings.Shared;tween?.Kill();tween=transform.DOScale(resting*(s!=null?s.pressScale:1.07f),.07f).SetLoops(2,LoopType.Yoyo).SetUpdate(true);}
    void OnDisable(){hovered=false;tween?.Kill();transform.localScale=resting;}
}
