"""Repair the peaceful character's automatic cloth/head skin and add stable boot controls.

Blender script. Raw Meshy model, motion, and previous loop assets remain untouched.
Default writes review candidates to Temp. Pass -- --publish after visual review.
"""
from pathlib import Path
import sys,json,math
import bpy,bmesh
import numpy as np
from mathutils import Vector,Matrix,Quaternion

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Documentation/MoonLuna/DeformationQA'
CACHE=ROOT/'Temp/MoonLuna/QA'
publish='--publish' in sys.argv
OUT.mkdir(parents=True,exist_ok=True);CACHE.mkdir(parents=True,exist_ok=True)
STRIDE_SCALE=1.48
RETARGET_SIZE_RATIO=0.758284
BOOT_ORIGINS={'Left':Vector((.028,-.020,.145)),'Right':Vector((-.060,-.020,.145))}

def reset_import(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps=30
    bpy.ops.import_scene.fbx(filepath=str(ROOT/path),use_image_search=False)
    bpy.context.scene.render.fps=30;bpy.context.scene.render.fps_base=1
    return next(obj for obj in bpy.data.objects if obj.type=='ARMATURE')

def capture(path):
    rig=reset_import(path);action=rig.animation_data.action
    frames=[]
    for frame in range(round(action.frame_range[0]),round(action.frame_range[1])+1):
        bpy.context.scene.frame_set(frame)
        frames.append({bone.name:rig.matrix_world@bone.matrix.copy() for bone in rig.pose.bones})
    return frames

walk_poses=capture('Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulWalk_Loop.fbx')
idle_poses=capture('Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulIdle.fbx')
trajectory_path=ROOT/'Documentation/MoonLuna/peaceful_walk_foot_trajectory.json'
if trajectory_path.exists():
    stored=json.loads(trajectory_path.read_text(encoding='utf-8'))
    source_feet=[{name:Vector(value) for name,value in frame.items()} for frame in stored['samples']]
else:
    raw=reset_import('Temp/MoonLuna/Meshy/peaceful_walk_raw.fbx')
    source_feet=[]
    for frame in range(1,52):
        bpy.context.scene.frame_set(frame)
        points={name:raw.matrix_world@raw.pose.bones[name].matrix.translation for name in ('Pelvis','L_Ankle','L_Foot','R_Ankle','R_Foot')}
        source_feet.append(points)
    trajectory_path.write_text(json.dumps({'source_meshy_task_id':'01a13d56-c56f-7226-b83e-b3a056f86fb5','source_provenance':'Documentation/MoonLuna/peaceful_walk_meshy.json','fps':30,'source_frames':[1,51],'samples':[{name:list(value) for name,value in frame.items()} for frame in source_feet]},indent=2),encoding='utf-8')
min_ankle={side:min(frame[letter+'_Ankle'].z for frame in source_feet) for side,letter in (('Left','L'),('Right','R'))}
mean_foreaft=sum((frame[letter+'_Ankle']-frame['Pelvis']).y for frame in source_feet for letter in ('L','R'))/(2*len(source_feet))

rig=reset_import('Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Rig.fbx')
mesh=next(obj for obj in bpy.data.objects if obj.type=='MESH')
scene=bpy.context.scene;rig.animation_data_clear()
rest_hips=rig.matrix_world@rig.data.bones['Hips'].head_local
body_names=[bone.name for bone in rig.data.bones]
# Boot-only controls avoid the auto rig's ankle pivots, which lie behind the actual shoes.
bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for side,origin in BOOT_ORIGINS.items():
    bone=rig.data.edit_bones.new('Luna'+side+'Boot')
    bone.head=rig.matrix_world.inverted()@origin
    bone.tail=rig.matrix_world.inverted()@(origin+Vector((0,.08,0)))
    bone.parent=rig.data.edit_bones['Hips'];bone.use_connect=False
bpy.ops.object.mode_set(mode='OBJECT')
for side in BOOT_ORIGINS:mesh.vertex_groups.new(name='Luna'+side+'Boot')

def smooth(a,b,value):
    t=max(0,min(1,(value-a)/(b-a)))
    return t*t*(3-2*t)
def mix(a,b,t):
    result={k:a.get(k,0)*(1-t)+b.get(k,0)*t for k in set(a)|set(b)}
    result={k:v for k,v in result.items() if v>1e-6}
    total=sum(result.values())
    return {k:v/total for k,v in result.items()}

image=bpy.data.images.load(str(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png'),check_existing=True)
pixels=np.array(image.pixels[:],dtype=np.float32).reshape(image.size[1],image.size[0],4)
brightness=np.zeros(len(mesh.data.vertices));uv_count=np.zeros(len(mesh.data.vertices))
for loop in mesh.data.loops:
    uv=mesh.data.uv_layers.active.data[loop.index].uv
    pixel=pixels[min(image.size[1]-1,max(0,int(uv.y*image.size[1]))),min(image.size[0]-1,max(0,int(uv.x*image.size[0])))]
    brightness[loop.vertex_index]+=float(np.mean(pixel[:3]));uv_count[loop.vertex_index]+=1
brightness/=np.maximum(1,uv_count)
counts={'head':0,'veil':0,'skirt':0,'boots':0}
for vertex in mesh.data.vertices:
    x,y,z=mesh.matrix_world@vertex.co
    weights={mesh.vertex_groups[group.group].name:group.weight for group in vertex.groups}
    veil=smooth(.07,.16,y)*smooth(1.00,1.13,z)
    if veil>0:weights=mix(weights,{'Head':.90,'Spine':.10},veil);counts['veil']+=1
    head=smooth(1.30,1.39,z)
    if head>0:weights=mix(weights,{'Head':1},head);counts['head']+=1
    boot_geometry=(1-smooth(.18,.25,z))*(1-smooth(.12,.17,abs(x+.0178)))*(1-smooth(.035,.10,y))*smooth(-.25,-.20,y)
    boot_color=1 if z<.17 else 1-smooth(.30,.55,brightness[vertex.index])
    boot=boot_geometry*boot_color
    skirt=1-smooth(.80,1.03,z)
    if skirt>0:
        left=smooth(-.10,.10,x)
        weights=mix(weights,{'Hips':.96,'LeftUpLeg':.04*left,'RightUpLeg':.04*(1-left)},skirt)
        counts['skirt']+=1
    if boot>0:
        side='Left' if x>-.0178 else 'Right'
        weights=mix(weights,{'Luna'+side+'Boot':1},boot);counts['boots']+=1
    weights=dict(sorted(weights.items(),key=lambda pair:pair[1],reverse=True)[:4])
    total=sum(weights.values());weights={name:value/total for name,value in weights.items()}
    for index in [group.group for group in vertex.groups]:mesh.vertex_groups[index].remove([vertex.index])
    for name,weight in weights.items():mesh.vertex_groups[name].add([vertex.index],weight,'REPLACE')
# The image-to-mesh reconstruction joined the touching soles. Separate their
# interior bridge faces so independently moving boots cannot stretch that seam.
bm=bmesh.new();bm.from_mesh(mesh.data)
bridge_faces=[]
for face in bm.faces:
    points=[mesh.matrix_world@vertex.co for vertex in face.verts]
    if max(p.z for p in points)<.19 and min(p.x for p in points)<-.021 and max(p.x for p in points)>-.014 and min(p.y for p in points)>-.23 and max(p.y for p in points)<.09:
        bridge_faces.append(face)
removed_bridges=len(bridge_faces)
if bridge_faces:bmesh.ops.delete(bm,geom=bridge_faces,context='FACES_ONLY')
bm.to_mesh(mesh.data);bm.free();mesh.data.update()

model_path=(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Game.fbx') if publish else CACHE/'LunaPeaceful_Game.fbx'
walk_path=(ROOT/'Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulWalk_Game.fbx') if publish else CACHE/'LunaPeacefulWalk_Game.fbx'
idle_path=(ROOT/'Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulIdle_Game.fbx') if publish else CACHE/'LunaPeacefulIdle_Game.fbx'
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(model_path),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
    bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='AUTO',use_custom_props=False)

def helper_world(side,index,hips,idle=False):
    origin=BOOT_ORIGINS[side]
    p=origin.copy();p.x+=hips.translation.x-rest_hips.x;p.y+=hips.translation.y-rest_hips.y
    roll=0.0
    if not idle:
        letter='L' if side=='Left' else 'R';frame=source_feet[index]
        ankle=frame[letter+'_Ankle'];toe=frame[letter+'_Foot']
        p.y+=((ankle-frame['Pelvis']).y-mean_foreaft)*RETARGET_SIZE_RATIO*STRIDE_SCALE
        p.z+=max(0,ankle.z-min_ankle[side])*.38
        delta=toe-ankle
        roll=max(math.radians(-10),min(math.radians(12),math.atan2(-delta.z,max(.001,-delta.y))-math.radians(27)))
    return Matrix.LocRotScale(p,Quaternion((1,0,0),roll),rig.matrix_world.to_scale())

def bake(poses,name,path,idle=False):
    for bone in rig.pose.bones:bone.rotation_mode='QUATERNION'
    rig.animation_data_create();rig.animation_data.action=bpy.data.actions.new(name)
    helpers={side:[helper_world(side,index,pose['Hips'],idle) for index,pose in enumerate(poses)] for side in BOOT_ORIGINS}
    # Close boot trajectories with the same smooth endpoint correction as the body loop.
    if not idle:
        for side,values in helpers.items():
            first=values[0];last=values[-1]
            for index,value in enumerate(values):
                t=index/(len(values)-1);s=t*t*(3-2*t)
                position,rotation,scale=value.decompose();first_pos,first_rot,_=first.decompose();last_pos,last_rot,_=last.decompose()
                position+=(first_pos-last_pos)*s
                rotation=rotation@Quaternion().slerp(last_rot.rotation_difference(first_rot),s)
                values[index]=Matrix.LocRotScale(position,rotation,scale)
    for index,pose in enumerate(poses):
        scene.frame_set(index+1)
        for name in body_names:
            bone=rig.pose.bones[name];bone.matrix=rig.matrix_world.inverted()@pose[name];bpy.context.view_layer.update()
        for side in BOOT_ORIGINS:
            rig.pose.bones['Luna'+side+'Boot'].matrix=rig.matrix_world.inverted()@helpers[side][index]
            bpy.context.view_layer.update()
        for bone in rig.pose.bones:
            bone.rotation_mode='QUATERNION'
            bone.keyframe_insert('location',frame=index+1);bone.keyframe_insert('rotation_quaternion',frame=index+1);bone.keyframe_insert('scale',frame=index+1)
    scene.frame_start=1;scene.frame_end=len(poses);scene.frame_set(1)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,
        bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
        axis_forward='-Z',axis_up='Y',path_mode='AUTO',use_custom_props=False)
    return rig.animation_data.action

walk_action=bake(walk_poses,'LunaPeacefulWalk_Game',walk_path)
idle_action=bake(idle_poses,'LunaPeacefulIdle_Game',idle_path,True)
# Neutral texture previews; emission changes exist only in this Blender process.
for material in bpy.data.materials:
    material.use_nodes=True;tree=material.node_tree;tree.nodes.clear()
    texture=tree.nodes.new('ShaderNodeTexImage');texture.image=image
    emission=tree.nodes.new('ShaderNodeEmission');output=tree.nodes.new('ShaderNodeOutputMaterial')
    tree.links.new(texture.outputs['Color'],emission.inputs['Color']);tree.links.new(emission.outputs['Emission'],output.inputs['Surface'])
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=720;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('QAWorld');scene.world.color=(.10,.12,.16)
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
camera_data=bpy.data.cameras.new('QA Camera');camera_data.type='ORTHO';camera_data.ortho_scale=1.95
camera=bpy.data.objects.new('QA Camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
for label,action,frames in (('game_walk',walk_action,(1,13,25,38)),('game_idle',idle_action,(1,))):
    rig.animation_data.action=action
    for frame in frames:
        scene.frame_set(frame)
        evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        points=[evaluated.matrix_world@vertex.co for vertex in evaluated.data.vertices]
        lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        center=(lo+hi)*.5;camera.location=center+Vector((-1.6,-3,.17))*1.65
        camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/f'{label}_frame{frame:03}.png');bpy.ops.render.render(write_still=True)
report={'character_name':'문루나','weights_modified':counts,'removed_touching_sole_bridge_faces':removed_bridges,'helper_bones':['LunaLeftBoot','LunaRightBoot'],
    'reason':'Auto skin mixed opposite feet and arm weights into the face/veil; original ankle pivots were behind the actual boots.',
    'method':'Preserved original skeleton and body gait. Reweighted head/veil and long skirt. Added two boot-only controls using measured original Meshy ankle trajectories; boots remain rigid and do not share opposite-leg weights.',
    'stride_multiplier':STRIDE_SCALE,'source_model_speed_mps':.2923365021,'playback_rate':1.5,'recommended_speed_mps':.2923365021*STRIDE_SCALE*1.5,
    'walk_duration_seconds':(len(walk_poses)-1)/30,
    'reproduction_inputs':['Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Rig.fbx','Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png','Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulWalk_Loop.fbx','Assets/Characters/MoonLuna/Peaceful/Animations/LunaPeacefulIdle.fbx','Documentation/MoonLuna/peaceful_walk_foot_trajectory.json'],
    'maximum_skin_influences':4,'outputs':[path.relative_to(ROOT).as_posix() for path in (model_path,walk_path,idle_path)],'published':publish}
(OUT/'game_rig_repair.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
