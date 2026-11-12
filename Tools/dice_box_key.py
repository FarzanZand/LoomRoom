"""The key to the bedroom's dice box (Items/Prefabs/Keys): a small old brass key that comes out of dungeon chests, so it
is made at the dungeon's 128 texels a metre (one texel = 1/128 m; the sizes below are in texels). A ring bow, two
collars, a round shank and a flat bit with two teeth, about 26 cm long (items are drawn large), lying flat with its pivot under its middle.
Every face is laid into a panel of the texture at exactly that density and the texture is painted here too: old brass,
tarnished dark in the creases and on what faces down, a few worn bright texels on what sticks out.

Unity key space: the key lies along x (bow at -x), flat on the ground (y up), its bit's teeth toward +z.

    blender -b -P Tools/dice_box_key.py -- "<out folder>"      writes Dice box key.fbx and .png, and Source~/Dice box key.blend
"""
import bpy, bmesh, math, sys, os
import numpy as np
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
T = 1 / 128
rng = np.random.default_rng(5)

# ── Sizes (texels) ──
# About 26 cm: twice a real key. Dungeon items are drawn large (a potion stands about 30 cm), and at a real key's
# 14 cm it was a speck of gold beside them that did not read as a key from standing height.
K = 1.75
BOW_X, BOW_R, BOW_r = -6.6 * K, 2.5 * K, .85 * K          # the ring: centre, its radius, the bar's radius
SHANK_A, SHANK_B, SHANK_R = -4.2 * K, 9.0 * K, .65 * K
COLLARS = tuple((a * K, b * K, r * K) for a, b, r in ((-3.5, -2.3, 1.15), (-1.6, -1.0, .95), (4.6, 5.1, .9)))   # from x, to x, radius
BIT = [(x * K, z * K) for x, z in ((5.5, .3), (9.0, .3), (9.0, 4.0), (8.0, 4.0), (8.0, 2.7), (7.0, 2.7), (7.0, 4.0), (5.5, 4.0))]   # its outline (x, z)
BIT_T = .8 * K                                 # its thickness
LIFT = max(BOW_r, max(c[2] for c in COLLARS))  # so the lowest point lies on the ground

panels = {}; faces = []


def panel(name, w, h):
    p = panels.setdefault(name, [0, 0]); p[0] = max(p[0], w); p[1] = max(p[1], h)


def add(pts, name, uvs, outward, key=None):
    faces.append(([Vector(p) for p in pts], name, uvs, Vector(outward), key))


def bar(x0, x1, radius, sides, name, key, caps=None):
    """A round bar along x: u round it starting on top, v along it."""
    circ = 2 * math.pi * radius; length = x1 - x0
    panel(name, circ, length)
    ring = lambda k, x: (x, radius * math.cos(2 * math.pi * k / sides), radius * math.sin(2 * math.pi * k / sides))
    for k in range(sides):
        p = [ring(k, x0), ring(k + 1, x0), ring(k + 1, x1), ring(k, x1)]
        uv = [(circ * k / sides, 0), (circ * (k + 1) / sides, 0), (circ * (k + 1) / sides, length), (circ * k / sides, length)]
        m = ring(k + .5, (x0 + x1) / 2)
        add(p, name, uv, (0, m[1], m[2]), key)
    if caps:
        panel(caps, 2 * radius, 2 * radius)
        for x, sign in ((x0, -1), (x1, 1)):
            add([ring(k, x) for k in range(sides)], caps, [(radius + radius * math.cos(2 * math.pi * k / sides), radius + radius * math.sin(2 * math.pi * k / sides)) for k in range(sides)], (sign, 0, 0))


# ── Bow: a ring lying flat, u round the ring, v round its bar starting on top and going outward ──
NU, NV = 16, 6
panel('bow', 2 * math.pi * BOW_R, 2 * math.pi * BOW_r)


def bow(i, j):
    a = 2 * math.pi * i / NU; b = 2 * math.pi * j / NV
    r = BOW_R + BOW_r * math.sin(b)
    return (BOW_X + r * math.cos(a), BOW_r * math.cos(b), r * math.sin(a))


