"""The bedroom's locked dice box (World/Art/Dice box): a small bound casket with an arched lid, two brass straps that
run down the back as its hinges, a lock plate with a keyhole and a hasp on the front, four bun feet and a lined inside.
Built in the room's units (about 22 to the metre) on a grid of texels: the bedroom is 6.4 texels a unit, so one texel is
0.15625 units and every size below is in texels. Every face is laid into a named panel of the texture at exactly that
density (no unwrap), and the panels' places are written to a layout file that Tools/dice_box_paint.py paints from.

Unity box space: x to the right, y up, z to the front; the root's pivot is the middle of the base. Two objects: Body, and
Lid with its pivot on the back hinge line, so the lid opens by turning about its own x.

    blender -b -P Tools/dice_box.py -- "<out folder>"      writes Dice box.fbx, Source~/Dice box.blend, Dice box layout.json
"""
import bpy, bmesh, math, json, sys, os
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
T = 1 / 6.4                      # one texel in room units

# ── Sizes (texels) ──
HX, HZ = 32, 19                  # half width, half depth: 64 x 38 texels = 10 x 5.94 units
FEET = 3                         # feet under the body
Y0, Y1 = FEET, FEET + 22         # body bottom and top (the seam)
WALL = 2                         # wall, floor and lid thickness
SKIRT, RISE = 4, 10              # the lid's upright edge and the arch's rise above it
ARC = 10                         # segments across the arch
PROUD = 1.0                      # how far straps and plates stand off the wood
BURY = .3                        # how deep their side faces go into it
STRAPS = ((-19, -13), (13, 19))  # each strap's x range
R = (HZ * HZ + RISE * RISE) / (2 * RISE)
YC = Y1 + SKIRT + RISE - R       # the arch's centre
HINGE = (0, Y1, -HZ)

panels = {}                      # name -> [width, height] in texels
faces = []                       # (object, points, panel, uv in panel texels, outward direction, smooth key or None)


def panel(name, w, h):
    p = panels.setdefault(name, [0, 0]); p[0] = max(p[0], w); p[1] = max(p[1], h); return name


def quad(obj, origin, du, dv, w, h, name, u0=0, v0=0, n=None):
    """A rectangle: from origin, w texels along du (the texture's right) and h along dv (the texture's down). Seen from
    outside with right and down as given it faces along du x dv; n says otherwise."""
    o, du, dv = Vector(origin), Vector(du), Vector(dv)
    panel(name, u0 + w, v0 + h)
    faces.append((obj, [o, o + du * w, o + du * w + dv * h, o + dv * h], name,
                  [(u0, v0), (u0 + w, v0), (u0 + w, v0 + h), (u0, v0 + h)], Vector(n) if n else du.cross(dv), None))


def level(obj, xa, xb, za, zb, y, name, up=True, ox=HX, oz=HZ):
    """A level rectangle: u = x, v runs toward the back."""
    pts = [Vector((xa, y, zb)), Vector((xb, y, zb)), Vector((xb, y, za)), Vector((xa, y, za))]
    panel(name, 2 * ox, 2 * oz)
    faces.append((obj, pts, name, [(p.x + ox, oz - p.z) for p in pts], Vector((0, 1 if up else -1, 0)), None))


def path(d, y_from=Y1):
    """The lid's outline from the back seam up over the arch to the front seam, d texels outside the wood (negative =
    inside): (z, y) points and how far along each is. The arch is concentric, so it meets the upright part a little
    higher or lower."""
    z = HZ + d; r = R + d
    a = math.asin(z / r); yj = YC + r * math.cos(a)
    pts = [(-z, y_from), (-z, yj)]
    for i in range(1, ARC):
        f = -a + 2 * a * i / ARC; pts.append((r * math.sin(f), YC + r * math.cos(f)))
    pts += [(z, yj), (z, y_from)]
    s = [0.0, yj - y_from]
    for i in range(1, ARC + 1): s.append(s[1] + r * 2 * a * i / ARC)
    s.append(s[-1] + yj - y_from)
    return pts, s


def strip(obj, xa, xb, d, name, key, inward=False, v0=0):
    """A band across x = xa..xb following the lid's outline at offset d, smooth over the arch."""
    pts, s = path(d)
    w = abs(xb - xa); panel(name, w, v0 + s[-1])
    mid = Vector(((xa + xb) / 2, Y1 + 2, 0))
    for i in range(len(pts) - 1):
        (z0, y0), (z1, y1) = pts[i], pts[i + 1]
        p = [Vector((xb, y0, z0)), Vector((xa, y0, z0)), Vector((xa, y1, z1)), Vector((xb, y1, z1))]
        uv = [(0, v0 + s[i]), (w, v0 + s[i]), (w, v0 + s[i + 1]), (0, v0 + s[i + 1])]
        away = (p[0] + p[2]) / 2 - mid
        faces.append((obj, p, name, uv, -away if inward else away, key if 1 <= i <= ARC else None))


