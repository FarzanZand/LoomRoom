# Woodland table level

The editable scene layout is under TableManager / Woodland Village:
- Medieval village: the far fifth of the board, with cottages, inn, blacksmith, watchtower, well and market.
- Woodland groves: mixed broadleaf and pine trees around open glades.
- Rocky grove: an inland rise and boulder clusters.
- Pond: jade water and a stone bank.
- Meadow and flowers: combined grass meshes and flower prefabs.

A main trail joins the player start and village; a western loop offers a second route.
The inland hills rise approximately 5.5 and 3.8 units above the base landscape.
Every terrain edge sits 0.035 units above the tabletop, with a 2.5-unit flat rim
and a smooth transition inland. Terrain transform scale stays at one.

Woodland Terrain.asset is a private copy; Table Terrain.asset remains the prior
terrain. The level root owns its NavMeshSurface, which collects only its children using collider geometry. Lighting, terrain, scenery and tabletop gameplay content belong to this root. The physical table and player rigs stay outside it. Disable the root to hide the level and remove its navigation.
Rebake it after moving solid scenery or sculpting terrain.

