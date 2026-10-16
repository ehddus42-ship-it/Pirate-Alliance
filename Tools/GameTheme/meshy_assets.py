"""Generate the original retro hardware prop kit for the Game dungeon theme.

Usage: python Tools/GameTheme/meshy_assets.py plan
       python Tools/GameTheme/meshy_assets.py run --dry-run
       python Tools/GameTheme/meshy_assets.py run --workers 4 --budget 360
       python Tools/GameTheme/optimize_textures.py

MESHY_API_KEY is read only from the environment. Source cache and API task responses
are git-ignored. Stored task ids resume paid tasks; ambiguous POST failures are not
retried. The budget covers all recorded attempts. Original unbranded designs only.
"""
import argparse
import concurrent.futures
import datetime
import json
import os
import pathlib
import struct
import sys
import threading
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/GameTheme/Source'
OUT = ROOT / 'Assets/GameTheme/Art/Meshy'
SUMMARY = ROOT / 'Documentation/GameTheme/meshy-assets.json'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'  # Same generator as the existing Ruins/Cave props.
TEXTURE_RESOLUTION = '2k'
# Observed on this account on 2026-09-30: preview 20 + refine 10 credits at standard geometry resolution.
ESTIMATED_CREDITS = 30
MAX_PROMPT = 800

STYLE = ' Single isolated complete environment prop, full object visible, no scene or floor plane, no characters. Clear silhouette from an elevated three-quarter game camera, precise bevelled hard surfaces, large clean forms, no tiny floating pieces, rich PBR normal detail, premium stylized game asset.'

