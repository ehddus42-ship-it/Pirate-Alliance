"""Generate the traffic-light boss body and three throwable cars with Meshy.

Usage: python Tools/TrafficLightBoss/meshy_assets.py run
The API key is read from MESHY_API_KEY only. Resumable: task ids are stored in Tools/TrafficLightBoss/Source/<key>/state.json.
The body uses reference_unlit.png (the supplied signal photo with the lit lenses darkened) so the game can switch
emissive lens overlays on and off instead of baking a permanently lit lamp into the texture.
"""
import base64
import concurrent.futures
import json
import os
import pathlib
import struct
import sys
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/TrafficLightBoss/Source'
OUT = ROOT / 'Assets/Liminal/Art/TrafficLightBoss/Meshy'
BASE = 'https://api.meshy.ai/openapi'
CAR_STYLE = (' Single isolated complete vehicle, full car visible, front of the car points to the right of the side view, four wheels touching one flat level,'
             ' no ground plane, no pedestal, no people, no letters, no logos, no license plate text. Strong readable silhouette,'
             ' efficient low polygon game mesh, fine detail in PBR normal textures, abandoned liminal city street prop.')
CARS = {
    'rusted_sedan': (7000, 'Abandoned rusted compact four-door family sedan, boxy 1990s proportions, dented roof, cracked dark windows, slightly flat tires.',
                     'Faded teal paint peeling to orange rust, dusty grey bumpers, dark cracked glass, worn black rubber tires, chipped normal detail.'),
    'yellow_taxi': (7000, 'Battered city taxi cab sedan with a plain blank roof light box, dented doors, cracked windows, scuffed bumpers.',
                    'Faded mustard yellow paint with scratches and rust at the edges, black trim, dirty dark glass, worn tires, dusty and weathered.'),
    'delivery_van': (8000, 'Old boxy delivery van with a tall rectangular cargo box behind a short cab, sliding side door, dented panels, cracked windshield.',
                     'Dirty off-white paint with brown rust streaks, dented grey bumpers, cracked dark glass, worn tires, blank plain cargo box sides.'),
}
BODY_POLYCOUNT = 30000


def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    request = urllib.request.Request(BASE + path, data=data, headers={
        'Authorization': 'Bearer ' + os.environ['MESHY_API_KEY'], 'Content-Type': 'application/json'})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            if exc.code == 429 and attempt < 3:
                time.sleep(30)
                continue
            raise RuntimeError(f'Meshy HTTP {exc.code}: {exc.read().decode("utf-8", errors="replace")[:1000]}') from None


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def wait(key, phase, endpoint, task_id, folder):
    last = None
    deadline = time.monotonic() + 3000
    while time.monotonic() < deadline:
        result = api(endpoint + task_id)
        save(folder / (phase + '_result.json'), result)
        marker = (result['status'], result.get('progress'))
        if marker != last:
            print(key, phase, *marker, flush=True)
            last = marker
        if result['status'] == 'SUCCEEDED':
            return result
        if result['status'] in ('FAILED', 'CANCELED'):
            raise RuntimeError(f'{key} {phase}: {result.get("task_error")}')
        time.sleep(20)
    raise TimeoutError(f'{key} {phase} timed out; rerun resumes the same task.')


def inspect_glb(path):
    blob = path.read_bytes()
    json_length, _ = struct.unpack_from('<II', blob, 12)
    gltf = json.loads(blob[20:20 + json_length])
    triangles = 0
    for mesh in gltf.get('meshes', []):
        for primitive in mesh['primitives']:
            count = gltf['accessors'][primitive['indices']]['count'] if 'indices' in primitive else gltf['accessors'][primitive['attributes']['POSITION']]['count']
            triangles += count // 3
    return {'triangles': triangles, 'materials': len(gltf.get('materials', [])), 'images': len(gltf.get('images', [])), 'bytes': len(blob)}


def finish(key, prompt_info, result, credits):
    folder = CACHE / key
    source = folder / (key + '_source.glb')
    if not source.exists():
        urllib.request.urlretrieve(result['model_urls']['glb'], source)
    thumbnail = folder / 'thumbnail.png'
    if not thumbnail.exists() and result.get('thumbnail_url'):
        urllib.request.urlretrieve(result['thumbnail_url'], thumbnail)
    destination = OUT / key
    destination.mkdir(parents=True, exist_ok=True)
    (destination / (key + '.glb')).write_bytes(source.read_bytes())
    stats = inspect_glb(source)
    save(destination / 'provenance.json', dict(provider='Meshy AI', model='meshy-7.1', stats=stats, consumed_credits=credits, **prompt_info))
    print('DONE', key, json.dumps(stats), flush=True)


def generate_body():
    key = 'traffic_light_body'
    folder = CACHE / key
    folder.mkdir(parents=True, exist_ok=True)
    if (OUT / key / 'provenance.json').exists():
        print('EXISTING', key)
        return
    state_path = folder / 'state.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    if 'image_id' not in state:
        image = (CACHE / 'reference_unlit.png').read_bytes()
        payload = dict(image_url='data:image/png;base64,' + base64.b64encode(image).decode(), ai_model='meshy-7.1',
                       should_remesh=True, topology='triangle', target_polycount=BODY_POLYCOUNT,
                       should_texture=True, enable_pbr=True, texture_resolution='2k', target_formats=['glb'])
        save(folder / 'image_request.json', {**payload, 'image_url': 'reference_unlit.png (data URI sent)'})
        state['image_id'] = api('/v1/image-to-3d', payload)['result']
        save(state_path, state)
        print(key, 'submitted', flush=True)
    result = wait(key, 'image', '/v1/image-to-3d/', state['image_id'], folder)
    finish(key, dict(pipeline='image-to-3d', reference='Tools/TrafficLightBoss/Source/reference_unlit.png'), result, result.get('consumed_credits'))


def generate_car(key):
    polycount, prompt, texture_prompt = CARS[key]
    folder = CACHE / key
    folder.mkdir(parents=True, exist_ok=True)
    if (OUT / key / 'provenance.json').exists():
        print('EXISTING', key)
        return
    state_path = folder / 'state.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    if 'preview_id' not in state:
        payload = dict(mode='preview', prompt=prompt + CAR_STYLE, ai_model='meshy-7.1', should_remesh=True,
                       topology='triangle', target_polycount=polycount, target_formats=['glb'])
        assert len(payload['prompt']) <= 800
        save(folder / 'preview_request.json', payload)
        state['preview_id'] = api('/v2/text-to-3d', payload)['result']
        save(state_path, state)
        print(key, 'preview submitted', flush=True)
    wait(key, 'preview', '/v2/text-to-3d/', state['preview_id'], folder)
    if 'refine_id' not in state:
        payload = dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=True, texture_resolution='2k',
                       target_formats=['glb'], texture_prompt=texture_prompt)
        save(folder / 'refine_request.json', payload)
        state['refine_id'] = api('/v2/text-to-3d', payload)['result']
        save(state_path, state)
        print(key, 'refine submitted', flush=True)
    result = wait(key, 'refine', '/v2/text-to-3d/', state['refine_id'], folder)
    finish(key, dict(pipeline='text-to-3d v2', prompt=prompt + CAR_STYLE, texture_prompt=texture_prompt), result, result.get('consumed_credits'))


def main():
    if len(sys.argv) < 2 or sys.argv[1] != 'run':
        print(__doc__)
        return
    jobs = [generate_body] + [lambda k=k: generate_car(k) for k in CARS]
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(jobs)) as pool:
        for future in [pool.submit(job) for job in jobs]:
            future.result()


if __name__ == '__main__':
    main()
