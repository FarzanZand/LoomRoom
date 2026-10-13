# Dungeon room and corridor heights

Edit `Assets/Game/Levels/Dungeon1/Dungeon1.asset` in the Inspector.

- **Architecture Tile Size**: 3 world units. A 32x32 texture repeats once per 3x3 square on walls, floors and ceilings. Point filtering and the existing artwork are retained.
- **Rooms and corridors are one tile (3 units) high.** Only a room template can be taller: its own **Height Tiles** (2 or 3) on `DungeonRoomTemplate`, for rooms that are tall for effect (Dungeon1: Gallery hall 3; Pillared hall, Chapel, Statue hall, Gate hall, Moonlit fountain and the Crypt Lord arena 2). A template left at 0 is one tile. Generated rooms and every corridor section are always one tile; the old room and corridor height percentages are gone.

Ceilings are 3, 6 or 9 units high.

Layout Cell Size remains 2 units, independently of the architectural texture tile size. This preserves the current 30-room layout, walkways and table footprint. Adjacent generated cells share continuous texture coordinates: a texture tile can span more than one grid cell without stretching. Player and enemy scale are unchanged.

Ceilings and cornices follow section heights. Upper masonry closes height changes above passages. Doors retain their authored 2.8-unit opening and fill above it to the lower adjoining ceiling. Floor heights stay level. Roof/edge visibility settings remain independent. Reload the dungeon to apply changes.

Future room styles can assign separate bottom-wall, upper-wall, floor and ceiling materials using this same physical texture density.
