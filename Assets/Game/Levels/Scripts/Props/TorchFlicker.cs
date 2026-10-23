using UnityEngine;

// A torch's light breathing a little: intensity and range wander on smooth noise.
[RequireComponent(typeof(Light))]
public class TorchFlicker : MonoBehaviour
{
    [Range(0, 1)] public float amount = .18f;
    [Min(0)] public float speed = 5f;
    // Set by others each frame to brighten or dim the flame around its flicker (a boss stage's beat and flares).
    [System.NonSerialized] public float scale = 1f;

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
    public void Rebase() { if (torch == null) torch = GetComponent<Light>(); baseIntensity = torch.intensity; baseRange = torch.range; }

    // Far from the view (a portal's pocket beside the floor, the floor while the player is in a pocket) or switched off,
    // a flame is left as it is: its breathing can't be seen there.
    const float FarAway = 150f;
    static int eyeFrame = -1;
    static Vector3 eye;
    static bool hasEye;

    void Update()
    {
        if (!torch.enabled) return;
        if (eyeFrame != Time.frameCount)
        {
            eyeFrame = Time.frameCount;
            var cam = Camera.main;
            hasEye = cam != null;
            if (hasEye) eye = cam.transform.position;
        }
        if (hasEye && (transform.position - eye).sqrMagnitude > FarAway * FarAway) return;
        float n = Mathf.PerlinNoise(seed, Time.time * speed) * 2 - 1;
        torch.intensity = baseIntensity * scale * (1 + n * amount);
        torch.range = baseRange * (1 + n * amount * .3f);
    }
}
