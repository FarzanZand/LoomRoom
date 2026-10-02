"""Builds the new Green Slime in Blender: an opaque blob with eyes and mouth, skinned to the same
bone rig as the old slime (Base > Core > Top > Eye.L/Eye.R/Mouth > MouthHole) so its animation
clips keep working. Writes Green Slime.blend and Green Slime.fbx.

Coordinates: Unity (x, y, z) = Blender (x, -y... see U()) with Unity +Z (front) = Blender -Y and
Unity +Y (up) = Blender +Z. Every bone points straight up (Blender +Z) with no roll, so with
primary bone axis Y they import into Unity with identity rotations, like the old rig.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, TEX = argv[0], argv[1]


def U(x, y, z):
    """Unity position -> Blender position."""
    return Vector((x, -z, y))


bpy.ops.wm.read_factory_settings(use_empty=True)

# ── Rig ────────────────────────────────────────────────────────────────────
BONES = {  # name: (unity world head position, parent)
    "Base": ((0, 0, 0), None),
    "Core": ((0, .25, 0), "Base"),
    "Top": ((0, .5, 0), "Core"),
    # Unity mirrors X on FBX import, so Eye.L is built on Blender's -X to land on Unity +X like the old rig.
    "Eye.L": ((-.095, .36, .355), "Top"),
    "Eye.R": ((.095, .36, .355), "Top"),
    "Mouth": ((0, .225, .44), "Top"),
    "MouthHole": ((0, .222, .425), "Mouth"),
}
arm_data = bpy.data.armatures.new("Rig")
rig = bpy.data.objects.new("Rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for name, (pos, parent) in BONES.items():
    b = arm_data.edit_bones.new(name)
    b.head = U(*pos)
    b.tail = b.head + Vector((0, 0, .08))
    b.roll = 0
    if parent:
        b.parent = arm_data.edit_bones[parent]
        b.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")

# ── Mesh ───────────────────────────────────────────────────────────────────
bm = bmesh.new()
uv = bm.loops.layers.uv.new("UVMap")
SEG, RINGS = 20, 9
# Profile (height, radius) from the bottom up: wide droopy base, soft shoulders, domed top.
PROFILE = [(0.0, .40), (.03, .47), (.09, .48), (.18, .46), (.28, .42), (.38, .36),
           (.47, .28), (.54, .18), (.59, .07), (.60, 0.0)]
BODY_V0, BODY_V1 = 0.0, .75   # body occupies the lower 3/4 of the atlas

rings = []
for i, (h, r) in enumerate(PROFILE):
    ring = []
    for s in range(SEG):
        a = s / SEG * 2 * math.pi
        # Unity: x = sin(a) * r, z = cos(a) * r  -> a = 0 is the front (+Z)
        ring.append(bm.verts.new(U(math.sin(a) * r, h, math.cos(a) * r)))
    rings.append(ring)
bottom_center = bm.verts.new(U(0, 0, 0))
bm.verts.ensure_lookup_table()


def body_uv(seg, ring_i):
    # u .25 is the front in the texture: a = 0 (front) -> u = .25
    u = (.25 + seg / SEG) % 1.0
    v = BODY_V0 + (BODY_V1 - BODY_V0) * (PROFILE[ring_i][0] / PROFILE[-1][0])
    return u, v


for i in range(len(rings) - 1):
    for s in range(SEG):
        s2 = (s + 1) % SEG
        f = bm.faces.new((rings[i][s], rings[i][s2], rings[i + 1][s2], rings[i + 1][s]))
        corners = [(s, i), (s + 1, i), (s + 1, i + 1), (s, i + 1)]
        for loop, (ss, ri) in zip(f.loops, corners):
            u, v = body_uv(ss, ri)
            if ss == SEG:  # seam: keep u continuous across the wrap
                u = body_uv(0, ri)[0] + (1.0 if body_uv(0, ri)[0] < .5 else 0)
            loop[uv].uv = (u, v)
# Bottom cap (unseen, dark rim colour)
for s in range(SEG):
    f = bm.faces.new((rings[0][(s + 1) % SEG], rings[0][s], bottom_center))
    for loop in f.loops: loop[uv].uv = (.5, .01)


def add_blob(center_unity, radius, squash, segs, uv_tile, front_flat=True):
    """A small half-dome facing the front (+Z Unity), UV'd as a planar front projection into a tile."""
    c = U(*center_unity)
    verts = []
    rows = 4
    grid = []
    for r_i in range(rows + 1):
        ring = []
        phi = (r_i / rows) * (math.pi / 2)  # 0 = tip, pi/2 = rim
        for s in range(segs):
            th = s / segs * 2 * math.pi
            x = math.sin(phi) * math.cos(th) * radius
            y = math.sin(phi) * math.sin(th) * radius * squash
            z = math.cos(phi) * radius * .55
            ring.append(bm.verts.new(c + Vector((x, -z, y))))  # bulge toward Unity +Z (Blender -Y)
        grid.append(ring)
    (u0, v0, u1, v1) = uv_tile
    def tile_uv(vert):
        p = vert.co - c
        tu = .5 + p.x / (2 * radius)
        tv = .5 + p.z / (2 * radius * squash)
        return (u0 + (u1 - u0) * tu, v0 + (v1 - v0) * tv)
    for r_i in range(rows):
        for s in range(segs):
            s2 = (s + 1) % segs
            f = bm.faces.new((grid[r_i][s], grid[r_i + 1][s], grid[r_i + 1][s2], grid[r_i][s2]))
            for loop in f.loops: loop[uv].uv = tile_uv(loop.vert)
    return [v for ring in grid for v in ring]


