"""Reproducible, resumable Meshy generation for the four plaza visitors.

python Tools/LobbyExpansion/meshy_lobby_expansion.py concept
python Tools/LobbyExpansion/meshy_lobby_expansion.py build

The existing official concept is a style reference only. Each new person has a
different age, silhouette, outfit and colour role. Paid POSTs are never retried:
an intent marker is persisted before transmission; uncertain results must be
reconciled against the Meshy task list before any subsequent run. Credentials
are read from MESHY_API_KEY and never stored. Signed URLs and source responses
remain in the ignored Source directory. Public provenance contains task IDs.
"""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import datetime
import hashlib
import json
import os
from pathlib import Path
import struct
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/LobbyExpansion/Source'
OUT = ROOT / 'Assets/Liminal/Resources/LiminalLobby'
DOC = ROOT / 'Documentation/Liminal/LobbyExpansion'
REFERENCE = ROOT / 'Tools/LiminalLobby/Reference/official.png'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
CONCEPT_MODEL = 'gpt-image-2'
STYLE = (' Use the supplied image ONLY as a reference for the clean cel-shaded anime game art style, facial'
         ' rendering and detail density. The new person must have the outfit described above, not a business suit.'
         ' ONE adult character, full body including head and soles, front view facing camera, relaxed symmetrical'
         ' A-pose with both arms STRAIGHT and 30 degrees away from the torso, hands at hip level, empty hands,'
         ' fingers separated, both feet flat, legs slightly apart. No bent elbows, no pose, no props in hands.'
         ' Clean white background, no text, no weapons, no frame, no floor shadow, soft even studio lighting.'
         ' Polished stylized action-RPG NPC, readable simple large shapes and carefully placed clothing details.')
CHARACTERS = {
    'field_medic': dict(height=1.64, role='현장 의료요원',
        concept='An original Korean woman field medic in her early thirties at a modern Seoul hunter association.'
        ' Warm attentive face, warm brown eyes, short rounded chestnut bob tucked behind ears, small mint hair clip.'
        ' Compact sturdy proportions. Ivory short medical utility jacket with mint-green shoulder panels and cuffs,'
        ' pale mint high-neck inner shirt, charcoal tapered cargo trousers, clean ivory trainers with mint trim.'
        ' A small mint emergency satchel attached firmly at left hip, discreet geometric mint medical emblem on'
        ' sleeve, tiny silver name badge. Practical friendly professional, modest everyday outfit.',
        actions={'idle':247,'walk':1,'talk':313,'greet':290,'inspect':281,'listen':47}),
    'gate_engineer': dict(height=1.78, role='게이트 정비사',
        concept='An original middle-aged Korean male gate technician, forty-five years old, stocky broad shoulders,'
        ' rounded friendly face with short dark stubble, short slightly messy salt-and-pepper hair, safety goggles'
        ' pushed up on forehead. Burnt orange utility vest with a few light reflective strips over rolled-sleeve'
        ' slate-grey work shirt, grey work trousers with reinforced knees, dark chunky safety boots, compact'
        ' tool pouches attached at both sides of belt, visible empty hands. Warm reliable veteran mechanic at a'
        ' modern Seoul hunter association. Distinct robust silhouette, no hard hat, no weapon.',
        actions={'idle':249,'walk':30,'talk':314,'greet':290,'phone':312,'inspect':276}),
    'rookie_hunter': dict(height=1.72, role='신입 헌터',
        concept='An original young adult Korean male rookie hunter, twenty-two years old, slim lanky build, youthful'
        ' friendly face, tousled dark chestnut hair with a small upward tuft, amber-brown eyes. Teal hooded sports'
        ' jacket with cream zipper and navy side panels over plain white T-shirt, slim dark navy cargo trousers,'
        ' cream and teal high-top sneakers. Large practical dark teal backpack fixed tightly against upper back'
        ' with visible padded shoulder straps and a rolled cream fabric pad at top; no dangling straps.'
        ' A small orange hunter pass on chest. Cheerful earnest modern urban adventure outfit, empty hands.',
        actions={'idle':244,'walk':30,'talk':56,'greet':28,'warmup':326,'breath':31}),
    'veteran_hunter': dict(height=1.75, role='베테랑 헌터',
        concept='An original experienced Korean woman hunter in her late thirties, tall athletic build, confident'
        ' calm angular face, dark grey eyes, short swept-back silver-grey pixie haircut with long asymmetrical'
        ' fringe. Dark burgundy thigh-length open field coat, narrow brass trim, sleeves rolled to forearms, coat'
        ' tails split at front and back for walking, black high-neck undershirt, fitted dark charcoal tactical'
        ' trousers, sturdy burgundy-black ankle boots. Compact brown leather waist pouches and one small shoulder'
        ' armour pad. Worldly composed urban fantasy adventurer, no cape, no weapon, no eyepatch, empty hands.',
        actions={'idle':251,'walk':106,'talk':309,'greet':290,'drink':342,'look':338}),
}


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.tmp')
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')
    tmp.replace(path)


