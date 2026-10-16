"""Normalize Meshy forms for procedural creature animation.

Run after optimize_textures.py. Keeps PBR texture payloads and all vertex channels.
Butterfly: rotate the vertical display pose into horizontal flight and provide
Body / LeftWing / RightWing nodes with hinges. Sapling: remove the unwanted base.
The source cache remains unchanged for reproducible processing.
"""
import copy
import json
import struct

import numpy as np

from meshy_monsters import OUT, load, save
from optimize_textures import load_glb

DTYPES = {5121: '<u1', 5123: '<u2', 5125: '<u4', 5126: '<f4'}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def read_accessor(gltf, binary, index):
    accessor = gltf['accessors'][index]
    assert 'sparse' not in accessor
    view = gltf['bufferViews'][accessor['bufferView']]
    dtype = np.dtype(DTYPES[accessor['componentType']])
    width = WIDTHS[accessor['type']]
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    stride = view.get('byteStride', width * dtype.itemsize)
    return np.ndarray((accessor['count'], width), dtype=dtype, buffer=binary,
                      offset=offset, strides=(stride, dtype.itemsize)).copy()


def add_accessor(gltf, binary, values, component_type, value_type, bounds=False):
    while len(binary) % 4:
        binary.append(0)
    values = np.asarray(values, dtype=DTYPES[component_type]).reshape(-1, WIDTHS[value_type])
    payload = values.tobytes()
    view = len(gltf['bufferViews'])
    gltf['bufferViews'].append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(payload)})
    binary.extend(payload)
    accessor = {'bufferView': view, 'componentType': component_type,
                'count': len(values), 'type': value_type}
    if bounds:
        accessor['min'] = values.min(axis=0).tolist()
        accessor['max'] = values.max(axis=0).tolist()
    gltf['accessors'].append(accessor)
    return len(gltf['accessors']) - 1


def write_glb(path, gltf, binary):
    gltf['buffers'][0]['byteLength'] = len(binary)
    while len(binary) % 4:
        binary.append(0)
    payload = json.dumps(gltf, separators=(',', ':')).encode('utf-8')
    payload += b' ' * (-len(payload) % 4)
    blob = struct.pack('<III', 0x46546C67, 2, 28 + len(payload) + len(binary))
    blob += struct.pack('<II', len(payload), 0x4E4F534A) + payload
    blob += struct.pack('<II', len(binary), 0x004E4942) + binary
    pending = path.with_suffix('.glb.tmp')
    pending.write_bytes(blob)
    pending.replace(path)


def require_single_mesh(gltf):
    assert len(gltf['meshes']) == 1
    assert len(gltf['meshes'][0]['primitives']) == 1
    return gltf['meshes'][0]['primitives'][0]


def worm():
    key = 'moonworm'
    path = OUT / key / (key + '.glb')
    record_path = OUT / key / 'provenance.json'
    record = load(record_path, {})
    if record.get('form_preparation'):
        print('ALREADY PREPARED', key)
        return
    gltf, old_binary = load_glb(path)
    binary = bytearray(old_binary)
    primitive = require_single_mesh(gltf)
    positions = read_accessor(gltf, old_binary, primitive['attributes']['POSITION'])
    upper = positions[positions[:, 1] > np.quantile(positions[:, 1], .9)]
    head_sign = 1 if upper[:, 0].mean() > 0 else -1
    for channel in ('POSITION', 'NORMAL', 'TANGENT'):
        if channel not in primitive['attributes']:
            continue
        values = read_accessor(gltf, old_binary, primitive['attributes'][channel])
        transformed = values.copy()
        transformed[:, 0] = -head_sign * values[:, 2]
        transformed[:, 2] = head_sign * values[:, 0]
        primitive['attributes'][channel] = add_accessor(gltf, binary, transformed,
            5126, 'VEC4' if channel == 'TANGENT' else 'VEC3', bounds=channel == 'POSITION')
    write_glb(path, gltf, binary)
    record['unity_front_axis'] = '+Z verified using the raised head of the longitudinal mesh'
    record['form_preparation'] = 'Rotated source X-long body around Y so the raised head is +Z and the tapered tail is -Z.'
    record['rejected_attempts'] = ['First preview/refine: gray armored shell, rejected for not reading as organic earthworm.']
    save(record_path, record)
    print('PREPARED', key, 'head +Z')


