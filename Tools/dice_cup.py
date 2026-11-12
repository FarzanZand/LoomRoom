"""The dice room's cup (Levels/Shared/Props/Events/Dice room/Dice cup): a round leather dice cup turned over on the dead
gambler's table, its mouth down. It replaces the Synty mug turned over, whose handle made it read as a block of stone and
pulled its quest outline to one side (review 2026-11-12). Made at the dungeon's 128 texels a metre (one texel = 1/128 m;
the sizes below are in texels): a tapered leather tube about 23 cm tall, a thick rolled lip at the mouth, a turned-over
edge at the closed end and a slightly domed base on top. Every face is laid into a panel of the texture at exactly that
density, and the texture is painted here too: old pale leather in tone clusters, a tooled
line under the top edge, a scuffed lip, grime toward the table and a worn lit edge on top (no stitched seam: it read as a stray line).

Unity space: y up, the mouth on the ground at y = 0, u = 0 of the tube toward -z.

    blender -b -P Tools/dice_cup.py -- "<out folder>"      writes Dice cup.fbx and .png, and Source~/Dice cup.blend
"""
import bpy, bmesh, math, sys, os
import numpy as np
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
T = 1 / 128
rng = np.random.default_rng(11)

# ── Sizes (texels) ──
# Half as big again as first made (the user, 2026-11-12: about 23 cm tall; 15 cm was hard to find on the table).
K = 1.5
SIDES = 16
R_LIP, H_LIP = 8.4 * K, 2.0 * K        # the rolled lip at the mouth: radius, height
R_MOUTH = 7.7 * K                      # the tube where it leaves the lip
R_TOP, H_TOP = 6.6 * K, 18.0 * K       # the tube at the closed end, and where it ends
R_EDGE, H_EDGE = 6.0 * K, 19.0 * K     # the turned-over edge on top
H_DOME = 19.6 * K                      # the base's middle

panels = {}; faces = []


def panel(name, w, h):
    p = panels.setdefault(name, [0, 0]); p[0] = max(p[0], w); p[1] = max(p[1], h)


def add(pts, name, uvs, outward, smooth=True):
    faces.append(([Vector(p) for p in pts], name, uvs, Vector(outward), smooth))


def ring(r, y, k):
    a = 2 * math.pi * k / SIDES - math.pi / 2          # k = 0 toward -z: the seam
    return (r * math.cos(a), y, r * math.sin(a))


def band(name, r0, y0, r1, y1):
    """A band round the cup from (r0, y0) up to (r1, y1): u round it from the seam, v up it (in texels along the slope)."""
    slant = math.hypot(r1 - r0, y1 - y0); circ = 2 * math.pi * max(r0, r1)
    panel(name, circ, slant)
    for k in range(SIDES):
        p = [ring(r0, y0, k), ring(r0, y0, k + 1), ring(r1, y1, k + 1), ring(r1, y1, k)]
        uv = [(circ * k / SIDES, slant), (circ * (k + 1) / SIDES, slant), (circ * (k + 1) / SIDES, 0), (circ * k / SIDES, 0)]
        m = ring(1, 0, k + .5)
        dy = (r0 - r1) / max(slant, 1e-6)                # a band leaning in faces a little up
        add(p, name, uv, (m[0], dy if y1 != y0 else (1 if r1 < r0 else -1), m[2]))


def disc(name, r, y, up, centre_y=None):
    """A disc (fan round its middle), mapped flat from above."""
    panel(name, 2 * r, 2 * r)
    cy = y if centre_y is None else centre_y
    for k in range(SIDES):
        a, b = ring(r, y, k), ring(r, y, k + 1)
        pts = [(0, cy, 0), a, b]
        uv = [(r + p[0], r - p[2] * (1 if up else -1)) for p in pts]
        add(pts, name, uv, (0, 1 if up else -1, 0), smooth=False)


band('lip', R_LIP, 0, R_LIP, H_LIP)                 # the lip's outside
band('lip top', R_LIP, H_LIP, R_MOUTH, H_LIP)       # the ledge where the tube leaves it
band('side', R_MOUTH, H_LIP, R_TOP, H_TOP)          # the tube
band('edge', R_TOP, H_TOP, R_EDGE, H_EDGE)          # the turned-over edge on top
disc('base', R_EDGE, H_EDGE, True, H_DOME)          # the base, a little domed
disc('mouth', R_LIP, 0, False)                      # closed underneath (on the table, never seen)

# ── Pack ──
PAD = 2
size = {k: (math.ceil(v[0] - 1e-6), math.ceil(v[1] - 1e-6)) for k, v in panels.items()}
order = sorted(size, key=lambda k: (-size[k][1], -size[k][0]))


def pack(width):
    x = y = PAD; row = 0; where = {}
    for k in order:
        w, h = size[k]
        if x + w + PAD > width: x = PAD; y += row + PAD; row = 0
        where[k] = (x, y, w, h); x += w + PAD; row = max(row, h)
    return where, y + row + PAD


best = None
for width in range(max(s[0] for s in size.values()) + 2 * PAD, 200):
    where, height = pack(width)
    if best is None or max(width, height) < max(best[1], best[2]) or (max(width, height) == max(best[1], best[2]) and width * height < best[1] * best[2]): best = (where, width, height)
place, TW, TH = best


# ── Mesh ──
def to_blender(v): return Vector((-v.x, -v.z, v.y))


bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); shared = {}
for pts, pan, uvs, outward, smooth in faces:
    px, py, _, _ = place[pan]
    cos = [to_blender(p * T) for p in pts]
    nb = Vector((0, 0, 0))
    for i in range(len(cos)):
        a, b = cos[i], cos[(i + 1) % len(cos)]
        nb += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
    if nb.dot(to_blender(outward)) < 0: cos = cos[::-1]; uvs = uvs[::-1]
    vs = []
    for co in cos:
        k = (pan, round(co.x, 6), round(co.y, 6), round(co.z, 6))
        v = shared.get(k)
        if v is None: v = shared[k] = bm.verts.new(co)
        vs.append(v)
    f = bm.faces.new(vs); f.smooth = smooth
    for loop, (u, v) in zip(f.loops, uvs): loop[uvl].uv = ((px + u) / TW, 1 - (py + v) / TH)
bm.normal_update()
me = bpy.data.meshes.new('Dice cup'); bm.to_mesh(me); bm.free()
o = bpy.data.objects.new('Dice cup', me); bpy.context.scene.collection.objects.link(o)
me.materials.append(bpy.data.materials.new('Dice cup'))
triangles = sum(len(p.vertices) - 2 for p in me.polygons)

# ── Paint ──
# Old pale tan leather, muted (darkest to lightest), and the dark waxed thread of the seam. Lighter than the gambler's
# table so the cup stands out on it (the user, 2026-11-12: the dark brown cup blended into the wood).
LEATHER = np.array([[int(c[i:i + 2], 16) for i in (0, 2, 4)] for c in ('3d2b1c', '573f2b', '70573c', '887051', '9e8664', 'b29a76', 'c3ad88', 'd2c09d')], float)
THREAD = np.array([[int(c[i:i + 2], 16) for i in (0, 2, 4)] for c in ('2e2116', '3d2c1d', '4d3926')], float)


def clusters(w, h, base, cell=(3, 2), spread=(-1, 0, 0, 1)):
    """The leather's tone in hard-edged clusters a few texels across (never one texel against the next at random, which
    reads as a checker speckle): a coarse random grid laid over the panel at a ragged offset per row, and here and there
    a single texel a step off, the way a worn surface catches the light."""
    cw, ch = cell
    gw, gh = w // cw + 3, h // ch + 3
    coarse = rng.choice(spread, size=(gh, gw))
    idx = np.empty((h, w), int)
    for y in range(h):
        shift = rng.integers(0, cw)
        for x in range(w):
            idx[y, x] = coarse[(y + rng.integers(0, 2) * (rng.random() < .15)) // ch, (x + shift) // cw]
    odd = rng.random((h, w)) < .06
    idx[odd] += rng.choice([-1, 1], size=odd.sum())
    return base + idx


tex = np.zeros((TH, TW, 3))
for name, (x, y, w, h) in place.items():
    thread = None
    if name == 'side':                     # rows top (the closed end) to bottom (the mouth); columns from the seam round
        idx = clusters(w, h, 4)
        rows = np.arange(h) / max(h - 1, 1)
        idx += np.where(rows > .78, -1, 0)[:, None]                               # grime toward the table
        idx[1, :] -= 1; idx[2, :] -= 1                                            # the tooled line under the top edge
        idx[0, :] = np.maximum(idx[0, :], 6)                                       # its lit lip
        # No stitched seam: a dark groove with thread down one side read as a stray vertical line on the cup (the user,
        # 2026-11-12). The tube's wrap is just another cluster boundary.
        for c in rng.integers(2, w - 2, 5):                                       # scuffs: short light scratches
            r = rng.integers(4, h - 4); idx[r:r + rng.integers(1, 3), c] += 2
    elif name == 'lip':
        idx = clusters(w, h, 3, cell=(4, 1)); idx[0, :] += 1                       # its top catches light
        for c in rng.integers(0, w, 7): idx[:, c] += 2                            # scuffed where it is gripped
        idx[-1, :] -= 1                                                           # dirt against the table
    elif name == 'lip top':
        idx = clusters(w, h, 4, cell=(4, 1)); idx[-1, :] = 2                      # the crease where the tube leaves it
    elif name == 'edge':
        idx = clusters(w, h, 6, cell=(4, 1))                                      # the worn turned edge, lit
        for c in rng.integers(0, w, 6): idx[:, c] += 1
    elif name == 'base':
        idx = clusters(w, h, 5, cell=(3, 3))
        yy, xx = np.indices((h, w)); rr = np.hypot(xx - (w - 1) / 2, yy - (h - 1) / 2)
        idx[(rr > w / 2 - 2.4) & (rr < w / 2 - 1.4)] -= 1                          # a faint pressed ring round the base
        idx[rr < 1.5] += 1
    else: idx = clusters(w, h, 2)          # underneath
    img = LEATHER[np.clip(idx, 0, len(LEATHER) - 1)]
    if thread:
        for r, c in thread: img[r, c] = THREAD[rng.integers(0, 3)]
    tex[y:y + h, x:x + w] = img
    tex[y - 1, x:x + w] = img[0]; tex[y + h, x:x + w] = img[-1]; tex[y:y + h, x - 1] = img[:, 0]; tex[y:y + h, x + w] = img[:, -1]
image = bpy.data.images.new('Dice cup', TW, TH, alpha=False)
rgba = np.concatenate([tex[::-1] / 255, np.ones((TH, TW, 1))], axis=2)
image.pixels = rgba.astype(np.float32).ravel()
image.filepath_raw = os.path.join(out, 'Dice cup.png'); image.file_format = 'PNG'; image.save()

# The .blend goes in Source~ beside the model: Unity skips folders ending in ~.
os.makedirs(os.path.join(out, 'Source~'), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'Source~', 'Dice cup.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'Dice cup.fbx'), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                         bake_space_transform=True, mesh_smooth_type='FACE', axis_forward='-Z', axis_up='Y')
print('RESULT texture', TW, TH, 'triangles', triangles, 'height cm', round(H_DOME * T * 100, 1), {k: place[k] for k in place})
