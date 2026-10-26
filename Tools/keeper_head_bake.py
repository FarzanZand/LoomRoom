"""Tavern keeper heads: a Meshy bust (head, neck and the collar of a mustard tunic), remeshed by Meshy to about 1,400
faces (meshy.py remesh --polycount 1400: Blender's decimation from Meshy's 300,000 to 2,000,000 triangles tore the
busts apart; pass a target above the face count so nothing is decimated here), cut to the head and neck. Everything
below cut_frac of the bust's height goes, and so does every face above it painted in the tunic's mustard; loose bits
are dropped and the open bottom is closed. Then, as Tools/room_head_bake.py: fresh UVs,
Meshy's painting baked onto them with short rays, written as OBJ (in Unity's frame) + PNG for PixelRestyle.Build in Unity (Tools/keeper_heads.eval.cs).

    blender -b -P Tools/keeper_head_bake.py -- in.glb out.obj target cut_frac out.png size angle margin [cloth 0/1] [neck_fix 0/1/2/3]
"""
import bpy, bmesh, sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, target, chin = argv[0], argv[1], int(argv[2]), float(argv[3])
png = argv[4]; size = int(argv[5]); angle = float(argv[6]); margin = float(argv[7])
cloth = len(argv) > 8 and argv[8] == '1'   # also cut pale undyed cloth (the ratfolk's kerchief) near the cut
neck_fix = int(argv[9]) if len(argv) > 9 else 0   # 1: repaint pale or mustard faces at the neck's foot in the neck's own colour; 2: mustard only (the ogre: its skin is pale itself); 3: dark or grey faces in the lower 40% (the gecko's earring, melted into its throat by the remesh)
import colorsys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
img = None   # the painting: the image feeding Base Color (Meshy's remeshes also carry normal and roughness maps)
for m in ob.data.materials:
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            for l in n.inputs['Base Color'].links:
                src_n = l.from_node
                while src_n.type != 'TEX_IMAGE' and src_n.inputs and src_n.inputs[0].links: src_n = src_n.inputs[0].links[0].from_node
                if src_n.type == 'TEX_IMAGE': img = src_n.image
if img is None:
    for m in ob.data.materials:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image and img is None: img = n.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
bm = bmesh.new(); bm.from_mesh(ob.data)
n0 = len(bm.verts); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); print('RESULT welded', n0, '->', len(bm.verts))
uv = bm.loops.layers.uv.active
zs = [v.co.z for v in bm.verts]; zmin, zmax = min(zs), max(zs)
def col(f):
    u = sum(l[uv].uv.x for l in f.loops) / len(f.loops); v = sum(l[uv].uv.y for l in f.loops) / len(f.loops)
    return px[min(H - 1, max(0, int(v * H))), min(W - 1, max(0, int(u * W)))][:3]
ymid = sum(v.co.y for v in bm.verts) / len(bm.verts)
def tunic(c, f):
    h, s, v = colorsys.rgb_to_hsv(*c)
    return (.09 < h < .17 and s > .35 and v > .25) or (cloth and s < .3 and v > .42 and f.calc_center_median().y > ymid)   # cloth: behind the face only (the bust faces -y), the teeth are pale too
dele = []
for f in bm.faces:
    z = (f.calc_center_median().z - zmin) / (zmax - zmin)
    if z < chin or (z < chin + .2 and tunic(col(f), f)): dele.append(f)
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
import random
from mathutils.bvhtree import BVHTree
def orient(bm):
    """Turn every face outward by what it can see: a Meshy remesh isn't watertight, so recalc_face_normals leaves faces
    wound either way and Unity culls the inward ones. Rays over a sphere round each face: the side of the face whose
    rays escape the head more often is outside."""
    bm.faces.ensure_lookup_table(); tree = BVHTree.FromBMesh(bm); rnd = random.Random(1); flipped = 0
    size = max((max(v.co[i] for v in bm.verts) - min(v.co[i] for v in bm.verts)) for i in range(3))
    dirs = []
    while len(dirs) < 24:
        d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)))
        if .05 < d.length <= 1: dirs.append(d.normalized())
    for f in bm.faces:
        n = f.normal
        if n.length < 1e-9: continue
        c = f.calc_center_median(); score = 0
        for d in dirs:
            side = 1 if d.dot(n) >= 0 else -1
            hit = tree.ray_cast(c + d * size * 1e-4, d, size * 3)[0]
            if hit is None: score += side
        if score < 0: f.normal_flip(); flipped += 1
    print('RESULT faces turned outward', flipped, 'of', len(bm.faces))
