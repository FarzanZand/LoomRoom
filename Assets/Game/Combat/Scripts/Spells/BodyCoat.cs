using System.Collections.Generic;
using UnityEngine;

// What a lasting spell does to how a body looks while it lasts (FrostCoat on a chilled enemy, BurnCoat on a burning one).
// It sits on the effect the spell puts under the body, and swaps the body's materials (and everything it wears and holds)
// for copies whose textures are baked from the body's own, texel for texel, by a bake material: the same shader and light
// as the body, so the look is the body's own pixel size, lit like it. It sets in through a half-strength look and goes
// back through it before the end. The baked textures and material copies are made once per (bake, material) and shared
// by every body. A body killed while it lasts keeps the look. Nothing on a player.
public abstract class BodyCoat : MonoBehaviour
{
    [Tooltip("The bake material: makes each texture of the look from the body's own (pass 0 the colour; pass 1, when the look has one, the glow).")]
    public Material bake;

    [Header("Setting in and wearing off")]
    [Min(0), Tooltip("Seconds it shows the half-strength look as it sets in.")] public float setSeconds = .12f;
    [Min(0), Tooltip("Seconds before the spell ends that it shows the half-strength look.")] public float endSeconds = .6f;
    [Range(0, 1), Tooltip("How strong the half-strength look is, as a share of the full one.")] public float half = .45f;

    protected static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex"),
                                  BaseColor = Shader.PropertyToID("_BaseColor"), Surface = Shader.PropertyToID("_Surface"),
                                  EmissionMap = Shader.PropertyToID("_EmissionMap"), EmissionColor = Shader.PropertyToID("_EmissionColor");

