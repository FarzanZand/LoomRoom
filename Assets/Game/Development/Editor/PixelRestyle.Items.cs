// The house pixel size for items, creatures and all new art (Docs/Pixel-texture-style.md): every model is painted at
// the same density in the world, so pixels are the same size in the hand, on the floor and on a creature. 128 texels per
// world unit since 2026-10-12 (was 150; art baked before then keeps 150 until it is baked again). The first item
// restyle (the noise-based "dirty" grade) lived here; the painted restyles (PixelRestyle.Painted.cs) replaced it.
public static partial class PixelRestyle
{
    public const float ItemTexelsPerUnit = 128f;
}
