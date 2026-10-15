"""Render original Meshy retargeted models for deformation QA; never alters assets."""
import bpy
from mathutils import Vector
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Documentation/NamgungLuna/DeformationQA"
OUT.mkdir(parents=True, exist_ok=True)
jobs = [("retarget_fbx", "Temp/NamgungLuna/Meshy/peaceful_walk_retargeted.fbx", [1, 25, 85]),
        ("retarget_glb", "Temp/NamgungLuna/Meshy/peaceful_walk_retargeted.glb", [85]),
        ("rig_fbx", "Assets/Characters/NamgungLuna/Peaceful/Models/LunaPeaceful_Rig.fbx", [1]),
        ("rig_world_pose", "Assets/Characters/NamgungLuna/Peaceful/Models/LunaPeaceful_Rig.fbx", [25,85])]
report={}
source_poses={}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Temp/NamgungLuna/Meshy/peaceful_walk_retargeted.fbx'),use_image_search=False)
source_rig=next(obj for obj in bpy.data.objects if obj.type=='ARMATURE')
for frame in (25,85):
    bpy.context.scene.frame_set(frame)
    source_poses[frame]={bone.name:source_rig.matrix_world @ bone.matrix.copy() for bone in source_rig.pose.bones}
for label, relative, frames in jobs:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene
    scene.render.fps=30
    if relative.endswith('.glb'):
        bpy.ops.import_scene.gltf(filepath=str(ROOT/relative))
    else:
        bpy.ops.import_scene.fbx(filepath=str(ROOT/relative),use_image_search=False)
    meshes=[obj for obj in bpy.data.objects if obj.type=='MESH' and obj.data.materials]
    for obj in bpy.data.objects:
        if obj.type=='MESH' and not obj.data.materials:
            obj.hide_render=True
    image=bpy.data.images.load(str(ROOT/'Assets/Characters/NamgungLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png'), check_existing=True)
    for material in bpy.data.materials:
        material.use_nodes=True
        tree=material.node_tree
        tree.nodes.clear()
        tex=tree.nodes.new('ShaderNodeTexImage');tex.image=image
        em=tree.nodes.new('ShaderNodeEmission')
        output=tree.nodes.new('ShaderNodeOutputMaterial')
        tree.links.new(tex.outputs['Color'],em.inputs['Color'])
        tree.links.new(em.outputs['Emission'],output.inputs['Surface'])
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=720;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.world=bpy.data.worlds.new('QAWorld')
    scene.world.color=(.10,.12,.16)
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    camera_data=bpy.data.cameras.new('Camera');camera_data.type='ORTHO';camera_data.ortho_scale=1.95
    camera=bpy.data.objects.new('Camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
    report[label]={'input':relative,'meshes':[(obj.name,len(obj.data.vertices)) for obj in meshes], 'frames':[]}
    for frame in frames:
        scene.frame_set(frame)
        if label == 'rig_world_pose':
            target_rig=next(obj for obj in bpy.data.objects if obj.type=='ARMATURE')
            target_rig.animation_data_clear()
            for bone in target_rig.pose.bones:
                bone.matrix=target_rig.matrix_world.inverted() @ source_poses[frame][bone.name]
                bpy.context.view_layer.update()
        depsgraph=bpy.context.evaluated_depsgraph_get()
        points=[]
        for obj in meshes:
            evaluated=obj.evaluated_get(depsgraph)
            points.extend(evaluated.matrix_world @ vert.co for vert in evaluated.data.vertices)
        lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        center=(lo+hi)*.5
        camera.location=center+Vector((-1.6,-3,.17))*1.65
        camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        path=OUT/f'{label}_frame{frame:03}.png';scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)
        report[label]['frames'].append({'frame':frame,'bounds_min':list(lo),'bounds_max':list(hi),'render':path.relative_to(ROOT).as_posix()})
(OUT/'deformation_qa.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
