using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

// One camera filter (ScreenFilter) as ScreenManager draws it: settings for the Retro Screen pass (Rendering/RetroScreen.shader)
// and an optional volume profile laid over the scene's own. Runs after the world and its post-processing and before the
// UI, so materials, pixel lighting, texel sizes and the HUD are never touched. Made by Tools > LoomRoom > Rendering >
// Build Screen Filters (keeps existing assets); tune the numbers here freely.
[CreateAssetMenu(menuName = "LoomRoom/Screen Filter Look", fileName = "Screen filter")]
public class ScreenFilterLook : ScriptableObject
{
    public enum ColourMode { Keep = 0, Grade = 1, Mono = 2, Palette = 3 }

    public ScreenFilter filter;
    [Tooltip("Laid over everything while this filter is on (above the room, table and death effects). Empty = none.")]
    public VolumeProfile profile;

    [Title("Pixels and colours")]
    [Range(0, 1080), Tooltip("Vertical resolution of the pixel grid while this filter is on. 0 = whatever the room or table effects use.")]
    public int pixelLines;
    [Range(0, 64), Tooltip("Shades per colour channel (with Mono or Palette: of the picture's brightness). 0 = whatever the effects use.")]
    public int colourLevels;
    [Range(0, 1), Tooltip("How much the ordered dither spreads between the shades.")]
    public float dither = 1f;
    [ValueDropdown(nameof(BayerSizes)), Tooltip("Size of the ordered dither pattern: 2 coarse checker, 4, 8 fine.")]
    public int ditherSize = 4;
    [Range(1, 4), Tooltip("Pixels of the grid per dither cell: above 1 makes the pattern coarser than the picture.")]
    public int ditherScale = 1;
    [Tooltip("Dither the brightness only: a dark checker over the colour instead of red, green and blue speckle.")]
    public bool ditherBrightness;
    [Range(0, 1), Tooltip("How much of each step between two shades is dithered: 1 the whole step, less leaves solid fills near each shade and dithers only the middle (shapes read better).")]
    public float ditherBand = 1f;
    [Range(0, .3f), Tooltip("Blacks lifted to this grey, so the darks become a dotted haze instead of solid black.")]
    public float blackLift;
    [Range(0, 1), Tooltip("Bright light (torches, glows) broken into a grid of lit squares with dark lines between.")]
    public float glowGrid;
    [Range(0, 1.5f), Tooltip("Crisper detail: edges and stone texture stand out more.")]
    public float sharpen;
    static int[] BayerSizes => new[] { 2, 4, 8 };

    [Title("Colour")]
    public ColourMode colour = ColourMode.Keep;
    [ShowIf(nameof(colour), ColourMode.Grade), Range(0, 2)] public float saturation = 1f;
    [HideIf(nameof(colour), ColourMode.Keep), Range(-1, 1), Tooltip("Above 0 pushes darks down and lights up around the middle grey.")] public float contrast;
    [ShowIf(nameof(colour), ColourMode.Grade), Tooltip("Shadows lean to this colour (alpha = how much).")] public Color shadowTint = new(.2f, .55f, .6f, 0f);
    [ShowIf(nameof(colour), ColourMode.Grade), Tooltip("Lights lean to this colour (alpha = how much).")] public Color lightTint = new(1f, .65f, .35f, 0f);
    [ShowIf(nameof(colour), ColourMode.Mono)] public Color monoDark = Color.black;
    [ShowIf(nameof(colour), ColourMode.Mono)] public Color monoLight = Color.white;
    [ShowIf(nameof(colour), ColourMode.Mono), Range(0, 1), Tooltip("Strong reds stay in colour (blood, the hurt flash).")] public float keepRed;
    [ShowIf(nameof(colour), ColourMode.Palette), Tooltip("Darkest to lightest; the picture's brightness picks between them, dithered.")]
    public Color[] palette = { Color.black, Color.gray, Color.gray, Color.white };
    [HideIf(nameof(colour), ColourMode.Keep), Range(0, .5f), Tooltip("Brightness below this goes to black before the colour step (crushed shadows).")]
    public float crush;
    [ShowIf("@colour == ColourMode.Mono || colour == ColourMode.Palette"), Range(.3f, 2f), Tooltip("Brightness curve before the tones are picked: below 1 lifts the darks so dark rooms still read.")]
    public float lift = 1f;

    [Title("Screen")]
    [Range(0, .3f), Tooltip("Barrel bulge of a tube screen; outside it is black.")] public float curvature;
    [Range(0, 1)] public float scanlines;
    [Range(240, 1080), ShowIf("@scanlines > 0"), Tooltip("Scanlines across the screen's height.")] public int scanlineCount = 540;
    [Range(0, 1), Tooltip("Red, green and blue stripes of a shadow mask.")] public float mask;
    [Range(0, 8), Tooltip("Colour bleeds sideways by this many grid pixels (a tape's soft chroma).")] public float colourBleed;
    [Range(0, 1), Tooltip("Now and then a band of the picture slips sideways.")] public float wobble;
    [Range(0, 1)] public float vignette;
    [Range(0, 1), Tooltip("Moving noise over the picture.")] public float grain;
}
