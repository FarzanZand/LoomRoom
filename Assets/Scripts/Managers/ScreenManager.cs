using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScreenManager : Singleton<ScreenManager>
{
    public Image fadeFullscreenImage;

    private Coroutine fadeRoutine;

    protected override void Awake()
    {
        base.Awake();
        SetAlpha(0f);
    }

    /// Starts invisible, waits holdDuration, then fades to fully visible over fadeDuration.
    public void FadeIn(float fadeDuration, float holdDuration = 0f)
        => RunFade(from: 0f, to: 1f, fadeDuration, holdDuration);

    /// Starts fully visible, waits holdDuration, then fades to invisible over fadeDuration.
    public void FadeOut(float fadeDuration, float holdDuration = 0f)
        => RunFade(from: 1f, to: 0f, fadeDuration, holdDuration);

    /// Fades to black over fadeInDuration, holds for fadedDuration, then fades back over fadeOutDuration.
    public void FadeInOut(float fadeInDuration, float fadedDuration, float fadeOutDuration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeInOutRoutine(fadeInDuration, fadedDuration, fadeOutDuration));
    }

    IEnumerator FadeInOutRoutine(float fadeInDuration, float fadedDuration, float fadeOutDuration)
    {
        yield return LerpAlpha(0f, 1f, fadeInDuration);
        if (fadedDuration > 0f) yield return new WaitForSecondsRealtime(fadedDuration);
        yield return LerpAlpha(1f, 0f, fadeOutDuration);
        fadeRoutine = null;
    }

    void RunFade(float from, float to, float fadeDuration, float holdDuration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(from, to, fadeDuration, holdDuration));
    }

    IEnumerator FadeRoutine(float from, float to, float fadeDuration, float holdDuration)
    {
        SetAlpha(from);

        if (holdDuration > 0f)
            yield return new WaitForSecondsRealtime(holdDuration);

        yield return LerpAlpha(from, to, fadeDuration);
        fadeRoutine = null;
    }

    IEnumerator LerpAlpha(float from, float to, float duration)
    {
        SetAlpha(from);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetAlpha(to);
    }

    void SetAlpha(float alpha)
    {
        if (fadeFullscreenImage == null) return;
        bool visible = alpha > 0f;
        fadeFullscreenImage.gameObject.SetActive(visible);
        if (visible)
        {
            var c = fadeFullscreenImage.color;
            c.a = alpha;
            fadeFullscreenImage.color = c;
        }
    }
}
