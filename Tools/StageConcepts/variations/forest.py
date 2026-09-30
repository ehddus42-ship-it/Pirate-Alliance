"""Moonlit ancient forest: seven rooms with different routes, landmarks and floors.

Theme identity: dark leaf-litter floor under blue moonlight, warm stone lanterns, mint rune glow, lavender
mushrooms. Every room has a rising, tree-covered slope past the east wall (screen top-right), a low fern
edge on the west (screen bottom-left) and a hedge-and-root line along both seams beside the doorway.
"""
import math

import numpy as np

import dress as d
import geom
from materials import Mat
from room import Room, LENGTH, stable_seed

THEME = 0
KEY = 'Forest'

MATERIALS = [
    Mat('Forest_Earth', 'd2d4ca', 0.0, 0.14, 'ForestGround_v2', 0.26, 0.9),
    Mat('Forest_Path', 'f0e6d6', 0.0, 0.18, 'ForestTrail_v2', 0.34, 0.8),
    Mat('Forest_Moss', '9fa992', 0.0, 0.10, 'ForestMoss_v2', 0.42, 1.0),
    Mat('Forest_MossLight', 'b9c19f', 0.0, 0.12, 'ForestMoss_v2', 0.55, 0.9),
    Mat('Forest_Cobble', 'a8aea6', 0.02, 0.22, 'Cobble_v2', 0.42, 0.9),
    Mat('Forest_Stone', '8a948b', 0.04, 0.26, 'rock', 0.65, 0.7),
    Mat('Forest_StoneDark', '4d5a5a', 0.02, 0.24, 'rock', 0.65, 0.7),
    Mat('Forest_Roots', '5d4636', 0.0, 0.14, 'leaf', 0.85, 0.7),
    Mat('Forest_Bark', '3d3029', 0.0, 0.12, 'leaf', 0.9, 0.8),
    Mat('Forest_Plank', '75583c', 0.0, 0.22, 'panel', 0.9, 0.35),
    Mat('Forest_Foliage', '41703f', 0.0, 0.16, 'moss', 0.85, 0.7),
    Mat('Forest_FoliageLight', '5f8448', 0.0, 0.16, 'moss', 0.85, 0.7),
    Mat('Forest_FoliageDeep', '1f3a28', 0.0, 0.1, 'moss', 0.7, 0.7),
    Mat('Forest_Reed', '6b8440', 0.0, 0.2, None, cull=0),
    Mat('Forest_Lily', '3f6f3c', 0.0, 0.35),
    Mat('Forest_Water', '2f5460', 0.04, 0.86, 'Water_v2', 0.16, 0.6),
    Mat('Forest_Rune', '7beab5', 0.12, 0.55, emission='46ffba', emission_strength=2.2),
    Mat('Forest_Firefly', 'daf08d', 0.0, 0.5, emission='c3ee60', emission_strength=3.0),
    Mat('Forest_Lantern', 'ffd9a0', 0.0, 0.5, emission='ffb65c', emission_strength=3.2),
    Mat('Forest_Flower', 'c9d8ff', 0.0, 0.4, emission='9fb8ff', emission_strength=1.1),
    Mat('Forest_MushroomCap', '9c80c5', 0.02, 0.45, emission='8a55d8', emission_strength=0.35),
    Mat('Forest_MushroomGill', 'd5dbb6', 0.0, 0.2),
    Mat('Forest_Spore', 'e6d4ff', 0.0, 0.4, emission='c49bff', emission_strength=2.4),
]
EMISSIVE = {m.name for m in MATERIALS if m.emissive}

TITLES = {1: '반딧불의 입구', 2: '달빛 연못', 3: '거대 버섯 습지', 4: '뿌리 회랑', 5: '달빛의 경계',
          6: '잊힌 엘프 성소', 7: '요정의 고리'}


# ---- shared forest dressing -------------------------------------------------------------------------
def seam_profile_east(x):
    return 0.35 + 3.4 * d.smoothstep(13.0, 24.0, x) + 1.2 * d.smoothstep(24.0, 32.0, x)


def seam_profile_west(x):
    return 0.2 + 0.9 * d.smoothstep(-13.0, -22.0, x)


