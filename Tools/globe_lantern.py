"""Globe lantern for the bedroom (the mood pass): a brass cage hung over the game table, after the user's reference photo.
Room units (about 22 to the metre), Unity space y up, centred on the globe. Eight meridian ribs and three rings round a
frosted amber glass globe, a stepped top cap with a hanging loop, a bottom cap with a knob, and a small bright core.
Objects: Brass (cube UVs at one unit per UV: the material's tiling sets the texel size), Glass, Core.
blender -b -P globe_lantern.py -- out.fbx"""
import bpy, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
R = 8.0     # globe radius
RIB = 0.26  # rib and ring thickness

def U(x, y, z):  # Unity (y up) -> Blender (z up)
    return Vector((x, -z, y))

def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)

def done(o, m):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.clear(); o.data.materials.append(mat(m))
    return o

parts = {'Brass': [], 'Glass': [], 'Core': []}
# meridian ribs: four vertical circles round the vertical axis make eight ribs
for k in range(4):
    a = k * math.pi / 4
    bpy.ops.mesh.primitive_torus_add(major_segments=48, minor_segments=6, major_radius=R + RIB, minor_radius=RIB,
                                     location=U(0, 0, 0), rotation=(math.pi / 2, 0, a))
    parts['Brass'].append(done(bpy.context.object, 'Brass'))
# rings round the globe: the equator a band, two lighter rings above and below
for y, w in ((0.0, RIB * 1.8), (R * .55, RIB), (-R * .55, RIB)):
    r = math.sqrt(max(0.0, R * R - y * y)) + RIB
    bpy.ops.mesh.primitive_torus_add(major_segments=48, minor_segments=6, major_radius=r, minor_radius=w, location=U(0, y, 0))
    parts['Brass'].append(done(bpy.context.object, 'Brass'))
# top cap: a stepped crown, a neck and a hanging loop
for y, r, d in ((R - .4, 2.6, 1.0), (R + .5, 1.9, 1.0), (R + 1.3, 1.0, 0.8)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=d, location=U(0, y, 0))
    parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_torus_add(major_segments=24, minor_segments=6, major_radius=1.0, minor_radius=0.28,
                                 location=U(0, R + 2.6, 0), rotation=(math.pi / 2, 0, 0))
parts['Brass'].append(done(bpy.context.object, 'Brass'))
# bottom cap and knob
bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=1.0, radius2=2.4, depth=1.4, location=U(0, -R - .2, 0))
parts['Brass'].append(done(bpy.context.object, 'Brass'))
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.8, location=U(0, -R - 1.3, 0))
parts['Brass'].append(done(bpy.context.object, 'Brass'))
# frosted glass globe just inside the cage, and a bright core inside it
bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=R - .1, location=U(0, 0, 0))
parts['Glass'].append(done(bpy.context.object, 'Glass'))
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=2.2, location=U(0, -1.0, 0))
parts['Core'].append(done(bpy.context.object, 'Core'))

for name, objs in parts.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.object; o.name = o.data.name = name
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.cube_project(cube_size=1.0, scale_to_bounds=False, correct_aspect=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    print('RESULT', name, len(o.data.polygons), 'faces')

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
