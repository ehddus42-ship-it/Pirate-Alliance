import bpy, pathlib, json
from mathutils import Vector
OUT=pathlib.Path('C:/Project/Pirate-Alliance/Documentation/NamgungLuna/Combat/Reference')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath='C:/Project/Pirate-Alliance/Assets/Characters/NamgungLuna/Models/NamgungLuna.fbx')
face=bpy.data.objects['Face_Head']; inv=face.matrix_world.inverted()
# Pupil centers measured against the 1000 px, 0.33 m orthographic front reference.
# Camera right is +Blender X. Sample the actual front surface for depth and normal.
anchors=[]
for label,x,z in [('CharacterRight',-.0366,1.5633),('CharacterLeft',.0366,1.5633)]:
    hit,p,n,poly=face.ray_cast(inv@Vector((x,-1,z)),inv.to_3x3()@Vector((0,1,0)))
    if not hit:raise RuntimeError(label+' pupil ray missed')
    p=face.matrix_world@p;n=(face.matrix_world.to_3x3()@n).normalized()
    u=Vector((-p.x,p.z,-p.y));un=Vector((-n.x,n.z,-n.y))
    emission=u+Vector((0,0,.003))
    raw=Vector((-p.x,p.y,p.z))*.01
    anchors.append(dict(name=label,blenderWorld=list(p),visualLocalSurface=list(u),visualLocalEmitter=list(emission),headLocalSurface=list(raw),visualNormal=list(un),facePolygon=poly))
report={'source':'Assets/Characters/NamgungLuna/Models/NamgungLuna.fbx','prefab':'Assets/Characters/NamgungLuna/Stage/NamgungLuna_Battle.prefab','parentRecommended':'Visual','modelPath':'Visual/NamgungLuna_Toon/Head/Face_Head','mappingBlenderMetersToVisual':'(-x,z,-y)','toonLocalScale':100,'toonLocalRotationEuler':[-90,0,0],'anchorMeasurement':'Pupil center in original textured front render; front-surface ray intersection. Approx. 1 mm lateral precision. Emitter moved +3 mm Visual Z to avoid surface occlusion.','anchors':anchors}
(OUT/'eye_anchors.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
# Repeat the actual texture render with tiny cyan locator marks at both pupils.
for o in bpy.data.objects:
    if o.type=='MESH':o.hide_render=not(o.name.startswith('Hair') or o.name=='Face_Head')
for mat in bpy.data.materials:
    mat.use_nodes=True; nt=mat.node_tree
    tex=next((n for n in nt.nodes if n.type=='TEX_IMAGE'),None)
    out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
    em=nt.nodes.new('ShaderNodeEmission')
    if tex:nt.links.new(tex.outputs['Color'],em.inputs['Color'])
    else:em.inputs['Color'].default_value=mat.diffuse_color
    nt.links.new(em.outputs[0],out.inputs['Surface'])
mark=bpy.data.materials.new('Reference_Cyan_Marker');mark.use_nodes=True
bs=mark.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(0,1,1,1);bs.inputs['Emission Color'].default_value=(0,1,1,1);bs.inputs['Emission Strength'].default_value=1
for a in anchors:
    p=Vector(a['blenderWorld']);p.y-=.003
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=.0015,location=p)
    bpy.context.object.data.materials.append(mark)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
scene.world=bpy.data.worlds.new('ReferenceWorld');scene.world.color=(.05,.05,.05)
bpy.ops.object.camera_add(location=(0,-2,1.55));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.55))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=.33;scene.camera=cam
scene.render.filepath=str(OUT/'eye_anchors_front.png');bpy.ops.render.render(write_still=True)
cam.location=(.8,-1.4,1.55);cam.rotation_euler=(Vector((0,-.04,1.55))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/'eye_anchors_threequarter.png');bpy.ops.render.render(write_still=True)
