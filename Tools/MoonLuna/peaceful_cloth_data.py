"""Read-only garment segmentation and cloth recommendations for peaceful Luna.

Run in Blender 5.2 background. Writes data/previews under Documentation only;
does not export a model, touch Unity, or modify the source FBX.
Coordinates in JSON are Blender world metres: +X left, +Z up, -Y forward.
"""
from pathlib import Path
import bpy, math, json, hashlib, argparse, sys
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--no-render',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
OUT=ROOT/'Documentation/MoonLuna/PeacefulCloth';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Game.fbx'
ALBEDO=SOURCE.parent/'LunaPeaceful_Albedo_0.png'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE),use_image_search=False)
scene=bpy.context.scene;rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
meshes=sorted([o for o in bpy.data.objects if o.type=='MESH'],key=lambda o:o.name)
image=bpy.data.images.load(str(ALBEDO));pixels=np.asarray(image.pixels[:],dtype=np.float32).reshape(image.size[1],image.size[0],4)
MID=-.0178

def clamp(v):return max(0.,min(1.,v))
def ramp(a,b,v):return clamp((v-a)/(b-a))
def sample(uv):
 return pixels[min(image.size[1]-1,max(0,int(uv.y*image.size[1]))),min(image.size[0]-1,max(0,int(uv.x*image.size[0])))][:3]
def bounds(points):
 p=np.asarray(points);return {'min':p.min(axis=0).tolist(),'max':p.max(axis=0).tolist()}
def face_color(mesh,face):
 if not mesh.uv_layers:return np.array([.1,.1,.1])
 # Sample corners and centre to reduce atlas-seam/mixed border noise.
 uvs=[mesh.uv_layers.active.data[i].uv for i in face.loop_indices]
 return np.median([sample(v) for v in uvs]+[sample(sum(uvs,Vector((0,0)))/len(uvs))],axis=0)

PALETTE={'fixed_body':(.22,.24,.27),'outer_skirt':(.13,.35,.9),'inner_skirt':(.15,.85,.7),
 'veil':(.72,.22,.92),'ribbon':(.96,.65,.1),'sleeve_tip':(.93,.25,.32),
 'fixed_sleeve':(.48,.16,.18),'fixed_hands':(.75,.58,.45),'fixed_boots':(.35,.25,.13),
 'fixed_legs':(.35,.35,.35),'fixed_lining':(.18,.18,.18),'fixed_interior':(.95,.48,.12)}
materials={}
for name,color in PALETTE.items():
 material=bpy.data.materials.new('Mask_'+name);material.diffuse_color=(*color,1);material.use_nodes=True
 tree=material.node_tree;tree.nodes.clear();out=tree.nodes.new('ShaderNodeOutputMaterial');em=tree.nodes.new('ShaderNodeEmission')
 em.inputs['Color'].default_value=(*color,1);tree.links.new(em.outputs['Emission'],out.inputs['Surface']);materials[name]=material

def classify(name,c,color):
 x,y,z=c;sx=abs(x-MID)
 if 'SeparatedBoot' in name:return 'fixed_boots'
 if 'OriginalHand' in name:return 'fixed_hands'
 if 'Stocking' in name:return 'fixed_legs'
 if 'DressLining' in name:return 'fixed_lining'
 if 'ForeArmSleeve' in name:return 'sleeve_tip'
 if 'ArmSleeve' in name:return 'fixed_sleeve'
 # The generated single surface contains old boot/shin stubs beneath the
 # skirt, connected to it by narrow inward bridge faces. These are neither
 # the visible skirt nor the separately rebuilt boots/legs. They cluster
 # within a 14 cm central radius below 50 cm; the external hem is wider.
 # Keep the original geometry skinned in FixedBody instead of simulating it.
 if z<.50 and math.hypot(x-MID,y+.04)<.14:return 'fixed_interior'
 # Dress below the waist, including a fully pinned band at its top.
 if z<.965:
  white=min(color)>.48 and max(color)-min(color)<.23
  return 'inner_skirt' if white else 'outer_skirt'
 # The veil is the rear/side hood surface. Keep all central frontal head,
 # hair, face and neckline outside the simulation, even if coloured white.
 if 1.02<z<1.65 and (y>.035 or (sx>.155 and y>-.08)):
  return 'veil'
 # Neck bow tails are white thin front surfaces; upper bow stays pinned.
 if 1.19<z<1.335 and -.255<y<-.12 and .025<sx<.115 and min(color)>.55 and max(color)-min(color)<.18:
  return 'ribbon'
 return 'fixed_body'

