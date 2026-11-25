"""The Pianist's desk statue as a model: a small grand piano with its lid up and a bench, to be cast in pewter by
Tools/pianist_statue.eval.cs (the first statue was the library's upright piano in plain pewter: a grey box from where the
player stands). In the room's units, Unity space (y up, standing on y 0, the keyboard and bench toward -z):
the case in a grand's outline on three legs, a keyboard of seven chunky white keys and five black ones (real key
widths would be under a texel at the bedroom's 6.4 texels a unit), the lid raised on its stick over a dark inside, a
pedal lyre, a bench. 3.6 wide, 6.95 long, 4.3 to the lid's edge. One object per material: Pewter, Pewter light (the
white keys), Pewter dark (the black keys and the inside under the lid), so the cast keeps the keys apart in tone.

    blender -b -P Tools/pianist_piano.py -- out.fbx [out.blend]
"""
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]
bpy.ops.wm.read_factory_settings(use_empty=True)

COLOURS = {'Pewter': (.47, .47, .5, 1), 'Pewter light': (.95, .95, .96, 1), 'Pewter dark': (.06, .06, .07, 1)}
meshes = {name: bmesh.new() for name in COLOURS}


def U(x, y, z):  # Unity space -> Blender (z up)
    return Vector((x, -z, y))


def box(mat, x0, x1, y0, y1, z0, z1, taper=1.0):
    """A box in Unity space; taper narrows its foot (x and z) toward its middle."""
    bm = meshes[mat]
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    def P(x, y, z):
        k = taper if y == y0 else 1.0
        return bm.verts.new(U(cx + (x - cx) * k, y, cz + (z - cz) * k))
    v = [P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1), P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)]
    for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
        bm.faces.new([v[i] for i in f])


def slab(mat, outline, y0, y1, top_mat=None, turn=None):
    """A flat piece with this outline (x, z) from y0 to y1; turn: a matrix applied to its points (the raised lid)."""
    def P(x, y, z):
        p = Vector((x, y, z))
        if turn: p = turn @ p
        return U(p.x, p.y, p.z)
    bm = meshes[mat]
    lo = [bm.verts.new(P(x, y0, z)) for x, z in outline]; hi = [bm.verts.new(P(x, y1, z)) for x, z in outline]
    n = len(outline)
    for i in range(n):
        bm.faces.new([lo[i], lo[(i + 1) % n], hi[(i + 1) % n], hi[i]])
    bm.faces.new(lo)
    if top_mat is None: bm.faces.new(list(reversed(hi)))
    else:
        tm = meshes[top_mat]
        tm.faces.new(list(reversed([tm.verts.new(P(x, y1 + .001, z)) for x, z in outline])))


# The case, seen from above: the keyboard's side straight, the bass side long, the tail rounded, the treble side drawn in.
CASE = [(-1.8, -1.6), (1.8, -1.6), (1.8, .1), (1.66, .9), (1.3, 1.65), (.85, 2.3), (.45, 2.8), (-.1, 3.08), (-.75, 3.1), (-1.35, 2.95), (-1.72, 2.6), (-1.8, 2.2)]
LEGS, BODY = 1.5, 2.3
# Its inside shows dark under the lid: a rim of pewter round a dark top.
slab('Pewter', CASE, LEGS, BODY, top_mat='Pewter dark')
RIM = []
cx = sum(p[0] for p in CASE) / len(CASE); cz = sum(p[1] for p in CASE) / len(CASE)
for x, z in CASE: RIM.append((cx + (x - cx) * .9, cz + (z - cz) * .92))
# (the rim: the case's top edge as a thin pewter frame standing a little over the dark inside)
bm = meshes['Pewter']
outer = [bm.verts.new(U(x, BODY + .06, z)) for x, z in CASE]; inner = [bm.verts.new(U(x, BODY + .06, z)) for x, z in RIM]
outer_lo = [bm.verts.new(U(x, BODY, z)) for x, z in CASE]; inner_lo = [bm.verts.new(U(x, BODY, z)) for x, z in RIM]
n = len(CASE)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new([outer[i], inner[i], inner[j], outer[j]])              # the frame's top
    bm.faces.new([inner_lo[i], inner_lo[j], inner[j], inner[i]])        # its inner wall
    bm.faces.new([outer_lo[j], outer_lo[i], outer[i], outer[j]])        # its outer wall

# The keyboard: a bed in front of the case, seven white keys on it, five black between them.
box('Pewter', -1.75, 1.75, LEGS, 1.84, -2.62, -1.6)          # deep: the keys are what says piano from above
KEY = 3.3 / 7
for k in range(7):
    x0 = -1.65 + k * KEY
    box('Pewter light', x0 + .03, x0 + KEY - .03, 1.84, 1.96, -2.58, -1.6)
for k in (0, 1, 3, 4, 5):                                               # between the 1st and 2nd, 2nd and 3rd, 4th to 7th
    x = -1.65 + (k + 1) * KEY
    box('Pewter dark', x - .13, x + .13, 1.96, 2.09, -2.2, -1.6)

# Three legs and the pedal lyre.
for x, z in ((-1.5, -1.3), (1.5, -1.3), (-.55, 2.55)):
    box('Pewter', x - .17, x + .17, 0, LEGS, z - .17, z + .17, taper=.6)
box('Pewter', -.1, .1, .3, LEGS, -1.25, -1.1)
box('Pewter', -.42, .42, .12, .3, -1.42, -1.05)

# The lid: the case's outline, hinged along the bass side and raised, with its stick.
ANGLE = math.radians(33)
hinge = Matrix.Translation(Vector((-1.8, BODY + .08, 0))) @ Matrix.Rotation(ANGLE, 4, Vector((0, 0, 1))) @ Matrix.Translation(Vector((1.8, 0, 0)))
slab('Pewter', CASE, 0, .1, turn=hinge)
tip = hinge @ Vector((1.25, 0, .7))
bm = meshes['Pewter']
stick = [bm.verts.new(U(x, y, z)) for x, y, z in ((1.42, BODY + .06, .62), (1.5, BODY + .06, .78), (tip.x + .05, tip.y, .78), (tip.x - .03, tip.y, .62))]
bm.faces.new(stick); bm.faces.new(list(reversed([bm.verts.new(v.co) for v in stick])))

# The bench in front of the keys.
box('Pewter', -.9, .9, .92, 1.12, -3.85, -3.15)
for x in (-.76, .76):
    for z in (-3.72, -3.28):
        box('Pewter', x - .08, x + .08, 0, .92, z - .08, z + .08)

objects = []
for name, bm in meshes.items():
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = False
    me.uv_layers.new(name='UVMap')
    m = bpy.data.materials.new(name); m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = COLOURS[name]
    m.diffuse_color = COLOURS[name]
    me.materials.append(m)
    o = bpy.data.objects.new('Piano ' + name, me); bpy.context.collection.objects.link(o); objects.append(o)
    print(name, len(me.polygons), 'faces')

if len(args) > 1: bpy.ops.wm.save_as_mainfile(filepath=args[1])
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False)
print('wrote', out)
