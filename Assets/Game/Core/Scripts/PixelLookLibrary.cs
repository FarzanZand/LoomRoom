using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The Pixel Look: every world material paired with a LoomRoom/Pixel Lit copy, plus the camera
// settings used while the look is on. ScreenManager.pixelLook switches between the two; the
// original materials are never modified. Build and refresh the pairs with Tools > LoomRoom > Pixel Look.
[CreateAssetMenu(menuName = "LoomRoom/Pixel Look Library")]
public class PixelLookLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public Material original;
        public Material pixel;
        [Tooltip("Which Pixel Look toggle switches this material. Renderers under a Character or skinned are always Character.")]
        public PixelLookCategory category = PixelLookCategory.Prop;
    }

    [FoldoutGroup("Low Res camera"), LabelText("Room effects")]
    [Tooltip("Room player's screen effects with the Low Res camera. Pixel Lines here is a real render resolution, not a filter.")]
    public ScreenEffectSettings roomEffects = new()
    {
        ambientOcclusion = false,
        pixelate = true, pixelLines = 270,
        colorBanding = false, colorLevels = 48, dither = 1f,
        vignette = true, vignetteIntensity = .3f, vignetteSmoothness = .5f, vignetteColor = Color.black,
        colorGrading = true, contrast = 12f, saturation = 5f, colorFilter = Color.white, postExposure = .35f,
    };
    [FoldoutGroup("Low Res camera"), LabelText("Table effects"), Tooltip("Table player's screen effects with the Low Res camera.")]
    public ScreenEffectSettings tableEffects = new()
    {
        ambientOcclusion = false,
        pixelate = true, pixelLines = 270,
        colorBanding = true, colorLevels = 48, dither = 1f,
        vignette = true, vignetteIntensity = .3f, vignetteSmoothness = .5f, vignetteColor = Color.black,
        colorGrading = true, contrast = 12f, saturation = 5f, colorFilter = Color.white, postExposure = 0f,
    };

    [FoldoutGroup("Clean camera"), LabelText("Room effects")]
    [Tooltip("Room player's screen effects with the Clean camera: native resolution, no anti-aliasing. Pixelate here is the old block filter.")]
    public ScreenEffectSettings cleanRoomEffects = new()
    {
        ambientOcclusion = false,
        vignette = true, vignetteIntensity = .3f, vignetteSmoothness = .5f, vignetteColor = Color.black,
        colorGrading = true, contrast = 10f, saturation = 0f, colorFilter = Color.white, postExposure = .3f,
    };
    [FoldoutGroup("Clean camera"), LabelText("Table effects"), Tooltip("Table player's screen effects with the Clean camera.")]
    public ScreenEffectSettings cleanTableEffects = new()
    {
        ambientOcclusion = false,
        vignette = true, vignetteIntensity = .3f, vignetteSmoothness = .5f, vignetteColor = Color.black,
        colorGrading = true, contrast = 10f, saturation = 0f, colorFilter = Color.white, postExposure = 0f,
    };

    [Header("Texels")]
    [Tooltip("Pixel Lit materials give texels per metre at dungeon scale (1 unit = 1 m). The room is built about 22 units to the metre, so its world units get this many metres each. Lower = chunkier texels.")]
    public float roomTexelScale = .03f;
    [Tooltip("Same for the Table player (dungeons and town).")]
    public float tableTexelScale = 1f;

    [Header("Brightness")]
    [Range(.1f, 3f), Tooltip("Multiplies all light on Pixel Lit materials while the Room player is active, on every camera mode. 1 = as lit; 1.2 = 20% lighter. Live in Play mode.")]
    public float roomBrightness = 1.2f;
    [Range(.1f, 3f), Tooltip("Same for the Table player (dungeons and town).")]
    public float tableBrightness = 1f;

    [Header("Materials")]
    [Tooltip("Hidden/LoomRoom/Pixel Lit Object (Rendering/PixelLook/PixelLitObject.shader), the per-object pixelation shader. Referenced here so player builds include it.")]
    public Shader objectShader;
    [Tooltip("Folder the generated Pixel Lit materials are written to.")]
    [FolderPath] public string outputFolder = "Assets/Game/Rendering/PixelLook/Materials";
    [TableList(AlwaysExpanded = false, ShowPaging = true, NumberOfItemsPerPage = 30)]
    public List<Entry> entries = new();
    [Tooltip("Materials Build Materials leaves alone: they keep drawing as they are. For fine printed artwork, like the board's grid, that stripes when point-sampled.")]
    public List<Material> excluded = new();

    Dictionary<Material, Entry> byOriginal;
    Dictionary<Material, Material> toOriginal;

    public void Rebuild()
    {
        byOriginal = new Dictionary<Material, Entry>();
        toOriginal = new Dictionary<Material, Material>();
        foreach (var e in entries)
        {
            if (e.original == null || e.pixel == null) continue;
            byOriginal[e.original] = e;
            toOriginal[e.pixel] = e.original;
        }
    }

    public Entry EntryFor(Material original)
    {
        if (byOriginal == null) Rebuild();
        return original != null && byOriginal.TryGetValue(original, out var e) ? e : null;
    }

    public Material OriginalFor(Material pixel)
    {
        if (toOriginal == null) Rebuild();
        return pixel != null && toOriginal.TryGetValue(pixel, out var m) ? m : null;
    }

    void OnValidate() { byOriginal = null; toOriginal = null; }

    // ── Pixel lighting ───────────────────────────────────────────────
    // The game's own materials switched between their own shader and LoomRoom/Pixel Lit in place (ScreenManager >
    // Pixel lighting, F6 in Play mode). Filled by Tools > LoomRoom > Pixel Look > Register Pixel Lighting Materials.

    [System.Serializable]
    public class LitEntry
    {
        public Material material;
        public Shader original;
        public int originalQueue = -1;
        public int pixelQueue = -1;
    }

    [FoldoutGroup("Pixel lighting"), Tooltip("LoomRoom/Pixel Lit.")]
    public Shader pixelLitShader;
    [FoldoutGroup("Pixel lighting"), TableList(AlwaysExpanded = false, ShowPaging = true, NumberOfItemsPerPage = 30)]
    public List<LitEntry> lighting = new();

    public void SetPixelLighting(bool on)
    {
        if (pixelLitShader == null) return;
        foreach (var e in lighting)
        {
            if (e.material == null || e.original == null) continue;
            var shader = on ? pixelLitShader : e.original;
            if (e.material.shader != shader) e.material.shader = shader;
            e.material.renderQueue = on ? e.pixelQueue : e.originalQueue;
        }
    }
}
