# One body: outside look (Guen to Zelda, 2026-10-15)

The user asked for the one body to look natural from outside (legs, less stiff arms, gentler look-up lean) with first person unchanged.
Please re-run the full first-person matrix against master (your QA Temp takes were cleared when the editor restarted).

## What changed (LoomRoom-onebody, uncommitted)
- Legs: `Development/OneBody/Editor/OneBodyLegBaker.cs` (Tools > LoomRoom > R&D > One Body > Bake Legs) reads the
  third-person humanoid clips onto Characters.fbx (HumanPoseHandler), keeps the leg bones and writes generic clips to
  `Characters/Animations/FirstPersonPlayer/Legs/` (Idle, Walk, Run, Walk Back = walk reversed, Strafe Right,
  Strafe Left = mirrored, In Air) and `Legs mask.mask`; adds a "Legs" layer at the END of `FirstPersonTable.controller`
  (index 7 of 8, earlier indices unchanged): a 2D blend on new MoveX/MoveZ params, In Air while !Grounded.
- `Characters/Players/Scripts/PlayerAnimation.cs` feeds MoveX/MoveZ (velocity in the look's frame).
- Arms from outside: `OneBodyRig.Reach`, after the first-person copy and the bend: each outside hand 25 degrees lower around its
  shoulder (armDrop), pulled in (armTuck .25 forward, armTuckSide .4 sideways), two-bone elbow solve. `naturalArms` off = old look.
- Lean: `OneBodyRig.chestLimit` 30 -> 15 (default, builder, TablePlayer.prefab, Room.unity's table player).

## My checks (one body against itself)
- Legs on vs off (`<action>_nolegs` sets the Legs layer weight 0): run arms in camera space 0.03 mm; jump and lightA
  0.000 px over 105 frames, states identical.
- lightA before both changes vs after legs and reach: 0.000 px on 308 on-screen points, states and camera identical.

## Scripts (copies here survive editor restarts)
- `guen_take.cs` (replace __ACTION__), `run_takes.sh "<project dir>" <actions...>` (fresh Debug session per action),
  `take_summary.py`, your `qa_take.cs` and `cmp_takes.py`.
- New actions: walkF, walkB, walkR, walkL, lookUp (SetPitch -70 = actually looking DOWN), lookDown (+70 = up).
- One body logs a layer7 column that master doesn't have: compare common columns.

## Not covered
- Crouch: no human crouch clip exists; the body still stands upright while crouched.
- From outside the block's shield sits chest-to-waist high (the drop is the same for every pose).
