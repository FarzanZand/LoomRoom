"""The Great bell's mounting, in metres, Unity axes (y up), two objects exported together:
- Mount (on the prefab's root, the top of the ceiling plate at y 0): an oak ceiling plate and two flat iron hangers down
  to bearing blocks at the axle's ends.
- Headstock (under the Swing child, its origin on the axle, which lies along x): a bevelled oak beam the bell hangs
  from, two iron bands round it, an iron pin out of each end into its bearing, and two iron straps down each side
  that clamp the bell's crown (they end sunk into its shoulder).
Every part that touches another is sunk into it by at least 1 cm or stands 1 cm proud of it, so no faces share a plane.
Materials: Oak, Iron (flat colours; the pixel restyle grades them).
blender -b -P great_bell_mount.py -- out.fbx [axle depth]"""
import bpy, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]
AXLE = float(args[1]) if len(args) > 1 else .40     # axle below the top of the ceiling plate
bpy.ops.wm.read_factory_settings(use_empty=True)

# Unity (x, y, z) -> Blender: measured through this export and Unity's import with the axis conversion baked
# (see Tools/dm_die.py); the eval bakes the importer's turn into the meshes
def U(x, y, z):
    return Vector((x, z, y))

def S(x, y, z):
    return (x, z, y)

def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m

OAK, IRON = mat('Oak', (.36, .24, .14)), mat('Iron', (.22, .21, .20))

def box(centre, size, m, bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=U(*centre))
    o = bpy.context.object; o.scale = S(*size)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        b = o.modifiers.new('b', 'BEVEL'); b.width = bevel; b.segments = 2; b.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier='b')
    o.data.materials.append(m)
    return o

def pin(centre, radius, length, m):
    # a round pin along x
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=length, location=U(*centre), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.data.materials.append(m)
    return o

def join(parts, name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.object; o.name = name; o.data.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return o

BEAM_L, BEAM_H, BEAM_D = 1.10, .24, .24     # the headstock beam (x, y, z)
PIN_R, PIN_L = .035, .16
HANGER_X = BEAM_L / 2 + .07                 # hangers stand just past the beam's ends, round the pins
BLOCK = (.07, .11, .11)                      # bearing block round each pin

# Mount: plate at the ceiling, hangers, bearing blocks (y measured from the plate's top, the root)
mount = [box((0, -.03, 0), (BEAM_L + .44, .06, .34), OAK, .01)]
for sx in (-1, 1):
    x = sx * HANGER_X
    top, bottom = -.05, -AXLE + BLOCK[1] / 2 - .01          # 1 cm into the plate and into the block
    mount.append(box((x, (top + bottom) / 2, 0), (.05, top - bottom, .07), IRON, .006))
    mount.append(box((x, -AXLE, 0), BLOCK, IRON, .012))
    # two bolt heads where each hanger meets the plate, 1 cm proud of its side
    for sz in (-1, 1):
        mount.append(box((x, -.09, sz * .045), (.03, .03, .04), IRON, .004))
mount = join(mount, 'Mount')

# Headstock, around the axle at the origin
head = [box((0, 0, 0), (BEAM_L, BEAM_H, BEAM_D), OAK, .03)]
for sx in (-1, 1):
    # pins out of the beam's ends (1 cm into the beam) through the bearing blocks
    head.append(pin((sx * (BEAM_L / 2 - .01 + PIN_L / 2), 0, 0), PIN_R, PIN_L, IRON))
    # bands round the beam, 1 cm proud of it all round
    head.append(box((sx * .40, 0, 0), (.05, BEAM_H + .02, BEAM_D + .02), IRON, .006))
    # crown straps on both sides: from 1 cm over the beam's top down into the bell's shoulder
    for sz in (-1, 1):
        top, bottom = BEAM_H / 2 + .015, -BEAM_H / 2 - .30
        head.append(box((sx * .13, (top + bottom) / 2, sz * (BEAM_D / 2 + .02)), (.06, top - bottom, .02), IRON, .005))
    # the strap's head over the beam's top (1 cm into it, a cm wider and deeper than the straps it caps)
    head.append(box((sx * .13, BEAM_H / 2 + .005, 0), (.07, .03, BEAM_D + .08), IRON, .005))
head = join(head, 'Headstock')

for o in (mount, head):
    o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
print('mount', len(mount.data.polygons), 'headstock', len(head.data.polygons))
