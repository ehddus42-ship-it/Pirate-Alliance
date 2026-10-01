"""Remove the stray floating handle Meshy added next to the katana and install the model for the game.

  python3 Tools/PlayerMelee/clean_katana.py

Input:  Tools/PlayerMelee/Source/sakura_katana/sakura_katana.glb (Meshy output, see meshy_katana.py)
Output: Assets/Characters/Astraia/Resources/AstraiaKatana.glb, loaded by MeleeSlash at runtime.
The model keeps only its widest connected mesh piece (blade, guard, handle and tassel); vertices and textures are
untouched, only the index buffer shrinks. MeleeSlash expects the pommel at glTF (0.95, 0.58, 0) and the tip at
(-0.95, -0.65, 0), which is what this Meshy result has.
"""
import json
import pathlib
import struct

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Tools/PlayerMelee/Source/sakura_katana/sakura_katana.glb'  # written by meshy_katana.py
TARGET = ROOT / 'Assets/Characters/Astraia/Resources/AstraiaKatana.glb'


def main():
    data = bytearray(SOURCE.read_bytes())
    n = struct.unpack('<I', data[12:16])[0]
    gltf = json.loads(data[20:20 + n])
    blob = 28 + n
    prim = gltf['meshes'][0]['primitives'][0]
    pa = gltf['accessors'][prim['attributes']['POSITION']]
    pv = gltf['bufferViews'][pa['bufferView']]
    pos = np.frombuffer(bytes(data[blob + pv.get('byteOffset', 0):][:pa['count'] * 12]), np.float32).reshape(-1, 3)
    ia = gltf['accessors'][prim['indices']]
    iv = gltf['bufferViews'][ia['bufferView']]
    dtype = {5125: np.uint32, 5123: np.uint16}[ia['componentType']]
    start = blob + iv.get('byteOffset', 0) + ia.get('byteOffset', 0)
    idx = np.frombuffer(bytes(data[start:start + ia['count'] * np.dtype(dtype).itemsize]), dtype).reshape(-1, 3)
    parent = list(range(len(pos)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    seen = {}
    for i, p in enumerate(map(tuple, np.round(pos, 5))):
        if p in seen:
            parent[find(i)] = find(seen[p])
        else:
            seen[p] = i
    for a, b, c in idx:
        parent[find(a)] = find(b)
        parent[find(b)] = find(c)
    roots = np.array([find(i) for i in range(len(pos))])
    tri_roots = roots[idx[:, 0]]
    keep = max(np.unique(tri_roots), key=lambda r: np.ptp(pos[roots == r][:, 0]))
    kept = idx[tri_roots == keep]
    raw = kept.astype(dtype).tobytes()
    data[start:start + len(raw)] = raw
    ia['count'] = int(kept.size)
    text = json.dumps(gltf, separators=(',', ':')).encode()
    text += b' ' * (n - len(text))
    data[20:20 + n] = text
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_bytes(bytes(data))
    print(f'kept {len(kept)} of {len(idx)} triangles -> {TARGET.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
