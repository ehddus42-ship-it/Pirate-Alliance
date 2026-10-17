"""Preserve the source outfit/hands; repair arm skin and bake a modest folded-hands pose.

Run with a separate Blender background process. Default outputs are review candidates only.
Inputs are immutable pre-prayer sources committed under Tools/MoonLuna/Source.
"""
from pathlib import Path
import sys,json,math,shutil,hashlib
import bpy,bmesh,numpy as np
from mathutils import Vector,Matrix,Quaternion

ROOT=Path(__file__).resolve().parents[2]
CACHE=ROOT/'Temp/MoonLuna/Prayer';OUT=ROOT/'Documentation/MoonLuna/PeacefulPrayerQA'
INPUT=ROOT/'Tools/MoonLuna/Source/PeacefulBeforePrayer'
for p in (CACHE,OUT):p.mkdir(parents=True,exist_ok=True)
sources={'model':INPUT/'LunaPeaceful_Game.fbx','idle':INPUT/'LunaPeacefulIdle_Game.fbx','walk':INPUT/'LunaPeacefulWalk_Game.fbx'}
for source in sources.values():
 if not source.exists():raise FileNotFoundError('The preserved pre-prayer source is required: '+str(source))

def load(path):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
 bpy.context.scene.render.fps=30;bpy.context.scene.render.fps_base=1
 return next(o for o in bpy.data.objects if o.type=='ARMATURE')

def capture(path):
 rig=load(path);action=rig.animation_data.action;frames=[]
 for f in range(round(action.frame_range[0]),round(action.frame_range[1])+1):
  bpy.context.scene.frame_set(f)
  frames.append({b.name:rig.matrix_world@b.matrix.copy() for b in rig.pose.bones})
 return frames

poses={'idle':capture(sources['idle']),'walk':capture(sources['walk'])}
rig=load(sources['model']);scene=bpy.context.scene
mesh=next(o for o in bpy.data.objects if o.type=='MESH')
rig.animation_data_clear();world=rig.matrix_world.copy();inverse=world.inverted();scale=world.to_scale()
rest={b.name:world@b.matrix_local for b in rig.data.bones}
names=list(rest)
original_vertex_count=len(mesh.data.vertices)
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for side in ('Left','Right'):
 for part in ('Arm','ForeArm','Hand'):
  source=rig.data.edit_bones[side+part];head=source.head.copy();tail=source.tail.copy();roll=source.roll
  bone=rig.data.edit_bones.new('Luna'+side+'Prayer'+part)
  bone.head=head;bone.tail=tail;bone.roll=roll;bone.parent=rig.data.edit_bones['Spine'];bone.use_connect=False
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()
for side in ('Left','Right'):
 for part in ('Arm','ForeArm','Hand'):rest['Luna'+side+'Prayer'+part]=world@rig.data.bones['Luna'+side+'Prayer'+part].matrix_local

def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def replace_weights(vertex,weights):
 for g in list(vertex.groups):mesh.vertex_groups[g.group].remove([vertex.index])
 total=sum(weights.values())
 for name,value in weights.items():
  if value>1e-5:mesh.vertex_groups[name].add([vertex.index],value/total,'REPLACE')

