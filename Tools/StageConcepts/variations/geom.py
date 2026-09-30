"""Procedural decoration geometry in Unity room space.

Coordinates follow Unity: left-handed, y up, room-local x = -13..13 (west..east), z = 0..36.4 (entry..exit).
A face is front-facing when cross(b - a, c - a) points outward, exactly like StageConceptScenery's C# helpers,
so meshes written from here render identically after Unity's RecalculateNormals/RecalculateTangents.
Planar UVs are metres projected on the dominant axis of the unnormalised face normal (also as in the C# kit).
"""
import math
import random

import numpy as np

TAU = math.tau


def v(x, y, z):
    return np.array((x, y, z), dtype=np.float64)


def yaw_matrix(degrees):
    r = math.radians(degrees)
    c, s = math.cos(r), math.sin(r)
    # Unity Quaternion.Euler(0, yaw, 0): x' = x cos + z sin, z' = -x sin + z cos
    return np.array(((c, 0, s), (0, 1, 0), (-s, 0, c)))


def euler_matrix(pitch, yaw, roll):
    """Unity Quaternion.Euler(x, y, z) as a matrix (rotation order Z, then X, then Y)."""
    x, y, z = (math.radians(a) for a in (pitch, yaw, roll))
    rx = np.array(((1, 0, 0), (0, math.cos(x), -math.sin(x)), (0, math.sin(x), math.cos(x))))
    ry = np.array(((math.cos(y), 0, math.sin(y)), (0, 1, 0), (-math.sin(y), 0, math.cos(y))))
    rz = np.array(((math.cos(z), -math.sin(z), 0), (math.sin(z), math.cos(z), 0), (0, 0, 1)))
    return ry @ rx @ rz


def from_to(a, b):
    """Rotation matrix taking unit vector a onto unit vector b."""
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    c = float(np.dot(a, b))
    if c > 0.999999:
        return np.eye(3)
    if c < -0.999999:
        axis = np.cross(a, (1, 0, 0))
        if np.linalg.norm(axis) < 1e-6:
            axis = np.cross(a, (0, 0, 1))
        axis /= np.linalg.norm(axis)
        return 2 * np.outer(axis, axis) - np.eye(3)
    w = np.cross(a, b)
    k = np.array(((0, -w[2], w[1]), (w[2], 0, -w[0]), (-w[1], w[0], 0)))
    return np.eye(3) + k + k @ k / (1 + c)


