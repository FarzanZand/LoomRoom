using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum PixelatorCamera
{
    Default = 0, // ScreenManager's own room/table effects, untouched
    Clean   = 1, // native resolution, no anti-aliasing blur, the library's clean effects
    LowRes  = 2, // real low-res render with nearest-neighbour upscale, the library's low-res effects
}

public enum PixelLookCategory
{
    Architecture = 0, // walls, floors, ceilings, dungeon tiles
    Prop         = 1, // furniture, decor, loot, anything else
    Character    = 2, // decided per renderer: anything under a Character or skinned
}

// Swaps renderers between their original materials and the Pixel Lit copies in a PixelLookLibrary.
// Owned and ticked by WorldManager (Pixel Look toggles); Play mode only, so scene and prefab files
// never change. Each category can be switched on its own. Renderers spawned later (dungeon floors,
// enemies, loot) are picked up by a periodic rescan.
public class PixelLook
{
    [System.Serializable]
    public struct Parts
    {
        public PixelatorCamera camera;
        public bool architecture, props, characters;
        public int objectPixelSize;                   // per-object pixelation: screen pixels per object pixel, 1 = off
        public bool lowResCharacters, lowResProps;    // which categories get per-object pixelation
        public PixelatorObjectMethod objectMethod;
        public ProPixelizerOptions proPixelizer;
        public bool LowRes(PixelLookCategory c) =>
            camera != PixelatorCamera.LowRes && objectPixelSize > 1 &&
            (c == PixelLookCategory.Character ? lowResCharacters : c == PixelLookCategory.Prop && lowResProps);
        public bool Has(PixelLookCategory c) => c switch
        {
            PixelLookCategory.Architecture => architecture,
            PixelLookCategory.Prop => props,
            _ => characters,
        };
    }

    static readonly int TexelScaleId = Shader.PropertyToID("_PixelLookTexelScale");
    const float RescanSeconds = .25f;

    public bool Active { get; private set; }
    public PixelatorCamera CameraMode => Active ? parts.camera : PixelatorCamera.Default;
    public PixelLookLibrary Library { get; private set; }

    readonly List<Material> buffer = new();
    readonly Dictionary<Renderer, bool> isCharacter = new();
    readonly Dictionary<Material, Material> objectTwin = new();     // Pixel Lit material -> its per-object twin
    readonly Dictionary<Material, Material> twinOriginal = new();   // twin -> the game's original material
    readonly Dictionary<Material, Material> proTwin = new();        // Pixel Lit material -> ProPixelizer twin
    Shader objectShader, proShader;
    ScriptableRendererFeature proFeature;
    bool proFeatureWasActive, proFeatureTouched;
    Parts parts;
    float nextScan;

    public void Set(PixelLookLibrary library, bool on, Parts which)
    {
        bool active = on && library != null;
        if (active == Active && library == Library && which.Equals(parts)) { RefreshProTwins(); return; }
        if (Library != null && Library != library) SwapAll(restoreAll: true);
        Library = library;
        Active = active;
        parts = which;
        if (Library != null)
        {
            Library.Rebuild();
            SwapAll(restoreAll: !Active);
            Shader.SetGlobalFloat(TexelScaleId, TexelScale);
        }
        bool lowResObjects = Active && (parts.LowRes(PixelLookCategory.Character) || parts.LowRes(PixelLookCategory.Prop));
        bool pro = lowResObjects && parts.objectMethod == PixelatorObjectMethod.ProPixelizer;
        PixelObjectFeature.PixelSize = Mathf.Max(1, parts.objectPixelSize);
        PixelObjectFeature.Enabled = lowResObjects && !pro;
        SetProFeature(pro);
        RefreshProTwins();
        if (ScreenManager.HasInstance) ScreenManager.Instance.ApplyRenderingEffects();
    }

