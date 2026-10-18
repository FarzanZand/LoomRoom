# Dungeon room styles

Dungeon1's floors take their styles from their biomes, two each (Cellars: Church, Church4; Catacombs: Church2, Church5; Crypt: Church3, Church6); the level's own Room Styles list is empty. Older styles in `Assets/Game/Levels/Dungeon1/Styles`: **Crypt Rooms** (Flagstone floor, Vault ceiling, level walls) and **Timber and Stone** (Timber lower bottom wall, Slate upper upper wall, Cut sandstone ceiling, trims hidden). Their materials are in `Styles/Materials`. Change Bottom Wall, Upper Wall, Floor, Ceiling and Trim in the Inspector. Empty fields use the level's material (an empty Upper Wall uses the level's upper wall, not the style's Bottom Wall).

Create more with **Create > Table > Room Style**. Add them to the level's Architecture tab under Room Styles or Corridor Styles; a biome's own styles are added to the level's. Each entry has a relative weight; zero disables it. A style is picked once per whole room or corridor section and repeats with the dungeon seed. Empty lists use the level materials. Styles do not change encounters, loot or layout.

Bottom Wall covers the first architecture tile from the floor. Upper Wall covers every tile above it. A one-tile room uses only Bottom Wall. Floor and ceiling apply to the whole section. Door lintels use the room-side style. Height-transition walls use the taller section's style.

The level's defaults are **Bottom Wall Material** and **Upper Wall Material**. An empty Upper Wall Material falls back to Bottom Wall Material.

Use square repeating textures, material tiling (1,1) and point filtering for the 32x32 pixel look. Materials do not change the physical texture scale. Changes apply on the next dungeon generation.

## Variety and dressing

Every wall tile and floor cell draws its material from the style's own material (Base Weight, default 10) and its weighted Wall and Floor Variants. A variant can be limited to the bottom tile or the tiles above it. A style can also hang wall decorations and repeat Corridor Dressing along straight corridors.

**Hide Trims** leaves out the style's cornices and footings and hides the Trim field. It does not remove timber painted into the wall texture.

## Corridors

**Corridors Copy Connected Room Style** (level, Architecture tab) is on for Dungeon1. A corridor section then copies the whole style of a directly connected room. Junction-only sections search outward along corridor connections for the nearest room; ties are broken by the dungeon seed. A room on level defaults passes those on. The search never crosses a doorway that has a door; if every route to a room does, the section uses level defaults. Opening a door in play does not restyle anything. Turn the option off to use the weighted Corridor Styles list instead (its contents are kept).

## Natural styles (Cave1, Cave2, Nature1, Nature2)

Four styles in `Styles/` (materials and textures in `Styles/HighRes Materials/<Style>`), in no level yet:

- **Cave1**: damp grey rock walls, packed earth floor, dark rock ceiling. Variants: glowing mushrooms (foot of the wall), a water trickle running the whole wall; floor mushrooms, a damp patch.
- **Cave2**: rough red rock walls and ceiling (muted to the dungeon palette), dusty red earth floor with stones. Variants: amber crystals, pale roots through a crack; floor crystals, a damp patch.
- **Nature1**: packed earth held by roots, grass and moss floor, root tangle ceiling. Variants: hanging ivy, small red mushrooms; floor mushrooms, a damp patch.
- **Nature2**: old bark walls, leaf litter floor, leaf canopy ceiling. Variants: shelf fungi, glowing moss; floor glowing moss, a damp patch.

Each uses one wall material for every row (the tile wraps, so there is no seam at the tile line) and hides trims (natural rock and earth have no cornices). Variants follow Church3's ratio: base weight 10, variants 0.6 to 1.2, one feature each.

Built from Meshy paintings (`Temp/Styles/gen`) with `Tools/style_tiles.py` (the painting brought down to one texel per painted pixel and quilted into a seamless 256 x 384 wall or 256 x 256 floor tile, marks that would show as a repeat kept out with `--exclude` boxes, light drift levelled) and `Tools/style_variants.py` (each variant is the base with one feature cut from the style's feature sheet). Materials are copies of Church3's (tiling 1.5 x 1 walls, 1.5 x 1.5 floors and ceilings, offset 0.5).

## Church4, Church5, Church6

Three more Church-family styles, made without painting by `Tools/style_recolour.py`: each surface (bottom wall, upper wall, floor, ceiling, trim) is taken from one of the Church styles and recoloured texel by texel with a gradient map, so every brick, crack and patch of dirt stays where it was drawn; moss and mould keep their own colour, a faint texture (plaster) keeps its faintness, and a checker floor maps each tile colour to its own ramp. The surface's variants (cracked, cross, moss, soot, flaking, grave, broken) get the same recolour. Materials are copies of the source surface's materials (`HighRes Materials/<Style>`), the style asset copies the wall source's settings.

- **Church4** (mossy cellar): Church's brick in green-grey, Church2's plaster grey-green, Church's flagstones brown, Church2's timber ceiling, Church's trim dark green.
- **Church5** (red brick chapel): Church3's brick red, its plaster dim ochre, Church2's checker bone and black, Church's vault grey stone, Church2's trim red-brown.
- **Church6** (pale crypt): Church's brick pale limestone, Church's upper brick violet-grey, Church3's checker pale and dark green, Church2's timber ash grey, Church3's trim bone.

To change one, edit its ramps in `STYLES` at the top of the script, run `python Tools/style_recolour.py <out dir>` and copy the textures over the ones in `HighRes Materials/<Style>`.