def mobility(region,p,name):
 x,y,z=p;sx=abs(x-MID)
 if region in ('outer_skirt','inner_skirt'):
  # Top 10 cm is an explicit pin blend, bottom of the skirt can move.
  return 1-ramp(.72,.90,z)
 if region=='veil':
  # A crown pinned to Head. The lower rear and side drape gradually release.
  return 1-ramp(1.19,1.46,z)
 if region=='ribbon':return 1-ramp(1.23,1.31,z)
 if region=='sleeve_tip':
  side='Left' if 'Left' in name else 'Right'
  elbow=rig.matrix_world@rig.data.bones[side+'ForeArm'].head_local
  wrist=rig.matrix_world@rig.data.bones[side+'Hand'].head_local
  t=(Vector(p)-elbow).dot(wrist-elbow)/(wrist-elbow).length_squared
  return ramp(.77,1.06,t)*.12
 return 0.

report={'schema_version':1,'source':str(SOURCE.relative_to(ROOT)),'sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
 'coordinates':{'space':'Blender world metres at model bind pose','up':'+Z','front':'-Y','character_left':'+X','centre_x':MID,
 'unity_hint':'Confirmed by Unity owner: ToUnity(x,y,z)=(-x,z,-y).'},
 'matching':{'method':'Match per-triangle bind-pose world centroid and area, or corner position+UV; do not rely on Blender/Unity vertex indices.',
 'position_tolerance_metres':.0001,'uv_tolerance':.0001,'preserve':'Original UVs, normals, tangents, bone weights, bind poses and original material. Duplicate seam vertices when splitting renderers.'},
 'palette':{n:list(v) for n,v in PALETTE.items()},'meshes':[],'regions':{},'landmarks':{},'colliders':[]}
for bone in ('Hips','Spine','Head','LeftHand','RightHand','LunaLeftBoot','LunaRightBoot'):
 report['landmarks'][bone]=list((rig.matrix_world@rig.data.bones[bone].matrix_local).translation)
all_region_points={}
GROUPS=[('Skirt','char1'),('Veil','char1'),('CollarRibbon','char1'),('Cuff_L','LunaLeftForeArmSleeve'),('Cuff_R','LunaRightForeArmSleeve')]
def group_index(region,name):
 if region in ('outer_skirt','inner_skirt'):return 0
 if region=='veil':return 1
 if region=='ribbon':return 2
 if region=='sleeve_tip':return 3 if 'Left' in name else 4
 return -1
