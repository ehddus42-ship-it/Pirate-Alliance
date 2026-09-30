"""Export rooms for the quarter-view preview renderer (three.js in headless Chromium).

Unity space is left-handed; the preview uses three.js's right-handed space with x mirrored (the same
convention glTFast uses between glTF and Unity), so every image matches what the Unity camera sees.
"""
import json
import math
import pathlib
import struct

import numpy as np

import geom
import materials as mt

MIRROR = np.diag([-1.0, 1.0, 1.0, 1.0])

# Theme atmospheres from StageConceptBuilder.ConfigureEnvironment (Unity sRGB values).
AMBIENT = [(.48, .58, .51), (.34, .43, .56), (.57, .53, .46), (.38, .47, .55)]


def srgb(c):
    return [mt.srgb_to_linear(x) for x in c]


def environment(theme, overrides=None):
    ambient = AMBIENT[theme]
    fog = (.24, .21, .17) if theme == 2 else tuple(a * .22 for a in ambient)
    sun_color = (1, .83, .63) if theme == 2 else (.68, .83, 1) if theme == 0 else (.64, .78, 1)
    sun_intensity = 1.8 if theme == 2 else 2.0 if theme == 0 else 1.65
    fill_color = (.69, .78, 1) if theme == 2 else (.60, .78, 1)
    env = {'ambient': srgb(ambient), 'fog': srgb(fog), 'fogDensity': .005 if theme == 2 else .007,
           # three.js ACESFilmic pre-multiplies exposure by 1/0.6; URP ACES does not.
           'exposure': 2 ** 0.55 * 0.6, 'bloom': .35 if theme == 1 else .3,
           'directional': [
               {'color': srgb(sun_color), 'intensity': sun_intensity, 'euler': (52, -28, 0), 'shadows': True, 'shadowStrength': .55},
               {'color': srgb(fill_color), 'intensity': .6, 'euler': (35, 150, 0), 'shadows': False, 'shadowStrength': 0}]}
    if overrides:
        env.update(overrides)
    for d in env['directional']:
        forward = geom.euler_matrix(*d['euler']) @ np.array([0, 0, 1.0])
        d['forward'] = [-forward[0], forward[1], forward[2]]
    return env


def translation(x, y, z):
    m = np.eye(4)
    m[:3, 3] = (x, y, z)
    return m


def rotation_y(degrees):
    m = np.eye(4)
    m[:3, :3] = geom.yaw_matrix(degrees)
    return m


def scaling(s):
    m = np.eye(4)
    m[0, 0] = m[1, 1] = m[2, 2] = s
    return m


def three(m_unity):
    """Convert a Unity-space transform to the mirrored preview space, as a column-major array."""
    return (MIRROR @ m_unity @ MIRROR).T.reshape(-1).tolist()


def model_matrix_unity(p, room_offset):
    return (translation(0, 0, room_offset) @ translation(*p.position) @ rotation_y(p.yaw)
            @ translation(*p.offset) @ rotation_y(p.model_yaw) @ scaling(p.scale))


