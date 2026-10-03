"""Build the stage-concept variation rooms.

  python Tools/StageConcepts/variations/build.py --textures              # (re)generate V2 surface textures
  python Tools/StageConcepts/variations/build.py --unity                 # write materials, meshes, prefabs, stages, layouts,
                                                                          # sync the shared gallery, run the traversal port
  python Tools/StageConcepts/variations/build.py --preview OUT [--theme Forest --room 2 --debug]
                                                   # game-camera PNGs (npm install in preview_web first)

Rooms are authored in forest.py, digital.py, ruins.py and cave.py. Every build runs the traversal check
(capsule r = 0.24 m, the shipped Astraia controller) and the triangle budget before anything is written; a Unity
build then re-checks the written prefabs with validate.py (a port of StageConceptValidation's traversal test).
"""
import argparse
import importlib
import json
import pathlib
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import decor as decor_pass      # noqa: E402
import materials as mt          # noqa: E402
import preview                  # noqa: E402
import room as rm               # noqa: E402
import textures as tx           # noqa: E402

THEMES = ['Forest', 'Digital', 'Ruins', 'Cave']
ROOM_BUDGET = 450_000           # placed triangles per room (Meshy + decoration)
REPORT = rm.ROOT / 'Documentation/StageConcepts/variations-build.json'
TRAVERSAL_REPORT = rm.ROOT / 'Documentation/StageConcepts/variations-traversal.json'


def theme_module(name):
    return importlib.import_module(name.lower())


def build_rooms(themes, rooms=None):
    built = []
    for name in themes:
        module = theme_module(name)
        for factory in module.ROOMS:
            index = int(factory.__name__.split('_')[1])
            if rooms and index not in rooms:
                continue
            r = factory()
            r.decor = decor_pass.dress(name, r)
            built.append((name, module, r))
    return built


def check(r):
    reach = rm.reachable(r)
    decor, models = r.triangle_budget()
    problems = [k for k, ok in reach.items() if isinstance(ok, bool) and not ok]
    if reach.get('spawn_blocked'):
        problems.append('player spawn blocked')
    if decor + models > ROOM_BUDGET:
        problems.append(f'triangles {decor + models} > {ROOM_BUDGET}')
    if r.kind == 'Combat' and len(r.enemies) < 3:
        problems.append('combat room needs three enemy spawns')
    return {'room': r.key, 'title': r.title, 'kind': r.kind, 'decor_triangles': decor, 'model_triangles': models,
            'models': len(r.models), 'lights': len(r.lights), 'missing_meshy': sorted(r.missing),
            'fallbacks': sorted(set(r.fallbacks)), 'reach': reach, 'problems': problems}


def texture_urls(module_materials, scratch):
    """Preview URLs for every texture the materials use (grain .asset maps are decoded once)."""
    import calibrate
    urls = {}
    for m in module_materials:
        if not m.texture or m.texture in urls:
            continue
        if m.texture in mt.GRAIN:
            stem = mt.GRAIN[m.texture]
            for suffix in ('_Base', '_Normal'):
                png = scratch / 'tex' / f'{stem}{suffix}.png'
                if not png.exists():
                    calibrate.decode_texture_asset(mt.TEXTURES / f'{stem}{suffix}.asset', png)
            urls[m.texture] = {'base': f'tex/{stem}_Base.png', 'normal': f'tex/{stem}_Normal.png'}
        else:
            base = f'/repo/Assets/StageConcepts/Textures/V2/{m.texture}'
            urls[m.texture] = {'base': base + '_Base.png', 'normal': base + '_Normal.png'}
            if (mt.V2 / f'{m.texture}_Emission.png').exists():
                urls[m.texture]['emission'] = base + '_Emission.png'
    return urls


def render_previews(built, out, scratch, debug=False, route=False):
    """One scene per room with three gameplay cameras (z = 6, 18, 30) and an overview."""
    out.mkdir(parents=True, exist_ok=True)
    jobs = []
    for name, module, r in built:
        decor = scratch / 'rooms' / f'{r.key}.glb'
        preview.write_decor_glb(r, decor)
        cams = preview.gameplay_cameras() + [preview.overview_camera()]
        scene = preview.scene_json([(r, 0.0, f'rooms/{r.key}.glb')], preview.environment(module.THEME), cams,
                                   [mt.preview_material(m) for m in module.MATERIALS], texture_urls(module.MATERIALS, scratch), debug)
        path = scratch / 'rooms' / f'{r.key}.json'
        path.write_text(json.dumps(scene))
        jobs.append((f'rooms/{r.key}.json', str(out / r.key)))
    for scene, prefix in jobs:
        subprocess.run(['node', 'render.mjs', scene, prefix, '1600', '1000'], cwd=scratch, check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--textures', action='store_true')
    parser.add_argument('--unity', action='store_true')
    parser.add_argument('--preview', type=pathlib.Path)
    # Folder holding the web renderer (viewer.html, render.mjs, node_modules); scene files are written next to it.
    parser.add_argument('--scratch', type=pathlib.Path, default=HERE / 'preview_web')
    parser.add_argument('--theme', action='append', choices=THEMES)
    parser.add_argument('--room', action='append', type=int)
    parser.add_argument('--debug', action='store_true')
    args = parser.parse_args()
    if args.textures:
        tx.write_all(mt.V2)
        print('textures written to', mt.V2)
    themes = args.theme or THEMES
    built = build_rooms(themes, args.room)
    reports = [check(r) for _, _, r in built]
    for rep in reports:
        flag = 'OK ' if not rep['problems'] else 'BAD'
        print(f"{flag} {rep['room']:11} {rep['title']:12} decor {rep['decor_triangles']:6} + meshy {rep['model_triangles']:6} "
              f"models {rep['models']:2} lights {rep['lights']} walk {rep['reach'].get('reached_m2')} m2 "
              f"missing {len(rep['missing_meshy'])} {rep['problems'] or ''}")
    if args.preview:
        render_previews(built, args.preview, args.scratch, args.debug)
    failed = any(rep['problems'] for rep in reports)
    if args.unity:
        import gallery
        import unitybuild
        import validate
        unitybuild.write_all(built, reports)
        if not args.theme and not args.room:
            text = gallery.SCENE.read_text(encoding='utf-8')
            scene, changes = gallery.update(text)
            if changes:
                gallery.ua.write_text(gallery.SCENE, scene)
            print(f'shared gallery: {len(changes)} changes, {gallery.verify()["theme_rooms"]} theme rooms')
        traversal = [validate.check_room(r.key) for _, _, r in built]
        for result in traversal:
            for error in result['errors']:
                print(f"BAD {result['room']}: {error}")
        failed = failed or any(result['errors'] for result in traversal)
        if not args.theme and not args.room:
            TRAVERSAL_REPORT.write_text(json.dumps(traversal, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
        print(f"traversal port: {sum(not t['errors'] for t in traversal)}/{len(traversal)} rooms pass")
    if failed:
        sys.exit(1)


if __name__ == '__main__':
    main()
