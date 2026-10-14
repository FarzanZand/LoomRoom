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
   What the projection does about the painting's edges (all three were missing on the first weapon batch, 2026-10-12, and
   the user found them on the Bronze Shield and Bronze Mace in hand, 2026-10-13):
   - **Background remap** (`NearestPainted`): a sample that lands outside the painted silhouette takes the nearest painted
     pixel, never the grey background (grey bands round grips and rims).
   - **Edge peel**: the painter's dark outline (up to 12 pixels deep, it is often a whole painted pixel thick) and its thin
     plain-grey fringe count as background, or rims pick up black notches and grey specks.
   - **Edge-on faces unfold**: a face nearly edge-on to the view (a shield's rim, a blade's or shaft's sides) samples further
     inward the deeper it lies, so it shows the painted band just inside the outline instead of one pixel streaked across.
   Any change to this code means rebuilding **every** item made with it (`Temp/Paint/list.txt`) and reviewing them all again.
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

## Review every generated image before using it

Open and look at **every single image** Meshy (or any tool) makes, at full size, before it goes into the next step
(user, 2026-10-12). Look for: views moved, merged or rearranged; silhouettes or proportions changed; blocks or patterns
copied from the reference instead of repainted (the old restyle's big square texels on the first torch); text, captions,
extra objects; colours or materials that are wrong for the thing; one view that doesn't match the others. Fix by hand what
can be fixed (crop, re-lay, colour match), regenerate what can't, and only then project or build.

## Review before calling it done

- **Every built model, from every side, up close.** Render `PixelRestyle.RenderPropTurntable(prefab, "Temp/Paint/turn/<Name>.png")`
  (eight sides, two high three-quarter views, straight down and from below, each zoomed to the model) and then crop and
  enlarge the views to at least 4 screen pixels per texel before judging: at the size a whole sheet is shown, streaks
  don't show. One three-quarter preview of a row of props is not a review (2026-10-12: I passed 12 props from one
  front-right angle at about 2 screen pixels per texel and the user found streaked sides on the grave and a blurred side
  panel on the altar at once).
- On each face look for: **stretched texels or streaks** (a face painted from a view it is nearly edge-on to: bevels,
  narrow sides, faces under overhangs); **flat or blurred blocks** much bigger than a texel (a side too narrow on the sheet
  to get painted detail); background grey or the painter's black outline on edges; wrong material (a top painted as stone
  where it is earth); seams where two views meet; unpainted bottoms that can be seen in game.
- Check the review image is new (its file time is after the build) before judging it: an editor call can fail
  quietly and leave the last render in place. The same goes for builds: read `Temp/Paint/build.log` for a line per item
  with the time of this run (a locked texture file fails one item and the old one stays).
- **These rules cover weapons, shields and items too, not only props.** The first weapon and shield batch was judged
  only from a whole-row preview and in-hand views, where the rims are a few screen pixels: streaked rims, black outline
  notches and grey bands all passed and the user found them in game. Turntable every item; look hardest at the
  **edge-on view** (third column of the turntable) and the three-quarter views, where rims, blade sides and the sides of
  a shaft face the camera.
- Compare each texture with its painting: a colour on the model that the painting doesn't have (plain grey, black,
  pinkish grey) came from the background or the outline, not the painter.
- When a review turns up a pipeline defect, fix the pipeline, then rebuild and re-review everything that pipeline made,
  not only the item that was reported.
- Five-view sheet projection (`PixelRestyle.PaintedProps.cs`) only suits blocky props made of large faces (barrels,
  crates, chests, pots, fountains). On organic or intricate models (the gargoyle, carved table legs, chairs, books on
  shelves) every view covers only a sliver of each surface, and the result is camouflage-like noise, black outline
  flecks and smears. Don't use it for those; say so and ask how to proceed.
- Look at every asset at full size, side by side with the reference creatures and with the old version.
- Then in game: hold it, drop it, fight with it (`Development/CombatTestBot.cs`: `capture` frame sheets).
- Show the user stills; never say "good" from small contact sheets. If the user says it isn't up to the creatures, redo it
  with this pipeline, from scratch if needed.

## Hard rules learned the hard way (2026-10-14 bedroom pass; the user found every one of these before I did)

**Review**
- **Never judge a prop from a whole turntable sheet.** At sheet size a view is a few hundred pixels and smears, grey
  patches, dark outline flecks and stretched streaks are invisible. For every prop crop each view to its object and
  enlarge it with nearest filtering until a texel is 4+ screen pixels (scratchpad `zoom.py` pattern: crop the object box
  of each 4x3 turntable cell, resize nearest to ~700 px). Look at all 12 views of every prop, one prop at a time, and
  list what is wrong before building the next thing. If you have not looked at a prop that way, do not call it fixed.
- **Then look at it in the scene** from where the player stands and close up, next to its neighbours, in the room's
  real lighting (Play mode, pixel lighting on). Edit-mode captures with a copied camera can be far darker or different.
