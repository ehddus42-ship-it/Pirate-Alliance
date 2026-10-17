"""Room description shared by the recipes, the Unity writers and the preview renderer.

Room-local axes: x = -13..13 (west..east), z = 0..36.4 (entry..exit), y up. Doors are 6.8 m wide at x = 0.
The quarter-view camera looks north-east (yaw 35, pitch 55): the east side and the far end are backdrop,
the west side is foreground (see Documentation/StageConcepts/VariationPlan.md, section 2).
"""
import dataclasses
import hashlib
import json
import math
import pathlib
import struct
import uuid

import numpy as np

import geom

ROOT = pathlib.Path(__file__).resolve().parents[3]
MESHY = ROOT / 'Assets/StageConcepts/Art/Meshy'
WIDTH, LENGTH = 26.0, 36.4
DOOR_HALF = 3.4
CAPSULE_RADIUS = 0.24


def asset_guid(key):
    """Stable GUID for a Meshy GLB .meta: the existing .meta wins, new kit assets get a name-derived GUID."""
    meta = MESHY / key / (key + '.glb.meta')
    if meta.exists():
        for line in meta.read_text(encoding='utf-8').splitlines():
            if line.startswith('guid: '):
                return line[6:].strip()
    return uuid.uuid5(uuid.NAMESPACE_URL, 'pirate-alliance/stage-concepts/meshy/' + key).hex


_bounds_cache = {}