ASSETS = {'game_arcade_cabinet': ('game',
                         'prop',
                         8000,
                         (1.6, 2.8, 1.5),
                         None,
                         'Original upright retro arcade machine, broad sloping control panel with one chunky '
                         'joystick and six large buttons, recessed deep square CRT screen beneath a '
                         'projecting blank marquee, squared side panels and stout tapered base. Clearly '
                         'articulated three-tier silhouette. Beveled block-built voxel architecture, '
                         'precision chamfered hard edges. No lettering, recognizable brands or characters.',
                         'Deep midnight navy enamel casing, warm ivory side panels, electric cyan screen '
                         'with abstract pixel star shapes, raspberry control deck accents, amber large '
                         'buttons, brushed aluminium edge trim. Light credible edge wear, screws, inset '
                         'vents, rich PBR detail.'),
 'game_racing_cockpit': ('game',
                         'landmark',
                         12000,
                         (4.8, 3.3, 4.5),
                         'x',
                         'Two connected arcade racing game machines side by side. Each machine has one tall '
                         'boxy upright monitor cabinet, a steering wheel below its recessed screen, and one '
                         'separate bucket seat facing the screen. Both seats and cabinets are anchored to one '
                         'low flat rectangular arcade floor platform. Stepped squared side panels, thick '
                         'beveled edges. Stationary indoor game equipment, not a car. No vehicle wheels, tires, '
                         'canopy, people, lettering or logos.',
                         'Midnight navy metal cabinets with completely blank solid navy marquee header panels. '
                         'No lettering, words, brands, logos, numbers or decals anywhere. Warm ivory seat shells, '
                         'raspberry seat pads, cyan screens showing only simple abstract blue light lines, amber '
                         'buttons, aluminium corners. Fine PBR vents, seams and lightly worn edges. All sign '
                         'panels are plain unmarked dark navy.'),
 'game_pinball_table': ('game',
                        'prop',
                        8000,
                        (1.9, 2.7, 3.4),
                        'z',
                        'Original arcade pinball machine, long shallow sloping glass-covered rectangular '
                        'playfield with large colorful geometric bumpers clearly visible beneath glass, tall '
                        'vertical backbox with framed pixel star graphic, four thick square metal legs, side '
                        'flipper buttons. Chunky beveled block-built forms. Whole grounded freestanding '
                        'table, no text, no logos.',
                        'Midnight navy painted cabinet, warm ivory leg trim, cyan and raspberry geometric '
                        'bumper islands, amber rails and buttons, clear glossy playfield panel, illuminated '
                        'abstract star backbox. Tasteful wear, precise metal edges, rich PBR surface '
                        'detail.'),
 'game_handheld_monument': ('game',
                            'landmark',
                            10000,
                            (4.0, 6.8, 1.7),
                            'x',
                            'Colossal original handheld game console standing upright as an architectural '
                            'monument, tall thick rectangular body with stepped beveled corners, large '
                            'deeply inset square screen on upper half, oversized raised plus shaped '
                            'directional pad lower left, three large diagonal circular buttons lower right, '
                            'three simple speaker slots near bottom. Recognizable retro hardware silhouette, '
                            'voxel-like geometric construction. No letters or logos.',
                            'Warm ivory slightly aged polymer front shell with midnight navy side frame, '
                            'cyan screen with glowing abstract pixel constellation, raspberry directional '
                            'pad, amber circular action buttons, dark recessed speaker slots, tiny '
                            'manufacturing seams and slight surface wear. Refined PBR plastic and metal.'),
 'game_cartridge_arch': ('game',
                         'landmark',
                         11000,
                         (7.0, 6.5, 2.0),
                         'x',
                         'Original monumental gaming cartridge portal arch, two thick upright square stacks '
                         'of interlocking rectangular game cartridges joined above by one massive horizontal '
                         'cartridge lintel, huge completely empty rectangular walk-through opening. Broad '
                         'stepped beveled block silhouette, recessed blank rectangular label panels, visible '
                         'short golden connector teeth at outer sides, narrow depth. No side walls, no back '
                         'wall, no letters or logos.',
                         'Midnight navy cartridge shells with alternating ivory inset label panels, cyan and '
                         'raspberry plastic ribbing, amber gold metal connector teeth, subtle molded plastic '
                         'seams and worn corners, light scuffs. High quality PBR industrial toy '
                         'architecture.'),
 'game_joystick_tower': ('game',
                         'prop',
                         7000,
                         (2.7, 4.8, 2.7),
                         None,
                         'Original oversized arcade joystick control tower, tall stout square navy control '
                         'pedestal, broad slanted beveled console deck with four large round buttons, thick '
                         'central upright joystick shaft topped with a large faceted spherical ball, squared '
                         'protruding corner housings and recessed base vents. Strong playful block-built '
                         'architectural silhouette, complete standalone monument. No text or logos.',
                         'Midnight navy painted metal pedestal, warm ivory beveled deck edges, raspberry '
                         'faceted joystick ball, cyan and amber oversized action buttons, brushed aluminium '
                         'shaft and corner protectors, subtle molded seams, normal-map vent grooves, '
                         'tasteful scuffed edges.'),
 'game_prize_cabinet': ('game',
                        'prop',
                        9000,
                        (2.3, 3.6, 2.2),
                        None,
                        'Original arcade claw prize machine, stout square glass-front cabinet with thick '
                        'blocky corner posts, suspended three-prong metal claw visible inside above several '
                        'colorful voxel cube prizes, broad lower cabinet with sloping two-button control '
                        'deck and square prize hatch. Oversized stepped marquee with blank inset panel. '
                        'Beveled hard-surface toy architecture, no text or logos.',
                        'Midnight navy metal frame, warm ivory corner posts, raspberry marquee trim, cyan '
                        'glowing edge strips, amber control buttons, clear glass panels revealing colorful '
                        'geometric cube prizes and silver claw, fine seams and lightly worn PBR finish.'),
 'game_controller_bench': ('game',
                           'prop',
                           7000,
                           (5.3, 1.3, 2.3),
                           'x',
                           'Original outdoor bench shaped like an oversized retro game controller, wide low '
                           'horizontal rounded rectangular block, two thick stepped grip ends, large raised '
                           'plus directional pad on left, four large circular button cushions on right, flat '
                           'recessed center seat, two sturdy short rectangular support feet. Chunky beveled '
                           'voxel-like solid geometry, architectural rest bench. No text, symbols or logos.',
                           'Warm ivory painted body with midnight navy lower frame, raspberry rubber '
                           'directional pad, cyan and amber round button cushions, brushed aluminium feet, '
                           'subtle manufacturing seam lines, gentle rubbed edges and clean PBR detail.')}

