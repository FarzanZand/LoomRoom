using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The map leaving the table when a run starts (Burn, instead of Sink): it burns away like parchment, a ragged front of
// char and embers eating in from its edges to its middle, every prop burning up from its foot as the front reaches it,
// embers rising off the burning edge, the places' lamps going out as their place burns and the lights over the map
// dimming with it, leaving the bare table. Every map material is drawn with a burn copy while it runs
// (Campaign/Map/Map burn.shader, the same texture and colour) and given back after. Like the sink, it never gets ahead of
// the floor being built (TableLevelLoader.BuildProgress), and the floor waits for it (MapSinking).
public partial class CampaignManager
{
    [TabGroup("Map"), Tooltip("Starting a run burns the map away like parchment. Off: it folds flat into the table.")]
    public bool burnAway = true;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Tooltip("Campaign/Map/Map burn.shader (referenced here so builds include it).")]
    public Shader burnShader;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Min(.3f), Tooltip("Seconds from the first edge catching to nothing left.")]
    public float burnSeconds = 1.6f;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), ColorUsage(false, true), Tooltip("The embers along the burning edge.")]
    public Color burnGlow = new(2.6f, .62f, .1f);
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Range(0f, .6f), Tooltip("How ragged the burning edge is.")]
    public float burnRagged = .3f;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Tooltip("Sparks rising off the burning edge (a particle material). Empty: none.")]
    public Material burnEmberMaterial;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Min(0)] public float burnEmbersPerSecond = 160f;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Tooltip("Played as the edges catch.")] public AudioClip burnSound;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Tooltip("The crackle while it burns (looped, faded out at the end).")] public AudioClip burnLoop;
    [TabGroup("Map"), ShowIf(nameof(burnAway)), Range(0f, 1f)] public float burnVolume = .5f;

    static readonly int BurnFrontId = Shader.PropertyToID("_MapBurnFront"), BurnCentreId = Shader.PropertyToID("_MapBurnCentre"),
        BurnRightId = Shader.PropertyToID("_MapBurnRight"), BurnForwardId = Shader.PropertyToID("_MapBurnForward"),
        BurnSizeId = Shader.PropertyToID("_MapBurnSize"), BurnGlowId = Shader.PropertyToID("_MapBurnGlow");

    IEnumerator Burn()
    {
        var m = Map.transform;
        sinking = true;
        var swapped = new List<(Renderer renderer, Material[] materials)>();
        var copies = new Dictionary<Material, Material>();
        var hidden = new List<Renderer>();
        var dimmed = new List<(Light light, float intensity, float inward)>();
        ParticleSystem embers = null;
        AudioSource crackle = null;
        try
        {
            foreach (var r in Map.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                var shared = r.sharedMaterials;
                // The mist and anything not drawn from a texture and colour just goes.
                if (System.Array.Exists(shared, x => x != null && x.shader != null && x.shader.name.Contains("Fog"))) { hidden.Add(r); r.enabled = false; continue; }
                var burning = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null) continue;
                    if (!copies.TryGetValue(source, out var copy))
                    {
                        copy = new Material(burnShader) { name = source.name + " (burning)" };
                        var tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                        if (tex != null) copy.SetTexture("_BaseMap", tex);
                        if (source.HasProperty("_BaseMap")) { copy.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap")); copy.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap")); }
                        var colour = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                        copy.SetColor("_BaseColor", colour);
                        copies[source] = copy;
                    }
                    burning[i] = copy;
                }
                swapped.Add((r, shared));
                r.sharedMaterials = burning;
            }
            float halfX = Map.halfX * Mathf.Abs(m.lossyScale.x), halfZ = Map.halfZ * Mathf.Abs(m.lossyScale.z);
            // Lights: the places' lamps go out as their place burns; the lights over the map dim with the whole.
            foreach (var l in Map.GetComponentsInChildren<Light>())
            {
                if (!l.enabled) continue;
                var local = m.InverseTransformPoint(l.transform.position);
                float inward = OnTheLand(l) ? Mathf.Clamp01(Mathf.Min(1f - Mathf.Abs(local.x) / Map.halfX, 1f - Mathf.Abs(local.z) / Map.halfZ)) : -1f;
                dimmed.Add((l, l.intensity, inward));
            }
            Shader.SetGlobalVector(BurnCentreId, m.position);
            Shader.SetGlobalVector(BurnRightId, m.right);
            Shader.SetGlobalVector(BurnForwardId, m.forward);
            // Height counts a little: a prop a tenth of the map's size tall burns from foot to top in a few hundredths.
            Shader.SetGlobalVector(BurnSizeId, new Vector4(halfX, halfZ, .35f / Mathf.Max(1f, Mathf.Min(halfX, halfZ)), burnRagged));
            Shader.SetGlobalColor(BurnGlowId, burnGlow);
            Shader.SetGlobalFloat(BurnFrontId, 0f);
            if (burnEmberMaterial != null) embers = MakeEmbers(Mathf.Min(halfX, halfZ));
            if (AudioManager.HasInstance)
            {
                if (burnSound != null) AudioManager.Instance.PlaySFX2D(burnSound, burnVolume);
                if (burnLoop != null)
                {
                    crackle = gameObject.AddComponent<AudioSource>();
                    crackle.clip = burnLoop; crackle.loop = true; crackle.spatialBlend = 0f; crackle.volume = 0f;
                    crackle.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
                    crackle.Play();
                }
            }
            float k = 0f, emitted = 0f;
            for (float s = 0f; ; s += Time.deltaTime)
            {
                // Never ahead of the floor being built under it, and never back.
                k = Mathf.Max(k, Mathf.Min(s / burnSeconds, loader != null ? loader.BuildProgress : 1f));
                // Slow to catch at the edges, quicker as it runs in.
                float front = k * k * (3f - 2f * k) * .35f + k * .65f;
                Shader.SetGlobalFloat(BurnFrontId, front);
                float reach = front * (1f + burnRagged + .12f);
                foreach (var (light, intensity, inward) in dimmed)
                {
                    if (light == null) continue;
                    if (inward >= 0f) light.enabled = reach < inward + burnRagged * .5f;
                    else light.intensity = intensity * (1f - k);
                }
                if (crackle != null) crackle.volume = burnVolume * Mathf.Clamp01(Mathf.Min(s * 4f, (1f - k) * 3f));
                // Sparks along the burning edge: a point on the shrinking rectangle the front has reached.
                if (embers != null && reach < 1f)
                {
                    emitted += burnEmbersPerSecond * Time.deltaTime;
                    var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                    for (; emitted >= 1f; emitted -= 1f)
                    {
                        float inset = Mathf.Clamp01(reach - Random.Range(0f, burnRagged));
                        float side = Random.value, along = Random.Range(-1f, 1f);
                        float x = side < .5f ? Map.halfX * (1f - inset) * (side < .25f ? -1f : 1f) : along * Map.halfX * (1f - inset);
                        float z = side < .5f ? along * Map.halfZ * (1f - inset) : Map.halfZ * (1f - inset) * (side < .75f ? -1f : 1f);
                        p.position = m.TransformPoint(new Vector3(x, Map.HeightAt(x, z) + .3f, z));
                        float lift = Mathf.Min(halfX, halfZ);
                        p.velocity = Vector3.up * Random.Range(.03f, .09f) * lift + Random.insideUnitSphere * .015f * lift;
                        embers.Emit(p, 1);
                    }
                }
                if (k >= 1f) break;
                yield return null;
            }
            Map.gameObject.SetActive(false);
            if (crackle != null) { crackle.Stop(); }
        }
        finally
        {
            foreach (var (r, materials) in swapped) if (r != null) r.sharedMaterials = materials;
            foreach (var r in hidden) if (r != null) r.enabled = true;
            foreach (var (light, intensity, _) in dimmed) if (light != null) { light.intensity = intensity; light.enabled = true; }
            foreach (var copy in copies.Values) if (copy != null) Destroy(copy);
            Shader.SetGlobalFloat(BurnFrontId, 0f);
            if (crackle != null) Destroy(crackle);
            if (embers != null) Destroy(embers.gameObject, 2.5f);
            sinking = false;
        }
    }

    // Small sparks that rise, drift and wink out (world space, so they stay where they rose as the map goes).
    ParticleSystem MakeEmbers(float mapSize)
    {
        var go = new GameObject("Map burn embers");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
        float size = mapSize * .012f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size * 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(mapSize * .03f, mapSize * .08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .55f, .15f), new Color(1f, .85f, .4f));
        main.gravityModifier = -.05f;
        main.maxParticles = 3000;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 20f; shape.radius = .01f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var noise = ps.noise; noise.enabled = true; noise.strength = mapSize * .02f; noise.frequency = .8f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .4f, .1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = burnEmberMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }
}
