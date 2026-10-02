using Sirenix.OdinInspector;
using UnityEngine;

public enum PixelatorObjectMethod
{
    LoomRoom    = 0, // Pixel Lit Object + PixelObjectFeature: keeps the stepped per-texel lighting
    ProPixelizer = 1, // ProPixelizer's own shader and renderer feature: toon ramp lighting, outlines
}

// Pixelator test settings for ProPixelizer (Assets/ProPixelizer). Applied to runtime copies of the
// game's materials on ProPixelizer/SRP/PixelizedWithOutline; edit them live in Play mode.
[System.Serializable]
public class ProPixelizerOptions
{
    [Range(1, 5), Tooltip("Screen pixels per object pixel (ProPixelizer allows 1 to 5).")]
    public int pixelSize = 4;
    [Tooltip("Light ramp: how NdotL maps to brightness. Assets/ProPixelizer/Ramps has smooth, default and harsh.")]
    public Texture2D lightingRamp;
    [Tooltip("Ambient light added on top of the ramp.")]
    public Color ambientLight = new(.2f, .2f, .2f, 1f);
    [Tooltip("Outline colour; alpha is the outline strength (0 = no outline).")]
    public Color outlineColor = new(0f, 0f, 0f, .5f);
    [Tooltip("Highlight on inner edges found from normals; alpha is the strength.")]
    public Color edgeHighlightColor = new(.5f, .5f, .5f, 0f);
    [Tooltip("ProPixelizer's ordered dithering between ramp bands.")]
    public bool dithering = true;
    [Tooltip("Snap colours to a palette lookup (Assets/ProPixelizer/Palettes, the *_lookup textures).")]
    public bool colorGrading;
    [ShowIf(nameof(colorGrading))] public Texture2D paletteLUT;
}
