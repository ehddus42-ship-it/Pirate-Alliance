"""Reduce the Moon Luna battle model to a playable polygon budget (about 350k triangles from 775k).

Needs Blender's Python module and meshoptimizer: `pip install bpy==5.2.2 meshoptimizer` (Python 3.13), then
    python -I Tools/MoonLuna/optimize_battle_model.py -- \
        --source Assets/Characters/MoonLuna/Models/Source~/MoonLuna_Full.fbx \
        --output Assets/Characters/MoonLuna/Models/MoonLuna.fbx \
        --report Documentation/MoonLuna/Performance/battle_model_decimation.json

- Only the parts in TARGETS are simplified, with meshoptimizer on the render vertices (position + UV + authored
  normal per corner, as Unity splits them). It only keeps existing vertices and collapses UV seams pairwise, so every
  kept corner has exactly its authored UV and normal, and texture atlases do not smear. (Blender's Decimate collapse
  was tried first: it scrambled the UVs of the atlas-textured hair, outer skirt and lantern.)
  Face, body and small trims stay as authored.
- Object names, mesh data names, hierarchy, transforms, materials and UV layers are kept, and the export settings
  reproduce the source FBX exactly (FBX 7400, Y up, UnitScaleFactor 1 with the 100x root), so Unity keeps every
  mesh fileID and the existing prefabs pick up the lighter meshes without new references.
- The report lists triangles before/after and the surface deviation (mm) both ways for every decimated part.
"""
import argparse
import json
import os
import sys

import bpy
import meshoptimizer
import numpy as np
from mathutils.bvhtree import BVHTree

# triangles per part after decimation
TARGETS = {
    "BellSleeve_L": 12000, "BellSleeve_R": 12000,
    "Skirt_Ivory": 12000, "Skirt_Navy": 17000,
    "Hair_Back_L": 20000, "Hair_Back_R": 19000, "Hair_Front": 12000,   # fewer shortens thin strand tips
    "Hair_BackFill_Lock_01": 4000, "Hair_BackFill_Lock_02": 4000, "Hair_BackFill_Lock_03": 4000,
    "Hair_BackFill_Lock_04": 3000, "Hair_BackFill_Lock_05": 3000, "Hair_BackFill_Lock_06": 3000,
    "Hair_BackFill_Lock_07": 3000,
    "Lantern_Frame": 9000, "Bodice_Navy": 9000, "Body_VisibleSkin": 8000,
    "Gold_Hem_Charms_R": 9500, "Gold_Hem_Charms_L": 7000, "Gold_Hem_Trim": 4500, "Chest_Crescent": 3500,
    "Collar_Ribbons_Ivory": 6000, "Waist_Navy_Inset": 3500,
    "Puff_Sleeve_Join_L": 3000, "Puff_Sleeve_Join_R": 3000,
    "Boot_L": 6000, "Boot_R": 6000, "Boot_Shaft_Underlap_L": 2500, "Boot_Shaft_Underlap_R": 2500,
    "Floating_FullMoon": 3000,
}

# Matches the source FBX: see Documentation/MoonLuna/Performance/battle_form_lag.md
EXPORT = dict(axis_forward="-Z", axis_up="Y", global_scale=1.0, apply_unit_scale=False,
              object_types={"EMPTY", "MESH"}, bake_anim=False, add_leaf_bones=False, path_mode="RELATIVE")


