"""Resume-safe image-to-3D boss generation. All five boss assets plus rig use at most 164 credits.

    python Tools/RuinsBoss/meshy_sovereign.py run

MESHY_API_KEY is read from the environment and never persisted. Ambiguous POSTs are not retried.
"""
import argparse
import base64
import hashlib
import time
import meshy_assets as m

KEY = 'storm_sovereign'
CACHE = m.CACHE / KEY
OUT = m.OUT / KEY
STATE_PATH = CACHE / 'image_state.json'
IMAGE = m.ROOT / 'Tools/RuinsBoss/References/storm_sovereign_modeling.png'

def committed_credits():
    total = 0
    for filename in ('state.json', 'motion_state.json', 'image_state.json'):
        for path in m.CACHE.glob('*/' + filename):
            for phase in m.load(path, {}).values():
                if isinstance(phase, dict) and 'reserved_credits' in phase:
                    total += phase.get('consumed_credits', phase['reserved_credits'])
    return total

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['run'])
    parser.parse_args()
    state = m.load(STATE_PATH, {})
    if 'image' not in state:
        if committed_credits() + 30 > 164:
            raise RuntimeError('Total 164-credit cap exceeded; no request sent.')
        payload = dict(image_url='data:image/png;base64,' + base64.b64encode(IMAGE.read_bytes()).decode(),
            ai_model=m.MODEL, model_type='standard', geometry_resolution='standard',
            should_texture=True, enable_pbr=True, texture_resolution='2k',
            should_remesh=True, topology='triangle', target_polycount=14000,
            image_enhancement=False, pose_mode='', target_formats=['glb'])
        m.save(CACHE / 'image_request.json', payload)
        state['image'] = dict(id=None, reserved_credits=30, submission_started_utc=m.now())
        m.save(STATE_PATH, state)
        response = m.api('/v1/image-to-3d', payload)
        state['image']['id'] = response['result']
        m.save(STATE_PATH, state)
        print('image submitted', response['result'], flush=True)
    identifier = state['image']['id']
    if not identifier:
        raise RuntimeError('Previous POST outcome unknown; recover task ID manually before continuing.')
    deadline = time.monotonic() + 3600
    previous = None
    while time.monotonic() < deadline:
        result = m.api('/v1/image-to-3d/' + identifier)
        m.save(CACHE / 'image_result.json', result)
        marker = (result['status'], result.get('progress'))
        if marker != previous:
            print('image', *marker, flush=True)
            previous = marker
        if result['status'] == 'SUCCEEDED':
            break
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError('Image generation failed; no further paid retry is automatic.')
        time.sleep(20)
    else:
        raise TimeoutError('Rerun resumes the same image task.')
    state['image']['consumed_credits'] = result.get('consumed_credits', 30)
    m.save(STATE_PATH, state)
    source = CACHE / (KEY + '_source.glb')
    if not source.exists():
        m.download(result['model_urls']['glb'], source)
    if result.get('thumbnail_url'):
        m.download(result['thumbnail_url'], CACHE / 'thumbnail.png')
    stats = m.glb_stats(source)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / (KEY + '.glb')).write_bytes(source.read_bytes())
    m.save(OUT / 'provenance.json', dict(provider='Meshy AI', api='image-to-3d v1', model=m.MODEL,
        theme='ruins/apocalypse boss', role='Adult feminine living-electricity boss based on the user-provided reference.',
        image_task_id=identifier, created_utc=m.now(), requested_triangles=14000,
        texture_resolution='2K', enable_pbr=True,
        concept_provider='User reference prepared as A-pose with OpenAI built-in image_gen',
        concept_file='Tools/RuinsBoss/References/storm_sovereign_modeling.png',
        source_reference='Tools/RuinsBoss/References/storm_sovereign_user_reference.png',
        concept_sha256=hashlib.sha256(IMAGE.read_bytes()).hexdigest(), actual=stats,
        consumed_credits=state['image']['consumed_credits'],
        review='Pending geometry and textured review.'))
    print('SOVEREIGN_BODY_READY', stats, flush=True)

if __name__ == '__main__':
    main()
