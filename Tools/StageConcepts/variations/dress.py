"""Shared dressing helpers: floors, paths, out-of-bounds backdrops, seams, scatter and procedural fallbacks.

Camera rules (VariationPlan.md section 2): east (x > 9) and the far end are backdrop zones where height is
free; the west side is foreground, so anything within 6 m of the route stays below ~2.5 m; the frame reaches
~9 m past the east wall and ~12 m behind the player, so the ground continues outside the walkable room.
"""
import math

import numpy as np

import geom
from geom import v
from room import LENGTH, DOOR_HALF

# Height of the invisible colliders over water, voids and pits. Taller than the 0.24 m step (the player cannot
# climb them) and seen by enemy steering (SphereCast 0.64-1.36 m), but below the talisman line-of-sight ray
# (cast origin ~1.2 m to the target's aim point at 1 m), so the player can still shoot across water.
WATER_BLOCK = 0.7


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def seam_weight(z, fade=4.5):
    """0 at both seams (z = 0 and z = LENGTH), 1 inside; keeps backdrops continuous between shuffled rooms."""
    return smoothstep(0.0, fade, z) * smoothstep(LENGTH, LENGTH - fade, z)


# ---- ground ---------------------------------------------------------------------------------------
def base_floor(r, material, z0=0.0, z1=LENGTH):
    r.kit.box(material, (0, -0.10, (z0 + z1) / 2), (26.4, 0.22, z1 - z0))


def outer_ground(r, material, seam_profile, room_profile, x0, x1, z0=0.0, z1=LENGTH, res=0.9, skirt=-0.6):
    """Out-of-bounds terrain. Height = seam profile at the seams, room-specific shape in between."""
    nx = max(2, int(round(abs(x1 - x0) / res)))
    nz = max(2, int(round((z1 - z0) / res)))
    lo, hi = min(x0, x1), max(x0, x1)

    def height(x, z):
        w = seam_weight(z) if z0 >= 0 and z1 <= LENGTH else 1.0
        return seam_profile(x) + w * (room_profile(x, z) - seam_profile(x))

    r.kit.heightfield(material, lo, hi, z0, z1, nx, nz, height, skirt=skirt)
    return height


def path(r, material, points, width, y=0.012, jitter=0.1, clear=True, clear_width=None, thickness=0.018):
    """Flat walkable strip (trail, road, boardwalk base). Also reserves the lane from scatter."""
    dense = densify(points, 0.9)
    r.kit.ribbon(material, dense, width, y, thickness=thickness, jitter=jitter)
    if clear:
        r.keep_clear_path(dense, clear_width or width + 0.6)
    return dense


def densify(points, step):
    out = [tuple(points[0])]
    for (ax, az), (bx, bz) in zip(points, points[1:]):
        n = max(1, int(math.ceil(math.hypot(bx - ax, bz - az) / step)))
        for k in range(1, n + 1):
            t = k / n
            out.append((ax + (bx - ax) * t, az + (bz - az) * t))
    return catmull(out) if len(out) > 3 else out


def catmull(points, passes=2):
    pts = list(points)
    for _ in range(passes):
        smoothed = [pts[0]]
        for i in range(1, len(pts) - 1):
            smoothed.append(((pts[i - 1][0] + 2 * pts[i][0] + pts[i + 1][0]) / 4, (pts[i - 1][1] + 2 * pts[i][1] + pts[i + 1][1]) / 4))
        smoothed.append(pts[-1])
        pts = smoothed
    return pts


def blob_outline(cx, cz, rx, rz, seed_rng, points=26, wobble=0.16):
    out = []
    phase = seed_rng(0, math.tau)
    for i in range(points):
        a = i * math.tau / points
        f = 1 + wobble * math.sin(3 * a + phase) + wobble * 0.5 * math.sin(5 * a + phase * 1.7)
        out.append((cx + math.sin(a) * rx * f, cz + math.cos(a) * rz * f))
    return out


