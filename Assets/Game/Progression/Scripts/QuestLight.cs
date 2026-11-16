using UnityEngine;

// The light a quest puts on a place (QuestGlow fits one to it): a cone of warm light falling on it from above, drawn with
// the dungeon's moon shaft (Moon Shaft.shader on its closed volume), a lamp at its top that lights the place itself, and
// dust turning in the cone (Air Mote: a mote shows only the light that reaches it, so the dust is seen in the cone and
// nowhere else). It breathes slowly and fades in and out. It took the place of the blue outline (the user, 2026-11-16:
// "the highlight effect is real ugly and sticks out too much in the room, do something more fitting. Especially ugly when
// the table or desk is highlighted"): the room is lit by lamps and candles, so a place is pointed out by light.
// Prefab: Progression/Resources/Quest light, made by Tools/quest_light.eval.cs; everything about its look is set there.
public class QuestLight : MonoBehaviour
{
    [Header("Pieces")]
    public MeshRenderer shaft;
    public Light lamp;
    public ParticleSystem motes;

    [Header("Look")]
    [Min(0), Tooltip("How bright the lamp makes the place: its strength at the place's distance (the room's night light gives about 4 on the big table).")]
    public float lightOnPlace = 3.2f;
    [Min(0), Tooltip("The shaft's own brightness (its material's Intensity).")]
    public float shaftStrength = .3f;
    [Range(.02f, 1f), Tooltip("The cone's radius at its top as a share of its radius at the place.")]
    public float topRadius = .22f;
    [Range(0f, .6f), Tooltip("How much it breathes: the share of its brightness that comes and goes.")]
    public float breath = .22f;
    [Min(.2f), Tooltip("Seconds a breath takes.")]
    public float breathSeconds = 3.4f;
    [Min(0f), Tooltip("Seconds to fade in and out.")]
    public float fadeSeconds = .6f;
    [Min(0f), Tooltip("Dust motes in the cone for each metre of its height.")]
    public float motesPerMetre = 6f;
    [Tooltip("A mote's size in metres, least and most.")]
    public Vector2 moteSize = new(.008f, .02f);

    // The volume the shaft is drawn on (Moon shaft volume.asset): a closed cylinder this wide and this high.
    const float VolumeRadius = 1.05f, VolumeHeight = 6f;

    Material shaftMaterial, moteMaterial;
    Color colour = Color.white;
    float level, target, lampFull;
    float footFade, softFade, moteNear, moteNearFull, moteFar, moteSpeed, moteNoise, moteLife = 7f;
    Vector3 moteDrift;
    bool read;

    // The prefab's own values, in metres: a fit scales them to the place's units.
    void Read()
    {
        if (read) return;
        read = true;
        if (shaft != null && shaft.sharedMaterial != null)
        {
            shaftMaterial = new Material(shaft.sharedMaterial);
            shaft.sharedMaterial = shaftMaterial;
            footFade = shaftMaterial.GetFloat("_FootFade"); softFade = shaftMaterial.GetFloat("_SoftFade");
        }
        if (motes != null)
        {
            var drawn = motes.GetComponent<ParticleSystemRenderer>();
            if (drawn != null && drawn.sharedMaterial != null)
            {
                moteMaterial = new Material(drawn.sharedMaterial);
                drawn.sharedMaterial = moteMaterial;
                if (moteMaterial.HasProperty("_Near")) { moteNear = moteMaterial.GetFloat("_Near"); moteNearFull = moteMaterial.GetFloat("_NearFull"); moteFar = moteMaterial.GetFloat("_Far"); }
            }
            var main = motes.main; var noise = motes.noise; var velocity = motes.velocityOverLifetime;
            moteSpeed = main.startSpeedMultiplier; moteNoise = noise.strengthMultiplier;
            moteDrift = new Vector3(velocity.xMultiplier, velocity.yMultiplier, velocity.zMultiplier);
            moteLife = Mathf.Max(.5f, main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants ? (main.startLifetime.constantMin + main.startLifetime.constantMax) * .5f : main.startLifetime.constant);
        }
    }

