"""Generate the stage-concept prop kit with Meshy; credentials stay in the environment.

Usage: python Tools/StageConcepts/meshy_assets.py run
       python Tools/StageConcepts/optimize_meshy_textures.py
Intermediate API responses, thumbnails, and original GLBs are local cache only.
"""
import argparse
import concurrent.futures
import datetime
import json
import os
import pathlib
import struct
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Tools/StageConcepts/Source'
OUT = ROOT / 'Assets/StageConcepts/Art/Meshy'
BASE = 'https://api.meshy.ai/openapi'
STYLE = ' Single isolated complete game environment prop, full object visible, no background scene, no ground plane, no pedestal, no characters. Strong readable silhouette, efficient low polygon game mesh, large clean forms, fine detail in PBR normal textures, realistic stylized fantasy game quality.'
PROPS = {
    'ancient_tree': ('forest', 6000, 'Ancient enormous magical oak tree with a thick twisted trunk, sweeping exposed roots, three broad upward curving branches and a lush rounded dark green leafy crown. Blue luminous fissures in the bark, moss on roots. Ancient enchanted forest landmark, complete upright tree.', 'Warm dark brown deeply grooved bark, vivid moss, rich emerald leaves, turquoise blue magical cracks, ancient enchanted forest. Fine bark relief in normal map.'),
    'rune_arch': ('forest', 4500, 'Ancient elven stone archway portal, two rough megalithic upright pillars joined by a pointed rounded stone arch, wide empty walk-through opening, trailing thick vines, moss and carved simple spiral runes. Symmetric vertical silhouette, visible open central void. Fantasy forest shrine.', 'Weathered pale grey limestone, emerald moss, muted green vines, cyan glowing carved ancient runes. Detailed normal map stone carving and surface erosion.'),
    'giant_mushrooms': ('forest', 3000, 'Cluster of three giant fantasy mushrooms growing from a small shared organic root foot, one tall largest cap and two short side caps, bulbous ivory stems, wide rounded umbrella caps with visible gills and simple luminous spots. No flat circular base. Enchanted fairy forest.', 'Deep crimson purple mushroom caps with pale cyan luminous speckles, warm ivory stems, fine mushroom gills and fibrous stem normal relief, softly magical forest.'),
    'server_monolith': ('digital', 4500, 'Tall futuristic computer server rack monolith, black rectangular metal tower twice as tall as wide, stacked horizontal server modules, recessed vent grilles, narrow cyan status light strips, thick cables on side, strong geometric silhouette. Computer program prison world architecture.', 'Charcoal matte metal server panels, brushed dark steel, thin electric cyan LED strips, subtle tiny green indicators, fine vents and circuitry in normal maps, clean futuristic machine.'),
    'processor_core': ('digital', 5000, 'Massive futuristic processor core machine, upright central faceted cube held in a thick industrial hexagonal supporting ring, grounded on a broad technical machine foot, four sturdy mechanical connections between cube and ring, futuristic supercomputer dungeon landmark. Complete standalone device.', 'Black graphite and silver brushed metal, bright cyan circuit traces on cube, magenta energy seams, subtle etched microcircuits and vents as normal texture, science fiction computer core.'),
    'data_gateway': ('digital', 4500, 'Futuristic digital gateway arch, two tall rectangular circuit board towers connected at top by squared technological beam, large completely empty rectangular walk-through central opening, thick geometric frame, inset strips and motherboard circuit traces, computer program prison portal.', 'Black graphite circuit panels, electric cyan circuit traces, purple magenta luminous edge strips, metallic silver connectors, crisp sci-fi etched circuitry normal map.'),
    'ruined_tower': ('ruins', 6000, 'Destroyed modern office tower ruin, narrow five-story concrete building shell, shattered upper floors at jagged diagonal, exposed horizontal floor slabs, broken window openings, partly collapsed facade, twisted reinforcing rods, small rubble foot. Post apocalyptic Earth city, no signs, no people.', 'Dusty pale grey cracked concrete, dark empty windows, rusty exposed rebar, scorched dark patches, faded blue glass remnants, chipped surface detailed normal maps, abandoned urban ruin.'),
    'broken_overpass': ('ruins', 5500, 'Collapsed modern highway overpass segment, two heavy concrete support piers holding a broken elevated road deck, one jagged shattered end sloping down, cracked low road barriers, exposed few thick bent reinforcement rods, compact standalone bridge ruin with empty space under it.', 'Weathered cracked grey concrete, faded dark asphalt top with worn yellow lane stripe, rusty steel reinforcement, dust and black soot, chipped normal detail, post apocalypse city.'),
    'wrecked_vehicle': ('ruins', 4000, 'Abandoned wrecked small city bus, long rectangular recognizable bus body, roof partly caved in, shattered window openings, missing front bumper, crooked rusted wheels, fully grounded complete vehicle, post apocalyptic Earth urban ruin. No people, no letters or text.', 'Heavily rusted faded yellow bus paint, dark shattered windows, brown rust, dusty grey metal, chipped paint and dents in normal texture, weathered abandoned city vehicle.'),
    'cavern_arch': ('cave', 5000, 'Freestanding natural horseshoe rock arch made of fused irregular eroded limestone boulders. Upright inverted U silhouette, two thick rough rocky legs and curved lumpy rock bridge. Huge central hole passes completely through front and back. Thin depth relative to width. Organic jagged stone silhouette from all directions. No cube, box, flat planes, side walls, back wall, roof slab, cave room, ground plane, pedestal, building, paint or crystals.', 'Natural unpainted neutral grey weathered limestone, subtle warm brown mineral streaks, rough matte erosion, fine cracks and pits in normal map. No blue color, glow, metallic sheen or artificial panels.'),
    'crystal_cluster': ('cave', 3000, 'Cluster of seven large quartz crystals growing from a small irregular natural rock foot, tall thick central hexagonal pointed crystal with varied shorter tilted hexagonal spikes around it, strong crisp faceted geometry, fantasy deep cave mineral formation, complete freestanding prop.', 'Rich luminous blue turquoise quartz crystals, saturated blue tips, pale cyan edges, dark grey rock foot, subtle mineral surface texture, polished faceted crystal appearance.'),
    'stalagmite_cluster': ('cave', 3500, 'Natural limestone stalagmite cluster, five uneven tapering conical rock spires rising from a single small rough irregular rocky foot, tallest pillar at back, thick bumpy mineral deposits and flowing rock ridges, primitive underground cave formation, no manufactured objects.', 'Damp dark grey and brown limestone, tan layered mineral bands, subtle green mineral staining, rough eroded calcite normal texture, underground cave rock.'),
}


