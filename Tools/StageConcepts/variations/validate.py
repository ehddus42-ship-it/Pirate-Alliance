"""Python port of the traversal part of Assets/StageConcepts/Editor/StageConceptValidation.cs.

Runs on the written room prefabs (shell and prop BoxColliders with their real transforms), so the result predicts
what Unity's "Validate All Concepts" menu reports without opening the editor:

* 0.5 m cells over the 26 x 36.4 m footprint; the floor of a cell is the highest upward-facing hit of a ray cast
  down from MAX_FLOOR + STEP + 0.05 m (rays that start inside a collider do not hit it, as in PhysX);
* a cell is open when the player capsule (radius 0.24, height 1.65, bottom STEP above the floor) overlaps nothing;
* neighbours connect when their floors differ by at most STEP and the capsule can move between them;
* player spawn, both doorway approaches and every enemy spawn must be reached from the spawn.

Extra checks that the Unity validator does not make:
* ground-only reachability (raised walkable decks treated as solid): a room whose exit is reachable that way while
  it relies on a bridge has a leak beside the bridge;
* reached cells whose floor is the top of a non-walkable blocker (the player would walk on an invisible surface);
* the 2.2 m forward walk of StageConceptPlayValidation from each arrival room's spawn.

Usage: python3 validate.py [--room Forest_03] [--json out.json]
"""
import argparse
import collections
import json
import math
import pathlib
import re
import sys

import numpy as np

import prefabdoc as pd

ROOT = pathlib.Path(__file__).resolve().parents[3]
ROOMS = ROOT / 'Assets/StageConcepts/Prefabs/Rooms'
LAYOUTS = ROOT / 'Assets/StageConcepts/Layouts'
CELL, RADIUS, HEIGHT, STEP, MAX_FLOOR = 0.5, 0.24, 1.65, 0.24, 1.2
WIDTH, LENGTH = 26.0, 36.4
SAMPLES = 12  # spheres along the capsule segment (max surface error ~4 mm)
THEMES = ('Forest', 'Digital', 'Ruins', 'Cave')


def parse_vec(text, keys='xyz'):
    values = dict(re.findall(r'(\w+): (-?[0-9.eE+-]+)', text))
    return np.array([float(values[k]) for k in keys])