# Decor batch (2026-10): six more original arcade-floor props so the seven Game rooms stop repeating the same machines.
ASSETS.update({
    'game_claw_machine': ('game', 'prop', 9000, (1.4, 2.3, 1.4), None,
        'Original arcade claw crane machine, tall square glass display box on a sturdy cabinet base, a metal claw hanging'
        ' from a gantry inside, a heap of round plush toy balls and capsules on the floor of the box, one joystick and one'
        ' big button on the front ledge, prize chute door below. No lettering, brands or characters.',
        'Deep midnight navy cabinet, warm ivory trim, plain blank glowing cyan marquee panel with no letters or words at'
        ' all, clear glass with cyan reflections, raspberry and amber prize balls, chrome claw, cyan rim lights, light'
        ' edge wear, rich PBR detail. Absolutely no text, letters, numbers or logos anywhere.'),
    'game_crt_stack': ('game', 'prop', 9000, (2.0, 2.2, 1.4), 'x',
        'Stack of five chunky retro CRT television sets of different sizes piled in a stepped pyramid, thick boxy bodies,'
        ' rounded screens showing simple abstract pixel patterns, short antennas on two of them, tangled cables at the'
        ' base. Solid grouped prop, no logos.',
        'Ivory, warm grey and navy plastic casings, glowing cyan, amber and raspberry pixel screens, dark grilles, black'
        ' cables, light scuffs, rich PBR detail.'),
    'game_dance_machine': ('game', 'landmark', 12000, (2.4, 2.6, 2.4), None,
        'Original rhythm dance arcade machine, a tall rear screen cabinet with two speaker towers, a wide square floor'
        ' platform in front with a grid of large glowing arrow step pads and a safety bar, chunky bevelled block forms.'
        ' No lettering, brands or characters.',
        'Midnight navy and ivory panels, glowing cyan, amber and raspberry arrow pads, chrome safety bar, black speaker'
        ' grilles with cyan rings, light wear, rich PBR detail.'),
    'game_cartridge_crate': ('game', 'prop', 7000, (1.6, 1.1, 1.2), 'x',
        'Open wooden shipping crate overflowing with chunky blank game cartridges, a few cartridges spilled on the floor'
        ' in front, one cartridge leaning against the crate. Compact grounded prop, no labels, logos or text.',
        'Pale stencilled pine crate boards, cartridges in navy, ivory, cyan, amber and raspberry plastic with blank label'
        ' areas, metal contact edges, light dust, rich PBR detail.'),
    'game_air_hockey': ('game', 'prop', 9000, (2.4, 1.0, 1.4), 'x',
        'Original air hockey arcade table, long rectangular table with raised rails, a goal slot at each end, two round'
        ' strikers and a puck on the playfield, a small blank score box on a short post at one side, sturdy legs.'
        ' No lettering or brands.',
        'Glossy ivory playfield with a cyan centre line and circles, midnight navy rails and body, raspberry and amber'
        ' strikers, chrome corners, light wear, rich PBR detail.'),
    'game_speaker_tower': ('game', 'prop', 8000, (1.4, 2.0, 1.0), 'x',
        'Stack of retro arcade speaker cabinets and a big boxy boombox on top, round speaker cones of different sizes,'
        ' equalizer light bars, chunky knobs, a coiled cable at the base. Solid vertical stacked prop, no logos.',
        'Midnight navy and ivory casings, black speaker cones with cyan glowing rings, amber and raspberry equalizer'
        ' lights, chrome knobs, light scuffs, rich PBR detail.'),
})

_ledger_lock = threading.Lock()
_print_lock = threading.Lock()


def log(*parts):
    with _print_lock:
        print(*parts, flush=True)


def key_or_exit():
    value = os.environ.get('MESHY_API_KEY', '')
    if not value:
        raise SystemExit('MESHY_API_KEY is not set in this shell. Set it as an environment variable; it is never read from files.')
    return value


