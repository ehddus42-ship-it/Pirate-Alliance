"""Meshy boss-support assets. Run plan, then run --budget 120. Keys resume persisted task IDs.

Four text assets cost at most 120 credits. Image boss and its rig add at most 44.
Only MESHY_API_KEY from environment is used. Never retry an ambiguous POST.
"""
import argparse
import concurrent.futures
import datetime
import json
import os
import pathlib
import struct
import threading
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/RuinsBoss/Source'
OUT = ROOT / 'Assets/RuinsBoss/Art/Meshy'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
PHASE_COST = {'preview': 20, 'refine': 10}
LOCK = threading.Lock()
STYLE = (' Single complete isolated original game asset, no floor or pedestal, all parts visible.'
         ' Rough heavy industrial salvage, worn angular armor, clear action-game silhouette.'
         ' No text, logos, neon or toy styling.')
ASSETS = {
    'iron_ram': {
        'triangles': 12000,
        'prompt': ('Massive low wide unmanned armored battering machine on two huge caterpillar tracks.'
                   ' Heavy sloped bulldozer wedge and short thick twin hydraulic impact pistons at front,'
                   ' squat asymmetric armored superstructure with one recessed amber sensor slit,'
                   ' exposed rear engine block, clustered exhaust pipes, thick welded patch plates,'
                   ' side cable conduits and scarred roll cage. Compact ground-hugging brute silhouette,'
                   ' no human head, no humanoid arms, no tank gun barrel. Front is positive Z.'),
        'texture_prompt': ('Rusty charcoal and faded olive salvaged steel, large dirty ivory welded replacement'
                           ' panels, orange brown rust streaks, black soot, tarnished brass hydraulic rods,'
                           ' dark oily tracks, small amber sensor slit. Heavy rough matte PBR metal,'
                           ' chipped paint, no symbols, writing, logos or colorful lights.'),
        'role': 'Left giant charge machine; rigid body for procedural suspension, recoil and charge motion.',
    },
    'siege_walker': {
        'triangles': 14000,
        'prompt': ('Enormous tall narrow four-legged industrial siege walker, an armored weapon platform'
                   ' held high on four clearly separated thick jointed hydraulic legs with broad metal feet.'
                   ' Upright rectangular furnace-like central armored torso, asymmetric shoulder rocket'
                   ' pod with round launch sockets, opposing thick cannon assembly, small recessed amber'
                   ' optical slit, exposed pistons, hanging short armored conduits, rear boiler exhausts.'
                   ' Rugged asymmetrical military salvage, no humanoid face, no tracked tank base.'
                   ' Front is positive Z.'),
        'texture_prompt': ('Worn gunmetal and charcoal steel, faded dark olive patches, one dirty ivory armor'
                           ' plate, rusted welded seams and rusty feet, brass piston shafts, soot around'
                           ' furnace vents, tiny amber optic, black missile tube recesses. Rough realistic'
                           ' industrial PBR metal. No letters, logos, neon or bright toy colors.'),
        'role': 'Right giant ranged machine; distinct tall four-leg silhouette and rocket launch mount.',
    },
    'missile_turret': {
        'triangles': 7500,
        'prompt': ('Heavy stationary salvaged missile turret, low wide armored turntable pedestal with'
                   ' reinforced mounting feet, three large vertical missile launch tubes clustered on top'
                   ' with open dark circular sockets facing upward, angled rectangular armor around the'
                   ' launcher rack, one side hydraulic support cylinder and exposed rear control box.'
                   ' Tubes empty, no missile inside, no smoke or projectiles. Industrial battlefield'
                   ' machinery, three distinct large upright launch barrels, compact centered silhouette.'),
        'texture_prompt': ('Rough charcoal armored steel, dirty faded olive launch tube casings, worn ivory'
                           ' side plate, rusty bolts, tarnished brass piston, oily dark launch sockets,'
                           ' soot and scratched paint. Heavy weathered PBR metal. No text, logos or decals.'),
        'role': 'Rear three-tube missile artillery emplacement; pivot, reload and firing animated in Unity.',
    },
    'apocalypse_missile': {
        'triangles': 2200,
        'prompt': ('One compact heavy industrial guided missile, long cylindrical steel body, pointed'
                   ' conical armored nose, four short triangular stabilizer fins around rear, one circular'
                   ' recessed rocket exhaust nozzle at tail, a few thick segmented body rings and rivets.'
                   ' One missile only, horizontal length axis, front nose pointing positive Z. No launcher,'
                   ' smoke, fire, stand, cable or extra missiles. Chunky readable projectile silhouette.'),
        'texture_prompt': ('Weathered dirty ivory and charcoal painted steel missile body, dark gunmetal'
                           ' nose and rear fins, faded olive body ring, orange rust scratches, brass seams,'
                           ' black exhaust cavity. Rough PBR industrial metal, no text, logos or symbols.'),
        'role': 'Shared solid missile projectile for siege walker and rear artillery turret.',
    },
}


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def load(path, default):
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else default


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_suffix(path.suffix + '.tmp')
    pending.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
    pending.replace(path)


