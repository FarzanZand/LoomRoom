"""The campaign map's first reveal, simulated as real cloth in Blender (headless):

   blender --background --python Tools/campaign_cloth_drop.py -- <heights.json> <out.bytes> [<out.blend>]

heights.json (written by CampaignClothBake.ExportHeights in Unity): {"gridX", "gridZ", "halfX", "halfZ", "heights"[(gridX+1)*(gridZ+1)]}
in the map's local units.

The story: a cloth the size of the map falls onto the bare table and settles; then the land rises under it (the
collider grows from flat to full height) and pushes the cloth up into hills and folds; it settles again.
The sim runs at a tenth of the map's size (so the cloth behaves like a tablecloth, not a circus tent).

out.bytes: little-endian
  int32 magic 0x434C4F54 ('CLOT'), int32 version 2, int32 nx, int32 nz (cloth vertices across, along),
  int32 frames, float32 fps, float32 riseStart, float32 riseEnd (seconds: when the land rises),
  then per frame per vertex: float16 x, y, z in map-local units (vertex i = z * nx + x, x across -halfX..halfX;
  half floats keep the file half the size and are within 0.03 units at the map's edge).
"""
import json
import math
import struct
import sys

import bpy
import bmesh

argv = sys.argv[sys.argv.index("--") + 1:]
heights_path, out_path = argv[0], argv[1]
blend_path = argv[2] if len(argv) > 2 else None

SCALE = 0.1          # map units -> sim metres
FPS = 24
FALL_END = 26        # frames: the cloth falls and settles on the bare table
RISE_START, RISE_END = 30, 66   # the land rises
FRAMES = 84
DROP = 1.6           # metres above the table the cloth starts
CLOTH_STEP = 1.0     # map units between cloth vertices
OVERSIZE = 1.0       # the cloth is the map's size (its vertices map 1:1 onto the act cloths after the drop)

with open(heights_path) as f:
    data = json.load(f)
gx, gz, hx, hz = data["gridX"], data["gridZ"], data["halfX"], data["halfZ"]
H = data["heights"]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
scene.frame_start, scene.frame_end = 1, FRAMES

# ── The land (collider): the height grid at full resolution, with a shape key from flat to full. ──
step = 1
lx = list(range(0, gx + 1, step)); lz = list(range(0, gz + 1, step))
if lx[-1] != gx: lx.append(gx)
if lz[-1] != gz: lz.append(gz)
verts, faces = [], []
for zi in lz:
    for xi in lx:
        x = (xi / gx * 2 - 1) * hx; z = (zi / gz * 2 - 1) * hz
        verts.append((x * SCALE, z * SCALE, 0.0))
nx_l = len(lx)
for j in range(len(lz) - 1):
    for i in range(nx_l - 1):
        a = j * nx_l + i
        faces.append((a, a + 1, a + nx_l + 1, a + nx_l))
mesh = bpy.data.meshes.new("Land")
mesh.from_pydata(verts, [], faces)
land = bpy.data.objects.new("Land", mesh)
scene.collection.objects.link(land)
land.shape_key_add(name="Flat")
full = land.shape_key_add(name="Full")
k = 0
for zi in lz:
    for xi in lx:
        full.data[k].co.z = H[zi * (gx + 1) + xi] * SCALE
        k += 1
full.value = 0.0
full.keyframe_insert("value", frame=RISE_START)
full.value = 1.0
full.keyframe_insert("value", frame=RISE_END)
# (Keys ease in and out by default.)
land.modifiers.new("Collision", "COLLISION")
land.collision.thickness_outer = 0.015
land.collision.cloth_friction = 0.8

# The table under it all.
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, -0.002))
table = bpy.context.active_object
table.scale = (hx * SCALE * 3, hz * SCALE * 3, 1)
table.modifiers.new("Collision", "COLLISION")
table.collision.cloth_friction = 2.0

# ── The cloth: a grid the size of the map, a little wavy, held above the table. ──
cnx = int(round(2 * hx / CLOTH_STEP)) + 1
cnz = int(round(2 * hz / CLOTH_STEP)) + 1
cv, cf = [], []
for j in range(cnz):
    for i in range(cnx):
        x = (i / (cnx - 1) * 2 - 1) * hx * OVERSIZE; z = (j / (cnz - 1) * 2 - 1) * hz * OVERSIZE
        # Billowing as it falls: long waves, the far side a little higher (it is thrown from the near side).
        wave = math.sin(x * 0.21 + z * 0.13) * 0.6 + math.sin(z * 0.33 - x * 0.07) * 0.4 + math.sin(x * 0.55 + z * 0.41) * 0.25
        cv.append((x * SCALE, z * SCALE, DROP + wave * 0.12 + (x / hx) * 0.2))
for j in range(cnz - 1):
    for i in range(cnx - 1):
        a = j * cnx + i
        cf.append((a, a + 1, a + cnx + 1, a + cnx))
cmesh = bpy.data.meshes.new("Cloth")
cmesh.from_pydata(cv, [], cf)
cloth = bpy.data.objects.new("Cloth", cmesh)
scene.collection.objects.link(cloth)
mod = cloth.modifiers.new("Cloth", "CLOTH")
s = mod.settings
s.quality = 8
s.mass = 0.3
s.air_damping = 0.6
# Stiff against stretching, soft against squashing and bending: the cloth buckles into folds instead of stretching.
s.tension_stiffness = 20; s.compression_stiffness = 1.5; s.shear_stiffness = 3; s.bending_stiffness = 0.05
s.tension_damping = 5; s.compression_damping = 2; s.shear_damping = 3; s.bending_damping = 0.1
mod.collision_settings.distance_min = 0.012
mod.collision_settings.collision_quality = 3
mod.collision_settings.use_self_collision = False
mod.point_cache.frame_start, mod.point_cache.frame_end = 1, FRAMES

# ── Bake by stepping through the frames, reading the evaluated cloth each frame. ──
depsgraph = bpy.context.evaluated_depsgraph_get()
frames = []
for frame in range(1, FRAMES + 1):
    scene.frame_set(frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    ev = cloth.evaluated_get(depsgraph)
    m = ev.to_mesh()
    pts = [(v.co.x / SCALE, v.co.z / SCALE, v.co.y / SCALE) for v in m.vertices]   # Blender z-up -> map (x, y up, z)
    ev.to_mesh_clear()
    frames.append(pts)
    if frame % 10 == 0:
        print("frame", frame, "of", FRAMES, flush=True)

with open(out_path, "wb") as f:
    f.write(struct.pack("<iiiiifff", 0x434C4F54, 2, cnx, cnz, len(frames), float(FPS), (RISE_START - 1) / FPS, (RISE_END - 1) / FPS))
    for pts in frames:
        f.write(struct.pack("<%de" % (len(pts) * 3), *[c for p in pts for c in p]))
if blend_path:
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("wrote", out_path, cnx, "x", cnz, len(frames), "frames")
