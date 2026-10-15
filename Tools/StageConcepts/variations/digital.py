"""Program prison: seven rooms of a computer world holding its prisoners.

Theme identity: dark navy tech-panel floor with glowing cyan traces, magenta firewall beams, amber warnings.
Past the east wall, server towers climb out of a grid void (screen top-right); the west keeps only low
cable trays. Seams are firewall lines: emitter pylons with magenta beams beside each doorway.
"""
import math

import numpy as np

import dress as d
import geom
from materials import Mat
from room import Room, LENGTH, stable_seed

THEME = 1
KEY = 'Digital'

MATERIALS = [
    Mat('Program_Floor', 'a7b6cf', 0.55, 0.62, 'TechPanel_v2', 0.25, 0.8, emission='23b9f0', emission_strength=0.5, emission_map=True),
    Mat('Program_FloorDark', '7c889e', 0.5, 0.55, 'TechPanel_v2', 0.25, 0.8),
    Mat('Program_Panels', '2a3b55', 0.7, 0.55, 'panel', 0.85, 0.22),
    Mat('Program_Frame', '172131', 0.7, 0.5),
    Mat('Program_Steel', '8792a3', 0.8, 0.55, 'panel', 0.9, 0.2),
    Mat('Program_Grid', '237b91', 0.25, 0.75, emission='1689ac', emission_strength=0.8),
    Mat('Program_Cyan', '50dbf6', 0.18, 0.7, emission='32cffa', emission_strength=1.7),
    Mat('Program_Magenta', 'dd64da', 0.15, 0.6, emission='d542cd', emission_strength=1.7),
    Mat('Program_Warning', 'fabe52', 0.2, 0.5, emission='ff9b28', emission_strength=1.4),
    Mat('Program_Hazard', 'b8912f', 0.1, 0.35),
    Mat('Program_Red', 'ff4a5e', 0.1, 0.5, emission='ff2640', emission_strength=2.4),
    Mat('Program_Data', 'bff6ff', 0.0, 0.8, emission='7df3ff', emission_strength=2.0),
    Mat('Program_Glass', '10283a', 0.6, 0.93),
    Mat('Program_Void', '04060b', 0.0, 0.2),
]
EMISSIVE = {m.name for m in MATERIALS if m.emissive}

TITLES = {1: '부팅 구역', 2: '방화벽 수용소', 3: '메모리 미로', 4: '데이터 강', 5: '탈출 프로토콜',
          6: '중앙 연산실', 7: '손상된 섹터'}


# ---- shared digital dressing ------------------------------------------------------------------------
def outer_floor(r, z0=0.0, z1=LENGTH):
    r.kit.box('Program_FloorDark', (22.0, -0.10, (z0 + z1) / 2), (18.0, 0.22, z1 - z0))
    r.kit.box('Program_FloorDark', (-19.5, -0.10, (z0 + z1) / 2), (13.0, 0.22, z1 - z0))
    for x in range(14, 31, 4):
        r.kit.box('Program_Grid', (x, 0.02, (z0 + z1) / 2), (0.05, 0.02, z1 - z0))
    for x in range(-26, -13, 4):
        r.kit.box('Program_Grid', (x, 0.02, (z0 + z1) / 2), (0.05, 0.02, z1 - z0))


def data_tower(r, x, z, w, h, depth=None, strips=True, glow='Program_Cyan'):
    depth = depth or w
    r.kit.box('Program_Panels', (x, h / 2, z), (w, h, depth))
    r.kit.box('Program_Frame', (x, h + 0.08, z), (w * 1.06, 0.16, depth * 1.06))
    if strips:
        for k in range(int(h / 0.9)):
            y = 0.5 + k * 0.9
            length = r.rng(0.3, 0.9) * w
            r.kit.box(glow if k % 4 else 'Program_Magenta', (x - w / 2 - 0.01, y, z + r.rng(-0.3, 0.3) * depth), (0.03, 0.05, length))
            r.kit.box(glow, (x + r.rng(-0.3, 0.3) * w, y + 0.3, z - depth / 2 - 0.01), (length, 0.05, 0.03))


