"""Abyssal crystal caverns: seven rooms deep underground.

Theme identity: wet slate floor with strata and cracks, cyan and violet crystal light, still black-blue water,
warm lantern light only in the old mine. A steep strata cliff rises past the east wall (screen top-right)
studded with crystals and glow-worms; the west is a low rock shelf. Seams are rock ridges with stalagmite
posts beside each doorway.
"""
import math

import numpy as np

import dress as d
import geom
from materials import Mat
from room import Room, LENGTH, stable_seed

THEME = 3
KEY = 'Cave'

MATERIALS = [
    Mat('Cave_Floor', 'c6d0d6', 0.07, 0.36, 'CaveRock_v2', 0.24, 0.9),
    Mat('Cave_Slate', '52666e', 0.11, 0.4, 'rock', 0.65, 0.7),
    Mat('Cave_Wall', '5a6a72', 0.06, 0.32, 'CaveRock_v2', 0.2, 1.0),
    Mat('Cave_DarkRock', '2f3f47', 0.07, 0.3, 'rock', 0.65, 0.7),
    Mat('Cave_Strata', '6d7e82', 0.09, 0.45, 'rock', 0.65, 0.7),
    Mat('Cave_Sand', 'a3adb4', 0.0, 0.2, 'ForestTrail_v2', 0.36, 0.7),
    Mat('Cave_Water', '2c5a68', 0.05, 0.9, 'Water_v2', 0.18, 0.6),
    Mat('Cave_WaterEdge', '4b7a84', 0.15, 0.85),
    Mat('Cave_CrystalGlow', '76dccf', 0.15, 0.7, emission='4ccfd3', emission_strength=2.2),
    Mat('Cave_VioletGlow', 'a49cde', 0.16, 0.68, emission='8873d9', emission_strength=2.0),
    Mat('Cave_Fungus', '8ff0dc', 0.0, 0.5, emission='3ff5d0', emission_strength=1.6),
    Mat('Cave_GlowWorm', 'b8fbff', 0.0, 0.5, emission='8ff7ff', emission_strength=2.6),
    Mat('Cave_Timber', '6f5238', 0.0, 0.22, 'panel', 0.9, 0.35),
    Mat('Cave_Rail', '8c9197', 0.8, 0.55),
    Mat('Cave_Lantern', 'ffd9a0', 0.0, 0.5, emission='ffb65c', emission_strength=3.2),
]
EMISSIVE = {m.name for m in MATERIALS if m.emissive}

TITLES = {1: '푸른 균열', 2: '종유석 회랑', 3: '수정의 심장', 4: '지하 호수', 5: '심연의 문',
          6: '버려진 광산', 7: '발광 버섯 동굴'}


# ---- shared cave dressing ---------------------------------------------------------------------------
def seam_profile_east(x):
    return 0.3 + 8.5 * d.smoothstep(13.0, 18.5, x) + 3.0 * d.smoothstep(18.5, 30.0, x)


def seam_profile_west(x):
    return 0.2 + 1.3 * d.smoothstep(-13.0, -17.0, x) + 0.6 * d.smoothstep(-17.0, -26.0, x)


