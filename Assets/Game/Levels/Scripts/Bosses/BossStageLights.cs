using System.Collections.Generic;
using UnityEngine;

// The boss stage's own lights through the fight (on the Boss stage template, driven by BossStageEncounter): every fire
// on the stage (braziers, torches: their light and their flame) throbs on the beat of the fight's music, and when the
// fight's second phase begins (BossFight > Phase two) the fires burn a sick green, the moonlight across the stage turns
// blood red and everything flares for a moment. Before the fight and after it they burn as placed.
public class BossStageLights : MonoBehaviour
{
    [Tooltip("The cold light raking across the stage from the Dungeon Master's side (rims the fighters).")]
    public Light moon;
    [Tooltip("A narrow light from high above that follows the boss through the fight, so it never fades into the dark.")]
    public Light follow;
    [Tooltip("Where the follow light hangs relative to the boss (world units: up, and toward the player's side).")]
    public Vector3 followOffset = new(-3f, 9f, 0f);
    [Min(0), Tooltip("How fast the follow light catches up with the boss (higher: snappier).")]
    public float followSpeed = 4f;
    [Range(0, 1), Tooltip("How much brighter the fires get on each beat.")]
    public float beatPulse = .35f;
    [Min(.5f), Tooltip("How fast a beat's throb fades (higher: a sharper thump).")]
    public float pulseFalloff = 7f;
    [Tooltip("The fires' colour in the second phase.")]
    public Color phaseTwoFire = new(.45f, 1f, .3f);
    [Tooltip("The moonlight's colour in the second phase.")]
    public Color phaseTwoMoon = new(1f, .12f, .1f);
    [Min(.1f)] public float phaseTwoSeconds = 2.5f;
    [Min(0), Tooltip("How much the fires flare as the second phase begins (fades out over a second).")]
    public float phaseTwoFlare = 2.5f;

    struct Fire { public Light light; public TorchFlicker flicker; public Color colour; public float intensity; }
    readonly List<Fire> fires = new();
    readonly List<(ParticleSystem system, Color colour)> flames = new();
    Color moonColour, followColour;
    float followIntensity;
    Transform followed;
    float phase, phaseTarget, flare, beat = -1f;

    void Awake()
    {
        foreach (var l in GetComponentsInChildren<Light>(true))
        {
            if (l == moon || l.type != LightType.Point || l.GetComponentInParent<BossStageFire>() == null) continue;
            fires.Add(new Fire { light = l, flicker = l.GetComponent<TorchFlicker>(), colour = l.color, intensity = l.intensity });
        }
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            if (ps.GetComponentInParent<BossStageFire>() != null) flames.Add((ps, ps.main.startColor.color));
        if (moon != null) moonColour = moon.color;
        if (follow != null) { followColour = follow.color; followIntensity = follow.intensity; follow.intensity = 0f; }
    }

    // The follow light picks up the boss (null lets it fade out).
    public void Follow(Transform target)
    {
        followed = target;
        if (follow != null && target != null) follow.transform.position = target.position + Offset(target);
    }

    // The offset turned so "toward the player's side" is away from the Dungeon Master (the template's -x).
    Vector3 Offset(Transform target) => transform.TransformDirection(new Vector3(followOffset.x, 0, followOffset.z)) + Vector3.up * followOffset.y;

    // The music's beat now (beats since its first, fractional), or below 0 while there is no music.
    public void Beat(float beats) => beat = beats;

    public void PhaseTwo() { phaseTarget = 1f; flare = 1f; }

    // The fight is over: the fires settle back to their own colour.
    public void Calm() { phaseTarget = 0f; beat = -1f; followed = null; }

    void Update()
    {
        phase = Mathf.MoveTowards(phase, phaseTarget, Time.deltaTime / phaseTwoSeconds);
        flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime);
        float thump = beat >= 0f ? beatPulse * Mathf.Exp(-(beat - Mathf.Floor(beat)) * pulseFalloff) : 0f;
        float scale = 1f + thump + flare * flare * phaseTwoFlare;
        float tint = Mathf.SmoothStep(0, 1, phase);
        foreach (var f in fires)
        {
            if (f.light == null) continue;
            f.light.color = Color.Lerp(f.colour, phaseTwoFire, tint);
            if (f.flicker != null) f.flicker.scale = scale;
            else f.light.intensity = f.intensity * scale;
        }
        foreach (var (system, colour) in flames)
        {
            if (system == null) continue;
            var main = system.main;
            main.startColor = Color.Lerp(colour, phaseTwoFire * 1.4f, tint);
        }
        if (moon != null) moon.color = Color.Lerp(moonColour, phaseTwoMoon, tint);
        if (follow != null)
        {
            bool on = followed != null && followed.gameObject.activeInHierarchy;
            follow.intensity = Mathf.MoveTowards(follow.intensity, on ? followIntensity : 0f, followIntensity * Time.deltaTime * .8f);
            follow.color = Color.Lerp(followColour, phaseTwoMoon, tint * .6f);
            if (on)
            {
                var chest = followed.position + Vector3.up * 1.2f;
                follow.transform.position = Vector3.Lerp(follow.transform.position, followed.position + Offset(followed), 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
                follow.transform.rotation = Quaternion.Slerp(follow.transform.rotation, Quaternion.LookRotation(chest - follow.transform.position), 1f - Mathf.Exp(-followSpeed * 2f * Time.deltaTime));
            }
        }
    }
}
