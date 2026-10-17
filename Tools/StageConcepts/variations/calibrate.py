"""Render an existing Unity-built concept room through the preview pipeline, for comparison with the
Unity capture in Documentation/StageConcepts/Previews/<Theme>_Gameplay.png. Development aid only."""
import json
import pathlib
import re
import sys

import numpy as np
from PIL import Image

import geom
import materials as mt
import prefabdoc as pd
import preview
import room as rm
import unityasset as ua

ROOT = rm.ROOT


def guid_index():
    index = {}
    for meta in ROOT.glob('Assets/StageConcepts/**/*.meta'):
        try:
            index[ua.read_guid(meta)] = meta.with_suffix('')
        except Exception:
            pass
    return index


def decode_texture_asset(path, out_png):
    text = path.read_text(encoding='utf-8')
    width = int(re.search(r'm_Width: (\d+)', text).group(1))
    height = int(re.search(r'm_Height: (\d+)', text).group(1))
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', text).group(1))[:width * height * 4]
    img = Image.frombytes('RGBA', (width, height), data).transpose(Image.FLIP_TOP_BOTTOM)
    out_png.parent.mkdir(parents=True, exist_ok=True)
    img.convert('RGB').save(out_png)


def parse_material(path):
    whole = path.read_text(encoding='utf-8')
    start = whole.index('--- !u!21 ')
    end = whole.find('--- !u!', start + 10)
    text = whole[start:end if end > 0 else len(whole)]
    def color(name):
        m = re.search(r'- %s: \{r: ([-0-9.e]+), g: ([-0-9.e]+), b: ([-0-9.e]+)' % name, text)
        return [float(m.group(i)) for i in (1, 2, 3)]
    def flt(name):
        return float(re.search(r'- %s: ([-0-9.e]+)' % name, text).group(1))
    def tex(name):
        m = re.search(r'- %s:\n        m_Texture: \{fileID: \d+(?:, guid: ([0-9a-f]+))?.*\n        m_Scale: \{x: ([-0-9.e]+)' % name, text)
        return (m.group(1), float(m.group(2))) if m else (None, 1)
    emission_on = '_EMISSION' in re.search(r'm_ValidKeywords:(.*?)\n  m_InvalidKeywords', text, re.S).group(1)
    return {'name': re.search(r'm_Name: (.*)', text).group(1), 'color': color('_BaseColor'), 'metallic': flt('_Metallic'),
            'smoothness': flt('_Smoothness'), 'base': tex('_BaseMap'), 'bump_tex': tex('_BumpMap'), 'bump': flt('_BumpScale'),
            'emission': color('_EmissionColor') if emission_on else None}


