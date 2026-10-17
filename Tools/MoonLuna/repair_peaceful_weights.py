"""Preview-only weight repair candidates; original assets are never overwritten.

Generated candidates and renders go to Temp and Documentation for review.
"""
from pathlib import Path
import bpy, json
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Documentation/MoonLuna/DeformationQA'
CACHE=ROOT/'Temp/MoonLuna/QA'
OUT.mkdir(parents=True,exist_ok=True);CACHE.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Temp/MoonLuna/Meshy/peaceful_walk_retargeted.fbx'),use_image_search=False)
source_rig=next(obj for obj in bpy.data.objects if obj.type=='ARMATURE')
source_poses={}
for frame in (1,25,85):
    bpy.context.scene.frame_set(frame)
    source_poses[frame]={bone.name:source_rig.matrix_world@bone.matrix.copy() for bone in source_rig.pose.bones}

def smooth(a,b,value):
    t=max(0,min(1,(value-a)/(b-a)))
    return t*t*(3-2*t)

def mix(a,b,t):
    result={k:a.get(k,0)*(1-t)+b.get(k,0)*t for k in set(a)|set(b)}
    result={k:v for k,v in result.items() if v>1e-6}
    total=sum(result.values())
    return {k:v/total for k,v in result.items()}

reports={}
for variant in ('side_only','rigid_shin','rigid_foot','boot_blend'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Rig.fbx'),use_image_search=False)
    scene=bpy.context.scene
    rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    mesh=next(o for o in bpy.data.objects if o.type=='MESH')
    rig.animation_data_clear()
    original={v.index:{mesh.vertex_groups[g.group].name:g.weight for g in v.groups} for v in mesh.data.vertices}
    report={'modified_head':0,'modified_veil':0,'modified_skirt':0,'modified_boot':0}
    for vertex in mesh.data.vertices:
        x,y,z=mesh.matrix_world@vertex.co
        weights=original[vertex.index]
        # Long head-covering fabric sits behind the arms; arms must not pull it.
        veil=smooth(.015,.09,y)*smooth(.95,1.08,z)
        if veil>0:
            weights=mix(weights,{'Head':.88,'Spine':.12},veil)
            report['modified_veil']+=1
        head=smooth(1.29,1.39,z)
        if head>0:
            weights=mix(weights,{'Head':1},head)
            report['modified_head']+=1
        # Below the cuffs the garment is a single long skirt, not two trouser legs.
        skirt=smooth(.19,.28,z)*(1-smooth(.83,1.04,z))
        if skirt>0:
            left=smooth(-.10,.10,x)
            weights=mix(weights,{'Hips':.88,'LeftUpLeg':.12*left,'RightUpLeg':.12*(1-left)},skirt)
            report['modified_skirt']+=1
        boot=1-smooth(.19,.25,z)
        if boot>0:
            side='Left' if x>0 else 'Right'
            if variant=='side_only':
                desired={name:weight for name,weight in weights.items() if not name.startswith('Right' if x>0 else 'Left')}
                if not desired:desired={side+'Leg':1}
                total=sum(desired.values());desired={name:weight/total for name,weight in desired.items()}
            elif variant=='rigid_shin':desired={side+'Leg':1}
            elif variant=='rigid_foot':desired={side+'Foot':1}
            else:desired={side+'Leg':.75,side+'Foot':.25}
            weights=mix(weights,desired,boot)
            report['modified_boot']+=1
        for group_index in [group.group for group in vertex.groups]:mesh.vertex_groups[group_index].remove([vertex.index])
        for name,weight in weights.items():mesh.vertex_groups[name].add([vertex.index],weight,'REPLACE')
    # Save a separate model with the original skeleton/rest offsets for review.
    for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True)
    bpy.context.view_layer.objects.active=rig
    candidate=CACHE/f'LunaPeaceful_{variant}.fbx'
    bpy.ops.export_scene.fbx(filepath=str(candidate),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
        bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='AUTO',use_custom_props=False)
    image=bpy.data.images.load(str(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png'),check_existing=True)
    for material in bpy.data.materials:
        material.use_nodes=True;tree=material.node_tree;tree.nodes.clear()
        texture=tree.nodes.new('ShaderNodeTexImage');texture.image=image
        emission=tree.nodes.new('ShaderNodeEmission');output=tree.nodes.new('ShaderNodeOutputMaterial')
        tree.links.new(texture.outputs['Color'],emission.inputs['Color']);tree.links.new(emission.outputs['Emission'],output.inputs['Surface'])
    scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=720;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('QAWorld');scene.world.color=(.1,.12,.16)
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    cam_data=bpy.data.cameras.new('QACamera');cam_data.type='ORTHO';cam_data.ortho_scale=1.95
    cam=bpy.data.objects.new('QACamera',cam_data);scene.collection.objects.link(cam);scene.camera=cam
    for frame in (25,85):
        for bone in rig.pose.bones:
            bone.matrix=rig.matrix_world.inverted()@source_poses[frame][bone.name]
            bpy.context.view_layer.update()
        evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        points=[evaluated.matrix_world@v.co for v in evaluated.data.vertices]
        lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        center=(lo+hi)*.5;cam.location=center+Vector((-1.6,-3,.17))*1.65
        cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/f'fixed_{variant}_frame{frame:03}.png')
        bpy.ops.render.render(write_still=True)
    reports[variant]=report
(OUT/'weight_repair_candidates.json').write_text(json.dumps(reports,indent=2))