def side(obj, x, d0, d1, name, nx, v0=0):
    """A strap's edge: the ruled face at one x between the outline at d0 and at d1."""
    a, _ = path(d0); b, sb = path(d1)
    w = abs(d1 - d0); panel(name, w, v0 + sb[-1])
    for i in range(len(a) - 1):
        p = [Vector((x, a[i][1], a[i][0])), Vector((x, b[i][1], b[i][0])), Vector((x, b[i + 1][1], b[i + 1][0])), Vector((x, a[i + 1][1], a[i + 1][0]))]
        uv = [(0, v0 + sb[i]), (w, v0 + sb[i]), (w, v0 + sb[i + 1]), (0, v0 + sb[i + 1])]
        faces.append((obj, p, name, uv, Vector((nx, 0, 0)), None))


def end(obj, x, d, name, nx):
    """The lid's end wall at one x: the upright part and the arch above it as one face."""
    pts, _ = path(d)
    z = HZ + d; top_y = YC + R + d
    p = [Vector((x, y, zz)) for zz, y in pts]
    uv = [((zz + z) if nx < 0 else (z - zz), top_y - y) for zz, y in pts]
    panel(name, 2 * z, top_y - Y1)
    faces.append((obj, p, name, uv, Vector((nx, 0, 0)), None))


def plate(obj, xa, xb, ya, yb, za, zb, face_name, edge, back=False):
    """A plate standing off the front: its face in its own panel, its edges in the shared edge panel."""
    w, h, dz = xb - xa, yb - ya, zb - za
    quad(obj, (xb, yb, zb), (-1, 0, 0), (0, -1, 0), w, h, face_name)
    quad(obj, (xa, yb, za), (1, 0, 0), (0, 0, 1), w, dz, edge, n=(0, 1, 0))
    quad(obj, (xa, ya, za), (1, 0, 0), (0, 0, 1), w, dz, edge, n=(0, -1, 0))
    quad(obj, (xb, yb, za), (0, 0, 1), (0, -1, 0), dz, h, edge, n=(1, 0, 0))
    quad(obj, (xa, yb, za), (0, 0, 1), (0, -1, 0), dz, h, edge, n=(-1, 0, 0))
    if back: quad(obj, (xa, yb, za), (1, 0, 0), (0, -1, 0), w, h, edge, n=(0, 0, -1))


def tube(obj, centre, axis, radius, length, n, name, key, cap=None):
    """A round bar: n sides, smooth round its length, with caps in the cap panel when one is given."""
    c, ax = Vector(centre), Vector(axis).normalized()
    a = ax.orthogonal().normalized(); b = ax.cross(a)
    circ = 2 * math.pi * radius
    panel(name, circ, length)
    ring = lambda k, t: c + ax * t + (a * math.cos(2 * math.pi * k / n) + b * math.sin(2 * math.pi * k / n)) * radius
    for k in range(n):
        p = [ring(k, 0), ring(k + 1, 0), ring(k + 1, length), ring(k, length)]
        uv = [(circ * k / n, 0), (circ * (k + 1) / n, 0), (circ * (k + 1) / n, length), (circ * k / n, length)]
        faces.append((obj, p, name, uv, (p[0] + p[2]) / 2 - (c + ax * length / 2), key))
    if cap:
        panel(cap, 2 * radius, 2 * radius)
        for t, sign in ((0, -1), (length, 1)):
            p = [ring(k, t) for k in range(n)]
            uv = [(radius + radius * math.cos(2 * math.pi * k / n), radius + radius * math.sin(2 * math.pi * k / n)) for k in range(n)]
            faces.append((obj, p, cap, uv, ax * sign, None))


