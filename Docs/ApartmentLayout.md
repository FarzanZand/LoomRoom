# Apartment blockout

The apartment uses the Room player's scale (20 world units per character-scale metre). The table miniature world and all existing bedroom props remain at their original transforms.

- Existing bedroom: preserve the interior bounds measured from Wall1–Wall4, floor top 0.5, ceiling underside 60.5.
- Hallway: west of the bedroom, 40 units clear width (2 character-scale metres).
- Existing-bedroom doorway: west wall, centred at z = 285 in the gap between the laundry basket and plant. The cabinets opposite the tabletop stay in place.
- Kitchen: west of the hallway, 160 × 145 units (8 × 7.25 character-scale metres). Empty for now, with room for counters, a dining table, chairs and circulation.
- Second bedroom: beside the kitchen, west of the hallway, approximately 160 × 104.6 units.
- Three open doorways: 28 units wide and 48 high (1.4 × 2.4 character-scale metres). No door leaves or decoration yet.
- Floor slabs meet under the thresholds. Ceilings and walls have box colliders.
- Old room geometry is disabled as a reversible reference. Detached dinner-cutscene sets remain outside the apartment; their oversized support floor is cropped to the staging area.

Editor builder: `Tools > LoomRoom > Apartment > Build Layout` (runs only once on a scene without an Apartment Layout root). The result is regular scene geometry, with no runtime construction dependency.

Validation: `Tools > LoomRoom > Apartment > Validate Layout` checks a route from the original bedroom through the hallway to both new rooms using the Room player's actual capsule radius and height, plus floor support. This does not substitute for a first-person playtest of movement feel or the dual game loop.

Verified in Play mode: Room player starts inside the bedroom at (-410, 0.5, 441), with Main Camera Room using PlayerCamera. Scene overrides start in Room mode and allow the Room camera brain to receive gameplay/wake-up (Default) and dinner (Channel02) cameras. The miniature player remains unchanged.

Rendering: URP is installed but Graphics and Quality pipeline assignments are empty, so the current renderer is built-in. The blockout wall material uses Standard; no project render-pipeline settings were changed.
