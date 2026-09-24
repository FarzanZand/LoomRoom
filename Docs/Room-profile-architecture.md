# Room profile architecture

Dungeon Room Profile exposes Style Override (empty = normal weighted style) and Size Scale (width/depth multiplier, 1 = normal). Heights, props, characters and texture density are unchanged. Forced styles are resolved before corridor inheritance, which still respects doors.

Dungeon level data exposes Large Room Percent (default 10%) and Large Room Scale (default 1.6). These multiply the profile scale on a seeded selection of whole rooms. With 30 rooms, three are selected. Sizes round to whole grid cells, minimum 3x3. Expansion is constrained by table bounds and two-cell separation from other rooms, so selected rooms may not reach their requested size. Use fewer rooms or a larger layout grid if a large hall needs more space.

Profiles are selected from the initial layout roles before resizing. The exit room identity is retained across resizing, while routes, walk distances, regions and reserved paths are rebuilt; it is not guaranteed to remain the absolute farthest room after resizing. Size changes apply on regeneration. Existing profile scale defaults to 1.
