import bpy, json, os, sys, argparse
from mathutils import Matrix
parser = argparse.ArgumentParser(description='Bake and simplify the supplied Astraia FBX without changing the original.')
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--report', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
if os.path.normcase(os.path.abspath(args.source)) == os.path.normcase(os.path.abspath(args.output)):
 raise ValueError('Output must be a different file from the source FBX.')
for destination in (args.output, args.report):
 os.makedirs(os.path.dirname(os.path.abspath(destination)), exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath(args.source))
meshes=[o for o in bpy.data.objects if o.type=='MESH']
# Bake source armature before normalizing object transforms; never alter the source FBX.
for o in meshes:
 bpy.context.view_layer.objects.active=o; o.select_set(True)
 for mod in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
 mw=o.matrix_world.copy(); o.parent=None; o.matrix_world=mw
 bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
 bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
for o in list(bpy.data.objects):
 if o.type!='MESH': bpy.data.objects.remove(o,do_unlink=True)
report=[]
targets={'Astraia_body':22000,'Astraia_dress':36000,'Astraia_face':20000,'Astraia_hair':26000,'Astraia_shoes':18000,'Astraia_stockings':13000,'Astraia_hair_accessory_L':5000,'Astraia_hair_accessory_R':5000}
for o in meshes:
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 before=len(o.data.polygons);target=targets.get(o.name,before)
 if before>target:
  d=o.modifiers.new('Playable polygon budget','DECIMATE');d.ratio=target/before;d.use_collapse_triangulate=True
  bpy.ops.object.modifier_apply(modifier=d.name)
 report.append({'name':o.name,'source_triangles':before,'triangles':len(o.data.polygons),'vertices':len(o.data.vertices)})
 print('OPTIMIZED '+json.dumps(report[-1]),flush=True)
bpy.ops.object.select_all(action='SELECT')
out=os.path.abspath(args.output)
bpy.ops.export_scene.fbx(filepath=out,use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='STRIP',use_mesh_modifiers=True)
with open(os.path.abspath(args.report),'w') as f: json.dump(report,f,indent=2)
print('ASTRAIA_OPTIMIZED_DONE',flush=True)

