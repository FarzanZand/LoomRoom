"""Cut a room player head down to a low-poly mesh like the Dungeon Master's faces (about 1,800 triangles): the OBJ
written by Tools/room_head_lowpoly.eval.cs (positions, UVs, normals in the mesh's own space) is collapsed in Blender
with its UVs kept, and written back for the eval to rebuild the skinned mesh.

    blender -b -P Tools/room_head_decimate.py -- <in.obj> <out.obj> <target triangles>
"""
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, target = argv[0], argv[1], int(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=src, forward_axis='NEGATIVE_Z', up_axis='Y')
ob = bpy.context.selected_objects[0]
bpy.context.view_layer.objects.active = ob
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-6)   # welds the split seam vertices; their UVs stay per corner
bpy.ops.object.mode_set(mode='OBJECT')
tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
m = ob.modifiers.new("decimate", 'DECIMATE')
m.decimate_type = 'COLLAPSE'
m.ratio = min(1.0, target / max(1, tris))
m.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=m.name)
out = sum(len(p.vertices) - 2 for p in ob.data.polygons)
bpy.ops.wm.obj_export(filepath=dst, forward_axis='NEGATIVE_Z', up_axis='Y', export_selected_objects=False, export_uv=True,
                      export_normals=True, export_materials=False, export_triangulated_mesh=True, apply_modifiers=True)
print(f"decimated {tris} -> {out} triangles")
