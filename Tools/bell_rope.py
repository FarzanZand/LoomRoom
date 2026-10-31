"""The Great bell's pull rope, in metres, Unity axes (y up), the pivot at its top (y 0) and LENGTH down: a three-strand
hemp rope (a twisted 10-sided tube whose radius follows three spiral ridges), a woollen sally to grip (russet and hemp
bands round a fatter wrap) part way up from its end, and a whipped tail with a knot at the bottom.
Materials: Hemp, Wool red, Wool cream.
blender -b -P bell_rope.py -- out.fbx [length]"""
import bpy, bmesh, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]
LENGTH = float(args[1]) if len(args) > 1 else 3.2
bpy.ops.wm.read_factory_settings(use_empty=True)

def U(x, y, z):   # Unity -> Blender, as Tools/dm_die.py measured
    return Vector((x, z, y))

def mat(name, rgb):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); return m

HEMP, RED, CREAM = mat('Hemp', (.52, .42, .28)), mat('Wool red', (.33, .22, .15)), mat('Wool cream', (.47, .38, .26))
mats = [HEMP, RED, CREAM]

SIDES, R = 10, .018
SALLY_FROM, SALLY_TO, SALLY_R = .32, .95, .042       # measured up from the rope's end
bm = bmesh.new()
rings = []
step = .025
n = int(LENGTH / step) + 1
for i in range(n + 1):
    y = -min(i * step, LENGTH)
    up = LENGTH + y                                    # height above the end
    ring = []
    for k in range(SIDES):
        a = 2 * math.pi * k / SIDES
        r = R * (1 + .16 * math.cos(3 * a + y * 2 * math.pi / .09))     # three strands, one turn per 9 cm
        if SALLY_FROM <= up <= SALLY_TO:
            edge = min(up - SALLY_FROM, SALLY_TO - up)
            r = SALLY_R * (.8 + .2 * min(1, edge / .03)) * (1 + .06 * math.cos(5 * a + up * 40))
        if up < .06: r = R * 1.25                       # the whipped tail
        ring.append(bm.verts.new(U(r * math.cos(a), y, r * math.sin(a))))
    rings.append((ring, up))
for (r0, up0), (r1, up1) in zip(rings, rings[1:]):
    mid = (up0 + up1) / 2
    if SALLY_FROM <= mid <= SALLY_TO:
        band = int((mid - SALLY_FROM) / .105) % 2       # red and cream bands about 10 cm deep
        m = 1 if band == 0 else 2
    else:
        m = 0
    for k in range(SIDES):
        f = bm.faces.new((r0[k], r0[(k + 1) % SIDES], r1[(k + 1) % SIDES], r1[k]))
        f.material_index = m
# caps
top = bm.faces.new(rings[0][0]); top.material_index = 0
bottom = bm.faces.new(list(reversed(rings[-1][0]))); bottom.material_index = 0
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new('Bell rope'); bm.to_mesh(me); bm.free()
o = bpy.data.objects.new('Bell rope', me); bpy.context.scene.collection.objects.link(o)
for m in mats: me.materials.append(m)
bpy.context.view_layer.objects.active = o; o.select_set(True)
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(60))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
print('rope faces', len(me.polygons))