def triangles(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def world_bvh(obj):
    mesh = obj.data
    m = obj.matrix_world
    verts = [m @ v.co for v in mesh.vertices]
    return BVHTree.FromPolygons(verts, [p.vertices for p in mesh.polygons]), verts


def deviation_mm(points, tree, stride=1):
    distances = []
    for i in range(0, len(points), stride):
        hit = tree.find_nearest(points[i])
        if hit[0] is not None:
            distances.append(hit[3])
    distances.sort()
    if not distances:
        return {"mean": 0.0, "p95": 0.0, "max": 0.0}
    # Source vertices are in metres once the 100x root is applied (matrix_world)
    to_mm = 1000.0
    return {"mean": round(sum(distances) / len(distances) * to_mm, 3),
            "p95": round(distances[int(len(distances) * 0.95)] * to_mm, 3),
            "max": round(distances[-1] * to_mm, 3)}


def simplify(obj, target_triangles):
    """Simplify one single-material, single-UV part in place, keeping authored UVs and normals on every kept corner."""
    mesh = obj.data
    if len(mesh.materials) > 1 or len(mesh.uv_layers) > 1:
        raise ValueError(f"{obj.name}: expected one material and at most one UV layer")
    mesh.calc_loop_triangles()
    loops = len(mesh.loops)
    corner_vertex = np.empty(loops, np.int32)
    mesh.loops.foreach_get("vertex_index", corner_vertex)
    uv = np.zeros(loops * 2, np.float32)   # parts without UVs (sleeve joins) simplify on position + normal
    if mesh.uv_layers:
        mesh.uv_layers[0].data.foreach_get("uv", uv)
    normal = np.empty(loops * 3, np.float32)
    mesh.corner_normals.foreach_get("vector", normal)
    position = np.empty(len(mesh.vertices) * 3, np.float32)
    mesh.vertices.foreach_get("co", position)
    uv, normal, position = uv.reshape(-1, 2), normal.reshape(-1, 3), position.reshape(-1, 3)

    # render vertices: one per distinct (vertex, uv, normal), like the Unity importer's split
    key = np.concatenate([corner_vertex[:, None].astype(np.float32), np.round(uv, 6), np.round(normal, 5)], axis=1)
    _, first_corner, render_of_corner = np.unique(key, axis=0, return_index=True, return_inverse=True)
    render_of_corner = render_of_corner.reshape(-1).astype(np.uint32)
    render_vertex = corner_vertex[first_corner]
    render_position = np.ascontiguousarray(position[render_vertex])
    tri_corners = np.empty(len(mesh.loop_triangles) * 3, np.int32)
    mesh.loop_triangles.foreach_get("loops", tri_corners)
    indices = np.ascontiguousarray(render_of_corner[tri_corners])

    destination = np.zeros(len(indices), np.uint32)
    count = meshoptimizer.simplify(destination, indices, render_position,
                                   target_index_count=target_triangles * 3, target_error=0.05)
    kept = destination[:count].reshape(-1, 3)
    # Two render vertices of one seam vertex can end up in the same triangle; drop those and exact duplicates.
    corners_vertex = render_vertex[kept]
    valid = ((corners_vertex[:, 0] != corners_vertex[:, 1]) & (corners_vertex[:, 1] != corners_vertex[:, 2])
             & (corners_vertex[:, 0] != corners_vertex[:, 2]))
    kept = kept[valid]
    _, unique_faces = np.unique(np.sort(render_vertex[kept], axis=1), axis=0, return_index=True)
    kept = kept[np.sort(unique_faces)]

    # rebuild: shared positions per original vertex, UV and normal per corner from the kept render vertex
    used = np.unique(render_vertex[kept.reshape(-1)])
    new_index = np.full(len(position), -1, np.int64)
    new_index[used] = np.arange(len(used))
    faces = new_index[render_vertex[kept]]
    rebuilt = bpy.data.meshes.new(mesh.name + "_reduced")
    rebuilt.from_pydata(position[used].tolist(), [], faces.tolist())
    if rebuilt.validate() or len(rebuilt.loops) != kept.size:
        raise ValueError(f"{obj.name}: simplified mesh needed repairs")
    corners = kept.reshape(-1)
    if mesh.uv_layers:
        rebuilt.uv_layers.new(name=mesh.uv_layers[0].name)
        rebuilt.uv_layers[0].data.foreach_set("uv", uv[first_corner[corners]].reshape(-1))
    for material in mesh.materials:
        rebuilt.materials.append(material)
    rebuilt.polygons.foreach_set("use_smooth", np.ones(len(rebuilt.polygons), bool))
    rebuilt.normals_split_custom_set(normal[first_corner[corners]].tolist())
    name = mesh.name
    obj.data = rebuilt
    bpy.data.meshes.remove(mesh)
    rebuilt.name = name


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:])
    source, output = os.path.abspath(args.source), os.path.abspath(args.output)
    if os.path.normcase(source) == os.path.normcase(output):
        raise ValueError("Output must be a different file from the source FBX.")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    view_layer = bpy.context.view_layer
    rows = []
    total_before = sum(triangles(o.data) for o in bpy.data.objects if o.type == "MESH")
    missing = sorted(set(TARGETS) - {o.name for o in bpy.data.objects})
    if missing:
        raise ValueError("Parts not found in the source FBX: " + ", ".join(missing))

    for obj in sorted((o for o in bpy.data.objects if o.name in TARGETS), key=lambda o: o.name):
        before = triangles(obj.data)
        target = TARGETS[obj.name]
        if before <= target:
            continue
        reference = obj.copy()
        reference.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(reference)
        source_tree, source_verts = world_bvh(reference)

        simplify(obj, target)

        reduced_tree, reduced_verts = world_bvh(obj)
        rows.append({
            "name": obj.name,
            "source_triangles": before,
            "triangles": triangles(obj.data),
            "reduced_to_source_mm": deviation_mm(reduced_verts, source_tree),
            "source_to_reduced_mm": deviation_mm(source_verts, reduced_tree, stride=max(1, len(source_verts) // 20000)),
        })
        print("OPTIMIZED " + json.dumps(rows[-1]), flush=True)
        bpy.data.objects.remove(reference, do_unlink=True)

    total_after = sum(triangles(o.data) for o in bpy.data.objects if o.type == "MESH")
    bpy.ops.object.select_all(action="SELECT")
    os.makedirs(os.path.dirname(output), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=output, use_selection=True, **EXPORT)

    report = {"source": os.path.relpath(source), "output": os.path.relpath(output),
              "total_triangles_before": total_before, "total_triangles_after": total_after, "parts": rows}
    os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
    with open(args.report, "w", encoding="utf8") as f:
        json.dump(report, f, indent=2)
    print(f"LUNA_BATTLE_OPTIMIZED {total_before} -> {total_after} triangles", flush=True)


main()