def write_decor_glb(room, path):
    """All material batches of a room as one GLB (positions mirrored to glTF convention)."""
    buffers, views, accessors, meshes, nodes, materials = bytearray(), [], [], [], [], []

    def add(data, target):
        nonlocal buffers
        while len(buffers) % 4:
            buffers.append(0)
        views.append({'buffer': 0, 'byteOffset': len(buffers), 'byteLength': len(data), 'target': target})
        buffers.extend(data)
        return len(views) - 1

    for name, g in sorted(room.kit.batches.items()):
        if not len(g):
            continue
        p, n, _, uv, t = g.arrays()
        p = p.astype(np.float32) * np.array([-1, 1, 1], dtype=np.float32)
        n = n.astype(np.float32) * np.array([-1, 1, 1], dtype=np.float32)
        tris = t[:, [0, 2, 1]].astype(np.uint32).reshape(-1)
        base = len(accessors)
        accessors.append({'bufferView': add(p.tobytes(), 34962), 'componentType': 5126, 'count': len(p), 'type': 'VEC3',
                          'min': p.min(axis=0).tolist(), 'max': p.max(axis=0).tolist()})
        accessors.append({'bufferView': add(n.tobytes(), 34962), 'componentType': 5126, 'count': len(n), 'type': 'VEC3'})
        accessors.append({'bufferView': add(uv.astype(np.float32).tobytes(), 34962), 'componentType': 5126, 'count': len(uv), 'type': 'VEC2'})
        accessors.append({'bufferView': add(tris.tobytes(), 34963), 'componentType': 5125, 'count': len(tris), 'type': 'SCALAR'})
        materials.append({'name': name})
        meshes.append({'name': name, 'primitives': [{'attributes': {'POSITION': base, 'NORMAL': base + 1, 'TEXCOORD_0': base + 2},
                                                     'indices': base + 3, 'material': len(materials) - 1}]})
        nodes.append({'mesh': len(meshes) - 1, 'name': name})
    gltf = {'asset': {'version': '2.0', 'generator': 'stage-concept variations preview'}, 'scene': 0,
            'scenes': [{'nodes': list(range(len(nodes)))}], 'nodes': nodes, 'meshes': meshes, 'materials': materials,
            'accessors': accessors, 'bufferViews': views, 'buffers': [{'byteLength': len(buffers)}]}
    body = json.dumps(gltf, separators=(',', ':')).encode()
    body += b' ' * (-len(body) % 4)
    while len(buffers) % 4:
        buffers.append(0)
    blob = struct.pack('<III', 0x46546C67, 2, 28 + len(body) + len(buffers))
    blob += struct.pack('<II', len(body), 0x4E4F534A) + body + struct.pack('<II', len(buffers), 0x004E4942) + bytes(buffers)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(blob)


def camera(name, focus, distance, pitch, yaw, fov):
    r = geom.euler_matrix(pitch, yaw, 0)
    forward = r @ np.array([0, 0, 1.0])
    up = r @ np.array([0, 1.0, 0])
    focus = np.asarray(focus, dtype=float)
    position = focus - forward * distance
    flip = np.array([-1, 1, 1.0])
    return {'name': name, 'fov': fov, 'position': (position * flip).tolist(), 'target': (focus * flip).tolist(),
            'up': (up * flip).tolist()}


def gameplay_cameras(room_offset=0.0, focus_points=((0, 6), (0, 18), (0, 30))):
    cams = []
    for i, (x, z) in enumerate(focus_points):
        cams.append(camera(f'play{i + 1}', (x, 0.6, z + room_offset), 24, 55, 35, 36))
    return cams


def overview_camera(room_offset=0.0, name='overview'):
    return camera(name, (0, 2.5, 18 + room_offset), 54, 48, 32, 44)


def scene_json(rooms, env, cameras, material_defs, textures_url, debug=False):
    out = {'environment': env, 'cameras': cameras, 'materials': material_defs, 'textures': textures_url,
           'lights': [], 'rooms': []}
    center = (min(o for _, o, _ in rooms) + max(o for _, o, _ in rooms)) / 2 + 18.2
    for d in env['directional']:
        d['target'] = [0.0, 0.0, center]
    for room, offset, decor_url in rooms:
        for light in room.lights:
            r, g, b = mt.hex_rgb(light.color)
            x, y, z = light.position
            out['lights'].append({'position': [-x, y, z + offset], 'color': srgb((r, g, b)), 'intensity': light.intensity,
                                  'range': light.range})
        entry = {'decor': decor_url, 'matrix': three(translation(0, 0, offset)), 'models': []}
        for p in room.models:
            entry['models'].append({'url': f'/repo/Assets/StageConcepts/Art/Meshy/{p.key}/{p.key}.glb',
                                    'matrix': three(model_matrix_unity(p, offset))})
        if debug:
            entry['debugBoxes'] = []
            for box in room.colliders():
                m = translation(box.center[0], box.center[1], box.center[2] + offset) @ rotation_y(box.yaw)
                entry['debugBoxes'].append({'size': list(box.size), 'matrix': three(m), 'color': '#ff3355'})
        out['rooms'].append(entry)
    return out
