"""Blender: export a light source skeleton and a seamless in-place peaceful walk.

blender --background --python Tools/MoonLuna/build_peaceful_walk_loop.py -- 
    --input Temp/MoonLuna/Meshy/peaceful_walk_raw.fbx

For the character-specific version, use peaceful_walk_retargeted.glb (or .fbx).
Only the animated armature is exported; provider meshes remain in the ignored cache.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import sys

import bpy
from mathutils import Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Characters/MoonLuna/Peaceful/Animations"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--input", default="Temp/MoonLuna/Meshy/peaceful_walk_raw.fbx")
parser.add_argument("--start-seconds", type=float, default=0.0)
parser.add_argument("--end-seconds", type=float, default=50 / 30)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
source_path = (ROOT / args.input).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
if source_path.suffix.lower() == ".glb":
    bpy.ops.import_scene.gltf(filepath=str(source_path))
else:
    bpy.ops.import_scene.fbx(filepath=str(source_path), use_image_search=False)
scene.render.fps = 30
scene.render.fps_base = 1.0
rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
if len(rigs) != 1:
    raise RuntimeError(f"Expected one armature, found {len(rigs)}")
rig = rigs[0]
if not rig.animation_data or not rig.animation_data.action:
    raise RuntimeError("Imported armature has no active animation")
source_action = rig.animation_data.action
source_action.name = "LunaPeacefulWalk_Source"
source_start, source_end = source_action.frame_range
fps = scene.render.fps / scene.render.fps_base
start = int(round(source_start + args.start_seconds * fps))
end = int(round(source_start + args.end_seconds * fps))
if start < source_start or end > source_end or end <= start:
    raise RuntimeError("Requested loop window lies outside the source action")
root_bones = [bone for bone in rig.pose.bones if bone.parent is None]
if len(root_bones) != 1:
    raise RuntimeError("Expected a single root bone for in-place correction")
root_bone = root_bones[0]
scene.frame_start = int(source_start)
scene.frame_end = int(source_end)
OUTPUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
bpy.context.view_layer.objects.active = rig


def export(path):
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"ARMATURE"},
        add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        axis_forward="-Z", axis_up="Y", path_mode="AUTO", use_custom_props=False,
    )


source_output = OUTPUT / "LunaPeacefulWalk_Source.fbx"
export(source_output)
# The end of the generated clip contains a calm, nearly parallel-foot stance.
# Reuse it as the movement prototype's idle so an unanimated A-pose is never shown.
idle_frame = min(int(source_end), int(round(source_start + 2.8 * fps)))
scene.frame_set(idle_frame)
idle_sample = {bone.name: bone.matrix_basis.decompose() for bone in rig.pose.bones}
samples = []
original_root_positions = []
reference_root = None
for frame in range(start, end + 1):
    scene.frame_set(frame)
    world_root = rig.matrix_world @ root_bone.matrix
    original_root_positions.append(list(world_root.translation))
    if reference_root is None:
        reference_root = world_root.translation.copy()
    # Locomotion code moves the player; preserve vertical weight transfer only.
    world_root.translation.x = reference_root.x
    world_root.translation.y = reference_root.y
    root_bone.matrix = rig.matrix_world.inverted() @ world_root
    bpy.context.view_layer.update()
    samples.append({bone.name: bone.matrix_basis.decompose() for bone in rig.pose.bones})

# Remove the small pose mismatch continuously over the cycle, including the last key.
# Quaternion corrections avoid Euler wrapping when the rig uses a different bone basis.
first, last = samples[0], samples[-1]
loop_action = bpy.data.actions.new("LunaPeacefulWalk")
rig.animation_data.action = loop_action
for index, sample in enumerate(samples):
    factor = index / (len(samples) - 1)
    smooth = factor * factor * (3.0 - 2.0 * factor)
    for bone in rig.pose.bones:
        position, rotation, scale = sample[bone.name]
        start_position, start_rotation, start_scale = first[bone.name]
        end_position, end_rotation, end_scale = last[bone.name]
        correction = end_rotation.rotation_difference(start_rotation)
        corrected_rotation = rotation @ Quaternion().slerp(correction, smooth)
        bone.rotation_mode = "QUATERNION"
        bone.location = position + (start_position - end_position) * smooth
        bone.rotation_quaternion = corrected_rotation
        bone.scale = scale + (start_scale - end_scale) * smooth
        bone.keyframe_insert("location", frame=index + 1)
        bone.keyframe_insert("rotation_quaternion", frame=index + 1)
        bone.keyframe_insert("scale", frame=index + 1)

scene.frame_start = 1
scene.frame_end = len(samples)
scene.frame_set(1)
loop_output = OUTPUT / "LunaPeacefulWalk_Loop.fbx"
export(loop_output)
idle_action = bpy.data.actions.new("LunaPeacefulIdle")
rig.animation_data.action = idle_action
chest_candidates = [bone for bone in rig.pose.bones if "spine" in bone.name.lower() or "chest" in bone.name.lower()]
chest = max(chest_candidates, key=lambda bone: len(bone.parent_recursive)).name if chest_candidates else None
for index in range(61):
    breath = 0.003 * math.sin(math.pi * index / 60.0) ** 2
    for bone in rig.pose.bones:
        position, rotation, scale = idle_sample[bone.name]
        bone.location = position
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = rotation
        bone.scale = scale * (1.0 + breath if bone.name == chest else 1.0)
        bone.keyframe_insert("location", frame=index + 1)
        bone.keyframe_insert("rotation_quaternion", frame=index + 1)
        bone.keyframe_insert("scale", frame=index + 1)
    # Use the same horizontal origin as the walk while keeping both feet still.
    bpy.context.view_layer.update()
    world_root = rig.matrix_world @ root_bone.matrix
    world_root.translation.x = reference_root.x
    world_root.translation.y = reference_root.y
    root_bone.matrix = rig.matrix_world.inverted() @ world_root
    root_bone.keyframe_insert("location", frame=index + 1)
scene.frame_start = 1
scene.frame_end = 61
scene.frame_set(1)
idle_output = OUTPUT / "LunaPeacefulIdle.fbx"
export(idle_output)
report = {
    "character_name": "문루나",
    "input": source_path.relative_to(ROOT).as_posix(),
    "source_frame_rate": fps,
    "source_frame_range": [source_start, source_end],
    "selected_source_frame_range": [start, end],
    "loop_frame_range": [1, len(samples)],
    "loop_duration_seconds": (len(samples) - 1) / fps,
    "idle_source_frame": idle_frame,
    "idle_duration_seconds": 2.0,
    "armature_name": rig.name,
    "root_bone": root_bone.name,
    "bones": [{"name": bone.name, "parent": bone.parent.name if bone.parent else None} for bone in rig.data.bones],
    "source_root_translation": [original_root_positions[-1][axis] - original_root_positions[0][axis] for axis in range(3)],
    "processing": "Selected one steady gait cycle. Removed horizontal root travel; preserved vertical weight transfer. Smooth distributed quaternion/position correction makes start and end poses match. Exported armature and action only.",
    "outputs": [{"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size,
                 "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in (source_output, loop_output, idle_output)],
}
report_path = ROOT / "Documentation/MoonLuna/peaceful_walk_loop.json"
report_path.parent.mkdir(parents=True, exist_ok=True)
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: value for key, value in report.items() if key != "bones"}, ensure_ascii=False, indent=2))
