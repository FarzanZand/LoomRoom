# Desktop URP rendering

The Graphics settings and all quality levels use Desktop URP.asset (URP 17.5).
Desktop Renderer.asset uses Forward+ with screen-space ambient occlusion.

Adjust overall rendering cost in Desktop URP.asset: render scale is 1.15,
shadow distance is 150 and the main/additional shadow atlases are 4096.
The shared output camera uses HDR, high-quality SMAA and post-processing.

Tabletop Filmic.asset contains ACES tone mapping, color grading, subtle bloom
and vignette. Its global Volume is Lighting/Filmic Color and Bloom.

TableManager/Tabletop Lighting contains the warm key, cool rim, soft fill
and local reflection probe. The apartment point lights were retuned for URP.
TerrainTable1 uses Table Terrain URP.mat. Its foliage contains combined mesh
grass patches with no gameplay colliders; the Meadow mesh assets live here.

Standard and legacy particle materials were converted to URP equivalents.
Synty Shader Graph materials retain their native URP support. Cartoon FX
shader importers are set to Universal Render Pipeline.
