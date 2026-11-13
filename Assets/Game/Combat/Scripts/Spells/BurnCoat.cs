using UnityEngine;

// The burning look on an enemy set alight (Burning: a Fireball's hit, the user, 2026-11-13). It sits on the fire effect
// Burning puts under the body (Combat/Spells/Fireball/Fireball Burn), and for as long as it burns (BodyCoat):
//   - the body and everything it wears and holds are drawn charred, baked by Burn Bake.shader: darkened toward soot, the
//     lightest texels toward ash, soot in ragged clusters, and embers glowing in the dark creases (the copies' emission);
//   - it burns with the Fireball's own fire (the pack's flame flipbook drawn in pixels, yellow to red, many flames
//     overlapping; small flame sprites of our own read as stickers, the user, 2026-11-13): small low flames close to
//     the whole body, a crown of slightly taller ones over its shoulders and head, embers rising, dark smoke above, and a
//     small firelight flickering over its head (never inside the body, where it lit the body's middle white).
public class BurnCoat : BodyCoat
{
    [Header("Charred")]
    [Range(0, 1), Tooltip("How far the colours go toward soot and ash (each texel keeps its order of light and dark).")] public float charred = .6f;
    [Range(0, 1), Tooltip("How much of the darker texels glow as embers.")] public float embers = .08f;
    [Tooltip("Soot, in the darkest texels.")] public Color soot = new Color(.05f, .04f, .035f);
    [Tooltip("Ash, in the lightest texels.")] public Color ash = new Color(.5f, .46f, .42f);
    [ColorUsage(false, true), Tooltip("The embers' glow (emission, so it reads in the dark).")] public Color glow = new Color(1.5f, .42f, .07f);

    [Header("Fire")]
    [Tooltip("Flames off the body's surface (from its skinned mesh where it can be read, else a box round it).")] public ParticleSystem flames;
    [Tooltip("Taller flames over its shoulders and head (kept at the top of the body by this script).")] public ParticleSystem crown;
    [Tooltip("Embers rising off it.")] public ParticleSystem sparks;
    [Tooltip("Dark smoke puffed out over its head by this script.")] public ParticleSystem smoke;
    [Min(.1f)] public float smokeEvery = .7f;
    [Min(1)] public int smokePuffs = 2;
    [Tooltip("A small flickering light over its head.")] public Light fireLight;
    [Min(0)] public float lightAbove = .35f;
    [Min(0)] public float lightIntensity = 1.6f;
    [Range(0, 1)] public float lightFlicker = .35f;

    static readonly int CharId = Shader.PropertyToID("_Char"), EmbersId = Shader.PropertyToID("_Embers"),
                        SootId = Shader.PropertyToID("_Soot"), AshId = Shader.PropertyToID("_Ash");

    Burning burning;
    float nextSmoke = .2f, seed;

    protected override bool Glows => true;
    protected override Color GlowColour => glow;

    protected override void Begin()
    {
        burning = GetComponentInParent<Burning>();
        seed = Random.value * 50f;
        if (flames != null)
        {
            var shape = flames.shape;
            var body = ReadableBody();
            if (body != null) { shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer; shape.skinnedMeshRenderer = body; shape.scale = Vector3.one; }
            else { shape.shapeType = ParticleSystemShapeType.Box; FitToBody(flames, .55f, .75f); }
        }
        FitToBody(sparks, .5f, .6f);
        FitToBody(crown, .55f, .18f);
        // The flames are sized for a body a man's height: smaller on a rat, bigger on a boss.
        float k = Mathf.Clamp(BodyBounds().size.y / 1.8f, .5f, 1.3f);
        Scale(flames, k); Scale(crown, k);
    }

    static void Scale(ParticleSystem ps, float k)
    {
        if (ps == null) return;
        var main = ps.main; main.startSizeXMultiplier *= k; main.startSizeYMultiplier *= k;
        var vel = ps.velocityOverLifetime; vel.yMultiplier *= k;
    }

    protected override bool Lasting(out float secondsLeft)
    {
        secondsLeft = burning != null ? burning.SecondsLeft : 0f;
        return burning != null;
    }

    protected override void SetBake(Material m, float strength)
    {
        m.SetFloat(CharId, charred * strength); m.SetFloat(EmbersId, embers * strength);
        m.SetColor(SootId, soot); m.SetColor(AshId, ash);
    }

    protected override Color TintPlain(Color c, float strength)
    {
        float lum = c.r * .3f + c.g * .59f + c.b * .11f;
        return Color.Lerp(c, Color.Lerp(soot, ash, lum), charred * strength);
    }

    protected override void Tick()
    {
        // Some skinned meshes give the shape module nothing to emit from (the Demon's): a box round the body then.
        if (flames != null && age > .4f && flames.particleCount == 0 && flames.shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer)
        {
            var shape = flames.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.skinnedMeshRenderer = null;
            FitToBody(flames, .55f, .75f);
        }
        var top = HeadPoint(out var forward);
        var b = BodyBounds();
        if (crown != null) crown.transform.position = new Vector3(b.center.x, b.center.y + b.extents.y * .55f, b.center.z);
        if (fireLight != null)
        {
            fireLight.transform.position = new Vector3(b.center.x, b.max.y + lightAbove, b.center.z);
            fireLight.intensity = lightIntensity * (1f - lightFlicker + lightFlicker * 2f * Mathf.PerlinNoise(seed, Time.time * 7f));
        }
        if (smoke == null) return;
        smoke.transform.position = top + Vector3.up * .15f;
        if ((nextSmoke -= Time.deltaTime) > 0f) return;
        smoke.Emit(smokePuffs);
        nextSmoke = smokeEvery * Random.Range(.7f, 1.3f);
    }

    protected override void End()
    {
        if (flames != null) flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (crown != null) crown.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (fireLight != null) fireLight.enabled = false;
    }
}
