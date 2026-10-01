"""Characters of the active lobby (hunter association plaza), made with Meshy and rigged for Humanoid import.

  python Tools/LiminalLobby/meshy_lobby.py run [--character player_casual]
  python Tools/LiminalLobby/meshy_lobby.py concept [--character association_agent]   # 2D candidates only
  python Tools/LiminalLobby/meshy_lobby.py pick association_agent 1                  # choose a candidate

- player_casual: the playable hunter in everyday clothes (the "gap" between daily life and dungeon outfit
  from the concept deck). Only the rigged model is needed: in Unity it plays the player's own Humanoid
  locomotion controller.
- association_staff: the Hunter Association agent NPC who runs permanent upgrades. Rigged, with library
  idle and talking animations baked into FBX files.

Text characters are generated in an A-pose (text-to-3D preview + refine). Association officials follow the
concept art instead: the reference crops in Tools/LiminalLobby/Reference are redrawn as full-body A-pose
character sheets (image-to-image, two candidate models), one candidate is picked, then image-to-3D builds it
in an A-pose. Every character is rigged with Meshy rigging and animated with library actions; officials keep
only the armature in their clip FBX files (the mesh lives once in <name>.fbx). The API key is read from MESHY_API_KEY only. Task ids are kept in
Tools/LiminalLobby/Source/<character>/state.json (git-ignored) so a rerun resumes the same paid tasks.
"""
import argparse
import base64
import datetime
import json
import os
import pathlib
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/LiminalLobby/Source'
REFERENCE = ROOT / 'Tools/LiminalLobby/Reference'
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


# ---- association officials: concept art -> image-to-image sheet -> image-to-3D -------------------------------
CONCEPT_MODELS = ['nano-banana-pro', 'gpt-image-2']
CONCEPT_STYLE = (' Draw ONE character only, as a clean 3D-modelling reference sheet: full body from the top of the head'
                 ' to the soles of the shoes, front view facing the camera, standing straight in a relaxed A-pose with'
                 ' both arms held about 30 degrees away from the body, palms facing the thighs, fingers relaxed and'
                 ' separated, empty hands holding nothing, feet slightly apart and flat on the ground. Plain pure white'
                 ' background, no floor shadow, no text, no frame, even soft studio lighting. Keep the clean cel-shaded'
                 ' anime art style and the uniform colours of the reference image.')
UNIFORM = ('the same Korea Hunter Association uniform design as the woman in the reference: charcoal grey, a fitted'
           ' single-breasted jacket with a black leather belt and brass buckle at the waist, crisp white shirt, slim'
           ' black tie, a white ID card on a lanyard clipped to the chest, a small gold wing emblem pin on the lapel')
