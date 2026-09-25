# Apartment layout

The apartment uses the Room player's scale: 20 world units per character-scale metre. The original bedroom shell dimensions, its furniture, the miniature table world and their transforms are preserved.

## Spaces

- Kitchen/dining: about 71.2 square metres at character scale, up from 58 (23% larger). A shallow west-facing dining bay creates a separate eating area; a 3.9 × 3.8 metre clearance envelope accommodates a six-seat table and chairs. The south side remains available for a cooking run. No furniture added yet.
- Second bedroom: 8 × 7.98 metres, with a recessed north window and a doorway offset from the kitchen. Its short dimension extends toward the north, keeping the kitchen, hallway and existing bedroom intact. A clear 5 × 4.5 metre bed/circulation area comfortably fits the existing bed's 2.17 × 2.85 metre footprint.
- Hall: 2 metres clear width, opening into a recessed entrance area. The entrance door opens onto a bounded shared landing.
- Existing bedroom: original interior bounds x = -471.8667 to -279.568481, z = 238.529663 to 491.308929; floor top 0.5, ceiling underside 60.5. The west-wall doorway stays in the gap between the laundry basket and plant, with all furniture retained.
- All four doors are single leaves. Door openings are 28 × 48 world units (1.4 × 2.4 metres). Floor slabs meet under thresholds.
- New windows, contrasting floor materials, simple trim and light pools establish the architectural spaces. Furnishing and final lighting/art polish remain separate work.
- Detached dinner-cutscene sets retain their support floor and references outside the apartment.

## Reusable door

`Assets/Prefabs/Props/ApartmentDoor.prefab` uses the leaf mesh/material from Synty's PolygonTown `SM_Bld_House_Door_01`.

The prefab is saved at scale 20 for dragging directly into this apartment. It has a hinge pivot, solid leaf collider, separate child interaction trigger and `HingedDoor` implementing the existing `IInteractable` contract. Prompts switch between Open and Close. An interaction during motion reverses its target. Angular substeps stop the leaf when it meets a solid obstruction; interact again to reverse it. Keep instances uniformly scaled. Place the hinge on the swing-facing wall surface so the leaf clears its jamb. Change the signed Open Angle to reverse the swing direction.

Opening and closing use existing `InteractionEffect.PlayAudio` entries and `AudioData` assets at `Assets/Data/Audio/ApartmentDoorOpen.asset` and `ApartmentDoorClose.asset`, each with two wooden-door clip variants. The close sound occurs when the leaf reaches its closed pose. AudioData now supports 3D min/max distance; door effects use room-scale attenuation, and pooled sources reset attenuation so they do not change subsequent miniature sounds.

Room-player interaction reach is 50 world units (2.5 character-scale metres). Miniature interaction reach is unchanged.

The prefab includes stationary jambs, casing and door stops that hide the side/head clearance when closed. For a negative Open Angle, set the Frame child's local Z scale to -1 (positive angles use +1), keeping the stops on the non-swing side. All four scene instances are configured. The old scene casing was removed because its inner faces coincided with the wall jamb faces and flickered as the camera moved. Dining-bay wall returns now butt against adjoining walls without overlapping corner faces.

## Verification

Play-mode checks passed for all four doors: open, close, changing prompts, opening/closing AudioData playback, obstruction stopping and safe reversal. Camera-centred selection and the existing interaction input handler were exercised from about 31 world units away. Both Room/Table player swaps and gameplay cameras were checked. Full-size capsule and floor-support samples cover the rooms, hall, entrance recess and landing with doors open; the dining clearance is also checked.

The `Tools > LoomRoom > Apartment > Validate Refined Layout` and `Render Refined Views` editor commands are no longer in the project; repeat clearance checks manually. The apartment is authored scene geometry with no runtime construction dependency.

After the frame repair, all four doors passed 101 collision-clearance poses each across their full swing. The 583 circulation/floor samples and dining clearance still pass; rendered close-ups check the repaired frame from both sides.

The scene starts in Room mode for layout testing. The Room camera receives gameplay/wake-up (Default) and dinner (Channel02) channels.

URP is installed, but the current project Graphics and Quality pipeline assignments are empty. New architectural materials match the active built-in renderer. No render-pipeline settings were changed.