def backdrop(r, tree_z=(6.0, 18.5, 30.0), rise=1.0, clearings=(), undergrowth=True):
    noise = geom.fbm(stable_seed(r.key, 'east'))
    west_noise = geom.fbm(stable_seed(r.key, 'west'))

    def east(x, z):
        return seam_profile_east(x) * rise + (noise(x * 0.18, z * 0.18) - 0.5) * 1.6 * d.smoothstep(13.5, 18, x)

    def west(x, z):
        return seam_profile_west(x) + (west_noise(x * 0.2, z * 0.2) - 0.5) * 0.5 * d.smoothstep(-13.5, -17, x)

    east_h = d.outer_ground(r, 'Forest_Earth', seam_profile_east, east, 13.05, 32.0, res=1.0)
    d.outer_ground(r, 'Forest_Earth', seam_profile_west, west, -26.0, -13.05, res=1.1)
    # Forest edge right behind the east wall: a continuous hedge of mounds.
    z = 0.4
    while z < LENGTH - 0.3:
        h = r.rng(1.5, 2.8)
        x = r.rng(13.3, 14.8)
        r.kit.rock(r.rng_choice(['Forest_Foliage', 'Forest_FoliageDeep', 'Forest_FoliageLight']), (x, east_h(x, z) + h * 0.35, z),
                   (r.rng(2.3, 3.5), h, r.rng(2.0, 3.0)), r.rng(0, 360), sides=8)
        z += r.rng(1.4, 2.2)
    for tz in tree_z:
        x = r.rng(15.5, 19.0)
        r.model('ancient_tree', (x, east_h(x, tz) - 0.3, tz), (8.5, r.rng(13.0, 16.5), 8.5), r.rng(0, 360), collider=None,
                fallback=fallback_tree)
    # Understorey trees on the slope: trunks with layered canopies fill the frame's top-right corner.
    for _ in range(11):
        x, zz = r.rng(15.0, 26.0), r.rng(0.5, LENGTH - 0.5)
        if any(abs(zz - c) < 3.0 for c in tuple(tree_z) + tuple(clearings)):
            continue
        base = east_h(x, zz)
        h = r.rng(5.0, 8.5)
        r.kit.cone('Forest_Bark', (x, base + h * 0.3, zz), r.rng(0.28, 0.42), 0.18, h * 0.62, 7)
        for k in range(3):
            r.kit.rock(r.rng_choice(['Forest_Foliage', 'Forest_FoliageDeep', 'Forest_FoliageDeep']),
                       (x + r.rng(-0.9, 0.9), base + h * (0.58 + k * 0.14), zz + r.rng(-0.9, 0.9)),
                       (r.rng(3.0, 4.6) * (1 - k * 0.2), r.rng(1.8, 2.6), r.rng(3.0, 4.4) * (1 - k * 0.2)), r.rng(0, 360), sides=9)
    for _ in range(10):
        x, zz = r.rng(15.0, 30.0), r.rng(0.5, LENGTH - 0.5)
        h = r.rng(1.2, 2.4)
        r.kit.rock(r.rng_choice(['Forest_Foliage', 'Forest_FoliageDeep']), (x, east_h(x, zz) + h * 0.35, zz), (r.rng(2.5, 4.0), h, r.rng(2.2, 3.6)), r.rng(0, 360))
    # West foreground: low bushes and ferns only (camera side).
    z = 0.8
    while z < LENGTH - 0.5:
        x = r.rng(-15.5, -13.2)
        r.kit.rock('Forest_Foliage' if r.rng(0, 1) < 0.6 else 'Forest_FoliageDeep', (x, 0.45, z), (r.rng(1.6, 2.4), r.rng(0.8, 1.3), r.rng(1.5, 2.2)), r.rng(0, 360))
        for _ in range(2):
            d.tuft(r, 'Forest_Reed', x + r.rng(0.5, 1.8), z + r.rng(-0.6, 0.6), r.rng(0.35, 0.6))
        z += r.rng(1.3, 2.2)
    if undergrowth:
        edge_undergrowth(r)


def edge_undergrowth(r):
    """Bushes, ferns and roots on the inside of both side walls, with matching collision, so the room edge
    reads as dense forest instead of an invisible wall."""
    for side, x0, x1 in ((1, 11.4, 12.9), (-1, -12.9, -11.6)):
        z = 1.2
        while z < LENGTH - 1.2:
            x = r.rng(min(x0, x1), max(x0, x1))
            h = r.rng(0.9, 1.6) if side > 0 else r.rng(0.6, 1.1)
            if r.is_clear(x, z, 0.4):
                r.kit.rock(r.rng_choice(['Forest_Foliage', 'Forest_FoliageLight', 'Forest_FoliageDeep']), (x, h * 0.4, z),
                           (r.rng(1.4, 2.1), h, r.rng(1.2, 1.9)), r.rng(0, 360), sides=8)
                for _ in range(2):
                    d.tuft(r, 'Forest_Reed', x - side * r.rng(0.6, 1.3), z + r.rng(-0.7, 0.7), r.rng(0.3, 0.55), blades=8)
            z += r.rng(1.1, 1.9)
        r.block(f'Undergrowth {"east" if side > 0 else "west"}', ((x0 + x1) / 2 + side * 0.2, 1.0, LENGTH / 2), (abs(x1 - x0) + 0.3, 2.0, LENGTH - 2.4))


def seams(r):
    d.door_posts(r, 'Forest_Stone', 2.5, 0.95, 0.9, cap='Forest_Moss', glow='Forest_Rune')

    def hedge(x, z):
        h = r.rng(1.1, 1.7)
        r.kit.rock('Forest_Foliage' if r.rng(0, 1) < 0.55 else 'Forest_FoliageDeep', (x, h * 0.4, z), (r.rng(1.5, 2.1), h, 0.95), r.rng(0, 360), sides=8)
        if r.rng(0, 1) < 0.35:
            r.kit.rock('Forest_StoneDark', (x + r.rng(-0.4, 0.4), 0.25, z), (0.9, 0.55, 0.7), r.rng(0, 360))

    d.seam_line(r, hedge, spacing=1.35)