for i in range(NU):
    for j in range(NV):
        p = [bow(i, j), bow(i + 1, j), bow(i + 1, j + 1), bow(i, j + 1)]
        cu, cv = 2 * math.pi * BOW_R / NU, 2 * math.pi * BOW_r / NV
        uv = [(cu * i, cv * j), (cu * (i + 1), cv * j), (cu * (i + 1), cv * (j + 1)), (cu * i, cv * (j + 1))]
        a = 2 * math.pi * (i + .5) / NU; b = 2 * math.pi * (j + .5) / NV
        add(p, 'bow', uv, (math.sin(b) * math.cos(a), math.cos(b), math.sin(b) * math.sin(a)), 'bow')

# ── Shank and collars ──
bar(SHANK_A, SHANK_B, SHANK_R, 8, 'shank', 'shank', caps='shank end')
for n, (x0, x1, r) in enumerate(COLLARS): bar(x0, x1, r, 8, 'collar', f'collar {n}', caps='collar end')

# ── Bit: a flat plate with two teeth ──
xs = [p[0] for p in BIT]; zs = [p[1] for p in BIT]
bw, bh = max(xs) - min(xs), max(zs) - min(zs)
panel('bit top', bw, bh); panel('bit under', bw, bh)
for name, y, sign in (('bit top', BIT_T / 2, 1), ('bit under', -BIT_T / 2, -1)):
    add([(x, y, z) for x, z in BIT], name, [(x - min(xs), max(zs) - z) for x, z in BIT], (0, sign, 0))
run = 0.0
for i in range(len(BIT)):
    (x0, z0), (x1, z1) = BIT[i], BIT[(i + 1) % len(BIT)]
    n = math.hypot(x1 - x0, z1 - z0)
    add([(x0, BIT_T / 2, z0), (x1, BIT_T / 2, z1), (x1, -BIT_T / 2, z1), (x0, -BIT_T / 2, z0)], 'bit edge',
        [(run, 0), (run + n, 0), (run + n, BIT_T), (run, BIT_T)], (z1 - z0, 0, -(x1 - x0)))
    run += n
panel('bit edge', run, BIT_T)

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
for width in range(max(s[0] for s in size.values()) + 2 * PAD, 160):
    where, height = pack(width)
    if best is None or max(width, height) < max(best[1], best[2]) or (max(width, height) == max(best[1], best[2]) and width * height < best[1] * best[2]): best = (where, width, height)
place, TW, TH = best


# ── Mesh ──
def to_blender(v): return Vector((-v.x, -v.z, v.y))


bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); shared = {}
for pts, pan, uvs, outward, key in faces:
    px, py, _, _ = place[pan]
    cos = [to_blender((p + Vector((0, LIFT, 0))) * T) for p in pts]
    nb = Vector((0, 0, 0))
    for i in range(len(cos)):
        a, b = cos[i], cos[(i + 1) % len(cos)]
        nb += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
    if nb.dot(to_blender(outward)) < 0: cos = cos[::-1]; uvs = uvs[::-1]
    vs = []
    for co in cos:
        if key:
            k = (key, round(co.x, 6), round(co.y, 6), round(co.z, 6))
            v = shared.get(k)
            if v is None: v = shared[k] = bm.verts.new(co)
        else: v = bm.verts.new(co)
        vs.append(v)
    f = bm.faces.new(vs); f.smooth = bool(key)
    for loop, (u, v) in zip(f.loops, uvs): loop[uvl].uv = ((px + u) / TW, 1 - (py + v) / TH)
bm.normal_update()
me = bpy.data.meshes.new('Dice box key'); bm.to_mesh(me); bm.free()
o = bpy.data.objects.new('Dice box key', me); bpy.context.scene.collection.objects.link(o)
me.materials.append(bpy.data.materials.new('Dice box key'))
triangles = sum(len(p.vertices) - 2 for p in me.polygons)