def log(*values):
    with LOCK:
        print(*values, flush=True)


def api(path, payload=None):
    key = os.environ.get('MESHY_API_KEY')
    if not key:
        raise RuntimeError('MESHY_API_KEY is not set; no request was sent.')
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    request = urllib.request.Request(BASE + path, data=data, headers={
        'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if payload is None and (error.code == 429 or error.code >= 500) and attempt < 4:
                time.sleep(10 + attempt * 10)
                continue
            raise RuntimeError(f'Meshy HTTP {error.code}; inspect the persisted request before retrying.') from None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if payload is None and attempt < 4:
                time.sleep(10 + attempt * 10)
                continue
            raise RuntimeError('Meshy request outcome is unknown; POST is not automatically retried.') from None


def payload_for(key, phase, state):
    spec = ASSETS[key]
    if phase == 'preview':
        return dict(mode='preview', prompt=spec['prompt'] + STYLE, ai_model=MODEL,
                    model_type='standard', geometry_resolution='standard', should_remesh=True,
                    topology='triangle', target_polycount=spec['triangles'],
                    pose_mode=spec.get('pose_mode', ''), target_formats=['glb'])
    return dict(mode='refine', preview_task_id=state['preview']['id'], ai_model=MODEL,
                enable_pbr=True, texture_resolution='2k', texture_prompt=spec['texture_prompt'],
                target_formats=['glb'])


def submit_once(key, phase, state, budget):
    state_path = CACHE / key / 'state.json'
    with LOCK:
        if phase in state:
            if not state[phase].get('id'):
                raise RuntimeError(f'{key} {phase}: previous submission has an unknown outcome; recover its ID manually.')
            return state[phase]['id']
        committed = sum(p.get('consumed_credits', p['reserved_credits'])
                        for asset in ASSETS
                        for p in load(CACHE / asset / 'state.json', {}).values()
                        if isinstance(p, dict) and 'reserved_credits' in p)
        if committed + PHASE_COST[phase] > budget:
            raise RuntimeError(f'Credit cap {budget} reached; no new task was submitted.')
        payload = payload_for(key, phase, state)
        save(CACHE / key / (phase + '_request.json'), payload)
        state[phase] = {'id': None, 'submission_started_utc': now(), 'reserved_credits': PHASE_COST[phase]}
        save(state_path, state)
    result = api('/v2/text-to-3d', payload)
    state[phase]['id'] = result['result']
    save(state_path, state)
    log(key, phase, 'submitted', result['result'])
    return result['result']


def wait_task(key, phase, task_id, state):
    deadline = time.monotonic() + 3600
    last = None
    while time.monotonic() < deadline:
        result = api('/v2/text-to-3d/' + task_id)
        save(CACHE / key / (phase + '_result.json'), result)
        marker = (result['status'], result.get('progress'))
        if marker != last:
            log(key, phase, *marker)
            last = marker
        if result['status'] == 'SUCCEEDED':
            state[phase]['consumed_credits'] = result.get('consumed_credits', PHASE_COST[phase])
            save(CACHE / key / 'state.json', state)
            return result
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{key} {phase} ended with {result["status"]}; no replacement task submitted.')
        time.sleep(20)
    raise TimeoutError(f'{key} {phase}: rerun resumes the same task ID.')


def download(url, path):
    pending = path.with_suffix(path.suffix + '.part')
    urllib.request.urlretrieve(url, pending)
    pending.replace(path)


def glb_stats(path):
    blob = path.read_bytes()
    magic, version, _ = struct.unpack_from('<III', blob)
    if magic != 0x46546C67 or version != 2:
        raise ValueError('Expected a glTF 2.0 binary.')
    length, _ = struct.unpack_from('<II', blob, 12)
    gltf = json.loads(blob[20:20 + length])
    triangles = vertices = 0
    low, high = [float('inf')] * 3, [float('-inf')] * 3
    for node in gltf.get('nodes', []):
        if 'mesh' in node and any(k in node for k in ('translation', 'rotation', 'scale')):
            raise ValueError('Node transforms need manual bounds review.')
        if 'mesh' in node and 'matrix' in node and node['matrix'] != [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]:
            raise ValueError('A non-identity node matrix needs manual bounds review.')
    for mesh in gltf.get('meshes', []):
        for primitive in mesh['primitives']:
            if primitive.get('mode', 4) != 4:
                raise ValueError('Expected triangle primitives.')
            pos = gltf['accessors'][primitive['attributes']['POSITION']]
            vertices += pos['count']
            triangles += (gltf['accessors'][primitive['indices']]['count']
                          if 'indices' in primitive else pos['count']) // 3
            for axis in range(3):
                low[axis], high[axis] = min(low[axis], pos['min'][axis]), max(high[axis], pos['max'][axis])
    return dict(triangles=triangles, vertices=vertices, mesh_count=len(gltf.get('meshes', [])),
                material_count=len(gltf.get('materials', [])), embedded_images=len(gltf.get('images', [])),
                normal_mapped_materials=sum('normalTexture' in m for m in gltf.get('materials', [])),
                unity_bounds_min=[-high[0], low[1], low[2]], unity_bounds_max=[-low[0], high[1], high[2]],
                bytes=len(blob))


def generate(key, budget):
    folder = CACHE / key
    folder.mkdir(parents=True, exist_ok=True)
    destination = OUT / key
    final = destination / (key + '.glb')
    if final.exists() and (destination / 'provenance.json').exists():
        log('EXISTING', key, json.dumps(glb_stats(final)))
        return
    state = load(folder / 'state.json', {})
    preview_id = submit_once(key, 'preview', state, budget)
    preview = wait_task(key, 'preview', preview_id, state)
    refine_id = submit_once(key, 'refine', state, budget)
    refine = wait_task(key, 'refine', refine_id, state)
    source = folder / (key + '_source.glb')
    if not source.exists():
        download(refine['model_urls']['glb'], source)
    thumbnail = folder / 'thumbnail.png'
    if not thumbnail.exists() and refine.get('thumbnail_url'):
        download(refine['thumbnail_url'], thumbnail)
    stats = glb_stats(source)
    destination.mkdir(parents=True, exist_ok=True)
    pending = final.with_suffix('.glb.tmp')
    pending.write_bytes(source.read_bytes())
    pending.replace(final)
    save(destination / 'provenance.json', dict(
        provider='Meshy AI', api='text-to-3d v2', model=MODEL, theme='ruins/apocalypse boss',
        role=ASSETS[key]['role'], prompt=ASSETS[key]['prompt'] + STYLE,
        texture_prompt=ASSETS[key]['texture_prompt'], preview_task_id=preview_id,
        refine_task_id=refine_id, created_utc=now(), requested_triangles=ASSETS[key]['triangles'],
        texture_resolution='2K', enable_pbr=True, actual=stats,
        consumed_credits=preview.get('consumed_credits', 20) + refine.get('consumed_credits', 10),
        review='Pending thumbnail and Unity assembly review.'))
    log('READY', key, json.dumps(stats))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('action', choices=['plan', 'run'])
    parser.add_argument('--key', choices=list(ASSETS), action='append')
    parser.add_argument('--budget', type=int, default=120)
    args = parser.parse_args()
    for key, spec in ASSETS.items():
        if len(spec['prompt'] + STYLE) > 800 or len(spec['texture_prompt']) > 800:
            raise SystemExit(f'{key}: prompt exceeds the documented 800-character maximum.')
    keys = args.key or list(ASSETS)
    if args.action == 'plan':
        print(json.dumps({k: dict(triangles=ASSETS[k]['triangles'],
                                 prompt_length=len(ASSETS[k]['prompt'] + STYLE),
                                 estimated_credits=30) for k in keys}, indent=2))
        return
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        futures = [executor.submit(generate, key, args.budget) for key in keys]
        for future in futures:
            future.result()


if __name__ == '__main__':
    main()
