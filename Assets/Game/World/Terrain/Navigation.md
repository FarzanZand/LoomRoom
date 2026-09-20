# Apartment and table navigation

Room.unity uses two separate agent types and baked data assets:

- **Apartment NPC**: radius 6, height 36, step height 4. The NavMeshSurface on `World/Apartment` covers the apartment and entrance landing. Its volume includes scene furniture outside the Apartment hierarchy, including the cinematic dining table. Data: `Apartment Navigation.asset`.
- **Table Character**: the original miniature agent type (ID 0). Its surface stays on `TableManager/Woodland Village` and collects only that level hierarchy. Data: `Woodland/Woodland Navigation.asset`.

DungeonMaster and the room-sized Mom/Dad scene instances use Apartment NPC. Table NPCs and enemies use Table Character. When adding characters, select the matching **Agent Type** on their NavMeshAgent. NPC and enemy wander queries use the agent's type and area mask.

The TableManager's NavMeshModifier marks its geometry Not Walkable for Apartment NPC only. It does not affect the table bake. No links connect the two agent types.

Each apartment door leaf is excluded from the static bake and has a carving NavMeshObstacle that follows the hinge. Closed doors block passage; opening them updates navigation without a rebake. NPCs do not automatically open doors.

After changing floors, walls, or furniture, select World/Apartment and Bake its NavMeshSurface. After editing a table level, bake that level's own surface separately.

Validation: all nine apartment floor sections returned PathComplete from DungeonMaster's position with door obstacles disabled to check the underlying open passages. The scene was saved with door obstacles enabled. Both meshes use different agent IDs and different NavMeshData assets.
