"""Gothic window for the bedroom (the mood pass, after the user's reference photo): a pointed arch, two lancets under a
round light, a deep frame and a sill. Room units (about 22 to the metre); Unity space y up, x across the window, z out of
the wall into the room; the origin is the middle of the bottom of the frame on the wall's face.
Objects: Frame (cube UVs at one unit per UV: the material's tiling sets the texel size), Glass (one quad per opening).
blender -b -P gothic_window.py -- out.fbx"""
import bpy, bmesh, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
W, H = 34.0, 56.0       # outer width, height to the arch's point
FR, DEP = 2.2, 3.0      # frame width, depth out of the wall
SPRING = H - W * .62    # where the arch starts

def U(x, y, z):  # Unity -> Blender
    return Vector((x, -z, y))

def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)

parts = {'Frame': [], 'Glass': []}

def arch_outline(w, spring, top, n=14):
    """points of a pointed arch outline (x, y), left foot up and over to the right foot"""
    r = w * .62                    # each arc's radius (equilateral-ish pointed arch)
    pts = [(-w / 2, 0.0), (-w / 2, spring)]
    cxL, cxR = -w / 2 + r, w / 2 - r
    # left arc: centred on the right of it
    aTop = math.acos(max(-1, min(1, (0 - cxL) / r)))
    for i in range(1, n + 1):
        a = math.pi - (math.pi - aTop) * i / n
        pts.append((cxL + r * math.cos(a), spring + r * math.sin(a)))
    for i in range(1, n + 1):
        a = aTop + (math.pi - aTop) * (i - 1) / n
        a = math.pi - a
        pts.append((cxR + r * math.cos(a), spring + r * math.sin(a)))
    pts.append((w / 2, spring)); pts.append((w / 2, 0.0))
    return pts

def slab_from_outline(name, outer, inner, z0, z1, m):
    """a frame between two outlines (same point count), extruded from z0 to z1"""
    bm = bmesh.new()
    def ring(o, z): return [bm.verts.new(U(x, y, z)) for x, y in o]
    oF, iF, oB, iB = ring(outer, z1), ring(inner, z1), ring(outer, z0), ring(inner, z0)
    n = len(outer)
    for i in range(n - 1):
        j = i + 1
        bm.faces.new((oF[i], oF[j], iF[j], iF[i])); bm.faces.new((oB[j], oB[i], iB[i], iB[j]))
        bm.faces.new((oB[i], oB[j], oF[j], oF[i])); bm.faces.new((iF[i], iF[j], iB[j], iB[i]))
    bm.faces.new((oF[0], iF[0], iB[0], oB[0])); bm.faces.new((oF[-1], oB[-1], iB[-1], iF[-1]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.data.materials.append(mat(m))
    return o

def box(cx, cy, cz, sx, sy, sz, m):
    bpy.ops.mesh.primitive_cube_add(size=1, location=U(cx, cy, cz)); o = bpy.context.object
    o.scale = (sx, sz, sy); bpy.ops.object.transform_apply(scale=True); o.data.materials.append(mat(m)); return o

outer = arch_outline(W, SPRING, H)
inner = [(x * (W - 2 * FR) / W, FR + (y - 0) * (H - 2 * FR) / H) for x, y in arch_outline(W, SPRING, H)]
parts['Frame'].append(slab_from_outline('frame', outer, inner, 0.0, DEP, 'Frame'))
# central mullion, transom under the round light, lancet heads as small arches
iw = W - 2 * FR
parts['Frame'].append(box(0, FR + (SPRING - FR) / 2 + 2, DEP * .6, 1.4, SPRING - FR + 4, DEP * .8, 'Frame'))
parts['Frame'].append(box(0, SPRING + 1.2, DEP * .6, iw, 1.2, DEP * .8, 'Frame'))
for sx in (-1, 1):
    lw = iw / 2 - .7
    lo = arch_outline(lw, SPRING - FR - lw * .55, SPRING - FR, 8)
    li = [(x * (lw - 1.6) / lw, y * (SPRING - FR - 1.0) / (SPRING - FR) ) for x, y in lo]
    o = slab_from_outline('lancet', [(x + sx * (lw / 2 + .7), y + FR) for x, y in lo], [(x + sx * (lw / 2 + .7), y + FR) for x, y in li], DEP * .2, DEP * .8, 'Frame')
    parts['Frame'].append(o)
# a round light (a ring) in the arch's head
bpy.ops.mesh.primitive_torus_add(major_segments=28, minor_segments=6, major_radius=5.2, minor_radius=.6, location=U(0, SPRING + 7.2, DEP * .6), rotation=(math.pi / 2, 0, 0))
o = bpy.context.object; o.data.materials.append(mat('Frame')); parts['Frame'].append(o)
# glazing bars across the lancets
for y in (FR + 9, FR + 18):
    parts['Frame'].append(box(0, y, DEP * .5, iw, .6, DEP * .5, 'Frame'))
# sill
parts['Frame'].append(box(0, -.8, DEP * .9, W + 4, 1.6, DEP * 1.8, 'Frame'))
# glass: the inner outline as one flat face, set back
bm = bmesh.new(); vs = [bm.verts.new(U(x, y, DEP * .3)) for x, y in inner]; bm.faces.new(vs)
me = bpy.data.meshes.new('glass'); bm.to_mesh(me); bm.free()
g = bpy.data.objects.new('glass', me); bpy.context.scene.collection.objects.link(g); g.data.materials.append(mat('Glass')); parts['Glass'].append(g)

for name, objs in parts.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.object; o.name = o.data.name = name
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=1e-4); bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.cube_project(cube_size=1.0, scale_to_bounds=False, correct_aspect=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_flat()
    print('RESULT', name, len(o.data.polygons), 'faces')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
