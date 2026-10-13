"""Blender model for the dungeon door leaf at the 128-texel art style (painted afterwards with the prop pipeline).

Run (Blender 5.2, headless):
  blender -b --python Tools/door_model.py -- <out dir>

Writes <out dir>/Dungeon door 128.fbx: objects "Door planks" (six vertical planks with a small gap and an uneven top),
"Door iron" (three straps with rivets on both faces and a ring pull on each side). The leaf fills the old one's box:
2.04 m wide, 2.79 m tall, pivot at the bottom centre, about 0.16 m thick with the straps.
"""
import math
import os
import sys

import bmesh
import bpy

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    return m


def box(bm, cx, cy, cz, sx, sy, sz):
    for v in bmesh.ops.create_cube(bm, size=1)["verts"]:
        v.co.x = cx + v.co.x * sx
        v.co.y = cy + v.co.y * sy
        v.co.z = cz + v.co.z * sz


def obj(name, bm, mat):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    mesh.materials.append(mat)
    return o


W, H, T = 2.04, 2.79, .1      # Blender: x across, y thickness, z up
planks = 6
gap = .012
bm = bmesh.new()
pw = (W - gap * (planks - 1)) / planks
tops = [H - .02, H, H - .05, H - .01, H - .04, H - .015]
for i in range(planks):
    x = -W / 2 + pw / 2 + i * (pw + gap)
    box(bm, x, 0, tops[i] / 2, pw, T, tops[i])
wood = obj("Door planks", bm, material("Door wood", (.32, .2, .11)))

bm = bmesh.new()
strap_t, strap_h = .025, .14
for z in (.35, 1.4, 2.42):
    for side in (-1, 1):
        y = side * (T / 2 + strap_t / 2)
        box(bm, 0, y, z, W - .04, strap_t, strap_h)
        # Rivets along the strap.
        for k in range(7):
            x = -W / 2 + .12 + k * (W - .24) / 6
            box(bm, x, side * (T / 2 + strap_t + .008), z, .035, .016, .035)
# Ring pulls: a plate and a square ring hanging on it, both faces, on the outermost plank at the free edge (inside the
# plank, clear of the gaps either side). The FBX export flips x, and the door's hinge in Unity is at leaf x = -1, so the
# pulls are at Blender x = -.855 (Unity x = +.855, the middle of the plank from .69 to 1.02). Their faces are mapped by
# hand to patches painted at 128 texels per metre in the iron texture's free corner (see the 2026-10-13 door fix); a
# re-export needs that mapping again.
hx, hz, k = -.855, 1.15, 1.5          # k: the pulls were made 50% bigger
for side in (-1, 1):
    y = side * (T / 2 + .01 * k)
    box(bm, hx, y, hz, .12 * k, .02 * k, .12 * k)
    r, half, th = .09 * k, .045 * k, .022 * k     # the ring is narrower than the plate, so its sides hang from it
    yy = side * (T / 2 + .035 * k)
    box(bm, hx, yy, hz - r, (half + th / 2) * 2, th, th)
    box(bm, hx - half, yy, hz - r / 2, th, th, r)
    box(bm, hx + half, yy, hz - r / 2, th, th, r)
iron = obj("Door iron", bm, material("Door iron", (.17, .17, .18)))

bpy.ops.object.select_all(action="DESELECT")
wood.select_set(True); iron.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Dungeon door 128.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False)
print("door written to", out)
