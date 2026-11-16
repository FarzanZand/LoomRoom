"""Table candlestick for the hobby table (the user, 2026-11-16: a candle on each side of the character sheet, framing
it), in the room's units (about 20 to the metre) and in the trophy wall's sconces' make, which hang right over it
(Tools/candle_sconce.py): a round stepped foot, a turned stem with a knop, a drip pan with a rolled rim and a socket,
the sconce's round candle with an uneven melted top and a few drips, a wick and a teardrop flame. About 9.6 units to
the flame's tip. Unity space: y up, standing on y 0. One object per material (Brass, Candle, Wick, Flame), UVs
cube-projected at one unit per UV, so the sconce's materials give it the sconce's texel size.
blender -b -P Tools/table_candlestick.py -- out.fbx [out.blend]"""
import bpy, bmesh, math, random, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]
random.seed(11)
bpy.ops.wm.read_factory_settings(use_empty=True)

def U(x, y, z):  # Unity space -> Blender (z up)
    return Vector((x, -z, y))

def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)

def done(o, m):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.clear(); o.data.materials.append(mat(m))
    return o

parts = {'Brass': [], 'Candle': [], 'Wick': [], 'Flame': []}

def lathe(profile, segments=16):
    """A turned piece: (radius, height) pairs from the bottom up, closed at both ends."""
    bm = bmesh.new()
    rings = []
    for r, y in profile:
        rings.append([bm.verts.new(U(math.cos(2 * math.pi * k / segments) * r, y, math.sin(2 * math.pi * k / segments) * r)) for k in range(segments)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segments):
            bm.faces.new((a[k], a[(k + 1) % segments], b[(k + 1) % segments], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    me = bpy.data.meshes.new('turned'); bm.normal_update(); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('turned', me); bpy.context.scene.collection.objects.link(o)
    return o

# foot, stem and socket: one turned brass piece
PAN = 4.55          # the drip pan's height
brass = lathe([
    (1.15, 0.00), (1.15, 0.12), (1.02, 0.22), (0.78, 0.30), (0.62, 0.52), (0.36, 0.66),   # stepped foot
    (0.22, 0.80), (0.18, 1.40), (0.20, 1.62),                                                 # lower stem
    (0.40, 1.80), (0.46, 2.00), (0.40, 2.20),                                                 # knop
    (0.20, 2.38), (0.17, 3.30), (0.19, 3.95), (0.30, 4.25), (0.34, PAN - 0.07),               # upper stem, flaring
])
parts['Brass'].append(done(brass, 'Brass'))
bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=0.86, depth=0.14, location=U(0, PAN, 0)); parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_torus_add(major_segments=20, minor_segments=6, major_radius=0.82, minor_radius=0.07, location=U(0, PAN + 0.08, 0)); parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_cone_add(vertices=20, radius1=0.42, radius2=0.5, depth=0.55, location=U(0, PAN + 0.34, 0)); parts['Brass'].append(done(bpy.context.object, 'Brass'))

# candle: round, slightly uneven, a melted dip in the top and a few drips down its side (the sconce's, a little taller)
BASE, TALL = PAN + 0.3, 3.6
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.36, depth=TALL, location=U(0, BASE + TALL / 2, 0))
c = bpy.context.object; done(c, 'Candle')
bm = bmesh.new(); bm.from_mesh(c.data)
for v in bm.verts:
    a = math.atan2(v.co.y, v.co.x)
    r = 1 + .04 * math.sin(a * 3 + .7) + .02 * math.sin(a * 5)
    v.co.x *= r; v.co.y *= r
top = max(bm.faces, key=lambda f: f.calc_center_median().z)
for v in top.verts: v.co.z -= random.uniform(0, .13)
bmesh.ops.inset_region(bm, faces=[top], thickness=0.09, depth=0)
bmesh.ops.translate(bm, vec=Vector((0, 0, -0.1)), verts=list(top.verts))
bm.to_mesh(c.data); bm.free(); parts['Candle'].append(c)
TOP = BASE + TALL
for ang, ln in [(.6, .55), (2.3, .9), (4.4, .4), (5.5, .65)]:
    x, z = math.cos(ang) * 0.36, math.sin(ang) * 0.36
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=1, location=U(x, TOP - 0.1 - ln / 2, z))
    d = bpy.context.object; d.scale = (0.08, 0.08, ln / 2 + .05); parts['Candle'].append(done(d, 'Candle'))
# a pool of wax run down onto the pan
bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=1, location=U(0.3, PAN + 0.1, 0.22))
d = bpy.context.object; d.scale = (0.3, 0.24, 0.07); parts['Candle'].append(done(d, 'Candle'))

# wick and flame
bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=0.32, location=U(0, TOP + 0.03, 0)); parts['Wick'].append(done(bpy.context.object, 'Wick'))
FLAME = TOP + 0.57
bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=U(0, FLAME, 0))
f = bpy.context.object; f.scale = (0.2, 0.2, 0.42); done(f, 'Flame')
for v in f.data.vertices:
    z = v.co.z - FLAME
    if z > 0:
        k = max(.12, 1 - (z / .42) ** 1.4 * .9); v.co.x *= k; v.co.y *= k
parts['Flame'].append(f)

# one object per material, smooth by angle, UVs cube-projected at one unit per UV (tiling sets the texel size in Unity)
for name, objs in parts.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.object; o.name = o.data.name = name
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.cube_project(cube_size=1.0, scale_to_bounds=False, correct_aspect=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    print('RESULT', name, len(o.data.polygons), 'faces', sum(len(p.vertices) - 2 for p in o.data.polygons), 'triangles')

if len(args) > 1: bpy.ops.wm.save_as_mainfile(filepath=args[1])
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
print('RESULT flame tip at', round(FLAME + 0.42, 2), 'candle top', TOP)