def parse_mesh_asset(path):
    text = path.read_text(encoding='utf-8')
    count = int(re.search(r'm_VertexCount: (\d+)', text).group(1))
    data = np.frombuffer(bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', text).group(1)), dtype='<f4').reshape(count, 12)
    wide = re.search(r'm_IndexFormat: (\d)', text).group(1) == '1'
    idx = np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', text).group(1)), dtype='<u4' if wide else '<u2')
    return data[:, 0:3], data[:, 10:12], idx.reshape(-1, 3)


class ImportedGeometry(geom.Geometry):
    def __init__(self, positions, uvs, tris):
        super().__init__()
        self.positions = [tuple(p) for p in positions]
        self.uvs = [tuple(u) for u in uvs]
        self.triangles = list(tris.reshape(-1))


def import_room(prefab_path, theme):
    guids = guid_index()
    prefab = pd.Prefab(prefab_path.read_text(encoding='utf-8'))
    room = rm.Room(theme, 0, prefab_path.stem, prefab_path.stem, 'Arrival', 1)
    mats = {}
    for go in [d for d in prefab.docs if d.cls == 1]:
        name = go.field('m_Name')
        comps = [prefab.by_id[int(c)] for c in re.findall(r'component: \{fileID: (-?\d+)\}', go.body)]
        transform = next(c for c in comps if c.cls == 4)
        pos = [float(v) for v in re.search(r'm_LocalPosition: \{x: ([-0-9.e]+), y: ([-0-9.e]+), z: ([-0-9.e]+)', transform.body).groups()]
        filt = next((c for c in comps if c.cls == 33), None)
        rend = next((c for c in comps if c.cls == 23), None)
        light = next((c for c in comps if c.cls == 108), None)
        if filt and rend and name.endswith('(batched)'):
            mesh_path = guids[re.search(r'm_Mesh: \{fileID: \d+, guid: ([0-9a-f]+)', filt.body).group(1)]
            mat_path = guids[re.search(r'- \{fileID: 2100000, guid: ([0-9a-f]+)', rend.body).group(1)]
            m = parse_material(mat_path)
            mats[m['name']] = m
            p, uv, t = parse_mesh_asset(mesh_path)
            room.kit.batches[m['name']] = ImportedGeometry(p, uv, t)
        if light:
            c = [float(v) for v in re.search(r'm_Color: \{r: ([-0-9.e]+), g: ([-0-9.e]+), b: ([-0-9.e]+)', light.body).groups()]
            room.lights.append(rm.Light(name, tuple(pos), '%02x%02x%02x' % tuple(round(x * 255) for x in c),
                                        float(light.field('m_Intensity')), float(light.field('m_Range'))))
        if name.startswith('MeshySlot__'):
            rot = [float(v) for v in re.search(r'm_LocalRotation: \{x: ([-0-9.e]+), y: ([-0-9.e]+), z: ([-0-9.e]+), w: ([-0-9.e]+)', transform.body).groups()]
            yaw = np.degrees(2 * np.arctan2(rot[1], rot[3]))
            child = prefab.by_id[prefab.children(transform)[0]]
            inst = prefab.by_id[child.ref('m_PrefabInstance')]
            glb = re.search(r'm_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]+)', inst.body).group(1)
            key = guids[glb].stem
            def mod(path_name):
                return float(re.search(r'propertyPath: %s\n\s+value: ([-0-9.e]+)' % re.escape(path_name), inst.body).group(1))
            p = rm.Placement(key, tuple(pos), (1, 1, 1), yaw, None, 0.0, mod('m_LocalScale.x'),
                             (mod('m_LocalPosition.x'), mod('m_LocalPosition.y'), mod('m_LocalPosition.z')))
            room.models.append(p)
    return room, mats


def main():
    theme_key = sys.argv[1] if len(sys.argv) > 1 else 'Forest'
    theme = ['Forest', 'Digital', 'Ruins', 'Cave'].index(theme_key)
    out = pathlib.Path(sys.argv[2])
    room, mats = import_room(ROOT / f'Assets/StageConcepts/Prefabs/Rooms/{theme_key}_01.prefab', theme)
    guids = guid_index()
    textures, defs = {}, []
    for m in mats.values():
        d = {'name': m['name'], 'color': [mt.srgb_to_linear(c) for c in m['color']], 'metallic': m['metallic'],
             'roughness': 1 - m['smoothness'], 'texture': None, 'tiling': m['base'][1], 'bump': m['bump'], 'doubleSided': False,
             'emission': [mt.srgb_to_linear(c) for c in m['emission']] if m['emission'] else None}
        if m['base'][0]:
            base = guids[m['base'][0]]
            normal = guids[m['bump_tex'][0]]
            for src in (base, normal):
                png = out / 'tex' / (src.stem + '.png')
                if not png.exists():
                    decode_texture_asset(src, png)
            textures[base.stem] = {'base': f'calib/tex/{base.stem}.png', 'normal': f'calib/tex/{normal.stem}.png'}
            d['texture'] = base.stem
        defs.append(d)
    preview.write_decor_glb(room, out / f'{theme_key}_01.glb')
    cams = [preview.camera('unity_gameplay', (0, .6, 13), 24, 55, 35, 36)]
    scene = preview.scene_json([(room, 0.0, f'calib/{theme_key}_01.glb')], preview.environment(theme), cams, defs, textures)
    (out / f'{theme_key}_01.json').write_text(json.dumps(scene))
    print('exported', len(room.models), 'models', len(room.kit.batches), 'batches', len(room.lights), 'lights')


if __name__ == '__main__':
    main()
