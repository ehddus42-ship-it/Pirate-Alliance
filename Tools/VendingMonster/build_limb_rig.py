"""Skin the reviewed Meshy limb meshes to a reusable generic rig, preserving UV/PBR data."""
import bpy, json, math, pathlib
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
PARTS=ROOT/'Assets/Liminal/Art/Meshy/vending_monster_parts'
OUT=ROOT/'Assets/Liminal/Art/VendingMonster';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Tools/VendingMonster/Source';SOURCE.mkdir(parents=True,exist_ok=True)
REVIEW=ROOT/'Documentation/Liminal/Concepts/VendingMachine/ModelReview'
bpy.ops.wm.read_factory_settings(use_empty=True)
def center_at(obj,z,band=.025):
    pts=[v.co for v in obj.data.vertices if abs(v.co.z-z)<band]
    return Vector(((min(p.x for p in pts)+max(p.x for p in pts))*.5,(min(p.y for p in pts)+max(p.y for p in pts))*.5,z))
def load(key):
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(ROOT/'Tools/VendingMonster/Source/GameMeshes'/(key+'_game.glb')))
    meshes=[o for o in bpy.data.objects if o not in before and o.type=='MESH']
    for o in meshes:
        mw=o.matrix_world.copy();o.parent=None;o.matrix_world=mw
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();obj=bpy.context.object
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.name=key+'_source';return obj
sources={key:load(key) for key in ['arm','leg']}
arm=sources['arm'];leg=sources['leg']
arm_lo=min(v.co.z for v in arm.data.vertices);arm_hi=max(v.co.z for v in arm.data.vertices)
leg_lo=min(v.co.z for v in leg.data.vertices);leg_hi=max(v.co.z for v in leg.data.vertices)
# Straighten the neutral bind pose along the anatomical centerline without altering UVs.
arm_knots=[(-.36,center_at(arm,-.36)),(.30,center_at(arm,.30)),(.82,center_at(arm,.82))]
leg_knots=[(-.64,center_at(leg,-.64)),(.18,center_at(leg,.18)),(.84,center_at(leg,.84))]
def interpolated(knots,z):
    if z<=knots[0][0]:return knots[0][1]
    if z>=knots[-1][0]:return knots[-1][1]
    for (a,pa),(b,pb) in zip(knots,knots[1:]):
        if a<=z<=b:return pa.lerp(pb,(z-a)/(b-a))
def arm_map(v,side):
    c=interpolated(arm_knots,v.z);s=2.24/(arm_hi-arm_lo)
    return Vector((side*(.60-(v.x-c.x)*s), (v.y-c.y)*s, .27+(v.z-arm_lo)*s))
def leg_map(v,side):
    c=interpolated(leg_knots,v.z);s=1.31/(leg_hi-leg_lo)
    return Vector((side*(.33+(v.x-c.x)*s), (v.y-c.y)*s, (v.z-leg_lo)*s))
rigdata=bpy.data.armatures.new('VendingMonsterSkeleton');rig=bpy.data.objects.new('VendingMonsterRig',rigdata)
bpy.context.collection.objects.link(rig);bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
def bone(name,head,tail,parent=None):
    b=rigdata.edit_bones.new(name);b.head=head;b.tail=tail
    if parent:b.parent=rigdata.edit_bones[parent]
    # Stable local X across all fingers; flexion curls towards the palm.
    b.align_roll(Vector((0,-1,0)))
    return b
bone('Root',(0,0,0),(0,0,.25))
bone('Cabinet',(0,0,1.16),(0,0,2.16),'Root')
segments={};finger_paths={
 'Little':[(-.14,-.59),(-.17,-.69),(-.175,-.78),(-.16,-.82)],
 'Ring':[(-.083,-.59),(-.104,-.73),(-.101,-.82),(-.091,-.90)],
 'Middle':[(-.025,-.59),(-.037,-.75),(-.035,-.85),(-.027,-.94)],
 'Index':[(.031,-.57),(.053,-.72),(.065,-.81),(.082,-.91)],
 'Thumb':[(.073,-.45),(.123,-.52),(.156,-.63),(.176,-.68)]}
def surface_y(x,z):
    pts=sorted(arm.data.vertices,key=lambda v:(v.co.x-x)**2+(v.co.z-z)**2)[:30]
    return sum(v.co.y for v in pts)/len(pts)
