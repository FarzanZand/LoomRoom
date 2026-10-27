# Dungeon room styles

Dungeon1's floors take their styles from their biomes, two each (Cellars: Cellar1, Cellar4; Catacombs: Cellar3, Cellar6; Crypt: Cellar2, Cellar5); the level's own Room Styles list is empty. Older styles in `Assets/Game/Levels/Shared/Room styles`: **Crypt Rooms** (Flagstone floor, Vault ceiling, level walls). The old Timber and Stone and the natural styles (Cave1, Cave2, Nature1, Nature2) were deleted on 2026-10-27; **Timber and Stone 2** (+ Pine, Black oak) and **Dungeon1 stone 2** (+ Limestone, Basalt) are the old 32-texel Timber and Stone and Dungeon1 default style made again at 384 texels a tile from Meshy paintings (`Tools/style_stones.py`, `Tools/style_redraw.eval.cs`). The Arena keeps its own two styles (`Levels/Arena/Styles`). Their materials are in `Styles/Materials`. Change Bottom Wall, Upper Wall, Floor, Ceiling and Trim in the Inspector. Empty fields use the level's material (an empty Upper Wall uses the level's upper wall, not the style's Bottom Wall).

Create more with **Create > Table > Room Style**. Add them to the level's Architecture tab under Room Styles or Corridor Styles; a biome's own styles are added to the level's. Each entry has a relative weight; zero disables it. A style is picked once per whole room or corridor section and repeats with the dungeon seed. Empty lists use the level materials. Styles do not change encounters, loot or layout.

Bottom Wall covers the first architecture tile from the floor. Upper Wall covers every tile above it. A one-tile room uses only Bottom Wall. Floor and ceiling apply to the whole section. Door lintels use the room-side style. Height-transition walls use the taller section's style.

The level's defaults are **Bottom Wall Material** and **Upper Wall Material**. An empty Upper Wall Material falls back to Bottom Wall Material.

Use square repeating textures, material tiling (1,1) and point filtering for the 32x32 pixel look. Materials do not change the physical texture scale. Changes apply on the next dungeon generation.

## Variety and dressing

Every wall tile and floor cell draws its material from the style's own material (Base Weight, default 10) and its weighted Wall and Floor Variants. A variant can be limited to the bottom tile or the tiles above it. A style can also hang wall decorations and repeat Corridor Dressing along straight corridors.

**Hide Trims** leaves out the style's cornices and footings and hides the Trim field. It does not remove timber painted into the wall texture.

## Corridors

**Corridors Copy Connected Room Style** (level, Architecture tab) is on for Dungeon1. A corridor section then copies the whole style of a directly connected room. Junction-only sections search outward along corridor connections for the nearest room; ties are broken by the dungeon seed. A room on level defaults passes those on. The search never crosses a doorway that has a door; if every route to a room does, the section uses level defaults. Opening a door in play does not restyle anything. Turn the option off to use the weighted Corridor Styles list instead (its contents are kept).

## Cellar4, Cellar5, Cellar6

Three more Church-family styles, made without painting by `Tools/style_recolour.py`: each surface (bottom wall, upper wall, floor, ceiling, trim) is taken from one of the Cellar styles and recoloured texel by texel with a gradient map, so every brick, crack and patch of dirt stays where it was drawn; moss and mould keep their own colour, a faint texture (plaster) keeps its faintness, and a checker floor maps each tile colour to its own ramp. The surface's variants (cracked, cross, moss, soot, flaking, grave, broken) get the same recolour. Materials are copies of the source surface's materials (`Levels/Shared/Room styles/Cellar/<Style>`), the style asset copies the wall source's settings.

- **Cellar4** (mossy cellar): Cellar1's brick in green-grey, Cellar2's plaster grey-green, Cellar1's flagstones brown, Cellar2's timber ceiling, Cellar1's trim dark green.
- **Cellar5** (red brick chapel): Cellar3's brick red, its plaster dim ochre, Cellar2's checker bone and black, Cellar1's vault grey stone, Cellar2's trim red-brown.
- **Cellar6** (pale crypt): Cellar1's brick pale limestone, Cellar1's upper brick violet-grey, Cellar3's checker pale and dark green, Cellar2's timber ash grey, Cellar3's trim bone.

To change one, edit its ramps in `STYLES` at the top of the script, run `python Tools/style_recolour.py <out dir>` and copy the textures over the ones in `Levels/Shared/Room styles/Cellar/<Style>`.