def flat_polygon(r, material, outline, y, thickness=0.02):
    """Filled polygon (fan from centroid) with a thin side band; used for water, plazas and pits."""
    g = r.kit.g(material)
    # The fan below expects a clockwise outline seen from above (x right, z up); flip anticlockwise input.
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(outline, outline[1:] + outline[:1]))
    if area > 0:
        outline = list(reversed(outline))
    cx = sum(p[0] for p in outline) / len(outline)
    cz = sum(p[1] for p in outline) / len(outline)
    center = v(cx, y, cz)
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        pa, pb = v(a[0], y, a[1]), v(b[0], y, b[1])
        # outline runs clockwise seen from above (sin/cos order), so (a, b, center) faces up in Unity
        g.tri(pa, pb, center)
        g.quad(pb, pa, pa - v(0, thickness, 0), pb - v(0, thickness, 0))


def polygon_blockers(r, name, outline, inset=0.45, slices=6, height=WATER_BLOCK, exclude=None):
    """Approximate a closed pond/pit outline with horizontal collider slabs (inset so banks stay walkable).
    exclude=(x0, x1, z0, z1) keeps a walkable strip (a jetty) free: slabs are split around its z band and,
    inside the band, clipped to the part west of x0."""
    zs = [p[1] for p in outline]
    z0, z1 = min(zs) + inset, max(zs) - inset
    cuts = [z0 + k * (z1 - z0) / slices for k in range(slices + 1)]
    if exclude:
        cuts = sorted(set(cuts + [c for c in (exclude[2], exclude[3]) if z0 < c < z1]))
    n = len(outline)

    def span(zc):
        hits = []
        for i in range(n):
            (ax, az), (bx, bz) = outline[i], outline[(i + 1) % n]
            if (az - zc) * (bz - zc) <= 0 and az != bz:
                t = (zc - az) / (bz - az)
                hits.append(ax + (bx - ax) * t)
        return (min(hits), max(hits)) if len(hits) >= 2 else None

    for k, (za, zb) in enumerate(zip(cuts, cuts[1:])):
        spans = [s for s in (span(za), span((za + zb) / 2), span(zb)) if s]
        if not spans or zb - za < 0.05:
            continue
        x0 = max(a for a, _ in spans) + inset
        x1 = min(b for _, b in spans) - inset
        if exclude and za >= exclude[2] - 1e-6 and zb <= exclude[3] + 1e-6:
            x1 = min(x1, exclude[0])
        if x1 - x0 > 0.3:
            r.block(f'{name}_{k}', ((x0 + x1) / 2, height / 2, (za + zb) / 2), (x1 - x0, height, zb - za))


# ---- scatter --------------------------------------------------------------------------------------
def scatter(r, count, x_range, z_range, place, min_spacing=0.8, margin=0.3, tries=40, extra=None):
    placed = []
    for _ in range(count):
        for _ in range(tries):
            x, z = r.rng(*x_range), r.rng(*z_range)
            if extra and not extra(x, z):
                continue
            if not r.is_clear(x, z, margin):
                continue
            if any((x - px) ** 2 + (z - pz) ** 2 < min_spacing ** 2 for px, pz in placed):
                continue
            placed.append((x, z))
            place(x, z)
            break
    return placed


# ---- props ----------------------------------------------------------------------------------------
def fireflies(r, material, count, x_range, z_range, y_range=(0.5, 2.4), size=0.05):
    for _ in range(count):
        x, z, y = r.rng(*x_range), r.rng(*z_range), r.rng(*y_range)
        r.kit.octahedron(material, (x, y, z), (size, size * 1.5, size))


def tuft(r, material, x, z, height, blades=7):
    r.kit.grass(material, (x, 0.02, z), height, r.rng(0, 360), blades=blades)


def mushroom(r, cap, stem, x, z, height, cap_scale=0.65, tilt=0.0):
    r.kit.cone(stem, (x, height * 0.45, z), 0.06 * height / 0.4, 0.045 * height / 0.4, height * 0.9, 7)
    r.kit.cone(cap, (x, height * 0.95, z), height * cap_scale, height * 0.10, height * 0.32, 10)


def log(r, material, a, b, radius, sides=9, end_material=None):
    a, b = np.asarray(a, float), np.asarray(b, float)
    delta = b - a
    length = float(np.linalg.norm(delta))
    rot = geom.from_to(v(0, 1, 0), delta / length)
    r.kit.cone(material, (a + b) / 2, radius, radius * 0.93, length, sides, rot, caps=end_material is None)
    if end_material:
        r.kit.cone(end_material, a + delta * 0.004, radius * 0.9, radius * 0.9, length * 0.008 + 0.01, sides, rot)
        r.kit.cone(end_material, b - delta * 0.004, radius * 0.85, radius * 0.85, length * 0.008 + 0.01, sides, rot)