def api(path, payload=None, attempts=6):
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    request = urllib.request.Request(BASE + path, data=data, headers={
        'Authorization': 'Bearer ' + key_or_exit(), 'Content-Type': 'application/json'})
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            # Rate limits are safe to retry. Other POST failures are ambiguous and must not duplicate paid tasks.
            retry = exc.code == 429 or (payload is None and exc.code >= 500)
            if retry and attempt < attempts - 1:
                time.sleep(20 + attempt * 10)
                continue
            message = exc.read().decode('utf-8', errors='replace')[:1000]
            raise RuntimeError(f'Meshy HTTP {exc.code}: {message}') from None
        except (urllib.error.URLError, TimeoutError, ConnectionError) as exc:
            if payload is None and attempt < attempts - 1:
                time.sleep(15 + attempt * 10)
                continue
            raise RuntimeError(f'Meshy request failed: {exc}') from None


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_suffix(path.suffix + '.tmp')
    pending.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
    pending.replace(path)


def load(path, default):
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else default


def full_prompt(key):
    return ASSETS[key][5] + STYLE


def preview_payload(key):
    polycount = ASSETS[key][2]
    return dict(mode='preview', prompt=full_prompt(key), ai_model=MODEL, should_remesh=True, topology='triangle',
                target_polycount=polycount, target_formats=['glb'])


def refine_payload(key, preview_id):
    return dict(mode='refine', preview_task_id=preview_id, enable_pbr=True, texture_resolution=TEXTURE_RESOLUTION,
                target_formats=['glb'], texture_prompt=ASSETS[key][6])


def validate_requests():
    problems = []
    for key, spec in ASSETS.items():
        theme, role, polycount, size, long_axis, prompt, texture = spec
        if len(full_prompt(key)) > MAX_PROMPT:
            problems.append(f'{key}: prompt is {len(full_prompt(key))} chars (max {MAX_PROMPT})')
        if len(texture) > MAX_PROMPT:
            problems.append(f'{key}: texture prompt is {len(texture)} chars (max {MAX_PROMPT})')
        if not 5000 <= polycount <= 15000:
            problems.append(f'{key}: target polycount {polycount} is outside the game kit 5k-15k range')
        if theme != 'game' or role not in ('landmark', 'prop'):
            problems.append(f'{key}: unknown theme or role')
        if long_axis not in (None, 'x', 'z') or len(size) != 3 or min(size) <= 0:
            problems.append(f'{key}: invalid size or long axis')
    return problems


def glb_stats(path):
    """Triangle/texture statistics plus bounds converted to Unity axes (glTFast imports glTF x as -x)."""
    blob = path.read_bytes()
    magic, version, _ = struct.unpack_from('<III', blob)
    assert magic == 0x46546C67 and version == 2, 'Not a glTF 2.0 binary'
    json_length, _ = struct.unpack_from('<II', blob, 12)
    gltf = json.loads(blob[20:20 + json_length])
    triangles = vertices = 0
    low = [float('inf')] * 3
    high = [float('-inf')] * 3
    for node in gltf.get('nodes', []):
        if 'mesh' not in node:
            continue
        if any(k in node for k in ('rotation', 'scale', 'translation')) or ('matrix' in node and node['matrix'] != [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0]):
            raise ValueError(f'{path.name}: node transforms are not supported by this inspector')
    for mesh in gltf.get('meshes', []):
        for primitive in mesh['primitives']:
            assert primitive.get('mode', 4) == 4, 'Unexpected non-triangle primitive'
            position = gltf['accessors'][primitive['attributes']['POSITION']]
            vertices += position['count']
            count = gltf['accessors'][primitive['indices']]['count'] if 'indices' in primitive else position['count']
            triangles += count // 3
            for axis in range(3):
                low[axis] = min(low[axis], position['min'][axis])
                high[axis] = max(high[axis], position['max'][axis])
    unity_min = [-high[0], low[1], low[2]]
    unity_max = [-low[0], high[1], high[2]]
    size = [unity_max[i] - unity_min[i] for i in range(3)]
    images = []
    for image in gltf.get('images', []):
        view = gltf['bufferViews'][image['bufferView']]
        images.append({'mime': image.get('mimeType'), 'bytes': view['byteLength'], 'size': image_size(blob, gltf, view)})
    return {'triangles': triangles, 'vertices': vertices, 'mesh_count': len(gltf.get('meshes', [])),
            'material_count': len(gltf.get('materials', [])), 'embedded_images': images,
            'normal_mapped_materials': sum('normalTexture' in m for m in gltf.get('materials', [])),
            'unity_bounds_min': unity_min, 'unity_bounds_max': unity_max,
            'longest_horizontal_axis': 'x' if size[0] >= size[2] else 'z', 'bytes': len(blob)}


