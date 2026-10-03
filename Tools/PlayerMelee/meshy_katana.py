"""Generate the player's katana with Meshy (text-to-3D).

  python Tools/PlayerMelee/meshy_katana.py run

The player switched from talisman throws to katana slashes in the style of an iaido shrine maiden. The blade is held
in the right hand by AstraiaMeleeSlash at runtime. The API key is read from MESHY_API_KEY only; task ids are kept in
Tools/PlayerMelee/Source/<key>/state.json (git-ignored) so a rerun resumes the same paid tasks.
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
CACHE = ROOT / 'Tools/PlayerMelee/Source'
OUT = ROOT / 'Tools/PlayerMelee/Source'  # raw download; clean_katana.py installs the game copy
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
STYLE = (' Single isolated weapon, whole object visible, no stand, no background, no hands, game-ready stylized anime action game asset.')
# key: target polycount, prompt, texture prompt
ASSETS = {
    'sakura_katana': (7000,
        'A long elegant Japanese katana sword without sheath, straight horizontal pose, slender gently curved single-edged'
        ' blade with a clear hamon temper line, round gold tsuba guard shaped like a five-petal cherry blossom, handle wrapped'
        ' in crimson and white silk diamond pattern with a gold pommel cap, a small pink tassel at the pommel.',
        'Polished silver steel blade with a soft pink sheen near the edge, white wavy hamon line, gold cherry blossom guard,'
        ' crimson and white braided handle wrap, bright pink tassel, anime style clean colors.'),
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
        state['refine_id'] = api('/v2/text-to-3d', dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=True,
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
