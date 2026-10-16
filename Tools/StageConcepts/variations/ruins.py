"""Earth after the collapse: seven rooms of a dead city under a dusty amber sky.

Theme identity: cracked asphalt with faded lane paint, paving slabs, rust, weeds and embers. Collapsed
high-rises with dark window grids stand past the east wall (screen top-right); the west keeps low curbs,
fences and rubble. Seams are checkpoint lines of concrete barriers and sandbags beside each doorway.
"""
import math

import numpy as np

import dress as d
import geom
from materials import Mat
from room import Room, LENGTH, stable_seed

THEME = 2
KEY = 'Ruins'

MATERIALS = [
    Mat('Ruins_Asphalt', 'cfcfca', 0.04, 0.22, 'Asphalt_v2', 0.2, 0.8),
    Mat('Ruins_AsphaltPatch', 'a9aaa4', 0.02, 0.18, 'Asphalt_v2', 0.28, 0.6),
    Mat('Ruins_Sidewalk', 'd7d2c6', 0.02, 0.2, 'Paving_v2', 0.25, 0.8),
    Mat('Ruins_Concrete', '8a8d80', 0.04, 0.2, 'concrete', 0.85, 0.7),
    Mat('Ruins_ConcreteDark', '5f6862', 0.02, 0.18, 'concrete', 0.85, 0.7),
    Mat('Ruins_Brick', '8c5242', 0.02, 0.16, 'concrete', 0.85, 0.7),
    Mat('Ruins_RustedSteel', '895d43', 0.52, 0.24),
    Mat('Ruins_FadedRoadPaint', 'c9b983', 0.01, 0.15),
    Mat('Ruins_Weeds', '5f7447', 0.0, 0.15, None, cull=0),
    Mat('Ruins_Grass', 'b0a878', 0.0, 0.1, 'ForestMoss_v2', 0.45, 0.8),
    Mat('Ruins_Dirt', 'b5ab98', 0.0, 0.14, 'ForestTrail_v2', 0.3, 0.8),
    Mat('Ruins_WindowVoid', '1b2221', 0.22, 0.34),
    Mat('Ruins_Glass', '3a4a52', 0.3, 0.9),
    Mat('Ruins_Soot', '1d1c1a', 0.0, 0.1),
    Mat('Ruins_Water', '4f5a55', 0.05, 0.88, 'Water_v2', 0.2, 0.5),
    Mat('Ruins_Ember', 'e69f51', 0.2, 0.35, emission='f28b31', emission_strength=2.0),
    Mat('Ruins_Fire', 'ffb060', 0.0, 0.3, emission='ff6a1c', emission_strength=3.4),
]
EMISSIVE = {m.name for m in MATERIALS if m.emissive}

TITLES = {1: '도시의 마지막 입구', 2: '끊어진 고가도로', 3: '무너진 주거지', 4: '버려진 주유소', 5: '대피소의 흔적',
          6: '싱크홀 대로', 7: '폐허 광장'}


# ---- shared ruins dressing -------------------------------------------------------------------------
def building(r, x, z, w, depth, h, facing=-1, broken=0.35, window='Ruins_WindowVoid', shell='Ruins_Concrete'):
    """Ruined block: body, jagged broken top, a window grid on the camera-facing west (facing=-1) and south faces."""
    r.kit.box(shell, (x, h / 2, z), (w, h, depth))
    cols = max(1, int(depth / 1.6))
    floors = max(1, int(h / 3.1))
    for f in range(floors):
        y = 1.6 + f * 3.1
        if y > h - 0.8:
            break
        for c in range(cols):
            zz = z - depth / 2 + (c + 0.5) * depth / cols
            if r.rng(0, 1) < 0.9:
                r.kit.box(window, (x + facing * (w / 2 + 0.01), y, zz), (0.06, 1.3, 0.9 * min(1.4, depth / cols * 0.6)))
        for c in range(max(1, int(w / 1.6))):
            xx = x - w / 2 + (c + 0.5) * w / max(1, int(w / 1.6))
            if r.rng(0, 1) < 0.85:
                r.kit.box(window, (xx, y, z - depth / 2 - 0.01), (0.9, 1.3, 0.06))
        r.kit.box('Ruins_ConcreteDark', (x + facing * (w / 2 + 0.06), y - 0.95, z), (0.14, 0.14, depth + 0.1))
    for k in range(int(3 + broken * 6)):
        bx = x + r.rng(-w / 2, w / 2) * 0.8
        bz = z + r.rng(-depth / 2, depth / 2) * 0.8
        bh = r.rng(0.6, 2.8) * broken * 2
        r.kit.box(shell, (bx, h + bh / 2 - 0.1, bz), (r.rng(0.8, w * 0.5), bh, r.rng(0.8, depth * 0.5)),
                  geom.euler_matrix(r.rng(-8, 8), r.rng(0, 30), r.rng(-8, 8)))
    for k in range(3):
        rx = x + r.rng(-w / 2, w / 2) * 0.7
        rz = z + r.rng(-depth / 2, depth / 2) * 0.7
        r.kit.tube('Ruins_RustedSteel', [(rx, h - 0.2, rz), (rx + r.rng(-0.3, 0.3), h + r.rng(0.8, 1.6), rz + r.rng(-0.3, 0.3))], 0.035, 4)


