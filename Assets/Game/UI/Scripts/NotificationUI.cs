using DG.Tweening;
using TMPro;
using UnityEngine;

// One toast for short messages ("Inventory full", "Picked up Knife").
public class NotificationUI : Singleton<NotificationUI>
{
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] float fadeIn  = 0.12f;
    [SerializeField] float hold    = 1f;
    [SerializeField] float fadeOut = 0.35f;

    Sequence routine;

    protected override void Awake()
    {
        base.Awake();
        if (label != null) SetAlpha(0f);
    }

    public static void Show(string message)
    {
        if (HasInstance) Instance.ShowMessage(message);
        else Debug.Log($"[Notification] {message}");
    }

    public void ShowMessage(string message)
    {
        if (label == null) return;
        label.text = message;
        routine?.Kill();
        SetAlpha(0);
        routine=DOTween.Sequence().SetUpdate(true).Append(label.DOFade(1,fadeIn))
            .AppendInterval(hold).Append(label.DOFade(0,fadeOut));
    }

    void OnDisable(){routine?.Kill();if(label!=null)SetAlpha(0);}

    void SetAlpha(float a)
    {
        var c = label.color; c.a = a; label.color = c;
    }
}