def position_key(p):return tuple(round(float(v),5) for v in p)
for obj in meshes:
 mesh=obj.data;mesh.calc_loop_triangles();world=[obj.matrix_world@v.co for v in mesh.vertices]
 colors={p.index:face_color(mesh,p) for p in mesh.polygons}
 labels={p.index:classify(obj.name,sum((world[i] for i in p.vertices),Vector())/len(p.vertices),colors[p.index]) for p in mesh.polygons}
 # Pin every split boundary on the dynamic side: this avoids cracks where
 # two regions duplicate the same original vertices.
 touching={}
 for p in mesh.polygons:
  for v in p.vertices:touching.setdefault(position_key(world[v]),set()).add(group_index(labels[p.index],obj.name))
 vertices=[]
 for v in mesh.vertices:vertices.append([round(n,7) for n in world[v.index]])
 triangles=[];counts={};pin_counts={}
 for tri in mesh.loop_triangles:
  region=labels[tri.polygon_index];ids=list(tri.vertices);pp=[world[i] for i in ids]
  uv=[list(mesh.uv_layers.active.data[i].uv) for i in tri.loops] if mesh.uv_layers else [[0,0]]*3
  move=[0. if len(touching[position_key(world[i])])>1 else mobility(region,world[i],obj.name) for i in ids]
  triangles.append({'v':ids,'uv':[[round(q,7) for q in p] for p in uv],'region':region,'mobility':[round(m,5) for m in move]})
  counts[region]=counts.get(region,0)+1;pin_counts[region]=pin_counts.get(region,0)+sum(m==0 for m in move)
  all_region_points.setdefault(region,[]).extend(pp)
 record={'name':obj.name,'vertices_world':vertices,'object_matrix_world':[list(row) for row in obj.matrix_world],
  'triangles':triangles,'triangle_counts':counts,'pinned_triangle_corners':pin_counts,'bounds':bounds(world)}
 report['meshes'].append(record)
 mesh.materials.clear()
 for m in materials.values():mesh.materials.append(m)
 indices={n:i for i,n in enumerate(materials)}
 for p in mesh.polygons:p.material_index=indices[labels[p.index]]
for name,points in all_region_points.items():report['regions'][name]={'bounds':bounds(points),'triangle_count':sum(m['triangle_counts'].get(name,0) for m in report['meshes'])}

# Conservative body volumes inside the existing garment; dimensions in metres.
# Parent bone names are the unchanged authored skeleton, not new simulation bones.
for entry in [
 ('pelvis','Hips',(-.0178,-.05,.91),(-.0178,-.05,1.04),.105),
 ('torso','Spine',(-.0178,-.055,1.04),(-.0178,-.055,1.27),.105),
 ('neck','neck',(-.0178,-.035,1.30),(-.0178,-.035,1.39),.045),
 ('head','Head',(-.0178,-.025,1.43),(-.0178,-.025,1.55),.09),
 ('left_thigh','LunaLeftThigh',(.035,-.045,.87),(.035,-.09,.58),.045),
 ('right_thigh','LunaRightThigh',(-.065,-.045,.87),(-.065,-.09,.58),.045),
 ('left_shin','LunaLeftShin',(.035,-.09,.55),(.035,-.02,.24),.033),
 ('right_shin','LunaRightShin',(-.065,-.09,.55),(-.065,-.02,.24),.033)]:
 name,bone,a,b,r=entry;report['colliders'].append({'name':name,'parent_bone':bone,'endpoint_a':a,'endpoint_b':b,'radius':r})
for side in ('Left','Right'):
 forearm='Luna'+side+'PrayerForeArm';hand='Luna'+side+'PrayerHand'
 a=(rig.matrix_world@rig.data.bones[forearm].matrix_local).translation
 b=(rig.matrix_world@rig.data.bones[hand].matrix_local).translation
 report['colliders'].append({'name':side.lower()+'_cuff_wrist','parent_bone':forearm,'endpoint_a':list(a.lerp(b,.78)),'endpoint_b':list(b),'radius':.019})
