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
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        // Restore to 1 explicitly: capturing the previous value freezes the game
        // permanently if Trigger fires again while a stop is already active
        // (it would capture 0 and "restore" to 0).
        Time.timeScale = 1f;
        current = null;
    }
}