OFFICIALS = {
    'association_agent': dict(
        refs=['official.png'], height=1.65,
        concept='Redraw the woman in the reference image exactly as she is: a Korea Hunter Association agent, a Korean'
                ' woman in her late 20s, dark brown hair tied in a low loose bun with soft side bangs, thin round silver'
                ' glasses, lavender-grey eyes, calm gentle smile. ' + UNIFORM[0].upper() + UNIFORM[1:] + ', a'
                ' knee-length pencil skirt, sheer skin-tone tights and black low-heel pumps.',
        # Calm library idles (Idle_3/7/9/11): action 0 "Idle" is a wide, turning ready stance.
        actions=[('idle', 243), ('talk', 313), ('bow', 41)]),
    'association_clerk': dict(
        refs=['official.png'], height=1.76,
        concept='Use ' + UNIFORM + ', but draw a different person: a Korean man in his late 20s, short neat black hair'
                ' with a side part, no glasses, friendly serious face, slim build, straight uniform trousers and black'
                ' leather dress shoes.',
        actions=[('idle', 249), ('walk', 30), ('phone', 312)]),
    'association_officer': dict(
        refs=['official.png'], height=1.60,
        concept='Use ' + UNIFORM + ', but draw a different person: a young Korean woman in her early 20s, straight black'
                ' hair in a shoulder-length bob with a small silver hair clip, no glasses, bright cheerful face, petite'
                ' build, a knee-length pencil skirt, black tights and black flat shoes.',
        actions=[('idle', 247), ('chat', 56), ('walk', 1)]),
    'association_director': dict(
        refs=['official.png'], height=1.75,
        concept='Use ' + UNIFORM + ', but draw a different person: a senior Korean man in his late 40s, short neatly'
                ' combed salt-and-pepper hair, rectangular black glasses, stern but kind face, broad sturdy build, a'
                ' gold-trimmed armband with the white wing emblem on the left upper arm, straight uniform trousers and'
                ' black leather dress shoes.',
        actions=[('idle', 251), ('talk', 314)]),
    'association_guard': dict(
        refs=['guards.png'], height=1.82,
        concept='Turn the back-view guards in the reference into one front-view character: a Korea Hunter Association'
                ' gate security guard, a Korean man in a slate grey tactical uniform, a matte grey combat helmet with a'
                ' chin strap, a tactical plate vest with pouches and a large white wing emblem patch on the chest,'
                ' black gloves, a utility belt with pouches and a holstered radio, cargo trousers with knee pads, black'
                ' combat boots, no weapon, face visible under the helmet, calm alert expression.',
        actions=[('idle', 251), ('look', 338)]),
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


def data_uri(path):
    return 'data:image/png;base64,' + base64.b64encode(path.read_bytes()).decode()


def concept(name):
    """Two full-body A-pose character sheets from the reference crops, one per image model (nothing 3D yet)."""
    spec = OFFICIALS[name]
    folder = CACHE / name
    state = load(folder / 'state.json')
    refs = [data_uri(REFERENCE / r) for r in spec['refs']]
    for i, model in enumerate(CONCEPT_MODELS):
        k = f'concept_{i}'
        if k not in state:
            state[k] = api('/v1/image-to-image', dict(ai_model=model, prompt=spec['concept'] + CONCEPT_STYLE,
                                                     reference_image_urls=refs, aspect_ratio='3:4'))['result']
            save(folder / 'state.json', state)
        result = wait('/v1/image-to-image', state[k], k, folder)
        if not (folder / f'{k}.png').exists():
            fetch(result['image_urls'][0], folder / f'{k}.png')
    print('CONCEPTS', name, flush=True)


def pick(name, index):
    folder = CACHE / name
    state = load(folder / 'state.json')
    if f'concept_{index}' not in state:
        raise SystemExit(f'{name}: no concept_{index}; run concept first.')
    state['pick'] = index
    save(folder / 'state.json', state)


def generate_official(name):
    spec = OFFICIALS[name]
    folder = CACHE / name
    state = load(folder / 'state.json')
    if 'pick' not in state:
        raise SystemExit(f'{name}: pick a concept first.')
    def step(k, fn):
        if k not in state:
            state[k] = fn()
            save(folder / 'state.json', state)
        return state[k]
    source = state[f'concept_{state["pick"]}']
    mid = step('image3d', lambda: api('/v1/image-to-3d', dict(input_task_id=source, ai_model=MODEL, pose_mode='a-pose',
                                                             should_remesh=True, topology='triangle', target_polycount=20000,
                                                             should_texture=True, enable_pbr=False,
                                                             target_formats=['glb', 'fbx']))['result'])
    model = wait('/v1/image-to-3d', mid, 'image3d', folder)
    out = OUT / name
    if model.get('thumbnail_url'):
        fetch(model['thumbnail_url'], folder / 'thumbnail.png')
    textures = model.get('texture_urls') or []
    if textures and textures[0].get('base_color'):
        fetch(textures[0]['base_color'], out / f'{name}_albedo.png')
    rig_id = step('rig', lambda: api('/v1/rigging', dict(input_task_id=mid, height_meters=spec['height']))['result'])
    rig = wait('/v1/rigging', rig_id, 'rig', folder)
    fbx = rig.get('result', {}).get('rigged_character_fbx_url')
    if fbx:
        fetch(fbx, out / f'{name}.fbx')
    for label, action in spec['actions']:
        aid = step('anim_' + label, lambda: api('/v1/animations', dict(rig_task_id=rig_id, action_id=action,
                                                                    post_process=dict(operation_type='extract_armature')))['result'])
        anim = wait('/v1/animations', aid, 'anim_' + label, folder)
        url = anim.get('result', {}).get('processed_armature_fbx_url') or anim.get('result', {}).get('animation_fbx_url')
        if url:
            fetch(url, out / f'{name}@{label}.fbx')
    save(out / 'provenance.json', dict(provider='Meshy AI', model=MODEL, concept_model=CONCEPT_MODELS[state['pick']],
                                       apis=['image-to-image v1', 'image-to-3d v1 (a-pose)', 'rigging v1',
                                             'animations v1 (extract_armature)'],
                                       reference=spec['refs'], concept_prompt=spec['concept'] + CONCEPT_STYLE,
                                       height_m=spec['height'], library_actions={l: a for l, a in spec['actions']},
                                       tasks=state, created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    print('READY', name, flush=True)

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('command', choices=['run', 'concept', 'pick'])
    ap.add_argument('pick_args', nargs='*', help='pick: <character> <candidate index>')
    ap.add_argument('--character', action='append', choices=list(CHARACTERS) + list(OFFICIALS))
    args = ap.parse_args()
    if args.command == 'pick':
        pick(args.pick_args[0], int(args.pick_args[1]))
    elif args.command == 'concept':
        for name in args.character or OFFICIALS:
            if name in OFFICIALS:
                concept(name)
    else:
        for name in args.character or list(CHARACTERS) + list(OFFICIALS):
            if name in OFFICIALS:
                generate_official(name)
            else:
                generate(name)
