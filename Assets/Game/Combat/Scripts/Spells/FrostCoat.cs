using UnityEngine;

// The frozen look on an enemy slowed by frost (a Frost Lance; the user, 2026-11-13: "making them look more bluer"). It sits
// on the chill effect Chill puts under the body (Combat/Spells/Frost Lance/Frost Lance Chill), and for as long as the chill
// lasts (BodyCoat):
//   - the body and everything it wears and holds are drawn frozen, baked by Frost Bake.shader: every texel keeps its value
//     and takes its colour from a cold ramp, deep blue to pale ice; sparse ragged rime on the lighter, raised parts; a few
//     single bright ice texels;
//   - its breath comes out in small cold puffs at its head, and rime flakes drift down off it.
public class FrostCoat : BodyCoat
{
    [Header("Frozen")]
    [Range(0, 1), Tooltip("How far the colours go toward the cold ramp (each texel keeps its value, takes the ramp's colour).")] public float cold = .7f;
    [Range(0, 1), Tooltip("How much of the lighter texels the rime covers.")] public float cover = .2f;
    [Tooltip("The cold ramp: the darkest texels (creases, outlines) ...")] public Color deep = new Color(.1f, .14f, .24f);
    [Tooltip("... the middle values ...")] public Color mid = new Color(.38f, .5f, .64f);
    [Tooltip("... and the lightest, and the rime.")] public Color ice = new Color(.8f, .88f, .96f);

    [Header("Breath and flakes")]
    [Tooltip("Cold puffs at its head, emitted by this script.")] public ParticleSystem breath;
    [Min(.1f)] public float breathEvery = 1.4f;
    [Min(1)] public int breathPuffs = 4;
    [Tooltip("Rime flakes drifting down off the body (emits by itself).")] public ParticleSystem flakes;

    static readonly int ColdId = Shader.PropertyToID("_Cold"), CoverId = Shader.PropertyToID("_Cover"),
                        IceId = Shader.PropertyToID("_Ice"), MidId = Shader.PropertyToID("_Mid"), DeepId = Shader.PropertyToID("_Deep");

    Chill chill;
    float nextBreath = .3f;

    protected override void Begin()
    {
        chill = GetComponentInParent<Chill>();
        FitToBody(flakes);
    }

    protected override bool Lasting(out float secondsLeft)
    {
        secondsLeft = chill != null ? chill.SecondsLeft : 0f;
        return chill != null;
    }

    protected override void SetBake(Material m, float strength)
    {
        m.SetFloat(ColdId, cold * strength); m.SetFloat(CoverId, cover * strength);
        m.SetColor(IceId, ice); m.SetColor(MidId, mid); m.SetColor(DeepId, deep);
    }

    protected override Color TintPlain(Color c, float strength)
    {
        float lum = c.r * .3f + c.g * .59f + c.b * .11f;
        return Color.Lerp(c, new Color(lum * .78f, lum * .93f, lum * 1.22f, c.a), cold * strength);
    }

    protected override void Tick()
    {
        if (breath == null) return;
        breath.transform.position = HeadPoint(out var forward);
        breath.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        if ((nextBreath -= Time.deltaTime) > 0f) return;
        breath.Emit(breathPuffs);
        nextBreath = breathEvery * Random.Range(.8f, 1.2f);
    }

    protected override void End() { if (flakes != null) flakes.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
}
