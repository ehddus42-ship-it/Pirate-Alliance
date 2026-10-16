from pathlib import Path
import bpy
from mathutils import Vector
R=Path(__file__).resolve().parents[2];S=R/'Tools/NamgungLuna/Source/PeacefulBeforePrayer';O=R/'Documentation/NamgungLuna/PeacefulPrayerQA'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(S/'LunaPeacefulIdle_Game.fbx'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');bpy.context.scene.frame_set(1)
pose={b.name:rig.matrix_world@b.matrix.copy() for b in rig.pose.bones}
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(S/'LunaPeaceful_Game.fbx'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');mesh=next(o for o in bpy.data.objects if o.type=='MESH')
for name,matrix in pose.items():rig.pose.bones[name].matrix=rig.matrix_world.inverted()@matrix;bpy.context.view_layer.update()
image=bpy.data.images.load(str(R/'Assets/Characters/NamgungLuna/Peaceful/Models/LunaPeaceful_Albedo_0.png'))
for m in mesh.data.materials:
 m.use_nodes=True;t=m.node_tree;t.nodes.clear();tex=t.nodes.new('ShaderNodeTexImage');tex.image=image;e=t.nodes.new('ShaderNodeEmission');out=t.nodes.new('ShaderNodeOutputMaterial');t.links.new(tex.outputs['Color'],e.inputs['Color']);t.links.new(e.outputs['Emission'],out.inputs['Surface'])
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=8;s.render.resolution_x=800;s.render.resolution_y=1000;s.render.resolution_percentage=100;s.view_settings.view_transform='Standard'
s.world=bpy.data.worlds.new('Baseline');s.world.color=(.15,.17,.21)
cdata=bpy.data.cameras.new('Camera');cdata.type='ORTHO';cdata.ortho_scale=1.9;c=bpy.data.objects.new('Camera',cdata);s.collection.objects.link(c);s.camera=c;center=Vector((-.0178,0,.83))
for name,offset in [('front',(0,-3,.1)),('side',(3,-.15,.1))]:
 c.location=center+Vector(offset);c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(O/('baseline_'+name+'.png'));bpy.ops.render.render(write_still=True)
