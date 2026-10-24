"""Candle sconce for the trophy wall, in the room's units (about 22 to the metre), laid out like the old box sconce:
an oval backplate on the wall, a curved arm out and down to a round drip pan with a rim and a socket, a round candle
with an uneven melted top and a few drips, a wick and a teardrop flame. Unity sconce space: y up, z out of the wall.
blender -b -P sconce.py -- out.fbx"""
import bpy, bmesh, math, random, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
random.seed(7)
bpy.ops.wm.read_factory_settings(use_empty=True)

def U(x, y, z):  # Unity sconce space -> Blender (z up, out of the wall along -Y)
    return Vector((x, -z, y))

def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)

def done(o, m):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.clear(); o.data.materials.append(mat(m))
    return o

parts = {'Brass': [], 'Candle': [], 'Wick': [], 'Flame': []}

# backplate: an oval plate 2 wide, 5 tall, a quarter unit thick, edges bevelled
bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=1, depth=0.22, location=U(0, 0, 0.11), rotation=(math.pi / 2, 0, 0))
p = bpy.context.object; p.scale = (0.95, 2.45, 1)
bev = p.modifiers.new('b', 'BEVEL'); bev.width = 0.07; bev.segments = 3; bev.limit_method = 'ANGLE'
done(p, 'Brass'); bpy.ops.object.modifier_apply(modifier='b'); parts['Brass'].append(p)
# a raised boss where the arm leaves the plate
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.32, location=U(0, -0.55, 0.22))
b = bpy.context.object; b.scale = (1, 0.55, 1); done(b, 'Brass'); parts['Brass'].append(b)

# arm: a tube curving out and down to under the drip pan
cu = bpy.data.curves.new('arm', 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 0.12; cu.bevel_resolution = 3; cu.resolution_u = 16
sp = cu.splines.new('BEZIER'); pts = [U(0, -0.55, 0.3), U(0, -2.05, 1.3), U(0, -1.75, 2.8)]
sp.bezier_points.add(len(pts) - 1)
for bp, q in zip(sp.bezier_points, pts):
    bp.co = q; bp.handle_left_type = bp.handle_right_type = 'AUTO'
arm = bpy.data.objects.new('arm', cu); bpy.context.scene.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.convert(target='MESH'); arm = bpy.context.object; done(arm, 'Brass'); parts['Brass'].append(arm)

# drip pan with a rolled rim, and the socket the candle stands in
bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=1.0, depth=0.14, location=U(0, -1.55, 2.8)); parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_torus_add(major_segments=28, minor_segments=8, major_radius=0.96, minor_radius=0.07, location=U(0, -1.47, 2.8)); parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_cone_add(vertices=20, radius1=0.42, radius2=0.5, depth=0.55, location=U(0, -1.2, 2.8)); parts['Brass'].append(done(bpy.context.object, 'Brass'))

# candle: round, slightly uneven, a melted dip in the top and a few drips down its side
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.36, depth=3.2, location=U(0, 0.65, 2.8))
c = bpy.context.object; done(c, 'Candle')
bm = bmesh.new(); bm.from_mesh(c.data)
for v in bm.verts:
    a = math.atan2(v.co.y + 2.8, v.co.x)
    r = 1 + .04 * math.sin(a * 3 + .7) + .02 * math.sin(a * 5)
    v.co.x *= r; v.co.y = -2.8 + (v.co.y + 2.8) * r
top = max(bm.faces, key=lambda f: f.calc_center_median().z)
for v in top.verts: v.co.z -= random.uniform(0, .13)
ins = bmesh.ops.inset_region(bm, faces=[top], thickness=0.09, depth=0)
bmesh.ops.translate(bm, vec=Vector((0, 0, -0.1)), verts=list(top.verts))
bm.to_mesh(c.data); bm.free(); parts['Candle'].append(c)
for k, (ang, ln) in enumerate([(.6, .5), (2.3, .8), (4.4, .35)]):
    x, y = math.cos(ang) * 0.36, math.sin(ang) * 0.36
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=1, location=U(x, 2.15 - ln / 2, 2.8 + y))
    d = bpy.context.object; d.scale = (0.08, 0.08, ln / 2 + .05); parts['Candle'].append(done(d, 'Candle'))

# wick and flame
bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=0.32, location=U(0, 2.28, 2.8)); parts['Wick'].append(done(bpy.context.object, 'Wick'))
bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=U(0, 2.82, 2.8))
f = bpy.context.object; f.scale = (0.2, 0.2, 0.42); done(f, 'Flame')
for v in f.data.vertices:
    z = v.co.z - U(0, 2.82, 2.8).z
    if z > 0: k = max(.12, 1 - (z / .42) ** 1.4 * .9); v.co.x *= k; v.co.y = U(0, 0, 2.8).y + (v.co.y - U(0, 0, 2.8).y) * k
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
    print('RESULT', name, len(o.data.polygons), 'faces')

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
