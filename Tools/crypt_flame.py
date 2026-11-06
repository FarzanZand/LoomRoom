"""The flame the Crypt's fires burn with on the realm map (CryptEntrance): a low-poly tongue of fire with two smaller ones
at its foot, and its texture at the map's 6.4 texels a unit: a few texels, yellow at the foot to dark red at the tips,
varying texel to texel. Pivot in the basket, 18% up; one unit tall.

The model:    blender -b -P Tools/crypt_flame.py -- "Assets/Game/Campaign/Map/Crypt flame/Crypt flame.fbx"
The texture:  python Tools/crypt_flame.py
"""
import math
import sys

OUT_PNG = "Assets/Game/Campaign/Map/Crypt flame/Crypt flame.png"
# Rows from the foot up.
ROWS = [
    [(255, 214, 96), (255, 176, 58), (255, 200, 78), (255, 186, 64)],
    [(255, 150, 42), (255, 186, 66), (240, 122, 34), (252, 164, 50)],
    [(228, 92, 28), (246, 124, 36), (210, 74, 24), (236, 104, 30)],
    [(170, 44, 20), (196, 60, 22), (148, 36, 18), (184, 52, 20)],
]
# Height up the tongue, its width there (share of its radius).
PROFILE = [(0, .6), (.14, 1), (.3, 1), (.48, .72), (.64, .52), (.8, .26), (.92, .1)]


def paint():
    from PIL import Image
    img = Image.new("RGB", (len(ROWS[0]), len(ROWS)))
    for row, colours in enumerate(ROWS):
        for x, c in enumerate(colours):
            img.putpixel((x, len(ROWS) - 1 - row), c)
    img.save(OUT_PNG)
    print("painted", OUT_PNG, img.size)


def model(out):
    import bpy
    import bmesh
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new("Flame")
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    seg = 7

    def tongue(cx, cy, radius, height, bend, phase, foot, u0, u1, v1):
        rings = []
        for t, w in PROFILE:
            sway = bend * math.sin(t * math.pi * 2 + phase) * t ** .7
            ring = []
            for i in range(seg):
                a = i / seg * math.tau
                # uneven and twisting as it rises, so it isn't a turned shape
                r = radius * w * (1 + .28 * math.sin(a * 3 + phase * 2 + t * 3))
                ring.append(bm.verts.new((cx + sway + math.cos(a) * r, cy + sway * .6 + math.sin(a) * r, foot + t * height)))
            rings.append((t, ring))
        tip_sway = bend * math.sin(math.pi * 2 + phase)
        tip = bm.verts.new((cx + tip_sway, cy + tip_sway * .6, foot + height))

        def face(verts, uvs):
            f = bm.faces.new(verts)
            for loop, (u, v) in zip(f.loops, uvs):
                # the colours climb unevenly round it, never in level bands
                up = v + .16 * math.sin(u * math.tau * 2 + phase) * (1 - v)
                loop[uv].uv = (u0 + (u1 - u0) * u, max(0, min(1, up)) * v1)

        for k in range(len(rings) - 1):
            (t0, a), (t1, b) = rings[k], rings[k + 1]
            for i in range(seg):
                j = (i + 1) % seg
                ui, uj = i / seg, (i + 1) / seg
                face([a[i], a[j], b[j], b[i]], [(ui, t0), (uj, t0), (uj, t1), (ui, t1)])
        t_last, last = rings[-1]
        for i in range(seg):
            j = (i + 1) % seg
            face([last[i], last[j], tip], [(i / seg, t_last), ((i + 1) / seg, t_last), ((i + .5) / seg, 1)])

    tongue(0, 0, .25, 1, .14, .4, -.18, 0, 1, .98)
    # the side tongues take one column of the texture, so the main one sets the density
    tongue(.21, .07, .12, .72, -.08, 1.9, -.16, 0, .25, .9)
    tongue(-.19, -.1, .1, .56, .07, 3.1, -.14, .5, .75, .9)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new("Flame", mesh)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE")
    print("RESULT", len(mesh.polygons), "faces")


if "--" in sys.argv:
    model(sys.argv[sys.argv.index("--") + 1])
else:
    paint()