def quat_matrix(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


class Colliders:
    """Enabled, active, non-trigger BoxColliders of a room prefab as oriented boxes in room space."""

    def __init__(self, prefab, walkable_names):
        transforms = {d.fid: d for d in prefab.docs if d.cls == 4 and not d.stripped}
        objects = {d.fid: d for d in prefab.docs if d.cls == 1}
        local = {}
        for fid, t in transforms.items():
            local[fid] = (parse_vec(t.field('m_LocalPosition')), quat_matrix(parse_vec(t.field('m_LocalRotation'), 'xyzw')),
                          parse_vec(t.field('m_LocalScale')), t.ref('m_Father'), t.ref('m_GameObject'))
        world = {}

        def resolve(fid):
            if fid in world:
                return world[fid]
            p, r, s, father, go = local[fid]
            active = objects[go].field('m_IsActive') == '1'
            name = objects[go].field('m_Name')
            if father and father in local:
                fp, fr, fs, factive, fpath = resolve(father)
                entry = (fp + fr @ (fs * p), fr @ r, fs * s, factive and active, fpath + '/' + name)
            else:
                entry = (p, r, s, active, '')  # the room root itself
            world[fid] = entry
            return entry

        centers, rotations, halves, names, walkable = [], [], [], [], []
        for d in prefab.docs:
            if d.cls != 65:
                continue
            go = objects[d.ref('m_GameObject')]
            if d.field('m_Enabled') != '1' or d.field('m_IsTrigger') == '1':
                continue
            tr = next(t for t in transforms.values() if t.ref('m_GameObject') == go.fid)
            p, r, s, active, path = resolve(tr.fid)
            if not active:
                continue
            name = go.field('m_Name')
            if name in ('EntranceGate', 'ExitGate'):
                continue  # the validator opens both gates (SetGates(false, false) deactivates them)
            size, center = parse_vec(d.field('m_Size')), parse_vec(d.field('m_Center'))
            centers.append(p + r @ (s * center))
            rotations.append(r)
            halves.append(np.abs(s * size) / 2)
            names.append(path.lstrip('/'))
            walkable.append(name in walkable_names)
        self.c = np.array(centers)
        self.r = np.array(rotations)
        self.h = np.array(halves)
        self.names = names
        self.walkable = np.array(walkable)
        self.is_floor = np.array([n.endswith('WalkableFloor') for n in names])

    def floor(self, x, z, solid_walkable=False):
        """Highest upward-facing hit below MAX_FLOOR + STEP + 0.05 (None if nothing within STEP + 0.2 below y=0)."""
        top = MAX_FLOOR + STEP + 0.05
        length = MAX_FLOOR + STEP + 0.2
        origin = np.array([x, top, z])
        o = np.einsum('nji,nj->ni', self.r, origin - self.c)       # R^T (origin - c)
        d = np.einsum('nji,j->ni', self.r, np.array([0.0, -1.0, 0.0]))
        with np.errstate(divide='ignore', invalid='ignore'):
            t1 = (-self.h - o) / d
            t2 = (self.h - o) / d
        tmin = np.where(np.abs(d) < 1e-12, np.where(np.abs(o) <= self.h, -np.inf, np.inf), np.minimum(t1, t2))
        tmax = np.where(np.abs(d) < 1e-12, np.where(np.abs(o) <= self.h, np.inf, -np.inf), np.maximum(t1, t2))
        enter = tmin.max(axis=1)
        leave = tmax.min(axis=1)
        axis = tmin.argmax(axis=1)
        hit = (enter <= leave) & (enter >= 0) & (enter <= length)
        best, source = None, None
        for n in np.nonzero(hit)[0]:
            if solid_walkable and self.walkable[n]:
                continue
            k = axis[n]
            local_normal = np.zeros(3)
            local_normal[k] = -np.sign(d[n, k])
            normal = self.r[n] @ local_normal
            if normal[1] <= 0.7:
                continue
            y = top - enter[n]
            if best is None or y > best:
                best, source = y, n
        return best, source

    def overlaps(self, x, z, floor):
        """Indices of colliders overlapping the player capsule standing on `floor` at (x, z)."""
        bottom = floor + STEP + RADIUS + 0.002
        top = floor + HEIGHT - RADIUS
        points = np.stack([np.full(SAMPLES, x), np.linspace(bottom, top, SAMPLES), np.full(SAMPLES, z)], axis=1)
        rel = points[None, :, :] - self.c[:, None, :]                  # (n, s, 3)
        loc = np.einsum('nji,nsj->nsi', self.r, rel)
        clamped = np.clip(loc, -self.h[:, None, :], self.h[:, None, :])
        dist = np.linalg.norm(loc - clamped, axis=2).min(axis=1)
        return list(np.nonzero(dist < RADIUS)[0])


def check_room(room_id):
    layout = json.loads((LAYOUTS / f'{room_id}.json').read_text(encoding='utf-8'))
    walkable_names = {b['name'] for b in layout['colliders'] if b.get('walkable')}
    prefab = pd.Prefab((ROOMS / f'{room_id}.prefab').read_text(encoding='utf-8'))
    col = Colliders(prefab, walkable_names)
    columns, rows = math.ceil(WIDTH / CELL - 1e-6), math.ceil(LENGTH / CELL - 1e-6)

    def world(node):
        return (-WIDTH / 2 + (node % columns + 0.5) * CELL, min((node // columns + 0.5) * CELL, LENGTH - 0.05))

    def node_of(x, z):
        return (min(max(int(math.floor(z / CELL)), 0), rows - 1) * columns +
                min(max(int(math.floor((x + WIDTH / 2) / CELL)), 0), columns - 1))

    def neighbours(node):
        x, z = node % columns, node // columns
        if x > 0:
            yield node - 1
        if x < columns - 1:
            yield node + 1
        if z > 0:
            yield node - columns
        if z < rows - 1:
            yield node + columns

    def survey(solid_walkable):
        floors, sources, blocked, causes = [], [], [], []
        for node in range(columns * rows):
            x, z = world(node)
            f, src = col.floor(x, z, solid_walkable)
            if f is None:
                floors.append(-math.inf)
                sources.append(None)
                blocked.append(True)
                causes.append(['NO_WALKABLE_FLOOR'])
                continue
            # With solid_walkable the decks are not floors, so the capsule on the ground below overlaps them.
            hits = col.overlaps(x, z, f)
            floors.append(f)
            sources.append(src)
            blocked.append(bool(hits))
            causes.append([col.names[n] for n in hits])
        return floors, sources, blocked, causes

    def flood(floors, blocked, start):
        visited = [False] * len(floors)
        if blocked[start]:
            return visited
        visited[start] = True
        queue = collections.deque([start])
        while queue:
            current = queue.popleft()
            for nxt in neighbours(current):
                if visited[nxt] or blocked[nxt] or abs(floors[current] - floors[nxt]) > STEP:
                    continue
                (ax, az), (bx, bz) = world(current), world(nxt)
                f = max(floors[current], floors[nxt])
                if any(col.overlaps(ax + (bx - ax) * t, az + (bz - az) * t, f) for t in (0.25, 0.5, 0.75)):
                    continue
                visited[nxt] = True
                queue.append(nxt)
        return visited

    floors, sources, blocked, causes = survey(False)
    spawn = layout['playerSpawn']
    start = node_of(spawn['x'], spawn['z'])
    visited = flood(floors, blocked, start)
    targets = [('Player spawn', spawn['x'], spawn['z']), ('Entry approach', 0.0, 0.8), ('Exit approach', 0.0, LENGTH - 0.8)]
    targets += [(f'Enemy_{"ABCDEFGH"[i]}', e['x'], e['z']) for i, e in enumerate(layout['enemySpawns'])]
    errors, warnings = [], []
    for name, x, z in targets:
        f, _ = col.floor(x, z)
        exact = ['NO_WALKABLE_FLOOR'] if f is None else [col.names[n] for n in col.overlaps(x, z, f)]
        node = node_of(x, z)
        if not (visited[node] and not exact):
            reason = 'overlaps geometry or lacks floor' if exact else 'no continuous capsule route from player spawn'
            errors.append(f'{name} ({x:.2f}, {z:.2f}): {reason} {sorted(set(exact + (causes[node] if blocked[node] else [])))[:6]}')
    # Reached cells standing on top of something that is neither the room floor nor a walkable deck or ramp.
    on_blockers = collections.Counter()
    for node, seen in enumerate(visited):
        if seen and sources[node] is not None and not col.is_floor[sources[node]] and not col.walkable[sources[node]]:
            on_blockers[col.names[sources[node]]] += 1
    for name, cells in sorted(on_blockers.items()):
        top = floors_max(floors, visited, sources, col, name)
        if top > STEP:
            errors.append(f'player can stand on {name} ({cells} cells, top {top:.2f} m)')
        else:
            warnings.append(f'low collider used as floor: {name} ({cells} cells, top {top:.2f} m)')
    # Stepping down onto a blocker top from a reached cell (CharacterController falls onto anything lower).
    drops = collections.Counter()
    for node, seen in enumerate(visited):
        if not seen:
            continue
        for nxt in neighbours(node):
            if visited[nxt] or blocked[nxt] or sources[nxt] is None:
                continue
            if floors[nxt] < floors[node] - STEP and floors[nxt] > STEP and not col.walkable[sources[nxt]]:
                (ax, az), (bx, bz) = world(node), world(nxt)
                if not any(col.overlaps(ax + (bx - ax) * t, az + (bz - az) * t, floors[node]) for t in (0.5, 1.0)):
                    drops[col.names[sources[nxt]]] += 1
    for name, cells in sorted(drops.items()):
        errors.append(f'player can drop onto {name} ({cells} edges)')
    result = {'room': room_id, 'colliders': len(col.names), 'walkable_decks': sorted(walkable_names),
              'open_cells': blocked.count(False), 'reached_cells': sum(visited), 'targets': len(targets)}
    if walkable_names:
        # A room that needs its raised walkway must not be crossable at ground level, even through gaps far
        # narrower than the 0.5 m cells: flood the ground on a 0.1 m grid with the decks treated as solid.
        leak = ground_path(col, (spawn['x'], spawn['z']), (0.0, LENGTH - 0.8))
        result['ground_only_exit_reachable'] = leak is not None
        if leak is not None:
            errors.append(f'exit is reachable without the raised walkway, e.g. through ({leak[0]:.2f}, {leak[1]:.2f})')
    if room_id.endswith('_01'):
        # StageConceptPlayValidation walks 2.2 m along +z from the arrival spawn.
        for k in range(1, 11):
            z = spawn['z'] + 0.25 * k
            f, _ = col.floor(spawn['x'], z)
            if f is None or col.overlaps(spawn['x'], z, f):
                errors.append(f'arrival walk blocked {0.25 * k:.2f} m ahead of the spawn')
                break
    result['errors'], result['warnings'] = errors, warnings
    return result


def ground_path(col, start, goal, res=0.1):
    """Fine ground-level flood (floor y = 0, every non-floor collider solid). Returns the narrowest point of a
    path to the goal (the likely gap), or None when the goal cannot be reached."""
    xs = np.arange(-WIDTH / 2 + res / 2, WIDTH / 2, res)
    zs = np.arange(res / 2, LENGTH, res)
    gx, gz = np.meshgrid(xs, zs)
    blocked = np.zeros(gx.shape, bool)
    heights = np.linspace(STEP + RADIUS + 0.002, HEIGHT - RADIUS, SAMPLES)
    for n in range(len(col.names)):
        if col.is_floor[n]:
            continue
        reach = np.abs(col.r[n]) @ col.h[n] + RADIUS          # world AABB half extents plus the capsule radius
        near = (np.abs(gx - col.c[n][0]) <= reach[0]) & (np.abs(gz - col.c[n][2]) <= reach[2])
        if not near.any():
            continue
        px, pz = gx[near], gz[near]
        pts = np.stack([np.repeat(px, SAMPLES), np.tile(heights, len(px)), np.repeat(pz, SAMPLES)], axis=1)
        loc = (pts - col.c[n]) @ col.r[n]
        dist = np.linalg.norm(loc - np.clip(loc, -col.h[n], col.h[n]), axis=1).reshape(len(px), SAMPLES).min(axis=1)
        blocked[near] |= dist < RADIUS
    def cell(x, z):
        return int(np.clip(round((z - zs[0]) / res), 0, len(zs) - 1)), int(np.clip(round((x - xs[0]) / res), 0, len(xs) - 1))
    s, g = cell(*start), cell(*goal)
    if blocked[s]:
        return None
    parent = {s: None}
    queue = collections.deque([s])
    while queue:
        cur = queue.popleft()
        if cur == g:
            break
        i, j = cur
        for nxt in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
            if 0 <= nxt[0] < len(zs) and 0 <= nxt[1] < len(xs) and not blocked[nxt] and nxt not in parent:
                parent[nxt] = cur
                queue.append(nxt)
    if g not in parent:
        return None
    # Report the narrowest spot of the path (fewest open cells around it), which is where the gap is.
    best, node = None, g
    while node is not None:
        i, j = node
        window = blocked[max(0, i - 4):i + 5, max(0, j - 4):j + 5]
        score = int(window.sum())
        if best is None or score > best[0]:
            best = (score, (float(xs[j]), float(zs[i])))
        node = parent[node]
    return best[1]


def floors_max(floors, visited, sources, col, name):
    return max(floors[n] for n, seen in enumerate(visited) if seen and sources[n] is not None and col.names[sources[n]] == name)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--room', action='append')
    ap.add_argument('--json')
    args = ap.parse_args()
    ids = args.room or [f'{t}_{i:02d}' for t in THEMES for i in range(1, 8)]
    results, failed = [], 0
    for room_id in ids:
        r = check_room(room_id)
        results.append(r)
        status = 'FAIL' if r['errors'] else 'ok  '
        failed += bool(r['errors'])
        extra = '' if 'ground_only_exit_reachable' not in r else f"  ground-only exit: {r['ground_only_exit_reachable']}"
        print(f"{status} {room_id:<11} colliders {r['colliders']:>3}  open {r['open_cells']:>4}  reached {r['reached_cells']:>4}{extra}")
        for e in r['errors']:
            print('      error:', e)
        for w in r['warnings']:
            print('      warn: ', w)
    if args.json:
        pathlib.Path(args.json).write_text(json.dumps(results, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'{len(ids) - failed}/{len(ids)} rooms pass the traversal port')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