def backdrop(r, tower_z=(7.0, 19.0, 31.0), density=1.0):
    outer_floor(r)
    for tz in tower_z:
        x = r.rng(16.0, 19.5)
        r.model('server_monolith', (x, 0, tz), (5.2, r.rng(11.0, 15.0), 5.2), r.rng(-10, 10) + 90, collider=None, fallback=fallback_monolith)
    for _ in range(int(16 * density)):
        x, z = r.rng(14.2, 30.0), r.rng(0.6, LENGTH - 0.6)
        if any(abs(z - t) < 3.2 and x < 21 for t in tower_z):
            continue
        near = d.smoothstep(14, 22, x)
        data_tower(r, x, z, r.rng(1.4, 2.6), r.rng(2.5, 5.0) + near * r.rng(3.0, 9.0), r.rng(1.4, 2.6))
    for _ in range(int(14 * density)):
        x, z, y = r.rng(13.5, 26), r.rng(0.5, LENGTH - 0.5), r.rng(3.0, 11.0)
        s = r.rng(0.3, 0.9)
        r.kit.wire_cube('Program_Cyan' if r.rng(0, 1) < 0.7 else 'Program_Magenta', (x, y, z), s, 0.03,
                        geom.euler_matrix(r.rng(0, 60), r.rng(0, 90), r.rng(0, 60)))
    # West: low cable trays only.
    z = 0.6
    while z < LENGTH - 0.6:
        r.kit.box('Program_Frame', (-13.9, 0.25, z), (1.1, 0.5, 1.9))
        r.kit.box('Program_Cyan', (-13.35, 0.46, z), (0.03, 0.04, 1.7))
        z += 2.2
    edge_racks(r)


def edge_racks(r):
    """Low racks and cable trays inside the side walls (with collision) so the wall reads as machinery."""
    for side, x in ((1, 12.2), (-1, -12.3)):
        z = 1.6
        while z < LENGTH - 1.6:
            h = r.rng(1.6, 2.6) if side > 0 else r.rng(0.5, 0.9)
            length = r.rng(1.6, 2.6)
            if r.is_clear(x, z, 0.3):
                r.kit.box('Program_Panels', (x, h / 2, z), (1.3, h, length))
                r.kit.box('Program_Cyan' if r.rng(0, 1) < 0.75 else 'Program_Magenta', (x - side * 0.66, h * r.rng(0.4, 0.85), z), (0.03, 0.05, length * 0.8))
                for k in range(3 if side > 0 else 1):
                    r.kit.box('Program_Grid', (x - side * 0.66, 0.3 + k * 0.45, z + r.rng(-0.4, 0.4)), (0.03, 0.03, r.rng(0.3, 0.9)))
            z += length + r.rng(0.2, 0.7)
        r.block(f'Rack line {"east" if side > 0 else "west"}', (x + side * 0.1, 1.2, LENGTH / 2), (1.5, 2.4, LENGTH - 3.0))


def seams(r):
    for z in (0.0, LENGTH):
        inside = 0.4 if z == 0 else LENGTH - 0.4
        for side in (-1, 1):
            x = side * 3.9
            r.kit.box('Program_Frame', (x, 1.6, inside), (0.8, 3.2, 0.8))
            r.kit.box('Program_Cyan', (x - side * 0.41, 1.6, inside), (0.03, 2.8, 0.1))
            r.kit.box('Program_Hazard', (x, 3.25, inside), (0.9, 0.08, 0.9))
            xs = [side * v for v in (4.3, 6.9, 9.5, 12.1)]
            for px in xs[1:]:
                pylon(r, px, inside)
            for a, b in zip(xs, xs[1:]):
                for y in (0.5, 1.1, 1.7):
                    r.kit.box('Program_Magenta', ((a + b) / 2, y, inside), (abs(b - a), 0.035, 0.035))


def pylon(r, x, z, height=2.2):
    r.kit.box('Program_Frame', (x, height / 2, z), (0.36, height, 0.36))
    for y in (0.5, 1.1, 1.7):
        r.kit.box('Program_Magenta', (x, y, z), (0.42, 0.06, 0.42))
    r.kit.box('Program_Frame', (x, height + 0.05, z), (0.46, 0.1, 0.46))


def floor_detail(r, strips=16, chevrons=True):
    d.scatter(r, strips, (-11, 11), (1, 35.4), lambda x, z: r.kit.box(
        'Program_Cyan' if r.rng(0, 1) < 0.7 else 'Program_Magenta', (x, 0.025, z),
        (0.04, 0.012, r.rng(1.0, 3.2)) if r.rng(0, 1) < 0.5 else (r.rng(1.0, 3.2), 0.012, 0.04)), min_spacing=1.6, margin=0.2)
    d.scatter(r, 10, (-11, 11), (1, 35.4), lambda x, z: r.kit.box('Program_Panels', (x, 0.05, z), (r.rng(1.0, 2.0), 0.1, r.rng(1.0, 2.0))),
              min_spacing=2.5, margin=0.3)


