# Dungeon room and corridor heights

Edit `Assets/Game/Levels/Dungeon1/Dungeon1.asset` in the Inspector.

- **Architecture Tile Size**: 3 world units. A 32x32 texture repeats once per 3x3 square on walls, floors and ceilings. Point filtering and the existing artwork are retained.
- **Two Tile Room Percent**: 25. **Three Tile Room Percent**: 0. Remaining rooms are one tile high.
- **Two Tile Corridor Percent**: 25. **Three Tile Corridor Percent**: 0. Remaining corridor sections are one tile high.

Ceilings are 3, 6 or 9 units high. Counts round to whole rooms/sections (30 rooms gives eight two-tile rooms). Percentages above 100 total normalize proportionally. Selection repeats for a fixed seed. Tall corridor sections must directly connect to a room at least as tall; if too few qualify, fewer tall corridors are generated. Three-tile corridors are allocated first.

Layout Cell Size remains 2 units, independently of the architectural texture tile size. This preserves the current 30-room layout, walkways and table footprint. Adjacent generated cells share continuous texture coordinates: a texture tile can span more than one grid cell without stretching. Player and enemy scale are unchanged.

Ceilings and cornices follow section heights. Upper masonry closes height changes above passages. Doors retain their authored 2.8-unit opening and fill above it to the lower adjoining ceiling. Floor heights stay level. Roof/edge visibility settings remain independent. Reload the dungeon to apply changes.

Future room styles can assign separate bottom-wall, upper-wall, floor and ceiling materials using this same physical texture density.