class Geometry:
    """Unshared-vertex triangle soup for one material batch."""

    def __init__(self):
        self.positions = []
        self.uvs = []
        self.triangles = []

    def __len__(self):
        return len(self.triangles) // 3

    @staticmethod
    def project_uv(p, normal):
        x, y, z = abs(normal[0]), abs(normal[1]), abs(normal[2])
        if y >= x and y >= z:
            return (p[0], p[2])
        if x >= z:
            return (p[2], p[1])
        return (p[0], p[1])

    def tri(self, a, b, c):
        normal = np.cross(b - a, c - a)
        base = len(self.positions)
        for p in (a, b, c):
            self.positions.append(tuple(float(q) for q in p))
            self.uvs.append(self.project_uv(p, normal))
        self.triangles.extend((base, base + 1, base + 2))

    def quad(self, a, b, c, d):
        normal = np.cross(b - a, c - a)
        base = len(self.positions)
        for p in (a, b, c, d):
            self.positions.append(tuple(float(q) for q in p))
            self.uvs.append(self.project_uv(p, normal))
        self.triangles.extend((base, base + 1, base + 2, base, base + 2, base + 3))

    def extend(self, other):
        base = len(self.positions)
        self.positions.extend(other.positions)
        self.uvs.extend(other.uvs)
        self.triangles.extend(i + base for i in other.triangles)

    def arrays(self):
        """Positions, normals, tangents and UVs as computed by Unity's Recalculate* on this vertex layout."""
        p = np.array(self.positions, dtype=np.float64).reshape(-1, 3)
        uv = np.array(self.uvs, dtype=np.float64).reshape(-1, 2)
        t = np.array(self.triangles, dtype=np.int64).reshape(-1, 3)
        n = np.zeros_like(p)
        tan = np.zeros_like(p)
        bit = np.zeros_like(p)
        if len(t):
            a, b, c = p[t[:, 0]], p[t[:, 1]], p[t[:, 2]]
            face = np.cross(b - a, c - a)
            for k in range(3):
                np.add.at(n, t[:, k], face)
            e1, e2 = b - a, c - a
            du1 = uv[t[:, 1], 0] - uv[t[:, 0], 0]
            dv1 = uv[t[:, 1], 1] - uv[t[:, 0], 1]
            du2 = uv[t[:, 2], 0] - uv[t[:, 0], 0]
            dv2 = uv[t[:, 2], 1] - uv[t[:, 0], 1]
            det = du1 * dv2 - du2 * dv1
            r = np.where(np.abs(det) > 1e-12, 1.0 / np.where(np.abs(det) > 1e-12, det, 1.0), 0.0)[:, None]
            sdir = (e1 * dv2[:, None] - e2 * dv1[:, None]) * r
            tdir = (e2 * du1[:, None] - e1 * du2[:, None]) * r
            for k in range(3):
                np.add.at(tan, t[:, k], sdir)
                np.add.at(bit, t[:, k], tdir)
        length = np.linalg.norm(n, axis=1, keepdims=True)
        n = np.where(length > 1e-12, n / np.where(length > 1e-12, length, 1), (0, 1, 0))
        tan = tan - n * np.sum(n * tan, axis=1, keepdims=True)
        tl = np.linalg.norm(tan, axis=1, keepdims=True)
        fallback = np.cross(n, (0, 0, 1))
        fallback = np.where(np.linalg.norm(fallback, axis=1, keepdims=True) > 1e-6, fallback, np.cross(n, (1, 0, 0)))
        tan = np.where(tl > 1e-12, tan / np.where(tl > 1e-12, tl, 1), fallback / np.linalg.norm(fallback, axis=1, keepdims=True))
        w = np.where(np.sum(np.cross(n, tan) * bit, axis=1) < 0, -1.0, 1.0)
        tangents = np.concatenate([tan, w[:, None]], axis=1)
        return p, n, tangents, uv, t

    def bounds(self):
        p = np.array(self.positions).reshape(-1, 3)
        return p.min(axis=0), p.max(axis=0)