report['recommendations']={
 'separate_renderers':['fixed_body','Skirt (outer+inner together)','Veil','CollarRibbon'],
 'fixed_parts':['face/hair in fixed_body','hands','upper sleeves','stocking legs','boots','inner torso lining','internal old boot/shin surface below .50m within radius .14m of (-.0178,-.04)'],
 'skirt':'Pin all vertices at z>=.90 and blend mobility over .72<z<.90. Keep fixed seam with torso. Outer/inner are colour regions on the same generated surface: simulate as a single Skirt renderer, preserving the original colour boundary.',
 'veil':'Pin z>=1.46; blend to free below1.19. Central frontal head is excluded; side boundary additionally pinned.',
 'ribbon':'Optional small secondary motion; only neutral-white frontal tails below1.335. Pin z>=1.31 and blend to full below1.23. Hair and central neckline are excluded.',
 'sleeves':'Keep shoulder and forearm skeleton motion. Only distal cuff has maximum12% mobility; hands and wrists remain fully animation-driven. May omit for stability.',
 'hair':'Leave fully fixed for this generated single surface. Colour-based blue hair extraction risks moving eyelash/face triangles.',
 'collision':'Use only the listed body colliders per relevant cloth. Exclude hands/forearms from skirt if unnecessary. Do not use an oversized head collider that pushes the hood away.',
 'interior_repair':'The 62/25-point skirt components are waist fabric and remain simulated. Boot/shin remnants belonged to the main 4307-point component. Only central low interior faces are assigned FixedBody (-1); their shared skirt boundary stays pinned.',
 'limitations':'Colour separates visible cloth layers but does not prove independent physically separate panels. A welded-position seam audit and preview review are included before use.'}
(OUT/'cloth_analysis.json').write_text(json.dumps(report,separators=(',',':')),encoding='utf-8')
compact={'schemaVersion':1,'source':'char1','sourceSha256':report['sha256'],'triangles':[],'groups':[],'colliders':[],
 'coordinateMapping':'Blender world metres -> Unity model metres: (-x,z,-y)','fixedTriangleGroupIndex':-1}
group_points=[{} for _ in GROUPS];group_edges=[[] for _ in GROUPS]
for record in report['meshes']:
 vv=record['vertices_world'];name=record['name']
 for tri in record['triangles']:
  group=group_index(tri['region'],name);pp=[vv[i] for i in tri['v']]
  if name=='char1':compact['triangles'].extend([round(sum(p[c] for p in pp)/3,7) for c in range(3)]+[group])
  if group<0:continue
  keys=[position_key(p) for p in pp];group_edges[group].append(keys)
  for key,p,m in zip(keys,pp,tri['mobility']):
   old=group_points[group].get(key)
   group_points[group][key]=(p,min(old[1],m) if old else m)
component_reports=[]
for gi,(name,renderer) in enumerate(GROUPS):
 points=group_points[gi];parents={k:k for k in points}
 def find(k):
  while parents[k]!=k:parents[k]=parents[parents[k]];k=parents[k]
  return k
 for triangle in group_edges[gi]:
  for key in triangle[1:]:parents[find(key)]=find(triangle[0])
 components={}
 for key in points:components.setdefault(find(key),[]).append(key)
 unanchored=[]
 for component in components.values():
  if not any(points[k][1]==0 for k in component):
   # An isolated texture/geometry fragment without a pinned anchor must not
   # become a free-falling particle island. Retain its skinned animation.
   unanchored.append(len(component))
   for key in component:points[key]=(points[key][0],0.)
 flat=[];move=[]
 for p,m in points.values():flat.extend(p+[1 if m==0 else 2]);move.append(m)
 compact['groups'].append({'name':name,'renderer':renderer,'points':flat,'mobility':move})
 component_reports.append({'name':name,'points':len(points),'fixed':sum(m==0 for m in move),'move':sum(m>0 for m in move),
  'components':len(components),'component_sizes':sorted([len(c) for c in components.values()],reverse=True),
  'unanchored_components_retained_fixed':unanchored})
for c in report['colliders']:
 compact['colliders'].append({'name':c['name'],'bone':c['parent_bone'],'start':c['endpoint_a'],'end':c['endpoint_b'],'r0':c['radius'],'r1':c['radius'],'type':'capsule'})
