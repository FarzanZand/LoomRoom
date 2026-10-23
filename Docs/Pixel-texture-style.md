# Pixel texture style

The house style for pixel textures, picked on 2026-10-12 from the cave bat and urn crawler concepts. Whenever a pixel texture is asked for (a creature, prop, item or surface), it should look like these.

![Cave bat](Images/Pixel%20texture%20style/Cave%20bat.png)
![Urn crawler](Images/Pixel%20texture%20style/Urn%20crawler.png)

## What makes it

- **Low resolution, visible texels.** The pixels read as chunky squares on the model, roughly as dense as the restyled Synty characters (about 40 texels per metre), never smooth.
- **Dirty, not clean.** Grime, stains, chips, scabs, damp patches and moss; blotchy colour that varies from texel to texel. No flat fills, no airbrushed gradients, no clean cel shading.
- **Hand-placed look.** Colour changes in small steps between neighbouring texels, like pixel art painted by hand: dark outlines in creases, lighter texels on raised edges, a few single bright texels as highlights.
- **Muted base, one accent.** Earthy, slightly desaturated base colours (brown, grey, bone, dull green), with one bright accent that reads in the dark (red or yellow eyes, a glowing mark).
- **Wear tells the story.** Torn wing membranes, cracked clay, chipped bone, wax drips, grave dirt: the surface says where the thing has been.
- **Low-poly shapes under it.** Chunky forms with flat facets; the texture carries the detail, not the geometry.

## Prompt wording that produced it

For Meshy concepts (`python Tools/meshy.py concept`), end the description with:

> Textures are low-resolution dirty pixel art: grimy, stained, chipped, uneven blotchy colour, hand-placed pixels, visible chunky texels, no clean smooth gradients or flat shading. 3/4 front view, plain dark background, single creature, no text, game asset

Add "no text": one of the two references came back with a caption baked into the image.

## In the project

The full pipeline is the `loomroom-art` skill (`.claude/skills/loomroom-art/SKILL.md`); in short:

- Creatures: a Meshy concept with the wording above, the user's approval, then `meshy.py image` (never Meshy retexture), then Pixel Look > Build Materials.
- Weapons, shields and other flat props are painted restyles (`Development/Editor/PixelRestyle.Painted.cs`): front and back reference renders are repainted with `meshy.py concept` in this style and projected back onto the model. Every weapon and shield prefab uses one (`Art/Synty Pixel/{Weapons,Shields}/<mesh>/<Item> painted.*`).
- One pixel size for all game art: **128 texels per metre** (`PixelRestyle.ItemTexelsPerUnit`), for everything made from 2026-10-12 on: props, items, creatures, characters and walls (a 3 m wall tile is 384 texels; the Church room style in `Levels/Shared/Room styles/HighRes Materials` is the first at it). Art made before then is at 150 until it is baked again; the Pixel Look library's `characterDetail` and `firstPersonDetail` stay 14 (150) until it is, then become 12. Textures are sized exactly from the UV area, never rounded to a power of two.
- Procedural noise painting was tried for weapons (`PixelRestyle.Items.cs`, the Dirt grade, `<mesh> dirty.*`) and is not used.
