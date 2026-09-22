# Dungeon room styles

Edit Assets/Game/Levels/Dungeon1/Styles/Crypt Rooms.asset or Crypt Corridors.asset. Both initially reference the existing materials; change Bottom Wall, Upper Wall, Floor, Ceiling and Trim in the Inspector. Empty fields inherit the corresponding level material (an empty upper wall inherits the level wall, not the style bottom wall).

Create additional assets with Create > Table > Room Style. Add them to Dungeon1 level data under Architecture Styles > Room Styles or Corridor Styles. Each entry has a relative weight; zero disables it. Selection occurs once per whole room/corridor section and is repeatable for the dungeon seed. Empty lists use level materials. Style randomness does not change encounters, loot or layout.

Bottom Wall covers the first physical tile (0–3 units at the default scale). Upper Wall covers every layer above it. A one-tile room uses only Bottom Wall. A two-tile room has one bottom and one upper layer. Floor and ceiling overrides apply across the entire section. Door lintels use the room-side style and split at vertical tile boundaries. Height-transition walls use the taller section's style.

Use square repeatable textures, default material tiling (1,1), and point filtering for the intended 32x32 pixel look. Materials do not alter the shared physical texture scale. Changes apply on the next dungeon generation.

Level defaults now expose Bottom Wall Material and Upper Wall Material directly. Empty Upper Wall Material falls back to Bottom Wall Material. Expand a style inline to edit its overrides. Starter Crypt styles inherit these wall defaults.

New original artwork: Assets/Game/Levels/Dungeon1/Materials/Slate Timber. Both PNGs are actual 32x32 RGBA, opaque, Point-filtered, Repeat-wrapped, uncompressed, no mipmaps. Generated using built-in imagegen and reduced to exactly 32x32 by nearest-neighbour sampling. Slate and Timber.asset is ready to assign in Room Styles or Corridor Styles; it is not automatically enabled.

Prompt set: Upper — original cool slate masonry, four offset courses, chipped blocks and two thin cracks, coarse 32x32 pixel art, seamless flat albedo. Lower — weathered walnut timber frame, dark iron straps, diagonal corner brace and large cool slate stones, coarse 32x32 pixel art, flat horizontally repeating albedo.

Enable Hide Trims on a room style to omit its generated cornices and footings. The Trim material field is hidden while enabled. Defaults off; applies to rooms and corridors using that style after regenerating the dungeon. This does not remove timber drawn into the wall texture.