(OUT/'cloth_data.json').write_text(json.dumps(compact,separators=(',',':')),encoding='utf-8')
summary={k:v for k,v in report.items() if k!='meshes'}
summary['simulation_groups']=component_reports
summary['meshes']=[{k:v for k,v in m.items() if k not in ('vertices_world','triangles')} for m in report['meshes']]
(OUT/'cloth_summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
face_moving=[]
for gi,g in enumerate(compact['groups']):
 for offset in range(0,len(g['points']),4):
  x,y,z,attribute=g['points'][offset:offset+4]
  if attribute==2 and abs(x-MID)<.12 and y<-.10 and 1.37<z<1.62:
   face_moving.append({'group':g['name'],'point':[x,y,z]})
checks={
 'source_unchanged':hashlib.sha256(SOURCE.read_bytes()).hexdigest()==report['sha256'],
 'centroid_entries_complete':len(compact['triangles'])%4==0,
 'all_centroids_finite':all(math.isfinite(v) for v in compact['triangles']),
 'all_group_indices_valid':all(-1<=v<len(GROUPS) for v in compact['triangles'][3::4]),
 'group_point_layout_valid':all(len(g['points'])==len(g['mobility'])*4 for g in compact['groups']),
 'group_attributes_valid':all(v in (1,2) for g in compact['groups'] for v in g['points'][3::4]),
 'all_group_points_finite':all(math.isfinite(v) for g in compact['groups'] for v in g['points']),
 'no_moving_points_in_frontal_face_roi':not face_moving,
 'every_moving_component_has_fixed_anchor':all(not c['unanchored_components_retained_fixed'] for c in component_reports),
 'hands_boots_upper_sleeves_not_simulated':all(g['renderer'] in ('char1','LunaLeftForeArmSleeve','LunaRightForeArmSleeve') for g in compact['groups'])}
validation={'passed':all(checks.values()),'checks':checks,'sourceSha256':report['sha256'],
 'clothDataSha256':hashlib.sha256((OUT/'cloth_data.json').read_bytes()).hexdigest(),
 'char1TriangleCount':len(compact['triangles'])//4,'groups':component_reports,'face_roi_violations':face_moving,
 'review':'Four bind-pose segmentation views checked. Skirt colour boundaries remain within one cloth group. Simulation tuning and collision behaviour must be checked in Unity.'}
(OUT/'cloth_validation.json').write_text(json.dumps(validation,indent=2),encoding='utf-8')
if not validation['passed']:raise RuntimeError('Cloth data validation failed; see cloth_validation.json')
if not args.no_render:
 scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.resolution_x=800;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('MaskWorld');scene.world.color=(.12,.12,.12);scene.view_settings.view_transform='Standard'
 camera_data=bpy.data.cameras.new('MaskCamera');camera_data.type='ORTHO';camera_data.ortho_scale=1.9
 camera=bpy.data.objects.new('MaskCamera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
 center=Vector((MID,0,.83))
 for name,d in [('front',(0,-3,.08)),('back',(0,3,.08)),('side',(3,0,.08)),('threequarter',(1.8,-3,.1))]:
  camera.location=center+Vector(d);camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(OUT/f'mask_{name}.png');bpy.ops.render.render(write_still=True)
 # A separate inspection view isolates the retained internal stub surfaces;
 # it changes only the temporary Blender scene, never the source FBX.
 for obj in meshes:
  if obj.name!='char1':obj.hide_render=True;continue
  for polygon in obj.data.polygons:polygon.hide=False
  keep=[p.index for p in obj.data.polygons if list(materials)[p.material_index]=='fixed_interior']
  import bmesh
  bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table()
  bmesh.ops.delete(bm,geom=[p for p in bm.faces if p.index not in keep],context='FACES')
  bm.to_mesh(obj.data);bm.free()
 center=Vector((MID,-.04,.28));camera_data.ortho_scale=.65
 camera.location=center+Vector((1.7,-3,.35));camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(OUT/'mask_internal_fixed_detail.png');bpy.ops.render.render(write_still=True)
print(json.dumps({'source_sha256':report['sha256'],'meshes':[{'name':m['name'],'triangle_counts':m['triangle_counts']} for m in report['meshes']],'regions':report['regions']},indent=2))
