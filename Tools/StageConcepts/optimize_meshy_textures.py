"""Repack Meshy GLBs with bounded texture sizes and no unused binary payload.

Geometry, UVs, materials and normal-map bindings remain unchanged. Tangent-space
normal maps are renormalized after reduction. Run only after source generation.
"""
import argparse
import io
import json
import pathlib
import struct

import numpy as np
from PIL import Image

from meshy_assets import CACHE, OUT, PROPS, inspect_glb, save

HERO_PROPS = {'ancient_tree', 'ruined_tower', 'cavern_arch'}


def load_glb(path):
    content = path.read_bytes()
    magic, version, total = struct.unpack_from('<III', content)
    assert magic == 0x46546C67 and version == 2 and total == len(content)
    json_size, json_type = struct.unpack_from('<II', content, 12)
    assert json_type == 0x4E4F534A
    gltf = json.loads(content[20:20 + json_size])
    binary_size, binary_type = struct.unpack_from('<II', content, 20 + json_size)
    assert binary_type == 0x004E4942
    return gltf, content[28 + json_size:28 + json_size + binary_size]


def optimize(key):
    source = CACHE / key / (key + '_source.glb')
    destination = OUT / key / (key + '.glb')
    record = OUT / key / 'provenance.json'
    if not source.exists() or not record.exists():
        return None
    cap = 2048 if key in HERO_PROPS else 1024
    gltf, binary = load_glb(source)
    normal_sources = {gltf['textures'][material['normalTexture']['index']]['source']
                      for material in gltf.get('materials', []) if 'normalTexture' in material}
    replacements = {}
    image_sizes = []
    for index, image in enumerate(gltf.get('images', [])):
        assert 'bufferView' in image, 'Expected embedded image'
        view = gltf['bufferViews'][image['bufferView']]
        offset = view.get('byteOffset', 0)
        pixels = Image.open(io.BytesIO(binary[offset:offset + view['byteLength']]))
        if max(pixels.size) > cap:
            ratio = cap / max(pixels.size)
            size = tuple(max(1, round(d * ratio)) for d in pixels.size)
            pixels = pixels.resize(size, Image.Resampling.LANCZOS)
            if index in normal_sources:
                values = np.asarray(pixels.convert('RGB'), dtype=np.float32) / 127.5 - 1.0
                norms = np.maximum(np.linalg.norm(values, axis=2, keepdims=True), 0.0001)
                values /= norms
                pixels = Image.fromarray(np.round((values + 1.0) * 127.5).clip(0, 255).astype(np.uint8), 'RGB')
        packed = io.BytesIO()
        if image.get('mimeType') == 'image/jpeg' and index not in normal_sources:
            pixels.convert('RGB').save(packed, format='JPEG', quality=93, optimize=True)
        else:
            pixels.save(packed, format='PNG', optimize=True)
            image['mimeType'] = 'image/png'
        replacements[image['bufferView']] = packed.getvalue()
        image_sizes.append({'image': index, 'size': list(pixels.size), 'normal_map': index in normal_sources})
    repacked = bytearray()
    for index, view in enumerate(gltf.get('bufferViews', [])):
        while len(repacked) % 4:
            repacked.append(0)
        offset = view.get('byteOffset', 0)
        data = replacements.get(index, binary[offset:offset + view['byteLength']])
        view['byteOffset'] = len(repacked)
        view['byteLength'] = len(data)
        repacked.extend(data)
    gltf['buffers'][0]['byteLength'] = len(repacked)
    while len(repacked) % 4:
        repacked.append(0)
    json_bytes = json.dumps(gltf, separators=(',', ':')).encode('utf-8')
    json_bytes += b' ' * (-len(json_bytes) % 4)
    content = struct.pack('<III', 0x46546C67, 2, 28 + len(json_bytes) + len(repacked))
    content += struct.pack('<II', len(json_bytes), 0x4E4F534A) + json_bytes
    content += struct.pack('<II', len(repacked), 0x004E4942) + repacked
    # Atomic replacement avoids partially written files during Unity import.
    pending = destination.with_suffix('.glb.tmp')
    pending.write_bytes(content)
    pending.replace(destination)
    provenance = json.loads(record.read_text(encoding='utf-8'))
    provenance['actual'] = inspect_glb(destination)
    provenance['texture_resolution'] = f'{cap // 1024}K'
    provenance['texture_images'] = image_sizes
    provenance['original_bytes'] = source.stat().st_size
    provenance['optimization'] = ('Meshy triangle remesh before PBR texturing; embedded normal maps preserve surface detail. '
                                  f'All textures capped to {cap}px; tangent normals renormalized; binary payload compactly repacked.')
    save(record, provenance)
    print('OPTIMIZED', key, json.dumps(provenance['actual']), 'textures', cap, flush=True)
    return provenance


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--key', choices=list(PROPS))
    args = parser.parse_args()
    for key in [args.key] if args.key else PROPS:
        optimize(key)
