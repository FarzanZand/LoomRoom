"""Blender models for the dungeon's room events: the floor spike trap and the ambush gate (a portcullis).

Run (Blender 5.2, headless):
  blender -b --python Tools/trap_models.py -- <out dir>

Writes <out dir>/Spike trap.fbx (objects "Spike plate" and "Spikes", the spikes rise through the plate's holes) and
<out dir>/Ambush gate.fbx (object "Gate bars"). Metres, Y up in Unity, pivots on the floor at the centre. Flat
colours only: the painted-prop pipeline repaints them in the house style.
"""
import math
import os
import sys

import bmesh
import bpy

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = .8
    return m


def new_object(name, bm, mat):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(mat)
    return obj


def box(bm, cx, cy, cz, sx, sy, sz):
    """A box centred on (cx, cy, cz) in Blender space (z up)."""
    geom = bmesh.ops.create_cube(bm, size=1)["verts"]
    for v in geom:
        v.co.x = cx + v.co.x * sx
        v.co.y = cy + v.co.y * sy
        v.co.z = cz + v.co.z * sz


def cone(bm, cx, cy, z0, radius, height, sides=6, down=False):
    """A pointed spike standing on z0 (down: its point at z0)."""
    ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=0.0 if down else radius, radius2=radius if down else 0.0, depth=height)
    for v in ret["verts"]:
        v.co.x += cx
        v.co.y += cy
        v.co.z += z0 + height * .5


def cylinder(bm, cx, cy, z0, radius, height, sides=6):
    ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=radius, radius2=radius, depth=height)
    for v in ret["verts"]:
        v.co.x += cx
        v.co.y += cy
        v.co.z += z0 + height * .5


def export(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                             add_leaf_bones=False)


# ── Spike trap ─────────────────────────────────────────────────────────────
clear()
iron = material("Trap iron", (.23, .22, .21))
rust = material("Trap rust", (.36, .2, .12))
size, holes, frame = 1.7, 4, .1
step = (size - frame * 2) / holes
bm = bmesh.new()
# The plate: a frame and crossbars, leaving square holes the spikes come up through.
t = .05
box(bm, 0, -size / 2 + frame / 2, t / 2, size, frame, t)
box(bm, 0, size / 2 - frame / 2, t / 2, size, frame, t)
box(bm, -size / 2 + frame / 2, 0, t / 2, frame, size, t)
box(bm, size / 2 - frame / 2, 0, t / 2, frame, size, t)
for i in range(1, holes):
    o = -size / 2 + frame + i * step
    box(bm, o, 0, t / 2, .06, size - frame * 2, t)
    box(bm, 0, o, t / 2, size - frame * 2, .06, t)
# Rivets at the corners.
for sx in (-1, 1):
    for sy in (-1, 1):
        cylinder(bm, sx * (size / 2 - frame / 2), sy * (size / 2 - frame / 2), t, .025, .02)
plate = new_object("Spike plate", bm, iron)

bm = bmesh.new()
# The spikes: a bed under the plate with one spike per hole (two smaller ones in every other hole).
box(bm, 0, 0, -.06, size - frame * 2 - .02, size - frame * 2 - .02, .04)
for i in range(holes):
    for j in range(holes):
        x = -size / 2 + frame + step * (i + .5)
        y = -size / 2 + frame + step * (j + .5)
        h = .62 if (i + j) % 2 == 0 else .5
        cone(bm, x, y, -.04, .07, h)
spikes = new_object("Spikes", bm, rust)
export(os.path.join(out, "Spike trap.fbx"), [plate, spikes])

# ── Ambush gate (portcullis) ───────────────────────────────────────────────
clear()
iron = material("Gate iron", (.2, .2, .21))
bm = bmesh.new()
width, height, bars = 2.1, 2.9, 10
for i in range(bars + 1):
    x = -width / 2 + width * i / bars
    cylinder(bm, x, 0, .12, .032, height - .12, sides=6)
    cone(bm, x, 0, 0, .032, .12 + .001, sides=6, down=True)  # the points at the foot
for z in (.55, 1.5, 2.45):
    box(bm, 0, 0, z, width + .06, .05, .09)
box(bm, 0, 0, height - .04, width + .1, .07, .08)
gate = new_object("Gate bars", bm, iron)
export(os.path.join(out, "Ambush gate.fbx"), [gate])

# ── Moon shaft ─────────────────────────────────────────────────────────────
# An open, slightly widening tube of light from 6 m up to the floor; vertex colour alpha fades it out at both ends
# (drawn additive by Moon shaft.mat).
clear()
bm = bmesh.new()
sides, rings, top, r0, r1 = 14, 8, 6.0, .75, 1.05
col = bm.loops.layers.color.new("Col")
grid = []
for k in range(rings + 1):
    f = k / rings
    z, r = top * (1 - f), r0 + (r1 - r0) * f
    grid.append([bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)) for a in [2 * math.pi * i / sides for i in range(sides)]])
for k in range(rings):
    for i in range(sides):
        j = (i + 1) % sides
        face = bm.faces.new((grid[k][i], grid[k][j], grid[k + 1][j], grid[k + 1][i]))
        for loop in face.loops:
            z = loop.vert.co.z / top
            a = min(1.0, z / .25) * min(1.0, (1 - z) / .35) * .55
            loop[col] = (1, 1, 1, a)
shaft = new_object("Moon shaft", bm, material("Shaft", (.6, .7, 1)))
export(os.path.join(out, "Moon shaft.fbx"), [shaft])
print("trap models written to", out)