class Kit:
    """Material-batched builder; one Geometry per material name. Mirrors StageConceptScenery.Context."""

    def __init__(self, seed):
        self.batches = {}
        self.random = random.Random(seed)

    def g(self, material):
        return self.batches.setdefault(material, Geometry())

    def rng(self, lo, hi):
        return lo + self.random.random() * (hi - lo)

    def triangle_count(self):
        return sum(len(g) for g in self.batches.values())

    # ---- primitives ------------------------------------------------------------------------------
    def box(self, m, center, size, rot=None):
        g = self.g(m)
        q = np.eye(3) if rot is None else rot
        h = np.asarray(size, dtype=float) * 0.5
        c = np.asarray(center, dtype=float)
        corners = [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1), (-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)]
        p = [c + q @ (h * np.array(k)) for k in corners]
        g.quad(p[0], p[1], p[2], p[3])
        g.quad(p[4], p[7], p[6], p[5])
        g.quad(p[0], p[4], p[5], p[1])
        g.quad(p[1], p[5], p[6], p[2])
        g.quad(p[2], p[6], p[7], p[3])
        g.quad(p[3], p[7], p[4], p[0])

    def cone(self, m, center, r0, r1, height, segments, rot=None, caps=True, phase=0.0):
        g = self.g(m)
        q = np.eye(3) if rot is None else rot
        c = np.asarray(center, dtype=float)
        low = c + q @ v(0, -height * 0.5, 0)
        high = c + q @ v(0, height * 0.5, 0)
        for i in range(segments):
            a = (i + phase) * TAU / segments
            b = (i + 1 + phase) * TAU / segments
            p0 = c + q @ v(math.sin(a) * r0, -height * 0.5, math.cos(a) * r0)
            p1 = c + q @ v(math.sin(b) * r0, -height * 0.5, math.cos(b) * r0)
            p2 = c + q @ v(math.sin(b) * r1, height * 0.5, math.cos(b) * r1)
            p3 = c + q @ v(math.sin(a) * r1, height * 0.5, math.cos(a) * r1)
            if r0 > 0 and r1 > 0:
                g.quad(p0, p1, p2, p3)
            elif r0 > 0:
                g.tri(p0, p1, p2)
            elif r1 > 0:
                g.tri(p0, p2, p3)
            if caps and r0 > 0:
                g.tri(low, p1, p0)
            if caps and r1 > 0:
                g.tri(high, p3, p2)

    def disc(self, m, center, radius, height, segments):
        self.cone(m, center, radius, radius, height, segments)

    def ring(self, m, center, r_in, r_out, thickness, segments, rot=None):
        g = self.g(m)
        q = np.eye(3) if rot is None else rot
        c = np.asarray(center, dtype=float)
        down = v(0, thickness, 0)
        for i in range(segments):
            a = i * TAU / segments
            b = (i + 1) * TAU / segments
            p0 = v(math.sin(a) * r_in, thickness * 0.5, math.cos(a) * r_in)
            p1 = v(math.sin(a) * r_out, thickness * 0.5, math.cos(a) * r_out)
            p2 = v(math.sin(b) * r_out, thickness * 0.5, math.cos(b) * r_out)
            p3 = v(math.sin(b) * r_in, thickness * 0.5, math.cos(b) * r_in)
            g.quad(c + q @ p0, c + q @ p1, c + q @ p2, c + q @ p3)
            g.quad(c + q @ p1, c + q @ (p1 - down), c + q @ (p2 - down), c + q @ p2)
            g.quad(c + q @ p3, c + q @ (p3 - down), c + q @ (p0 - down), c + q @ p0)

    def rock(self, m, center, size, yaw, sides=8, rounds=None, flat_top=False, squash=0.0):
        """Irregular faceted boulder. Four rings give a softer silhouette than the original three."""
        g = self.g(m)
        q = yaw_matrix(yaw)
        c = np.asarray(center, dtype=float)
        s = np.asarray(size, dtype=float)
        rings = rounds or [(-0.46, 0.36), (-0.12, 0.50), (0.18, 0.44), (0.40, 0.26)]
        jitter = [self.rng(0.80, 1.06) for _ in range(sides)]
        pts = []
        for level, (hy, radius) in enumerate(rings):
            row = []
            twist = level * 0.21
            for i in range(sides):
                a = (i + 0.15 + twist) * TAU / sides
                f = jitter[(i + level) % sides] * self.rng(0.94, 1.04)
                y = hy + self.rng(-0.035, 0.05)
                if flat_top and level == len(rings) - 1:
                    y = hy
                row.append(c + q @ (v(math.sin(a) * radius * f, y * (1 - squash), math.cos(a) * radius * f) * s))
            pts.append(row)
        top_y = (0.49 if not flat_top else rings[-1][0]) * (1 - squash)
        top = c + q @ (v(self.rng(-0.04, 0.04), top_y, self.rng(-0.04, 0.04)) * s)
        for level in range(len(rings) - 1):
            lo, hi = pts[level], pts[level + 1]
            for i in range(sides):
                j = (i + 1) % sides
                g.quad(lo[i], lo[j], hi[j], hi[i])
        last = pts[-1]
        for i in range(sides):
            g.tri(last[i], last[(i + 1) % sides], top)

    def slab(self, m, center, size, yaw, tilt=(0.0, 0.0), bevel=0.12):
        """Broken slab / flagstone: an irregular prism with a bevelled top."""
        g = self.g(m)
        rot = yaw_matrix(yaw) @ euler_matrix(tilt[0], 0, tilt[1])
        c = np.asarray(center, dtype=float)
        sx, sy, sz = size
        sides = 7
        outline = []
        for i in range(sides):
            a = (i + self.rng(-0.25, 0.25)) * TAU / sides
            r = self.rng(0.78, 1.0)
            outline.append((math.sin(a) * 0.5 * sx * r, math.cos(a) * 0.5 * sz * r))
        bottom = [c + rot @ v(x, -sy * 0.5, z) for x, z in outline]
        rim = [c + rot @ v(x, sy * 0.5 - sy * bevel, z) for x, z in outline]
        top = [c + rot @ v(x * (1 - bevel), sy * 0.5, z * (1 - bevel)) for x, z in outline]
        mid = c + rot @ v(0, sy * 0.5, 0)
        for i in range(sides):
            j = (i + 1) % sides
            g.quad(bottom[i], bottom[j], rim[j], rim[i])
            g.quad(rim[i], rim[j], top[j], top[i])
            g.tri(top[i], top[j], mid)

    def patch(self, m, center, radius_x, radius_z, height, yaw=0.0, segments=11, dome=0.35, wobble=0.22):
        """Low organic ground patch (moss, puddle, ash) with a softly domed top and a thin skirt."""
        g = self.g(m)
        q = yaw_matrix(yaw)
        c = np.asarray(center, dtype=float)
        offsets = [1 + self.rng(-wobble, wobble) for _ in range(segments)]
        outer, inner = [], []
        for i in range(segments):
            a = i * TAU / segments
            f = (offsets[i] + offsets[(i + 1) % segments] + offsets[i - 1]) / 3
            outer.append(c + q @ v(math.sin(a) * radius_x * f, -height * 0.5, math.cos(a) * radius_z * f))
            inner.append(c + q @ v(math.sin(a) * radius_x * f * 0.72, height * 0.5, math.cos(a) * radius_z * f * 0.72))
        top = c + v(0, height * (0.5 + dome), 0)
        for i in range(segments):
            j = (i + 1) % segments
            g.quad(outer[i], outer[j], inner[j], inner[i])
            g.tri(inner[i], inner[j], top)

    def tube(self, m, points, radius, sides, taper=0.85, end_radius=None):
        pts = [np.asarray(p, dtype=float) for p in points]
        count = len(pts) - 1
        for i in range(count):
            delta = pts[i + 1] - pts[i]
            length = float(np.linalg.norm(delta))
            if length < 1e-5:
                continue
            r0 = radius if end_radius is None else radius + (end_radius - radius) * i / count
            r1 = r0 * taper if end_radius is None else radius + (end_radius - radius) * (i + 1) / count
            self.cone(m, (pts[i + 1] + pts[i]) * 0.5, r0, r1, length, sides, from_to(v(0, 1, 0), delta / length))

    def grass(self, m, center, height, yaw, blades=7, spread=0.58):
        g = self.g(m)
        q = yaw_matrix(yaw)
        c = np.asarray(center, dtype=float)
        for i in range(blades):
            a = i * TAU / blades + self.rng(-0.2, 0.2)
            radial = v(math.sin(a), 0, math.cos(a))
            across = v(radial[2], 0, -radial[0]) * height * 0.10
            foot = radial * height * 0.20
            tip = radial * height * spread + v(0, height * (0.7 + (i % 3) * 0.2), 0)
            p0, p1, p2 = c + q @ (foot - across), c + q @ (foot + across), c + q @ tip
            g.tri(p0, p1, p2)
            g.tri(p2, p1, p0)

    def octahedron(self, m, center, size):
        g = self.g(m)
        c = np.asarray(center, dtype=float)
        ring = [c + v(size[0], 0, 0), c + v(0, 0, size[2]), c + v(-size[0], 0, 0), c + v(0, 0, -size[2])]
        for i in range(4):
            g.tri(c + v(0, size[1], 0), ring[(i + 1) % 4], ring[i])
            g.tri(c - v(0, size[1], 0), ring[i], ring[(i + 1) % 4])

    def crystal(self, m, base, height, radius, tilt=(0.0, 0.0), yaw=0.0, sides=6, tip=0.28):
        """Hexagonal crystal prism with a pointed termination, growing from base along its tilted axis."""
        rot = yaw_matrix(yaw) @ euler_matrix(tilt[0], 0, tilt[1])
        g = self.g(m)
        b = np.asarray(base, dtype=float)
        body = height * (1 - tip)
        low = [b + rot @ v(math.sin(i * TAU / sides) * radius, 0, math.cos(i * TAU / sides) * radius) for i in range(sides)]
        high = [b + rot @ v(math.sin(i * TAU / sides) * radius * 0.92, body, math.cos(i * TAU / sides) * radius * 0.92) for i in range(sides)]
        point = b + rot @ v(0, height, 0)
        for i in range(sides):
            j = (i + 1) % sides
            g.quad(low[i], low[j], high[j], high[i])
            g.tri(high[i], high[j], point)

    def wire_cube(self, m, center, half, width, rot):
        c = np.asarray(center, dtype=float)
        for a in (-1, 1):
            for b in (-1, 1):
                self.box(m, c + rot @ v(a * half, b * half, 0), (width, width, half * 2), rot)
                self.box(m, c + rot @ v(a * half, 0, b * half), (width, half * 2, width), rot)
                self.box(m, c + rot @ v(0, a * half, b * half), (half * 2, width, width), rot)

    def ribbon(self, m, points, width, y, thickness=0.03, jitter=0.0, taper_ends=0.0):
        """Flat strip along a polyline (paths, streams, road paint). Edges can be irregular."""
        g = self.g(m)
        pts = [np.asarray((p[0], y, p[1]), dtype=float) for p in points]
        left, right = [], []
        for i, p in enumerate(pts):
            prev_p = pts[max(0, i - 1)]
            next_p = pts[min(len(pts) - 1, i + 1)]
            d = next_p - prev_p
            d[1] = 0
            d /= max(1e-6, np.linalg.norm(d))
            side = v(d[2], 0, -d[0])
            w = width * 0.5
            if taper_ends and (i == 0 or i == len(pts) - 1):
                w *= taper_ends
            wl = w * (1 + self.rng(-jitter, jitter))
            wr = w * (1 + self.rng(-jitter, jitter))
            left.append(p - side * wl)
            right.append(p + side * wr)
        up = v(0, thickness, 0)
        for i in range(len(pts) - 1):
            g.quad(left[i] + up, left[i + 1] + up, right[i + 1] + up, right[i] + up)
            g.quad(right[i] + up, right[i + 1] + up, right[i + 1], right[i])
            g.quad(left[i + 1] + up, left[i] + up, left[i], left[i + 1])

    def heightfield(self, m, x0, x1, z0, z1, nx, nz, height, skirt=None):
        """Regular terrain grid; height(x, z) -> y. Used for banks, cliffs and out-of-bounds backdrops."""
        g = self.g(m)
        xs = np.linspace(x0, x1, nx + 1)
        zs = np.linspace(z0, z1, nz + 1)
        grid = [[v(x, height(x, z), z) for x in xs] for z in zs]
        for j in range(nz):
            for i in range(nx):
                a, b, c, d = grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1]
                # Split along the shorter diagonal for a cleaner silhouette.
                if np.linalg.norm(a - c) <= np.linalg.norm(b - d):
                    g.tri(a, b, c)
                    g.tri(a, c, d)
                else:
                    g.tri(a, b, d)
                    g.tri(b, c, d)
        if skirt is not None:
            for i in range(nx):
                for row, flip in ((grid[0], False), (grid[-1], True)):
                    p, r = row[i], row[i + 1]
                    lp, lr = v(p[0], skirt, p[2]), v(r[0], skirt, r[2])
                    if flip:
                        g.quad(p, lp, lr, r)
                    else:
                        g.quad(r, lr, lp, p)
            for j in range(nz):
                for col, flip in ((0, True), (-1, False)):
                    p, r = grid[j][col], grid[j + 1][col]
                    lp, lr = v(p[0], skirt, p[2]), v(r[0], skirt, r[2])
                    if flip:
                        g.quad(p, lp, lr, r)
                    else:
                        g.quad(r, lr, lp, p)


def smooth_noise(seed):
    """Tileless value noise with smooth interpolation; returns f(x, z) in [0, 1]."""
    rnd = random.Random(seed)
    table = [rnd.random() for _ in range(512)]
    perm = list(range(256))
    rnd.shuffle(perm)
    perm += perm

    def lattice(ix, iz):
        return table[perm[(perm[ix & 255] + iz) & 255]]

    def f(x, z):
        ix, iz = math.floor(x), math.floor(z)
        fx, fz = x - ix, z - iz
        ux, uz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
        a, b = lattice(ix, iz), lattice(ix + 1, iz)
        c, d = lattice(ix, iz + 1), lattice(ix + 1, iz + 1)
        return (a + (b - a) * ux) * (1 - uz) + (c + (d - c) * ux) * uz

    return f


def fbm(seed, octaves=4):
    layers = [smooth_noise(seed + k * 101) for k in range(octaves)]

    def f(x, z):
        total, amp, norm, freq = 0.0, 1.0, 0.0, 1.0
        for layer in layers:
            total += layer(x * freq, z * freq) * amp
            norm += amp
            amp *= 0.5
            freq *= 2.03
        return total / norm

    return f