added=[]
image=bpy.data.images.load(str(ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png'))
pixels=np.asarray(image.pixels[:],dtype=np.float32).reshape(image.size[1],image.size[0],4)
brightness=np.zeros(len(mesh.data.vertices));uv_count=np.zeros(len(mesh.data.vertices))
for loop in mesh.data.loops:
 uv=mesh.data.uv_layers.active.data[loop.index].uv
 color=pixels[min(image.size[1]-1,max(0,int(uv.y*image.size[1]))),min(image.size[0]-1,max(0,int(uv.x*image.size[0])))][:3]
 brightness[loop.vertex_index]+=float(np.mean(color));uv_count[loop.vertex_index]+=1
brightness/=np.maximum(1,uv_count);boot_counts={'Left':0,'Right':0}
# The generated surface joins both shoes and the gown. Weighting this shared
# surface by vertex side stretches tiny bridge triangles across a whole stride.
# Extract complete shoe faces first, then cut and close each shoe independently.
boot_faces=set();discarded_hem_fragments=set()
for face in mesh.data.polygons:
 points=[mesh.matrix_world@mesh.data.vertices[i].co for i in face.vertices]
 x,y,z=sum(points,Vector())/len(points)
 shade=float(np.mean([brightness[i] for i in face.vertices]))
 if abs(x+.0178)<.125 and -.24<y<.06 and z<.25 and (z<.18 or shade<.55):
  boot_faces.add(face.index)
  if shade>.75:discarded_hem_fragments.add(face.index)
hand_faces={'Left':set(),'Right':set()}
for face in mesh.data.polygons:
 points=[mesh.matrix_world@mesh.data.vertices[i].co for i in face.vertices]
 center=sum(points,Vector())/len(points);x,y,z=center
 if not (.775<z<.98 and -.255<y<-.085 and .22<abs(x+.0178)<.33):continue
 colors=[]
 for li in face.loop_indices:
  uv=mesh.data.uv_layers.active.data[li].uv
  colors.append(pixels[min(image.size[1]-1,max(0,int(uv.y*image.size[1]))),min(image.size[0]-1,max(0,int(uv.x*image.size[0])))][:3])
 color=np.mean(colors,axis=0)
 if color[0]>.50 and color[0]-color[1]>.022 and color[0]-color[2]>.015:
  hand_faces['Left' if x>0 else 'Right'].add(face.index)
arm_counts={}
for side,selected in hand_faces.items():
 obj=mesh.copy();obj.data=mesh.data.copy();obj.name='Luna'+side+'OriginalHand';scene.collection.objects.link(obj)
 bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index not in selected],context='FACES')
 bm.to_mesh(obj.data);bm.free();obj.data.update()
 # Keep the original finger surface, but bring its spread into a relaxed, compact hand.
 pivot=rest[side+'Hand'].translation;axis=(rest[side+'Hand'].to_3x3()@Vector((0,1,0))).normalized()
 for vertex in obj.data.vertices:
  delta=obj.matrix_world@vertex.co-pivot;length=delta.dot(axis);along=axis*length
  bend=max(0,length-.085);angle=min(1.25,bend*12)
  curved=.085+math.sin(angle)/12 if bend>0 else length
  normal=Vector((0,-1,0));normal=(normal-axis*normal.dot(axis)).normalized()
  curl=normal*((1-math.cos(angle))/12) if bend>0 else Vector()
  vertex.co=obj.matrix_world.inverted()@(pivot+axis*curved*.78+(delta-along)*.55+curl*.75)
 obj.vertex_groups.clear();group=obj.vertex_groups.new(name='Luna'+side+'PrayerHand');group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
 arm_counts[side]=len(obj.data.vertices);added.append(obj)

boot_cap=bpy.data.materials.new('LunaPeaceful_BootLining');boot_cap.diffuse_color=(.012,.014,.028,1);boot_cap.use_nodes=True
boot_cap.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=boot_cap.diffuse_color
for side in ('Left','Right'):
 obj=mesh.copy();obj.data=mesh.data.copy();obj.name='Luna'+side+'SeparatedBoot';scene.collection.objects.link(obj)
 obj.data.materials.append(boot_cap);cap_index=len(obj.data.materials)-1
 bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index not in boot_faces or f.index in discarded_hem_fragments],context='FACES')
 # Welding coincident UV-seam vertices makes the top and centre cuts closable.
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-5/max(obj.matrix_world.to_scale()))
 plane_co=obj.matrix_world.inverted()@Vector((-.0178,0,0))
 plane_no=(obj.matrix_world.to_3x3().transposed()@Vector((1,0,0))).normalized()
 bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-6,
  plane_co=plane_co,plane_no=plane_no,clear_inner=side=='Left',clear_outer=side=='Right')
 boundary=[e for e in bm.edges if e.is_boundary]
 if boundary:
  caps=bmesh.ops.holes_fill(bm,edges=boundary,sides=0)['faces']
  for face in caps:face.material_index=cap_index
 # Unity's FBX importer rejects some non-convex cut caps. Export explicit
 # triangles so both Blender and Unity use the same stable cap tessellation.
 bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='BEAUTY',ngon_method='EAR_CLIP')
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
 bm.to_mesh(obj.data);bm.free();obj.data.update()
 obj.vertex_groups.clear();group=obj.vertex_groups.new(name='Luna'+side+'Boot')
 group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
 boot_counts[side]=len(obj.data.vertices);added.append(obj)

