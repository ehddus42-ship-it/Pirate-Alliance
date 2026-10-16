import bpy, pathlib, numpy as np, json
from mathutils import Vector
OUT=pathlib.Path('C:/Project/Pirate-Alliance/Documentation/NamgungLuna/Combat/Reference')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath='C:/Project/Pirate-Alliance/Assets/Characters/NamgungLuna/Models/NamgungLuna.fbx')
for o in bpy.data.objects:
    if o.type=='MESH':o.hide_render=not(o.name.startswith('Hair') or o.name=='Face_Head')
for mat in bpy.data.materials:
    mat.use_nodes=True
    nt=mat.node_tree
    tex=next((n for n in nt.nodes if n.type=='TEX_IMAGE'),None)
    out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
    em=nt.nodes.new('ShaderNodeEmission')
    if tex:nt.links.new(tex.outputs['Color'],em.inputs['Color'])
    else:em.inputs['Color'].default_value=mat.diffuse_color
    nt.links.new(em.outputs[0],out.inputs['Surface'])
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('ReferenceWorld');scene.world.color=(.05,.05,.05)
scene.view_settings.view_transform='Standard'
bpy.ops.object.camera_add(location=(0,-2,1.55))
cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.55))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=.33;scene.camera=cam
scene.render.filepath=str(OUT/'face_front.png');bpy.ops.render.render(write_still=True)
o=bpy.data.objects['Face_Head'];uv=o.data.uv_layers.active.data
im=bpy.data.images['TEX_Face_Refit_0'];w,h=im.size
pix=np.array(im.pixels[:]).reshape(h,w,4)
gold=[]
for p in o.data.polygons:
    if p.material_index!=0:continue
    center=o.matrix_world@p.center
    if center.y>-.045 or not(1.48<center.z<1.59):continue
    tuv=sum((uv[i].uv for i in p.loop_indices),Vector((0,0)))/len(p.loop_indices)
    r,g,b,a=pix[min(h-1,int(tuv.y*h)),min(w-1,int(tuv.x*w))]
    if r>.27 and g>.15 and b<g*.68 and r>g*1.05:
        gold.append(dict(pos=list(center),rgb=[float(r),float(g),float(b)],area=p.area,poly=p.index,uv=list(tuv)))
(OUT/'gold_samples.json').write_text(json.dumps(gold,indent=2))
print('GOLD_SAMPLES',len(gold))
for sign in [-1,1]:
    ss=[p for p in gold if p['pos'][0]*sign>.005]
    if ss:
        arr=np.array([p['pos'] for p in ss]);weights=np.array([p['area'] for p in ss])
        print('EYE',sign,'center',np.average(arr,axis=0,weights=weights).tolist(),'bounds',arr.min(0).tolist(),arr.max(0).tolist())
