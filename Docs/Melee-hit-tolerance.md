# Melee hit tolerance

Edit CombatManager > Melee Hit Tolerance (prefab: Assets/Game/Combat/Prefabs/CombatManager.prefab).

- Melee Reach Multiplier: 1.2, shared by player weapon queries, enemy weapon queries, enemy attack selection/range checks and fallback enemy hits.
- Melee Width Multiplier: 1.25, expands hit volume thickness and fallback enemy impact arcs.
- Existing close-range queries remain alongside extended queries; one-hit-per-swing tracking prevents duplicate damage. Animation damage windows and wall line-of-sight checks remain in place.

The TablePlayer prefab CharacterController radius is 0.4025, up from 0.35 (15%). Its visual mesh and camera are unchanged. This movement collider is separate from the weapon query settings. Check tight corners and open doors during playtesting before increasing it further.

New enemies using EnemyBrain/Hitbox inherit these multipliers automatically; authored attack ranges remain their base values.