    public void Tick()
    {
        if (!Active || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + RescanSeconds;
        if (isCharacter.Count > 4096) isCharacter.Clear(); // forget destroyed renderers
        Shader.SetGlobalFloat(TexelScaleId, TexelScale);
        SwapAll(restoreAll: false);
    }

    float TexelScale => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
        ? Library.tableTexelScale : Library.roomTexelScale;

    void SwapAll(bool restoreAll)
    {
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            Swap(r, restoreAll);
    }

    void Swap(Renderer r, bool restoreAll)
    {
        if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return;
        if (!isCharacter.TryGetValue(r, out bool character))
            isCharacter[r] = character = r is SkinnedMeshRenderer || r.GetComponentInParent<Character>(true) != null;
        r.GetSharedMaterials(buffer);
        bool changed = false;
        for (int i = 0; i < buffer.Count; i++)
        {
            var m = buffer[i];
            var original = (m != null && twinOriginal.TryGetValue(m, out var o) ? o : null) ?? Library.OriginalFor(m) ?? m;
            var entry = Library.EntryFor(original);
            if (entry == null) continue;
            var category = character ? PixelLookCategory.Character : entry.category;
            var wanted = original;
            if (!restoreAll && Active && parts.Has(category))
                wanted = !parts.LowRes(category) ? entry.pixel
                       : parts.objectMethod == PixelatorObjectMethod.ProPixelizer ? ProTwinOf(entry) : TwinOf(entry);
            if (wanted == m) continue;
            buffer[i] = wanted;
            changed = true;
        }
        if (changed) r.SetSharedMaterials(buffer);
    }

    // The per-object twin: same material on Hidden/LoomRoom/Pixel Lit Object, made once per run.
    Material TwinOf(PixelLookLibrary.Entry entry)
    {
        if (objectTwin.TryGetValue(entry.pixel, out var twin) && twin != null) return twin;
        if (objectShader == null) objectShader = Shader.Find("Hidden/LoomRoom/Pixel Lit Object");
        if (objectShader == null) return entry.pixel;
        twin = new Material(entry.pixel) { shader = objectShader, name = entry.pixel.name + " (object)" };
        twin.renderQueue = entry.pixel.renderQueue;
        objectTwin[entry.pixel] = twin;
        twinOriginal[twin] = entry.original;
        return twin;
    }

    // ── ProPixelizer test ─────────────────────────────────────────────

    static readonly int ProBaseMap = Shader.PropertyToID("_BaseMap"), ProBaseMapST = Shader.PropertyToID("_BaseMap_ST"),
        ProBaseColor = Shader.PropertyToID("_BaseColor"), ProPixelSize = Shader.PropertyToID("_PixelSize"),
        ProRamp = Shader.PropertyToID("_LightingRamp"), ProAmbient = Shader.PropertyToID("_AmbientLight"),
        ProOutline = Shader.PropertyToID("_OutlineColor"), ProEdge = Shader.PropertyToID("_EdgeHighlightColor"),
        ProPalette = Shader.PropertyToID("_PaletteLUT"), ProId = Shader.PropertyToID("_ID"),
        ProEmission = Shader.PropertyToID("_EmissionColor"), ProEmissionMap = Shader.PropertyToID("_EmissionMap"), ProClip = Shader.PropertyToID("_AlphaClipThreshold");

    // The same material on ProPixelizer/SRP/PixelizedWithOutline, made once per run.
    Material ProTwinOf(PixelLookLibrary.Entry entry)
    {
        if (proTwin.TryGetValue(entry.pixel, out var twin) && twin != null) return twin;
        if (proShader == null) proShader = Shader.Find("ProPixelizer/SRP/PixelizedWithOutline");
        if (proShader == null) return TwinOf(entry);
        var src = entry.pixel;
        twin = new Material(proShader) { name = src.name + " (ProPixelizer)" };
        twin.SetTexture(ProBaseMap, src.GetTexture("_BaseMap"));
        var scale = src.GetTextureScale("_BaseMap"); var offset = src.GetTextureOffset("_BaseMap");
        twin.SetVector(ProBaseMapST, new Vector4(scale.x, scale.y, offset.x, offset.y));
        twin.SetColor(ProBaseColor, src.GetColor("_BaseColor"));
        // Emission is colour x map; without the map ProPixelizer's default white map lights the whole model.
        var emissionMap = src.GetTexture("_EmissionMap");
        twin.SetColor(ProEmission, emissionMap != null || src.GetColor("_EmissionColor").maxColorComponent <= 0 ? src.GetColor("_EmissionColor") : Color.black);
        if (emissionMap != null) twin.SetTexture(ProEmissionMap, emissionMap);
        twin.SetFloat(ProClip, src.GetFloat("_AlphaClip") > .5f ? src.GetFloat("_Cutoff") : 0f);
        twin.SetFloat(ProId, 1 + proTwin.Count % 254);
        proTwin[src] = twin;
        twinOriginal[twin] = entry.original;
        ApplyProOptions(twin);
        return twin;
    }

    void RefreshProTwins()
    {
        foreach (var twin in proTwin.Values) if (twin != null) ApplyProOptions(twin);
    }

    void ApplyProOptions(Material m)
    {
        var o = parts.proPixelizer;
        if (o == null) return;
        m.SetFloat(ProPixelSize, o.pixelSize);
        if (o.lightingRamp != null) m.SetTexture(ProRamp, o.lightingRamp);
        m.SetColor(ProAmbient, o.ambientLight);
        m.SetColor(ProOutline, o.outlineColor);
        m.SetColor(ProEdge, o.edgeHighlightColor);
        SetToggle(m, "PROPIXELIZER_DITHERING", o.dithering);
        SetToggle(m, "COLOR_GRADING", o.colorGrading && o.paletteLUT != null);
        if (o.paletteLUT != null) m.SetTexture(ProPalette, o.paletteLUT);
        SetToggle(m, "RECEIVE_SHADOWS", true);
    }

    static void SetToggle(Material m, string name, bool on)
    {
        m.SetFloat(name, on ? 1 : 0);
        if (on) m.EnableKeyword(name + "_ON"); else m.DisableKeyword(name + "_ON");
    }

    // ProPixelizer's renderer feature lives on the Desktop Renderer, off by default; switched on only
    // while it is the object method, and put back as found afterwards.
    void SetProFeature(bool on)
    {
        if (proFeature == null)
            foreach (var f in Resources.FindObjectsOfTypeAll<ProPixelizer.ProPixelizerRenderFeature>()) { proFeature = f; break; }
        if (proFeature == null) return;
        if (on && !proFeatureTouched) { proFeatureWasActive = proFeature.isActive; proFeatureTouched = true; }
        if (on) proFeature.SetActive(true);
        else if (proFeatureTouched) { proFeature.SetActive(proFeatureWasActive); proFeatureTouched = false; }
    }
}
