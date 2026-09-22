# Table level reveal

Select **Systems > WorldManager > Table Level Reveal** in the Room scene. The shared settings expand directly in the Inspector. The assigned asset is `Assets/Game/Levels/_Resources/Shared/Resources/TableReveal/Table assembly.asset`; dungeon data no longer owns a reveal reference. Clear the WorldManager reference to use the standard transition, or create another asset with Create > Table > Level Reveal.

The actual generated floor plan is traced in steel blue. Geometry rises in spatial chunks, lights awaken, enemies appear, and the camera flies into first person without fading to black. **Use Room Player POV** keeps the room player's position and look direction while the table assembles. With it disabled, **Camera Angle** controls overview pitch (90 is straight down). Camera Transition Seconds controls the move to the overview; Approach Seconds controls entry into the dungeon. Roofs stay hidden during the overview. The sequence runs on unscaled time with gameplay simulation blocked. Space or Escape skips after generation is ready; Allow Skip can disable this. Descending to another floor retains the short fade.

## Designer controls

- Timing: blueprint, assembly, settle and approach durations.
- Assembly: lift distance, piece duration and rise curve.
- Blueprint: material, colour, line width and entrance marker colour.
- Camera: Use Room Player POV, angle, minimum height and framing margin. Overview-only fields are hidden when room POV is enabled.
- Audio: build/settle clips and volume, played through AudioManager's SFX mixer.
- Dust: the **Assembly dust** prefab in the same Presentation folder. Its emission area fits the dungeon; edit its particles, lifetime, colour and size directly on the prefab.

The renderer positions, visibility, lights, actors, camera lens and Cinemachine state are restored when playback ends or is interrupted. Navigation is generated before presentation and no new layout is generated during the animation. The existing level is replaced before the assembly starts, without a black overlay. The camera projection blends continuously between overview and first-person lenses.

Validation: `TableRevealValidation` is an explicit development test triggered by `Temp/TableRevealValidation.request`. It checks the overview angle, unchanged room POV, no visible black overlay, frozen simulation, smooth camera handoff, enemy restoration and early skipping, and captures the overview and first-person result. It does not run automatically on import.

Room lighting is held during construction and blends to the dungeon mood during the entrance flight. Dungeon BGM starts after the camera has arrived; construction sounds remain on the SFX mixer. Cold-start validation begins with the table player inactive and uses the actual adventure-menu button. Blueprint lines are transparent from creation, including the initial camera move.

## Adventure menu and equipped hands

WorldManager also references **Table Adventure Menu**. Edit the full screen at `Assets/Game/UI/Prefabs/Adventure/Table adventures.prefab` and reusable destination/action cards at `Assets/Game/UI/Prefabs/Adventure/Adventure option.prefab`. Button colours, fonts, borders, spacing and sizes are authored in these prefabs. Mouse menus start without a selected destination; hover transfers selection, leaving clears it, and arrows/Tab or gamepad navigation establish keyboard focus.

Starter gear is assigned before reveal playback. Before the entrance flight, the equipped arm pose is evaluated while hidden; the hands remain hidden for the entire camera flight. On arrival inside the dungeon, the already-equipped hands become visible and control returns in the same frame. The flight follows the live player camera so grounding cannot cause a downward snap at handover. The room avatar is hidden during travel to prevent the camera seeing inside its head. Control returns when the transition finishes, with no additional hold. Transition mouse input and residual look smoothing are discarded.
