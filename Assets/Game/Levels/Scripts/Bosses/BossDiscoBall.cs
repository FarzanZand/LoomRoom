using System.Collections;
using UnityEngine;

// A disco ball on a chain over a boss stage (BossFight > Phase two > Disco Ball, a prefab with this on its root, made by
// DiscoBallSetup.Show()): lowered from high above as the second phase begins, with a small bounce as the chain takes its
// weight, then turning while its spot light (a cookie of little dots) sweeps moving spots of light over the stage, its
// colour stepping through the light show's colours and its brightness thumping on the beat. Off puts the lights out.
public class BossDiscoBall : MonoBehaviour
{
    [Tooltip("The ball (turned about its up).")] public Transform ball;
    [Tooltip("The spot light pointing down, its cookie the dots it throws.")] public Light spots;
    [Tooltip("Metres over the stage's top the ball hangs at.")] public float hangHeight = 3.6f;
    [Tooltip("Metres above where it hangs that it comes down from.")] public float dropFrom = 16f;
    [Min(.1f)] public float lowerSeconds = 3f;
    [Tooltip("Degrees a second the ball turns, and the spots sweep.")] public float spin = 40f, sweep = 28f;
    [Min(0), Tooltip("How much brighter the spots get on each beat.")] public float beatThump = .5f;
    [Min(1), Tooltip("Beats per colour step.")] public int beatsPerColour = 2;
    public AudioClip lowerSound;
    [Range(0, 1)] public float lowerVolume = .8f;

    float spotsIntensity, level, target, beat = -1f;
    Color[] palette;

    void Awake()
    {
        if (spots != null) { spotsIntensity = spots.intensity; spots.intensity = 0f; }
    }

    public void Lower(Vector3 stageTop, Color[] colours)
    {
        palette = colours != null && colours.Length > 0 ? colours : null;
        StartCoroutine(Run(stageTop));
    }

    IEnumerator Run(Vector3 stageTop)
    {
        var rest = stageTop + Vector3.up * hangHeight;
        var from = rest + Vector3.up * dropFrom;
        transform.position = from;
        if (lowerSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(lowerSound, lowerVolume);
        for (float t = 0f; t < lowerSeconds; t += Time.deltaTime)
        {
            float u = t / lowerSeconds;
            // Eased in, then the chain catching it: a little past, back up, settled.
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(u / .8f), 3f);
            float bounce = u > .8f ? Mathf.Sin((u - .8f) / .2f * Mathf.PI * 2f) * (1f - (u - .8f) / .2f) * .25f : 0f;
            transform.position = Vector3.LerpUnclamped(from, rest, e) + Vector3.down * bounce;
            yield return null;
        }
        transform.position = rest;
        target = 1f;
    }

    public void Beat(float beats) => beat = beats;

    public void Off() => target = 0f;

    void Update()
    {
        if (ball != null) ball.Rotate(0f, spin * Time.deltaTime, 0f, Space.Self);
        level = Mathf.MoveTowards(level, target, Time.deltaTime / (target > level ? .6f : .12f));
        if (spots == null) return;
        spots.transform.Rotate(0f, sweep * Time.deltaTime, 0f, Space.World);
        float steps = beat >= 0f ? beat : Time.time * 2f;
        float thump = beatThump * Mathf.Exp(-(steps - Mathf.Floor(steps)) * 4f);
        spots.intensity = spotsIntensity * level * (1f + thump);
        if (palette != null)
        {
            int k = Mathf.FloorToInt(steps / beatsPerColour);
            float blend = Mathf.Clamp01((steps / beatsPerColour - k) * 6f);
            spots.color = Color.Lerp(palette[((k - 1) % palette.Length + palette.Length) % palette.Length], palette[k % palette.Length], blend);
        }
    }
}