def streetlight(r, x, z, facing=1, lit=True, lean=0.0):
    top = (x + facing * 1.2, 4.4, z)
    r.kit.tube('Ruins_RustedSteel', [(x, 0, z), (x + lean, 3.9, z), top], 0.08, 7, taper=0.9)
    r.kit.box('Ruins_ConcreteDark', (top[0], 4.35, z), (0.75, 0.16, 0.36))
    if lit:
        r.kit.box('Ruins_Ember', (top[0], 4.24, z), (0.5, 0.03, 0.26))
        r.light(f'Streetlight {x:.0f},{z:.0f}', (top[0], 4.0, z), 'ffb46a', 1.7, 8.5)
    r.block(f'Lamp post {x:.0f},{z:.0f}', (x, 1.5, z), (0.35, 3.0, 0.35))


def burning_barrel(r, x, z):
    r.kit.cone('Ruins_RustedSteel', (x, 0.45, z), 0.3, 0.3, 0.9, 10)
    r.kit.disc('Ruins_Soot', (x, 0.9, z), 0.27, 0.03, 10)
    for k in range(5):
        r.kit.cone('Ruins_Fire', (x + r.rng(-0.12, 0.12), 1.05 + k * 0.05, z + r.rng(-0.12, 0.12)), r.rng(0.08, 0.16), 0, r.rng(0.35, 0.6), 5)
    r.light(f'Barrel fire {x:.0f},{z:.0f}', (x, 1.4, z), 'ff8a3a', 2.0, 7.0)
    r.block(f'Barrel {x:.0f},{z:.0f}', (x, 0.5, z), (0.6, 1.0, 0.6))


def lane_paint(r, x_lanes=(-1.6, 1.6), z0=0.5, z1=LENGTH - 0.5, dash=2.6, gap=2.4):
    for x in x_lanes:
        z = z0
        while z < z1:
            if r.rng(0, 1) < 0.85:
                r.kit.box('Ruins_FadedRoadPaint', (x, 0.021, z + dash / 2), (0.12, 0.012, dash * r.rng(0.7, 1.0)))
            z += dash + gap


def road(r, x0=-6.0, x1=6.0, z0=0.0, z1=LENGTH, curb=True):
    r.kit.box('Ruins_Asphalt', ((x0 + x1) / 2, 0.005, (z0 + z1) / 2), (x1 - x0, 0.02, z1 - z0))
    if curb:
        for x in (x0, x1):
            r.kit.box('Ruins_Concrete', (x, 0.07, (z0 + z1) / 2), (0.28, 0.14, z1 - z0))
    for _ in range(int((z1 - z0) / 3)):
        x, z = r.rng(x0 + 0.6, x1 - 0.6), r.rng(z0 + 0.5, z1 - 0.5)
        r.kit.patch('Ruins_AsphaltPatch', (x, 0.018, z), r.rng(0.6, 1.8), r.rng(0.5, 1.6), 0.008, r.rng(0, 360), segments=9, dome=0, wobble=0.25)


def backdrop(r, towers=((18.0, 8.0), (19.0, 27.0)), blocks=7, sidewalk_x=13.1):
    noise = geom.fbm(stable_seed(r.key, 'east'))
    r.kit.box('Ruins_Sidewalk', (15.0, 0.02, LENGTH / 2), (3.8, 0.1, LENGTH))
    r.kit.box('Ruins_Dirt', (24.0, -0.02, LENGTH / 2), (14.2, 0.12, LENGTH))
    r.kit.box('Ruins_Sidewalk', (-15.2, 0.02, LENGTH / 2), (4.2, 0.1, LENGTH))
    r.kit.box('Ruins_Dirt', (-22.5, -0.02, LENGTH / 2), (10.8, 0.12, LENGTH))
    for x, z in towers:
        r.model('ruined_tower', (x, 0, z), (8.5, r.rng(15.0, 19.0), 9.0), r.rng(-15, 15) + 90, collider=None, fallback=fallback_tower)
    placed = 0
    tries = 0
    while placed < blocks and tries < 60:
        tries += 1
        x, z = r.rng(18.5, 26.0), r.rng(2.0, LENGTH - 2.0)
        if any(abs(z - tz) < 6.0 for _, tz in towers):
            continue
        building(r, x, z, r.rng(4.0, 6.5), r.rng(4.5, 7.0), r.rng(6.0, 13.0))
        placed += 1
    for _ in range(4):
        x, z = r.rng(14.2, 17.0), r.rng(2.0, LENGTH - 2.0)
        r.model('rubble_pile', (x, 0, z), (3.6, 1.5, 3.0), r.rng(0, 360), collider=None, fallback=fallback_rubble)
    # West: a low garden wall remnant and weeds on the far side of the west sidewalk.
    z = 0.6
    while z < LENGTH - 0.6:
        h = r.rng(0.4, 1.1)
        r.kit.box('Ruins_Brick' if r.rng(0, 1) < 0.5 else 'Ruins_ConcreteDark', (-17.2, h / 2, z), (0.5, h, r.rng(1.2, 2.4)))
        for _ in range(2):
            weeds(r, r.rng(-19, -13.6), z + r.rng(-1, 1))
        z += r.rng(2.0, 3.2)
    edges(r)


