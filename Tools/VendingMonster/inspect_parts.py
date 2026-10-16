import bpy, json, pathlib, math
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
PARTS=ROOT/'Assets/Liminal/Art/Meshy/vending_monster_parts'
OUT=ROOT/'Documentation/Liminal/Concepts/VendingMachine/ModelReview'
OUT.mkdir(parents=True,exist_ok=True)
report={}
for part in ['arm','leg']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path=PARTS/part/(part+'_final.glb')
    if not path.exists():continue
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes=[o for o in bpy.data.objects if o.type=='MESH']
    verts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    lo=Vector([min(v[i] for v in verts) for i in range(3)])
    hi=Vector([max(v[i] for v in verts) for i in range(3)])
    report[part]={'min':list(lo),'max':list(hi),'objects':[o.name for o in meshes], 'vertices':len(verts)}
    center=(lo+hi)*.5
    for face,offset in [('front',(0,-4,0)),('side',(4,0,0))]:
        bpy.ops.object.camera_add(location=center+Vector(offset));camera=bpy.context.object
        camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.type='ORTHO';camera.data.ortho_scale=(hi.z-lo.z)*1.15
        scene=bpy.context.scene;scene.camera=camera;scene.render.engine='CYCLES';scene.cycles.samples=16
        scene.render.resolution_x=720;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
        scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True
        scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.45,.45,.45,1)
        scene.world.node_tree.nodes['Background'].inputs[1].default_value=.8
        bpy.ops.object.light_add(type='AREA',location=center+Vector((-2,-3,3)));light=bpy.context.object
        light.data.energy=400;light.data.shape='DISK';light.data.size=4
        light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/(part+'_'+face+'.png'));bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(camera,do_unlink=True);bpy.data.objects.remove(light,do_unlink=True)
    # Horizontal landmark slices make manual bone placement reproducible.
    slices=[]
    for f in [0,.04,.08,.12,.16,.2,.24,.28,.32,.4,.48,.52,.6,.7,.8,.9,.98]:
        z=lo.z+(hi.z-lo.z)*f
        sample=[v for v in verts if abs(v.z-z)<(hi.z-lo.z)*.01]
        if sample:slices.append({'h':f,'z':z,'x_min':min(v.x for v in sample),'x_max':max(v.x for v in sample),'y_min':min(v.y for v in sample),'y_max':max(v.y for v in sample)})
    report[part]['slices']=slices
(OUT/'mesh_landmarks.json').write_text(json.dumps(report,indent=2))
print('LIMB_BOUNDS '+json.dumps(report),flush=True)
