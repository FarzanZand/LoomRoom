"""Turn a Meshy (or any textured) model into a pixel-art prop at the walls' texel density.

Gives the mesh clean new UVs, bakes the original paint onto a small texture, reduces it to a
small palette, and exports an FBX (new UVs only) plus the PNG. Walls are 32 texels per 3-unit
tile, so the default density is 10.67 texels per metre.

Unwrap modes:
  cylinder  barrels, pots, pillars: one strip around the side, two lids (faces facing up/down)
  smart     anything else: Blender's Smart UV Project, packed

Run (Blender 5.2, headless):
  blender -b --python Tools/pixel_bake.py -- <model.glb|fbx> <out dir> <Name>
      [--height 1.0] [--density 10.67] [--colors 24] [--unwrap cylinder|smart]
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

SUPER = 8  # bake at 8x and box-filter down, so each texel is the average of its area

argv = sys.argv[sys.argv.index("--") + 1:]
p = argparse.ArgumentParser()
p.add_argument("model"); p.add_argument("out"); p.add_argument("name")
p.add_argument("--height", type=float, default=1.0, help="final height in metres")
p.add_argument("--density", type=float, default=32 / 3, help="texels per metre")
p.add_argument("--colors", type=int, default=24)
p.add_argument("--unwrap", choices=["cylinder", "smart"], default="cylinder")
a = p.parse_args(argv)
out = Path(a.out); out.mkdir(parents=True, exist_ok=True)

# --- load, join, scale to height, origin at bottom centre -----------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
src = Path(a.model)
if src.suffix.lower() in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=str(src))
else:
    bpy.ops.import_scene.fbx(filepath=str(src))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = obj.data
zs = [v.co.z for v in me.vertices]
s = a.height / (max(zs) - min(zs))
cx = sum(v.co.x for v in me.vertices) / len(me.vertices)
cy = sum(v.co.y for v in me.vertices) / len(me.vertices)
z0 = min(zs)
for v in me.vertices:
    v.co = Vector(((v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - z0) * s))
orig_uv = me.uv_layers[0].name

# --- new UVs ----------------------------------------------------------------------------------
pix = me.uv_layers.new(name="Pixel")
me.uv_layers.active = pix
bm = bmesh.new(); bm.from_mesh(me)
uvl = bm.loops.layers.uv["Pixel"]

if a.unwrap == "cylinder":
    radius = max(math.hypot(v.co.x, v.co.y) for v in bm.verts)
    side_w = max(8, math.ceil(2 * math.pi * radius * a.density))
    side_h = max(4, math.ceil(a.height * a.density))
    lid = max(4, math.ceil(2 * radius * a.density))
    W = side_w + 2                       # two spare columns for the faces that cross the seam
    W = max(W, 2 * lid + 3)
    H = side_h + 1 + lid
    # side strip: rows [0, side_h); lids: two squares above it
    for f in bm.faces:
        n = f.normal
        if abs(n.z) > 0.6:
            cxl = (1 + lid / 2) if n.z > 0 else (2 + lid * 1.5)
            for l in f.loops:
                x, y = l.vert.co.x, l.vert.co.y
                l[uvl].uv = ((cxl + x / radius * lid / 2) / W,
                             (side_h + 1 + lid / 2 + y / radius * lid / 2) / H)
        else:
            us = [(math.atan2(l.vert.co.y, l.vert.co.x) / (2 * math.pi)) % 1.0 for l in f.loops]
            if max(us) - min(us) > 0.5:
                us = [u + 1 if u < 0.5 else u for u in us]
            for l, u in zip(f.loops, us):
                l[uvl].uv = (u * side_w / W, min(l.vert.co.z / a.height, 1.0) * side_h / H)
    bm.to_mesh(me)
    layout = {"unwrap": "cylinder", "W": W, "H": H, "side_w": side_w, "side_h": side_h, "lid": lid}
else:
    bm.to_mesh(me)
    area = sum(f.area for f in me.polygons)
    side = max(16, math.ceil(math.sqrt(area / 0.55) * a.density / 4) * 4)
    W = H = side
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=1.5 / side)
    bpy.ops.uv.pack_islands(margin=1.5 / side, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    layout = {"unwrap": "smart", "W": W, "H": H}
bm.free()
(out / f"{a.name}_layout.json").write_text(json.dumps(layout))
print(f"PIXEL atlas {W}x{H}")

# --- bake the original colour onto the new UVs -----------------------------------------------
target = bpy.data.images.new("PixelBake", W * SUPER, H * SUPER, alpha=False)
for mat in me.materials:
    nt = mat.node_tree
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"
                and any(l.to_node.type == "BSDF_PRINCIPLED" and l.to_socket.name == "Base Color"
                        for l in n.outputs[0].links)), None)
    tex = tex or next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = orig_uv
    nt.links.new(uvn.outputs[0], tex.inputs[0])
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(tex.outputs[0], emit.inputs[0])
    outn = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    nt.links.new(emit.outputs[0], outn.inputs[0])
    tgt = nt.nodes.new("ShaderNodeTexImage"); tgt.image = target
    nt.nodes.active = tgt
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.samples = 4
sc.cycles.device = "CPU"
sc.render.bake.margin = SUPER * 2
sc.render.bake.margin_type = "EXTEND"
bpy.ops.object.select_all(action="DESELECT"); obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.bake(type="EMIT")
raw = out / f"{a.name}_bake.png"
target.filepath_raw = str(raw); target.file_format = "PNG"; target.save()

# --- export: only the pixel UVs, one plain material ------------------------------------------
me.uv_layers.remove(me.uv_layers[orig_uv])
plain = bpy.data.materials.new(a.name)
me.materials.clear(); me.materials.append(plain)
obj.name = me.name = a.name
bpy.ops.export_scene.fbx(filepath=str(out / f"{a.name}.fbx"), use_selection=True,
                         apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
                         axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                         path_mode="STRIP", embed_textures=False)
print(f"PIXEL bake {raw}")
print(f"PIXEL size {W} {H} colors {a.colors}")
