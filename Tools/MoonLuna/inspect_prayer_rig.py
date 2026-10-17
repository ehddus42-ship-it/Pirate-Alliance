from pathlib import Path
import json,bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Documentation/MoonLuna/PeacefulPrayerQA';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Game.fbx'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.data.objects if o.type=='MESH')
report={'bones':{b.name:{'head':list(rig.matrix_world@b.head_local),'tail':list(rig.matrix_world@b.tail_local),'parent':b.parent.name if b.parent else None} for b in rig.data.bones},'meshes':[]}
for o in bpy.data.objects:
 if o.type=='MESH':report['meshes'].append({'name':o.name,'vertices':len(o.data.vertices),'bounds':[list(o.matrix_world@Vector(p)) for p in o.bound_box]})
report['regions']={}
for label,test in [('left_hand',lambda p:p.x>.18 and .65<p.z<.95),('right_hand',lambda p:p.x<-.18 and .65<p.z<.95),('inside_skirt',lambda p:abs(p.x)<.10 and abs(p.y)<.07 and .25<p.z<.75)]:
 vertices=[v for v in mesh.data.vertices if test(mesh.matrix_world@v.co)]
 weights={}
 for v in vertices:
  for g in v.groups:
   name=mesh.vertex_groups[g.group].name;weights[name]=weights.get(name,0)+g.weight
 report['regions'][label]={'count':len(vertices),'average_weights':{k:v/max(1,len(vertices)) for k,v in weights.items()}}
(OUT/'source_geometry.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
