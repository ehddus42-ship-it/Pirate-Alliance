"""Generate five original apocalypse dungeon monsters with Meshy AI.

    python Tools/RuinsMonsters/meshy_monsters.py plan
    python Tools/RuinsMonsters/meshy_monsters.py run --budget 150

Only MESHY_API_KEY from the environment is used. Source responses and downloaded
originals are ignored. A persisted submit marker prevents accidental duplication
after ambiguous POST outcomes. Run resumes recorded task IDs without recharging.
API reference checked 2026-10-04: https://docs.meshy.ai/en/api/text-to-3d
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
CACHE = ROOT / 'Tools/RuinsMonsters/Source'
OUT = ROOT / 'Assets/RuinsMonsters/Art/Meshy'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
PHASE_COST = {'preview': 20, 'refine': 10}
LOCK = threading.Lock()
STYLE = (' Single isolated original game asset, complete visible object, upright centered, front faces +Z.'
         ' No floor, pedestal, letters or logos. Rough welded industrial salvage, large readable shapes,'
         ' angular worn armor, grounded dark post-apocalypse, no neon or toy styling.')
ASSETS = {
    'mourning_matron': {
        'triangles': 12000,
        'pose_mode': 'a-pose',
        'prompt': ('Tall gaunt adult female cyborg, full body A pose.'
                   ' Eerie pale humanlike synthetic face, angular porcelain half-mask, closed mouth,'
                   ' hollow dark eyes, long black cable hair. Narrow shoulders with asymmetrical worn armor.'
                   ' Solid modest chest armor, deeply open mechanical abdomen and rib cage exposing pistons'
                   ' and gears, no gore. Very long articulated claw fingers, arms and hands separated from'
                   ' torso, two long legs. Motherlike menace, no skull helmet, no weapons, no sexual'
                   ' emphasis, no cables bridging limbs.'),
        'texture_prompt': ('Pale aged porcelain synthetic human face, black oily cable hair, small dim amber'
                           ' eyes, weathered charcoal and faded olive industrial armor with dirty ivory'
                           ' patches, rust seams and brass abdominal pistons inside deep dark rib cage.'
                           ' Long dark gunmetal claw fingers, gray cloth joints, tarnished heavy machinery.'
                           ' Modest solid chest plate, no exposed breast or cleavage, no text, logos, neon'
                           ' or blood. Rough matte PBR steel, ceramic and rubber.'),
        'role': 'Additional eerie female-shaped cyborg; skeletal rig for mechanical claw lunge attacks.',
    },
    'scrap_bulwark': {
        'triangles': 12000,
        'prompt': ('Heavy unmanned industrial siege automaton, low broad compact tank chassis on two oversized'
                   ' caterpillar tracks, tall asymmetric welded slab armor upper body, short heavy forward'
                   ' cannon muzzle low on front, wedge ram bumper, offset narrow amber optical slit above'
                   ' cannon, exposed hydraulic struts, twin stub exhaust pipes at rear, bolted patch plates.'
                   ' Mechanical vehicle monster without human torso, arms or legs.'),
        'texture_prompt': ('Weathered charcoal and faded olive salvaged steel, dirty ivory replacement armor'
                           ' plate, orange brown rust at welds, exposed gunmetal and brass hydraulics, oily'
                           ' dark metal tracks, small amber lens weak point. Matte rough metal, chipped paint'
                           ' and soot. No markings, text, logos, neon, camouflage pattern or bright toy colors.'),
        'role': 'Slow siege automaton; hull recoil, track gait and cannon attacks are animated in Unity.',
    },
    'penitent_husk': {
        'triangles': 12000,
        'pose_mode': 'a-pose',
        'prompt': ('Unsettling tall gaunt adult humanoid cyborg, full body in neutral A pose with separate'
                   ' arms and hands away from torso, two clearly separated long legs and heavy narrow feet.'
                   ' Small blind metal visor face, hunched upper back mechanical spine cage, long oversized'
                   ' mechanical forearms with articulated industrial hands, asymmetrical rusty exoskeleton'
                   ' and riblike external chest braces, body wrapped in tattered gray cloth. No exposed gore,'
                   ' no weapons, no dangling cables between limbs; recognizable biped proportions for rigging.'),
        'texture_prompt': ('Weathered charcoal exoskeleton, dirty ivory face plate with tiny amber visor slit,'
                           ' pale gray stained rough cloth wraps covering body, faded olive shoulder patches,'
                           ' rusty worn gunmetal forearms, brass joint bearings and orange brown rust.'
                           ' Grim industrial salvage, matte PBR metal and fabric, no blood, flesh, symbols,'
                           ' lettering, logos, neon or glossy toy surfaces.'),
        'role': 'Gaunt melee humanoid; textured A-pose for skeletal rig and animated lunging swipes.',
    },
    'carrion_drone': {
        'triangles': 9000,
        'prompt': ('Compact angular unmanned military salvage hover drone, two large vertical ducted fan'
                   ' housings on left and right joined to armored central fuselage, visible heavy rotor'
                   ' blades inside ducts, asymmetric belly mounted short spike shaped projector cannon,'
                   ' forward armored narrow visor sensor slit, rear exposed engine piping, offset antenna'
                   ' stub, overlapping bolted plates. Wide aggressive silhouette, no legs or humanoid parts,'
                   ' no quadcopter, no tentacles, no smooth consumer electronics.'),
        'texture_prompt': ('Rough faded olive and charcoal armored steel with dirty ivory salvage patches,'
                           ' chipped paint, rusty bolt edges, exposed gunmetal fan blades and brass pipework,'
                           ' small amber visor and projector lens, soot around turbines, oily dark underside.'
                           ' Realistic weathered PBR materials, no text, logos, neon, toy colors or decals.'),
        'role': 'Ranged hovering drone; banked flight, rotor motion and aimed darts animated in Unity.',
    },
    'ossuary_medusa': {
        'triangles': 10000,
        'prompt': ('Body only of an original floating octopus-like industrial machine. Squat angular armored'
                   ' bell buoy, asymmetric shell with sloping rusty plates,'
                   ' offset vertical amber optical slit on front, raised vent ridge, exposed'
                   ' side piping. Overlapping segmented collar around bottom with eight round downward'
                   ' facing cable sockets around underside. No tentacles, arms, legs,'
                   ' wires or appendages below sockets. No smooth round'
                   ' hemisphere, red button or translucency.'),
        'texture_prompt': ('Rough weathered charcoal and gray olive iron armor, dirty ivory replacement plate,'
                           ' orange brown rust seams, exposed tarnished brass piping and gunmetal cable'
                           ' sockets, dark recessed underside, one narrow vertical amber optic. Chipped'
                           ' industrial PBR surfaces, old heavy salvage. No red button, logos, lettering,'
                           ' symbols, neon, toy colors or organic skin.'),
        'role': 'Floating octopus body; eight articulated mechanical tentacles are assembled and animated in Unity.',
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
    if key == 'mourning_matron' and (folder / 'rejected_text_body').exists():
        raise RuntimeError('The Matron text body was rejected. Resume meshy_matron_image.py; do not restore the rejected body.')
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
        provider='Meshy AI', api='text-to-3d v2', model=MODEL, theme='ruins/apocalypse',
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
    parser.add_argument('--budget', type=int, default=150)
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