EYE_TILE = (0 / 64, 48 / 64, 16 / 64, 1.0)
MOUTH_TILE = (16 / 64, 48 / 64, 32 / 64, 1.0)
# Eyes sit on the surface at the bone positions, pushed slightly out.
eye_l = add_blob((.095, .36, .385), .065, 1.25, 10, EYE_TILE)
eye_r = add_blob((-.095, .36, .385), .065, 1.25, 10, EYE_TILE)
mouth = add_blob((0, .225, .455), .07, .45, 12, MOUTH_TILE)

bmesh.ops.remove_doubles(bm, verts=[v for ring in rings for v in ring], dist=1e-5)
body_mesh = bpy.data.meshes.new("Body")
bm.to_mesh(body_mesh)
bm.free()
body = bpy.data.objects.new("Body", body_mesh)
bpy.context.scene.collection.objects.link(body)
for p in body_mesh.polygons: p.use_smooth = True

# Material
mat = bpy.data.materials.new("Green Slime")
mat.use_nodes = True
tex_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex_node.image = bpy.data.images.load(TEX)
tex_node.interpolation = "Closest"
bsdf = mat.node_tree.nodes["Principled BSDF"]
mat.node_tree.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = .45
body_mesh.materials.append(mat)

# ── Skin weights ───────────────────────────────────────────────────────────
groups = {n: body.vertex_groups.new(name=n) for n in BONES}
eye_l_ids = {v.index for v in body_mesh.vertices if (v.co - U(-.095, .36, .385)).length < .08}
eye_r_ids = {v.index for v in body_mesh.vertices if (v.co - U(.095, .36, .385)).length < .08}
mouth_ids = {v.index for v in body_mesh.vertices if (v.co - U(0, .225, .455)).length < .08 and v.index not in eye_l_ids | eye_r_ids}
for v in body_mesh.vertices:
    i = v.index
    if i in eye_l_ids: groups["Eye.L"].add([i], 1.0, "REPLACE"); continue
    if i in eye_r_ids: groups["Eye.R"].add([i], 1.0, "REPLACE"); continue
    if i in mouth_ids: groups["Mouth"].add([i], 1.0, "REPLACE"); continue
    h = v.co.z  # Unity height
    # Base for the bottom, Core through the middle, Top for the dome; smooth blends between.
    wb = max(0.0, 1 - h / .2)
    wt = max(0.0, min(1.0, (h - .3) / .2))
    wc = max(0.0, 1 - wb - wt)
    for name, w in (("Base", wb), ("Core", wc), ("Top", wt)):
        if w > 0: groups[name].add([i], w, "REPLACE")

mod = body.modifiers.new("Rig", "ARMATURE")
mod.object = rig
body.parent = rig

# ── Save and export ────────────────────────────────────────────────────────
os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "Green Slime.blend"))
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, "Green Slime.fbx"),
    use_selection=False, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    use_armature_deform_only=True, bake_anim=False, mesh_smooth_type="FACE",
    path_mode="STRIP")

# Preview render (front three-quarter view)
cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = U(.9, .75, 1.2); cam.rotation_euler = (math.radians(68), 0, math.radians(37))
bpy.context.scene.camera = cam
light = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
light.rotation_euler = (math.radians(40), 0, math.radians(30)); bpy.context.scene.collection.objects.link(light)
bpy.context.scene.render.engine = "BLENDER_EEVEE"
bpy.context.scene.render.resolution_x = bpy.context.scene.render.resolution_y = 320
bpy.context.scene.render.filepath = os.path.join(OUT_DIR, "preview.png")
bpy.ops.render.render(write_still=True)
print("SLIME DONE", len(body_mesh.vertices), "verts")
