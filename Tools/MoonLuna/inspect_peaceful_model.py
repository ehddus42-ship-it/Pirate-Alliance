"""Blender batch preview/geometry report; never modifies the imported character asset.

blender --background --python Tools/MoonLuna/inspect_peaceful_model.py
"""
import json
import math
from pathlib import Path
import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT / "Assets/Characters/MoonLuna/Peaceful/Models/LunaPeaceful_Rig.glb"
if not MODEL.exists():
    MODEL = MODEL.with_name("LunaPeaceful_Source.glb")
OUT = ROOT / "Documentation/MoonLuna/Preview"
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(MODEL))
objects = list(bpy.context.scene.objects)
meshes = [obj for obj in objects if obj.type == "MESH" and obj.data.materials]
for obj in objects:
    if obj.type == "MESH" and not obj.data.materials:
        obj.hide_render = True
corners = [obj.matrix_world @ Vector(v) for obj in meshes for v in obj.bound_box]
lo = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
hi = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
height = hi.z - lo.z
center = (lo + hi) * 0.5
report = {
    "input": MODEL.relative_to(ROOT).as_posix(),
    "bounds_min": list(lo), "bounds_max": list(hi), "height": height,
    "vertices": sum(len(obj.data.vertices) for obj in meshes),
    "triangles": sum(sum(len(poly.vertices) - 2 for poly in obj.data.polygons) for obj in meshes),
    "meshes": [{"name": obj.name, "vertices": len(obj.data.vertices),
                "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
                "armature_modifiers": sum(mod.type == "ARMATURE" for mod in obj.modifiers)} for obj in meshes],
    "armatures": [{"name": obj.name, "bones": [bone.name for bone in obj.data.bones]} for obj in objects if obj.type == "ARMATURE"],
}
(OUT / "peaceful_geometry.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 960
scene.render.resolution_y = 1200
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.12, 0.14, 0.18)
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
# A neutral unlit texture review avoids white clipping from studio light intensity.
# The source model materials remain untouched on disk.
for material in bpy.data.materials:
    if not material.use_nodes:
        continue
    tree = material.node_tree
    principled = next((node for node in tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    output = next((node for node in tree.nodes if node.type == "OUTPUT_MATERIAL"), None)
    if principled and output:
        emission = tree.nodes.new("ShaderNodeEmission")
        color = principled.inputs["Base Color"]
        if color.is_linked:
            tree.links.new(color.links[0].from_socket, emission.inputs["Color"])
        else:
            emission.inputs["Color"].default_value = color.default_value
        emission.inputs["Strength"].default_value = 1
        tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])

def area(name, position, energy, size):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.location = center + Vector(position) * height
    light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()

area("Key", (1.3, -2, 2.1), 750 * height * height, 3 * height)
area("Fill", (-1.4, -1, 0.5), 350 * height * height, 3 * height)
area("Back", (0.4, 1.5, 1.5), 550 * height * height, 2.5 * height)
camera_data = bpy.data.cameras.new("ReviewCamera")
camera_data.type = "ORTHO"
camera_data.ortho_scale = height * 1.16
camera = bpy.data.objects.new("ReviewCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
for name, direction in (("front", (0, -3, 0.08)), ("threequarter", (1.6, -3, 0.17)), ("back", (0, 3, 0.08))):
    camera.location = center + Vector(direction) * height
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(OUT / ("peaceful_" + name + ".png"))
    bpy.ops.render.render(write_still=True)
print(json.dumps({"height": height, "triangles": report["triangles"], "armatures": len(report["armatures"])}))