def backdrop(r, column_z=(8.0, 26.0), crystals=10, worms=60):
    noise = geom.fbm(stable_seed(r.key, 'east'))
    west_noise = geom.fbm(stable_seed(r.key, 'west'))

    def east(x, z):
        n = noise(x * 0.22, z * 0.22)
        return seam_profile_east(x) + (n - 0.5) * 3.2 * d.smoothstep(13.5, 17.0, x) + math.sin(z * 0.9 + n * 3) * 0.5 * d.smoothstep(14, 18, x)

    def west(x, z):
        return seam_profile_west(x) + (west_noise(x * 0.3, z * 0.3) - 0.5) * 0.9 * d.smoothstep(-13.4, -15.5, x)

    east_h = d.outer_ground(r, 'Cave_Wall', seam_profile_east, east, 13.05, 32.0, res=0.8)
    d.outer_ground(r, 'Cave_DarkRock', seam_profile_west, west, -26.0, -13.05, res=0.9)
    # Strata ledges on the cliff face.
    for _ in range(12):
        x, z = r.rng(13.6, 16.5), r.rng(0.5, LENGTH - 0.5)
        h = east_h(x, z)
        r.kit.rock('Cave_Strata', (x, max(0.4, h * r.rng(0.3, 0.8)), z), (r.rng(1.2, 2.2), 0.35, r.rng(2.5, 4.5)), r.rng(-15, 15), sides=7)
    for tz in column_z:
        x = r.rng(14.2, 15.8)
        r.model('cave_column', (x, east_h(x, tz) * 0.2, tz), (3.4, r.rng(9.0, 11.0), 3.4), r.rng(0, 360), collider=None, fallback=fallback_column)
    for _ in range(crystals):
        x, z = r.rng(13.4, 17.5), r.rng(0.8, LENGTH - 0.8)
        base = (x, east_h(x, z) * r.rng(0.2, 0.7), z)
        crystal_cluster(r, base, r.rng(0.5, 1.2), 'Cave_CrystalGlow' if r.rng(0, 1) < 0.65 else 'Cave_VioletGlow', tilt_bias=(0, -35))
    for _ in range(worms):
        x, z = r.rng(13.6, 19.0), r.rng(0.5, LENGTH - 0.5)
        y = east_h(x, z) * r.rng(0.35, 0.95)
        r.kit.octahedron('Cave_GlowWorm', (x - 0.2, y, z), (0.035, 0.035, 0.035))
    # West: low shelf boulders.
    z = 0.8
    while z < LENGTH - 0.5:
        r.kit.rock(r.rng_choice(['Cave_DarkRock', 'Cave_Slate']), (r.rng(-15.5, -13.3), 0.5, z), (r.rng(1.6, 2.6), r.rng(0.8, 1.4), r.rng(1.5, 2.4)), r.rng(0, 360))
        z += r.rng(1.5, 2.6)
    edges(r)


def crystal_cluster(r, base, scale, material, count=5, tilt_bias=(0, 0)):
    bx, by, bz = base
    for k in range(count):
        h = scale * r.rng(0.6, 1.6) * (1.4 if k == 0 else 1)
        r.kit.crystal(material, (bx + r.rng(-0.3, 0.3) * scale, by, bz + r.rng(-0.3, 0.3) * scale), h, h * r.rng(0.14, 0.2),
                      (r.rng(-28, 28) + tilt_bias[0], r.rng(-28, 28) + tilt_bias[1]), r.rng(0, 60))


def edges(r):
    for side, x in ((1, 12.2), (-1, -12.25)):
        z = 1.3
        while z < LENGTH - 1.3:
            if r.is_clear(x, z, 0.3):
                h = r.rng(1.2, 2.6) if side > 0 else r.rng(0.6, 1.1)
                r.kit.rock(r.rng_choice(['Cave_Slate', 'Cave_DarkRock', 'Cave_Wall']), (x, h * 0.42, z), (r.rng(1.3, 2.0), h, r.rng(1.4, 2.2)), r.rng(0, 360))
                if side > 0 and r.rng(0, 1) < 0.4:
                    r.kit.cone('Cave_DarkRock', (x - 0.5, h + 0.6, z), 0.28, 0.02, 1.4, 7)
                if r.rng(0, 1) < 0.25:
                    crystal_cluster(r, (x - side * 0.8, 0.05, z), 0.35, 'Cave_CrystalGlow', 3)
            z += r.rng(1.2, 2.0)
        r.block(f'Rock band {"east" if side > 0 else "west"}', (x + side * 0.1, 1.0, LENGTH / 2), (1.5, 2.0, LENGTH - 2.6))


