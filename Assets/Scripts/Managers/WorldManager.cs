using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;
    public Transform startPositionRoom;
    public Light directionalLight;

    public void Awake()
    {
        if (PlayerManager.Instance.startingPlayer == PlayerManager.ActivePlayer.RoomPlayer)
        {
            directionalLight.color = Color.black;
            directionalLight.intensity = 0.7f;
        }
    }
    
    public void Start()
    {
        if(!ProgressionManager.Instance.skipWakeUp)
            WakeUpReset();
    }

    public void WakeUpReset()
    {
        ScreenManager.Instance.FadeOut(2f, 2f);
        PlayerManager.Instance.roomPlayer.transform.SetPositionAndRotation(startPositionRoom.position, startPositionRoom.rotation);

        PlayerManager.Instance.SetRoomPlayerControllerEnabled(false);

        WakeUpDirector.stopped += OnWakeUpComplete;
        WakeUpDirector.Play();
        StartCoroutine(PlayGroundhogAudio());
    }

    void OnWakeUpComplete(PlayableDirector _)
    {
        WakeUpDirector.stopped -= OnWakeUpComplete;
        if (PlayerManager.Instance == null) return;
        PlayerManager.Instance.SetRoomPlayerControllerEnabled(true);
    }

    Coroutine lightFadeRoutine;

    public void FadeDirectionalLight(float from, float to, float duration)
    {
        if (lightFadeRoutine != null)
            StopCoroutine(lightFadeRoutine);
        lightFadeRoutine = StartCoroutine(FadeDirectionalLightRoutine(from, to, duration));
    }

    IEnumerator FadeDirectionalLightRoutine(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            directionalLight.intensity = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        directionalLight.intensity = to;
        lightFadeRoutine = null;
    }

    Coroutine lightColorRoutine;

    public void FadeDirectionalLightColor(Color to, float duration)
    {
        if (lightColorRoutine != null)
            StopCoroutine(lightColorRoutine);
        lightColorRoutine = StartCoroutine(FadeDirectionalLightColorRoutine(to, duration));
    }

    IEnumerator FadeDirectionalLightColorRoutine(Color to, float duration)
    {
        Color from = directionalLight.color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            directionalLight.color = Color.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        directionalLight.color = to;
        lightColorRoutine = null;
    }

    IEnumerator PlayGroundhogAudio()
    {
        yield return new WaitForSeconds(1f);
        AudioManager.Instance.PlayMusic("groundhog");
        yield return new WaitForSeconds(12f);
        AudioManager.Instance.StopMusic(1f);
    }
}