def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    request = urllib.request.Request(BASE + path, data=data, headers={
        'Authorization': 'Bearer ' + os.environ['MESHY_API_KEY'],
        'Content-Type': 'application/json'})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=90) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            # Retry explicit rate-limit responses only; ambiguous POSTs must not duplicate paid tasks.
            if exc.code == 429 and attempt < 3:
                time.sleep(30)
                continue
            message = exc.read().decode('utf-8', errors='replace')[:1000]
            raise RuntimeError(f'Meshy HTTP {exc.code}: {message}') from None


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def wait_task(key, phase, task_id, folder):
    last_status = None
    deadline = time.monotonic() + 2400
    while time.monotonic() < deadline:
        result = api('/v2/text-to-3d/' + task_id)
        save(folder / (phase + '_result.json'), result)
        marker = (result['status'], result.get('progress'))
        if marker != last_status:
            print(key, phase, *marker, flush=True)
            last_status = marker
        if result['status'] == 'SUCCEEDED':
            return result
        if result['status'] in ('FAILED', 'CANCELED'):
            raise RuntimeError(f'{key} {phase}: {result.get("task_error")}')
        time.sleep(25)
    raise TimeoutError(f'{key} {phase} timed out; rerun resumes the same task.')


def inspect_glb(path):
    blob = path.read_bytes()
    _, version, _ = struct.unpack_from('<III', blob)
    assert version == 2
    json_length, _ = struct.unpack_from('<II', blob, 12)
    gltf = json.loads(blob[20:20 + json_length])
    triangles = 0
    vertices = 0
    for mesh in gltf.get('meshes', []):
        for primitive in mesh['primitives']:
            assert primitive.get('mode', 4) == 4, 'Unexpected non-triangle primitive'
            vertices += gltf['accessors'][primitive['attributes']['POSITION']]['count']
            count = gltf['accessors'][primitive['indices']]['count'] if 'indices' in primitive else gltf['accessors'][primitive['attributes']['POSITION']]['count']
            triangles += count // 3
    return {'triangles': triangles, 'vertices': vertices, 'mesh_count': len(gltf.get('meshes', [])),
            'material_count': len(gltf.get('materials', [])), 'embedded_images': len(gltf.get('images', [])),
            'normal_mapped_materials': sum('normalTexture' in m for m in gltf.get('materials', [])), 'bytes': len(blob)}