def image_size(blob, gltf, view):
    start = 28 + struct.unpack_from('<I', blob, 12)[0] + view.get('byteOffset', 0)
    head = blob[start:start + 64 * 1024]
    if head[:8] == b'\x89PNG\r\n\x1a\n':
        return list(struct.unpack('>II', head[16:24]))
    index = 2
    while head[:2] == b'\xff\xd8' and index < len(head) - 9:  # JPEG: walk markers to the first SOF segment
        if head[index] != 0xFF:
            break
        marker, length = head[index + 1], struct.unpack('>H', head[index + 2:index + 4])[0]
        if marker in (0xC0, 0xC1, 0xC2):
            height, width = struct.unpack('>HH', head[index + 5:index + 9])
            return [width, height]
        index += 2 + length
    return None


class Budget:
    def __init__(self, limit):
        self.limit = limit
        self.path = CACHE / 'ledger.json'
        self.reserved = 0

    def spent(self):
        return sum(entry.get('credits', 0) for entry in load(self.path, {}).get('tasks', []))

    def reserve(self, amount, key):
        with _ledger_lock:
            if self.spent() + self.reserved + amount > self.limit:
                return False
            self.reserved += amount
            log(f'{key}: reserved {amount} credits (spent {self.spent()}, reserved {self.reserved}, cap {self.limit})')
            return True

    def release(self, amount):
        with _ledger_lock:
            self.reserved = max(0, self.reserved - amount)

    def record(self, key, phase, task_id, credits):
        with _ledger_lock:
            ledger = load(self.path, {'tasks': []})
            if not any(t['task_id'] == task_id for t in ledger['tasks']):
                ledger['tasks'].append({'key': key, 'phase': phase, 'task_id': task_id, 'credits': credits,
                                        'recorded_utc': now()})
                save(self.path, ledger)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def wait_task(key, phase, task_id, folder):
    last = None
    deadline = time.monotonic() + 3600
    while time.monotonic() < deadline:
        result = api('/v2/text-to-3d/' + task_id)
        save(folder / (phase + '_result.json'), result)
        marker = (result['status'], result.get('progress'))
        if marker != last:
            log(key, phase, *marker)
            last = marker
        if result['status'] == 'SUCCEEDED':
            return result
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{key} {phase}: {result.get("task_error")}')
        time.sleep(20)
    raise TimeoutError(f'{key} {phase} timed out; rerun resumes the same task.')


def download(url, destination):
    pending = destination.with_suffix(destination.suffix + '.part')
    urllib.request.urlretrieve(url, pending)
    pending.replace(destination)


