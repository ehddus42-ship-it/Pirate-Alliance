"""Katana motion set for the player, made with Meshy (rigging + animation library + text-to-motion).

The motions are retargetable Humanoid clips, not fitted to the current character model: Meshy rigs a neutral
mannequin, applies library sword attacks and text-to-motion iaido moves to it, and Unity imports the FBX as a
Humanoid avatar so the clips play on Astraia or any later humanoid model.

  python Tools/PlayerMelee/meshy_motion.py mannequin      # text-to-3D neutral mannequin (preview + refine)
  python Tools/PlayerMelee/meshy_motion.py rig            # Meshy rigging of the mannequin
  python Tools/PlayerMelee/meshy_motion.py motions        # text-to-motion clips (prime)
  python Tools/PlayerMelee/meshy_motion.py animate        # library + generated motions onto the rig, GLB + FBX
  python Tools/PlayerMelee/meshy_motion.py status

The API key is read from MESHY_API_KEY only. Task ids live in Tools/PlayerMelee/Source/motion/state.json
(git-ignored) so every step resumes the same paid tasks.
"""
import json
import os
import pathlib
import sys
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/PlayerMelee/Source/motion'
STATE = CACHE / 'state.json'
BASE = 'https://api.meshy.ai/openapi'

MANNEQUIN = ('A neutral humanoid female game character base mesh standing in a relaxed A-pose, arms angled down and away'
             ' from the body, legs slightly apart, simple fitted plain grey bodysuit, bare hands with separated fingers,'
             ' short hair, facing forward, full body, symmetrical, clean proportions, no weapon, no accessories.')
# Yae Sakura (Honkai Impact 3rd) style: one-handed katana, iaido draws, quick light footwork.
MOTIONS = {
    'iai_draw_cut': ('Female samurai iaido: right hand grips the katana at her left hip, she crouches slightly, then '
                     'explosively draws and cuts horizontally from left to right in one fast motion with a lunge step '
                     'forward, holds the finished pose for a moment, then sheathes the sword at the left hip.', 3.0),
    'quick_slash_combo': ('Female swordswoman holding a katana in her right hand performs a fast combo: a horizontal '
                          'slash from right to left across the body, then a rising diagonal backhand slash up to the right, '
                          'light quick steps, then returns to a ready stance.', 3.0),
    'spin_slash': ('Female swordswoman with a katana in her right hand spins once on the spot with a full 360 degree '
                   'horizontal slash at waist height, a light hop forward, and lands back in a ready stance.', 2.5),
    # Second pass: flashier, more acrobatic anime-action candidates (the combo is assembled from the best parts).
    'dash_slash': ('Anime action game swordswoman holding a katana in her right hand: she leans low and darts forward in a '
                   'lightning-fast dash, cutting horizontally as she passes, sliding to a stop in a sharp low pose.', 2.5),
    'pirouette_slash': ('Agile swordswoman with a katana in her right hand: graceful but very fast spinning double slash, '
                        'she pirouettes twice on one foot with the blade extended, elegant cherry blossom dance style.', 3.0),
    'aerial_spin_slash': ('Swordswoman with a katana in her right hand: leaps up, spins horizontally in the air slashing '
                          'around her, and lands in a low crouch with the blade out to the side.', 3.0),
    'rising_launcher': ('Swordswoman with a katana in her right hand: powerful rising uppercut slash that lifts her off the '
                        'ground in a small jump, then a fast diagonal downward slash as she lands.', 2.5),
    'flurry': ('Anime swordswoman with a katana in her right hand performs a flurry of five extremely fast alternating '
               'diagonal slashes while stepping forward, ending with a wide horizontal cut.', 3.0),
    'iai_dash_finisher': ('Samurai woman iaido finisher: hand on the sheathed katana at her left hip, deep crouch, explosive '
                          'dash forward with a horizontal draw cut, slides past and stops with her back turned, then slowly '
                          'and dramatically sheathes the sword.', 3.5),
    'katana_ready_idle': ('Female swordswoman standing in a relaxed ready stance holding a katana low in her right hand, '
                          'slight breathing and weight shift, calm and alert.', 3.0),
}
# Library actions (preview GIFs reviewed): one-handed sword attacks.
LIBRARY = [219, 242, 221, 240, 241]


def key():
    value = os.environ.get('MESHY_API_KEY', '')
    if not value:
        raise SystemExit('MESHY_API_KEY is not set.')
    return value