for side,label in [(1,'L'),(-1,'R')]:
    shoulder=arm_map(interpolated(arm_knots,.87),side);shoulder.z=2.46
    elbow=arm_map(center_at(arm,.30),side);wrist=arm_map(center_at(arm,-.36),side)
    palm=arm_map(Vector((-.025,surface_y(-.025,-.57),-.57)),side)
    names=[f'UpperArm_{label}',f'Forearm_{label}',f'Hand_{label}']
    pts=[shoulder,elbow,wrist,palm]
    segments[('arm',label)]=[]
    for i,n in enumerate(names):
        bone(n,pts[i],pts[i+1], 'Cabinet' if i==0 else names[i-1]);segments[('arm',label)].append((n,pts[i],pts[i+1]))
    for finger,path in finger_paths.items():
        pp=[arm_map(Vector((x,surface_y(x,z),z)),side) for x,z in path]
        for i in range(3):
            n=f'{finger}{i+1}_{label}';bone(n,pp[i],pp[i+1],f'Hand_{label}' if i==0 else f'{finger}{i}_{label}')
            segments[('arm',label)].append((n,pp[i],pp[i+1]))
    hip=leg_map(center_at(leg,.84),side);hip.z=1.26
    knee=leg_map(center_at(leg,.18),side);ankle=leg_map(center_at(leg,-.64),side)
    toe=Vector((side*.33,-.32,.075));tip=Vector((side*.33,-.44,.04))
    names=[f'Thigh_{label}',f'Shin_{label}',f'Foot_{label}',f'Toes_{label}'];pts=[hip,knee,ankle,toe,tip]
    segments[('leg',label)]=[]
    for i,n in enumerate(names):
        bone(n,pts[i],pts[i+1],'Cabinet' if i==0 else names[i-1]);segments[('leg',label)].append((n,pts[i],pts[i+1]))
bpy.ops.object.mode_set(mode='OBJECT')
def distance(p,a,b):
    ab=b-a;t=max(0,min(1,(p-a).dot(ab)/ab.length_squared));return (p-(a+ab*t)).length
report={'provider':'Meshy','rig':'custom generic skeleton; 3 finger joints per digit','bones':len(rigdata.bones),'parts':[]}
outputs=[]
for key,src in sources.items():
    for side,label in [(1,'L'),(-1,'R')]:
        obj=src.copy();obj.data=src.data.copy();bpy.context.collection.objects.link(obj);obj.name=f'{key}_{label}_Skinned'
        mapper=arm_map if key=='arm' else leg_map
        for v in obj.data.vertices:v.co=mapper(v.co,side)
        # Reflection requires winding correction, otherwise one limb is inside out.
        if (key=='arm' and side==1) or (key=='leg' and side==-1):
            import bmesh
            bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()
        for poly in obj.data.polygons:poly.use_smooth=True
        group={n:obj.vertex_groups.new(name=n) for n,a,b in segments[(key,label)]}
        weights=[]
        for v in obj.data.vertices:
            candidates=sorted([(distance(v.co,a,b),n) for n,a,b in segments[(key,label)]])[:4]
            vals=[(n,1/max(.009,d)**5) for d,n in candidates];total=sum(w for n,w in vals)
            weights.append({n:w/total for n,w in vals})
        # Neighbor smoothing keeps elbows and finger knuckles deforming continuously.
        neighbors=[[] for _ in obj.data.vertices]
        for edge in obj.data.edges:
            a,b=edge.vertices;neighbors[a].append(b);neighbors[b].append(a)
        for _ in range(2):
            smooth=[]
            for i,w in enumerate(weights):
                out={n:x*.7 for n,x in w.items()}
                for j in neighbors[i]:
                    for n,x in weights[j].items():out[n]=out.get(n,0)+x*.3/max(1,len(neighbors[i]))
                top=sorted(out.items(),key=lambda a:-a[1])[:4];total=sum(x for n,x in top);smooth.append({n:x/total for n,x in top})
            weights=smooth
        for i,w in enumerate(weights):
            for n,x in w.items():group[n].add([i],x,'REPLACE')
        mod=obj.modifiers.new('Four-weight skeletal deformation','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=True
        obj.parent=rig;outputs.append(obj)
        report['parts'].append({'name':obj.name,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'weighted_vertices':len(weights),'max_influences':4})
for obj in sources.values():bpy.data.objects.remove(obj,do_unlink=True)
for image in bpy.data.images:
    if image.packed_file or image.has_data:
        image.pack()
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in outputs:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'vending_monster_rig.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'vending_monster_rig.glb'),export_format='GLB',use_selection=True,
                          export_skins=True,export_animations=False,export_yup=True)
(REVIEW/'rig-build.json').write_text(json.dumps(report,indent=2))
print('RIG_COMPLETE '+json.dumps(report),flush=True)