def generate(key, budget):
    theme, role, polycount, size, long_axis, prompt, texture_prompt = ASSETS[key]
    final = OUT / key / (key + '.glb')
    record = OUT / key / 'provenance.json'
    if final.exists() and record.exists():
        log('EXISTING', key, json.dumps({k: v for k, v in glb_stats(final).items() if k in ('triangles', 'bytes')}))
        return load(record, {})
    folder = CACHE / key
    state_path = folder / 'state.json'
    state = load(state_path, {})
    reserved = 0
    try:
        if 'preview_id' not in state:
            if not budget.reserve(ESTIMATED_CREDITS, key):
                raise RuntimeError(f'budget cap of {budget.limit} credits reached; not submitting {key}')
            reserved = ESTIMATED_CREDITS
            payload = preview_payload(key)
            save(folder / 'preview_request.json', payload)
            state['preview_id'] = api('/v2/text-to-3d', payload)['result']
            state['submitted_utc'] = now()
            save(state_path, state)
            log(key, 'preview submitted')
        preview = wait_task(key, 'preview', state['preview_id'], folder)
        budget.record(key, 'preview', state['preview_id'], preview.get('consumed_credits', 20))
        if 'refine_id' not in state:
            if not reserved:
                if not budget.reserve(10, key):
                    raise RuntimeError(f'budget cap of {budget.limit} credits reached; not retexturing {key}')
                reserved = 10
            payload = refine_payload(key, state['preview_id'])
            save(folder / 'refine_request.json', payload)
            state['refine_id'] = api('/v2/text-to-3d', payload)['result']
            save(state_path, state)
            log(key, 'refine submitted')
        refine = wait_task(key, 'refine', state['refine_id'], folder)
        budget.record(key, 'refine', state['refine_id'], refine.get('consumed_credits', 10))
    finally:
        if reserved:
            budget.release(reserved)
    source = folder / (key + '_source.glb')
    if not source.exists():
        download(refine['model_urls']['glb'], source)
    thumbnail = folder / 'thumbnail.png'
    if not thumbnail.exists() and refine.get('thumbnail_url'):
        download(refine['thumbnail_url'], thumbnail)
    stats = glb_stats(source)
    destination = OUT / key
    destination.mkdir(parents=True, exist_ok=True)
    # Meshy already remeshed to the requested budget; keep its UVs, 2K PBR maps and normal map untouched.
    pending = final.with_suffix('.glb.tmp')
    pending.write_bytes(source.read_bytes())
    pending.replace(final)
    provenance = dict(provider='Meshy AI', api='text-to-3d v2', model=MODEL, kit='game-theme original arcade hardware',
                      theme=theme, role=role, prompt=full_prompt(key), texture_prompt=texture_prompt,
                      preview_task_id=state['preview_id'], refine_task_id=state['refine_id'],
                      attempt=len(state.get('rejected', [])) + 1, created_utc=now(),
                      texture_attempt=len(state.get('rejected_textures', [])) + 1,
                      rejected_geometry=state.get('rejected', []),
                      rejected_textures=state.get('rejected_textures', []),
                      requested_triangles=polycount, texture_resolution='2K', enable_pbr=True,
                      target_size_m=list(size), long_horizontal_axis=long_axis, actual=stats,
                      consumed_credits=preview.get('consumed_credits', 0) + refine.get('consumed_credits', 0),
                      optimization='Meshy triangle remesh to the requested budget before PBR texturing; original 2K maps kept.',
                      review='pending: inspect thumbnail and in-room capture, then keep or reject')
    save(record, provenance)
    log('READY', key, json.dumps({k: stats[k] for k in ('triangles', 'vertices', 'bytes')}))
    return provenance


def reject(key, reason):
    folder = CACHE / key
    state = load(folder / 'state.json', {})
    if 'preview_id' not in state:
        raise SystemExit(f'{key} has no generated task to reject.')
    history = state.setdefault('rejected', [])
    history.append({'preview_id': state.pop('preview_id'), 'refine_id': state.pop('refine_id', None),
                    'reason': reason, 'rejected_utc': now()})
    save(folder / 'state.json', state)
    for name in (key + '_source.glb', 'thumbnail.png'):
        stale = folder / name
        if stale.exists():
            stale.replace(folder / (f'rejected{len(history)}_' + name))
    for stale in (OUT / key / (key + '.glb'), OUT / key / 'provenance.json'):
        if stale.exists():
            stale.unlink()
    log(f'{key}: rejected ({reason}). Edit its prompt if needed, then run --key {key} to regenerate.')


def reject_texture(key, reason):
    """Keep approved geometry and regenerate only PBR maps after visual review."""
    folder = CACHE / key
    state = load(folder / 'state.json', {})
    if 'refine_id' not in state:
        raise SystemExit(f'{key} has no refined texture task to reject.')
    history = state.setdefault('rejected_textures', [])
    history.append({'refine_id': state.pop('refine_id'), 'reason': reason, 'rejected_utc': now()})
    save(folder / 'state.json', state)
    for name in (key + '_source.glb', 'thumbnail.png'):
        stale = folder / name
        if stale.exists():
            stale.replace(folder / (f'rejected_texture{len(history)}_' + name))
    for stale in (OUT / key / (key + '.glb'), OUT / key / 'provenance.json'):
        if stale.exists():
            stale.unlink()
    log(f'{key}: rejected texture only ({reason}); approved preview geometry retained.')


