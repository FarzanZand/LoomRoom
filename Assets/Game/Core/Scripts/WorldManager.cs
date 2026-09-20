using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;

// World-level helpers: the directional light and the opening sequence hook.
// Cutscene logic itself lives on CutsceneController subclasses.
public class WorldManager : Singleton<WorldManager>
{
    [Header("Opening")]
    public WakeUpCutsceneController wakeUpCutscene;
    public DinnerCutsceneController dinnerCutscene;

    [Header("Lighting")]
    public Light directionalLight;
    [Tooltip("Light colour used while the game starts in the room (night).")]
    public Color roomStartLightColor = Color.black;
    public float roomStartLightIntensity = 0.7f;

    protected override void Awake()
    {
        base.Awake();
        if (ProgressionManager.HasInstance &&
            ProgressionManager.Instance.startingPlayer == PlayerKind.Room &&
            EnsureDirectionalLight())
        {
            directionalLight.color     = roomStartLightColor;
            directionalLight.intensity = roomStartLightIntensity;
        }
    }

    void Start()
    {
        bool skip = ProgressionManager.HasInstance && ProgressionManager.Instance.skipWakeUp;
        if (!skip && wakeUpCutscene != null)
            wakeUpCutscene.Play();
    }

    [Button]
    public void PlayDinnerCutscene()
    {
        if (dinnerCutscene != null) dinnerCutscene.Play();
    }

    // ── Directional light fades ───────────────────────────────────────

    Coroutine lightFadeRoutine;
    Coroutine lightColorRoutine;

    bool EnsureDirectionalLight()
    {
        if (directionalLight != null) return true;
        directionalLight = RenderSettings.sun;
        if (directionalLight == null)
            foreach (var light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional) { directionalLight = light; break; }
        if (directionalLight == null)
            Debug.LogWarning("WorldManager: no directional light assigned or found in scene.", this);
        return directionalLight != null;
    }

    public void FadeDirectionalLight(float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        FadeDirectionalLight(directionalLight.intensity, to, duration);
    }

    public void FadeDirectionalLight(float from, float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        if (lightFadeRoutine != null) StopCoroutine(lightFadeRoutine);
        lightFadeRoutine = StartCoroutine(FadeIntensity(from, to, duration));
    }

    public void FadeDirectionalLightColor(Color to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        if (lightColorRoutine != null) StopCoroutine(lightColorRoutine);
        lightColorRoutine = StartCoroutine(FadeColor(to, duration));
    }

    IEnumerator FadeIntensity(float from, float to, float duration)
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

    IEnumerator FadeColor(Color to, float duration)
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
}
