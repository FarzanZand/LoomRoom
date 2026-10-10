# Dungeon room styles

Dungeon1 has two styles in `Assets/Game/Levels/Dungeon1/Styles`: **Crypt Rooms** (Flagstone floor, Vault ceiling, level walls) and **Timber and Stone** (Timber lower bottom wall, Slate upper upper wall, Cut sandstone ceiling, trims hidden). Their materials are in `Styles/Materials`. Change Bottom Wall, Upper Wall, Floor, Ceiling and Trim in the Inspector. Empty fields use the level's material (an empty Upper Wall uses the level's upper wall, not the style's Bottom Wall).

Create more with **Create > Table > Room Style**. Add them to the level's Architecture tab under Room Styles or Corridor Styles; a biome's own styles are added to the level's. Each entry has a relative weight; zero disables it. A style is picked once per whole room or corridor section and repeats with the dungeon seed. Empty lists use the level materials. Styles do not change encounters, loot or layout.

Bottom Wall covers the first architecture tile from the floor. Upper Wall covers every tile above it. A one-tile room uses only Bottom Wall. Floor and ceiling apply to the whole section. Door lintels use the room-side style. Height-transition walls use the taller section's style.

The level's defaults are **Bottom Wall Material** and **Upper Wall Material**. An empty Upper Wall Material falls back to Bottom Wall Material.

Use square repeating textures, material tiling (1,1) and point filtering for the 32x32 pixel look. Materials do not change the physical texture scale. Changes apply on the next dungeon generation.

## Variety and dressing

Every wall tile and floor cell draws its material from the style's own material (Base Weight, default 10) and its weighted Wall and Floor Variants. A variant can be limited to the bottom tile or the tiles above it. A style can also hang wall decorations and repeat Corridor Dressing along straight corridors.

**Hide Trims** leaves out the style's cornices and footings and hides the Trim field. It does not remove timber painted into the wall texture.

## Corridors

**Corridors Copy Connected Room Style** (level, Architecture tab) is on for Dungeon1. A corridor section then copies the whole style of a directly connected room. Junction-only sections search outward along corridor connections for the nearest room; ties are broken by the dungeon seed. A room on level defaults passes those on. The search never crosses a doorway that has a door; if every route to a room does, the section uses level defaults. Opening a door in play does not restyle anything. Turn the option off to use the weighted Corridor Styles list instead (its contents are kept).
