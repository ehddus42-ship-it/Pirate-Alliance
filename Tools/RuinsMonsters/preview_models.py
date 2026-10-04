"""Blender inspection renders. Run with blender --background --python this_file -- key."""
import bpy
from mathutils import Vector
from pathlib import Path
import sys
import json

ROOT = Path(__file__).resolve().parents[2]
key = sys.argv[sys.argv.index('--') + 1]
folder = ROOT / 'Tools/RuinsMonsters/Source' / key
model = ROOT / 'Assets/RuinsMonsters/Art/Meshy' / key / (key + '.glb')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(model))
mesh_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
points = [o.matrix_world @ Vector(p) for o in mesh_objects for p in o.bound_box]
low = Vector([min(p[i] for p in points) for i in range(3)])
high = Vector([max(p[i] for p in points) for i in range(3)])
center = (low + high) * .5
radius = max(high - low) * .6
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 20
scene.cycles.use_denoising = True
scene.render.resolution_x = 1024
scene.render.resolution_y = 1024
scene.render.resolution_percentage = 100
scene.world.color = (.3, .3, .3)
scene.view_settings.view_transform = 'AgX'
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
world = scene.world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.11, .14, .18, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .6

def light(name, offset, power, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = center + Vector(offset) * radius
    obj.rotation_euler = (center - obj.location).to_track_quat('-Z', 'Y').to_euler()

light('Key', (2, -3, 4), 650, radius * 3)
light('Fill', (-3, -1, 1), 450, radius * 3)
light('Rim', (1, 3, 3), 800, radius * 2)
camera_data = bpy.data.cameras.new('Inspection camera')
camera = bpy.data.objects.new('Inspection camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
camera_data.ortho_scale = radius * 2.4
for tag, offset in [('plus_z', (.6, -4, 1.25)), ('minus_z', (.6, 4, 1.25)),
                    ('minus_x', (-4, -.6, 1.25)), ('plus_x', (4, -.6, 1.25))]:
    camera.location = center + Vector(offset) * radius
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(folder / ('gltf_' + tag + '.png'))
    bpy.ops.render.render(write_still=True)
print('INSPECTION_READY', key, flush=True)
