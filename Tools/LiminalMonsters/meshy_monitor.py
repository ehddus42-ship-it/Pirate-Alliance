"""Generate the CRT monitor used by the monitor turret enemies with Meshy (text-to-3D, preview + refine).

  python Tools/LiminalMonsters/meshy_monitor.py run

The copier and locker monsters reuse the props already in the liminal concept rooms
(Assets/Liminal/Art/Meshy/photocopier, lockers); only a standalone monitor was missing. The API key is read
from MESHY_API_KEY only and never written anywhere. Task ids live in Tools/LiminalMonsters/Source/state.json
(git-ignored) so a rerun resumes the same paid tasks.
"""
import datetime
import json
import os
import pathlib
import sys
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/LiminalMonsters/Source'
OUT = ROOT / 'Assets/Liminal/Resources/LiminalMonsters'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
NAME = 'crt_monitor'
PROMPT = ('An old 1990s office CRT computer monitor, a deep boxy beige plastic housing slightly yellowed with age, a curved '
          'glass screen in front with a thick bezel, a small power button and vents, standing on a short swivel foot. '
          'Abandoned office prop, single isolated object, no keyboard, no desk, no cables, no background, game-ready, '
          'realistic proportions about 40 cm wide.')
TEXTURE = ('Yellowed beige matte plastic with faint scuffs and dust, dark glossy grey-green glass screen, small green '
           'power LED, worn edges.')


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


def wait(name, task_id):
    last = None
    while True:
        result = api('/v2/text-to-3d/' + task_id)
        save(CACHE / f'{name}_result.json', result)
        if (result['status'], result.get('progress')) != last:
            last = (result['status'], result.get('progress'))
            print(name, *last, flush=True)
        if result['status'] == 'SUCCEEDED':
            return result
        if result['status'] in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{name}: {result.get("task_error")}')
        time.sleep(15)


def fetch(url, path):
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, path)
            return
        except (urllib.error.ContentTooShortError, urllib.error.URLError, ConnectionError):
            if attempt == 4:
                raise
            time.sleep(5 * (attempt + 1))


def run():
    state = load(CACHE / 'state.json')
    if 'preview_id' not in state:
        state['preview_id'] = api('/v2/text-to-3d', dict(mode='preview', prompt=PROMPT, ai_model=MODEL, should_remesh=True,
                                                         topology='triangle', target_polycount=6000, target_formats=['glb']))['result']
        save(CACHE / 'state.json', state)
    preview = wait('preview', state['preview_id'])
    if 'refine_id' not in state:
        state['refine_id'] = api('/v2/text-to-3d', dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=False,
                                                        texture_resolution='2k', target_formats=['glb'], texture_prompt=TEXTURE))['result']
        save(CACHE / 'state.json', state)
    refine = wait('refine', state['refine_id'])
    OUT.mkdir(parents=True, exist_ok=True)
    fetch(refine['model_urls']['glb'], OUT / f'{NAME}.glb')
    if refine.get('thumbnail_url'):
        fetch(refine['thumbnail_url'], CACHE / 'thumbnail.png')
    save(ROOT / 'Assets/Liminal/Art/Meshy/crt_monitor_provenance.json',
         dict(provider='Meshy AI', api='text-to-3d v2', model=MODEL, prompt=PROMPT, texture_prompt=TEXTURE,
              requested_triangles=6000, texture_resolution='2K', preview_task_id=state['preview_id'],
              refine_task_id=state['refine_id'],
              consumed_credits=preview.get('consumed_credits', 0) + refine.get('consumed_credits', 0),
              created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    print('READY', NAME, flush=True)


if __name__ == '__main__':
    if sys.argv[1:] != ['run']:
        raise SystemExit(__doc__)
    run()
