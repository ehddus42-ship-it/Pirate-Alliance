"""Write variation rooms as Unity prefabs (plus their mesh assets and layout JSON).

The room shell (WalkableFloor, boundaries, doorway blockers, gates, sockets, player/enemy markers and the
LiminalRoom component) comes from the existing Unity-authored prefab of the same number, so scenes, stage
definitions and the shared gallery keep resolving the same root objects. Rooms 06 and 07 are new: their
shell is Unity's room 02 shell with every fileID renewed.
"""
import json
import math
import pathlib
import re

import materials as mt
import prefabdoc as pd
import room as rm
import unityasset as ua

ROOT = rm.ROOT
ROOMS = ROOT / 'Assets/StageConcepts/Prefabs/Rooms'
GEOMETRY = ROOT / 'Assets/StageConcepts/Geometry/Variations'
LAYOUTS = ROOT / 'Assets/StageConcepts/Layouts'
KIND = {'Arrival': 0, 'Exploration': 1, 'Combat': 2, 'Threshold': 3, 'Boss': 4}


def load_shell(room):
    own = ROOMS / f'{room.key}.prefab'
    if own.exists():
        prefab = pd.Prefab(own.read_text(encoding='utf-8'))
        return prefab, room.key, False
    template_key = f'{room.key.split("_")[0]}_02'
    prefab = pd.Prefab((ROOMS / f'{template_key}.prefab').read_text(encoding='utf-8'))
    prefab.remap_ids()
    return prefab, template_key, True


def child_named(prefab, transform, name):
    for cid in prefab.children(transform):
        t = prefab.by_id[cid]
        if t.stripped:
            continue
        if prefab.by_id[t.ref('m_GameObject')].field('m_Name') == name:
            return t
    return None


def set_position(transform, position):
    transform.body = re.sub(r'  m_LocalPosition: \{[^}]*\}', lambda m: '  m_LocalPosition: ' + ua.vec3(position), transform.body, count=1)


def write_meshes(room):
    """One mesh asset per material batch; returns {material: mesh guid}."""
    out = {}
    GEOMETRY.mkdir(parents=True, exist_ok=True)
    ua.ensure_meta(GEOMETRY, ua.folder_meta)
    for material, geometry in sorted(room.kit.batches.items()):
        if not len(geometry):
            continue
        name = f'{room.key}_{material}'
        p, n, t, uv, tris = geometry.arrays()
        path = GEOMETRY / f'{name}.asset'
        ua.write_text(path, ua.mesh_yaml(name, p, n, t, uv, tris))
        out[material] = ua.ensure_meta(path, lambda g: ua.native_meta(g, 4300000))
    return out