def seams(r):
    for z in (0.0, LENGTH):
        inside = 0.45 if z == 0 else LENGTH - 0.45
        for side in (-1, 1):
            x = side * 3.95
            r.kit.cone('Cave_Slate', (x, 1.3, inside), 0.55, 0.12, 2.6, 8)
            r.kit.rock('Cave_DarkRock', (x, 0.35, inside), (1.3, 0.8, 1.1), r.rng(0, 360))
            crystal_cluster(r, (x - side * 0.2, 0.3, inside), 0.3, 'Cave_CrystalGlow', 3)
            xx = 5.0
            while xx < 13.0:
                h = r.rng(0.9, 1.8)
                r.kit.rock(r.rng_choice(['Cave_Slate', 'Cave_DarkRock', 'Cave_Wall']), (side * (xx + r.rng(-0.2, 0.2)), h * 0.4, inside), (r.rng(1.3, 1.9), h, 0.95), r.rng(0, 360))
                xx += r.rng(1.1, 1.6)


def ground_detail(r, slabs=20, pebbles=40, glints=18, puddles=4, x_range=(-11.2, 11.2), z_range=(0.8, 35.6)):
    d.scatter(r, slabs // 2, x_range, z_range, lambda x, z: r.kit.patch(
        'Cave_DarkRock', (x, 0.018, z), r.rng(0.9, 2.6), r.rng(0.7, 2.0), 0.014, r.rng(0, 360), segments=14, dome=0.05, wobble=0.38),
        min_spacing=2.2, margin=0.1)
    d.scatter(r, pebbles, x_range, z_range, lambda x, z: r.kit.rock('Cave_DarkRock', (x, 0.07, z), (r.rng(0.2, 0.55), r.rng(0.1, 0.3), r.rng(0.2, 0.5)), r.rng(0, 360), sides=6),
              min_spacing=0.8, margin=0.2)
    d.scatter(r, glints, x_range, z_range, lambda x, z: crystal_cluster(r, (x, 0.02, z), r.rng(0.18, 0.35),
              'Cave_CrystalGlow' if r.rng(0, 1) < 0.6 else 'Cave_VioletGlow', 3), min_spacing=1.8, margin=0.3)
    d.scatter(r, puddles, x_range, z_range, lambda x, z: pool(r, x, z, r.rng(0.8, 1.8), r.rng(0.6, 1.4)), min_spacing=4.0, margin=0.3)


def pool(r, x, z, rx, rz, edge=True):
    outline = d.blob_outline(x, z, rx, rz, r.rng, 18, 0.18)
    if edge:
        d.flat_polygon(r, 'Cave_WaterEdge', d.blob_outline(x, z, rx + 0.25, rz + 0.25, r.rng, 18, 0.12), 0.02, 0.01)
    d.flat_polygon(r, 'Cave_Water', outline, 0.03, 0.02)
    r.keep_clear_polygon(outline, 0.1)
    return outline


def stalagmite(r, x, z, h, material='Cave_Slate'):
    r.kit.cone(material, (x, h / 2, z), h * 0.18, 0.03, h, 8)
    r.kit.rock('Cave_DarkRock', (x, 0.15, z), (h * 0.45, 0.4, h * 0.45), r.rng(0, 360))


# ---- procedural fallbacks ---------------------------------------------------------------------------
def fallback_column(r, p, size, yaw):
    x, y, z = p
    r.kit.cone('Cave_Strata', (x, y + size[1] * 0.25, z), size[0] * 0.4, size[0] * 0.18, size[1] * 0.5, 10)
    r.kit.cone('Cave_Strata', (x, y + size[1] * 0.75, z), size[0] * 0.18, size[0] * 0.42, size[1] * 0.5, 10)


# ---- rooms -------------------------------------------------------------------------------------------
def new_room(index, kind, notes):
    r = Room(THEME, index, f'{KEY}_{index:02d}', TITLES[index], kind, stable_seed(KEY, index), notes)
    d.base_floor(r, 'Cave_Floor')
    return r


