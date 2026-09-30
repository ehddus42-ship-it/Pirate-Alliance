"""Static integrity check of the generated Unity assets (run after build.py --unity).

Checks every variation room prefab, stage definition, layout and material: local fileID references,
parent/child symmetry, stripped nested-prefab transforms, external GUIDs, LiminalRoom wiring, meshes and
materials, and that stages reference rooms that exist. Exit code 1 on any problem.
"""
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import prefabdoc as pd  # noqa: E402
import room as rm       # noqa: E402

ROOT = rm.ROOT
problems = []


def guid_index():
    index = {}
    for meta in ROOT.glob('Assets/**/*.meta'):
        m = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8', errors='replace'), re.M)
        if m:
            index[m.group(1)] = meta.with_suffix('')
    for meta in ROOT.glob('Packages/**/*.meta'):
        pass
    return index


GUIDS = guid_index()
EXTERNAL_OK = {'0000000000000000e000000000000000', '0000000000000000f000000000000000'}
PACKAGE_SCRIPTS = {'715df9372183c47e389bb6e19fbc3b52', '933532a4fcc9baf4fa0491de14d08ed7', 'd0353a89b1f911e48b9e16bdc9f2e058'}


def check_prefab(path):
    text = path.read_text(encoding='utf-8')
    prefab = pd.Prefab(text)
    ids = [d.fid for d in prefab.docs]
    if len(ids) != len(set(ids)):
        problems.append(f'{path.name}: duplicate fileIDs')
    local = set(ids)
    for d in prefab.docs:
        for m in re.finditer(r'\{fileID: (-?\d+)(, guid: ([0-9a-f]+), type: \d)?\}', d.body):
            fid, guid = int(m.group(1)), m.group(3)
            if guid:
                if guid not in GUIDS and guid not in EXTERNAL_OK and guid not in PACKAGE_SCRIPTS:
                    problems.append(f'{path.name}: missing external guid {guid} in doc {d.fid}')
            elif fid and fid not in local:
                problems.append(f'{path.name}: dangling local ref {fid} in doc {d.fid} ({d.cls})')
    for t in [d for d in prefab.docs if d.cls == 4 and not d.stripped]:
        for c in prefab.children(t):
            child = prefab.by_id.get(c)
            if child is None:
                problems.append(f'{path.name}: missing child {c}')
                continue
            if child.stripped:
                inst = prefab.by_id.get(child.ref('m_PrefabInstance'))
                parent = re.search(r'm_TransformParent: \{fileID: (-?\d+)\}', inst.body).group(1) if inst else None
                if not inst or int(parent) != t.fid:
                    problems.append(f'{path.name}: stripped child {c} not parented to {t.fid}')
            elif child.ref('m_Father') != t.fid:
                problems.append(f'{path.name}: child {c} father mismatch')
        go = prefab.by_id.get(t.ref('m_GameObject'))
        if go is None or go.cls != 1:
            problems.append(f'{path.name}: transform {t.fid} without GameObject')
        elif f'component: {{fileID: {t.fid}}}' not in go.body:
            problems.append(f'{path.name}: GameObject {go.fid} does not list its transform')
    for go in [d for d in prefab.docs if d.cls == 1]:
        for c in re.findall(r'component: \{fileID: (-?\d+)\}', go.body):
            comp = prefab.by_id.get(int(c))
            if comp is None or comp.ref('m_GameObject') != go.fid:
                problems.append(f'{path.name}: component {c} of {go.field("m_Name")} broken')
    behaviour = [d for d in prefab.docs if d.cls == 114 and 'LiminalRoom' in d.body]
    if len(behaviour) != 1:
        problems.append(f'{path.name}: expected one LiminalRoom')
        return None
    b = behaviour[0]
    for field in ('entry', 'exit', 'playerSpawn'):
        target = prefab.by_id.get(b.ref(field))
        if target is None or target.cls != 4:
            problems.append(f'{path.name}: LiminalRoom.{field} is not a Transform')
    for field in ('entranceGate', 'exitGate'):
        target = prefab.by_id.get(b.ref(field))
        if target is None or target.cls != 1:
            problems.append(f'{path.name}: LiminalRoom.{field} is not a GameObject')
    spawns = [int(x) for x in re.findall(r'- \{fileID: (-?\d+)\}', b.body.split('enemySpawns:')[1].split('localBounds:')[0])]
    for s in spawns:
        if prefab.by_id.get(s) is None or prefab.by_id[s].cls != 4:
            problems.append(f'{path.name}: enemy spawn {s} is not a Transform')
    kind = int(re.search(r'  kind: (\d)', b.body).group(1))
    if kind == 2 and len(spawns) < 3:
        problems.append(f'{path.name}: combat room with {len(spawns)} spawns')
    room_id = re.search(r'  roomId: (\S+)', b.body).group(1)
    if room_id != path.stem:
        problems.append(f'{path.name}: roomId {room_id} != file name')
    return {'room': path.stem, 'docs': len(prefab.docs), 'guid': rm_guid(path), 'component': b.fid, 'kind': kind, 'spawns': len(spawns),
            'meshy_instances': sum(1 for d in prefab.docs if d.cls == 1001)}


