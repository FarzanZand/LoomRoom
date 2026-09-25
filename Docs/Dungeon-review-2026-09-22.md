# Dungeon review — 22 September 2026

This is the pre-implementation audit. The subsequent system pass addresses exact room counts, routed connections, navigation validation, load rollback, reward authoring, reusable UI and duplicate catalogs. See [Dungeon authoring guide](Dungeon-authoring.md) for the current implementation and remaining limits.

The dungeon is a playable prototype, not a finished procedural design system. This review covers the generator, navigation and doors, floor transitions, loot/equipment, presentation, and designer workflow. It combines source inspection, 100 seeded layout tests, and a two-floor Unity play-mode test. It is not a complete manual playthrough or performance profile.

## Verified

- 100 seeds produced connected floor grids and reachable exits. Requested 30 rooms produced a minimum of 25; requested room count is still a target, not a guarantee.
- At 5% of whole regions hidden, the largest hidden tile area was 11.1%. Count percentage and area percentage differ because regions have different sizes.
- Doors generated, opened, and cleared their blocking colliders and navigation obstacles.
- Starting equipment occupied the hotbar and was equipped.
- Descending generated another seed, preserved item count and health, used floor-specific lighting, and stopped at the configured final floor.
- Runtime/editor C# build passed. Existing off-NavMesh agent warnings still appeared; NPC navigation outside the tested dungeon route needs a separate inspection.

## Fixed during this pass

- Corridor intersections previously merged into huge visibility regions. Regions now preserve corridor-leg ownership.
- Saved Dungeon1 hide percentages were 100, despite the intended 5. Both are now 5.
- Long connections between rooms in placement order produced excessive crossings. Rooms now connect to their nearest earlier room with narrower corridors. Requested room count increased to 30.
- Asset setup could recreate a catalog after a folder move and overwrite authored data. Setup now detects existing catalogs globally and refuses to rebuild existing content.
- Held consumables previously applied their effect before checking inventory ownership at animation completion. Consumption now succeeds before the effect is applied.

## Remaining findings

| Priority | Finding | Recommended change |
| --- | --- | --- |
| High | Loading destroys the previous generated environment before the replacement succeeds. A failed descent cannot roll back to the last playable floor. | Validate configuration first, build a replacement separately, then commit it. Keep the old floor available until success. |
| High | Grid connectivity does not guarantee a traversable furnished dungeon. Props are placed by room coordinates without reserving entrance paths, stair clearance or enemy spawn clearance. | Reserve walkable lanes and validate actual NavMesh paths after furnishing, with doors treated as openable connections. Reject or repair failed seeds. |
| Medium | The requested room count can silently underfill: 30 requested, as few as 25 in testing. | Use a packing/partition strategy with a minimum accepted count and regeneration budget; report actual counts. |
| Medium | Corridors are still L-shaped center-to-center cuts. They can cross third-party rooms; removing long loop connections reduces crossings but also reduces route choice. | Build an explicit room graph, select doorway sockets, route around unrelated rooms, then add a small controlled number of loops. |
| Medium | Encounters and containers follow room-index patterns, not pacing. Additional floors change layout and lighting but do not yet have separate difficulty or reward budgets. | Add floor encounter budgets, enemy weights, room roles and loot overrides. Keep early rooms forgiving and optional branches rewarding. |
| Medium | New doors are one-way opening gates. Enemies have no deliberate door-opening/breaking behavior, and doors cannot be reclosed. | Choose enemy-specific door permissions; add clear gate audio and feedback. Test door chokepoints against enemy avoidance. |
| Medium | Dungeon UI still constructs much of its hierarchy and styling in code. This does not meet the intended designer-prefab workflow. | Move enemy bars, damage numbers and reusable slots into editable UI prefabs. Keep behavior scripts separate from authored visuals. |
| Medium | Architecture uses many separate primitive GameObjects/renderers/colliders and builds navigation synchronously. | Profile first; combine static meshes by material per room/chunk, reduce colliders, and stage floor generation under the fade. |
| Low | **Resolved.** Two level catalog assets existed, one under `_Resources` and one under `Resources`. | Only `Assets/Game/Levels/Resources/TableLevels.asset` remains; `TableLevelLoader` loads it and logs an error if it is missing. |

Melee wall occlusion is already checked in both Hitbox and the enemy fallback hit path; this review does not identify "attacks through walls" as a confirmed bug.

## Recommended generator direction

Use a hybrid: designer-authored room prefabs inside a procedural room graph. Start with 8–12 room types, each with doorway sockets, reserved movement space, enemy sockets, loot sockets, and lighting anchors. Suggested roles: entrance, small skirmish, guard post, library, crypt, supply room, treasure vault, shrine, trap room, stair room, and an optional elite encounter.

Generate a readable main route with two or three optional branches and one or two shortcuts. Give each branch a purpose. Place a recognizable landmark near junctions so the player can navigate without constantly watching the minimap. Secret rooms should have visible clues rather than requiring inspection of every wall.

Use room roles to vary pace: quiet room, small encounter, resource decision, optional danger, then a memorable encounter. Enemy variety should change player decisions: a shield guard, a flanker, a ranged enemy that retreats, and a creature that alerts nearby rooms are more valuable than four health/damage variants.

Keep the user's readable-lighting preference. Use palette, architectural silhouettes, sound and movement for mood before adding darkness or torch dependence.

## References and their application

- [Barony's Quality of Death update](https://www.baronygame.com/blog/qod-update-launched) describes roughly 1,000 added rooms including combat gauntlets, puzzles, mazes, traps and ambushes. The useful lesson is variety of authored situations, not simply increasing room count.
- [Barony's Instruments of Destruction release](https://www.baronygame.com/blog/iodrelease) describes protected vaults with rewards that change with depth. Apply this as optional risk/reward branches; do not put essential progression behind an unreliable random lock.
- [Grimrock's editor documentation](https://www.grimrock.net/modding/inspector-and-connectors/) exposes monster behavior and connections between dungeon objects to creators. Apply that designer-facing approach to door sockets, triggers, rewards and encounter settings.

## Proposed next pass

1. Navigation/placement validation and safe failed-load recovery.
2. Room graph, controlled loops, doorway sockets and 8–12 editable room templates.
3. Encounter and loot budgets per floor, with distinct enemy roles.
4. UI prefab migration, door feedback and generation profiling.

These are proposals, not claims of completed implementation.
