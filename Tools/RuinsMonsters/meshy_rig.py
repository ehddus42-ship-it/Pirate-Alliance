"""Resume-safe Meshy humanoid rig plus three motion clips. Maximum cost: 14 credits per humanoid.

    python Tools/RuinsMonsters/meshy_rig.py run --key penitent_husk
    python Tools/RuinsMonsters/meshy_rig.py run --key mourning_matron

Requires the already-generated penitent_husk refine task. Request/response cache is ignored.
References checked 2026-10-04: /en/api/rigging, /en/api/animation, /en/api/pricing.
"""
import argparse
import json
import time
import struct
import meshy_monsters as m

KEY = 'penitent_husk'
CACHE = m.CACHE / KEY
OUT = m.OUT / KEY
STATE = CACHE / 'motion_state.json'
ACTIONS = [0, 214, 8]
ACTION_NAMES = ['Idle', 'Punch Forward with Both Fists', 'Dead']
PHASES = {'rig': ('/v1/rigging', 5), 'animations': ('/v1/animations', 9)}

def task(phase, payload):
    endpoint, cost = PHASES[phase]
    state = m.load(STATE, {})
    if phase not in state:
        reserved = sum(x.get('consumed_credits', x['reserved_credits']) for x in state.values())
        if reserved + cost > 14:
            raise RuntimeError('The additional 14-credit cap would be exceeded; no request sent.')
        m.save(CACHE / (phase + '_request.json'), payload)
        state[phase] = dict(id=None, reserved_credits=cost, submission_started_utc=m.now())
        m.save(STATE, state)
        result = m.api(endpoint, payload)
        state[phase]['id'] = result['result']
        m.save(STATE, state)
        print(phase, 'submitted', result['result'], flush=True)
    identifier = state[phase]['id']
    if not identifier:
        raise RuntimeError('Previous POST outcome unknown; recover its task ID before continuing.')
    deadline = time.monotonic() + 3600
    previous = None
    while time.monotonic() < deadline:
        result = m.api(endpoint + '/' + identifier)
        m.save(CACHE / (phase + '_result.json'), result)
        marker = (result['status'], result.get('progress'))
        if previous != marker:
            print(phase, *marker, flush=True)
            previous = marker
        if result['status'] == 'SUCCEEDED':
            state[phase]['consumed_credits'] = result.get('consumed_credits', cost)
            m.save(STATE, state)
            return result
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{phase} {result["status"]}; replacement is not automatically submitted.')
        time.sleep(20)
    raise TimeoutError('Rerun resumes this recorded task.')

def fetch(url, basename):
    path = OUT / basename
    if not path.exists():
        m.download(url, path)
    return path

def describe_glb(path):
    blob = path.read_bytes()
    length = struct.unpack_from('<I', blob, 12)[0]
    gltf = json.loads(blob[20:20 + length])
    nodes = gltf.get('nodes', [])
    clips = []
    for clip in gltf.get('animations', []):
        low, high = float('inf'), float('-inf')
        for sampler in clip['samplers']:
            acc = gltf['accessors'][sampler['input']]
            low, high = min(low, acc.get('min', [0])[0]), max(high, acc.get('max', [0])[0])
        clips.append(dict(name=clip.get('name', ''), start=low, end=high, channels=len(clip['channels'])))
    return dict(skins=len(gltf.get('skins', [])), joints=[nodes[i].get('name', str(i)) for s in gltf.get('skins', []) for i in s['joints']], clips=clips)

def main():
    global KEY, CACHE, OUT, STATE, ACTIONS, ACTION_NAMES
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['run'])
    parser.add_argument('--key', choices=['penitent_husk', 'mourning_matron'], default='penitent_husk')
    args = parser.parse_args()
    KEY = args.key
    CACHE, OUT = m.CACHE / KEY, m.OUT / KEY
    STATE = CACHE / 'motion_state.json'
    if KEY == 'mourning_matron':
        ACTIONS = [0, 97, 8]
        ACTION_NAMES = ['Idle', 'Left Slash', 'Dead']
    use_image = KEY == 'mourning_matron' and (CACHE / 'image_state.json').exists()
    generated = m.load(CACHE / ('image_state.json' if use_image else 'state.json'), {})
    refine = m.load(CACHE / ('image_result.json' if use_image else 'refine_result.json'), {})
    if refine.get('status') != 'SUCCEEDED':
        raise RuntimeError('Humanoid refine must finish before any rigging request is sent.')
    rig = task('rig', dict(input_task_id=generated['image' if use_image else 'refine']['id'], height_meters=2.55))
    assets = {}
    for suffix in ('glb', 'fbx'):
        key = 'rigged_character_' + suffix + '_url'
        if rig['result'].get(key):
            path = fetch(rig['result'][key], KEY + '_rig.' + suffix)
            assets[path.name] = describe_glb(path) if suffix == 'glb' else {'format': 'fbx'}
    for key, url in rig['result'].get('basic_animations', {}).items():
        if 'armature' in key or not url:
            continue
        suffix = '.fbx' if '_fbx_' in key else '.glb'
        name = KEY + '_' + key.split('_')[0] + suffix
        path = fetch(url, name)
        assets[name] = describe_glb(path) if suffix == '.glb' else {'format': 'fbx'}
    animations = task('animations', dict(rig_task_id=rig['id'], action_ids=ACTIONS))
    for suffix in ('glb', 'fbx'):
        key = 'animation_' + suffix + '_url'
        if animations['result'].get(key):
            path = fetch(animations['result'][key], KEY + '_motions.' + suffix)
            assets[path.name] = describe_glb(path) if suffix == 'glb' else {'format': 'fbx'}
    m.save(OUT / 'motion_provenance.json', dict(provider='Meshy AI', created_utc=m.now(),
        rig_task_id=rig['id'], animation_task_id=animations['id'], action_ids=ACTIONS,
        input_task_id=generated['image' if use_image else 'refine']['id'],
        input_api='image-to-3d v1' if use_image else 'text-to-3d v2',
        action_names=ACTION_NAMES, height_meters=2.55,
        consumed_credits=rig.get('consumed_credits', 5) + animations.get('consumed_credits', 9), assets=assets))
    print(json.dumps(assets, indent=2), flush=True)

if __name__ == '__main__':
    main()
