# Table Spotlight mood

Preset: Assets/Game/Levels/_Resources/Shared/Resources/DungeonLighting/TableSpotlight.asset.

The Room scene Lighting Manager now has this preset selected. Use its existing Preview Mood or Blend to Mood buttons, or F5 in play mode. Restore Default returns to saved scene lighting. Selecting the preset does not replace the saved startup lighting. Dungeon level/floor Mood Lighting also offers Table Spotlight.

SceneMood > Separate table and room lighting > Override Light Groups enables independent Table Brightness/Tint and Room Brightness/Tint. The preset starts at table brightness 1.4 and room brightness 0.04, with warm table light, cool dark surroundings and a dim sky. Room brightness affects room lamp intensities, directional light and ambient illumination; dedicated table sources stay separate. Both groups blend alongside sky and mood colors. Leaving a group-overriding mood restores the group values from before entering it.

Lighting Manager > Light references > Sources contains the authored table/skylight lights; Scene Sources contains general room lights. Keep a light in only one group. Moving the table should move its authored light rig. Skylight Cover still controls whether its daylight lights are enabled. This mood does not override that cover. Emissive materials and baked lighting are not dimmed by these realtime controls.

Tune the preset in the Inspector. No new editor menu commands or generation tools were added. Visual play-mode validation remains necessary for the final exposure and spill on the apartment walls.
