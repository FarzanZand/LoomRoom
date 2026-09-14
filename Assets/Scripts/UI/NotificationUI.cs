using System.Collections;
using TMPro;
using UnityEngine;

// One toast for short messages ("Inventory full", "Picked up Knife").
public class NotificationUI : Singleton<NotificationUI>
{
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] float fadeIn  = 0.12f;
    [SerializeField] float hold    = 1f;
    [SerializeField] float fadeOut = 0.35f;

    Coroutine routine;

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
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        float t = 0f;
        while (t < fadeIn)  { SetAlpha(t / fadeIn);       t += Time.unscaledDeltaTime; yield return null; }
        SetAlpha(1f);
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < fadeOut) { SetAlpha(1f - t / fadeOut); t += Time.unscaledDeltaTime; yield return null; }
        SetAlpha(0f);
        routine = null;
    }

    void SetAlpha(float a)
    {
        var c = label.color; c.a = a; label.color = c;
    }
}