# The original arms are fused to the gown. Remove their stretched bridge surface,
# keep the original textured hands, and replace sleeves with closed, independently skinned cloth.
bm=bmesh.new();bm.from_mesh(mesh.data);bm.faces.ensure_lookup_table()
remove=[]
for face in bm.faces:
 points=[mesh.matrix_world@v.co for v in face.verts];c=sum(points,Vector())/len(points);x,y,z=c;sx=abs(x+.0178)
 boundary=.22+(.14-.22)*max(0,min(1,(z-.72)/.28)) if z<1.0 else .14+(.12-.14)*max(0,min(1,(z-1)/.16))
 if face.index in boot_faces or (.72<z<1.16 and -.29<y<(.08 if z<1.04 else .055) and boundary<sx<.365):remove.append(face)
bmesh.ops.delete(bm,geom=remove,context='FACES');removed_arm_faces=len(remove)-len(boot_faces)
bm.to_mesh(mesh.data);bm.free();mesh.data.update()
# No part of the gown may retain a shoe helper influence after separation.
for vertex in mesh.data.vertices:
 weights={mesh.vertex_groups[g.group].name:g.weight for g in vertex.groups}
 removed=weights.pop('LunaLeftBoot',0)+weights.pop('LunaRightBoot',0)
 if removed:
  weights['Hips']=weights.get('Hips',0)+removed;replace_weights(vertex,weights)

def cloth(name,color):
 mat=bpy.data.materials.new(name);mat.diffuse_color=(*color,1);mat.use_nodes=True
 p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=mat.diffuse_color;p.inputs['Roughness'].default_value=.9
 return mat