    // foot: where the light lands (the middle of the place, at its base). source: where it comes from. footRadius: how
    // wide it is at the place. litFrom: how far under the source the place's lit face is (a table's top, well over the
    // floor the cone ends on): the lamp's strength is set for that distance. unit: world units to the metre there (the
    // room is built about 20 to the metre, a floor on the table 1). layers: the rendering layers of what it lights (the
    // room's lamps light only the room).
    public void Fit(Vector3 foot, Vector3 source, float footRadius, float litFrom, float unit, Color colour, uint layers)
    {
        Read();
        var axis = source - foot;
        float height = axis.magnitude;
        if (height < 1e-3f) return;
        this.colour = colour;
        transform.SetPositionAndRotation(foot, Quaternion.FromToRotation(Vector3.up, axis / height));
        float top = Mathf.Max(footRadius * topRadius, .02f * unit), wide = Mathf.Max(footRadius, top);
        if (shaft != null && shaftMaterial != null)
        {
            var t = shaft.transform;
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(wide / VolumeRadius, height / VolumeHeight, wide / VolumeRadius);
            shaftMaterial.SetFloat("_Height", VolumeHeight);
            shaftMaterial.SetFloat("_RadiusBottom", VolumeRadius * footRadius / wide);
            shaftMaterial.SetFloat("_RadiusTop", VolumeRadius * top / wide);
            // The foot's fade is measured along the volume's own height, the fade into what stands in it in world units.
            shaftMaterial.SetFloat("_FootFade", footFade * unit * VolumeHeight / height);
            shaftMaterial.SetFloat("_SoftFade", softFade * unit);
        }
        if (lamp != null)
        {
            lamp.transform.localPosition = Vector3.up * height;
            lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lamp.range = height * 1.6f;
            lamp.spotAngle = Mathf.Clamp(Mathf.Atan2(footRadius * 1.15f, height) * Mathf.Rad2Deg * 2f, 8f, 150f);
            lamp.innerSpotAngle = lamp.spotAngle * .55f;
            lamp.color = colour;
            lampFull = lightOnPlace * litFrom * litFrom;
            FirstPersonLighting.SetLayers(lamp, layers);
        }
        if (motes != null)
        {
            motes.transform.localPosition = Vector3.up * height;
            motes.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            shape.radius = top; shape.length = height;
            shape.angle = Mathf.Clamp(Mathf.Atan2(footRadius - top, height) * Mathf.Rad2Deg, 0f, 80f);
            var main = motes.main; var emission = motes.emission; var noise = motes.noise; var velocity = motes.velocityOverLifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(moteSize.x * unit, moteSize.y * unit);
            main.startSpeedMultiplier = moteSpeed * unit; noise.strengthMultiplier = moteNoise * unit;
            velocity.xMultiplier = moteDrift.x * unit; velocity.yMultiplier = moteDrift.y * unit; velocity.zMultiplier = moteDrift.z * unit;
            int count = Mathf.Clamp(Mathf.RoundToInt(motesPerMetre * height / unit), 6, 90);
            main.maxParticles = count;
            emission.rateOverTime = count / moteLife;
            var drawn = motes.GetComponent<ParticleSystemRenderer>();
            if (drawn != null) drawn.renderingLayerMask = layers;
            if (moteMaterial != null && moteMaterial.HasProperty("_Near")) { moteMaterial.SetFloat("_Near", moteNear * unit); moteMaterial.SetFloat("_NearFull", moteNearFull * unit); moteMaterial.SetFloat("_Far", moteFar * unit); }
            // Dust already hanging in the cone, not a cloud let out at its top.
            motes.Clear(true); motes.Simulate(moteLife, true, true); motes.Play(true);
        }
        Draw();
    }

    public void Show(bool on)
    {
        target = on ? 1f : 0f;
        if (on && !gameObject.activeSelf) gameObject.SetActive(true);
    }

    // Gone at once (its owner was switched off).
    public void Hide() { target = level = 0f; gameObject.SetActive(false); }

    void Update()
    {
        level = fadeSeconds > 0f ? Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / fadeSeconds) : target;
        if (level <= 0f && target <= 0f) { gameObject.SetActive(false); return; }
        Draw();
    }

    void Draw()
    {
        float k = .5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 2f / breathSeconds);
        float glow = level * (1f - breath + breath * k);
        if (shaftMaterial != null) { shaftMaterial.SetColor("_BaseColor", colour); shaftMaterial.SetFloat("_Intensity", shaftStrength * glow); }
        if (lamp != null) lamp.intensity = lampFull * glow;
    }

    void OnDestroy()
    {
        if (shaftMaterial != null) Destroy(shaftMaterial);
        if (moteMaterial != null) Destroy(moteMaterial);
    }
}
