using System.Collections;
using UnityEngine;

public class HitStopManager : Singleton<HitStopManager>
{
    Coroutine current;

    public void Trigger(float duration)
    {
        if (current != null) StopCoroutine(current);
        current = StartCoroutine(Routine(duration));
    }

    IEnumerator Routine(float duration)
    {
        float prev = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = prev;
        current = null;
    }
}
