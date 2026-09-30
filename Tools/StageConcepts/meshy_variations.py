"""Generate the stage-concept variation kit with Meshy: 28 props, seven per theme.

The first concept kit gave every theme only three models, so all five rooms of a theme
reused the same silhouettes. This kit adds seven props per theme that the variation
rooms are designed around (see Documentation/StageConcepts/VariationPlan.md).

Usage, from the repository root (PowerShell or bash):
  python Tools/StageConcepts/meshy_variations.py plan              # asset list, targets and credit estimate
  python Tools/StageConcepts/meshy_variations.py balance           # remaining Meshy credits
  python Tools/StageConcepts/meshy_variations.py run --dry-run     # validate every request offline
  python Tools/StageConcepts/meshy_variations.py run               # generate everything (resumable)
  python Tools/StageConcepts/meshy_variations.py run --key crystal_geode
  python Tools/StageConcepts/meshy_variations.py reject --key crystal_geode --reason "closed rock, no crystals"
  python Tools/StageConcepts/meshy_variations.py inspect

The API key is read from the MESHY_API_KEY environment variable only and is never written to disk.
Task ids live in Tools/StageConcepts/Source/variations/<key>/state.json (git-ignored), so an
interrupted run resumes the same paid tasks instead of submitting new ones. Spending is capped by
--budget (default 1000 credits) across all attempts, including rejected ones.
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
CACHE = ROOT / 'Tools/StageConcepts/Source/variations'
OUT = ROOT / 'Assets/StageConcepts/Art/Meshy'
SUMMARY = ROOT / 'Documentation/StageConcepts/meshy-variations.json'
BASE = 'https://api.meshy.ai/openapi'
MODEL = 'meshy-7.1'  # Same generator as the first twelve concept props, so the kit reads as one family.
TEXTURE_RESOLUTION = '2k'
# Observed on this account on 2026-09-30: preview 20 + refine 10 credits at standard geometry resolution.
ESTIMATED_CREDITS = 30
MAX_PROMPT = 800

STYLE = (' Single isolated complete game environment prop, whole object visible, no background scene, no ground plane,'
         ' no people, no text or logos. Clear readable silhouette from an elevated three-quarter top-down game camera,'
         ' clean game-ready mesh with solid well-defined forms, fine surface detail carried by PBR normal textures,'
         ' stylized realistic fantasy game quality.')

# key: theme, role, target triangles, in-game size W x H x D (m), long horizontal axis, prompt, texture prompt.
# Sizes are the targets the room builder scales to; "long" names the axis the model's longest side is aligned with.
ASSETS = {
    # ---- Forest: moonlit ancient forest -------------------------------------------------------------
    'fallen_giant_log': ('forest', 'landmark', 26000, (10.5, 2.4, 2.6), 'x',
        'Massive fallen ancient tree trunk lying horizontally on its side, very long thick log about five times longer than'
        ' wide, one end splintered and hollow, the other end a short torn root flare, deeply grooved bark, thick moss on the'
        ' upper side, clusters of pale shelf fungi, a few short broken branch stubs. Rests flat and stable like a natural'
        ' bridge. Enchanted moonlit forest.',
        'Dark weathered brown oak bark with deep grooves, lush emerald moss on the top side, pale ivory and lavender shelf'
        ' fungi, damp dark splintered wood inside the hollow end, tiny cyan glowing lichen specks. Fine bark relief in the'
        ' normal map.'),
    'root_archway': ('forest', 'landmark', 28000, (8.0, 6.2, 3.0), 'x',
        'Colossal gnarled tree root archway, several thick twisting roots rise from the ground on both sides and fuse'
        ' overhead into one arch, a large completely open walk-through passage in the middle, knotted braided roots with'
        ' short hanging moss strands, thin depth compared to width, organic gate silhouette. Enchanted forest gateway,'
        ' no tree crown, no leaves on top, no walls.',
        'Dark mahogany brown root bark with twisted grain, patches of vivid green moss, faint turquoise glowing sap lines in'
        ' the cracks, pale grey lichen. Rich organic bark normal detail.'),
    'elven_shrine': ('forest', 'landmark', 30000, (7.5, 6.0, 7.5), None,
        'Ruined circular elven shrine pavilion, round stepped stone platform with six slender carved pillars in a ring,'
        ' a partly collapsed domed roof with the front half broken open, fallen roof fragments on the platform edge, ivy'
        ' climbing two pillars, a small round empty altar stone in the center. Graceful ancient fantasy architecture,'
        ' open interior visible from above.',
        'Weathered pale moonstone limestone with a soft blue grey tint, green moss in the joints, carved leaf motifs, dark'
        ' green ivy, faint cyan glowing rune inlays on the altar. Detailed stone erosion normal map.'),
    'mossy_boulders': ('forest', 'prop', 16000, (4.2, 2.4, 3.6), 'x',
        'Cluster of four large rounded granite boulders of different sizes huddled together, the tallest at the back,'
        ' thick moss caps on the top surfaces, small ferns and grass tufts growing from the crevices between stones, a'
        ' small group of glowing blue mushrooms at the base. Natural forest floor rock formation, no pedestal.',
        'Grey green granite with speckled mineral grain, deep emerald moss caps, olive grass tufts, small luminous blue'
        ' mushrooms, damp dark crevices, fine rock pitting normal detail.'),
    'stone_lantern': ('forest', 'prop', 15000, (0.9, 2.1, 0.9), None,
        'Ancient mossy stone lantern, tall slender carved column on a square stepped base, a curved pagoda-like stone cap'
        ' on top, an open four-sided light chamber holding one faceted glowing crystal, a little moss and a few small'
        ' vines. Elven forest path marker, upright and symmetric.',
        'Weathered grey green carved stone, soft green moss patches, pale mint cyan crystal in the light chamber, darker'
        ' stone inside, subtle lichen. Crisp carved stone normal map.'),
    'guardian_statue': ('forest', 'landmark', 26000, (2.8, 6.4, 2.6), None,
        'Tall weathered stone statue of a hooded elven guardian standing upright on a low round plinth, long cloak falling'
        ' to the feet, both hands resting on the pommel of a long greatsword planted point down in front of the body, face'
        ' hidden in the hood shadow, a crack across one shoulder, ivy wrapping the legs. Solemn ancient forest monument.',
        'Weathered pale grey marble with green moss in the cloak folds, dark ivy leaves, deep hood shadow, faint turquoise'
        ' glow in the carved sword runes, rain streaks. Detailed carved stone normal map.'),
    'wooden_footbridge': ('forest', 'prop', 20000, (2.6, 1.9, 7.0), 'z',
        'Small rustic arched wooden footbridge for crossing a narrow forest stream, gently curved plank deck about three'
        ' times longer than wide, chunky log posts at both ends and in the middle, thick rounded log handrails on both'
        ' sides, rope lashings at the joints, moss on the posts. Complete standalone bridge with both ends resting at the'
        ' same height, no water, no terrain.',
        'Aged dark oak planks with a worn lighter walkway center, rough bark on the log posts and handrails, green moss on'
        ' post tops, hemp rope lashings. Detailed wood grain normal map.'),
    # ---- Digital: computer program prison -----------------------------------------------------------
    'containment_pod': ('digital', 'landmark', 24000, (2.4, 4.6, 2.4), None,
        'Futuristic prison containment pod, tall upright rounded capsule chamber about twice as tall as wide, a thick dark'
        ' metal frame ring around a closed luminous frosted front panel, heavy clamps at the top and bottom, cables and'
        ' hoses plugged into the rear, a small control panel on the side, sturdy stepped base. Sealed empty cell of a'
        ' computer program prison, nobody inside.',
        'Dark graphite metal armor with brushed steel edges, frosted cyan luminous front panel, thin magenta warning light'
        ' strips, black rubber hoses, fine panel seams, bolts and vents in the normal map.'),
    'security_turret': ('digital', 'prop', 18000, (1.8, 2.2, 1.8), None,
        'Sci-fi security sentry turret, heavy octagonal armored base, a turret head with two stubby barrels and one large'
        ' round sensor eye in the middle, cooling fins on the sides, thick cables at the base. Compact threatening guard'
        ' machine, upright and standalone.',
        'Charcoal and gunmetal armor plates with white hazard marking stripes, glowing magenta sensor eye, cyan status'
        ' lights, scuffed edges, fine panel lines and vents in the normal map.'),
    'holo_terminal': ('digital', 'prop', 18000, (2.2, 2.3, 1.3), 'x',
        'Futuristic control terminal console, an angled keyboard desk on a thick central pedestal, a large vertical'
        ' rectangular screen frame above it, side panels with buttons and knobs, cable bundles running into the floor at'
        ' the back, compact sturdy technological workstation. Computer prison control room, no chair.',
        'Dark navy metal casing with brushed silver trim, glowing cyan screen surface with a faint grid pattern, small amber'
        ' and magenta button lights, worn keys, fine seam and vent normal detail.'),
    'coolant_pipes': ('digital', 'prop', 22000, (4.2, 3.2, 2.2), 'x',
        'Industrial coolant pipe assembly, several thick parallel pipes rising from the floor, bending ninety degrees and'
        ' running horizontally, large round valve wheels, a boxy pump housing with gauges, bolted flanges, a small'
        ' cylindrical tank. Compact freestanding machinery cluster, sci-fi server facility plumbing.',
        'Dark steel pipes with cyan glowing coolant bands, silver bolted flanges, yellow and black hazard stripes on the'
        ' pump housing, red valve wheels, light frost on cold sections, fine metal normal detail.'),
    'firewall_pylon': ('digital', 'prop', 15000, (1.0, 4.2, 1.0), None,
        'Tall slim sci-fi laser fence emitter pylon, square tapered tower about four times taller than wide, three stacked'
        ' glowing emitter rings along its height, an armored base with anchor bolts, a short antenna tip on top, one side'
        ' cable. Security firewall barrier post, upright and standalone.',
        'Black graphite armor, bright magenta and cyan emitter rings, silver edges, small amber warning lights, fine'
        ' technical seams in the normal map.'),
    'broken_server_rack': ('digital', 'prop', 22000, (3.4, 2.6, 2.4), 'x',
        'Damaged futuristic server rack toppled diagonally onto a pile of its own broken modules, bent black metal'
        ' cabinet, pulled out drawers, exposed circuit boards and tangled thick cables, cracked data blocks scattered'
        ' around its foot. Corrupted glitching computer sector debris, compact grounded cluster.',
        'Scorched black metal, exposed green and gold circuit boards, glowing corrupted magenta and red cracks, broken'
        ' cyan LED strips, burnt edges, fine circuit normal detail.'),
    'sentinel_robot': ('digital', 'landmark', 30000, (3.6, 5.0, 3.4), None,
        'Giant deactivated robot warden kneeling on one knee with its head bowed, massive armored humanoid machine, broad'
        ' shoulders, heavy blocky limbs, one fist resting on the ground, a large chest core hatch, cables hanging from the'
        ' back, strong geometric silhouette. Dormant guardian of a computer program prison, powered down, standalone.',
        'Dark steel and graphite armor plating with pale grey panel accents, dim cyan glowing eye slit and chest core,'
        ' magenta seam lights, oil stains and scratches, detailed mechanical panel normal map.'),
    # ---- Ruins: Earth after the collapse ------------------------------------------------------------
    'concrete_barricade': ('ruins', 'prop', 18000, (6.2, 1.6, 1.6), 'x',
        'Row of three heavy concrete jersey highway barriers placed end to end, chipped and cracked, a stack of'
        ' weathered sandbags piled against the middle one, a few broken wooden planks leaning on the side, rusty steel'
        ' lifting hooks on top. Post apocalyptic street blockade, grounded compact linear arrangement.',
        'Dirty cracked grey concrete with faded red and white stripe paint, stained brown burlap sandbags, rusty steel,'
        ' dust and black soot streaks, chipped normal detail.'),
    'burned_car': ('ruins', 'prop', 20000, (2.0, 1.5, 4.2), 'z',
        'Burned out abandoned compact hatchback car wreck, blackened body shell, empty window frames, melted tires on bare'
        ' rims, crumpled hood popped open, sagging roof, recognizable small city car proportions. Post apocalyptic Earth'
        ' street, complete grounded vehicle, no license plate.',
        'Charred black and rusty orange metal with patches of scorched pale blue paint, soot, ash grey interior, bare dark'
        ' rims, heavy flaking rust normal detail.'),
    'gas_station_ruin': ('ruins', 'landmark', 30000, (9.0, 5.2, 6.5), 'x',
        'Abandoned roadside gas station canopy ruin, a wide flat roof canopy on four square steel columns, one front'
        ' corner of the roof collapsed and hanging down, two old fuel pumps on a small concrete island beneath, broken'
        ' light panels under the canopy. Post apocalyptic Earth, open structure, no signs, no text.',
        'Faded white and teal painted metal canopy with rust streaks, dirty grey concrete island, sun bleached fuel pumps'
        ' with cracked blank display panels, rust, dust and weeds at the base, fine weathered normal detail.'),
    'bus_stop_ruin': ('ruins', 'prop', 18000, (4.2, 2.8, 1.8), 'x',
        'Ruined city bus stop shelter, rectangular metal frame with a slanted roof, the back and side panels shattered'
        ' leaving only frame edges, a long bench inside, a bent sign pole beside it, small rubble and dry leaves on its'
        ' floor slab. Post apocalyptic street furniture, complete grounded structure, no advertisement.',
        'Rust streaked grey green painted steel frame, a few cracked frosted panel pieces, weathered wooden bench slats,'
        ' dusty concrete slab, dried leaves, fine rust normal detail.'),
    'sandbag_bunker': ('ruins', 'landmark', 24000, (6.5, 3.6, 5.0), 'x',
        'Military checkpoint fortification, a curved waist-high wall of stacked sandbags forming a half circle, a small'
        ' wooden watch post with a corrugated metal roof behind it, stacked supply crates, a tattered canvas tarp, a'
        ' rusty oil drum. Abandoned survivor outpost, grounded compact complex.',
        'Olive and tan burlap sandbags with dirt stains, weathered grey wood, rusty corrugated metal, faded olive canvas,'
        ' dark oil drum, fine fabric and wood normal detail.'),
    'rubble_pile': ('ruins', 'prop', 16000, (4.6, 1.8, 3.8), 'x',
        'Large mound of collapsed building debris, broken flat concrete slabs tilted in a heap, crumbled brick chunks, a'
        ' few bent rusty rebar rods sticking out, a broken window frame, small weeds growing between the pieces. Post'
        ' apocalyptic city rubble, low wide grounded pile.',
        'Dusty grey concrete with exposed aggregate, red brown bricks, rusty rebar, dark soot, pale green weeds, chipped'
        ' fracture normal detail.'),
    'collapsed_billboard': ('ruins', 'landmark', 22000, (7.5, 6.5, 3.0), 'x',
        'Collapsed roadside billboard, a large rectangular blank sign panel torn and tilted down at a steep angle, held up'
        ' by one remaining bent steel lattice leg while the other leg is broken at its base, a catwalk rail on the front'
        ' edge, dangling light fixtures. Post apocalyptic highway landmark, no letters, no advertisement image.',
        'Rusty dark steel lattice and frame, faded torn pale blank panel with water stains, peeling paint, rust, soot,'
        ' fine metal fatigue normal detail.'),
    # ---- Cave: abyssal crystal caverns --------------------------------------------------------------
    'mine_cart': ('cave', 'prop', 18000, (1.6, 1.7, 3.2), 'z',
        'Old wooden mining cart heaped with glowing blue crystal ore chunks, riveted iron bands on a sturdy plank box,'
        ' four small iron wheels resting on a short straight section of rail track with wooden sleepers, a pickaxe'
        ' leaning on the side. Abandoned underground mine prop, grounded and complete.',
        'Dark weathered wood planks, rusty iron bands and wheels, luminous turquoise blue crystal ore, grey stone dust,'
        ' worn steel rails, fine wood grain and rust normal detail.'),
    'mine_support': ('cave', 'prop', 16000, (4.4, 3.8, 1.0), 'x',
        'Wooden mine tunnel support frame, two thick upright timber posts joined by a heavy crossbeam on top, diagonal'
        ' braces in the upper corners, iron brackets and nails, an old oil lantern hanging from the crossbeam, a few loose'
        ' stones at the post feet. Standalone frame with a wide open walk-through gap, no walls, no tunnel.',
        'Dark aged timber with deep grain and splinters, rusty iron brackets, warm amber lantern glass, grey rock dust at'
        ' the base, detailed wood normal map.'),
    'crystal_geode': ('cave', 'landmark', 28000, (5.2, 4.4, 4.6), None,
        'Giant split open geode boulder, a huge rough rounded rock shell broken open on the front side, revealing a hollow'
        ' interior densely packed with large pointed amethyst crystals, several big crystal shards also growing out of the'
        ' top and base. Mystical deep cave centerpiece, grounded and complete.',
        'Dark grey brown rough rock exterior, saturated violet purple amethyst crystals fading to pale lilac tips,'
        ' glittering white quartz rim, subtle faceted crystal normal detail.'),
    'cave_column': ('cave', 'landmark', 24000, (3.2, 8.0, 3.2), None,
        'Massive natural cave column where a stalactite and a stalagmite have fused, a thick hourglass shaped limestone'
        ' pillar with flowing rippled flowstone drapery on its sides, small stalagmites around its wide base, knobbly'
        ' calcite surface, tall vertical silhouette about three times taller than wide. Underground cavern formation, no'
        ' crystals, no man made parts, no ceiling.',
        'Wet tan and grey limestone with cream flowstone bands, darker brown mineral streaks, damp glossy surfaces, fine'
        ' calcite nodules in the normal map.'),
    'glow_fungus': ('cave', 'prop', 16000, (2.6, 2.4, 2.4), None,
        'Cluster of bioluminescent cave fungi growing on a low rock, several large flat shelf mushrooms stacked on the rock'
        ' sides, a group of tall thin-stemmed round capped mushrooms on top, small glowing spore spheres, organic alien'
        ' underground growth. Compact grounded prop.',
        'Dark slate rock, luminous cyan and teal fungus caps with pale undersides, faint violet gills, glowing spore dots,'
        ' moist surfaces, fine organic normal detail.'),
    'abyss_gate': ('cave', 'landmark', 30000, (8.5, 7.0, 3.4), 'x',
        'Ancient carved stone gate set into a rough natural rock outcrop, huge closed double doors with deep carved spiral'
        ' and eye runes, a heavy stone lintel and pillars framing the doors, rough cave rock mass around the frame on the'
        ' sides and top, a few steps in front. Mysterious sealed door to the abyss, front facing, thick and solid.',
        'Dark basalt grey carved stone doors, glowing cyan and violet rune grooves, rough brown grey cave rock around the'
        ' frame, mineral dust, fine chiselled carving normal map.'),
    'mining_crates': ('cave', 'prop', 16000, (3.0, 2.2, 2.4), 'x',
        'Abandoned miners camp supply pile, stacked wooden crates of different sizes, two wooden barrels, a coil of thick'
        ' rope, a pickaxe and a shovel leaning on the crates, a small oil lantern hanging from a short post. Compact'
        ' grounded underground mine prop cluster.',
        'Worn light and dark wooden crate planks, rusty iron bands on the barrels, hemp rope, steel tools, warm amber'
        ' lantern glass, dust, fine wood normal detail.'),
}

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
        if not 15000 <= polycount <= 30000:
            problems.append(f'{key}: target polycount {polycount} is outside the agreed 15k-30k range')
        if theme not in ('forest', 'digital', 'ruins', 'cave') or role not in ('landmark', 'prop'):
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
    provenance = dict(provider='Meshy AI', api='text-to-3d v2', model=MODEL, kit='stage-concept variations',
                      theme=theme, role=role, prompt=full_prompt(key), texture_prompt=texture_prompt,
                      preview_task_id=state['preview_id'], refine_task_id=state['refine_id'],
                      attempt=len(state.get('rejected', [])) + 1, created_utc=now(),
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


def write_summary():
    ledger = load(CACHE / 'ledger.json', {'tasks': []})
    rows = {}
    for key in ASSETS:
        record = load(OUT / key / 'provenance.json', None)
        state = load(CACHE / key / 'state.json', {})
        rows[key] = {'theme': ASSETS[key][0], 'role': ASSETS[key][1], 'requested_triangles': ASSETS[key][2],
                     'status': 'ready' if record else ('in_progress' if 'preview_id' in state else 'pending'),
                     'triangles': record['actual']['triangles'] if record else None,
                     'credits_all_attempts': sum(t['credits'] for t in ledger['tasks'] if t['key'] == key),
                     'rejected_attempts': [r['reason'] for r in state.get('rejected', [])]}
    save(SUMMARY, {'generator': MODEL, 'texture_resolution': '2K', 'updated_utc': now(),
                   'credits_spent_total': sum(t['credits'] for t in ledger['tasks']), 'assets': rows})


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('action', choices=['plan', 'balance', 'run', 'inspect', 'reject'])
    parser.add_argument('--key', action='append', choices=list(ASSETS), help='limit to one or more assets')
    parser.add_argument('--theme', choices=['forest', 'digital', 'ruins', 'cave'])
    parser.add_argument('--workers', type=int, default=4)
    parser.add_argument('--budget', type=int, default=1000, help='credit cap across all attempts')
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
    if args.action == 'reject':
        if not args.key or len(args.key) != 1:
            raise SystemExit('reject needs exactly one --key')
        reject(args.key[0], args.reason)
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