def room_01():
    r = new_room(1, 'Arrival', 'Blue fissure: the way in squeezes through a crystal-lit crack before the cavern opens into stalagmites.')
    # Fissure walls (tall east, low west) from the entry to z ~ 10.
    for z in np.arange(0.8, 10.5, 1.3):
        w_east = 3.4 + d.smoothstep(4, 10.5, z) * 5.0
        w_west = 3.6 + d.smoothstep(4, 10.5, z) * 4.8
        h = r.rng(4.5, 7.0) * (1 - d.smoothstep(7, 11, z) * 0.5)
        r.kit.rock(r.rng_choice(['Cave_Wall', 'Cave_Slate']), (w_east + 1.2, h * 0.45, z), (2.6, h, 1.9), r.rng(-20, 20))
        r.kit.rock(r.rng_choice(['Cave_DarkRock', 'Cave_Slate']), (-w_west - 1.2, 0.9, z), (2.6, r.rng(1.5, 2.3), 1.9), r.rng(-20, 20))
    for z0, z1, x_in in ((0.5, 6.0, 3.6), (6.0, 10.5, 5.8)):
        for side in (-1, 1):
            r.block(f'Fissure {side} {z0:.0f}', (side * (x_in + 4.6), 1.5, (z0 + z1) / 2), (9.2, 3.0, z1 - z0))
    for z in (2.5, 5.5, 8.5):
        crystal_cluster(r, (3.2, 0.1, z), 0.6, 'Cave_CrystalGlow', 5, (0, -25))
        crystal_cluster(r, (-3.3, 0.1, z + 1.0), 0.45, 'Cave_VioletGlow', 4, (0, 25))
    for _ in range(40):
        z = r.rng(0.5, 10)
        r.kit.octahedron('Cave_GlowWorm', (r.rng(3.6, 5.0), r.rng(1.5, 5.5), z), (0.03, 0.03, 0.03))
    r.model('crystal_cluster', (-7.8, 0, 21.0), (3.6, 3.8, 3.6), 30)
    r.model('crystal_cluster', (7.6, 0, 29.0), (3.2, 3.6, 3.2), 200)
    for x, z, h in ((-4.5, 15.5, 2.4), (5.0, 17.5, 3.0), (-6.2, 27.5, 2.6), (3.8, 23.5, 1.8)):
        stalagmite(r, x, z, h)
        r.block(f'Stalagmite {x:.0f},{z:.0f}', (x, 1.2, z), (h * 0.35, 2.4, h * 0.35))
    r.model('stalagmite_cluster', (-9.4, 0, 14.0), (4.0, 4.4, 4.0), 60, collider={'size': (2.6, 3.0, 2.6)})
    r.model('stalagmite_cluster', (9.4, 0, 20.5), (4.2, 5.2, 4.2), 140, collider={'size': (2.6, 3.0, 2.6)})
    # South: the fissure continues into darkness past the entry.
    r.kit.heightfield('Cave_DarkRock', -26, 32, -16, 0, 44, 12, lambda x, z: max(0.0, abs(x) - 3.5) * 0.9 * d.smoothstep(-0.5, -4, z) + 0.2, skirt=-0.6)
    backdrop(r, column_z=(18.0, 32.0))
    seams(r)
    ground_detail(r, puddles=3)
    r.light('Fissure crystals', (0, 1.8, 5.0), '4ccfd3', 2.4, 9.0)
    r.light('Cavern glow', (-7.8, 2.5, 21.0), '63e6ea', 2.0, 9.0)
    r.light('Violet deep', (7.6, 2.6, 29.0), '9d89dd', 1.8, 8.0)
    return r