B, L = 'Body', 'Lid'
BODY = Y1 - Y0
# ── Body: outside, rim, lined inside ──
quad(B, (HX, Y1, HZ), (-1, 0, 0), (0, -1, 0), 2 * HX, BODY, 'body front')
quad(B, (-HX, Y1, -HZ), (1, 0, 0), (0, -1, 0), 2 * HX, BODY, 'body back')
quad(B, (HX, Y1, -HZ), (0, 0, 1), (0, -1, 0), 2 * HZ, BODY, 'body right')
quad(B, (-HX, Y1, HZ), (0, 0, -1), (0, -1, 0), 2 * HZ, BODY, 'body left')
level(B, -HX, HX, -HZ, HZ, Y0, 'body under', up=False)
IX, IZ = HX - WALL, HZ - WALL
level(B, -HX, HX, IZ, HZ, Y1, 'body rim'); level(B, -HX, HX, -HZ, -IZ, Y1, 'body rim')
level(B, -HX, -IX, -IZ, IZ, Y1, 'body rim'); level(B, IX, HX, -IZ, IZ, Y1, 'body rim')
FLOOR = Y0 + WALL
quad(B, (-IX, Y1, IZ), (1, 0, 0), (0, -1, 0), 2 * IX, Y1 - FLOOR, 'lining front')
quad(B, (IX, Y1, -IZ), (-1, 0, 0), (0, -1, 0), 2 * IX, Y1 - FLOOR, 'lining back')
quad(B, (IX, Y1, IZ), (0, 0, -1), (0, -1, 0), 2 * IZ, Y1 - FLOOR, 'lining right')
quad(B, (-IX, Y1, -IZ), (0, 0, 1), (0, -1, 0), 2 * IZ, Y1 - FLOOR, 'lining left')
level(B, -IX, IX, -IZ, IZ, FLOOR, 'lining floor', ox=IX, oz=IZ)

# ── Lid: outside over the arch, ends, lined inside, the rim it shuts on ──
strip(L, -HX, HX, 0, 'lid out', 'lid')
end(L, HX, 0, 'lid end right', 1); end(L, -HX, 0, 'lid end left', -1)
strip(L, -IX, IX, -WALL, 'lid lining', 'lid in', inward=True)
end(L, IX, -WALL, 'lid lining end right', -1); end(L, -IX, -WALL, 'lid lining end left', 1)
level(L, -HX, HX, IZ, HZ, Y1, 'lid rim', up=False); level(L, -HX, HX, -HZ, -IZ, Y1, 'lid rim', up=False)
level(L, -HX, -IX, -IZ, IZ, Y1, 'lid rim', up=False); level(L, IX, HX, -IZ, IZ, Y1, 'lid rim', up=False)

# ── Straps: over the lid, down the front and the back of the body ──
LID_STRAP = path(PROUD)[1][-1]
for n, (xa, xb) in enumerate(STRAPS):
    name = f'strap {n + 1}'
    w = xb - xa
    strip(L, xa, xb, PROUD, name, f'strap {n}', v0=BODY)                     # the lid's part, in the panel's middle
    side(L, xa, -BURY, PROUD, 'strap edge', -1, v0=BODY); side(L, xb, -BURY, PROUD, 'strap edge', 1, v0=BODY)
    quad(B, (xa, Y0, -HZ - PROUD), (1, 0, 0), (0, 1, 0), w, BODY, name, n=(0, 0, -1))      # the back, above it in the panel
    quad(B, (xb, Y1, HZ + PROUD), (-1, 0, 0), (0, -1, 0), w, BODY, name, v0=BODY + LID_STRAP)  # the front, below it
    for za, zb in ((HZ - BURY, HZ + PROUD), (-HZ - PROUD, -HZ + BURY)):
        d = zb - za
        quad(L, (xa, Y1, za), (1, 0, 0), (0, 0, 1), w, d, 'strap edge', n=(0, -1, 0))     # the lid strap's end at the seam
        quad(B, (xa, Y1, za), (0, 0, 1), (0, -1, 0), d, BODY, 'strap edge', n=(-1, 0, 0))  # the body strap's two edges
        quad(B, (xb, Y1, za), (0, 0, 1), (0, -1, 0), d, BODY, 'strap edge', n=(1, 0, 0))
        quad(B, (xa, Y1, za), (1, 0, 0), (0, 0, 1), w, d, 'strap edge', n=(0, 1, 0))      # its top at the seam
        quad(B, (xa, Y0, za), (1, 0, 0), (0, 0, 1), w, d, 'strap edge', n=(0, -1, 0))     # its foot

# ── Lock: a plate with the keyhole on the body, a hasp on the lid that hangs over it ──
plate(B, -6, 6, Y1 - 15, Y1 - 1, HZ - BURY, HZ + PROUD, 'lock plate', 'plate edge')
plate(L, -4, 4, Y1 + .5, Y1 + SKIRT, HZ - BURY, HZ + PROUD, 'hasp plate', 'plate edge')
plate(L, -2.5, 2.5, Y1 - 6, Y1 + 1.5, HZ + PROUD + .12, HZ + PROUD + 1, 'hasp tongue', 'plate edge', back=True)

# ── Hinge barrels on the straps at the back seam, round the lid's own axis ──
for n, (xa, xb) in enumerate(STRAPS):
    tube(B, (xa - .5, Y1, -HZ), (1, 0, 0), 1.35, xb - xa + 1, 10, 'hinge', f'hinge {n}', cap='hinge cap')

