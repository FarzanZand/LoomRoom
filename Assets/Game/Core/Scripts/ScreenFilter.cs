// The whole-screen look picked on ScreenManager (Camera filter). Standard is the game's own look; every other one is a
// ScreenFilterLook asset (Rendering/Filters) drawn by the Retro Screen pass and, for some, a volume profile.
// Serialized by integer: explicit values, append only, retired numbers stay retired.
public enum ScreenFilter
{
    Standard = 0,
    Ghost = 1,        // the co-op ghost's cold view (Multiplayer/Ghost look)
    Noir = 2,         // black and white, crushed, red kept
    Sepia = 3,        // an old photo: brown tones, vignette, grain
    Crt = 4,          // a curved tube: scanlines and a shadow mask
    Vhs = 5,          // a worn tape: colour bleed, soft picture, a wobble now and then
    Handheld = 6,     // four greens
    FourTone = 7,     // four cold dungeon tones: black, slate, teal, bone
    ClassicOne = 8,   // crisp and bright: punchy contrast, richer colour, warm glowing highlights
    ClassicTwo = 9,   // dark and moody: deep contrast, quieter colour, warm light out of the dark
    Vhs2 = 10,        // Vhs without the slipping band
    Film = 11,        // a little grain, a soft vignette, slightly warm
    Faded = 12,       // lifted blacks and quieter colour, like an old print
    Cold = 13,        // a cool, blue-grey grade
    Warm = 14,        // a golden grade
}