def room_02():
    r = new_room(2, 'Combat', 'Stalactite gallery: fused columns stand like a colonnade across the cavern; drip pools and crystals between them give cover.')
    r.keep_clear_path(d.densify([(0, 0), (0, 5)], 0.9), 3.6)
    r.keep_clear_path(d.densify([(0, 32), (0, 36.4)], 0.9), 3.6)
    columns = [(-6.5, 9.5, 8.5), (3.5, 11.0, 10.0), (-2.5, 17.5, 9.0), (7.0, 19.0, 11.0), (-7.2, 24.5, 8.0), (2.2, 26.5, 10.5), (-3.0, 31.0, 7.5), (7.5, 30.5, 9.5)]
    for x, z, h in columns:
        w = 2.2 + h * 0.08
        r.model('cave_column', (x, 0, z), (w, h, w), r.rng(0, 360), collider={'size': (w * 0.6, 3.0, w * 0.6)}, fallback=fallback_column)
    for x, z in ((0.5, 14.0), (-4.8, 21.0), (4.8, 23.5)):
        pool(r, x, z, r.rng(1.2, 1.8), r.rng(1.0, 1.5))
        crystal_cluster(r, (x + 1.2, 0.05, z + 0.6), 0.4, 'Cave_CrystalGlow', 4)
    r.model('stalagmite_cluster', (10.0, 0, 8.0), (3.8, 4.4, 3.8), 20, collider={'size': (2.4, 3.0, 2.4)})
    r.model('stalagmite_cluster', (-10.2, 0, 32.0), (3.4, 3.4, 3.4), 200, collider={'size': (2.2, 3.0, 2.2)})
    r.model('crystal_cluster', (-10.0, 0, 16.5), (2.8, 3.0, 2.8), 90)
    backdrop(r, column_z=(12.0, 29.0), crystals=8)
    seams(r)
    ground_detail(r, glints=12, puddles=2)
    r.light('Pool shimmer', (0.5, 1.4, 14.0), '4ccfd3', 1.8, 8.0)
    r.light('Colonnade', (0, 4.0, 22.0), '7fb8d8', 1.6, 12.0)
    r.enemies = [(-1.0, 0.08, 21.5), (4.8, 0.08, 15.0), (-4.2, 0.08, 28.2)]
    return r


def room_03():
    r = new_room(3, 'Combat', 'Crystal heart: a split geode full of amethyst sits in the middle, ringed by crystal spires; the route circles it in violet light.')
    cx, cz = 0.4, 19.0
    r.kit.disc('Cave_DarkRock', (cx, 0.04, cz), 5.8, 0.08, 28)
    r.kit.ring('Cave_VioletGlow', (cx, 0.09, cz), 5.0, 5.1, 0.02, 40)
    r.model('crystal_geode', (cx, 0, cz), (6.2, 5.2, 5.6), 180, collider={'size': (4.6, 3.0, 4.0)})
    r.keep_clear_circle((cx, cz), 5.0)
    for k, a in enumerate((20, 75, 130, 200, 255, 320)):
        x, z = cx + math.sin(math.radians(a)) * 8.6, cz + math.cos(math.radians(a)) * 8.3
        x = max(-10.8, min(10.8, x))
        if k % 2 == 0:
            r.model('crystal_cluster', (x, 0, z), (2.4, 3.2, 2.4), a, collider={'size': (1.4, 3.0, 1.4)})
        else:
            crystal_cluster(r, (x, 0.05, z), 0.9, 'Cave_VioletGlow', 6)
            r.block(f'Spire {k}', (x, 1.5, z), (1.2, 3.0, 1.2))
    for _ in range(40):
        a = r.rng(0, math.tau)
        rr = r.rng(5.4, 11.5)
        x, z = cx + math.sin(a) * rr, cz + math.cos(a) * rr
        if abs(x) < 11.5 and 1 < z < 35.5 and r.is_clear(x, z, 0.3):
            r.kit.crystal('Cave_VioletGlow' if r.rng(0, 1) < 0.6 else 'Cave_CrystalGlow', (x, 0.02, z), r.rng(0.15, 0.4), 0.05, (r.rng(-30, 30), r.rng(-30, 30)))
    backdrop(r, column_z=(7.0, 31.0), crystals=12)
    seams(r)
    ground_detail(r, glints=6, puddles=2)
    r.light('Geode heart', (cx, 2.2, cz - 1.0), 'b18cff', 3.2, 12.0)
    r.light('Geode rim', (cx, 4.5, cz), '8873d9', 1.8, 10.0)
    r.enemies = [(-6.2, 0.08, 17.0), (6.4, 0.08, 21.5), (0.0, 0.08, 28.8)]
    return r


