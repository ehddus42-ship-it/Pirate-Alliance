"""Write every Unity asset of the variation rooms (called by build.py --unity)."""
import json
import pathlib
import re

import materials as mt
import room as rm
import unityasset as ua
import unitywriter as uw

ROOT = rm.ROOT
STAGES = ROOT / 'Assets/StageConcepts/Stages'
PROFILES = ROOT / 'Assets/StageConcepts/Profiles'
OLD_GEOMETRY = ROOT / 'Assets/StageConcepts/Geometry'
REPORT = ROOT / 'Documentation/StageConcepts/variations-build.json'
START, END = 1, 5
POOL = (2, 3, 4, 6, 7)


def fix_gate_materials():
    """Gate seals were saved with GI flag None, so URP stripped their _EMISSION keyword."""
    for path in sorted((ROOT / 'Assets/StageConcepts/Materials').glob('Gate_*.mat')):
        text = path.read_text(encoding='utf-8')
        text = re.sub(r'  m_ValidKeywords: \[\]', '  m_ValidKeywords:\n  - _EMISSION', text, count=1)
        text = re.sub(r'  m_LightmapFlags: \d+', '  m_LightmapFlags: 2', text, count=1)
        ua.write_text(path, text)


def write_meshy_metas(built):
    keys = sorted({p.key for _, _, r in built for p in r.models})
    created = []
    for key in keys:
        glb = rm.MESHY / key / f'{key}.glb'
        meta = pathlib.Path(str(glb) + '.meta')
        if glb.exists() and not meta.exists():
            ua.write_text(meta, ua.gltf_meta(rm.asset_guid(key)))
            created.append(key)
        record = rm.MESHY / key / 'provenance.json'
        if record.exists() and not pathlib.Path(str(record) + '.meta').exists():
            ua.write_text(pathlib.Path(str(record) + '.meta'), ua.text_meta(ua.new_guid()))
        folder_meta = pathlib.Path(str(rm.MESHY / key) + '.meta')
        if not folder_meta.exists():
            ua.write_text(folder_meta, ua.folder_meta(ua.new_guid()))
    return created


def write_stage(theme_key, rooms):
    path = STAGES / f'{theme_key}.asset'
    text = path.read_text(encoding='utf-8')

    def ref(index):
        info = rooms[index]
        return '{fileID: %d, guid: %s, type: 3}' % (info['room_component'], info['guid'])

    text = re.sub(r'  startRoom: \{[^}]*\}', lambda m: '  startRoom: ' + ref(START), text, count=1)
    text = re.sub(r'  endRoom: \{[^}]*\}', lambda m: '  endRoom: ' + ref(END), text, count=1)
    pool = ''.join('\n  - ' + ref(i) for i in POOL)
    text = re.sub(r'  roomPool:(\n  - \{[^}]*\})*', lambda m: '  roomPool:' + pool, text, count=1)
    text = re.sub(r'  middleRoomCount: \d+', '  middleRoomCount: 3', text, count=1)
    subtitle = '스테이지 1 기반 테마 실험 · 일곱 공간 중 다섯을 지나 출구를 찾아라'
    text = re.sub(r'  subtitle: .*?\n  startRoom:', lambda m: '  subtitle: ' + ua.yaml_string(subtitle) + '\n  startRoom:', text, count=1, flags=re.S)
    ua.write_text(path, text)


def tune_profiles():
    digital = PROFILES / 'Digital.asset'
    text = digital.read_text(encoding='utf-8')
    text = re.sub(r'(m_Name: Bloom\n(?:.*\n)*?  intensity:\n    m_OverrideState: 1\n    m_Value: )[-0-9.]+', r'\g<1>0.35', text, count=1)
    ua.write_text(digital, text)


def remove_old_geometry():
    removed = 0
    for path in sorted(OLD_GEOMETRY.glob('T[0-3]_R[0-4]_*.asset')):
        path.unlink()
        meta = pathlib.Path(str(path) + '.meta')
        if meta.exists():
            meta.unlink()
        removed += 1
    return removed


def write_all(built, reports):
    guids = mt.texture_guids()
    material_guids = {}
    for module in {m for _, m, _ in built}:
        for mat in module.MATERIALS:
            material_guids[mat.name] = mt.write_material(mat, guids)
    fix_gate_materials()
    created = write_meshy_metas(built)
    by_theme = {}
    for theme_key, module, r in built:
        info = uw.write_room(r, material_guids, module.EMISSIVE)
        uw.write_layout(r, material_guids, module.EMISSIVE)
        by_theme.setdefault(theme_key, {})[r.index] = info
        print('wrote', info['prefab'], 'new shell' if info['new_shell'] else '')
    for theme_key, rooms in by_theme.items():
        if all(i in rooms for i in (START, END) + POOL):
            write_stage(theme_key, rooms)
    tune_profiles()
    removed = remove_old_geometry()
    summary = {'rooms': reports, 'meshy_metas_created': created, 'old_geometry_removed': removed,
               'stage_route': {'start': START, 'end': END, 'pool': list(POOL), 'middleRoomCount': 3}}
    ua.write_text(REPORT, json.dumps(summary, indent=1, ensure_ascii=False) + '\n')
    print(f'Meshy metas created: {len(created)}, old geometry assets removed: {removed}')
