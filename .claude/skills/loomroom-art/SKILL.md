---
name: loomroom-art
description: The main art skill for LoomRoom. Use for ANY art request in this game: new creatures, enemies, props, weapons, shields, items, characters, or retexturing/restyling anything ("make it pixel", "retexture", "redo the texture", "new model", "concept"). Defines the house dirty-pixel style and the one pipeline every asset goes through (concept image in the house style -> approval -> applied to the model at a fixed pixel density -> full-size review).
---

# LoomRoom art: the dirty pixel pipeline

Every piece of art in LoomRoom looks like the approved creature concepts (cave bat, urn crawler, crypt rat):
low-resolution, chunky, **dirty** pixel art painted by an image model, never procedural noise and never Meshy
*retexture*. Reference images: `Docs/Images/Pixel texture style/`. Style notes: `Docs/Pixel-texture-style.md`.

## Tools: what to use and what never to use

- **Images: Meshy's image generator only** (`python Tools/meshy.py concept ...`). It made every approved creature.
  It costs **9 Meshy credits per image** (the user's Meshy Pro account, not Claude tokens); check the balance with
  `python Tools/meshy.py balance` and say what a batch will cost before a big run.
- **Never Meshy retexture** (it scrambles the Synty models) and **no procedural/noise texture painting** (rejected).
- **Small edits by hand, not Meshy**: marks and glyphs drawn texel by texel (colours sampled from the texture, bevel light
  from the upper left), cropping, aligning, colour matching, compositing and making things tile are done directly on the
  pixels. Meshy only paints genuinely new content (user, 2026-10-12: "Don't use meshy for things you can do easily").
- **3D models from Meshy (`image`, `rig`) only when the user asks for a new model** after approving its concept.

## The style (what to ask for, what to check)

- Low resolution with visible chunky texels; colour changes in small hand-placed steps.
- Dirty: grime, stains, chips, scabs, rust, moss, wax drips, grave dirt. Blotchy colour that varies texel to texel.
- Coherent material detail you can read (bone ridges, fur clumps, cracks, planks, rivets, leather wraps), not random speckle.
- Muted earthy base colours with one bright accent (red or yellow eyes, a glowing mark).
- Low-poly shapes that are **organic and continuous** (one body, limbs growing out of shoulders and haunches). The pixel
  look lives only in the texture: **never voxel or cube-built shapes, never limbs that look like separate attached boxes**
  (rejected rat, 2026-10-12: "looks voxel based... limbs are separate attached parts"; texture was fine, mesh was bad).
  Put "one continuous organic body, no voxels, no cube or box shapes, no separate attached parts; the pixel style is
  only in the texture, not in the shapes" in creature prompts.
- Original designs: nothing that looks like a stock monster from other games.

**Style wording (end every image prompt with this):**

> Textures are low-resolution dirty pixel art: grimy, stained, chipped, uneven blotchy colour, hand-placed pixels, visible chunky texels, no clean smooth gradients or flat shading. 3/4 front view, plain dark background, single creature, no text, game asset

For props seen straight on, replace the last sentence with the framing in step 2 of the props pipeline.

## Pipeline A: creatures and characters (new models)

1. **Concept**: `python Tools/meshy.py concept --name "<Name>" --out Temp/Concepts --aspect 1:1 "<description>. <style wording>"`.
   Add `--ref "Docs/Images/Pixel texture style/Cave bat.png" --ref "Docs/Images/Pixel texture style/Urn crawler.png"` to pin the style.
   Make two or three if unsure; show them to the user side by side.
2. **Approval**: wait for the user's OK before any 3D work (they pick, or ask for changes: e.g. "make it different", "dirtier").
3. **Model**: `python Tools/meshy.py image` from the approved concept (crop any baked-in caption first), then `rig` for creatures.
   Meshy textures: import at 512 px, Point filter (never below 1024 source; 256 px smears faces).
4. **In game**: enemy prefab via `Tools > LoomRoom > New Enemy` (variant of the right base), gore profile on its data
   (`CharacterData.gore`: blood kind, colour, kill sound), then `Tools > LoomRoom > Pixel Look > Build Materials` and
   `Register Pixel Lighting Materials`, then `Even Out Texel Sizes`.

## Pipeline B: props, weapons, shields, items (retexture existing models)

Flat props (swords, knives, maces, shields, planks, signs) are painted straight on and projected back
(`Assets/Game/Development/Editor/PixelRestyle.Painted.cs`):

1. **Reference renders**: `PixelRestyle.RenderReferences("<Synty prefab path>", "<Name>")` writes
   `Temp/Paint/<Name> front.png` and `back.png` (flat lit, straight on, plain grey background).
2. **Paint** each view with the creature pipeline:
   `python Tools/meshy.py concept --name "<Name> front" --out Temp/Paint/gen --aspect 1:1 --ref "Temp/Paint/<Name> front.png" --ref "Docs/Images/Pixel texture style/Cave bat.png" --ref "Docs/Images/Pixel texture style/Urn crawler.png" "Repaint the object in the first reference image exactly as it is: the same silhouette, the same proportions, the same position and size in the frame, seen straight on, on the same plain flat grey background, nothing added around it. <what it is and its materials, worn and dirty>. Textures are low-resolution dirty pixel art: grimy, stained, chipped, uneven blotchy colour, hand-placed pixels, visible chunky texels, no clean smooth gradients or flat shading. Paint it in the same pixel art style as the creature reference images. No text."`
   Copy the result to `Temp/Paint/<Name> front painted.png` (and `back painted.png`). Check the silhouette matches the render;
   regenerate if the painter moved or reshaped it.
3. **Project**: `PixelRestyle.BuildPainted("<ItemData path>", "<Synty prefab path>", "<Name>")` projects the paintings onto
   fresh UVs at the fixed item density and swaps the item's prefab to the result (`Art/Synty Pixel/{Weapons,Shields}/`).
4. Rounder props (barrels, pots, chests) need more views; until that exists, paint the side view and project around the axis.

## Pixel size: identical across everything (128 texels per metre)

- **One pixel size for all game art: 128 texels per world metre** (since 2026-10-12; it was 150, and art baked before then stays at 150 until it is baked again) (items in hand and on the floor, creatures, characters),
  the size of the painted pixels in the approved bat / urn crawler / rat concepts (one painted pixel is ~6 px of a 1024
  concept image across a ~1 m rat). `PixelRestyle.ItemTexelsPerUnit` = 128; the Pixel Look library's `firstPersonDetail`
  and `characterDetail` = 14 (x `tableTexelsPerUnit` 10.7) so the pixel lighting never coarsens them.
- Bake at 128 x the model's world scale (texels per mesh unit). Textures are sized **exactly** from the unwrapped UV area
  (`PixelRestyle.Build`): never round to a power of two, never cap or floor the size (that gave a knife 114, a shield 32
  and the rat 219 texels per metre, and the user saw the mismatch at once).
- Texture importer: `npotScale = None` (Unity otherwise snaps every texture to a power of two and undoes the exact
  size). `BuildPainted` bakes, measures (`PixelRestyle.TexelsPerUnit`), and re-bakes until within 5% of 128.
- Reference: `Docs/Images/Pixel texture style/Weapons and shields 150.png` (all items at one world scale, same pixels; made at the old 150).
- Surfaces (walls, floors, ceilings): one texel per painted pixel at 128 per metre. Meshy paints about 200 pixels across with "Detailed pixel art at 320 by 320 pixel resolution" (100 without it); sample one texel per painted pixel and set the material's tiling to `tile x 128 / width` (UVs repeat once per 3 m architecture tile). The Church style (`Levels/Dungeon1/Styles/HighRes Materials`) is built this way.
- Meshy creature textures: resample so the model lands at 128 per metre (measure with `PixelRestyle.TexelsPerUnit`).
- Check it: render items and creatures side by side **at the same world scale** (not each zoomed to fit) and compare the
  pixel squares. Dungeon walls and floors stay 10.7; the apartment room 1.8.
- After any new art run `Tools > LoomRoom > Pixel Look > Even Out Texel Sizes`.

## Review before calling it done

- Look at every asset at full size, side by side with the reference creatures and with the old version.
- Then in game: hold it, drop it, fight with it (`Development/CombatTestBot.cs`: `capture` frame sheets).
- Show the user stills; never say "good" from small contact sheets. If the user says it isn't up to the creatures, redo it
  with this pipeline, from scratch if needed.

## Tiling surfaces (walls, floors, ceilings): check before showing

Lessons from the Church room styles (`Levels/Dungeon1/Styles/HighRes Materials`, 2026-10-12). Lay the textures out as
they will repeat (a strip of 6-8 wall tiles with the variants in place, the floor 3x3) at full size and look for:

- **Distinct features in the base repeat.** Anything that catches the eye (a mould or soot blotch, a stain, a mark, a
  block much darker or lighter than the rest) becomes an obvious pattern once it repeats. Meshy often paints a two-course
  repeat inside one image, so one blotch appears several times per tile. Take it out of the base (fill only the blotch from
  the same block's clean stone, same row) and put it on a rare variant, once. Even out odd blocks per colour channel against
  their neighbours in the same course, not against one block (that overshoots and turns them grey).
- **Keep the ordinary detail.** Cracks, chips, bevels and small moss in joints are what make the stone; never strip them
  wholesale. Remove only the blobs (many dark neighbours) and their drips; thin cracks stay.
- **One repeat per tile.** A texture that repeats inside a cell cuts variant features at cell edges and doubles them
  (two moss bands up one wall tile). Floors: one repeat per 2x2 cell (256 px at 128/unit, material offset 0.5 because cell
  edges sit at odd world coordinates). Walls: 256 wide per 2-unit segment; variants are full tile height (256x384) so a
  feature shows once.
- **Variants grow on the base.** A variant must be the base with something added, never a different painting pasted in:
  repaints drift in block size, colour and course height and read as a patch. Align every wall texture so a mortar line is
  row 0, colour match against the base's plain stone, and add features as overlays (moss pixels copied onto the base,
  hand-drawn marks and soot) or as halves cut on mortar lines.
- **Pixel size.** Painted pixels must stay square and the material's texels exactly 128 per unit (tiling = tile x 128 /
  texture size); never stretch a painting to a different aspect.