def room_04():
    r = new_room(4, 'Combat', 'Underground lake: black water fills the west and centre; a stone causeway hugs the east shore under a natural arch.')
    lake = [(-12.6, 4.0), (-5.0, 3.5), (0.5, 7.0), (1.8, 12.0), (0.0, 17.0), (2.5, 22.0), (1.0, 27.5), (-4.0, 31.5), (-12.6, 32.5)]
    lake_poly = d.catmull(d.densify(lake, 1.0), 2) + [(-13.4, 32.5), (-13.4, 4.0)]
    d.flat_polygon(r, 'Cave_Water', lake_poly, 0.03, 0.03)
    for x, z in lake[1:-1]:
        for k in range(3):
            r.kit.rock(r.rng_choice(['Cave_Slate', 'Cave_DarkRock']), (x + r.rng(-0.8, 0.8), 0.15, z + r.rng(-1.0, 1.0)), (r.rng(0.8, 1.6), r.rng(0.3, 0.6), r.rng(0.7, 1.4)), r.rng(0, 360))
    d.polygon_blockers(r, 'Lake', lake_poly, inset=0.5, slices=9)
    r.keep_clear_polygon(lake_poly, 0.3)
    # Stepping stones out to a small crystal islet (walkable, below the step height).
    for i, (x, z) in enumerate(((0.8, 19.0), (-0.8, 19.8), (-2.4, 20.2))):
        r.kit.cone('Cave_Slate', (x, 0.1, z), 0.7, 0.6, 0.2, 9)
    r.model('cavern_arch', (6.2, 0, 17.5), (7.4, 7.6, 4.2), 90, collider=None)
    for dz in (-2.8, 2.8):
        r.block(f'Arch leg {dz:+.0f}', (8.6, 1.5, 17.5 + dz), (2.6, 3.0, 1.6))
    for x, z, s in ((-2.0, 11.0, 2.4), (-6.0, 25.0, 2.8), (3.0, 29.5, 2.2)):
        r.model('glow_fungus', (x, 0, z), (s, s * 0.95, s), r.rng(0, 360), collider={'size': (s * 0.5, 2.0, s * 0.5)})
        r.light(f'Fungus {x:.0f}', (x, 1.1, z), '3ff5d0', 1.4, 6.0)
    r.model('stalagmite_cluster', (8.8, 0, 5.5), (3.6, 4.0, 3.6), 10, collider={'size': (2.2, 3.0, 2.2)})
    r.model('crystal_cluster', (9.4, 0, 29.5), (3.0, 3.4, 3.0), 250, collider={'size': (1.8, 3.0, 1.8)})
    backdrop(r, column_z=(5.0, 31.0), crystals=9)
    seams(r)
    ground_detail(r, slabs=14, glints=10, puddles=0, x_range=(1.5, 11.2))
    r.light('Lake reflection', (-6.0, 3.0, 18.0), '5fb6d8', 2.2, 12.0)
    r.enemies = [(5.4, 0.08, 10.5), (5.6, 0.08, 24.0), (-2.0, 0.08, 33.6)]
    return r


def rails(r, points):
    dense = d.densify(points, 0.6)
    for side in (-0.55, 0.55):
        edge = []
        for i, (x, z) in enumerate(dense):
            a = dense[max(0, i - 1)]
            b = dense[min(len(dense) - 1, i + 1)]
            dx, dz = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dz) or 1
            edge.append((x + side * dz / length, z - side * dx / length))
        r.kit.ribbon('Cave_Rail', edge, 0.08, 0.1, thickness=0.08)
    d.planks(r, 'Cave_Timber', points, 1.7, y=0.04, plank=0.22, gap=0.45, thickness=0.08, jitter=4)