def write_room(room, material_guids, emissive):
    ua.seed_ids(rm.stable_seed('prefab', room.key))
    prefab, template_key, is_new = load_shell(room)
    root_go = prefab.game_objects(template_key)[0]
    root = prefab.transform_of(root_go)
    groups = {name: child_named(prefab, root, name) for name in ('Architecture', 'Gameplay', 'Sockets', 'Props', 'Lighting')}
    order = prefab.children(root)
    old_props, old_lighting = groups['Props'].fid, groups['Lighting'].fid
    prefab.remove_subtree(old_props)
    prefab.remove_subtree(old_lighting)
    mesh_guids = write_meshes(room)
    ua.seed_ids(rm.stable_seed('props', room.key))
    taken = set(prefab.by_id)
    raw_file_id = ua.file_id

    def fresh_id():
        while True:
            value = raw_file_id()
            if value not in taken:
                taken.add(value)
                return value

    def node(name, parent, position=(0, 0, 0), rotation=(0, 0, 0, 1), components=(), euler=(0, 0, 0), children=()):
        go_id, tr_id = fresh_id(), fresh_id()
        comp_docs = [factory(fresh_id(), go_id) for factory in components]
        prefab.add(pd.game_object(go_id, name, [tr_id] + [c.fid for c in comp_docs]),
                   pd.transform(tr_id, go_id, position, rotation, (1, 1, 1), children, parent, euler), *comp_docs)
        return prefab.by_id[tr_id]

    props = node('Props', root.fid)
    lighting = node('Lighting', root.fid)
    prop_children, light_children = [], []
    for material in sorted(mesh_guids):
        mesh_guid = mesh_guids[material]
        t = node(f'{material} (batched)', props.fid, components=(
            lambda fid, go, g=mesh_guid: pd.mesh_filter(fid, go, g),
            lambda fid, go, m=material: pd.mesh_renderer(fid, go, [material_guids[m]], cast_shadows=m not in emissive)))
        prop_children.append(t.fid)
    for placement in room.models:
        slot = node(f'MeshySlot__{placement.key}', props.fid, placement.position, ua.yaw_quaternion(placement.yaw),
                    euler=(0, placement.yaw, 0))
        instance, stripped = pd.gltf_instance(fresh_id(), slot.fid, placement.guid, f'Meshy__{placement.key}',
                                              placement.offset, (placement.scale,) * 3, fresh_id())
        if placement.model_yaw:
            q = ua.yaw_quaternion(placement.model_yaw)
            for axis, value in zip('xyzw', q):
                instance.body = re.sub(r'(propertyPath: m_LocalRotation\.%s\n      value: )[-0-9.e]+' % axis,
                                       lambda m, v=value: m.group(1) + ua.f32(v), instance.body, count=1)
            instance.body = re.sub(r'(propertyPath: m_LocalEulerAnglesHint\.y\n      value: )[-0-9.e]+',
                                   lambda m: m.group(1) + ua.f32(placement.model_yaw), instance.body, count=1)
        prefab.add(instance, stripped)
        prefab.set_children(slot, [stripped.fid])
        prop_children.append(slot.fid)
    for box in room.colliders():
        t = node(box.name, props.fid, box.center, ua.euler_quaternion(box.pitch, box.yaw, 0), euler=(box.pitch, box.yaw, 0),
                 components=(lambda fid, go, s=box.size: pd.box_collider(fid, go, s),))
        prop_children.append(t.fid)
    prefab.set_children(props, prop_children)
    for light in room.lights:
        rgb = mt.hex_rgb(light.color)
        t = node(light.name, lighting.fid, light.position,
                 components=(lambda fid, go, c=rgb, i=light.intensity, r=light.range: pd.point_light(fid, go, c, i, r),))
        light_children.append(t.fid)
    prefab.set_children(lighting, light_children)
    prefab.set_children(root, [props.fid if c == old_props else lighting.fid if c == old_lighting else c for c in order])

    # Gameplay markers: player spawn and one Enemy marker per requested spawn.
    gameplay = groups['Gameplay']
    spawn = child_named(prefab, gameplay, 'PlayerSpawn')
    set_position(spawn, room.player_spawn)
    markers = [prefab.by_id[c] for c in prefab.children(gameplay)
               if not prefab.by_id[c].stripped and prefab.by_id[prefab.by_id[c].ref('m_GameObject')].field('m_Name').startswith('Enemy_')]
    while len(markers) > len(room.enemies):
        gone = markers.pop()
        prefab.set_children(gameplay, [c for c in prefab.children(gameplay) if c != gone.fid])
        prefab.remove_subtree(gone.fid)
    while len(markers) < len(room.enemies):
        t = node('Enemy_' + 'ABCDEFGH'[len(markers)], gameplay.fid)
        prefab.set_children(gameplay, prefab.children(gameplay) + [t.fid])
        markers.append(t)
    for marker, position in zip(markers, room.enemies):
        set_position(marker, position)

    # LiminalRoom component.
    behaviour = next(d for d in prefab.docs if d.cls == 114 and 'LiminalRoom' in d.body)
    body = behaviour.body
    body = re.sub(r'  roomId: .*', lambda m: f'  roomId: {room.key}', body, count=1)
    body = re.sub(r'  displayName: .*', lambda m: '  displayName: ' + ua.yaml_string(room.title), body, count=1)
    body = re.sub(r'  kind: \d+', lambda m: f'  kind: {KIND[room.kind]}', body, count=1)
    spawns = ''.join(f'\n  - {{fileID: {m.fid}}}' for m in markers) if markers else ' []'
    body = re.sub(r'  enemySpawns:.*?\n  localBounds:', lambda m: f'  enemySpawns:{spawns}\n  localBounds:', body, count=1, flags=re.S)
    notes = ua.yaml_string(room.notes)
    body = re.sub(r"  designNotes: .*\Z", lambda m: f'  designNotes: {notes}\n', body, count=1, flags=re.S)
    behaviour.body = body
    root_go.body = re.sub(r'  m_Name: .*', lambda m: f'  m_Name: {room.key}', root_go.body, count=1)
    path = ROOMS / f'{room.key}.prefab'
    ua.write_text(path, prefab.text())
    guid = ua.ensure_meta(path, ua.prefab_meta)
    return {'prefab': str(path.relative_to(ROOT)), 'guid': guid, 'room_component': behaviour.fid,
            'root_gameobject': root_go.fid, 'root_transform': root.fid, 'new_shell': is_new, 'meshes': mesh_guids}


def vec(v):
    return {'x': round(float(v[0]), 5), 'y': round(float(v[1]), 5), 'z': round(float(v[2]), 5)}


def write_layout(room, material_guids, emissive):
    """Engine-neutral description read by StageConceptLayoutBuilder (Unity JsonUtility format: vectors as {x,y,z})."""
    LAYOUTS.mkdir(parents=True, exist_ok=True)
    ua.ensure_meta(LAYOUTS, ua.folder_meta)
    data = {
        'roomId': room.key, 'displayName': room.title, 'kind': room.kind, 'notes': room.notes,
        'playerSpawn': vec(room.player_spawn), 'enemySpawns': [vec(e) for e in room.enemies],
        'decor': [{'material': m, 'mesh': f'Assets/StageConcepts/Geometry/Variations/{room.key}_{m}.asset',
                   'castShadows': m not in emissive} for m in sorted(room.kit.batches) if len(room.kit.batches[m])],
        'models': [{'key': p.key, 'position': vec(p.position), 'yaw': p.yaw, 'targetSize': vec(p.target),
                    'modelYaw': p.model_yaw, 'scale': round(float(p.scale), 7), 'offset': vec(p.offset)} for p in room.models],
        'colliders': [{'name': b.name, 'center': vec(b.center), 'size': vec(b.size), 'yaw': round(float(b.yaw), 4),
                       'pitch': round(float(b.pitch), 4), 'walkable': b.walkable} for b in room.colliders()],
        'lights': [{'name': l.name, 'position': vec(l.position), 'color': '#' + l.color.lstrip('#'),
                    'intensity': l.intensity, 'range': l.range} for l in room.lights],
    }
    path = LAYOUTS / f'{room.key}.json'
    ua.write_text(path, json.dumps(data, indent=1, ensure_ascii=False) + '\n')
    ua.ensure_meta(path, ua.text_meta)
    return path