# ── Feet ──
for n, (fx, fz) in enumerate(((-26, -13), (26, -13), (-26, 13), (26, 13))):
    tube(B, (fx, 0, fz), (0, 1, 0), 3.5, FEET, 12, 'foot', f'foot {n}')
    ring = [Vector((fx + 3.5 * math.cos(2 * math.pi * k / 12), 0, fz + 3.5 * math.sin(2 * math.pi * k / 12))) for k in range(12)]
    panel('foot under', 7, 7)
    faces.append((B, ring, 'foot under', [(3.5 + p.x - fx, 3.5 + p.z - fz) for p in ring], Vector((0, -1, 0)), None))

# ── Pack the panels into one texture (shelves, 2 texels between) ──
PAD = 2
order = sorted(panels, key=lambda k: (-math.ceil(panels[k][1] - 1e-6), -panels[k][0]))
size = {k: (math.ceil(panels[k][0] - 1e-6), math.ceil(panels[k][1] - 1e-6)) for k in panels}


def pack(width):
    x = y = PAD; row = 0; where = {}
    for k in order:
        w, h = size[k]
        if x + w + PAD > width: x = PAD; y += row + PAD; row = 0
        where[k] = (x, y, w, h); x += w + PAD; row = max(row, h)
    return where, y + row + PAD


best = None
for width in range(max(s[0] for s in size.values()) + 2 * PAD, 400, 2):
    where, height = pack(width)
    if best is None or max(width, height) < max(best[1], best[2]) or (max(width, height) == max(best[1], best[2]) and width * height < best[1] * best[2]):
        best = (where, width, height)
place, TW, TH = best


# ── Build the two meshes ──
def to_blender(v):               # Unity box space -> Blender (z up, the front along -Y); a mirror, as the importer mirrors back
    return Vector((-v.x, -v.z, v.y))


bpy.ops.wm.read_factory_settings(use_empty=True)
mat = bpy.data.materials.new('Dice box')
count = {}
for name, origin in ((B, Vector((0, 0, 0))), (L, Vector(HINGE))):
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); shared = {}
    for obj, pts, pan, uvs, outward, key in faces:
        if obj != name: continue
        px, py, _, _ = place[pan]
        cos = [to_blender((Vector(p) - origin) * T) for p in pts]
        # wind it so Blender's normal points outward
        nb = Vector((0, 0, 0))
        for i in range(len(cos)):
            a, b = cos[i], cos[(i + 1) % len(cos)]
            nb += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        if nb.dot(to_blender(outward)) < 0: cos = cos[::-1]; uvs = uvs[::-1]
        vs = []
        for co in cos:
            if key:
                k = (key, round(co.x, 5), round(co.y, 5), round(co.z, 5))
                v = shared.get(k)
                if v is None: v = shared[k] = bm.verts.new(co)
            else: v = bm.verts.new(co)
            vs.append(v)
        f = bm.faces.new(vs)
        f.smooth = bool(key)
        for loop, (u, v) in zip(f.loops, uvs): loop[uvl].uv = ((px + u) / TW, 1 - (py + v) / TH)
    bm.normal_update()
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.location = to_blender(origin * T)
    me.materials.append(mat)
    count[name] = sum(len(p.vertices) - 2 for p in me.polygons)

json.dump({'width': TW, 'height': TH, 'texels_per_unit': 6.4,
           'panels': {k: list(place[k]) + [panels[k][0], panels[k][1]] for k in place},
           'sizes': {'half_x': HX, 'half_z': HZ, 'feet': FEET, 'seam': Y1, 'wall': WALL, 'skirt': SKIRT, 'rise': RISE, 'body': BODY,
                     'straps': STRAPS, 'lid_len': path(0)[1][-1], 'lid_join': path(0)[1][1], 'strap_len': LID_STRAP, 'strap_join': path(PROUD)[1][1],
                     'lining_len': path(-WALL)[1][-1], 'hinge_units': [h * T for h in HINGE], 'top_units': (YC + R + PROUD) * T}},
          open(os.path.join(out, 'Dice box layout.json'), 'w'), indent=1)
# The .blend goes in Source~ beside the model: Unity skips folders ending in ~, and a .blend it does import logs
# "Blender could not be found" on every computer without Blender set as the file's program.
os.makedirs(os.path.join(out, 'Source~'), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'Source~', 'Dice box.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'Dice box.fbx'), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                         bake_space_transform=True, mesh_smooth_type='FACE', axis_forward='-Z', axis_up='Y')
print('RESULT texture', TW, TH, 'triangles', count, 'total', sum(count.values()))