def room_06():
    r = new_room(6, 'Combat', 'Abandoned mine: timber frames march over a rail line, a crystal-laden cart and supply stacks glowing in lantern light.')
    route = [(0, 0), (0.6, 6), (-0.8, 12), (0.8, 18), (-0.6, 24), (0.4, 30), (0, 36.4)]
    rails(r, route)
    r.keep_clear_path(d.densify(route, 0.9), 3.6)
    for i, z in enumerate((5.0, 11.0, 17.0, 23.0, 29.0)):
        x = [0.6, -0.6, 0.8, -0.5, 0.3][i]
        r.model('mine_support', (x, 0, z), (5.8, 4.2, 1.2), r.rng(-4, 4), collider=None, long_axis='x')
        for side in (-1, 1):
            r.block(f'Support post {i} {side}', (x + side * 2.65, 1.5, z), (0.5, 3.0, 0.5))
        if i % 2 == 0:
            r.light(f'Mine lantern {i}', (x, 3.1, z), 'ffb65c', 1.8, 7.5)
    r.model('mine_cart', (-0.2, 0, 20.4), (1.6, 1.8, 3.2), 3, collider={'size': (1.4, 1.8, 2.8)}, long_axis='z')
    r.model('mining_crates', (-7.2, 0, 13.5), (3.2, 2.4, 2.6), 60, collider={'size': (2.4, 2.2, 1.9)}, long_axis='x')
    r.model('mining_crates', (7.8, 0, 27.0), (3.0, 2.2, 2.4), 230, collider={'size': (2.2, 2.0, 1.8)}, long_axis='x')
    r.model('mine_cart', (6.8, 0, 9.0), (1.5, 1.7, 3.0), 70, collider={'size': (2.6, 1.8, 1.4)}, long_axis='z')
    for x, z in ((-8.4, 23.5), (8.8, 17.5), (-6.0, 31.0)):
        r.kit.rock('Cave_Slate', (x, 0.6, z), (2.4, 1.4, 2.0), r.rng(0, 360))
        crystal_cluster(r, (x, 1.1, z), 0.45, 'Cave_CrystalGlow', 4)
        r.block(f'Ore rock {x:.0f}', (x, 1.0, z), (2.0, 2.0, 1.6))
    for _ in range(5):
        x, z = r.rng(-10, 10), r.rng(3, 34)
        if r.is_clear(x, z, 0.6):
            r.kit.box('Cave_Timber', (x, 0.08, z), (r.rng(1.2, 2.4), 0.12, 0.22), geom.yaw_matrix(r.rng(0, 180)))
    backdrop(r, column_z=(14.0,), crystals=6, worms=30)
    seams(r)
    ground_detail(r, glints=8, puddles=2)
    r.enemies = [(-4.6, 0.08, 16.5), (4.4, 0.08, 22.0), (-3.2, 0.08, 30.0)]
    return r


def room_07():
    r = new_room(7, 'Combat', 'Glowcap grotto: towering bioluminescent fungi and spore clouds light a winding path between shallow teal pools.')
    route = [(0, 0), (2.4, 6), (3.4, 11), (-0.8, 16.5), (-3.4, 21.5), (0.2, 27.0), (2.0, 31.5), (0, 36.4)]
    r.keep_clear_path(d.densify(route, 0.9), 3.4)
    d.path(r, 'Cave_Sand', route, 2.6, jitter=0.2, clear=False)
    for x, z, s in ((-5.6, 7.5, 3.2), (6.8, 16.0, 3.6), (-7.2, 17.5, 2.8), (5.6, 25.0, 3.0), (-6.4, 30.5, 3.4), (8.6, 33.0, 2.4)):
        r.model('glow_fungus', (x, 0, z), (s, s * 0.95, s), r.rng(0, 360), collider={'size': (s * 0.5, 2.0, s * 0.5)})
        r.light(f'Glowcap {x:.0f}', (x, 1.4, z), '3ff5d0', 1.6, 6.5)
    for x, z in ((0.8, 3.6), (-2.6, 12.0), (3.4, 20.0), (-3.6, 27.5)):
        pool(r, x, z, r.rng(1.2, 2.0), r.rng(1.0, 1.6))
    for _ in range(30):
        x, z = r.rng(-11, 11), r.rng(1, 35)
        if r.is_clear(x, z, 0.4):
            h = r.rng(0.25, 0.7)
            r.kit.cone('Cave_WaterEdge', (x, h * 0.45, z), 0.035, 0.03, h * 0.9, 5, caps=False)
            r.kit.cone('Cave_Fungus', (x, h, z), h * 0.45, h * 0.06, h * 0.18, 9)
    for _ in range(70):
        r.kit.octahedron('Cave_GlowWorm', (r.rng(-11, 11), r.rng(0.4, 3.2), r.rng(1, 35)), (0.03, 0.03, 0.03))
    r.model('stalagmite_cluster', (9.6, 0, 7.0), (3.4, 4.2, 3.4), 30, collider={'size': (2.2, 3.0, 2.2)})
    backdrop(r, column_z=(10.0, 27.0), crystals=6)
    seams(r)
    ground_detail(r, glints=6, puddles=0)
    r.enemies = [(3.8, 0.08, 14.0), (-3.4, 0.08, 24.8), (4.4, 0.08, 29.5)]
    return r