def planks(r, material, points, width, y=0.07, plank=0.28, gap=0.05, thickness=0.06, jitter=2.0):
    """Boardwalk of individual planks across a polyline (walkable: tops stay below the 0.24 m step)."""
    pts = densify(points, 0.25)
    travelled, next_at = 0.0, 0.0
    for (ax, az), (bx, bz) in zip(pts, pts[1:]):
        seg = math.hypot(bx - ax, bz - az)
        if seg < 1e-6:
            continue
        while next_at <= travelled + seg:
            t = (next_at - travelled) / seg
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            yaw = math.degrees(math.atan2(bx - ax, bz - az))
            r.kit.box(material, (x, y, z), (width * r.rng(0.92, 1.04), thickness, plank),
                      geom.yaw_matrix(yaw + r.rng(-jitter, jitter)))
            next_at += plank + gap
        travelled += seg


def door_posts(r, material, height, width=0.9, depth=0.8, cap=None, glow=None, seams=(0.0, LENGTH)):
    """Visible posts framing each 6.8 m doorway so the invisible seam blockers read as a wall line."""
    for z in seams:
        for side in (-1, 1):
            x = side * (DOOR_HALF + width * 0.5 + 0.05)
            r.kit.rock(material, (x, height * 0.5, z + (0.3 if z == 0 else -0.3)), (width, height, depth), r.rng(0, 360),
                       sides=7, rounds=[(-0.5, 0.52), (-0.1, 0.5), (0.25, 0.44), (0.48, 0.3)])
            if cap:
                r.kit.rock(cap, (x, height + 0.05, z + (0.3 if z == 0 else -0.3)), (width * 1.1, 0.3, depth * 1.1), r.rng(0, 360), sides=7)
            if glow:
                r.kit.ring(glow, (x, height * 0.62, z + (0.3 if z == 0 else -0.3)), width * 0.52, width * 0.56, 0.08, 16)


def seam_line(r, place, spacing=1.15, seams=(0.0, LENGTH), x_from=DOOR_HALF + 1.0, x_to=13.2):
    """Call place(x, z_inside) along both seams outside the doorway (each room dresses its own half)."""
    for z in seams:
        inside = z + 0.35 if z == 0 else z - 0.35
        for side in (-1, 1):
            x = x_from
            while x <= x_to:
                place(side * (x + r.rng(-0.2, 0.2)), inside + r.rng(-0.12, 0.12))
                x += spacing * r.rng(0.8, 1.2)


def ramp_block(r, name, x, z0, z1, y0, y1, width, thickness=0.3):
    """Tilted walkable BoxCollider whose top face runs from (z0, y0) to (z1, y1) along +z."""
    length = math.hypot(z1 - z0, y1 - y0)
    angle = math.atan2(y1 - y0, z1 - z0)
    top_mid = np.array([x, (y0 + y1) / 2, (z0 + z1) / 2])
    normal = np.array([0.0, math.cos(angle), -math.sin(angle)])
    center = top_mid - normal * thickness / 2
    # Unity Euler x: positive pitch tips +z down, so a ramp rising along +z needs a negative pitch.
    r.block(name, tuple(center), (width, thickness, length), 0.0, -math.degrees(angle), walkable=True)
    return angle


def plank_ramp(r, material, x, z0, z1, y0, y1, width, plank=0.24, gap=0.06):
    angle = math.atan2(y1 - y0, z1 - z0)
    length = math.hypot(z1 - z0, y1 - y0)
    n = int(length / (plank + gap))
    for k in range(n):
        t = (k + 0.5) / n
        r.kit.box(material, (x, y0 + (y1 - y0) * t - 0.02, z0 + (z1 - z0) * t), (width * r.rng(0.94, 1.02), 0.06, plank),
                  geom.euler_matrix(-math.degrees(angle), r.rng(-2, 2), 0))
    for side in (-1, 1):
        r.kit.box(material, (x + side * width * 0.52, (y0 + y1) / 2 - 0.08, (z0 + z1) / 2), (0.12, 0.14, length), geom.euler_matrix(-math.degrees(angle), 0, 0))
