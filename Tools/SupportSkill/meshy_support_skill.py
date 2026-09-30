"""Generate the voxel objects of the support character's active skill with Meshy (text-to-3D).

  python Tools/SupportSkill/meshy_support_skill.py run [--key voxel_meteor]
  python Tools/SupportSkill/meshy_support_skill.py plan

Assets: a voxel meteor that falls once, a voxel "1UP" sign shown over the player for five seconds and a
voxel Pac-Man style chomper fired alongside every talisman while the 1UP is active. The concept: a gamer
support character borrows objects from games she played. The API key is read from MESHY_API_KEY only and
never written anywhere. Task ids are kept in Tools/SupportSkill/Source/<key>/state.json (git-ignored) so a
rerun resumes the same paid tasks.
"""
import argparse
import datetime
import json
import os
import pathlib
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/SupportSkill/Source'
OUT = ROOT / 'Assets/Liminal/Art/SupportSkill'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
STYLE = (' Voxel art style, built entirely from small uniform cubes like a retro 8-bit video game sprite turned into 3D,'
         ' crisp blocky stepped edges, flat saturated colors per cube, single isolated object, no base, no ground,'
         ' no background, game-ready.')
# key: target polycount, prompt, texture prompt
ASSETS = {
    'voxel_meteor': (8000,
        'A chunky voxel meteor rock falling from the sky, roughly round cube-built boulder with a glowing molten orange core'
        ' showing through cracks, dark charcoal and purple rock voxels on the outside, a short blocky trail of flame voxels'
        ' on one side, retro arcade game asteroid.',
        'Dark charcoal grey and deep purple rock cubes, bright orange and yellow glowing lava cubes in the cracks and in the'
        ' flame trail, pixel-art flat colors.'),
    'voxel_1up': (4000,
        'The text "1UP" as thick chunky voxel block letters standing upright, the digit 1 followed by the letters U and P,'
        ' side by side in one row, facing forward, extruded pixel font like a retro platformer extra-life pickup, small'
        ' pixel heart above the letters.',
        'Bright lime green letters with lighter green top cubes and a dark green outline, a red and pink pixel heart,'
        ' glossy arcade pixel colors.'),
    'voxel_chomper': (3000,
        'A round yellow voxel pac-man style chomper ball with a wide open wedge-shaped mouth facing forward, one small black'
        ' cube eye on each side, built from yellow cubes, retro maze arcade game character, compact sphere shape.',
        'Bright saturated yellow cubes with slightly darker yellow cubes at the back, black eye cubes, dark red inside the'
        ' mouth, pixel-art flat colors.'),
}


def key():
    value = os.environ.get('MESHY_API_KEY', '')
    if not value:
        raise SystemExit('MESHY_API_KEY is not set.')
    return value


def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data, headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.load(r)
        except urllib.error.HTTPError as exc:
            if (exc.code == 429 or (payload is None and exc.code >= 500)) and attempt < 5:
                time.sleep(20 + 10 * attempt)
                continue
            raise RuntimeError(f'Meshy HTTP {exc.code}: {exc.read().decode(errors="replace")[:500]}') from None
        except (urllib.error.URLError, TimeoutError, ConnectionError) as exc:
            if payload is None and attempt < 5:
                time.sleep(15)
                continue
            raise RuntimeError(f'Meshy request failed: {exc}') from None


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


def load(path):
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}


def wait(name, task_id, folder):
    last = None
    while True:
        result = api('/v2/text-to-3d/' + task_id)
        save(folder / f'{name}_result.json', result)
        if (result['status'], result.get('progress')) != last:
            last = (result['status'], result.get('progress'))
            print(folder.name, name, *last, flush=True)
        if result['status'] == 'SUCCEEDED':
            return result
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{folder.name} {name}: {result.get("task_error")}')
        time.sleep(15)


def generate(name):
    polycount, prompt, texture = ASSETS[name]
    folder = CACHE / name
    state = load(folder / 'state.json')
    if 'preview_id' not in state:
        state['preview_id'] = api('/v2/text-to-3d', dict(mode='preview', prompt=prompt + STYLE, ai_model=MODEL, should_remesh=True,
                                                         topology='triangle', target_polycount=polycount, target_formats=['glb']))['result']
        save(folder / 'state.json', state)
    preview = wait('preview', state['preview_id'], folder)
    if 'refine_id' not in state:
        state['refine_id'] = api('/v2/text-to-3d', dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=False,
                                                        texture_resolution='2k', target_formats=['glb'], texture_prompt=texture))['result']
        save(folder / 'state.json', state)
    refine = wait('refine', state['refine_id'], folder)
    out = OUT / name
    out.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve(refine['model_urls']['glb'], out / f'{name}.glb')
    if refine.get('thumbnail_url'):
        urllib.request.urlretrieve(refine['thumbnail_url'], folder / 'thumbnail.png')
    save(out / 'provenance.json', dict(provider='Meshy AI', api='text-to-3d v2', model=MODEL, prompt=prompt + STYLE,
                                       texture_prompt=texture, requested_triangles=polycount, texture_resolution='2K',
                                       preview_task_id=state['preview_id'], refine_task_id=state['refine_id'],
                                       consumed_credits=preview.get('consumed_credits', 0) + refine.get('consumed_credits', 0),
                                       created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    print('READY', name, flush=True)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('command', choices=['run', 'plan'])
    ap.add_argument('--key', action='append')
    args = ap.parse_args()
    for name in args.key or ASSETS:
        if args.command == 'plan':
            print(name, ASSETS[name][0])
        else:
            generate(name)