def load(path):
    return json.loads(path.read_text(encoding='utf8')) if path.exists() else {}


def api(path, payload=None):
    key = os.environ.get('MESHY_API_KEY')
    if not key:
        raise RuntimeError('MESHY_API_KEY is not set')
    body = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=body, headers={
        'Authorization':'Bearer ' + key, 'Content-Type':'application/json'})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            # GET retries are safe. A POST rejection or unknown outcome is never retried here.
            if payload is None and (exc.code == 429 or exc.code >= 500) and attempt < 5:
                time.sleep(8 + attempt * 5)
                continue
            raise RuntimeError(f'Meshy HTTP {exc.code} for {path}') from None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if payload is None and attempt < 5:
                time.sleep(8 + attempt * 5)
                continue
            raise RuntimeError(f'Meshy request interrupted for {path}; reconcile task list before retrying POST') from None


def task(name, label, endpoint, payload, estimated_credits):
    folder = CACHE / name
    statepath = folder / 'state.json'
    state = load(statepath)
    fingerprint = hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()
    if label not in state:
        state[label] = dict(status='POST_INTENT', endpoint=endpoint, fingerprint=fingerprint,
                            estimated_credits=estimated_credits,
                            created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        save(statepath, state)
        response = api(endpoint, payload)
        state[label].update(id=response['result'], status='SUBMITTED')
        save(statepath, state)
    entry = state[label]
    if entry.get('fingerprint') != fingerprint:
        raise RuntimeError(f'{name}/{label}: parameters changed; use a reviewed new task label')
    if not entry.get('id'):
        raise RuntimeError(f'{name}/{label}: pending POST_INTENT; reconcile Meshy list before proceeding')
    last = None
    while True:
        result = api(f'{endpoint}/{entry["id"]}')
        save(folder / (label + '_result.json'), result)
        mark = (result.get('status'), result.get('progress'))
        if mark != last:
            print(name, label, *mark, flush=True)
            last = mark
        if result.get('status') == 'SUCCEEDED':
            entry.update(status='SUCCEEDED', consumed_credits=result.get('consumed_credits'))
            save(statepath, state)
            return result
        if result.get('status') in ('FAILED','CANCELED','EXPIRED'):
            entry['status'] = result['status']
            save(statepath, state)
            raise RuntimeError(f'{name}/{label}: {result["status"]}')
        time.sleep(12)


def fetch(url, path):
    if path.exists() and path.stat().st_size > 1024:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.download')
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, tmp)
            tmp.replace(path)
            return
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            if attempt == 4:
                raise RuntimeError(f'Download failed for {path.name}') from None
            time.sleep(5 * (attempt + 1))


