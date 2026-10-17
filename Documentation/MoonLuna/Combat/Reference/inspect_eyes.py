import bpy, json, pathlib, numpy as np
from mathutils import Vector
ROOT=pathlib.Path('C:/Project/Pirate-Alliance')
OUT=ROOT/'Documentation/MoonLuna/Combat/Reference'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/Characters/MoonLuna/Models/MoonLuna.fbx'))
report=[]
for o in bpy.data.objects:
    if o.type=='MESH':
        vv=np.array([list(o.matrix_world@v.co) for v in o.data.vertices])
        report.append(dict(name=o.name,parent=o.parent.name if o.parent else None,location=list(o.location),scale=list(o.scale),matrix=[list(x) for x in o.matrix_world],bounds=[vv.min(0).tolist(),vv.max(0).tolist()],materials=[m.name for m in o.data.materials],verts=len(vv)))
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
for im in bpy.data.images:
    print('IMAGE',im.name,im.filepath)
print('OBJECTS',json.dumps(report))
