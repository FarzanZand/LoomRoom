using UnityEngine;

// A torch's light breathing a little: intensity and range wander on smooth noise.
[RequireComponent(typeof(Light))]
public class TorchFlicker : MonoBehaviour
{
    [Range(0, 1)] public float amount = .18f;
    [Min(0)] public float speed = 5f;

    Light torch;
    float baseIntensity, baseRange, seed;

    void Awake()
    {
        torch = GetComponent<Light>();
        // Torches light the world and the first-person hands alike.
        FirstPersonLighting.SetLayers(torch, FirstPersonLighting.WorldLayer | FirstPersonLighting.ArmsLayer);
        baseIntensity = torch.intensity; baseRange = torch.range;
        seed = Random.value * 100;
    }

    // Tinted or dimmed after spawning (room profiles): flicker around the new values.
    public void Rebase() { baseIntensity = torch.intensity; baseRange = torch.range; }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed) * 2 - 1;
        torch.intensity = baseIntensity * (1 + n * amount);
        torch.range = baseRange * (1 + n * amount * .3f);
    }
}