def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data,
                                 headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.load(r)
        except urllib.error.HTTPError as exc:
            if (exc.code == 429 or (payload is None and exc.code >= 500)) and attempt < 5:
                time.sleep(20)
                continue
            raise RuntimeError(f'Meshy HTTP {exc.code} {path}: {exc.read().decode(errors="replace")[:600]}') from None
        except (urllib.error.URLError, TimeoutError, ConnectionError) as exc:
            if payload is None and attempt < 5:
                time.sleep(15)
                continue
            raise RuntimeError(f'Meshy request failed: {exc}') from None


def load():
    return json.loads(STATE.read_text()) if STATE.exists() else {}


def save(state):
    CACHE.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(state, indent=2))


def wait(path, task_id, label):
    last = None
    while True:
        result = api(f'{path}/{task_id}')
        (CACHE / f'{label}_result.json').write_text(json.dumps(result, indent=2))
        mark = (result.get('status'), result.get('progress'))
        if mark != last:
            print(label, *mark, flush=True)
            last = mark
        if result.get('status') == 'SUCCEEDED':
            return result
        if result.get('status') in ('FAILED', 'CANCELED', 'EXPIRED'):
            raise RuntimeError(f'{label}: {result.get("task_error")}')
        time.sleep(10)


def fetch(url, name):
    CACHE.mkdir(parents=True, exist_ok=True)
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, CACHE / name)
            return CACHE / name
        except (urllib.error.ContentTooShortError, urllib.error.URLError, ConnectionError):
            if attempt == 4:
                raise
            time.sleep(5 * (attempt + 1))


def mannequin():
    s = load()
    if 'mannequin_preview' not in s:
        s['mannequin_preview'] = api('/v2/text-to-3d', dict(mode='preview', prompt=MANNEQUIN, ai_model='meshy-7.1', should_remesh=True,
                                                            topology='triangle', target_polycount=20000, target_formats=['glb']))['result']
        save(s)
    wait('/v2/text-to-3d', s['mannequin_preview'], 'mannequin_preview')
    if 'mannequin_refine' not in s:
        s['mannequin_refine'] = api('/v2/text-to-3d', dict(mode='refine', preview_task_id=s['mannequin_preview'], enable_pbr=False,
                                                           texture_resolution='2k', target_formats=['glb'],
                                                           texture_prompt='plain light grey matte bodysuit, neutral skin, dark hair'))['result']
        save(s)
    r = wait('/v2/text-to-3d', s['mannequin_refine'], 'mannequin_refine')
    fetch(r['model_urls']['glb'], 'mannequin.glb')
    if r.get('thumbnail_url'):
        fetch(r['thumbnail_url'], 'mannequin.png')


def rig():
    s = load()
    if 'rig' not in s:
        s['rig'] = api('/v1/rigging', dict(input_task_id=s['mannequin_refine'], height_meters=1.65))['result']
        save(s)
    r = wait('/v1/rigging', s['rig'], 'rig')
    result = r.get('result', {})
    for k, v in result.items():
        if isinstance(v, str) and v.startswith('http') and ('glb' in k or 'fbx' in k):
            fetch(v, f'rig_{k}.{"fbx" if "fbx" in k else "glb"}')


def motions():
    s = load()
    s.setdefault('motions', {})
    for name, (prompt, duration) in MOTIONS.items():
        if name not in s['motions']:
            s['motions'][name] = api('/v1/text-to-motion', dict(prompt=prompt, mode='prime', duration=duration))['result']
            save(s)
    for name, task in s['motions'].items():
        wait('/v1/text-to-motion', task, f'motion_{name}')


def animate():
    s = load()
    s.setdefault('animations', {})
    jobs = {'library': dict(action_ids=LIBRARY)}
    for name in MOTIONS:
        jobs[name] = dict(motion_task_id=s['motions'][name])
    for name, extra in jobs.items():
        if name not in s['animations']:
            s['animations'][name] = api('/v1/animations', dict(rig_task_id=s['rig'], post_process={'operation_type': 'change_fps', 'fps': 60}, **extra)
                                        if name == 'library' else dict(rig_task_id=s['rig'], **extra))['result']
            save(s)
    for name, task in s['animations'].items():
        r = wait('/v1/animations', task, f'anim_{name}')
        res = r.get('result', {})
        if res.get('animation_glb_url'):
            fetch(res['animation_glb_url'], f'anim_{name}.glb')
        if res.get('animation_fbx_url'):
            fetch(res['animation_fbx_url'], f'anim_{name}.fbx')


def status():
    print(json.dumps(load(), indent=1))


if __name__ == '__main__':
    {'mannequin': mannequin, 'rig': rig, 'motions': motions, 'animate': animate, 'status': status}[sys.argv[1]]()