- **Done means it holds up side by side with the approved concept** (render from the concept's camera, put both in one
  image). Palette, light, wall and ceiling texture, props present. If it does not, keep going.
- When the user reports one broken prop, assume the cause is in the pipeline and every prop built with it is broken:
  find the cause, fix it, rebuild all, and review all again.

**Five-view painted props (PixelRestyle.PaintedProps)**
- **Only for blocky props** (cabinets, wardrobes, chests, tables, chairs, doors, bookcases). Organic or intricate models
  (plants, flowers, flamingo, cake, laundry, jars, bed linen) get smears and camouflage patches whatever the tuning: use
  `PixelRestyle.PlainColours` (each face takes the colour the painter gave that part, per-texel variation, crease AO,
  no projection) or a new Meshy model from an approved concept. Decide this per prop **before** painting it.
- **The painter returns a square image, not the 16:9 sheet.** Always run `Tools/sheet_relayout.py` on every painted
  sheet before projecting (it finds each painted view and fits it to its reference box); never let the projection lay
  the 16:9 cell grid over a square painting (cells took in neighbouring views and smeared every side panel). Then
  blend each relaid sheet over its reference to confirm no view is doubled or misplaced.
- **Check every painted sheet's layout against its reference before projecting**: the painter moves views to other
  cells, flips the top view upside down (bed, chair), swaps the side views (flamingo), stacks views, adds a roof that
  isn't there, or draws a view the wrong size. Overlay painting and reference per cell; fix with `relay.py`-style
  crop-and-place (flip/swap as needed), never project a sheet whose silhouettes don't match.
- **Diagnose view assignment with a colour-coded sheet** (each view a flat colour with a checker): build it, turntable
  it, and every face shows which view it really took. Do this as soon as projection looks wrong, instead of guessing.
- Pick each texel's view by the **face's own geometric normal** (the plain cross product of its corners; verified
  outward with a colour-coded sheet), never by the smoothed vertex normal (box sides leaned toward the front view).
  Verify any change to normals or view picking with the colour-coded sheet and `PixelRestyle.DebugSquarest`.
- **The most head-on view that sees a texel wins.** Colour may only veto a view that is clearly wrong (knob smear,
  outline, background), never choose between views: "closest colour" picking made oblique views win and streaked every
  side panel in the room.
- Never remap a band of painted pixels to one pixel further in (the old "rim peel"): it copies one column across the
  band and streaks every face beside a vertical edge. A flat-colour debug sheet can't show this kind of fault; check a
  build of the real painting zoomed in as well.
- Line a painted view up with its reference by the **body's** box (rows/columns that are substantially filled), not the
  outermost pixel: knobs and handles drawn at another size shifted whole side views and put background on the edges.
- The painter recolours freely (a grey Synty lamp comes back bronze), so never judge painted colour against Synty's
  colour; judge against what the painting made of that part elsewhere.
- Painted parts that obviously aren't the model's (clock hands on the dial, a knob smear, white specks, the painter's
  pale rim) are fixed by hand on the sheet first (clear the dial, mirror the clean back of a door onto its front).
- The Synty source for the reference render must be found by name **and size**: several packs share mesh names
  (PolygonTown and PolygonDarkFantasy both have SM_Prop_Bookshelf_01). Render sources through plain URP Lit:
  `Synty/Generic_Basic` renders solid black in the sheet camera.

**Room props (scaled ~20x, `PixelRestyle.Room.cs`)**
- Room objects are prefab instances scaled ~20x: everything that measures in units (sheet camera far clip, occlusion ray
  length and start offset, AO distance) must scale with the prop's bounds.
- Bake at `RoomTexelsPerUnit` (6.4 = 128 per real metre). The bookcase's contents (books, cake, flamingo) are separate
  props: build the case without them (`MakeRoomHolder(..., alone: true)`), or the painter paints them onto the shelf.
- A rebuild creates new mesh assets and the scene's references to the old ones go missing (the bed vanished): after
  every build re-apply the holders to the scene and scan the scene for renderers with no mesh. Re-apply must find
  broken copies through their Synty source (`ApplyRoomHolder` does): a copy whose mesh is already missing has no name
  to match, and 47 props were left invisible that way.
- Lights belong under their prop (lamp, lantern, pendant), so moving the prop moves the light.
- Lamp glow (emission) is masked by the shade's geometry, never by bright texels (that made the pole glow).
- Moving parts (clock hands) stay separate objects with their own plain material; the dial is drawn by hand, marks on
  the face, not the frame.

**Room surfaces**
- Wall/ceiling paint from the concept's own wall (crop it as the reference). Scatter the painting's marks (flakes,
  cracks) over a large tile instead of repeating one painting every 2 m. Take the painting's broad light drift out.
- Check wall boxes for overlapping or edge-touching faces (z-fighting flicker, hairline cracks at corners): walls must
  run through the corners into each other, and no two faces may share a plane.

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
