"""Meshy bust -> a head cut at the chin (no neck, no shoulders; hair below the chin kept when keep_hair), decimated,
faces turned outward. blender -b -P chin_head.py -- in.glb out.fbx target_faces chin_frac keep_hair(0/1)
Prints the chin height and the face (skin) box so Unity can size it by the face."""
import bpy, bmesh, sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, target, chin, keep_hair = argv[0], argv[1], int(argv[2]), float(argv[3]), argv[4] == '1'
hair_min = float(argv[5]); png = argv[6]; size = int(argv[7]); angle = float(argv[8]); margin = float(argv[9])
neck_min = float(argv[10]) if len(argv) > 10 else 2.0   # keep the bust's own neck (skin faces) down to this height (share of the bust's height)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
img = None
for m in ob.data.materials:
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image: img = n.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
bm = bmesh.new(); bm.from_mesh(ob.data)
n0 = len(bm.verts); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); print('RESULT welded', n0, '->', len(bm.verts))
uv = bm.loops.layers.uv.active
zs = [v.co.z for v in bm.verts]; zmin, zmax = min(zs), max(zs)
def col(f):
    u = sum(l[uv].uv.x for l in f.loops) / len(f.loops); v = sum(l[uv].uv.y for l in f.loops) / len(f.loops)
    return px[min(H - 1, max(0, int(v * H))), min(W - 1, max(0, int(u * W)))][:3]
def hair(c): return c[0] > c[1] * 1.6 and c[0] > c[2] * 1.7 and c.mean() < .5   # auburn hair, not skin (shaded skin is r/g ~1.3)
def skin(c): return c[0] > c[2] * 1.08 and c.mean() > .45
dele = []
for f in bm.faces:
    z = (f.calc_center_median().z - zmin) / (zmax - zmin)
    if z < chin and not (keep_hair and z >= hair_min and hair(col(f))) and not (z >= neck_min and skin(col(f))): dele.append(f)
bmesh.ops.delete(bm, geom=dele, context='FACES')
# loose bits left below the chin (islands with no part above it) go too
bm.verts.ensure_lookup_table()
seen = set(); islands = []
for v in bm.verts:
    if v.index in seen: continue
    stack = [v]; comp = []; seen.add(v.index)
    while stack:
        a = stack.pop(); comp.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen: seen.add(b.index); stack.append(b)
    islands.append(comp)
big = max(len(c) for c in islands)
loose = [v for c in islands if len(c) < big * .02 for v in c]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
# close the open bottom (the chin cut): new faces take each corner's colour from a face it already belongs to
uvl = bm.loops.layers.uv.active
old = set(bm.faces)
res = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
newf = [f for f in bm.faces if f not in old]
for f in newf:
    for l in f.loops:
        src_l = next((ll for ll in l.vert.link_loops if ll.face in old), None)
        if src_l: l[uvl].uv = src_l[uvl].uv
bmesh.ops.triangulate(bm, faces=newf)
print('RESULT filled', len(newf))
bm.to_mesh(ob.data); bm.free()

import math
# hi = the welded, cut, full-detail head (keeps Meshy's UVs and texture); lo = a decimated copy with fresh UVs the colour is baked onto
hi = ob
bm = bmesh.new(); bm.from_mesh(hi.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
zs = [v.co.z for v in bm.verts]
xs = [v.co for v in bm.verts]; lo_ = Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs))); hi_ = Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
ctr = (lo_ + hi_) / 2; ctr.z = zmin + (zmax - zmin) * (chin + (1 - chin) * .45)
if sum(1 for f in bm.faces if f.normal.dot(f.calc_center_median() - ctr) < 0) > len(bm.faces) / 2:
    for f in bm.faces: f.normal_flip()
bm.to_mesh(hi.data); bm.free()
lo = hi.copy(); lo.data = hi.data.copy(); bpy.context.scene.collection.objects.link(lo); lo.name = 'Head'
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = lo; lo.select_set(True)
mod = lo.modifiers.new('dec', 'DECIMATE'); mod.use_collapse_triangulate = True; mod.ratio = min(1.0, target / max(1, len(lo.data.polygons)))
bpy.ops.object.modifier_apply(modifier='dec')
bm = bmesh.new(); bm.from_mesh(lo.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
if sum(1 for f in bm.faces if f.normal.dot(f.calc_center_median() - ctr) < 0) > len(bm.faces) / 2:
    for f in bm.faces: f.normal_flip()
bm.to_mesh(lo.data); bm.free()
while len(lo.data.uv_layers): lo.data.uv_layers.remove(lo.data.uv_layers[0])
lo.data.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0, scale_to_bounds=False)
bpy.ops.object.mode_set(mode='OBJECT')
bm = bmesh.new(); bm.from_mesh(lo.data); uvl = bm.loops.layers.uv.active
# count UV islands
par = {}
def fnd(a):
    while par.setdefault(a, a) != a: par[a] = par[par[a]]; a = par[a]
    return a
key = lambda l: (l.vert.index, round(l[uvl].uv.x, 5), round(l[uvl].uv.y, 5))
for f in bm.faces:
    ks = [key(l) for l in f.loops]
    for k in ks[1:]: par[fnd(k)] = fnd(ks[0])
print('RESULT islands', len({fnd(k) for k in list(par)}), 'lo faces', len(bm.faces), 'hi faces', len(hi.data.polygons))
bm.free()
# bake: hi emits its own texture, lo receives it on the fresh UVs
mat_hi = bpy.data.materials.new('hi'); mat_hi.use_nodes = True; nt = mat_hi.node_tree; nt.nodes.clear()
t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Closest'
e = nt.nodes.new('ShaderNodeEmission'); o = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(t.outputs['Color'], e.inputs['Color']); nt.links.new(e.outputs['Emission'], o.inputs['Surface'])
hi.data.materials.clear(); hi.data.materials.append(mat_hi)
out = bpy.data.images.new('baked', size, size, alpha=True); out.generated_color = (0, 0, 0, 0)
mat_lo = bpy.data.materials.new('Room head'); mat_lo.use_nodes = True; nt = mat_lo.node_tree
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = out; nt.nodes.active = tn
lo.data.materials.clear(); lo.data.materials.append(mat_lo)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1
bk = sc.render.bake; bk.use_selected_to_active = True; bk.cage_extrusion = (zmax - zmin) * .008; bk.max_ray_distance = (zmax - zmin) * .025   # short rays: longer ones went through the hair strands and baked the cheek behind onto them
bk.margin = 16; bk.margin_type = 'EXTEND'; bk.use_clear = True
bpy.ops.object.select_all(action='DESELECT'); hi.select_set(True); lo.select_set(True); bpy.context.view_layer.objects.active = lo
bpy.ops.object.bake(type='EMIT', margin=16, use_clear=True)
out.filepath_raw = png; out.file_format = 'PNG'; out.save()
bpy.data.objects.remove(hi)
xs = [v.co for v in lo.data.vertices]
print('RESULT lo bounds z', round(min(c.z for c in xs), 4), round(max(c.z for c in xs), 4), 'chin z', round(zmin + (zmax - zmin) * chin, 4))
bpy.ops.object.select_all(action='DESELECT'); lo.select_set(True); bpy.context.view_layer.objects.active = lo
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, path_mode='COPY', embed_textures=False, apply_scale_options='FBX_SCALE_ALL')