def generate(key):
    theme, polycount, prompt, texture_prompt = PROPS[key]
    final_file = OUT / key / (key + '.glb')
    final_record = OUT / key / 'provenance.json'
    if final_file.exists() and final_record.exists():
        existing = json.loads(final_record.read_text(encoding='utf-8'))
        print('EXISTING', key, json.dumps(inspect_glb(final_file)), flush=True)
        return existing
    folder = CACHE / key
    folder.mkdir(parents=True, exist_ok=True)
    state_path = folder / 'state.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    if 'preview_id' not in state:
        payload = dict(mode='preview', prompt=prompt + STYLE, ai_model='meshy-7.1',
                       should_remesh=True, topology='triangle', target_polycount=polycount, target_formats=['glb'])
        assert len(payload['prompt']) <= 800
        save(folder / 'preview_request.json', payload)
        state['preview_id'] = api('/v2/text-to-3d', payload)['result']
        save(state_path, state)
        print(key, 'preview submitted', flush=True)
    preview = wait_task(key, 'preview', state['preview_id'], folder)
    if 'refine_id' not in state:
        payload = dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=True,
                       texture_resolution='2k', target_formats=['glb'], texture_prompt=texture_prompt)
        save(folder / 'refine_request.json', payload)
        state['refine_id'] = api('/v2/text-to-3d', payload)['result']
        save(state_path, state)
        print(key, 'refine submitted', flush=True)
    refine = wait_task(key, 'refine', state['refine_id'], folder)
    original = folder / (key + '_source.glb')
    if not original.exists():
        urllib.request.urlretrieve(refine['model_urls']['glb'], original)
    thumbnail = folder / 'thumbnail.png'
    if not thumbnail.exists() and refine.get('thumbnail_url'):
        urllib.request.urlretrieve(refine['thumbnail_url'], thumbnail)
    stats = inspect_glb(original)
    destination = OUT / key
    destination.mkdir(parents=True, exist_ok=True)
    # Meshy already remeshes to the requested triangle budget. Preserve its UVs and PBR normal maps.
    (destination / (key + '.glb')).write_bytes(original.read_bytes())
    provenance = dict(provider='Meshy AI', api='text-to-3d v2', model='meshy-7.1', theme=theme,
                      prompt=prompt + STYLE, texture_prompt=texture_prompt,
                      preview_task_id=state['preview_id'], refine_task_id=state['refine_id'],
                      created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      requested_triangles=polycount, texture_resolution='2K', enable_pbr=True,
                      actual=stats, consumed_credits=preview.get('consumed_credits', 0) + refine.get('consumed_credits', 0),
                      optimization='Meshy triangle remesh before PBR texturing; embedded normal maps preserve surface detail.')
    save(destination / 'provenance.json', provenance)
    print('READY', key, json.dumps(stats), flush=True)
    return provenance


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['balance', 'run', 'inspect'])
    parser.add_argument('--key', choices=list(PROPS))
    parser.add_argument('--workers', type=int, default=4)
    args = parser.parse_args()
    if args.action == 'balance':
        print(json.dumps(api('/v1/balance')))
        return
    keys = [args.key] if args.key else list(PROPS)
    if args.action == 'inspect':
        for key in keys:
            path = OUT / key / (key + '.glb')
            if path.exists():
                print(key, json.dumps(inspect_glb(path)))
        return
    CACHE.mkdir(parents=True, exist_ok=True)
    report = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(generate, key): key for key in keys}
        for future in concurrent.futures.as_completed(futures):
            key = futures[future]
            try:
                report[key] = future.result()
            except Exception as exc:
                report[key] = {'error': str(exc)}
                print('ERROR', key, str(exc), flush=True)
            save(CACHE / 'generation_report.json', report)
    if any('error' in value for value in report.values()):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
