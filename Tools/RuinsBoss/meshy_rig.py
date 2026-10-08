"""Resume-safe Meshy boss and biped siege-walker skeletons and motion clips.

    python Tools/RuinsBoss/meshy_rig.py run
    python Tools/RuinsBoss/meshy_rig.py run --key siege_walker

Maximum extra cost 14 credits. No ambiguous POST retry. Cache records every task ID.
References checked 2026-10-04: docs.meshy.ai/en/api/rigging and /en/api/animation.
"""
import argparse
import json
import time
import struct
import meshy_assets as m

KEY = 'storm_sovereign'
CACHE = m.CACHE / KEY
OUT = m.OUT / KEY
STATE = CACHE / 'motion_state.json'
ACTIONS = [0, 125, 8]
ACTION_NAMES = ['Idle', 'Charged Spell Cast', 'Dead']
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
            state[phase]['status'] = result['status']
            if 'consumed_credits' in result:
                state[phase]['consumed_credits'] = result['consumed_credits']
            m.save(STATE, state)
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
    parser.add_argument('--key', choices=['storm_sovereign', 'siege_walker'], default='storm_sovereign')
    args = parser.parse_args()
    KEY = args.key
    CACHE, OUT = m.CACHE / KEY, m.OUT / KEY
    STATE = CACHE / 'motion_state.json'
    use_image = KEY == 'storm_sovereign'
    height = 3.6 if use_image else 4.3
    if not use_image:
        ACTIONS, ACTION_NAMES = [0, 8], ['Idle', 'Dead']
        PHASES['animations'] = ('/v1/animations', 6)
    generated = m.load(CACHE / ('image_state.json' if use_image else 'state.json'), {})
    refine = m.load(CACHE / ('image_result.json' if use_image else 'refine_result.json'), {})
    if refine.get('status') != 'SUCCEEDED':
        raise RuntimeError('Humanoid refine must finish before any rigging request is sent.')
    rig = task('rig', dict(input_task_id=generated['image' if use_image else 'refine']['id'], height_meters=height))
    assets = {}
    for suffix in ('glb', 'fbx'):
        key = 'rigged_character_' + suffix + '_url'
        if rig['result'].get(key):
            path = fetch(rig['result'][key], KEY + '_rig.' + suffix)
            assets[path.name] = describe_glb(path) if suffix == 'glb' else {'format': 'fbx'}
    if not use_image:
        basic = rig['result'].get('basic_animations', {})
        for motion in ('walking', 'running'):
            for suffix in ('glb', 'fbx'):
                url = basic.get(motion + '_' + suffix + '_url')
                if url:
                    path = fetch(url, KEY + '_' + motion + '.' + suffix)
                    assets[path.name] = describe_glb(path) if suffix == 'glb' else {'format': 'fbx'}
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
        action_names=ACTION_NAMES, height_meters=height,
        consumed_credits=rig.get('consumed_credits', 5) + animations.get('consumed_credits', PHASES['animations'][1]), assets=assets))
    print(json.dumps(assets, indent=2), flush=True)

if __name__ == '__main__':
    main()