def room_05():
    r = new_room(5, 'Threshold', 'Abyss gate: carved steps climb to a sealed rune door set in the rock; beyond, the cavern drops into a violet chasm.')
    route = [(0, 0), (-1.0, 8), (0.8, 16), (0, 24), (0, 30)]
    r.keep_clear_path(d.densify(route, 0.9), 3.6)
    d.path(r, 'Cave_Sand', route, 2.8, jitter=0.15, clear=False)
    for k in range(4):
        r.kit.box('Cave_Slate', (0, 0.04 + k * 0.045, 27.5 + k * 1.0), (7.0 - k * 0.6, 0.08, 1.0))
    r.model('abyss_gate', (0, 0.2, 33.9), (9.0, 7.6, 3.6), 180, collider=None, long_axis='x')
    for side in (-1, 1):
        r.block(f'Gate frame {side}', (side * 4.3, 1.5, 33.9), (1.8, 3.0, 2.8))
        r.kit.disc('Cave_DarkRock', (side * 3.6, 0.5, 26.0), 0.6, 1.0, 10)
        crystal_cluster(r, (side * 3.6, 1.0, 26.0), 0.5, 'Cave_VioletGlow', 5)
        r.block(f'Brazier {side}', (side * 3.6, 0.8, 26.0), (1.2, 1.6, 1.2))
    for x, z, h in ((-8.0, 12.0, 9.0), (8.5, 20.0, 10.5)):
        r.model('cave_column', (x, 0, z), (3.0, h, 3.0), r.rng(0, 360), collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_column)
    r.model('crystal_geode', (-8.4, 0, 26.0), (4.2, 3.6, 3.8), 120, collider={'size': (3.0, 3.0, 2.6)})
    r.model('crystal_cluster', (8.4, 0, 9.0), (3.0, 3.4, 3.0), 30, collider={'size': (1.8, 3.0, 1.8)})
    # North: the chasm beyond the gate (dark drop with glowing crystal teeth far below).
    r.kit.heightfield('Cave_DarkRock', -26, 32, LENGTH, 58, 44, 16, lambda x, z: -6.0 * d.smoothstep(LENGTH + 2, LENGTH + 8, z) * (1 - d.smoothstep(14, 24, abs(x - 3))) + 0.2, skirt=-8)
    for _ in range(14):
        x, z = r.rng(-12, 16), r.rng(42, 56)
        crystal_cluster(r, (x, -5.5, z), r.rng(0.8, 1.8), 'Cave_VioletGlow' if r.rng(0, 1) < 0.6 else 'Cave_CrystalGlow', 5)
    for x, z, h in ((-14, 44, 12), (16, 42, 14), (-6, 55, 16)):
        r.model('cave_column', (x, -2, z), (4.0, h, 4.0), r.rng(0, 360), collider=None, fallback=fallback_column)
    backdrop(r, column_z=(5.0, 30.0), crystals=10)
    seams(r)
    ground_detail(r, glints=10, puddles=2)
    r.light('Gate runes', (0, 3.4, 31.5), '9d89dd', 2.6, 10.0)
    r.light('Chasm glow', (2, -3.0, 48.0), 'a07cff', 3.0, 20.0)
    return r


ROOMS = [room_01, room_02, room_03, room_04, room_05, room_06, room_07]