def edges(r):
    """Debris bands inside both walls (with collision): planters, rubble and a guard rail on the west."""
    for side, x in ((1, 12.2), (-1, -12.25)):
        z = 1.4
        while z < LENGTH - 1.4:
            if r.is_clear(x, z, 0.3):
                kind = r.rng(0, 1)
                if side > 0 and kind < 0.35:
                    r.kit.box('Ruins_Concrete', (x, 0.35, z), (1.2, 0.7, 1.2))
                    r.kit.box('Ruins_Dirt', (x, 0.72, z), (1.0, 0.05, 1.0))
                    weeds(r, x, z, 0.7)
                elif kind < 0.75:
                    for k in range(3):
                        r.kit.rock('Ruins_ConcreteDark' if k % 2 else 'Ruins_Concrete', (x + r.rng(-0.4, 0.4), 0.2, z + r.rng(-0.6, 0.6)),
                                   (r.rng(0.5, 1.1), r.rng(0.3, 0.6), r.rng(0.5, 1.0)), r.rng(0, 360), sides=6)
                    if r.rng(0, 1) < 0.4:
                        r.kit.tube('Ruins_RustedSteel', [(x, 0.1, z), (x + 0.2, 0.9, z + 0.3)], 0.03, 4)
                else:
                    weeds(r, x, z, 1.0)
            z += r.rng(1.3, 2.2)
        if side < 0:
            for zz in np.arange(1.0, LENGTH - 0.5, 2.0):
                r.kit.box('Ruins_RustedSteel', (-12.9, 0.55, zz), (0.08, 1.1, 0.08))
            r.kit.box('Ruins_RustedSteel', (-12.9, 0.95, LENGTH / 2), (0.06, 0.18, LENGTH - 1.6))
        r.block(f'Debris band {"east" if side > 0 else "west"}', (x + side * 0.1, 1.0, LENGTH / 2), (1.5, 2.0, LENGTH - 2.8))


def seams(r):
    for z in (0.0, LENGTH):
        inside = 0.45 if z == 0 else LENGTH - 0.45
        for side in (-1, 1):
            r.kit.box('Ruins_Concrete', (side * 3.95, 0.6, inside), (0.8, 1.2, 0.8))
            for k in range(3):
                r.kit.box('Ruins_Ember' if k % 2 else 'Ruins_FadedRoadPaint', (side * 3.95, 0.25 + k * 0.35, inside - (0.41 if z == 0 else -0.41)), (0.8, 0.12, 0.02))
            r.model('concrete_barricade', (side * 6.6, 0, inside), (3.3, 1.25, 0.9), 0, collider=None, long_axis='x', fallback=fallback_barricade)
            for k, x in enumerate((9.2, 10.4, 11.6, 12.6)):
                r.kit.box('Ruins_Concrete' if k % 2 else 'Ruins_ConcreteDark', (side * x, 0.45, inside + r.rng(-0.1, 0.1)), (1.1, 0.9, 0.7),
                          geom.yaw_matrix(r.rng(-6, 6)))
                r.kit.rock('Ruins_Dirt', (side * x, 1.0, inside), (0.9, 0.28, 0.55), r.rng(0, 360), sides=6)


def weeds(r, x, z, scale=1.0):
    for k in range(3):
        r.kit.grass('Ruins_Weeds', (x + r.rng(-0.3, 0.3), 0.02, z + r.rng(-0.3, 0.3)), r.rng(0.3, 0.7) * scale, r.rng(0, 360), blades=6)


def ground_detail(r, cracks=24, weed_count=40, puddles=5, debris=26, x_range=(-11.2, 11.2), z_range=(0.8, 35.6)):
    d.scatter(r, weed_count, x_range, z_range, lambda x, z: weeds(r, x, z), min_spacing=0.9, margin=0.1)
    d.scatter(r, debris, x_range, z_range, lambda x, z: r.kit.rock(
        r.rng_choice(['Ruins_Concrete', 'Ruins_ConcreteDark', 'Ruins_Brick']), (x, 0.08, z), (r.rng(0.25, 0.7), r.rng(0.15, 0.35), r.rng(0.25, 0.6)),
        r.rng(0, 360), sides=6), min_spacing=0.9, margin=0.2)
    d.scatter(r, puddles, x_range, z_range, lambda x, z: puddle(r, x, z), min_spacing=4.0, margin=0.2)
    d.scatter(r, cracks, x_range, z_range, lambda x, z: crack(r, x, z), min_spacing=1.8, margin=0.0)