def ground_detail(r, moss_patches=18, tufts=46, stones=14, flowers=0, x_range=(-11.2, 11.0), z_range=(0.8, 35.6)):
    def moss(x, z):
        rx, rz = r.rng(0.9, 2.6), r.rng(0.8, 2.2)
        r.kit.patch('Forest_Moss' if r.rng(0, 1) < 0.7 else 'Forest_MossLight', (x, 0.022, z), rx, rz, 0.02, r.rng(0, 360),
                    segments=16, dome=0.2, wobble=0.34)
        for _ in range(int(rx * rz * 2.2)):
            a = r.rng(0, math.tau)
            d.tuft(r, 'Forest_Reed', x + math.sin(a) * rx * r.rng(0.8, 1.05), z + math.cos(a) * rz * r.rng(0.8, 1.05), r.rng(0.18, 0.34), blades=6)

    d.scatter(r, moss_patches, x_range, z_range, moss, min_spacing=1.8, margin=0.2)
    d.scatter(r, tufts, x_range, z_range, lambda x, z: d.tuft(r, 'Forest_Reed', x, z, r.rng(0.22, 0.48)), min_spacing=0.5, margin=0.1)
    d.scatter(r, stones, x_range, z_range, lambda x, z: r.kit.rock(
        'Forest_StoneDark' if r.rng(0, 1) < 0.5 else 'Forest_Stone', (x, 0.1, z), (r.rng(0.35, 0.8), r.rng(0.2, 0.45), r.rng(0.35, 0.7)),
        r.rng(0, 360)), min_spacing=1.2, margin=0.2)
    d.scatter(r, max(3, stones // 3), x_range, z_range, lambda x, z: branch(r, x, z), min_spacing=2.0, margin=0.3)
    if flowers:
        d.scatter(r, flowers, x_range, z_range, lambda x, z: flower(r, x, z), min_spacing=0.35, margin=0.05)


def branch(r, x, z):
    a = r.rng(0, math.tau)
    length = r.rng(1.2, 2.4)
    p0 = (x - math.sin(a) * length / 2, 0.08, z - math.cos(a) * length / 2)
    p1 = (x + math.sin(a) * length / 2, 0.06, z + math.cos(a) * length / 2)
    r.kit.tube('Forest_Bark', [p0, ((p0[0] + p1[0]) / 2, 0.1, (p0[2] + p1[2]) / 2), p1], 0.07, 5)
    mid = ((p0[0] + p1[0]) / 2, 0.08, (p0[2] + p1[2]) / 2)
    r.kit.tube('Forest_Bark', [mid, (mid[0] + math.cos(a) * 0.6, 0.12, mid[2] - math.sin(a) * 0.6)], 0.035, 4)


def flower(r, x, z):
    h = r.rng(0.12, 0.3)
    r.kit.cone('Forest_Reed', (x, h * 0.5, z), 0.012, 0.008, h, 4, caps=False)
    r.kit.octahedron('Forest_Flower', (x, h + 0.03, z), (0.05, 0.035, 0.05))


def lantern(r, x, z, light=True, yaw=0.0):
    r.model('stone_lantern', (x, 0, z), (0.9, 2.1, 0.9), yaw, collider={'size': (0.8, 2.0, 0.8)}, fallback=fallback_lantern)
    if light:
        r.light(f'Lantern {x:.0f},{z:.0f}', (x, 1.6, z), 'ffb86a', 1.5, 6.5)


def rune_circle(r, cx, cz, radius, glyphs=12):
    r.kit.disc('Forest_StoneDark', (cx, 0.03, cz), radius + 0.45, 0.06, 28)
    r.kit.ring('Forest_Rune', (cx, 0.07, cz), radius - 0.05, radius, 0.03, 44)
    r.kit.ring('Forest_Stone', (cx, 0.05, cz), radius + 0.1, radius + 0.42, 0.08, 22)
    for i in range(glyphs):
        a = i * 360 / glyphs
        p = (cx + math.sin(math.radians(a)) * (radius - 0.55), 0.075, cz + math.cos(math.radians(a)) * (radius - 0.55))
        r.kit.box('Forest_Rune', p, (0.09, 0.02, 0.34), geom.yaw_matrix(a))
        r.kit.box('Forest_Rune', (p[0] + 0.05, p[1], p[2] + 0.08), (0.2, 0.02, 0.06), geom.yaw_matrix(a))


def roots_around(r, x, z, count=5, reach=2.4, radius=0.16):
    for i in range(count):
        a = math.radians(i * 360 / count + r.rng(-20, 20))
        p0 = (x + math.sin(a) * 0.6, 0.12, z + math.cos(a) * 0.6)
        p1 = (x + math.sin(a) * reach * 0.55, 0.25, z + math.cos(a) * reach * 0.55)
        p2 = (x + math.sin(a) * reach, 0.05, z + math.cos(a) * reach)
        r.kit.tube('Forest_Roots', [p0, p1, p2], radius * r.rng(0.8, 1.2), 6)


# ---- procedural fallbacks (used only when a Meshy model is missing) ---------------------------------
def fallback_tree(r, p, size, yaw):
    x, y, z = p
    h = size[1]
    r.kit.cone('Forest_Bark', (x, y + h * 0.3, z), size[0] * 0.13, size[0] * 0.08, h * 0.6, 9)
    roots_around(r, x, z, 6, size[0] * 0.45, 0.3)
    for k in range(4):
        r.kit.rock('Forest_FoliageDeep' if k % 2 else 'Forest_Foliage', (x + r.rng(-1.2, 1.2), y + h * r.rng(0.55, 0.8), z + r.rng(-1.2, 1.2)),
                   (size[0] * r.rng(0.55, 0.8), h * 0.32, size[2] * r.rng(0.55, 0.8)), r.rng(0, 360), sides=9)


def fallback_lantern(r, p, size, yaw):
    x, _, z = p
    r.kit.box('Forest_Stone', (x, 0.15, z), (0.8, 0.3, 0.8))
    r.kit.box('Forest_Stone', (x, 0.95, z), (0.28, 1.3, 0.28))
    r.kit.box('Forest_Lantern', (x, 1.72, z), (0.36, 0.3, 0.36))
    r.kit.cone('Forest_Stone', (x, 2.0, z), 0.62, 0.05, 0.3, 4, phase=0.5)


def fallback_arch(r, p, size, yaw):
    x, _, z = p
    rot = geom.yaw_matrix(yaw)
    w, h = size[0], size[1]
    for side in (-1, 1):
        base = np.asarray(p) + rot @ np.array([side * w * 0.42, 0, 0])
        top = np.asarray(p) + rot @ np.array([side * w * 0.18, h * 0.95, 0])
        mid = np.asarray(p) + rot @ np.array([side * w * 0.36, h * 0.55, 0])
        r.kit.tube('Forest_Roots', [base, mid, top], 0.55, 8, end_radius=0.3)
        r.kit.tube('Forest_Roots', [base + rot @ np.array([side * 0.8, 0, 0.5]), mid], 0.3, 6)
    r.kit.tube('Forest_Roots', [np.asarray(p) + rot @ np.array([-w * 0.2, h * 0.93, 0]), np.asarray(p) + rot @ np.array([0, h, 0]),
                                np.asarray(p) + rot @ np.array([w * 0.2, h * 0.93, 0])], 0.34, 8, taper=1.0)


def fallback_log(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    half = size[0] * 0.5
    a = np.asarray(p) + rot @ np.array([-half, size[1] * 0.45, 0])
    b = np.asarray(p) + rot @ np.array([half, size[1] * 0.42, 0])
    d.log(r, 'Forest_Bark', a, b, size[1] * 0.45, 10, end_material='Forest_Roots')
    r.kit.rock('Forest_Moss', (a + b) / 2 + np.array([0, size[1] * 0.42, 0]), (size[0] * 0.7, 0.3, size[2] * 0.6), yaw, sides=9)


def fallback_boulders(r, p, size, yaw):
    x, _, z = p
    for k in range(4):
        s = 1 - k * 0.2
        r.kit.rock('Forest_Stone' if k % 2 else 'Forest_StoneDark', (x + r.rng(-1, 1) * size[0] * 0.25, size[1] * 0.35 * s, z + r.rng(-1, 1) * size[2] * 0.25),
                   (size[0] * 0.55 * s, size[1] * 0.8 * s, size[2] * 0.55 * s), r.rng(0, 360))
        r.kit.rock('Forest_Moss', (x, size[1] * 0.72 * s, z), (size[0] * 0.4 * s, 0.25, size[2] * 0.4 * s), r.rng(0, 360))


def fallback_statue(r, p, size, yaw):
    x, _, z = p
    r.kit.disc('Forest_Stone', (x, 0.2, z), size[0] * 0.45, 0.4, 14)
    r.kit.cone('Forest_Stone', (x, size[1] * 0.45, z), size[0] * 0.3, size[0] * 0.14, size[1] * 0.7, 10)
    r.kit.rock('Forest_Stone', (x, size[1] * 0.86, z), (size[0] * 0.3, size[1] * 0.18, size[0] * 0.3), 0)


def fallback_shrine(r, p, size, yaw):
    x, _, z = p
    r.kit.disc('Forest_Stone', (x, 0.2, z), size[0] * 0.5, 0.4, 24)
    for i in range(6):
        a = math.radians(i * 60 + yaw)
        px, pz = x + math.sin(a) * size[0] * 0.38, z + math.cos(a) * size[0] * 0.38
        h = size[1] * (0.75 if i not in (0, 1) else 0.45)
        r.kit.cone('Forest_Stone', (px, 0.4 + h / 2, pz), 0.32, 0.28, h, 10)
    r.kit.disc('Forest_Rune', (x, 0.5, z), 0.8, 0.2, 16)


def fallback_bridge(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    start = np.asarray(p) + rot @ np.array([0, 0, -size[2] / 2])
    end = np.asarray(p) + rot @ np.array([0, 0, size[2] / 2])
    d.planks(r, 'Forest_Plank', [(start[0], start[2]), (end[0], end[2])], size[0] * 0.8, y=0.12)


# ---- rooms -------------------------------------------------------------------------------------------
def new_room(index, kind, notes):
    r = Room(THEME, index, f'{KEY}_{index:02d}', TITLES[index], kind, stable_seed(KEY, index), notes)
    d.base_floor(r, 'Forest_Earth')
    return r


def room_01():
    r = new_room(1, 'Arrival', 'Forest arrival: winding lantern trail from the south treeline through a colossal root archway.')
    trail = [(0, -14), (0.2, -6), (0, 0), (1.0, 4.5), (-0.8, 9.5), (0.4, 13.5), (1.6, 18.5), (0.5, 24.5), (-0.9, 29.5), (0, 36.4)]
    d.path(r, 'Forest_Path', trail, 3.2, jitter=0.14)
    # South approach (visible behind the spawn): the forest continues past the entry.
    noise = geom.fbm(stable_seed(r.key, 'south'))
    r.kit.heightfield('Forest_Earth', -26, 32, -16, 0, 50, 14, lambda x, z: 0.0 + max(0, abs(x) - 3.5) * 0.08 * d.smoothstep(0, -12, z)
                      + (noise(x * 0.2, z * 0.2) - 0.5) * 0.4 * d.smoothstep(3, 8, abs(x)), skirt=-0.6)
    for x, z, h in ((-7.5, -6, 11), (7, -9, 13), (-12, -12, 12), (13, -4, 14), (4.5, -13, 10)):
        r.model('ancient_tree', (x, 0, z), (7.5, h, 7.5), r.rng(0, 360), collider=None, fallback=fallback_tree)
    for _ in range(14):
        x, z = r.rng(-24, 26), r.rng(-15, -1)
        if abs(x) < 3.5:
            continue
        h = r.rng(1.0, 2.4)
        r.kit.rock('Forest_Foliage' if r.rng(0, 1) < 0.5 else 'Forest_FoliageDeep', (x, h * 0.4, z), (r.rng(2, 3.4), h, r.rng(1.8, 3)), r.rng(0, 360))
    backdrop(r, tree_z=(8.0, 21.0, 31.5))
    seams(r)
    r.model('root_archway', (0.3, 0, 13.5), (8.4, 6.4, 3.0), 4, collider=None, long_axis='x', fallback=fallback_arch)
    for side in (-1, 1):
        r.block(f'Archway root {side}', (0.3 + side * 3.55, 1.5, 13.5), (1.5, 3.0, 2.2))
    for x, z in ((-2.4, 6.5), (2.9, 11.0), (-2.0, 18.0), (3.4, 23.5), (-2.9, 29.0)):
        lantern(r, x, z, light=(x, z) in ((-2.4, 6.5), (3.4, 23.5)))
    r.model('mossy_boulders', (-8.6, 0, 9.0), (4.4, 2.3, 3.6), 30, fallback=fallback_boulders)
    r.model('mossy_boulders', (8.2, 0, 27.5), (3.8, 2.0, 3.2), 200, fallback=fallback_boulders)
    r.model('giant_mushrooms', (7.2, 0, 6.8), (2.8, 2.6, 2.7), 60)
    r.model('giant_mushrooms', (-7.0, 0, 30.5), (3.4, 3.2, 3.3), 150)
    r.model('ancient_tree', (10.2, 0, 16.5), (6.5, 11.5, 6.5), 75, collider={'size': (2.2, 3.0, 2.2)}, fallback=fallback_tree)
    roots_around(r, 10.2, 16.5, 6, 3.6, 0.22)
    ground_detail(r, moss_patches=16, tufts=50, stones=12, flowers=36)
    d.fireflies(r, 'Forest_Firefly', 55, (-11, 11), (1, 35))
    r.light('Archway glow', (0.3, 3.4, 13.5), '63ffd1', 1.6, 8.0)
    r.light('Firefly hollow', (-6.5, 1.6, 27), 'c3ee60', 1.2, 7.0)
    return r


def room_02():
    r = new_room(2, 'Combat', 'Moonlit pond fills the west-centre; the route bends round its east bank with a plank jetty into the water.')
    trail = [(0, 0), (1.2, 4.5), (4.8, 10.5), (6.2, 16.5), (5.2, 22.5), (1.8, 28.5), (0, 36.4)]
    d.path(r, 'Forest_Path', trail, 3.0, jitter=0.12)
    pond = d.blob_outline(-4.0, 17.5, 6.2, 8.4, r.rng, 30, 0.12)
    d.flat_polygon(r, 'Forest_Water', pond, 0.045, 0.05)
    r.keep_clear_polygon(pond, 0.2)
    # Muddy bank ring, reeds and lily pads.
    for i, (x, z) in enumerate(pond):
        if i % 2 == 0:
            r.kit.rock('Forest_StoneDark' if i % 3 else 'Forest_Stone', (x, 0.1, z), (r.rng(0.7, 1.3), r.rng(0.25, 0.5), r.rng(0.6, 1.1)), r.rng(0, 360))
        if r.rng(0, 1) < 0.55:
            for k in range(3):
                d.tuft(r, 'Forest_Reed', x + r.rng(-0.5, 0.5), z + r.rng(-0.5, 0.5), r.rng(0.6, 1.1), blades=9)
    for _ in range(14):
        a = r.rng(0, math.tau)
        rr = r.rng(0.3, 0.8)
        x, z = -4.0 + math.sin(a) * 6.2 * rr, 17.5 + math.cos(a) * 8.4 * rr
        if abs(z - 15.2) < 1.2 and x > -3.5:
            continue
        r.kit.cone('Forest_Lily', (x, 0.06, z), r.rng(0.28, 0.5), r.rng(0.26, 0.48), 0.015, 9, caps=True)
    jetty = [(3.1, 15.2), (-2.2, 15.2)]
    d.planks(r, 'Forest_Plank', jetty, 1.5, y=0.1)
    for x in (-2.0, -0.2, 1.6):
        for side in (-1, 1):
            r.kit.cone('Forest_Roots', (x, 0.25, 15.2 + side * 0.72), 0.07, 0.07, 0.55, 6)
    d.polygon_blockers(r, 'Pond', pond, inset=0.5, slices=7, exclude=(-2.6, 3.5, 14.35, 16.05))
    lantern(r, -2.6, 15.2, light=False)
    r.model('mossy_boulders', (-9.8, 0, 10.5), (3.6, 2.0, 3.0), 110, fallback=fallback_boulders)
    r.model('mossy_boulders', (0.8, 0, 22.8), (2.8, 1.5, 2.4), 20, fallback=fallback_boulders)
    r.model('ancient_tree', (10.5, 0, 7.5), (6.5, 11.0, 6.5), 140, collider={'size': (2.2, 3.0, 2.2)}, fallback=fallback_tree)
    roots_around(r, 10.5, 7.5, 6, 3.4, 0.2)
    r.model('giant_mushrooms', (9.8, 0, 29.5), (3.6, 3.4, 3.4), 250)
    r.model('giant_mushrooms', (-10.3, 0, 27.6), (2.6, 2.4, 2.6), 10)
    lantern(r, 3.4, 5.2)
    lantern(r, 7.7, 21.0)
    backdrop(r, tree_z=(4.0, 17.0, 31.0))
    seams(r)
    ground_detail(r, 14, 40, 10, 24)
    d.fireflies(r, 'Forest_Firefly', 45, (-9, 2), (10, 26), (0.3, 1.6))
    r.light('Moon on the pond', (-4.0, 5.0, 17.5), 'a9c7ff', 2.4, 13.0)
    r.enemies = [(6.8, 0.08, 14.0), (3.2, 0.08, 25.0), (-3.0, 0.08, 30.5)]
    return r


def room_03():
    r = new_room(3, 'Combat', 'Giant mushroom marsh: a boardwalk winds between mushroom stands to a raised footbridge over a bog stream; the fight waits on the far bank.')
    route = [(0, 0), (-1.8, 5), (-2.4, 9.0), (0.6, 11.6), (0.6, 22.6), (2.4, 25.8), (-1.4, 29.6), (0, 36.4)]
    r.keep_clear_path(d.densify(route, 0.9), 3.6)
    d.planks(r, 'Forest_Plank', route[:4], 2.0, y=0.08)
    d.planks(r, 'Forest_Plank', route[4:7], 2.0, y=0.08)
    # Bog stream across the room, crossed by the Meshy footbridge on its deck and two plank ramps.
    stream = [(x, 17.2 + 0.8 * math.sin(x * 0.33 + 0.6)) for x in np.linspace(-13.6, 13.6, 30)]
    r.kit.ribbon('Forest_Water', stream, 2.6, 0.035, thickness=0.03, jitter=0.12)
    r.keep_clear_path(stream, 3.4)
    for x, z in stream[::2]:
        for side in (-1, 1):
            if abs(x - 0.6) > 1.8 and r.rng(0, 1) < 0.7:
                d.tuft(r, 'Forest_Reed', x + r.rng(-0.4, 0.4), z + side * r.rng(1.2, 1.6), r.rng(0.6, 1.0), blades=9)
    # Stream colliders run from the walls right up to the deck sides (x = -0.1 and 1.3), so the only way
    # across is the bridge; the last segment tucks under the deck, whose top stays above the blocker.
    def stream_z(x):
        return 17.2 + 0.8 * math.sin(x * 0.33 + 0.6)
    for xa, xb in ((-13.2, -0.1), (1.3, 13.2)):
        xs = np.linspace(xa, xb, max(2, int(round((xb - xa) / 0.95)) + 1))
        for ax, bx in zip(xs, xs[1:]):
            az, bz = stream_z(ax), stream_z(bx)
            yaw = math.degrees(math.atan2(bx - ax, bz - az))
            r.block(f'Bog stream {(ax + bx) / 2:.1f}', ((ax + bx) / 2, d.WATER_BLOCK / 2, (az + bz) / 2),
                    (2.2, d.WATER_BLOCK, math.hypot(bx - ax, bz - az) + 0.35), yaw)
    deck = 0.95
    bridge = r.model('wooden_footbridge', (0.6, 0, 17.2), (2.4, 3.2, 5.2), 0, collider=None, long_axis='z', fit='z',
                     fallback=fallback_bridge)
    if bridge:
        deck = round(bridge.actual[1] * 0.405, 3)
    r.block('Bridge deck', (0.6, deck / 2, 17.2), (1.4, deck, 4.6), walkable=True)
    for side in (-1, 1):
        r.block(f'Bridge rail {side}', (0.6 + side * 0.86, deck + 0.6, 17.2), (0.24, 1.2, 5.0))
    d.ramp_block(r, 'Ramp south', 0.6, 12.1, 14.95, 0.0, deck, 1.5)
    d.ramp_block(r, 'Ramp north', 0.6, 19.45, 22.3, deck, 0.0, 1.5)
    d.plank_ramp(r, 'Forest_Plank', 0.6, 12.1, 14.95, 0.02, deck, 1.5)
    d.plank_ramp(r, 'Forest_Plank', 0.6, 19.45, 22.3, deck, 0.02, 1.5)
    for i in range(12):
        x, z = r.rng(-12, 12), r.rng(2, 34)
        if not r.is_clear(x, z, 1.2):
            continue
        pool = d.blob_outline(x, z, r.rng(1.2, 2.6), r.rng(1.0, 2.2), r.rng, 18, 0.2)
        d.flat_polygon(r, 'Forest_Water', pool, 0.028, 0.03)
        r.keep_clear_polygon(pool, 0.1)
    stands = [((5.4, 9.6), 4.6, 150), ((-6.2, 22.4), 5.2, 20), ((6.0, 30.0), 4.2, 280)]
    for (x, z), size, yaw in stands:
        r.model('giant_mushrooms', (x, 0, z), (size, size * 1.05, size), yaw, collider={'size': (size * 0.42, 3.0, size * 0.42)})
        r.light(f'Mushroom stand {x:.0f}', (x, size * 0.75, z), 'bb89fa', 2.0, 7.5)
    r.model('giant_mushrooms', (10.6, 0, 23.4), (7.2, 7.6, 7.0), 60, collider={'size': (2.6, 3.0, 2.6)})
    r.model('giant_mushrooms', (-10.4, 0, 7.5), (3.0, 3.0, 3.0), 200)
    for (x, z), size, yaw in (((-7.8, 12.0), 2.6, 30), ((8.8, 13.0), 2.9, 250), ((-9.6, 31.0), 2.4, 120), ((10.2, 4.5), 2.2, 300)):
        r.model('glow_fungus', (x, 0, z), (size, size * 0.95, size), yaw, collider={'size': (size * 0.55, 2.0, size * 0.55)})
        r.light(f'Fungus glow {x:.0f}', (x, 1.2, z), '4fe8d9', 1.3, 5.5)
    r.model('mossy_boulders', (-4.2, 0, 12.6), (2.6, 1.4, 2.2), 70, fallback=fallback_boulders)
    for _ in range(40):
        x, z = r.rng(-12.5, 12.5), r.rng(1, 35.5)
        if r.is_clear(x, z, 0.5):
            d.mushroom(r, 'Forest_MushroomCap', 'Forest_MushroomGill', x, z, r.rng(0.18, 0.55))
    backdrop(r, tree_z=(5.0, 28.0))
    seams(r)
    ground_detail(r, 20, 44, 8, 12)
    d.fireflies(r, 'Forest_Spore', 70, (-12, 12), (1, 35), (0.4, 2.6), 0.04)
    r.light('Bridge lantern', (0.6, 2.2, 17.2), 'ffb86a', 1.4, 7.0)
    r.enemies = [(4.6, 0.08, 26.2), (-5.2, 0.08, 27.4), (1.6, 0.08, 32.6)]
    return r


def room_04():
    r = new_room(4, 'Combat', 'Root corridor: a fallen giant blocks the west, a root gate squeezes the route before a clearing opens north.')
    route = [(0, 0), (0.8, 6), (1.8, 12), (1.6, 18.5), (0.2, 24), (-1.6, 29.5), (0, 36.4)]
    d.path(r, 'Forest_Path', route, 2.8, jitter=0.16)
    r.model('fallen_giant_log', (-7.4, 0, 16.6), (11.0, 3.2, 3.0), 12, collider=None, long_axis='x', fallback=fallback_log, fit='x')
    r.block('Fallen log', (-7.4, 1.2, 16.6), (10.4, 2.4, 2.0), 12)
    r.model('root_archway', (1.7, 0, 18.5), (7.6, 6.0, 2.8), -6, collider=None, long_axis='x', fallback=fallback_arch)
    for side in (-1, 1):
        r.block(f'Root gate {side}', (1.7 + side * 3.25, 1.5, 18.5 + side * 0.3), (1.4, 3.0, 2.0), -6)
    r.model('mossy_boulders', (7.2, 0, 17.2), (4.2, 2.4, 3.8), 70, fallback=fallback_boulders)
    r.model('mossy_boulders', (-3.2, 0, 11.0), (2.6, 1.5, 2.2), 180, fallback=fallback_boulders)
    r.model('root_archway', (0.9, 0, 8.6), (6.6, 5.2, 2.6), 8, collider=None, long_axis='x', fallback=fallback_arch)
    for side in (-1, 1):
        r.block(f'Root arch south {side}', (0.9 + side * 2.85, 1.5, 8.6 + side * 0.4), (1.2, 3.0, 1.8), 8)
    r.model('fallen_giant_log', (6.8, 0, 25.4), (7.0, 2.4, 2.4), 150, collider=None, long_axis='x', fallback=fallback_log, fit='x')
    r.block('Fallen log north', (6.8, 1.0, 25.4), (6.4, 2.0, 1.7), 150)
    r.model('ancient_tree', (8.2, 0, 30.2), (7.2, 12.0, 7.2), 20, collider={'size': (2.4, 3.0, 2.4)}, fallback=fallback_tree)
    roots_around(r, 8.2, 30.2, 7, 4.0, 0.25)
    r.model('ancient_tree', (10.6, 0, 5.0), (6.0, 10.0, 6.0), 300, collider={'size': (2.0, 3.0, 2.0)}, fallback=fallback_tree)
    lantern(r, 3.9, 13.6)
    lantern(r, -2.3, 27.6)
    backdrop(r, tree_z=(12.0, 24.0, 34.0))
    seams(r)
    ground_detail(r, 18, 52, 14, 20)
    d.fireflies(r, 'Forest_Firefly', 40, (-10, 10), (20, 35))
    r.light('Clearing moonbeam', (-1.0, 6.0, 29.0), 'b3ccff', 2.2, 12.0)
    r.enemies = [(-1.0, 0.08, 25.5), (4.6, 0.08, 29.0), (-6.0, 0.08, 31.5)]
    return r


def room_06():
    r = new_room(6, 'Combat', 'Forgotten elven sanctuary: a ruined round shrine on a cobbled plaza that the route circles, with broken pillars as cover.')
    d.path(r, 'Forest_Cobble', [(0, 0), (0.0, 13.0)], 3.4, jitter=0.0)
    d.path(r, 'Forest_Cobble', [(0.0, 29.0), (0, 36.4)], 3.4, jitter=0.0)
    plaza = d.blob_outline(0, 21, 8.4, 8.4, r.rng, 40, 0.02)
    d.flat_polygon(r, 'Forest_Cobble', plaza, 0.025, 0.03)
    r.kit.ring('Forest_Stone', (0, 0.05, 21), 8.3, 8.8, 0.1, 44)
    rune_circle(r, 0, 21, 5.6, 16)
    r.model('elven_shrine', (0, 0, 21.0), (7.2, 6.0, 7.2), 0, collider={'size': (4.6, 3.0, 4.6)}, fallback=fallback_shrine)
    r.block('Shrine skirt', (0, 1.5, 21.0), (4.6, 3.0, 4.6), 45)
    r.keep_clear_circle((0, 21), 8.6)
    for side in (-1, 1):
        r.model('guardian_statue', (side * 4.4, 0, 12.2), (2.6, 6.0, 2.4), 180, collider={'size': (1.6, 3.0, 1.6)}, fallback=fallback_statue)
    lantern(r, -2.6, 5.5)
    lantern(r, 2.6, 5.5, light=False)
    # Broken colonnade ring (cover). Standing drums, a toppled column and scattered blocks.
    for i, a in enumerate((35, 80, 125, 235, 280, 325)):
        x, z = math.sin(math.radians(a)) * 10.2, 21 + math.cos(math.radians(a)) * 10.2
        if abs(x) > 11.8:
            x = math.copysign(11.8, x)
        h = (2.6, 1.3, 3.2, 2.0, 1.1, 2.8)[i]
        r.kit.cone('Forest_Stone', (x, h / 2, z), 0.55, 0.5, h, 12)
        r.kit.cone('Forest_Stone', (x, h + 0.12, z), 0.7, 0.7, 0.24, 12)
        r.kit.rock('Forest_Moss', (x, h + 0.25, z), (1.0, 0.18, 1.0), r.rng(0, 360))
        r.block(f'Pillar {i}', (x, 1.5, z), (1.1, 3.0, 1.1))
    d.log(r, 'Forest_Stone', (-9.5, 0.45, 27.5), (-5.6, 0.45, 31.6), 0.5, 12)
    r.block('Toppled pillar', (-7.55, 0.5, 29.55), (5.3, 1.0, 1.0), 45)
    backdrop(r, tree_z=(6.0, 20.0, 33.0))
    seams(r)
    ground_detail(r, 12, 40, 10, 30)
    d.fireflies(r, 'Forest_Firefly', 30, (-9, 9), (12, 30), (1.0, 3.5))
    r.light('Shrine rune glow', (0, 1.2, 21), '63ffd1', 2.6, 10.0)
    r.light('Moonbeam', (0, 8.0, 21), 'b8ccff', 1.8, 14.0)
    r.enemies = [(-5.8, 0.08, 20.0), (5.8, 0.08, 22.5), (0.4, 0.08, 30.6)]
    return r


def room_07():
    r = new_room(7, 'Combat', 'Fairy ring glade: an open meadow inside a broken circle of standing stones, a mushroom ring glowing at its heart.')
    d.path(r, 'Forest_Path', [(0, 0), (-0.8, 6), (-1.0, 11)], 2.8)
    d.path(r, 'Forest_Path', [(1.0, 28), (0.4, 32), (0, 36.4)], 2.8)
    meadow = d.blob_outline(-0.5, 19.5, 9.5, 9.0, r.rng, 36, 0.1)
    d.flat_polygon(r, 'Forest_Moss', meadow, 0.022, 0.025)
    for k in range(22):
        a = k * math.tau / 22
        x, z = -0.5 + math.sin(a) * 4.8, 19.5 + math.cos(a) * 4.8
        d.mushroom(r, 'Forest_MushroomCap', 'Forest_MushroomGill', x, z, r.rng(0.22, 0.42))
    stones = [(15, 3.2), (60, 2.4), (100, 3.6), (150, 2.8), (205, 3.3), (250, 2.2), (300, 3.0), (340, 2.6)]
    for i, (a, h) in enumerate(stones):
        x, z = -0.5 + math.sin(math.radians(a)) * 9.6, 19.5 + math.cos(math.radians(a)) * 9.2
        if abs(x) > 11.6:
            x = math.copysign(11.6, x)
        r.kit.rock('Forest_Stone' if i % 2 else 'Forest_StoneDark', (x, h * 0.48, z), (1.2, h, 0.9), a + 90, sides=7,
                   rounds=[(-0.5, 0.5), (-0.15, 0.52), (0.2, 0.46), (0.45, 0.28)])
        r.kit.ring('Forest_Rune', (x, h * 0.55, z), 0.62, 0.66, 0.06, 14)
        r.block(f'Standing stone {i}', (x, 1.5, z), (1.1, 3.0, 0.9), a + 90)
    r.model('rune_arch', (-10.4, 0, 20.5), (6.0, 6.6, 3.0), 90, collider={'size': (1.2, 3.0, 5.4)})
    r.model('mossy_boulders', (8.8, 0, 8.5), (4.0, 2.2, 3.4), 40, fallback=fallback_boulders)
    r.model('mossy_boulders', (8.0, 0, 31.0), (3.4, 1.8, 3.0), 220, fallback=fallback_boulders)
    r.model('guardian_statue', (10.8, 0, 20.0), (2.4, 5.4, 2.2), 270, collider={'size': (1.5, 3.0, 1.5)}, fallback=fallback_statue)
    r.model('ancient_tree', (-9.8, 0, 4.2), (5.0, 7.5, 5.0), 10, collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_tree)
    backdrop(r, tree_z=(7.0, 19.5, 32.0))
    seams(r)
    ground_detail(r, 10, 60, 8, 70)
    d.fireflies(r, 'Forest_Firefly', 50, (-9, 9), (11, 28), (0.4, 2.2))
    r.light('Fairy ring', (-0.5, 1.4, 19.5), 'd1a6ff', 2.4, 9.5)
    r.light('Arch rune', (-9.0, 2.6, 20.5), '63ffd1', 1.4, 7.0)
    r.enemies = [(3.8, 0.08, 14.0), (-4.2, 0.08, 23.5), (4.4, 0.08, 26.5)]
    return r


def room_05():
    r = new_room(5, 'Threshold', 'Forest exit: the trail climbs through guardian statues to a towering root gate, a moonlit clearing beyond.')
    d.path(r, 'Forest_Path', [(0, 0), (-1.0, 6), (0.6, 12), (-0.4, 18)], 3.0)
    d.path(r, 'Forest_Cobble', [(-0.4, 18), (0, 25), (0, 36.4), (0, 50)], 3.6, jitter=0.0)
    rune_circle(r, 0, 25.5, 3.2, 10)
    r.model('root_archway', (0, 0, 33.2), (10.5, 7.8, 3.4), 0, collider=None, long_axis='x', fallback=fallback_arch)
    for side in (-1, 1):
        r.block(f'Great gate root {side}', (side * 4.4, 1.5, 33.2), (1.8, 3.0, 2.4))
        r.model('guardian_statue', (side * 5.4, 0, 28.2), (2.8, 6.6, 2.6), 180, collider={'size': (1.6, 3.0, 1.6)}, fallback=fallback_statue)
    for x, z in ((-2.6, 8.0), (2.6, 14.0), (-2.9, 20.5), (2.9, 20.5)):
        lantern(r, x, z, light=(x, z) in ((-2.6, 8.0), (2.9, 20.5)))
    r.model('mossy_boulders', (-8.8, 0, 13.5), (4.0, 2.2, 3.4), 60, fallback=fallback_boulders)
    r.model('elven_shrine', (9.4, 0, 12.0), (5.2, 4.4, 5.2), 30, collider={'size': (3.4, 3.0, 3.4)}, fallback=fallback_shrine)
    # North vista seen while approaching the exit.
    noise = geom.fbm(stable_seed(r.key, 'north'))
    r.kit.heightfield('Forest_Earth', -26, 32, LENGTH, 58, 48, 18,
                      lambda x, z: max(0, abs(x) - 4) * 0.06 * d.smoothstep(LENGTH, LENGTH + 10, z) + (noise(x * 0.15, z * 0.15) - 0.5) * 0.5, skirt=-0.6)
    r.kit.patch('Forest_Moss', (0, 0.1, 47), 9, 7, 0.06, 0, 18)
    r.model('ancient_tree', (7.5, 0, 50.0), (13.0, 19.0, 13.0), 45, collider=None, fallback=fallback_tree)
    for x, z, h in ((-10, 44, 12), (-4, 55, 14), (15, 42, 12)):
        r.model('ancient_tree', (x, 0, z), (8, h, 8), r.rng(0, 360), collider=None, fallback=fallback_tree)
    for _ in range(16):
        x, z = r.rng(-24, 26), r.rng(38, 57)
        if abs(x) < 4:
            continue
        h = r.rng(1.2, 3.0)
        r.kit.rock('Forest_FoliageDeep' if r.rng(0, 1) < 0.6 else 'Forest_Foliage', (x, h * 0.4, z), (r.rng(2.5, 4), h, r.rng(2.2, 3.6)), r.rng(0, 360))
    d.fireflies(r, 'Forest_Firefly', 50, (-8, 8), (36, 54), (0.5, 3.0))
    backdrop(r, tree_z=(5.0, 18.0, 30.0))
    seams(r)
    ground_detail(r, 14, 48, 12, 40)
    r.light('Gate runes', (0, 3.2, 31.5), '63ffd1', 2.2, 10.0)
    r.light('Moon clearing', (0, 9.0, 46.0), 'b8ccff', 3.0, 18.0)
    return r


ROOMS = [room_01, room_02, room_03, room_04, room_05, room_06, room_07]
