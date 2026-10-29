"""The Dungeon Master's own die: a cube SIZE (0.8) units across with rounded edges and corners and dished pips sunk into its
faces. The face values sit on Unity's local axes (the script that lands it reads them): +Y 1, +X 2, +Z 3, -Z 4, -X 5,
-Y 6. Pivot in the middle. UVs: each face (with the rounded edge half up to it) is projected onto its own cell of a 3 x 2
atlas (cells of SIZE * 128 texels and a margin), values 1-3 on the top row, 4-6 under it, laid out exactly as Tools/dm_die_texture.py paints them.
blender -b -P dm_die.py -- out.fbx"""
import bpy, bmesh, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

SIZE = .8          # the die's size (it was .6: from across a dark room his die read small)
K = SIZE / .6      # everything below was laid out for .6 and scales with it
HALF = SIZE / 2    # half the die's size
BEVEL = .065 * K   # rounded edges
PIP_R = .052 * K   # radius of a pip's dish at the face
PIP_D = .022 * K   # how deep it is sunk
SPREAD = .155 * K  # pip grid offset from the face's middle
TEXELS = 128
CELL = int(SIZE * TEXELS) + 4   # atlas cell in texels: the face's SIZE * 128 and a 2-texel margin round it
W, H = 3 * CELL, 2 * CELL

# Unity (x, y, z) -> Blender (x, z, y): measured through this export and Unity's import with the axis conversion baked
# (the importer turns the root; dm_die.eval.cs bakes that into DM die.asset, so the prefab's root is unturned)
def U(x, y, z):
    return Vector((x, z, y))

def unity(v):
    return Vector((v.x, v.z, v.y))

# value -> (outward axis, face-local a axis, b axis) in Unity space
FACES = {
    1: (Vector((0, 1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
    2: (Vector((1, 0, 0)), Vector((0, 0, -1)), Vector((0, 1, 0))),
    3: (Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0))),
    4: (Vector((0, 0, -1)), Vector((-1, 0, 0)), Vector((0, 1, 0))),
    5: (Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, 1, 0))),
    6: (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, -1))),
}

def pips(value):
    s = SPREAD
    return {
        1: [(0, 0)],
        2: [(-s, s), (s, -s)],
        3: [(-s, s), (0, 0), (s, -s)],
        4: [(-s, s), (s, s), (-s, -s), (s, -s)],
        5: [(-s, s), (s, s), (0, 0), (-s, -s), (s, -s)],
        6: [(-s, s), (-s, 0), (-s, -s), (s, s), (s, 0), (s, -s)],
    }[value]

bpy.ops.mesh.primitive_cube_add(size=2 * HALF)
die = bpy.context.object
bev = die.modifiers.new('round', 'BEVEL'); bev.width = BEVEL; bev.segments = 4; bev.limit_method = 'NONE'
bpy.ops.object.modifier_apply(modifier='round')

# dishes: a sphere of radius R whose cap sinks PIP_D into the face over a circle of PIP_R
R = (PIP_R ** 2 + PIP_D ** 2) / (2 * PIP_D)
for value, (n, a, b) in FACES.items():
    for (pa, pb) in pips(value):
        c = n * (HALF + R - PIP_D) + a * pa + b * pb
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=R, location=U(c.x, c.y, c.z))
        cutter = bpy.context.object
        bool_mod = die.modifiers.new('pip', 'BOOLEAN'); bool_mod.operation = 'DIFFERENCE'; bool_mod.object = cutter; bool_mod.solver = 'EXACT'
        bpy.context.view_layer.objects.active = die
        bpy.ops.object.modifier_apply(modifier='pip')
        bpy.data.objects.remove(cutter, do_unlink=True)

# UVs: each polygon takes the cell of the side it lies on; the rounded parts are projected with it
me = die.data
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()   # the boolean's new faces come in with no normal
uv = bm.loops.layers.uv.verify()   # the cube's own UV layer (a new one would not be the one exported)
for f in bm.faces:
    # by where the polygon lies (the side whose plane its middle is nearest), not its normal: a dish's walls and the
    # boolean's slivers lean every way
    # (the rounded edges and corners go by their normal, so each strip splits at its middle and no texel is stretched
    # more than about 1.4 times)
    c = unity(f.calc_center_median())
    on_edge = sum(abs(x) > HALF - BEVEL * .98 for x in c) >= 2
    d = unity(f.normal) if on_edge else c
    value = max(FACES, key=lambda k: d.dot(FACES[k][0]))
    _, a, b = FACES[value]
    col, row = (value - 1) % 3, (value - 1) // 3
    for loop in f.loops:
        p = unity(loop.vert.co)
        u = (col * CELL + CELL / 2 + p.dot(a) * TEXELS) / W
        v = 1 - (row * CELL + CELL / 2 - p.dot(b) * TEXELS) / H
        loop[uv].uv = (u, v)
bm.to_mesh(me); bm.free()
die.data.materials.append(bpy.data.materials.new('DM die'))
die.name = 'DM die'
bpy.ops.object.select_all(action='DESELECT'); die.select_set(True); bpy.context.view_layer.objects.active = die
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(30))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
print('faces', len(me.polygons))
# texel density check: texels per unit over the whole surface (should be 128)
bm = bmesh.new(); bm.from_mesh(me); uvl = bm.loops.layers.uv.active
wa = sum(f.calc_area() for f in bm.faces)
ua = 0
for f in bm.faces:
    l = f.loops
    for i in range(1, len(l) - 1):
        a, b, c = l[0][uvl].uv, l[i][uvl].uv, l[i + 1][uvl].uv
        ua += abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * .5 * W * H
print('density', (ua / wa) ** .5, 'world area', wa, 'dims', die.dimensions[:])