def puddle(r, x, z):
    outline = d.blob_outline(x, z, r.rng(0.8, 1.8), r.rng(0.6, 1.4), r.rng, 14, 0.22)
    d.flat_polygon(r, 'Ruins_Water', outline, 0.026, 0.01)


def crack(r, x, z):
    a = r.rng(0, math.tau)
    pts = [(x, 0.024, z)]
    for k in range(3):
        a += r.rng(-0.7, 0.7)
        step = r.rng(0.5, 1.1)
        pts.append((pts[-1][0] + math.sin(a) * step, 0.024, pts[-1][2] + math.cos(a) * step))
    r.kit.ribbon('Ruins_Soot', [(p[0], p[2]) for p in pts], 0.05, 0.024, thickness=0.005)


def car(r, key, x, z, yaw, size=None):
    size = size or (2.0, 1.5, 4.2)
    r.model(key, (x, 0, z), size, yaw, collider={'size': (size[0] * 0.85, 1.6, size[2] * 0.85)}, long_axis='z', fallback=fallback_car)


# ---- procedural fallbacks ---------------------------------------------------------------------------
def fallback_tower(r, p, size, yaw):
    building(r, p[0], p[2], size[0] * 0.8, size[2] * 0.8, size[1])


def fallback_rubble(r, p, size, yaw):
    for k in range(7):
        r.kit.slab(r.rng_choice(['Ruins_Concrete', 'Ruins_ConcreteDark', 'Ruins_Brick']), (p[0] + r.rng(-1, 1) * size[0] * 0.3, r.rng(0.2, 0.7), p[2] + r.rng(-1, 1) * size[2] * 0.3),
                   (r.rng(0.8, 1.8), 0.3, r.rng(0.8, 1.6)), r.rng(0, 360), (r.rng(-25, 25), r.rng(-25, 25)))