def write_summary():
    ledger = load(CACHE / 'ledger.json', {'tasks': []})
    rows = {}
    for key in ASSETS:
        record = load(OUT / key / 'provenance.json', None)
        state = load(CACHE / key / 'state.json', {})
        rows[key] = {'theme': ASSETS[key][0], 'role': ASSETS[key][1], 'requested_triangles': ASSETS[key][2],
                     'status': 'ready' if record else ('in_progress' if 'preview_id' in state else 'pending'),
                     'asset_path': f'Assets/GameTheme/Art/Meshy/{key}/{key}.glb',
                     'target_size_m': list(ASSETS[key][3]),
                     'long_horizontal_axis': ASSETS[key][4],
                     'triangles': record['actual']['triangles'] if record else None,
                     'actual': record['actual'] if record else None,
                     'texture_resolution': record.get('texture_resolution') if record else None,
                     'unity_front_axis': record.get('unity_front_axis') if record else None,
                     'review': record.get('review') if record else None,
                     'credits_all_attempts': sum(t['credits'] for t in ledger['tasks'] if t['key'] == key),
                     'rejected_attempts': [r['reason'] for r in state.get('rejected', [])],
                     'rejected_textures': [r['reason'] for r in state.get('rejected_textures', [])]}
    save(SUMMARY, {'generator': MODEL, 'source_texture_resolution': '2K',
                   'texture_resolution': '1K repeated props / 2K landmarks after optimization', 'updated_utc': now(),
                   'credits_spent_total': sum(t['credits'] for t in ledger['tasks']), 'assets': rows})


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('action', choices=['plan', 'balance', 'run', 'inspect', 'reject', 'retexture'])
    parser.add_argument('--key', action='append', choices=list(ASSETS), help='limit to one or more assets')
    parser.add_argument('--theme', choices=['game'])
    parser.add_argument('--workers', type=int, default=4)
    parser.add_argument('--budget', type=int, default=360, help='credit cap across all attempts')
    parser.add_argument('--dry-run', action='store_true', help='validate requests without contacting Meshy')
    parser.add_argument('--reason', default='rejected after review')
    args = parser.parse_args()
    keys = [k for k in (args.key or list(ASSETS)) if not args.theme or ASSETS[k][0] == args.theme]
    problems = validate_requests()
    if problems:
        raise SystemExit('Invalid asset table:\n  ' + '\n  '.join(problems))
    if args.action == 'plan':
        for key in keys:
            theme, role, polycount, size, long_axis, _, _ = ASSETS[key]
            print(f'{theme:8} {role:8} {key:20} {polycount:6} tris  {size[0]:>4} x {size[1]:>4} x {size[2]:>4} m  prompt {len(full_prompt(key))} chars')
        print(f'{len(keys)} assets, estimated {len(keys) * ESTIMATED_CREDITS} credits before any rejection.')
        return
    if args.action == 'balance':
        print(json.dumps(api('/v1/balance')))
        return
    if args.action == 'inspect':
        for key in keys:
            path = OUT / key / (key + '.glb')
            if path.exists():
                print(key, json.dumps(glb_stats(path)))
        return
    if args.action in ('reject', 'retexture'):
        if not args.key or len(args.key) != 1:
            raise SystemExit('reject needs exactly one --key')
        if args.action == 'reject':
            reject(args.key[0], args.reason)
        else:
            reject_texture(args.key[0], args.reason)
        write_summary()
        return
    if args.dry_run:
        for key in keys:
            payload = preview_payload(key)
            print(key, 'preview', len(payload['prompt']), 'chars', payload['target_polycount'], 'tris;', 'refine texture',
                  len(ASSETS[key][6]), 'chars')
        print('Dry run OK:', len(keys), 'requests validated, nothing sent.')
        return
    key_or_exit()
    balance = api('/v1/balance').get('balance')
    budget = Budget(args.budget)
    log(f'Meshy balance {balance}; cap {args.budget}; already spent by this kit {budget.spent()}.')
    CACHE.mkdir(parents=True, exist_ok=True)
    failures = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {pool.submit(generate, key, budget): key for key in keys}
        for future in concurrent.futures.as_completed(futures):
            key = futures[future]
            try:
                future.result()
            except Exception as exc:  # keep the other tasks running; the summary lists what is missing
                failures[key] = str(exc)
                log('ERROR', key, exc)
            write_summary()
    write_summary()
    log(f'Done. Spent by this kit: {Budget(args.budget).spent()} credits. Failures: {len(failures)}')
    if failures:
        sys.exit(1)


if __name__ == '__main__':
    main()