    static readonly Dictionary<(Material bake, Material source, bool full), Material> copies = new();
    static readonly Dictionary<(Material bake, Texture source, bool full, int pass), RenderTexture> baked = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        foreach (var rt in baked.Values) if (rt != null) rt.Release();
        baked.Clear(); copies.Clear();
    }

    protected Character character;
    protected Transform head;
    readonly List<Renderer> renderers = new();
    readonly List<Material[]> originals = new();
    protected float age;
    int shown = -1;   // -1 own look, 0 half strength, 1 full
    bool kept;        // died with the look on: it stays

    // Whether the spell still holds the body, and how long it has left.
    protected abstract bool Lasting(out float secondsLeft);
    // The bake material's settings for a look of this strength (1 full, else the half-strength share).
    protected abstract void SetBake(Material bakeMaterial, float strength);
    // True when the look has a glow (bake pass 1, put in the copies' emission).
    protected virtual bool Glows => false;
    protected virtual Color GlowColour => Color.black;
    protected virtual void Begin() { }
    protected virtual void Tick() { }
    protected virtual void End() { }

    protected virtual void Start()
    {
        character = GetComponentInParent<Character>();
        if (character == null || character is Player || bake == null) { enabled = false; return; }
        var anim = character.Animator;
        if (anim != null && anim.isHuman) head = anim.GetBoneTransform(HumanBodyBones.Head);
        foreach (var r in character.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || !r.enabled) continue;
            if (r.transform.IsChildOf(transform) || r.GetComponentInParent<SpellVisual>() != null) continue;
            if (EliteEnemy.IsGlow(r)) continue;   // an elite's glow shells keep their own material
            renderers.Add(r); originals.Add(r.sharedMaterials);
        }
        Begin();
    }

    void Update()
    {
        age += Time.deltaTime;
        // Killed while it lasts: the body keeps the look (a frozen or charred corpse, and the pieces DeathBurst breaks it
        // into, which take the body's materials as it dies), so nothing snaps back to its own colours on the floor. Only
        // the particles stop.
        if (character != null && !character.IsAlive) { kept = true; End(); enabled = false; return; }
        float left = 0f;
        bool on = character != null && character.IsAlive && Lasting(out left);
        int want = !on ? -1 : age < setSeconds || left < endSeconds ? 0 : 1;
        if (want != shown) Show(want);
        if (!on) { End(); enabled = false; return; }
        Tick();
    }

    // The middle of the body's top, a little in front: where breath or smoke comes out.
    protected Vector3 HeadPoint(out Vector3 forward)
    {
        forward = character.transform.forward;
        if (head != null) return head.position + forward * .1f - Vector3.up * .05f;
        var b = BodyBounds();
        return new Vector3(b.center.x, b.center.y + b.extents.y * .5f, b.center.z) + forward * b.extents.z;
    }

    protected Bounds BodyBounds()
    {
        var col = character.GetComponent<Collider>();
        return col != null ? col.bounds : new Bounds(character.transform.position + Vector3.up, Vector3.one);
    }

    // Fits a box-shaped emitter to the body (the effect sits at the middle of its collider).
    protected void FitToBody(ParticleSystem ps, float width = .7f, float height = .8f)
    {
        if (ps == null) return;
        var shape = ps.shape; var s = BodyBounds().size; var l = ps.transform.lossyScale;
        shape.scale = new Vector3(s.x * width / Mathf.Max(l.x, .01f), s.y * height / Mathf.Max(l.y, .01f), s.z * width / Mathf.Max(l.z, .01f));
    }

    // The largest skinned body part with a mesh that can be read (particles can come off its surface), or null.
    protected SkinnedMeshRenderer ReadableBody()
    {
        SkinnedMeshRenderer best = null; float most = 0f;
        foreach (var r in renderers)
            if (r is SkinnedMeshRenderer s && s.sharedMesh != null && s.sharedMesh.isReadable && s.bounds.size.sqrMagnitude > most) { best = s; most = s.bounds.size.sqrMagnitude; }
        return best;
    }

    void Show(int look)
    {
        shown = look;
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var own = originals[i];
            if (look < 0) { r.sharedMaterials = own; continue; }
            var mats = new Material[own.Length];
            for (int m = 0; m < own.Length; m++) mats[m] = Coated(own[m], look == 1);
            r.sharedMaterials = mats;
        }
    }

    protected virtual void OnDestroy() { if (shown >= 0 && !kept) Show(-1); }

    Material Coated(Material source, bool full)
    {
        if (source == null || !source.HasProperty(BaseMap)) return source;
        if (source.HasProperty(Surface) && source.GetFloat(Surface) > .5f) return source;   // glass, glows: left as they are
        if (copies.TryGetValue((bake, source, full), out var copy) && copy != null) return copy;
        float k = full ? 1f : half;
        copy = new Material(source) { name = $"{source.name} ({bake.name}{(full ? "" : ", half")})" };
        var tex = source.GetTexture(BaseMap);
        if (tex != null)
        {
            var rt = Bake(tex, full, k, 0);
            copy.SetTexture(BaseMap, rt);
            if (copy.HasProperty(MainTex)) copy.SetTexture(MainTex, rt);
            if (Glows && copy.HasProperty(EmissionMap))
            {
                // The glow replaces any emission the body had (a creature's eyes go dark under the soot).
                copy.SetTexture(EmissionMap, Bake(tex, full, k, 1));
                copy.SetColor(EmissionColor, GlowColour * (full ? 1f : half));
                copy.EnableKeyword("_EMISSION");
                copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
        else if (source.HasProperty(BaseColor))
            copy.SetColor(BaseColor, TintPlain(source.GetColor(BaseColor), k));
        return copies[(bake, source, full)] = copy;
    }

    // A material with no texture: its colour, as the look makes it.
    protected abstract Color TintPlain(Color colour, float strength);

    RenderTexture Bake(Texture source, bool full, float strength, int pass)
    {
        if (baked.TryGetValue((bake, source, full, pass), out var rt) && rt != null && rt.IsCreated()) return rt;
        rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, pass == 0 ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
        {
            name = $"{source.name} ({bake.name}{(full ? "" : ", half")}{(pass == 1 ? ", glow" : "")})",
            filterMode = FilterMode.Point, wrapMode = source.wrapMode, useMipMap = true, autoGenerateMips = true
        };
        rt.Create();
        SetBake(bake, strength);
        Graphics.Blit(source, rt, bake, pass);
        return baked[(bake, source, full, pass)] = rt;
    }
}