def glb_unity_bounds(key):
    """Unity-space bounds of a Meshy GLB (glTFast maps glTF x to -x) or None if the model is not in the repo."""
    if key in _bounds_cache:
        return _bounds_cache[key]
    path = MESHY / key / (key + '.glb')
    result = None
    if path.exists() and path.stat().st_size > 1024:
        blob = path.read_bytes()
        if blob[:4] == b'glTF':
            json_length = struct.unpack_from('<I', blob, 12)[0]
            gltf = json.loads(blob[20:20 + json_length])
            low, high = [math.inf] * 3, [-math.inf] * 3
            for mesh in gltf['meshes']:
                for primitive in mesh['primitives']:
                    accessor = gltf['accessors'][primitive['attributes']['POSITION']]
                    for i in range(3):
                        low[i] = min(low[i], accessor['min'][i])
                        high[i] = max(high[i], accessor['max'][i])
            triangles = sum(gltf['accessors'][p['indices']]['count'] // 3 for m in gltf['meshes'] for p in m['primitives'])
            result = {'min': np.array([-high[0], low[1], low[2]]), 'max': np.array([-low[0], high[1], high[2]]),
                      'triangles': triangles}
    _bounds_cache[key] = result
    return result


@dataclasses.dataclass
class Placement:
    key: str
    position: tuple
    target: tuple
    yaw: float
    collider: object            # None, 'auto', or dict(size=(w, h, d), center=(0, y, 0)) in slot space
    model_yaw: float = 0.0      # extra rotation inside the slot that aligns the model's long axis
    scale: float = 1.0
    offset: tuple = (0, 0, 0)   # model root position inside the slot (after model_yaw)
    actual: tuple = None        # scaled model size in slot axes
    triangles: int = 0
    guid: str = ''


@dataclasses.dataclass
class Box:
    name: str
    center: tuple
    size: tuple
    yaw: float = 0.0
    pitch: float = 0.0          # rotation about the local x axis (ramps)
    walkable: bool = False      # ramps and decks: floor, not an obstacle


@dataclasses.dataclass
class Light:
    name: str
    position: tuple
    color: str
    intensity: float
    range: float


class Room:
    def __init__(self, theme, index, key, title, kind, seed, notes=''):
        self.theme, self.index, self.key, self.title, self.kind = theme, index, key, title, kind
        self.kit = geom.Kit(seed)
        self.models = []
        self.boxes = []
        self.lights = []
        self.clear = []            # (kind, params) zones that scatter must avoid
        self.player_spawn = (0.0, 0.05, 4.0)
        self.enemies = []
        self.notes = notes
        self.missing = set()
        self.fallbacks = []

    @property
    def rng(self):
        return self.kit.rng

    def rng_choice(self, options):
        return options[int(self.kit.rng(0, len(options) - 1e-9))]

    def rng_int(self, lo, hi):
        return int(self.kit.rng(lo, hi + 1 - 1e-9))

    # ---- authoring API -------------------------------------------------------------------------
    def model(self, key, position, size, yaw=0.0, collider='auto', long_axis=None, fallback=None, fit='min'):
        """Place a Meshy model scaled to fit `size` (w, h, d in slot axes), like StageConceptScenery.Model."""
        bounds = glb_unity_bounds(key)
        if bounds is None:
            self.missing.add(key)
            if fallback:
                fallback(self, position, size, yaw)
                self.fallbacks.append(key)
            return None
        extent = bounds['max'] - bounds['min']
        model_yaw = 0.0
        if long_axis in ('x', 'z'):
            longest = 'x' if extent[0] >= extent[2] else 'z'
            if longest != long_axis:
                model_yaw = 90.0
        if model_yaw:
            extent = np.array([extent[2], extent[1], extent[0]])
        ratios = [size[i] / max(extent[i], 1e-3) for i in range(3)]
        scale = min(ratios) if fit == 'min' else ratios['xyz'.index(fit)]
        center = (bounds['max'] + bounds['min']) * 0.5
        # Model root offset inside the slot: centre x/z, rest the lowest point on y = 0 (after model_yaw).
        rot = geom.yaw_matrix(model_yaw)
        c = rot @ center
        offset = (-c[0] * scale, -bounds['min'][1] * scale, -c[2] * scale)
        placement = Placement(key, tuple(position), tuple(size), yaw, collider, model_yaw, scale, offset,
                              tuple(extent * scale), bounds['triangles'], asset_guid(key))
        self.models.append(placement)
        return placement

    def block(self, name, center, size, yaw=0.0, pitch=0.0, walkable=False):
        self.boxes.append(Box(name, tuple(center), tuple(size), yaw, pitch, walkable))

    def light(self, name, position, color, intensity, light_range):
        self.lights.append(Light(name, tuple(position), color, intensity, light_range))

    def keep_clear_path(self, points, width):
        self.clear.append(('path', ([tuple(p) for p in points], width)))

    def keep_clear_circle(self, center, radius):
        self.clear.append(('circle', (tuple(center), radius)))

    def keep_clear_polygon(self, outline, margin=0.0):
        self.clear.append(('poly', ([tuple(p) for p in outline], margin)))

    def is_clear(self, x, z, margin=0.0):
        for kind, params in self.clear:
            if kind == 'circle':
                (cx, cz), r = params
                if (x - cx) ** 2 + (z - cz) ** 2 < (r + margin) ** 2:
                    return False
            elif kind == 'poly':
                outline, pad = params
                if point_in_polygon(x, z, outline) or min(segment_distance(x, z, *a, *b) for a, b in zip(outline, outline[1:] + outline[:1])) < pad + margin:
                    return False
            else:
                points, width = params
                for (ax, az), (bx, bz) in zip(points, points[1:]):
                    if segment_distance(x, z, ax, az, bx, bz) < width * 0.5 + margin:
                        return False
        return True

    # ---- derived data --------------------------------------------------------------------------
    def colliders(self):
        """All solid footprints in room space: explicit blocks plus model footprints (yawed boxes)."""
        out = list(self.boxes)
        for i, m in enumerate(self.models):
            if m.collider is None:
                continue
            if m.collider == 'auto':
                w, h, d = m.actual
                size = (w * 0.66, min(h, 3.0), d * 0.66)
                center = (0, size[1] * 0.5, 0)
            else:
                size = m.collider['size']
                center = m.collider.get('center', (0, size[1] * 0.5, 0))
            rot = geom.yaw_matrix(m.yaw)
            world = np.asarray(m.position) + rot @ np.asarray(center)
            out.append(Box(f'Landmark footprint__{m.key}', tuple(world), tuple(size), m.yaw))
        return out

    def triangle_budget(self):
        decor = self.kit.triangle_count()
        models = sum(m.triangles for m in self.models)
        return decor, models


def point_in_polygon(x, z, outline):
    inside = False
    n = len(outline)
    for i in range(n):
        (ax, az), (bx, bz) = outline[i], outline[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (bx - ax) * (z - az) / (bz - az):
            inside = not inside
    return inside


def segment_distance(px, pz, ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    length = dx * dx + dz * dz
    t = 0.0 if length == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / length))
    qx, qz = ax + t * dx, az + t * dz
    return math.hypot(px - qx, pz - qz)


# ---- traversal (mirrors StageConceptValidation's capsule test on a 0.25 m grid) -------------------
SHELL_BOXES = [
    Box('WestBoundary', (-13.3, 2, 18.2), (0.6, 4, 36.4)),
    Box('EastBoundary', (13.3, 2, 18.2), (0.6, 4, 36.4)),
    Box('DoorwayBoundary', (-8.2, 1.5, 0), (9.6, 3, 0.35)), Box('DoorwayBoundary', (8.2, 1.5, 0), (9.6, 3, 0.35)),
    Box('DoorwayBoundary', (-8.2, 1.5, 36.4), (9.6, 3, 0.35)), Box('DoorwayBoundary', (8.2, 1.5, 36.4), (9.6, 3, 0.35)),
]


def occupancy(room, cell=0.25, inflate=CAPSULE_RADIUS + 0.04, max_step=0.24):
    xs = np.arange(-13.0 + cell / 2, 13.0, cell)
    zs = np.arange(-1.0 + cell / 2, LENGTH + 1.0, cell)
    gx, gz = np.meshgrid(xs, zs)
    blocked = np.zeros(gx.shape, dtype=bool)
    for box in SHELL_BOXES + room.colliders():
        if box.walkable:
            continue
        bottom = box.center[1] - box.size[1] / 2
        top = box.center[1] + box.size[1] / 2
        if top <= max_step or bottom >= 1.65:
            continue  # below the step offset (walkable) or above head height
        r = math.radians(box.yaw)
        c, s = math.cos(r), math.sin(r)
        lx = (gx - box.center[0]) * c - (gz - box.center[2]) * s
        lz = (gx - box.center[0]) * s + (gz - box.center[2]) * c
        blocked |= (np.abs(lx) <= box.size[0] / 2 + inflate) & (np.abs(lz) <= box.size[2] / 2 + inflate)
    return xs, zs, blocked


def reachable(room):
    """Flood fill from the player spawn; report which required targets can be reached."""
    xs, zs, blocked = occupancy(room)
    def cell_of(x, z):
        return int(round((z - zs[0]) / (zs[1] - zs[0]))), int(round((x - xs[0]) / (xs[1] - xs[0])))
    start = cell_of(room.player_spawn[0], room.player_spawn[2])
    seen = np.zeros_like(blocked)
    if blocked[start]:
        return {'spawn_blocked': True}
    stack = [start]
    seen[start] = True
    while stack:
        i, j = stack.pop()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            a, b = i + di, j + dj
            if 0 <= a < blocked.shape[0] and 0 <= b < blocked.shape[1] and not blocked[a, b] and not seen[a, b]:
                seen[a, b] = True
                stack.append((a, b))
    targets = {'entry': (0.0, 0.6), 'exit': (0.0, LENGTH - 0.6)}
    for n, e in enumerate(room.enemies):
        targets[f'enemy_{n}'] = (e[0], e[2])
    result = {}
    for name, (x, z) in targets.items():
        idx = cell_of(x, z)
        result[name] = bool(seen[idx]) and not blocked[idx]
    walkable = (~blocked).sum() * 0.0625
    result['walkable_m2'] = round(float(walkable), 1)
    result['reached_m2'] = round(float(seen.sum() * 0.0625), 1)
    return result


def stable_seed(*parts):
    return int(hashlib.sha256('/'.join(map(str, parts)).encode()).hexdigest()[:12], 16)
