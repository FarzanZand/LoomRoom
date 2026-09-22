# Dungeon room and corridor heights

Edit `Assets/Game/Levels/Dungeon1/Dungeon1.asset` in the Inspector. Under **Room heights** and **Corridor heights**, set the percentages of three-tile and four-tile sections. The remainder are two tiles tall. Percentages select whole rooms or whole authored corridor sections, not individual cells. Counts round to the nearest whole section; totals above 100 are normalized proportionally. Selection repeats for the same seed.

Dungeon1 starts with 0% three-tile and 25% four-tile rooms and corridors. With 30 rooms, rounding produces eight four-tile rooms. One vertical tile equals Cell Size: currently 2 world units, giving ceilings at 4, 6, or 8 units. Wall textures repeat once per square tile instead of stretching over the entire wall. Floors and gameplay paths stay level.

Tall corridor sections must directly border a room at least as tall: four-tile corridors require a four-tile room, and three-tile corridors require a three- or four-tile room. Corridor percentages are targets across all sections; when too few qualify, fewer tall corridors are generated. This never promotes extra rooms or creates isolated tall hallways. Four-tile corridors are allocated first.

Ceilings and cornices follow each section's height. At height changes, masonry fills the space above the shorter passage, including openings without doors. Doors retain their authored opening height and their lintels fill up to the lower adjoining ceiling. Roof/edge visibility settings still operate independently of height. Reload the dungeon to apply changes.
