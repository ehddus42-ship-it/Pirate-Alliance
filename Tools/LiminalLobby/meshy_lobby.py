"""Characters of the active lobby (hunter association plaza), made with Meshy and rigged for Humanoid import.

  python Tools/LiminalLobby/meshy_lobby.py run [--character player_casual]

- player_casual: the playable hunter in everyday clothes (the "gap" between daily life and dungeon outfit
  from the concept deck). Only the rigged model is needed: in Unity it plays the player's own Humanoid
  locomotion controller.
- association_staff: the Hunter Association agent NPC who runs permanent upgrades. Rigged, with library
  idle and talking animations baked into FBX files.

Every character is generated in an A-pose (text-to-3D preview + refine), rigged with Meshy rigging, then
animated with library actions. The API key is read from MESHY_API_KEY only. Task ids are kept in
Tools/LiminalLobby/Source/<character>/state.json (git-ignored) so a rerun resumes the same paid tasks.
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
CACHE = ROOT / 'Tools/LiminalLobby/Source'
OUT = ROOT / 'Assets/Liminal/Resources/LiminalLobby'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'
STYLE = (' Anime-style stylized game character, clean toon-like proportions, full body, standing straight in an'
         ' A-pose with arms angled down away from the body, legs slightly apart, facing forward, symmetrical,'
         ' separated fingers, no props in hands, no base, no background.')
CHARACTERS = {
    'player_casual': dict(
        prompt='A young woman hunter in her everyday clothes: very long straight silver-white hair, an oversized soft'
               ' light grey hoodie with the hood down, black shorts, black tights, white sneakers, a small ID lanyard'
               ' around the neck, relaxed sleepy look.',
        texture='Silver-white hair, light heather grey hoodie, black shorts and tights, white sneakers, light skin,'
                ' violet eyes, soft anime shading.',
        height=1.62, actions=[]),
    'association_staff': dict(
        prompt='A Hunter Association agent woman: brown hair tied in a neat low bun, thin glasses, a fitted black'
               ' business suit jacket and pencil skirt, white shirt with a dark tie, black low heels, a silver wing'
               ' emblem badge on the chest, calm professional expression.',
        texture='Dark brown hair, black suit, crisp white shirt, dark navy tie, silver wing badge, light skin,'
                ' brown eyes, soft anime shading.',
        height=1.66, actions=[('idle', 0), ('talk', 313)]),
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
            raise RuntimeError(f'Meshy HTTP {exc.code} {path}: {exc.read().decode(errors="replace")[:500]}') from None
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


def wait(endpoint, task_id, label, folder):
    last = None
    while True:
        result = api(f'{endpoint}/{task_id}')
        save(folder / f'{label}_result.json', result)
        mark = (result.get('status'), result.get('progress'))
        if mark != last:
            print(folder.name, label, *mark, flush=True)
            last = mark
        if result.get('status') == 'SUCCEEDED':
            return result
        if result.get('status') in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{folder.name} {label}: {result.get("task_error")}')
        time.sleep(12)


def fetch(url, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, path)
            return path
        except (urllib.error.ContentTooShortError, urllib.error.URLError, ConnectionError):
            if attempt == 4:
                raise
            time.sleep(5 * (attempt + 1))


def generate(name):
    spec = CHARACTERS[name]
    folder = CACHE / name
    state = load(folder / 'state.json')
    def step(k, fn):
        if k not in state:
            state[k] = fn()
            save(folder / 'state.json', state)
        return state[k]
    preview = dict(mode='preview', prompt=spec['prompt'] + STYLE, ai_model=MODEL, should_remesh=True, topology='triangle',
                   target_polycount=24000, target_formats=['glb', 'fbx'], pose_mode='a-pose')
    pid = step('preview', lambda: api('/v2/text-to-3d', preview)['result'])
    wait('/v2/text-to-3d', pid, 'preview', folder)
    rid = step('refine', lambda: api('/v2/text-to-3d', dict(mode='refine', preview_task_id=pid, enable_pbr=False,
                                                            texture_resolution='2k', texture_prompt=spec['texture'],
                                                            target_formats=['glb', 'fbx']))['result'])
    refine = wait('/v2/text-to-3d', rid, 'refine', folder)
    if refine.get('thumbnail_url'):
        fetch(refine['thumbnail_url'], folder / 'thumbnail.png')
    rig_id = step('rig', lambda: api('/v1/rigging', dict(input_task_id=rid, height_meters=spec['height']))['result'])
    rig = wait('/v1/rigging', rig_id, 'rig', folder)
    out = OUT / name
    fbx = rig.get('result', {}).get('rigged_character_fbx_url')
    if fbx:
        fetch(fbx, out / f'{name}.fbx')
    for label, action in spec['actions']:
        aid = step('anim_' + label, lambda: api('/v1/animations', dict(rig_task_id=rig_id, action_id=action))['result'])
        anim = wait('/v1/animations', aid, 'anim_' + label, folder)
        url = anim.get('result', {}).get('animation_fbx_url')
        if url:
            fetch(url, out / f'{name}@{label}.fbx')
    save(out / 'provenance.json', dict(provider='Meshy AI', model=MODEL, apis=['text-to-3d v2 (a-pose)', 'rigging v1', 'animations v1'],
                                       prompt=spec['prompt'] + STYLE, texture_prompt=spec['texture'], height_m=spec['height'],
                                       library_actions={l: a for l, a in spec['actions']}, tasks=state,
                                       created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    print('READY', name, flush=True)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('command', choices=['run'])
    ap.add_argument('--character', action='append', choices=list(CHARACTERS))
    args = ap.parse_args()
    for name in args.character or CHARACTERS:
        generate(name)