def fallback_barricade(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    r.kit.box('Ruins_Concrete', np.asarray(p) + rot @ np.array([0, size[1] / 2, 0]), (size[0], size[1], size[2] * 0.6), rot)


def fallback_car(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    c = np.asarray(p, float)
    r.kit.box('Ruins_RustedSteel', c + rot @ np.array([0, 0.55, 0]), (size[0], 0.7, size[2]), rot)
    r.kit.box('Ruins_RustedSteel', c + rot @ np.array([0, 1.15, -0.2]), (size[0] * 0.9, 0.55, size[2] * 0.55), rot)
    r.kit.box('Ruins_WindowVoid', c + rot @ np.array([0, 1.15, -0.2]), (size[0] * 0.92, 0.35, size[2] * 0.5), rot)


def fallback_generic(material):
    def build(r, p, size, yaw):
        rot = geom.yaw_matrix(yaw)
        r.kit.box(material, np.asarray(p) + rot @ np.array([0, size[1] / 2, 0]), (size[0] * 0.9, size[1], size[2] * 0.9), rot)
    return build


# ---- rooms -------------------------------------------------------------------------------------------
def new_room(index, kind, notes):
    r = Room(THEME, index, f'{KEY}_{index:02d}', TITLES[index], kind, stable_seed(KEY, index), notes)
    r.kit.box('Ruins_Sidewalk', (0, -0.10, LENGTH / 2), (26.4, 0.22, LENGTH))
    return r


def room_01():
    r = new_room(1, 'Arrival', 'Last city gate: a highway checkpoint of barriers and a sandbag post; the chicane leads north between wrecks.')
    road(r, -6.5, 6.5, -16.0, LENGTH)
    lane_paint(r, (0.0,), -15, LENGTH - 0.5)
    for x in (-6.2, 6.2):
        r.kit.box('Ruins_FadedRoadPaint', (x - math.copysign(0.3, x), 0.02, 10), (0.12, 0.012, 50))
    # Crosswalk at the checkpoint line.
    for k in range(8):
        r.kit.box('Ruins_FadedRoadPaint', (-5.2 + k * 1.5, 0.021, 7.0), (0.75, 0.012, 2.0))
    r.model('sandbag_bunker', (-8.6, 0, 11.0), (6.2, 3.4, 4.8), 70, collider={'size': (4.6, 2.2, 3.2)}, long_axis='x')
    r.model('concrete_barricade', (-2.4, 0, 13.5), (3.4, 1.3, 1.0), 8, collider={'size': (3.1, 1.3, 0.8)}, long_axis='x')
    r.model('concrete_barricade', (3.2, 0, 18.0), (3.4, 1.3, 1.0), -6, collider={'size': (3.1, 1.3, 0.8)}, long_axis='x')
    r.model('concrete_barricade', (-2.8, 0, 22.5), (3.4, 1.3, 1.0), 12, collider={'size': (3.1, 1.3, 0.8)}, long_axis='x')
    car(r, 'burned_car', 5.2, 9.0, 160)
    car(r, 'wrecked_vehicle', 8.6, 26.0, 184, (2.6, 2.4, 6.4))
    car(r, 'burned_car', -5.6, 30.5, 20)
    r.model('bus_stop_ruin', (10.6, 0, 16.5), (4.2, 2.8, 1.8), 270, collider={'size': (3.8, 2.6, 1.4)}, long_axis='x')
    burning_barrel(r, -5.2, 15.8)
    streetlight(r, 7.2, 4.0, -1)
    streetlight(r, -7.4, 24.0, 1)
    streetlight(r, 7.2, 32.0, -1, lit=False, lean=0.5)
    # South approach: the highway continues past the entry with abandoned cars and dark blocks.
    r.kit.box('Ruins_Dirt', (0, -0.12, -8), (58, 0.2, 16))
    for x, z, yaw, key in ((3.0, -5.0, 175, 'burned_car'), (-3.2, -10.0, 10, 'burned_car'), (4.4, -13.0, 190, 'wrecked_vehicle')):
        car(r, key, x, z, yaw, (2.6, 2.4, 6.4) if key == 'wrecked_vehicle' else None)
    for x, z in ((-14, -6), (-12, -14), (15, -5), (13, -14), (22, -10)):
        building(r, x, z, r.rng(4, 6), r.rng(4, 6), r.rng(5, 11))
    backdrop(r, towers=((18.5, 10.0), (19.5, 30.0)))
    seams(r)
    ground_detail(r)
    return r


def room_02():
    r = new_room(2, 'Combat', 'Broken overpass: the route runs under a severed highway deck through a jam of buses and burned cars.')
    road(r, -8.0, 8.0)
    lane_paint(r, (-2.7, 2.7))
    r.model('broken_overpass', (1.0, 0, 18.4), (14.0, 7.0, 9.5), 90, collider=None, long_axis='x')
    for x in (-5.2, 6.8):
        r.block(f'Overpass pier {x:.0f}', (x, 1.5, 18.4), (1.6, 3.0, 2.2))
    car(r, 'wrecked_vehicle', -4.4, 8.5, 12, (2.6, 2.4, 6.6))
    car(r, 'burned_car', 3.8, 11.5, 200)
    car(r, 'burned_car', -1.4, 26.0, 170)
    car(r, 'wrecked_vehicle', 4.8, 29.5, 192, (2.6, 2.4, 6.6))
    car(r, 'burned_car', -6.6, 31.5, 35)
    r.model('concrete_barricade', (-8.8, 0, 21.0), (3.2, 1.2, 0.9), 88, collider={'size': (3.0, 1.2, 0.8)}, long_axis='x')
    r.model('rubble_pile', (9.4, 0, 22.6), (4.0, 1.8, 3.4), 30, collider={'size': (3.0, 1.6, 2.4)})
    r.model('rubble_pile', (-9.8, 0, 14.0), (3.6, 1.5, 3.0), 200, collider={'size': (2.6, 1.4, 2.2)})
    streetlight(r, 8.6, 6.0, -1)
    streetlight(r, -8.6, 30.0, 1, lit=False, lean=-0.4)
    burning_barrel(r, 1.8, 20.5)
    backdrop(r, towers=((18.0, 6.0), (19.0, 30.0)))
    seams(r)
    ground_detail(r, puddles=7)
    r.enemies = [(-0.8, 0.08, 15.5), (2.8, 0.08, 22.8), (-3.4, 0.08, 29.0)]
    return r


def room_03():
    r = new_room(3, 'Combat', 'Collapsed residential street: apartment facades close in on both sides and rubble slides narrow the middle to a gauntlet.')
    road(r, -4.5, 4.5, curb=True)
    lane_paint(r, (0.0,))
    # Facades inside the room: tall on the east, cut down to ground-floor stumps on the west.
    for z, depth in ((4.5, 6.0), (13.0, 7.0), (27.5, 6.5)):
        building(r, 10.4, z, 4.4, depth, r.rng(9.0, 12.0))
        r.block(f'Block east {z:.0f}', (10.4, 1.5, z), (4.4, 3.0, depth))
    for z, depth in ((8.0, 6.0), (24.0, 7.0), (33.0, 4.6)):
        building(r, -10.2, z, 4.2, depth, r.rng(1.6, 3.2), facing=1, broken=0.6, shell='Ruins_Brick')
        r.block(f'Block west {z:.0f}', (-10.2, 1.5, z), (4.2, 3.0, depth))
    for x, z, s, yaw in ((5.6, 18.5, 5.0, 20), (-5.8, 17.0, 4.6, 200), (6.2, 33.5, 3.4, 110)):
        r.model('rubble_pile', (x, 0, z), (s, 1.9, s * 0.8), yaw, collider={'size': (s * 0.72, 1.8, s * 0.58)})
    car(r, 'burned_car', -2.6, 28.5, 25)
    r.model('bus_stop_ruin', (7.2, 0, 22.2), (4.0, 2.7, 1.7), 270, collider={'size': (3.6, 2.4, 1.3)}, long_axis='x')
    streetlight(r, 4.2, 10.0, -1, lit=True)
    streetlight(r, -4.4, 30.5, 1, lit=False, lean=0.6)
    burning_barrel(r, -3.4, 11.2)
    for _ in range(6):
        x, z = r.rng(-8, 8), r.rng(4, 33)
        if r.is_clear(x, z, 0.5):
            r.kit.slab(r.rng_choice(['Ruins_Concrete', 'Ruins_ConcreteDark']), (x, 0.15, z), (r.rng(1.2, 2.4), 0.25, r.rng(1.0, 2.0)), r.rng(0, 360), (r.rng(-12, 12), r.rng(-12, 12)))
    backdrop(r, towers=((18.5, 18.0),), blocks=8)
    seams(r)
    ground_detail(r, weed_count=55)
    r.light('Street glow', (0, 5.0, 20.0), 'ffc68a', 1.2, 11.0)
    r.enemies = [(0.6, 0.08, 14.0), (-1.4, 0.08, 23.0), (2.2, 0.08, 30.8)]
    return r


def room_04():
    r = new_room(4, 'Combat', 'Abandoned gas station: a canopy and pumps on the east forecourt, a parking lot of wrecks on the west, a toppled billboard north.')
    r.kit.box('Ruins_Asphalt', (-1.5, 0.005, LENGTH / 2), (19.0, 0.02, LENGTH))
    for k in range(7):
        z = 6.0 + k * 3.4
        r.kit.box('Ruins_FadedRoadPaint', (-7.6, 0.021, z), (4.4, 0.012, 0.12))
    r.kit.box('Ruins_Sidewalk', (8.3, 0.03, 16.5), (7.2, 0.06, 14.0))
    r.model('gas_station_ruin', (7.8, 0.06, 16.5), (9.0, 5.4, 6.6), 90, collider=None, long_axis='z')
    for dz in (-2.6, 2.6):
        r.block(f'Pump island {dz:+.0f}', (7.8, 1.0, 16.5 + dz), (1.6, 2.0, 1.4))
    for dz in (-4.2, 4.2):
        for dx in (-3.0, 3.0):
            r.block(f'Canopy column {dx:+.0f}{dz:+.0f}', (7.8 + dx, 1.5, 16.5 + dz), (0.6, 3.0, 0.6))
    car(r, 'burned_car', -7.6, 9.4, 88)
    car(r, 'burned_car', -7.4, 19.6, 95)
    car(r, 'wrecked_vehicle', -2.4, 24.5, 30, (2.6, 2.4, 6.4))
    car(r, 'burned_car', 1.4, 11.0, 210)
    r.model('collapsed_billboard', (7.4, 0, 30.4), (7.6, 6.6, 3.0), 200, collider={'size': (5.0, 3.0, 1.6)}, long_axis='x')
    r.model('concrete_barricade', (-3.2, 0, 31.5), (3.2, 1.2, 0.9), 80, collider={'size': (3.0, 1.2, 0.8)}, long_axis='x')
    burning_barrel(r, 3.4, 22.8)
    streetlight(r, -10.6, 14.0, 1)
    backdrop(r, towers=((19.0, 5.0), (18.5, 29.0)))
    seams(r)
    ground_detail(r, puddles=6)
    r.light('Canopy light', (7.8, 4.2, 16.5), 'ffd6a0', 1.6, 9.0)
    r.enemies = [(-3.8, 0.08, 15.0), (3.8, 0.08, 26.5), (-6.0, 0.08, 28.5)]
    return r


def room_06():
    r = new_room(6, 'Combat', 'Sinkhole avenue: a collapsed crater splits the boulevard into two lanes around its broken rim, streetlights leaning in.')
    road(r, -9.0, 9.0)
    lane_paint(r, (-4.5, 4.5))
    hole = d.blob_outline(-0.4, 18.5, 5.0, 6.4, r.rng, 30, 0.14)
    d.flat_polygon(r, 'Ruins_Soot', hole, 0.03, 0.03)
    inner = d.blob_outline(-0.4, 18.5, 4.0, 5.2, r.rng, 24, 0.12)
    d.flat_polygon(r, 'Ruins_WindowVoid', inner, 0.034, 0.01)
    for i, (x, z) in enumerate(hole):
        a = math.atan2(x + 0.4, z - 18.5)
        r.kit.slab(r.rng_choice(['Ruins_AsphaltPatch', 'Ruins_Concrete']), (x, 0.12, z), (r.rng(1.2, 2.0), 0.26, r.rng(0.8, 1.4)),
                   math.degrees(a) + 90, (r.rng(-20, -8), r.rng(-10, 10)))
        if i % 4 == 0:
            r.kit.tube('Ruins_RustedSteel', [(x, 0.1, z), (x - math.sin(a) * 0.8, 0.6, z - math.cos(a) * 0.8)], 0.035, 4)
    d.polygon_blockers(r, 'Sinkhole', hole, inset=0.35, slices=7)
    r.keep_clear_polygon(hole, 0.6)
    streetlight(r, 3.6, 13.0, -1, lit=False, lean=-0.9)
    streetlight(r, -4.6, 24.0, 1, lit=True, lean=0.7)
    car(r, 'burned_car', 7.0, 8.0, 172)
    car(r, 'wrecked_vehicle', -7.2, 29.0, 8, (2.6, 2.4, 6.4))
    car(r, 'burned_car', 6.6, 30.5, 195)
    r.model('concrete_barricade', (-6.8, 0, 7.0), (3.2, 1.2, 0.9), 20, collider={'size': (3.0, 1.2, 0.8)}, long_axis='x')
    r.model('rubble_pile', (9.0, 0, 20.0), (3.8, 1.7, 3.2), 260, collider={'size': (2.8, 1.6, 2.4)})
    burning_barrel(r, -8.2, 17.2)
    backdrop(r, towers=((18.0, 16.0),), blocks=8)
    seams(r)
    ground_detail(r, puddles=4)
    r.light('Sinkhole underglow', (-0.4, -0.6, 18.5), 'ff7a2a', 1.6, 7.0)
    r.enemies = [(-6.2, 0.08, 15.0), (6.0, 0.08, 20.8), (0.4, 0.08, 29.4)]
    return r


def room_07():
    r = new_room(7, 'Combat', 'Ruined plaza: a dry fountain in a paved square with dead planters and benches, a bus shelter and a fallen billboard.')
    plaza = d.blob_outline(0, 19.0, 10.5, 11.0, r.rng, 36, 0.04)
    d.flat_polygon(r, 'Ruins_Sidewalk', plaza, 0.03, 0.04)
    r.kit.ring('Ruins_Concrete', (0, 0.25, 19.0), 3.4, 4.0, 0.5, 32)
    r.kit.disc('Ruins_Dirt', (0, 0.06, 19.0), 3.45, 0.06, 32)
    r.kit.cone('Ruins_Concrete', (0, 0.8, 19.0), 0.7, 0.55, 1.6, 12)
    r.kit.cone('Ruins_Concrete', (0, 1.7, 19.0), 1.3, 1.1, 0.2, 14)
    r.kit.slab('Ruins_Concrete', (1.8, 0.35, 21.0), (1.6, 0.25, 1.0), 40, (0, 18))
    r.block('Fountain basin', (0, 0.6, 19.0), (6.6, 1.2, 6.6), 45)
    r.block('Fountain basin square', (0, 0.6, 19.0), (6.6, 1.2, 6.6))
    r.keep_clear_circle((0, 19.0), 4.6)
    for k, a in enumerate((20, 110, 200, 290)):
        x, z = math.sin(math.radians(a)) * 7.4, 19.0 + math.cos(math.radians(a)) * 7.4
        r.kit.box('Ruins_Concrete', (x, 0.4, z), (1.6, 0.8, 1.6))
        r.kit.box('Ruins_Dirt', (x, 0.82, z), (1.4, 0.05, 1.4))
        r.kit.tube('Ruins_Soot', [(x, 0.8, z), (x + 0.2, 2.2, z), (x + 0.9, 3.0, z + 0.4)], 0.09, 5)
        r.kit.tube('Ruins_Soot', [(x + 0.1, 2.0, z), (x - 0.6, 2.8, z - 0.3)], 0.05, 4)
        r.block(f'Planter {k}', (x, 0.6, z), (1.6, 1.2, 1.6))
    for x, z, yaw in ((-4.2, 11.0, 0), (4.4, 27.0, 180)):
        r.kit.box('Ruins_RustedSteel', (x, 0.45, z), (2.0, 0.08, 0.5), geom.yaw_matrix(yaw))
        r.kit.box('Ruins_RustedSteel', (x, 0.2, z), (0.08, 0.4, 0.45), geom.yaw_matrix(yaw))
    r.model('bus_stop_ruin', (-9.4, 0, 28.0), (4.2, 2.8, 1.8), 90, collider={'size': (3.8, 2.6, 1.4)}, long_axis='x')
    r.model('collapsed_billboard', (8.6, 0, 9.0), (7.6, 6.6, 3.0), 250, collider={'size': (4.6, 3.0, 1.6)}, long_axis='x')
    r.model('rubble_pile', (8.4, 0, 30.0), (3.6, 1.6, 3.0), 60, collider={'size': (2.6, 1.5, 2.2)})
    car(r, 'burned_car', -8.2, 8.0, 130)
    streetlight(r, 5.0, 3.0, -1)
    streetlight(r, -5.2, 34.0, 1, lit=False, lean=0.4)
    burning_barrel(r, 3.0, 12.4)
    backdrop(r, towers=((18.5, 6.0), (18.0, 27.0)))
    seams(r)
    ground_detail(r, weed_count=34, puddles=3)
    r.enemies = [(-5.6, 0.08, 17.5), (5.8, 0.08, 20.8), (0.0, 0.08, 29.2)]
    return r


def room_05():
    r = new_room(5, 'Threshold', 'Shelter traces: a military compound of sandbag posts and barriers; the exit gate opens on a burnt-out skyline.')
    road(r, -4.0, 4.0, 0.0, 56.0)
    lane_paint(r, (0.0,), 1, 55)
    r.model('sandbag_bunker', (-7.4, 0, 12.0), (6.4, 3.6, 5.0), 80, collider={'size': (4.6, 2.4, 3.4)}, long_axis='x')
    r.model('sandbag_bunker', (7.8, 0, 22.0), (6.4, 3.6, 5.0), 260, collider={'size': (4.6, 2.4, 3.4)}, long_axis='x')
    for x in (-9.0, -5.6, 5.6, 9.0):
        r.model('concrete_barricade', (x, 0, 31.8), (3.2, 1.3, 0.9), 0, collider={'size': (3.0, 1.3, 0.8)}, long_axis='x')
    for side in (-1, 1):
        r.kit.box('Ruins_RustedSteel', (side * 3.9, 2.2, 32.4), (0.25, 4.4, 0.25))
    r.kit.box('Ruins_RustedSteel', (0, 4.35, 32.4), (8.0, 0.2, 0.25))
    r.kit.box('Ruins_Ember', (0, 4.2, 32.2), (1.4, 0.06, 0.1))
    # Watchtower: four legs, platform, low wall (procedural).
    for dx in (-1.0, 1.0):
        for dz in (-1.0, 1.0):
            r.kit.tube('Ruins_RustedSteel', [(8.6 + dx, 0, 8.0 + dz), (8.6 + dx * 0.7, 4.2, 8.0 + dz * 0.7)], 0.08, 5, taper=1.0)
    r.kit.box('Ruins_Concrete', (8.6, 4.3, 8.0), (2.4, 0.2, 2.4))
    r.kit.box('Ruins_RustedSteel', (8.6, 4.9, 8.0 - 1.1), (2.4, 1.0, 0.1))
    r.block('Watchtower', (8.6, 1.5, 8.0), (2.4, 3.0, 2.4))
    for x, z in ((-3.4, 20.0), (3.6, 14.0)):
        for k in range(3):
            r.kit.box('Ruins_Dirt' if k % 2 else 'Ruins_RustedSteel', (x + r.rng(-0.2, 0.2), 0.35 + k * 0.62, z), (0.9, 0.6, 0.9), geom.yaw_matrix(r.rng(0, 30)))
        r.block(f'Crates {x:.0f}', (x, 1.0, z), (1.1, 2.0, 1.1))
    burning_barrel(r, -3.6, 27.0)
    burning_barrel(r, 3.4, 5.8)
    r.model('burned_car', (-9.4, 0, 24.5), (2.0, 1.5, 4.2), 5, collider={'size': (1.7, 1.6, 3.6)}, long_axis='z')
    # North vista: the dead skyline beyond the gate.
    r.kit.box('Ruins_Dirt', (0, -0.12, 47), (58, 0.2, 21.6))
    for x, z, h in ((-10, 46, 18), (11, 44, 22), (-3, 55, 26), (18, 54, 20), (-18, 52, 16)):
        r.model('ruined_tower', (x, 0, z), (9.0, h, 9.0), r.rng(0, 90), collider=None, fallback=fallback_tower)
    for _ in range(6):
        x, z = r.rng(-24, 26), r.rng(40, 56)
        if abs(x) < 6:
            continue
        building(r, x, z, r.rng(4, 7), r.rng(4, 7), r.rng(6, 14))
    backdrop(r, towers=((18.5, 5.0), (19.0, 18.0)))
    seams(r)
    ground_detail(r, weed_count=30)
    r.light('Gate lamp', (0, 3.8, 31.5), 'ffb46a', 1.8, 9.0)
    r.light('Burning city', (4, 8.0, 50), 'ff7a2a', 3.0, 22.0)
    return r


ROOMS = [room_01, room_02, room_03, room_04, room_05, room_06, room_07]