def lane(r, points, width=3.0):
    dense = d.densify(points, 0.9)
    r.keep_clear_path(dense, width + 0.6)
    for sign in (-1, 1):
        edge = []
        for i, (x, z) in enumerate(dense):
            a = dense[max(0, i - 1)]
            b = dense[min(len(dense) - 1, i + 1)]
            dx, dz = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dz) or 1
            edge.append((x + sign * dz / length * width / 2, z - sign * dx / length * width / 2))
        r.kit.ribbon('Program_Grid', edge, 0.05, 0.012, thickness=0.01)
    return dense


def chevrons(r, x, z0, z1, step=2.2):
    z = z0
    while z < z1:
        for side in (-1, 1):
            r.kit.box('Program_Data', (x + side * 0.35, 0.02, z), (0.75, 0.012, 0.09), geom.yaw_matrix(side * 38))
        z += step


def floating_cubes(r, count, x_range, z_range, y_range=(2.0, 5.5)):
    for _ in range(count):
        x, z, y = r.rng(*x_range), r.rng(*z_range), r.rng(*y_range)
        r.kit.wire_cube('Program_Cyan' if r.rng(0, 1) < 0.65 else 'Program_Magenta', (x, y, z), r.rng(0.15, 0.45), 0.022,
                        geom.euler_matrix(r.rng(0, 45), r.rng(0, 90), r.rng(0, 45)))


# ---- procedural fallbacks ---------------------------------------------------------------------------
def fallback_monolith(r, p, size, yaw):
    data_tower(r, p[0], p[2], size[0] * 0.8, size[1], size[2] * 0.8)


def fallback_pod(r, p, size, yaw):
    x, _, z = p
    r.kit.disc('Program_Frame', (x, 0.2, z), size[0] * 0.5, 0.4, 12)
    r.kit.cone('Program_Glass', (x, size[1] * 0.5, z), size[0] * 0.38, size[0] * 0.38, size[1] * 0.75, 12)
    r.kit.ring('Program_Cyan', (x, size[1] * 0.3, z), size[0] * 0.38, size[0] * 0.42, 0.08, 16)
    r.kit.ring('Program_Cyan', (x, size[1] * 0.7, z), size[0] * 0.38, size[0] * 0.42, 0.08, 16)
    r.kit.disc('Program_Frame', (x, size[1] * 0.92, z), size[0] * 0.46, 0.3, 12)


def fallback_turret(r, p, size, yaw):
    x, _, z = p
    r.kit.cone('Program_Frame', (x, 0.5, z), size[0] * 0.45, size[0] * 0.35, 1.0, 8)
    r.kit.box('Program_Panels', (x, 1.4, z), (size[0] * 0.6, 0.7, size[2] * 0.5), geom.yaw_matrix(yaw))
    r.kit.disc('Program_Magenta', (x, 1.45, z), 0.16, 0.2, 10)


def fallback_terminal(r, p, size, yaw):
    x, _, z = p
    rot = geom.yaw_matrix(yaw)
    c = np.asarray(p, float)
    r.kit.box('Program_Frame', c + rot @ np.array([0, 0.5, 0]), (size[0] * 0.3, 1.0, size[2] * 0.4), rot)
    r.kit.box('Program_Panels', c + rot @ np.array([0, 1.0, 0]), (size[0] * 0.9, 0.12, size[2] * 0.7), rot @ geom.euler_matrix(-18, 0, 0))
    r.kit.box('Program_Cyan', c + rot @ np.array([0, 1.7, 0.25]), (size[0] * 0.85, 0.8, 0.05), rot)


