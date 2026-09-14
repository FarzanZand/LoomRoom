using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;
    public PlayableDirector DinnerDirector;
    public Transform startPositionRoom;
    public Light directionalLight;

    protected override void Awake()
    {
        base.Awake();
        if (ProgressionManager.Instance.startingPlayer == PlayerManager.ActivePlayer.RoomPlayer)
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

        WakeUpDirector.stopped -= OnWakeUpComplete;
        WakeUpDirector.stopped += OnWakeUpComplete;
        WakeUpDirector.Play();
        StartCoroutine(PlayGroundhogAudio());
    }

    [Button]
    public void PlayDinnerCutscene()
    {
        var controller = DinnerDirector.GetComponent<DinnerCutsceneController>();
        if (controller != null)
            controller.Play();
        else
            DinnerDirector.Play();
    }
    
    void OnWakeUpComplete(PlayableDirector _)
    {
        WakeUpDirector.stopped -= OnWakeUpComplete;
        if (PlayerManager.Instance == null) return;
        PlayerManager.Instance.SetRoomPlayerControllerEnabled(true);
    }

    Coroutine lightFadeRoutine;

    bool EnsureDirectionalLight()
    {
        if (directionalLight != null) return true;

        directionalLight = RenderSettings.sun;
        if (directionalLight == null)
        {
            foreach (var light in FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional) { directionalLight = light; break; }
            }
        }

        if (directionalLight == null)
        {
            Debug.LogWarning("WorldManager: no directional light assigned or found in scene.", this);
            return false;
        }
        return true;
    }

    public void FadeDirectionalLight(float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        FadeDirectionalLight(directionalLight.intensity, to, duration);
    }

    public void FadeDirectionalLight(float from, float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
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
        if (!EnsureDirectionalLight()) return;
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
