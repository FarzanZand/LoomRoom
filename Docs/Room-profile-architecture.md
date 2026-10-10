# Room profile architecture

A Dungeon Room Profile has **Style Override** (empty uses the normal weighted style) and **Size Scale** (width and depth multiplier, 0.5 to 3, 1 = normal). Heights, props, characters and texture density are unchanged. Forced styles are applied before corridor style copying, which still stops at doors.

The level's Layout tab has **Large Room Percent** (0 to 30, default 10) and **Large Room Scale** (1 to 3, default 1.6). Grown layouts roll Large Room Percent once per room; a room that hits is scaled by Large Room Scale on top of its profile's Size Scale. The exit room on a guardian floor is at least the guardian's Arena Size Scale.

Sizes round to whole cells, at least 3 and at most half the smaller table side. A room template's footprint or a painted Room Shape keeps its own size and ignores both scales (a template without a footprint still grows to its Minimum Cells). A grown room that cannot fit is dropped with a Console warning, so use fewer rooms or a larger grid if large halls keep failing. Size changes apply on regeneration.