def fallback_pipes(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    c = np.asarray(p, float)
    for k in range(3):
        off = (k - 1) * 0.55
        a = c + rot @ np.array([-size[0] * 0.4, 0, off])
        b = c + rot @ np.array([-size[0] * 0.4, size[1] * 0.8, off])
        e = c + rot @ np.array([size[0] * 0.45, size[1] * 0.8, off])
        r.kit.tube('Program_Steel', [a, b, e], 0.18, 8, taper=1.0)
    r.kit.box('Program_Warning', c + rot @ np.array([size[0] * 0.25, 0.6, 0]), (1.0, 1.2, 1.4), rot)


def fallback_pylon(r, p, size, yaw):
    pylon(r, p[0], p[2], size[1])


def fallback_wreck(r, p, size, yaw):
    rot = geom.yaw_matrix(yaw)
    c = np.asarray(p, float)
    r.kit.box('Program_Panels', c + rot @ np.array([0, 0.9, 0]), (size[0] * 0.5, size[1] * 0.9, size[2] * 0.6), rot @ geom.euler_matrix(0, 0, 38))
    for k in range(5):
        r.kit.box('Program_Frame', c + rot @ np.array([r.rng(-1.2, 1.2), 0.2, r.rng(-1, 1)]), (0.6, 0.3, 0.5), geom.yaw_matrix(r.rng(0, 90)))
    r.kit.box('Program_Red', c + rot @ np.array([0, 0.9, -size[2] * 0.3]), (size[0] * 0.4, 0.05, 0.05), rot)


def fallback_robot(r, p, size, yaw):
    x, _, z = p
    r.kit.box('Program_Panels', (x, size[1] * 0.35, z), (size[0] * 0.6, size[1] * 0.45, size[2] * 0.45))
    r.kit.box('Program_Frame', (x, size[1] * 0.75, z), (size[0] * 0.8, size[1] * 0.3, size[2] * 0.5))
    r.kit.box('Program_Cyan', (x, size[1] * 0.92, z - size[2] * 0.2), (size[0] * 0.3, 0.1, 0.05))
    for side in (-1, 1):
        r.kit.box('Program_Panels', (x + side * size[0] * 0.45, size[1] * 0.3, z), (size[0] * 0.2, size[1] * 0.6, size[2] * 0.25))


# ---- rooms -------------------------------------------------------------------------------------------
def new_room(index, kind, notes):
    r = Room(THEME, index, f'{KEY}_{index:02d}', TITLES[index], kind, stable_seed(KEY, index), notes)
    d.base_floor(r, 'Program_Floor')
    return r


def room_01():
    r = new_room(1, 'Arrival', 'Boot sector: the player boots on a lit spawn pad; a chevron data lane leads past terminals to the first gate.')
    r.kit.box('Program_FloorDark', (0, -0.10, -8), (58, 0.22, 16))
    for x in range(-24, 31, 4):
        r.kit.box('Program_Grid', (x, 0.02, -8), (0.05, 0.02, 16))
    for zz in range(-16, 0, 4):
        r.kit.box('Program_Grid', (3, 0.02, zz), (54, 0.02, 0.05))
    r.kit.cone('Program_Frame', (0, 0.06, 4), 2.6, 2.4, 0.12, 6, phase=0.5)
    r.kit.ring('Program_Cyan', (0, 0.14, 4), 2.05, 2.2, 0.03, 6)
    r.kit.ring('Program_Data', (0, 0.14, 4), 1.2, 1.28, 0.03, 36)
    for k in range(4):
        a = math.radians(45 + k * 90)
        x, z = math.sin(a) * 2.9, 4 + math.cos(a) * 2.9
        r.kit.box('Program_Frame', (x, 0.2, z), (0.5, 0.4, 0.5))
        r.kit.box('Program_Data', (x, 2.5, z), (0.1, 5.0, 0.1))
    lane(r, [(0, 7), (0, 36.4)], 3.0)
    chevrons(r, 0, 8, 35)
    r.model('holo_terminal', (-5.2, 0, 10.0), (2.2, 2.3, 1.3), 90, fallback=fallback_terminal)
    r.model('holo_terminal', (5.4, 0, 15.5), (2.2, 2.3, 1.3), -90, fallback=fallback_terminal)
    r.model('holo_terminal', (-5.6, 0, 21.0), (2.2, 2.3, 1.3), 90, fallback=fallback_terminal)
    for i, z in enumerate((9.0, 14.0, 19.0, 24.0, 29.0)):
        r.model('containment_pod', (9.4, 0, z), (2.4, 4.6, 2.4), 270, collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_pod)
    r.model('server_monolith', (-9.0, 0, 29.5), (3.2, 4.0, 3.2), 0, collider={'size': (2.0, 3.0, 2.0)}, fallback=fallback_monolith)
    r.model('security_turret', (-4.8, 0, 31.0), (1.8, 2.2, 1.8), 150, collider={'size': (1.3, 2.0, 1.3)}, fallback=fallback_turret)
    for x, z in ((-16, -6), (-10, -12), (8, -7), (14, -13), (20, -4)):
        data_tower(r, x, z, r.rng(1.6, 2.6), r.rng(3, 8))
    backdrop(r, tower_z=(6.0, 20.0, 32.0))
    seams(r)
    floor_detail(r)
    floating_cubes(r, 18, (-10, 10), (-6, 34))
    r.light('Boot pad', (0, 2.2, 4), '48e4ff', 2.4, 9.0)
    r.light('Pod row', (7.5, 3.0, 19), '37c6ff', 1.8, 10.0)
    r.light('Terminal glow', (-4.5, 2.0, 15), '6fb0ff', 1.3, 8.0)
    return r


def room_02():
    r = new_room(2, 'Combat', 'Firewall detention: pod rows on both walls, a kneeling warden robot splitting the hall and two beam barriers forcing a zigzag.')
    for z in (6.0, 11.0, 16.0, 21.0, 26.0, 31.0):
        r.model('containment_pod', (9.6, 0, z), (2.4, 4.6, 2.4), 270, collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_pod)
    for z in (8.5, 16.5, 24.5, 32.0):
        r.model('containment_pod', (-9.8, 0, z), (2.1, 3.6, 2.1), 90, collider={'size': (1.6, 3.0, 1.6)}, fallback=fallback_pod)
    r.model('sentinel_robot', (0.4, 0, 19.6), (4.0, 5.4, 3.8), 180, collider={'size': (3.0, 3.0, 2.8)}, fallback=fallback_robot)
    r.kit.cone('Program_Frame', (0.4, 0.08, 19.6), 3.0, 2.9, 0.16, 8, phase=0.5)
    r.kit.ring('Program_Red', (0.4, 0.18, 19.6), 2.7, 2.8, 0.02, 8)
    r.keep_clear_circle((0.4, 19.6), 3.2)
    # Beam barriers: west half at z = 11.5, east half at z = 27.
    for z, x0, x1 in ((11.5, -8.4, -1.6), (27.0, 1.6, 8.4)):
        xs = np.linspace(x0, x1, 3)
        for x in xs:
            r.model('firewall_pylon', (x, 0, z), (0.9, 3.4, 0.9), 0, collider={'size': (0.7, 3.0, 0.7)}, fallback=fallback_pylon)
        for y in (0.6, 1.3, 2.0):
            r.kit.box('Program_Magenta', ((x0 + x1) / 2, y, z), (x1 - x0, 0.045, 0.045))
        r.block(f'Firewall beam {z:.0f}', ((x0 + x1) / 2, 1.2, z), (x1 - x0, 2.4, 0.3))
        r.kit.box('Program_Hazard', ((x0 + x1) / 2, 0.02, z), (x1 - x0 + 0.8, 0.012, 1.2))
    r.model('security_turret', (6.4, 0, 8.0), (1.8, 2.2, 1.8), 220, collider={'size': (1.3, 2.0, 1.3)}, fallback=fallback_turret)
    r.model('security_turret', (-6.4, 0, 31.5), (1.6, 2.0, 1.6), 40, collider={'size': (1.2, 2.0, 1.2)}, fallback=fallback_turret)
    lane(r, [(0, 0), (0, 4), (3.2, 10), (4.5, 15), (4.5, 24), (-1.5, 29), (-1.0, 33), (0, 36.4)], 2.8)
    backdrop(r, tower_z=(4.0, 17.0, 30.0))
    seams(r)
    floor_detail(r, 12)
    floating_cubes(r, 14, (-8, 8), (4, 34))
    r.light('Warden core', (0.4, 3.0, 17.5), 'ff3a6a', 1.8, 8.0)
    r.light('Pods east', (7.8, 3.0, 14), '37c6ff', 1.8, 10.0)
    r.light('Pods west', (-8.0, 2.4, 27), '37c6ff', 1.4, 8.0)
    r.enemies = [(-4.2, 0.08, 17.0), (4.5, 0.08, 22.5), (-3.2, 0.08, 31.2)]
    return r


def room_03():
    r = new_room(3, 'Combat', 'Memory maze: an offset grid of data blocks (tall east, short west) forms a cover field with floating cache cubes.')
    r.keep_clear_path(d.densify([(0, 0), (0, 6)], 0.9), 3.6)
    r.keep_clear_path(d.densify([(0, 31), (0, 36.4)], 0.9), 3.6)
    rows = [8.0, 12.5, 17.0, 21.5, 26.0, 30.0]
    for j, z in enumerate(rows):
        offset = 0 if j % 2 == 0 else 2.6
        x = -9.2 + offset
        k = 0
        while x <= 9.4:
            if abs(x) < 1.0 and j in (0, 5):
                x += 5.2
                continue
            h = 1.4 + d.smoothstep(-9, 9, x) * 3.6 + r.rng(-0.4, 0.6)
            if (j + k) % 4 == 1 and x > 2:
                r.model('server_monolith', (x, 0, z), (1.8, h + 1.2, 1.8), 90 * r.rng_int(0, 3), collider={'size': (1.5, 3.0, 1.5)}, fallback=fallback_monolith)
            else:
                data_tower(r, x, z, 1.6, h, 1.6)
                r.block(f'Block {j}-{k}', (x, 1.5, z), (1.6, 3.0, 1.6))
            k += 1
            x += 5.2
    r.kit.box('Program_Data', (0, 0.02, 3.5), (2.6, 0.012, 0.08))
    backdrop(r, tower_z=(9.0, 22.0, 33.0))
    seams(r)
    floor_detail(r, 18)
    floating_cubes(r, 30, (-10, 10), (6, 32), (2.6, 6.5))
    r.light('Cache glow west', (-5.0, 2.6, 14), '48e4ff', 1.6, 9.0)
    r.light('Cache glow east', (5.0, 3.4, 24), 'd542cd', 1.6, 9.0)
    r.light('Maze centre', (0, 3.2, 19), '7df3ff', 1.4, 10.0)
    r.enemies = [(-2.6, 0.08, 14.8), (2.6, 0.08, 23.8), (-5.2, 0.08, 28.2)]
    return r


def room_04():
    r = new_room(4, 'Combat', 'Data river: a glowing stream of data cuts the hall diagonally; two light bridges cross it, coolant plants line its banks.')
    a, b = np.array([-13.5, 11.0]), np.array([13.5, 25.5])
    direction = (b - a) / np.linalg.norm(b - a)
    normal = np.array([-direction[1], direction[0]])
    width = 3.4
    yaw = math.degrees(math.atan2(direction[0], direction[1]))
    length = float(np.linalg.norm(b - a))
    center = (a + b) / 2
    r.kit.box('Program_Void', (center[0], 0.015, center[1]), (width, 0.03, length), geom.yaw_matrix(yaw))
    for k in range(9):
        off = (k - 4) * width / 10
        start = a + normal * off
        r.kit.ribbon('Program_Data' if k % 4 == 0 else 'Program_Grid', [tuple(start + direction * t) for t in np.linspace(0.5, length - 0.5, 20)],
                     0.05, 0.035, thickness=0.01)
    for side in (-1, 1):
        edge = [tuple(a + normal * side * (width / 2 + 0.15) + direction * t) for t in np.linspace(0, length, 24)]
        r.kit.ribbon('Program_Hazard', edge, 0.16, 0.05, thickness=0.06)
    bridges = [0.36, 0.64]
    for t in bridges:
        c = a + direction * length * t
        r.kit.box('Program_Panels', (c[0], 0.07, c[1]), (2.6, 0.12, width + 1.4), geom.yaw_matrix(yaw + 90))
        for side in (-1, 1):
            e = c + direction * side * 1.35
            r.kit.box('Program_Cyan', (e[0], 0.14, e[1]), (0.05, 0.03, width + 1.2), geom.yaw_matrix(yaw + 90))
    # River colliders with gaps at the bridges.
    cuts = [0.0] + [x for t in bridges for x in (t - 1.5 / length, t + 1.5 / length)] + [1.0]
    for k in range(0, len(cuts), 2):
        t0, t1 = cuts[k], cuts[k + 1]
        c = a + direction * length * (t0 + t1) / 2
        r.block(f'Data river {k // 2}', (c[0], d.WATER_BLOCK / 2, c[1]), (width - 0.6, d.WATER_BLOCK, length * (t1 - t0)), yaw)
    r.keep_clear_path([tuple(a), tuple(b)], width + 1.5)
    r.model('coolant_pipes', (-8.2, 0, 5.5), (4.2, 3.2, 2.2), 20, fallback=fallback_pipes)
    r.model('coolant_pipes', (8.8, 0, 29.8), (4.2, 3.2, 2.2), 200, fallback=fallback_pipes)
    r.model('coolant_pipes', (9.2, 0, 13.0), (3.4, 2.6, 1.8), 200, fallback=fallback_pipes)
    r.model('holo_terminal', (-7.8, 0, 26.5), (2.2, 2.3, 1.3), 110, fallback=fallback_terminal)
    r.model('server_monolith', (-3.2, 0, 32.2), (2.2, 3.4, 2.2), 0, collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_monolith)
    backdrop(r, tower_z=(5.0, 30.0))
    seams(r)
    floor_detail(r, 10)
    floating_cubes(r, 16, (-10, 10), (8, 28), (1.5, 4.5))
    r.light('River glow west', (-6.0, 1.5, 14.0), '7df3ff', 2.2, 9.0)
    r.light('River glow east', (6.0, 1.5, 22.5), '7df3ff', 2.2, 9.0)
    r.enemies = [(-4.5, 0.08, 20.5), (5.5, 0.08, 16.0), (1.5, 0.08, 29.5)]
    return r


def room_06():
    r = new_room(6, 'Combat', 'Central processor: the core hangs over a raised hex dais in the middle; thick cables radiate to the walls and the route rings it.')
    cx, cz = 0.0, 19.5
    r.kit.cone('Program_Frame', (cx, 0.18, cz), 4.2, 3.9, 0.36, 6, phase=0.5)
    r.kit.ring('Program_Cyan', (cx, 0.38, cz), 3.5, 3.62, 0.03, 6)
    r.kit.ring('Program_Magenta', (cx, 0.02, cz), 5.6, 5.72, 0.02, 48)
    r.model('processor_core', (cx, 0.36, cz), (5.0, 6.2, 5.0), 30, collider=None)
    r.block('Core dais', (cx, 1.5, cz), (5.8, 3.0, 5.8), 30)
    r.keep_clear_circle((cx, cz), 5.0)
    for k in range(6):
        a = math.radians(k * 60 + 30)
        p0 = (cx + math.sin(a) * 3.6, 0.35, cz + math.cos(a) * 3.6)
        p1 = (cx + math.sin(a) * 7.0, 0.12, cz + math.cos(a) * 7.0)
        p2 = (cx + math.sin(a) * 12.0, 0.12, cz + math.cos(a) * 12.0)
        r.kit.tube('Program_Frame', [p0, p1, p2], 0.22, 8, taper=1.0)
        r.kit.tube('Program_Cyan', [(p0[0], 0.6, p0[2]), (p1[0], 0.36, p1[2]), (p2[0], 0.36, p2[2])], 0.03, 4, taper=1.0)
    for k, a in enumerate((60, 150, 240, 330)):
        x, z = cx + math.sin(math.radians(a)) * 7.8, cz + math.cos(math.radians(a)) * 7.8
        r.model('holo_terminal', (x, 0, z), (2.2, 2.3, 1.3), a + 180, fallback=fallback_terminal)
    r.model('coolant_pipes', (9.2, 0, 7.0), (4.2, 3.2, 2.2), 270, fallback=fallback_pipes)
    r.model('coolant_pipes', (-9.0, 0, 31.5), (4.0, 3.0, 2.1), 90, fallback=fallback_pipes)
    r.model('server_monolith', (9.6, 0, 30.5), (2.6, 5.0, 2.6), 90, collider={'size': (2.0, 3.0, 2.0)}, fallback=fallback_monolith)
    lane(r, [(0, 0), (0, 12.5)], 3.0)
    lane(r, [(0, 26.5), (0, 36.4)], 3.0)
    backdrop(r, tower_z=(8.0, 21.0, 33.0))
    seams(r)
    floor_detail(r, 10)
    floating_cubes(r, 20, (-7, 7), (13, 26), (4.0, 8.0))
    r.light('Core', (cx, 4.4, cz), '36d6ff', 3.0, 12.0)
    r.light('Core underglow', (cx, 0.9, cz), 'd542cd', 1.6, 7.0)
    r.enemies = [(-6.4, 0.08, 17.0), (6.4, 0.08, 22.5), (0.0, 0.08, 29.2)]
    return r


def room_07():
    r = new_room(7, 'Combat', 'Corrupted sector: missing floor panels open red voids along an S-route; toppled racks and glitch cubes spill across the floor.')
    pits = [((-5.5, 9.0), 3.6, 3.0, 10), ((5.8, 15.0), 3.8, 3.2, -8), ((-4.8, 22.5), 4.2, 3.0, 15), ((5.2, 29.0), 3.4, 2.6, -12)]
    for (x, z), w, dpt, yaw in pits:
        r.kit.box('Program_Void', (x, 0.02, z), (w, 0.03, dpt), geom.yaw_matrix(yaw))
        r.kit.box('Program_Red', (x, 0.0, z), (w * 0.8, 0.02, dpt * 0.8), geom.yaw_matrix(yaw))
        rot = geom.yaw_matrix(yaw)
        for sx, sz, lw, ld in ((0, 0.5, w, 0.08), (0, -0.5, w, 0.08), (0.5, 0, 0.08, dpt), (-0.5, 0, 0.08, dpt)):
            c = np.array([x, 0.05, z]) + rot @ np.array([sx * w, 0, sz * dpt])
            r.kit.box('Program_Red', c, (lw, 0.05, ld), rot)
        for k in range(4):
            px = x + r.rng(-w / 2, w / 2)
            pz = z + r.rng(-dpt / 2, dpt / 2)
            r.kit.box('Program_FloorDark', (px, r.rng(0.1, 0.7), pz), (r.rng(0.8, 1.4), 0.1, r.rng(0.8, 1.4)),
                      geom.euler_matrix(r.rng(-35, 35), r.rng(0, 90), r.rng(-35, 35)))
        r.block(f'Void {x:.0f},{z:.0f}', (x, d.WATER_BLOCK / 2, z), (w - 0.3, d.WATER_BLOCK, dpt - 0.3), yaw)
        r.keep_clear_circle((x, z), max(w, dpt) * 0.62)
    r.model('broken_server_rack', (-8.6, 0, 15.5), (3.4, 2.6, 2.4), 40, fallback=fallback_wreck)
    r.model('broken_server_rack', (9.0, 0, 22.0), (3.2, 2.4, 2.2), 210, fallback=fallback_wreck)
    r.model('broken_server_rack', (-1.2, 0, 33.0), (2.6, 2.0, 1.9), 100, fallback=fallback_wreck)
    r.model('security_turret', (8.6, 0, 7.4), (1.6, 2.0, 1.6), 250, collider={'size': (1.2, 2.0, 1.2)}, fallback=fallback_turret)
    for _ in range(26):
        x, z = r.rng(-11, 11), r.rng(3, 34)
        if r.is_clear(x, z, 0.3):
            r.kit.wire_cube('Program_Red' if r.rng(0, 1) < 0.5 else 'Program_Magenta', (x, r.rng(0.4, 3.2), z), r.rng(0.12, 0.4), 0.025,
                            geom.euler_matrix(r.rng(0, 60), r.rng(0, 90), r.rng(0, 60)))
    backdrop(r, tower_z=(12.0, 26.0), density=0.8)
    seams(r)
    floor_detail(r, 8)
    r.light('Corruption A', (-5.5, 1.2, 9.0), 'ff2a4a', 1.8, 7.0)
    r.light('Corruption B', (5.8, 1.2, 15.0), 'ff2a4a', 1.8, 7.0)
    r.light('Corruption C', (-4.8, 1.2, 22.5), 'ff2a4a', 1.8, 7.0)
    r.light('Corruption D', (5.2, 1.2, 29.0), 'ff2a4a', 1.6, 7.0)
    r.enemies = [(3.4, 0.08, 11.5), (-2.0, 0.08, 18.5), (-1.5, 0.08, 27.5)]
    return r


def room_05():
    r = new_room(5, 'Threshold', 'Escape protocol: firewall pylons line the approach to the data gateway; upload beams rise beside it into the grid void.')
    lane(r, [(0, 0), (0, 36.4)], 3.4)
    chevrons(r, 0, 3, 30)
    for z in (6.0, 12.0, 18.0, 24.0):
        for side in (-1, 1):
            r.model('firewall_pylon', (side * 4.2, 0, z), (0.9, 3.6, 0.9), 0, collider={'size': (0.7, 3.0, 0.7)}, fallback=fallback_pylon)
    r.kit.box('Program_Frame', (0, 0.12, 31.0), (8.0, 0.24, 4.0))
    r.kit.box('Program_Frame', (0, 0.3, 32.0), (6.5, 0.2, 2.6))
    r.model('data_gateway', (0, 0.4, 33.6), (8.0, 8.4, 3.2), 0, collider=None)
    for side in (-1, 1):
        r.block(f'Gateway tower {side}', (side * 3.5, 1.5, 33.6), (1.4, 3.0, 2.4))
        r.kit.box('Program_Data', (side * 6.4, 7.0, 33.0), (0.35, 14.0, 0.35))
        r.kit.box('Program_Frame', (side * 6.4, 0.4, 33.0), (1.2, 0.8, 1.2))
    r.model('containment_pod', (-8.6, 0, 16.0), (2.4, 4.6, 2.4), 90, collider={'size': (1.8, 3.0, 1.8)}, fallback=fallback_pod)
    r.model('sentinel_robot', (8.4, 0, 20.0), (3.6, 5.0, 3.4), 250, collider={'size': (2.6, 3.0, 2.6)}, fallback=fallback_robot)
    # North vista: grid void with far monoliths and a light column.
    r.kit.box('Program_FloorDark', (3, -0.10, 47.2), (58, 0.22, 21.6))
    for x in range(-24, 31, 4):
        r.kit.box('Program_Grid', (x, 0.02, 47.2), (0.05, 0.02, 21.6))
    for zz in range(38, 58, 4):
        r.kit.box('Program_Grid', (3, 0.02, zz), (54, 0.02, 0.05))
    for x, z, h in ((-12, 46, 16), (12, 44, 18), (-4, 55, 22), (18, 54, 20)):
        r.model('server_monolith', (x, 0, z), (5.0, h, 5.0), r.rng(0, 90), collider=None, fallback=fallback_monolith)
    r.kit.box('Program_Data', (0, 12, 50), (1.4, 24, 1.4))
    floating_cubes(r, 26, (-14, 14), (38, 56), (3.0, 12.0))
    backdrop(r, tower_z=(5.0, 17.0, 29.0))
    seams(r)
    floor_detail(r, 10)
    r.light('Gateway', (0, 4.0, 32.0), '7df3ff', 3.0, 12.0)
    r.light('Upload column', (0, 6.0, 48.0), '9ff8ff', 3.0, 20.0)
    return r


ROOMS = [room_01, room_02, room_03, room_04, room_05, room_06, room_07]