def butterfly():
    key = 'lunar_butterfly'
    path = OUT / key / (key + '.glb')
    record_path = OUT / key / 'provenance.json'
    record = load(record_path, {})
    if record.get('form_preparation'):
        print('ALREADY PREPARED', key)
        return
    gltf, old_binary = load_glb(path)
    binary = bytearray(old_binary)
    primitive = require_single_mesh(gltf)
    # Meshy returned a vertical specimen with head +Y and the dorsal surface +Z.
    # Proper rotation (determinant +1): head -> +Z, dorsal surface -> +Y.
    for channel in ('POSITION', 'NORMAL', 'TANGENT'):
        if channel not in primitive['attributes']:
            continue
        index = primitive['attributes'][channel]
        values = read_accessor(gltf, old_binary, index)
        transformed = values.copy()
        transformed[:, 0] = -values[:, 0]
        transformed[:, 1] = values[:, 2]
        transformed[:, 2] = values[:, 1]
        primitive['attributes'][channel] = add_accessor(
            gltf, binary, transformed, 5126, 'VEC4' if channel == 'TANGENT' else 'VEC3',
            bounds=channel == 'POSITION')
    write_glb(path, gltf, binary)

    articulated = copy.deepcopy(gltf)
    art_binary = bytearray(binary)
    positions = read_accessor(gltf, binary, primitive['attributes']['POSITION'])
    triangles = read_accessor(gltf, binary, primitive['indices']).reshape(-1, 3)
    centers = positions[triangles].mean(axis=1)
    # Protect the central thorax, abdomen and antennae. Only the broad outer
    # membrane is assigned to a wing; the root seam sits below the thorax.
    body = (np.abs(centers[:, 0]) < .062) | ((np.abs(centers[:, 0]) < .175) & (centers[:, 2] > .235))
    part_specs = [('Body', body, [0, 0, 0]),
                  ('LeftWing', ~body & (centers[:, 0] > 0), [.05, .015, .09]),
                  ('RightWing', ~body & (centers[:, 0] < 0), [-.05, .015, .09])]
    nodes, meshes, counts = [], [], {}
    for name, selected, pivot in part_specs:
        part = copy.deepcopy(primitive)
        part['attributes']['POSITION'] = add_accessor(articulated, art_binary,
                                                     positions - np.asarray(pivot), 5126, 'VEC3', True)
        part['indices'] = add_accessor(articulated, art_binary, triangles[selected], 5125, 'SCALAR')
        meshes.append({'name': name, 'primitives': [part]})
        nodes.append({'name': name, 'mesh': len(meshes) - 1, 'translation': pivot})
        counts[name] = int(selected.sum())
    assert sum(counts.values()) == len(triangles) and min(counts.values()) > 0
    articulated['nodes'] = nodes
    articulated['meshes'] = meshes
    articulated['scenes'] = [{'name': 'LunarButterflyArticulated', 'nodes': [0, 1, 2]}]
    articulated['scene'] = 0
    art_path = path.with_name(key + '_articulated.glb')
    write_glb(art_path, articulated, art_binary)
    record['unity_front_axis'] = '+Z verified after flight-pose normalization; dorsal surface +Y'
    record['form_preparation'] = 'Rotated vertical specimen into horizontal flight pose; optional articulated GLB preserves all triangles and PBR UVs.'
    record['articulated_model'] = art_path.name
    record['articulated_parts_triangles'] = counts
    record['wing_animation'] = 'Body static; LeftWing and RightWing rotate around their local Z hinge with opposite signs. Start around +/-30 degrees, cap at 65.'
    save(record_path, record)
    print('PREPARED', key, counts)


def sapling():
    key = 'volatile_sapling'
    path = OUT / key / (key + '.glb')
    record_path = OUT / key / 'provenance.json'
    record = load(record_path, {})
    if record.get('form_preparation'):
        print('ALREADY PREPARED', key)
        return
    gltf, old_binary = load_glb(path)
    binary = bytearray(old_binary)
    primitive = require_single_mesh(gltf)
    positions = read_accessor(gltf, old_binary, primitive['attributes']['POSITION'])
    triangles = read_accessor(gltf, old_binary, primitive['indices']).reshape(-1, 3)
    # The generated seedling stands on a thin circular display slab. Remove the
    # lower 7% of source height; small root toes remain as a mobile silhouette.
    cutoff = positions[:, 1].min() + np.ptp(positions[:, 1]) * .07
    retained = triangles[(positions[triangles, 1].mean(axis=1) >= cutoff)]
    used, remap = np.unique(retained, return_inverse=True)
    for channel, index in list(primitive['attributes'].items()):
        accessor = gltf['accessors'][index]
        values = read_accessor(gltf, old_binary, index)[used]
        primitive['attributes'][channel] = add_accessor(gltf, binary, values,
            accessor['componentType'], accessor['type'], bounds=channel == 'POSITION')
    primitive['indices'] = add_accessor(gltf, binary, remap, 5125, 'SCALAR')
    write_glb(path, gltf, binary)
    record['form_preparation'] = 'Removed the unwanted lower display slab from Meshy geometry; retains root feet and original atlas UVs.'
    record['removed_base_triangles'] = len(triangles) - len(retained)
    save(record_path, record)
    print('PREPARED', key, 'removed base triangles', record['removed_base_triangles'])


if __name__ == '__main__':
    worm()
    butterfly()
    sapling()