dark=cloth('LunaPeaceful_SleeveDark',(.012,.014,.028));white=cloth('LunaPeaceful_SleeveWhite',(.87,.88,.94))
def sleeve(side,part,a,b,profile):
 a=Vector(a);b=Vector(b);axis=(b-a).normalized();u=axis.cross(Vector((0,1,0))).normalized();v=axis.cross(u).normalized()
 verts=[];faces=[];sides=24
 for i,(t,radius,material) in enumerate(profile):
  center=a.lerp(b,t)
  for j in range(sides):
   angle=math.tau*j/sides;ruffle=1+(.035*math.sin(6*angle) if part=='ForeArm' and t>.8 else 0)
   verts.append(center+(u*math.cos(angle)+v*math.sin(angle))*radius*ruffle)
 for i in range(len(profile)-1):
  for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
 faces.append(tuple(reversed(range(sides))))
 if part=='ForeArm':
  outer=(len(profile)-1)*sides;inner=len(verts);center=a.lerp(b,profile[-1][0])
  for j in range(sides):
   angle=math.tau*j/sides;verts.append(center+(u*math.cos(angle)+v*math.sin(angle))*profile[-1][1]*.78)
  for j in range(sides):faces.append((outer+j,outer+(j+1)%sides,inner+(j+1)%sides,inner+j))
 else:faces.append(tuple((len(profile)-1)*sides+j for j in range(sides)))
 data=bpy.data.meshes.new('Prayer'+side+part);data.from_pydata(verts,[],faces);data.materials.append(dark);data.materials.append(white)
 obj=bpy.data.objects.new('Luna'+side+part+'Sleeve',data);scene.collection.objects.link(obj)
 group=obj.vertex_groups.new(name='Luna'+side+'Prayer'+part);group.add(list(range(len(verts))),1,'REPLACE')
 for polygon in data.polygons:
  polygon.use_smooth=True;polygon.material_index=profile[min(len(profile)-1,polygon.index//sides)][2]
 mod=obj.modifiers.new('LunaArmature','ARMATURE');mod.object=rig;obj.parent=rig;obj.matrix_world=Matrix.Identity(4);added.append(obj)
for side in ('Left','Right'):
 shoulder=rest[side+'Arm'].translation;elbow=rest[side+'ForeArm'].translation;wrist=rest[side+'Hand'].translation
 sleeve(side,'Arm',shoulder,elbow,[(-.18,.025,0),(-.05,.055,0),(.25,.067,0),(.60,.062,0),(.95,.046,0),(1.1,.028,0)])
 sleeve(side,'ForeArm',elbow,wrist,[(-.12,.026,0),(0,.045,0),(.3,.040,0),(.65,.030,0),(.78,.029,1),(.85,.035,1),(1.07,.036,1)])

# The image-to-mesh body has no separate cloth under its fused sleeves. A closed
# inner dress lining fills that hidden area without changing the visible textured front.
verts=[];faces=[];profile=[(.68,.23,.12),(.78,.20,.11),(.96,.13,.10),(1.12,.13,.095),(1.28,.12,.085)];sides=32
for z,width,depth in profile:
 for j in range(sides):
  angle=j*math.tau/sides;verts.append((-.0178+width*math.cos(angle),-.07+depth*math.sin(angle),z))
for i in range(len(profile)-1):
 for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
faces.append(tuple(reversed(range(sides))));faces.append(tuple((len(profile)-1)*sides+j for j in range(sides)))
data=bpy.data.meshes.new('PrayerDressLining');data.from_pydata(verts,[],faces);data.materials.append(dark)
lining=bpy.data.objects.new('LunaPrayerDressLining',data);scene.collection.objects.link(lining)
for name in ('Hips','Spine01'):lining.vertex_groups.new(name=name)
for i,(z,_,__) in enumerate(profile):
 weight=smooth(1.00,1.20,z)
 for name,w in (('Hips',1-weight),('Spine01',weight)):
  if w>0:lining.vertex_groups[name].add(list(range(i*sides,(i+1)*sides)),w,'REPLACE')
for polygon in data.polygons:polygon.use_smooth=True
modifier=lining.modifiers.new('LunaArmature','ARMATURE');modifier.object=rig;lining.parent=rig;lining.matrix_world=Matrix.Identity(4);added.append(lining)

# Add complete stocking legs inside the long skirt, ending inside the existing boot shafts.
leg_rest={}
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for side,x in (('Left',.035),('Right',-.065)):
 top=Vector((x,-.045,.94));knee=Vector((x,-.095,.57));ankle=Vector((x,-.020,.205))
 leg_rest[side]=(top,knee,ankle)
 for part,a,b in (('Thigh',top,knee),('Shin',knee,ankle)):
  bone=rig.data.edit_bones.new('Luna'+side+part);bone.head=inverse@a;bone.tail=inverse@b
  bone.parent=rig.data.edit_bones['Hips'];bone.use_connect=False
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()
for b in rig.data.bones:
 if b.name not in rest:rest[b.name]=world@b.matrix_local
stockings=bpy.data.materials.new('LunaPeaceful_Stockings');stockings.diffuse_color=(.028,.025,.055,1);stockings.use_nodes=True
stockings.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=stockings.diffuse_color
stockings.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.88
for side,(top,knee,ankle) in leg_rest.items():
 verts=[];faces=[];rings=11;sides=16
 for i in range(rings):
  t=i/(rings-1)
  center=top.lerp(knee,t*2) if t<=.5 else knee.lerp(ankle,(t-.5)*2)
  radius=.047*(1-t)+.024*t
  for j in range(sides):
   angle=j*math.tau/sides;verts.append(center+Vector((math.cos(angle)*radius,math.sin(angle)*radius,0)))
 for i in range(rings-1):
  for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
 faces.append(tuple(reversed(range(sides))));faces.append(tuple((rings-1)*sides+j for j in range(sides)))
 data=bpy.data.meshes.new('Luna'+side+'StockingMesh');data.from_pydata(verts,[],faces);data.materials.append(stockings)
 obj=bpy.data.objects.new('Luna'+side+'Stocking',data);scene.collection.objects.link(obj)
 for part in ('Thigh','Shin'):obj.vertex_groups.new(name='Luna'+side+part)
 for i in range(rings):
  t=i/(rings-1);w=smooth(.38,.62,t)
  for part,value in (('Thigh',1-w),('Shin',w)):
   if value>0:obj.vertex_groups['Luna'+side+part].add(list(range(i*sides,(i+1)*sides)),value,'REPLACE')
 for polygon in data.polygons:polygon.use_smooth=True
 mod=obj.modifiers.new('LunaArmature','ARMATURE');mod.object=rig;obj.parent=rig;obj.matrix_world=Matrix.Identity(4)
 added.append(obj)

def orient(name,head,tail,palm=None):
 old=rest[name];old_dir=(old.to_3x3()@Vector((0,1,0))).normalized();direction=(tail-head).normalized()
 q=old_dir.rotation_difference(direction)
 if palm is not None:
  normal=q@Vector((0,-1,0));normal=(normal-direction*normal.dot(direction)).normalized()
  target=(palm-direction*palm.dot(direction)).normalized()
  q=Quaternion(direction,math.atan2(direction.dot(normal.cross(target)),normal.dot(target)))@q
 return Matrix.LocRotScale(head,q@old.to_quaternion(),scale)

def elbow_between(shoulder,wrist,l1,l2,bend):
 delta=wrist-shoulder;d=max(.001,min(delta.length,l1+l2-.0001));unit=delta.normalized()
 a=(l1*l1-l2*l2+d*d)/(2*d);height=math.sqrt(max(0,l1*l1-a*a))
 bend=(bend-unit*bend.dot(unit)).normalized();return shoulder+unit*a+bend*height

def pose_prayer(pose,index,count,kind):
 for name in names:rig.pose.bones[name].matrix=inverse@pose[name];bpy.context.view_layer.update()
 chest=pose['Spine']@rest['Spine'].inverted()
 for side,sign in (('Left',1),('Right',-1)):
  shoulder=(world@rig.pose.bones[side+'Arm'].matrix).translation
  wrist=chest@Vector((.060 if side=='Left' else .035,-.255 if side=='Left' else -.279,1.035 if side=='Left' else 1.055))
  l1=(rest[side+'ForeArm'].translation-rest[side+'Arm'].translation).length
  l2=(rest[side+'Hand'].translation-rest[side+'ForeArm'].translation).length
  elbow=elbow_between(shoulder,wrist,l1,l2,Vector((sign*.7,0,-1)))
  fingertip=wrist+chest.to_quaternion()@Vector((-.11,-.005,-.10))
  for name,a,b,palm in ((side+'Arm',shoulder,elbow,None),(side+'ForeArm',elbow,wrist,None),(side+'Hand',wrist,fingertip,Vector((0,-1,0)))):
   helper='Luna'+side+'Prayer'+name[len(side):]
   rig.pose.bones[helper].matrix=inverse@orient(helper,a,b,palm);bpy.context.view_layer.update()
 for side,(top,knee,ankle) in leg_rest.items():
  hips_delta=pose['Hips']@rest['Hips'].inverted();top=hips_delta@top
  boot=pose['Luna'+side+'Boot'];boot_delta=boot@rest['Luna'+side+'Boot'].inverted();ankle=boot_delta@ankle
  knee=elbow_between(top,ankle,.374,.368,Vector((0,-1,0)))
  for part,a,b in (('Thigh',top,knee),('Shin',knee,ankle)):
   rig.pose.bones['Luna'+side+part].matrix=inverse@orient('Luna'+side+part,a,b);bpy.context.view_layer.update()

def export(path,model=False):
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 if model:
  for obj in [mesh]+added:obj.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE','MESH'} if model else {'ARMATURE'},add_leaf_bones=False,
  bake_anim=not model,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
  axis_forward='-Z',axis_up='Y',path_mode='AUTO',use_custom_props=False)

for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();export(CACHE/'LunaPeaceful_Game.fbx',True)
if '--model-only' in sys.argv:
 print('Model-only export complete; clips and preview renders preserved.')
 sys.exit(0)
actions={};report={'original_hand_vertices_preserved':arm_counts,'boot_vertices_bound_rigidly':boot_counts,'separated_shoe_source_faces':len(boot_faces),'boot_repair':'Independent capped left/right shoe meshes; all gown boot influences removed.','removed_fused_arm_faces':removed_arm_faces,'source_vertices':original_vertex_count,'materials':{m.name:list(m.diffuse_color) for m in (stockings,dark,white,boot_cap)},'original_hands_preserved':True,'new_bones':[n for n in rest if n not in names],'clips':{}}
# A short low dash: the source walk's foot path runs once in .333 seconds while the upper body stays composed.
dash=[]
for i in range(11):
 t=i/10;src=poses['walk'][round(t*(len(poses['walk'])-1))];frame={n:m.copy() for n,m in src.items()}
 # Low forward bow about the hips; both endpoints return to the idle pose for a clean exit.
 blend=math.sin(math.pi*t);base=poses['idle'][0]
 for name in names:
  a=base[name];b=frame[name];pa,qa,sa=a.decompose();pb,qb,sb=b.decompose()
  frame[name]=Matrix.LocRotScale(pa.lerp(pb,blend),qa.slerp(qb,blend),sa.lerp(sb,blend))
 pivot=frame['Hips'].translation;bow=Matrix.Translation(pivot)@Matrix.Rotation(math.radians(4)*blend,4,'X')@Matrix.Translation(-pivot)
 for name in names:
  if name not in ('LunaLeftBoot','LunaRightBoot'):frame[name]=bow@frame[name]
 dash.append(frame)
poses['dash']=dash
for kind,frames in poses.items():
 for i,pose in enumerate(frames):
  t=i/(len(frames)-1);wave=math.sin(math.tau*t);bob=.0025*(1-math.cos(math.tau*t))
  pitch=math.radians(4)*math.sin(math.pi*t) if kind=='dash' else math.radians(.6)*wave
  pivot=rest['Hips'].translation
  calm=Matrix.Translation(Vector((.002*wave,0,bob)))@Matrix.Translation(pivot)@Matrix.Rotation(pitch,4,'X')@Matrix.Translation(-pivot)
  # Auto-retargeted spine translations compressed the chest and tore the veil.
  # Preserve the authored proportions and use a coherent gentle body sway instead.
  for name in names:
   if name not in ('LunaLeftBoot','LunaRightBoot'):pose[name]=calm@rest[name]
for kind,frames in poses.items():
 for b in rig.pose.bones:b.rotation_mode='QUATERNION'
 action=bpy.data.actions.new('LunaPeaceful'+kind.title()+'_Game');rig.animation_data_create();rig.animation_data.action=action
 for i,pose in enumerate(frames):
  scene.frame_set(i+1);pose_prayer(pose,i,len(frames),kind)
  for bone in rig.pose.bones:
   for prop in ('location','rotation_quaternion','scale'):bone.keyframe_insert(prop,frame=i+1)
 scene.frame_start=1;scene.frame_end=len(frames);scene.frame_set(1)
 export(CACHE/('LunaPeaceful'+kind.title()+'_Game.fbx'));actions[kind]=action
 report['clips'][kind]={'frames':len(frames),'seconds':(len(frames)-1)/30}

# Neutral texture previews; only this temporary Blender process changes material display nodes.
for material in mesh.data.materials:
 material.use_nodes=True;tree=material.node_tree;tree.nodes.clear()
 tex=tree.nodes.new('ShaderNodeTexImage');tex.image=image;emission=tree.nodes.new('ShaderNodeEmission');output=tree.nodes.new('ShaderNodeOutputMaterial')
 tree.links.new(tex.outputs['Color'],emission.inputs['Color']);tree.links.new(emission.outputs['Emission'],output.inputs['Surface'])
for material in (stockings,dark,white,boot_cap):
 tree=material.node_tree;output=tree.nodes.get('Material Output');emission=tree.nodes.new('ShaderNodeEmission')
 emission.inputs['Color'].default_value=material.diffuse_color;tree.links.new(emission.outputs['Emission'],output.inputs['Surface'])
scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.resolution_x=800;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('PrayerQAWorld');scene.world.color=(.15,.17,.21)
scene.view_settings.view_transform='Standard'
camera_data=bpy.data.cameras.new('PrayerCamera');camera_data.type='ORTHO';camera_data.ortho_scale=1.90
camera=bpy.data.objects.new('PrayerCamera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
center=Vector((-.0178,0,.83))
for kind,frame in ((('idle',1),) if '--quick' in sys.argv else (('idle',1),('walk',13),('walk',38),('dash',6))):
 rig.animation_data.action=actions[kind];scene.frame_set(frame)
 for angle,direction in (('front',(0,-3,.10)),('threequarter',(1.8,-3,.12)),('side',(3,-.15,.10))):
  if frame==38 and angle!='threequarter':continue
  camera.location=center+Vector(direction);camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(OUT/f'{kind}_{frame:02}_{angle}.png');bpy.ops.render.render(write_still=True)
report['inputs']={k:{'path':str(v.relative_to(ROOT)),'sha256':hashlib.sha256(v.read_bytes()).hexdigest()} for k,v in sources.items()}
(OUT/'prayer_build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(CACHE/'prayer_review.blend'))
print(json.dumps(report,indent=2))