def geometry_stats(path):
    """Count the delivered glTF mesh rather than trusting its requested polygon count."""
    data = path.read_bytes()
    magic, version, _ = struct.unpack_from('<III', data)
    if magic != 0x46546C67 or version != 2:
        raise RuntimeError(f'{path.name}: expected glTF 2 binary')
    length, kind = struct.unpack_from('<II', data, 12)
    if kind != 0x4E4F534A:
        raise RuntimeError(f'{path.name}: missing glTF JSON')
    gltf = json.loads(data[20:20+length].decode('utf8'))
    accessors = gltf.get('accessors', [])
    triangles, vertices = 0, 0
    bounds = []
    for mesh in gltf.get('meshes', []):
        for primitive in mesh.get('primitives', []):
            positions = accessors[primitive['attributes']['POSITION']]
            vertices += positions['count']
            if primitive.get('mode', 4) == 4:
                count = accessors[primitive['indices']]['count'] if 'indices' in primitive else positions['count']
                triangles += count // 3
            if 'min' in positions and 'max' in positions:
                bounds.append(dict(min=positions['min'], max=positions['max']))
    return dict(triangles=triangles, vertices=vertices, meshes=len(gltf.get('meshes', [])),
                materials=len(gltf.get('materials', [])), source_bounds=bounds)


def concept(name):
    spec = CHARACTERS[name]
    ref = 'data:image/png;base64,' + base64.b64encode(REFERENCE.read_bytes()).decode()
    result = task(name, 'concept', '/v1/image-to-image', dict(ai_model=CONCEPT_MODEL,
        prompt=spec['concept'] + STYLE, reference_image_urls=[ref], aspect_ratio='3:4'), 12)
    fetch(result['image_urls'][0], CACHE / name / 'concept.png')
    fetch(result['image_urls'][0], DOC / ('characters-' + name + '-concept.png'))


def build(name):
    spec = CHARACTERS[name]
    folder, out = CACHE / name, OUT / name
    state = load(folder / 'state.json')
    source = state.get('concept', {})
    if source.get('status') != 'SUCCEEDED':
        raise RuntimeError(f'{name}: generate and inspect the concept before building')
    model = task(name, 'model', '/v1/image-to-3d', dict(input_task_id=source['id'], ai_model=MODEL,
        pose_mode='a-pose', should_remesh=True, topology='triangle', target_polycount=20000,
        should_texture=True, enable_pbr=False, texture_resolution='2k', target_formats=['glb','fbx']), 30)
    if model.get('thumbnail_url'):
        fetch(model['thumbnail_url'], DOC / ('characters-' + name + '-model.png'))
    if model.get('model_urls', {}).get('glb'):
        fetch(model['model_urls']['glb'], folder / (name + '.glb'))
    texture = (model.get('texture_urls') or [{}])[0].get('base_color')
    if texture:
        fetch(texture, out / (name + '_albedo.png'))
    state = load(folder / 'state.json')
    rig = task(name, 'rig', '/v1/rigging', dict(input_task_id=state['model']['id'],
        height_meters=spec['height']), 5)
    fetch(rig['result']['rigged_character_fbx_url'], out / (name + '.fbx'))
    state = load(folder / 'state.json')
    for label, action in spec['actions'].items():
        result = task(name, 'anim_' + label, '/v1/animations',
            dict(rig_task_id=state['rig']['id'], action_id=action), 3)
        fetch(result['result']['animation_fbx_url'], out / (name + '@' + label + '.fbx'))
        if label in ('phone','drink') and result['result'].get('animation_glb_url'):
            fetch(result['result']['animation_glb_url'], folder / (name + '@' + label + '.glb'))
    state = load(folder / 'state.json')
    save(out / 'provenance.json', dict(provider='Meshy AI', model=MODEL, concept_model=CONCEPT_MODEL,
        role=spec['role'], height_m=spec['height'], target_triangles=20000, texture_resolution='2k',
        concept_prompt=spec['concept'] + STYLE,
        delivered_geometry=geometry_stats(folder / (name + '.glb')),
        reference='Tools/LiminalLobby/Reference/official.png (style only)',
        library_actions=spec['actions'],
        tasks={label:{k:v for k,v in entry.items() if k in ('id','status','consumed_credits','estimated_credits')}
               for label, entry in state.items()},
        created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    print('READY', name, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['concept','build'])
    parser.add_argument('--character', action='append', choices=list(CHARACTERS))
    parser.add_argument('--workers', type=int, default=4)
    args = parser.parse_args()
    fn = concept if args.command == 'concept' else build
    with ThreadPoolExecutor(max_workers=max(1,min(args.workers,4))) as pool:
        futures = [pool.submit(fn, name) for name in args.character or CHARACTERS]
        for future in futures:
            future.result()


if __name__ == '__main__':
    main()