def rm_guid(path):
    return re.search(r'guid: ([0-9a-f]+)', pathlib.Path(str(path) + '.meta').read_text()).group(1)


def check_stage(path, rooms):
    text = path.read_text(encoding='utf-8')
    refs = re.findall(r'\{fileID: (\d+), guid: ([0-9a-f]+), type: 3\}', text)
    by_guid = {r['guid']: r for r in rooms}
    for fid, guid in refs:
        if guid in (GUIDS and '0312d99c176381a4fa77ae2f61fb7212',):
            continue
        info = by_guid.get(guid)
        if not info:
            problems.append(f'{path.name}: references unknown room guid {guid}')
        elif int(fid) != info['component']:
            problems.append(f'{path.name}: wrong LiminalRoom fileID for {info["room"]}')
    pool = len(re.findall(r'\n  - \{fileID', text.split('roomPool:')[1].split('middleRoomCount')[0]))
    return {'stage': path.stem, 'room_refs': len(refs), 'pool': pool}


def main():
    rooms = []
    for path in sorted((ROOT / 'Assets/StageConcepts/Prefabs/Rooms').glob('*.prefab')):
        info = check_prefab(path)
        if info:
            rooms.append(info)
    stages = [check_stage(p, rooms) for p in sorted((ROOT / 'Assets/StageConcepts/Stages').glob('*.asset'))]
    for mat in sorted((ROOT / 'Assets/StageConcepts/Materials').glob('*.mat')):
        text = mat.read_text(encoding='utf-8')
        for guid in re.findall(r'guid: ([0-9a-f]{32})', text):
            if guid not in GUIDS and guid not in PACKAGE_SCRIPTS:
                problems.append(f'{mat.name}: missing texture guid {guid}')
        emissive = re.search(r'- _EmissionColor: \{r: ([-0-9.e]+), g: ([-0-9.e]+), b: ([-0-9.e]+)', text)
        glowing = emissive and sum(float(x) for x in emissive.groups()) > 0.01
        has_kw = '_EMISSION' in text.split('m_ValidKeywords:')[1].split('m_InvalidKeywords')[0]
        if glowing != has_kw:
            problems.append(f'{mat.name}: emission colour and _EMISSION keyword disagree')
        if glowing and 'm_LightmapFlags: 2' not in text and 'm_LightmapFlags: 1' not in text:
            problems.append(f'{mat.name}: emissive material without an emissive GI flag (URP would strip _EMISSION)')
    for layout in sorted((ROOT / 'Assets/StageConcepts/Layouts').glob('*.json')):
        data = json.loads(layout.read_text(encoding='utf-8'))
        for entry in data['decor']:
            if not (ROOT / entry['mesh']).exists():
                problems.append(f'{layout.name}: missing mesh {entry["mesh"]}')
        for m in data['models']:
            if not (rm.MESHY / m['key'] / f'{m["key"]}.glb.meta').exists():
                problems.append(f'{layout.name}: model {m["key"]} has no .meta')
    missing_meta = [str(p.relative_to(ROOT)) for p in (ROOT / 'Assets/StageConcepts').rglob('*')
                    if not p.name.endswith('.meta') and not pathlib.Path(str(p) + '.meta').exists()]
    for p in missing_meta:
        problems.append(f'missing .meta: {p}')
    print(json.dumps({'rooms': len(rooms), 'stages': stages, 'meshy_instances': sum(r['meshy_instances'] for r in rooms)}, ensure_ascii=False))
    for p in problems[:80]:
        print('PROBLEM', p)
    print('problems:', len(problems))
    sys.exit(1 if problems else 0)


if __name__ == '__main__':
    main()
