using Unity.Cinemachine;
using UnityEngine;

// Runtime behaviour for spell effect prefabs: flickering or flashing light, charge intensity for
// effects held in the hand, a camera impulse on spawn, and a graceful end where particles stop
// emitting and finish their lifetime instead of vanishing.
[DisallowMultipleComponent]
public class SpellVisual : MonoBehaviour
{
    [Header("Light")]
    public Light glow;
    [Min(0)] public float intensity = 2f;
    [Tooltip("Perlin flicker amount, as a fraction of the intensity.")]
    [Range(0, 1)] public float flicker = .25f;
    [Min(0)] public float flickerSpeed = 9f;
    [Tooltip("Empty for a steady light. Otherwise the intensity follows this curve over its length (a flash).")]
    public AnimationCurve flash;
    [Min(.01f)] public float flashSeconds = .4f;

    [Header("Charge")]
    [Tooltip("Emission and size of these systems scale with SetCharge (held effects).")]
    public ParticleSystem[] chargeScaled = new ParticleSystem[0];
    [Min(0)] public float chargeSizeBoost = .8f, chargeRateBoost = 1.5f, chargeLightBoost = 1.2f;

    [Header("Lifetime")]
    [Tooltip("Zero keeps the effect alive until Stop is called.")]
    [Min(0)] public float autoDestroySeconds;
    public CinemachineImpulseSource impulse;
    [Min(0)] public float impulseForce = 1f;
    public AudioSource loop;

    ParticleSystem[] systems;
    float[] baseRates, baseSizes;
    float charge, age, seed, loopVolume;
    bool stopping;

    void Awake()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        baseRates = new float[chargeScaled.Length];
        baseSizes = new float[chargeScaled.Length];
        for (int i = 0; i < chargeScaled.Length; i++)
        {
            if (chargeScaled[i] == null) continue;
            baseRates[i] = chargeScaled[i].emission.rateOverTimeMultiplier;
            baseSizes[i] = chargeScaled[i].main.startSizeMultiplier;
        }
        seed = Random.value * 100f;
        if (loop != null)
        {
            loopVolume = loop.volume;
            if (AudioManager.HasInstance && AudioManager.Instance.SfxGroup != null) loop.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
        }
    }

    void OnEnable()
    {
        age = 0; stopping = false;
        if (impulse != null) impulse.GenerateImpulseWithForce(impulseForce);
        ApplyLight();
    }

    // 0 = resting in the hand, 1 = fully charged.
    public void SetCharge(float value)
    {
        charge = Mathf.Clamp01(value);
        for (int i = 0; i < chargeScaled.Length; i++)
        {
            var ps = chargeScaled[i]; if (ps == null) continue;
            var emission = ps.emission; emission.rateOverTimeMultiplier = baseRates[i] * (1 + chargeRateBoost * charge);
            var main = ps.main; main.startSizeMultiplier = baseSizes[i] * (1 + chargeSizeBoost * charge);
        }
    }

    // Stop emitting, let live particles finish, then destroy. Detaches from a moving parent so the
    // remains stay where they were left.
    public void Stop(bool detach = true)
    {
        if (stopping) return;
        stopping = true;
        if (detach) transform.SetParent(null, true);
        float longest = 0;
        foreach (var ps in systems)
        {
            if (ps == null) continue;
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            longest = Mathf.Max(longest, ps.main.startLifetime.constantMax);
        }
        Destroy(gameObject, longest + .1f);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (stopping)
        {
            if (glow != null) glow.intensity = Mathf.MoveTowards(glow.intensity, 0, intensity * 6f * Time.deltaTime);
            if (loop != null) loop.volume = Mathf.MoveTowards(loop.volume, 0, loopVolume * 5f * Time.deltaTime);
            return;
        }
        ApplyLight();
        if (autoDestroySeconds > 0 && age >= autoDestroySeconds) Destroy(gameObject);
    }

    void ApplyLight()
    {
        if (glow == null) return;
        float value = intensity * (1 + chargeLightBoost * charge);
        if (flash != null && flash.length > 0) value *= flash.Evaluate(age / flashSeconds);
        if (flicker > 0) value *= 1 - flicker + flicker * 2 * Mathf.PerlinNoise(seed, age * flickerSpeed);
        glow.intensity = value;
    }
}
