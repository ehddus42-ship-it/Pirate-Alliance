"""Turn the Meshy models of the support skill into true voxel models.

Meshy text-to-3D gives the right shapes and colors (a molten meteor, a 1UP sign with a heart, a chomper) but smooth,
faceted surfaces. This samples each Meshy mesh and its base-color texture into a cube grid, fills the inside, snaps the
colors to a small pixel-art palette and writes a GLB of exposed cube faces only, with a nearest-filtered palette texture
and an emissive palette for the glowing parts.

  python3 Tools/SupportSkill/voxelize.py            # all three
  python3 Tools/SupportSkill/voxelize.py voxel_1up

Input:  Assets/Liminal/Art/SupportSkill/<key>/<key>.glb        (Meshy output, kept unchanged)
Output: Assets/Liminal/Resources/SupportSkill/<key>.glb         (loaded by SupportCharacterSkill at runtime)
        Documentation/Liminal/SupportSkill/<key>_voxels.png    (front and top views for review)
"""
import collections
import io
import json
import pathlib
import struct
import sys

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / 'Assets/Liminal/Art/SupportSkill'
OUT = ROOT / 'Assets/Liminal/Resources/SupportSkill'
DOC = ROOT / 'Documentation/Liminal/SupportSkill'

# key: voxels along the largest side, palette size, emissive rule, emission strength
SETTINGS = {
    'voxel_meteor': (18, 12, 'lava', 2.2),
    'voxel_1up': (34, 16, 'all', 0.8),
    'voxel_chomper': (16, 14, 'bright', 0.6),
}
FACES = [  # direction, 4 corners (unit cube, counter-clockwise seen from outside)
    ((1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
    ((-1, 0, 0), [(0, 0, 1), (0, 1, 1), (0, 1, 0), (0, 0, 0)]),
    ((0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
    ((0, -1, 0), [(0, 0, 1), (0, 0, 0), (1, 0, 0), (1, 0, 1)]),
    ((0, 0, 1), [(1, 0, 1), (1, 1, 1), (0, 1, 1), (0, 0, 1)]),
    ((0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)]),
]


def read_glb(path):
    data = path.read_bytes()
    length = struct.unpack('<I', data[12:16])[0]
    gltf = json.loads(data[20:20 + length])
    blob = data[28 + length:]

    def accessor(index):
        a = gltf['accessors'][index]
        view = gltf['bufferViews'][a['bufferView']]
        count = a['count']
        width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
        dtype = {5126: np.float32, 5125: np.uint32, 5123: np.uint16}[a['componentType']]
        start = view.get('byteOffset', 0) + a.get('byteOffset', 0)
        return np.frombuffer(blob, dtype, count * width, start).reshape(count, width) if width > 1 else \
            np.frombuffer(blob, dtype, count, start)

    prim = gltf['meshes'][0]['primitives'][0]
    positions = accessor(prim['attributes']['POSITION']).astype(np.float64)
    uvs = accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64)
    indices = accessor(prim['indices']).astype(np.int64).reshape(-1, 3)
    material = gltf['materials'][prim['material']]
    image_index = gltf['textures'][material['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
    view = gltf['bufferViews'][gltf['images'][image_index]['bufferView']]
    image = Image.open(io.BytesIO(blob[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']])).convert('RGB')
    return positions, uvs, indices, np.asarray(image, np.float64) / 255


def voxelize(positions, uvs, indices, texture, resolution):
    low, high = positions.min(0), positions.max(0)
    size = (high - low).max() / resolution
    dims = np.ceil((high - low) / size).astype(int) + 1
    colors = np.zeros(tuple(dims) + (3,))
    counts = np.zeros(tuple(dims))
    th, tw = texture.shape[:2]
    for tri in indices:
        p, t = positions[tri], uvs[tri]
        longest = max(np.linalg.norm(p[1] - p[0]), np.linalg.norm(p[2] - p[1]), np.linalg.norm(p[0] - p[2]))
        steps = max(1, int(np.ceil(longest / (size * .35))))
        ii, jj = np.meshgrid(np.arange(steps + 1), np.arange(steps + 1))
        keep = ii + jj <= steps
        a, b = ii[keep] / steps, jj[keep] / steps
        c = 1 - a - b
        pts = np.outer(c, p[0]) + np.outer(a, p[1]) + np.outer(b, p[2])
        uv = np.outer(c, t[0]) + np.outer(a, t[1]) + np.outer(b, t[2])
        cells = np.clip(((pts - low) / size).astype(int), 0, dims - 1)
        px = np.clip((uv[:, 0] % 1) * tw, 0, tw - 1).astype(int)
        py = np.clip((uv[:, 1] % 1) * th, 0, th - 1).astype(int)
        np.add.at(colors, (cells[:, 0], cells[:, 1], cells[:, 2]), texture[py, px])
        np.add.at(counts, (cells[:, 0], cells[:, 1], cells[:, 2]), 1)
    surface = counts > 0
    colors[surface] /= counts[surface][:, None]
    # Everything not reachable from outside is solid (hidden interior cubes are never drawn).
    padded = np.pad(surface, 1)
    outside = np.zeros_like(padded)
    queue = collections.deque([(0, 0, 0)])
    outside[0, 0, 0] = True
    while queue:
        x, y, z = queue.popleft()
        for dx, dy, dz in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
            n = (x + dx, y + dy, z + dz)
            if all(0 <= n[i] < padded.shape[i] for i in range(3)) and not outside[n] and not padded[n]:
                outside[n] = True
                queue.append(n)
    solid = ~outside[1:-1, 1:-1, 1:-1]
    return solid, surface, colors, low, size


def palette(colors, surface, count):
    """Snap surface colors to a small saturated palette (pixel-art look)."""
    samples = colors[surface]
    hsv = np.asarray(Image.fromarray((samples[None] * 255).astype(np.uint8)).convert('HSV'), np.float64)[0]
    hsv[:, 1] = np.clip(hsv[:, 1] * 1.25, 0, 255)
    hsv[:, 2] = np.clip(hsv[:, 2] * 1.08, 0, 255)
    boosted = np.asarray(Image.fromarray(hsv[None].astype(np.uint8), 'HSV').convert('RGB'))[0]
    quantized = Image.fromarray(boosted[None]).quantize(count, method=Image.Quantize.MEDIANCUT)
    lut = np.array(quantized.getpalette()[:count * 3]).reshape(-1, 3) / 255
    labels = np.asarray(quantized)[0]
    index = np.full(surface.shape, -1)
    index[surface] = labels
    return lut, index


def lava_veins(lut, index):
    """The Meshy meteor is mostly dark rock; carve glowing molten veins through its surface voxels."""
    lut = np.vstack([lut, [[1.0, .42, .06], [1.0, .78, .22]]])
    hot, core = len(lut) - 2, len(lut) - 1
    for x, y, z in zip(*np.nonzero(index >= 0)):
        v = np.sin(x * 1.3 + y * .7) + np.sin(y * 1.1 - z * .9) + np.sin(z * 1.7 + x * .45)
        if abs(v) < .28:
            index[x, y, z] = core
        elif abs(v) < .55:
            index[x, y, z] = hot
    return lut, index


def emissive(lut, rule):
    if rule == 'all':
        return np.ones(len(lut), bool)
    r, g, b = lut.T
    if rule == 'lava':
        return (r > .7) & (r > g + .15) & (b < .45)
    return (r + g + b) / 3 > .55


def build_mesh(solid, index, low, size):
    dims = solid.shape
    # Interior cubes take the nearest surface color (only needed where a face is exposed).
    filled = index.copy()
    for x, y, z in zip(*np.nonzero(solid & (index < 0))):
        best, dist = 0, 1e9
        near = np.argwhere(index[max(0, x - 2):x + 3, max(0, y - 2):y + 3, max(0, z - 2):z + 3] >= 0)
        for n in near:
            d = ((n - 2) ** 2).sum()
            if d < dist:
                dist, best = d, index[max(0, x - 2) + n[0], max(0, y - 2) + n[1], max(0, z - 2) + n[2]]
        filled[x, y, z] = best
    positions, normals, uvs, tris = [], [], [], []
    columns = 16
    for x, y, z in zip(*np.nonzero(solid)):
        for (dx, dy, dz), corners in FACES:
            n = (x + dx, y + dy, z + dz)
            if all(0 <= n[i] < dims[i] for i in range(3)) and solid[n]:
                continue
            base = len(positions)
            for cx, cy, cz in corners:
                positions.append(low + np.array([x + cx, y + cy, z + cz]) * size)
                normals.append((dx, dy, dz))
                c = filled[x, y, z]
                uvs.append(((c % columns + .5) / columns, (c // columns + .5) / columns))
            tris += [base, base + 1, base + 2, base, base + 2, base + 3]
    positions = np.array(positions, np.float32)
    positions -= (positions.min(0) + positions.max(0)) / 2
    return positions, np.array(normals, np.float32), np.array(uvs, np.float32), np.array(tris, np.uint32)


def png(pixels):
    buffer = io.BytesIO()
    Image.fromarray(pixels).save(buffer, 'PNG')
    return buffer.getvalue()


def write_glb(path, positions, normals, uvs, tris, lut, glow, strength):
    columns = 16
    base = np.zeros((columns, columns, 3), np.uint8)
    emit = np.zeros((columns, columns, 3), np.uint8)
    for i, color in enumerate(lut):
        base[i // columns, i % columns] = (color * 255).astype(np.uint8)
        if glow[i]:
            emit[i // columns, i % columns] = (color * 255).astype(np.uint8)
    blobs = [tris.tobytes(), positions.tobytes(), normals.tobytes(), uvs.tobytes(), png(base), png(emit)]
    views, offset, binary = [], 0, b''
    for i, blob in enumerate(blobs):
        view = {'buffer': 0, 'byteOffset': offset, 'byteLength': len(blob)}
        if i < 4:
            view['target'] = 34963 if i == 0 else 34962
        views.append(view)
        binary += blob + b'\0' * (-len(blob) % 4)
        offset = len(binary)
    gltf = {
        'asset': {'version': '2.0', 'generator': 'Pirate-Alliance voxelize.py (from Meshy text-to-3D)'},
        'extensionsUsed': ['KHR_materials_emissive_strength'],
        'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'mesh': 0, 'name': path.stem}],
        'meshes': [{'name': path.stem, 'primitives': [{'attributes': {'POSITION': 1, 'NORMAL': 2, 'TEXCOORD_0': 3},
                                                       'indices': 0, 'material': 0}]}],
        'materials': [{'name': path.stem + '_palette', 'pbrMetallicRoughness': {
            'baseColorTexture': {'index': 0}, 'metallicFactor': 0.0, 'roughnessFactor': 0.75},
            'emissiveTexture': {'index': 1}, 'emissiveFactor': [1.0, 1.0, 1.0],
            'extensions': {'KHR_materials_emissive_strength': {'emissiveStrength': strength}}}],
        'samplers': [{'magFilter': 9728, 'minFilter': 9728, 'wrapS': 33071, 'wrapT': 33071}],
        'images': [{'bufferView': 4, 'mimeType': 'image/png'}, {'bufferView': 5, 'mimeType': 'image/png'}],
        'textures': [{'sampler': 0, 'source': 0}, {'sampler': 0, 'source': 1}],
        'accessors': [
            {'bufferView': 0, 'componentType': 5125, 'count': len(tris), 'type': 'SCALAR'},
            {'bufferView': 1, 'componentType': 5126, 'count': len(positions), 'type': 'VEC3',
             'min': positions.min(0).tolist(), 'max': positions.max(0).tolist()},
            {'bufferView': 2, 'componentType': 5126, 'count': len(normals), 'type': 'VEC3'},
            {'bufferView': 3, 'componentType': 5126, 'count': len(uvs), 'type': 'VEC2'},
        ],
        'bufferViews': views, 'buffers': [{'byteLength': len(binary)}],
    }
    text = json.dumps(gltf, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, total) + struct.pack('<II', len(text), 0x4E4F534A) + text +
                     struct.pack('<II', len(binary), 0x004E4942) + binary)


def review(path, solid, filled_colors):
    """Front (seen from glTF -Z, the side the game shows) and top orthographic views, one pixel block per voxel."""
    dims = solid.shape
    views = []
    for axis, flip in ((2, False), (1, True)):
        w, h = (dims[0], dims[1]) if axis == 2 else (dims[0], dims[2])
        img = np.full((h, w, 3), 24, np.uint8)
        for u in range(w):
            for v in range(h):
                line = range(dims[axis]) if not flip else reversed(range(dims[axis]))
                for d in line:
                    cell = (u, v, d) if axis == 2 else (u, d, v)
                    if solid[cell]:
                        img[h - 1 - v if axis == 2 else v, w - 1 - u if axis == 2 else u] = (filled_colors[cell] * 255).astype(np.uint8)
                        break
        views.append(Image.fromarray(img).resize((w * 12, h * 12), Image.NEAREST))
    sheet = Image.new('RGB', (sum(v.width for v in views) + 24, max(v.height for v in views)), (24, 24, 24))
    x = 0
    for v in views:
        sheet.paste(v, (x, 0))
        x += v.width + 24
    sheet.save(path)


def main(keys):
    OUT.mkdir(parents=True, exist_ok=True)
    DOC.mkdir(parents=True, exist_ok=True)
    for key in keys:
        resolution, count, rule, strength = SETTINGS[key]
        positions, uvs, indices, texture = read_glb(SRC / key / f'{key}.glb')
        solid, surface, colors, low, size = voxelize(positions, uvs, indices, texture, resolution)
        lut, index = palette(colors, surface, count)
        if key == 'voxel_meteor':
            lut, index = lava_veins(lut, index)
        if key == 'voxel_1up':
            # Texture averaging dulls the small pixel heart; keep it a clear arcade pink.
            for i, (r, g, b) in enumerate(lut):
                if r > g:
                    lut[i] = (1.0, .3, .5) if r + g + b > .9 else (.75, .12, .3)
        glow = emissive(lut, rule)
        mesh = build_mesh(solid, index, low, size)
        write_glb(OUT / f'{key}.glb', *mesh, lut, glow, strength)
        shown = np.zeros(solid.shape + (3,))
        shown[index >= 0] = lut[index[index >= 0]]
        interior = solid & (index < 0)
        shown[interior] = .3
        review(DOC / f'{key}_voxels.png', solid, shown)
        print(f'{key}: grid {solid.shape}, {int(solid.sum())} voxels, {len(mesh[3]) // 3} triangles, '
              f'{len(lut)} colors ({int(glow.sum())} emissive)')


if __name__ == '__main__':
    main(sys.argv[1:] or list(SETTINGS))