# hi = the welded, cut, full-detail head (keeps Meshy's UVs and texture); lo = a decimated copy with fresh UVs the colour is baked onto
hi = ob
bm = bmesh.new(); bm.from_mesh(hi.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
zs = [v.co.z for v in bm.verts]
xs = [v.co for v in bm.verts]; lo_ = Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs))); hi_ = Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
ctr = (lo_ + hi_) / 2; ctr.z = zmin + (zmax - zmin) * (chin + (1 - chin) * .45)
orient(bm)
bm.to_mesh(hi.data); bm.free()
lo = hi.copy(); lo.data = hi.data.copy(); bpy.context.scene.collection.objects.link(lo); lo.name = 'Head'
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = lo; lo.select_set(True)
mod = lo.modifiers.new('dec', 'DECIMATE'); mod.use_collapse_triangulate = True; mod.ratio = min(1.0, target / max(1, len(lo.data.polygons)))
bpy.ops.object.modifier_apply(modifier='dec')
bm = bmesh.new(); bm.from_mesh(lo.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
orient(bm)
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
if neck_fix:
    # the bottom fifth of the head: faces painted as the undershirt (pale, grey) or the tunic (mustard) take the median
    # colour of the other faces there, rasterised into their UV triangles
    W2 = size; pxo = np.array(out.pixels[:], dtype=np.float32).reshape(W2, W2, 4)
    me = lo.data; uvd = me.uv_layers.active.data
    zl = [v.co.z for v in me.vertices]; z0, z1 = min(zl), max(zl); band = z0 + (z1 - z0) * (.4 if neck_fix == 3 else .2)
    def tri_px(p):
        uvs = [uvd[li].uv for li in p.loop_indices]
        pts = []
        for k in range(1, len(uvs) - 1):
            a, b, c = [np.array((u[0] * W2, u[1] * W2)) for u in (uvs[0], uvs[k], uvs[k + 1])]
            x0, x1 = int(max(0, min(a[0], b[0], c[0]) - 1)), int(min(W2 - 1, max(a[0], b[0], c[0]) + 1))
            y0, y1 = int(max(0, min(a[1], b[1], c[1]) - 1)), int(min(W2 - 1, max(a[1], b[1], c[1]) + 1))
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-9: continue
            for y in range(y0, y1 + 1):
                for x in range(x0, x1 + 1):
                    px_, py_ = x + .5, y + .5
                    w1 = ((b[1] - c[1]) * (px_ - c[0]) + (c[0] - b[0]) * (py_ - c[1])) / den; w2 = ((c[1] - a[1]) * (px_ - c[0]) + (a[0] - c[0]) * (py_ - c[1])) / den
                    if w1 >= -.05 and w2 >= -.05 and 1 - w1 - w2 >= -.05: pts.append((y, x))
        return pts
    good, bad = [], []
    for p in me.polygons:
        if p.center.z > band: continue
        pts = tri_px(p)
        if not pts: continue
        c = np.mean([pxo[y, x, :3] for y, x in pts], axis=0); h, s_, v = colorsys.rgb_to_hsv(*c)
        if neck_fix == 3: foreign = v < .3 or (s_ < .12 and v > .35)
        else: foreign = (neck_fix == 1 and s_ < .18 and v > .45) or (.09 < h < .17 and s_ > .35 and v > .25)
        if foreign: bad.append(pts)
        else: good.append((c, pts))
    # the neck's own colour comes from just above the band (the band's own faces carry the collar's browns), and in mode
    # 2 any band face far from it (the collar showing through as brown on the ogre's nape) is repainted too
    ref = []
    for p in me.polygons:
        if band < p.center.z < z0 + (z1 - z0) * .4:
            pts = tri_px(p)
            if pts: ref.append(np.mean([pxo[y, x, :3] for y, x in pts], axis=0))
    specks = 0
    if good:
        med = np.median(np.array(ref), axis=0) if ref else np.median(np.array([g[0] for g in good]), axis=0)
        if neck_fix == 2:
            keep = []
            for c, pts in good:
                if np.linalg.norm(c - med) > .22: bad.append(pts)
                else: keep.append((c, pts))
            good = keep
        for pts in bad:
            for y, x in pts: pxo[y, x, :3] = med
        # single mustard texels inside the neck's own faces (the tunic's edge running through a face) go too
        if neck_fix in (1, 2):
            for _, pts in good:
                for y, x in pts:
                    h, s_, v = colorsys.rgb_to_hsv(*pxo[y, x, :3])
                    if .09 < h < .17 and s_ > .35 and v > .25: pxo[y, x, :3] = med; specks += 1
        out.pixels[:] = pxo.ravel()
    print('RESULT mustard texels repainted', specks)
    print('RESULT neck faces repainted', len(bad), 'of', len(bad) + len(good))
out.filepath_raw = png; out.file_format = 'PNG'; out.save()
bpy.data.objects.remove(hi)
xs = [v.co for v in lo.data.vertices]
print('RESULT lo bounds z', round(min(c.z for c in xs), 4), round(max(c.z for c in xs), 4), 'chin z', round(zmin + (zmax - zmin) * chin, 4))
bpy.ops.object.select_all(action='DESELECT'); lo.select_set(True); bpy.context.view_layer.objects.active = lo
# written as OBJ in Unity's own frame (Unity read the FBX of these remeshes wrongly: triangles strung across the head):
# x = -x, y = z, z = -y, so the head stands up and faces +z with its left at -x; one line per corner, triangles only
me = lo.data; me.calc_loop_triangles() if hasattr(me, 'calc_loop_triangles') else None
uvd = me.uv_layers.active.data; ln = me.corner_normals if hasattr(me, 'corner_normals') else None
def U(v): return (-v[0], v[2], -v[1])
lines = []
for v in me.vertices: x, y, z = U(v.co); lines.append(f"v {x:.6f} {y:.6f} {z:.6f}")
for l in me.loops: u = uvd[l.index].uv; lines.append(f"vt {u[0]:.6f} {u[1]:.6f}")
for l in me.loops:
    n = ln[l.index].vector if ln is not None else l.normal
    x, y, z = U(n); lines.append(f"vn {x:.6f} {y:.6f} {z:.6f}")
for t in me.loop_triangles:
    a = [f"{me.loops[li].vertex_index + 1}/{li + 1}/{li + 1}" for li in t.loops]
    lines.append("f " + " ".join(a))
open(dst, 'w').write("\n".join(lines) + "\n")
print('RESULT obj triangles', len(me.loop_triangles))