# ── Paint ──
# Deeper than the bedroom's brass: under the table player's own light a paler brass burns out to one flat yellow.
BRASS = np.array([[int(c[i:i + 2], 16) for i in (0, 2, 4)] for c in ('2e200d', '473315', '63491e', '7c5e26', '93732e', 'ad8c3c', 'd6b65c')], float)


def blotchy(w, h, base, cell=(2, 2)):
    """Old brass: the tone its place gives it in hard-edged clusters a few texels long, laid along the bar (cell: a
    cluster's width and height in texels), never a flat fill. (Each texel picked on its own read as a checker speckle up
    close, review 2026-11-12.) Here and there a single texel a step off, where the light catches a nick."""
    cw, ch = cell
    coarse = rng.choice([-2, -1, -1, 0, 0, 0, 1], size=(h // ch + 3, w // cw + 3))
    idx = np.empty((h, w), int)
    for y in range(h):
        shift = rng.integers(0, cw)
        for x in range(w):
            idx[y, x] = coarse[y // ch, (x + shift) // cw]
    odd = rng.random((h, w)) < .05
    idx[odd] += rng.choice([-1, 1], size=odd.sum())
    return base + idx


tex = np.zeros((TH, TW, 3))
for name, (x, y, w, h) in place.items():
    if name == 'bow':                      # rows go round the bar: top, outer side, underside, the side facing the hole
        idx = blotchy(w, h, 3, cell=(4, 1))
        rows = np.linspace(0, 1, h, endpoint=False)
        for r, t in enumerate(rows):
            idx[r] += (1 if t < .17 or t > .85 else 0 if t < .4 else -2 if t < .62 else -1)
        idx[0, rng.integers(0, w, 3)] = 6
    elif name in ('shank', 'collar'):      # columns go round it starting on top
        idx = blotchy(w, h, 3, cell=(1, 4))
        cols = np.linspace(0, 1, w, endpoint=False)
        for c, t in enumerate(cols):
            idx[:, c] += (1 if t < .13 or t > .87 else 0 if t < .3 or t > .7 else -2)
        if name == 'shank':
            idx[0:2, :] -= 1; idx[rng.integers(2, h - 1, 2), 0] = 6
        else: idx[:, 0] = np.maximum(idx[:, 0], 5)
    elif name == 'bit top':
        idx = blotchy(w, h, 4, cell=(3, 2)); idx[0, :] += 1; idx[-1, :] -= 1
    elif name == 'bit under': idx = blotchy(w, h, 1)
    elif name == 'bit edge': idx = blotchy(w, h, 2)
    else: idx = blotchy(w, h, 2)           # the ends of the bars
    img = BRASS[np.clip(idx, 0, 6)]
    tex[y:y + h, x:x + w] = img
    tex[y - 1, x:x + w] = img[0]; tex[y + h, x:x + w] = img[-1]; tex[y:y + h, x - 1] = img[:, 0]; tex[y:y + h, x + w] = img[:, -1]
image = bpy.data.images.new('Dice box key', TW, TH, alpha=False)
rgba = np.concatenate([tex[::-1] / 255, np.ones((TH, TW, 1))], axis=2)
image.pixels = rgba.astype(np.float32).ravel()
image.filepath_raw = os.path.join(out, 'Dice box key.png'); image.file_format = 'PNG'; image.save()

# The .blend goes in Source~ beside the model: Unity skips folders ending in ~, and a .blend it does import logs
# "Blender could not be found" on every computer without Blender set as the file's program.
os.makedirs(os.path.join(out, 'Source~'), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'Source~', 'Dice box key.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'Dice box key.fbx'), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                         bake_space_transform=True, mesh_smooth_type='FACE', axis_forward='-Z', axis_up='Y')
print('RESULT texture', TW, TH, 'triangles', triangles, 'length cm', round((SHANK_B - (BOW_X - BOW_R - BOW_r)) * T * 100, 1), {k: place[k] for k in place})
