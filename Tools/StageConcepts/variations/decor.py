"""Decor pass: sprinkle the 2026-10 Meshy dressing props over every variation room.

Each room gets three of its theme's five decor props, a different rotation per room so shuffled routes stop showing
the same set dressing twice in a row. Placement is searched, not hand-placed, and every candidate must respect the
room as authored:

- stays inside the walkable shell, away from both door seams (z 3..LENGTH-3);
- keeps clear of the recipe's keep-clear paths and circles, existing Meshy footprints, solid and walkable boxes
  (water blockers, ramps, bridges), the player spawn and every enemy spawn;
- follows the camera rule of VariationPlan.md section 2: props taller than 2.5 m only on the east backdrop side
  (x > 6.5) or at the far end (z > 30); the west foreground only gets low props;
- the room's flood fill must still reach both doors and all enemy spawns, and the reachable area may only lose
  about the prop's own footprint (no pocket of floor sealed off).

The search is seeded from the room key, so builds are deterministic.
"""
import math
import random

import room as rm
from room import LENGTH

# key: (size w, h, d in metres, long horizontal axis or None)
DECOR = {
    'Forest': [('hollow_stump', (2.6, 2.0, 2.6), None), ('fairy_ring_stones', (3.4, 1.2, 3.4), None),
               ('druid_totem', (1.1, 3.2, 1.1), None), ('overgrown_well', (2.2, 2.7, 2.2), None),
               ('woodcutter_cart', (3.4, 1.8, 1.8), 'x')],
    'Digital': [('cable_junction', (4.0, 0.9, 1.8), 'x'), ('holo_pedestal', (1.4, 1.9, 1.4), None),
                ('quarantine_crates', (2.4, 1.7, 1.8), 'x'), ('drone_dock', (1.9, 1.6, 1.9), None),
                ('glitch_cube_pile', (2.6, 1.5, 2.4), 'x')],
    'Ruins': [('overturned_dumpster', (2.8, 1.5, 1.8), 'x'), ('newsstand_ruin', (2.4, 2.6, 1.8), 'x'),
              ('shopping_cart_pile', (2.4, 1.3, 1.7), 'x'), ('fallen_water_tank', (3.8, 2.8, 3.0), 'x'),
              ('tire_barricade', (3.0, 1.4, 1.6), 'x')],
    'Cave': [('glow_coral', (2.0, 1.9, 2.0), None), ('ore_vein_boulder', (2.4, 1.6, 2.0), 'x'),
             ('miners_campfire', (2.6, 1.0, 2.6), None), ('drill_rig', (2.6, 2.6, 2.0), 'x'),
             ('fossil_ribcage', (4.6, 2.8, 2.4), 'x')],
}
PER_ROOM = 3
TALL = 2.5
TRIES = 260


def _radius(w, d):
    return 0.5 * math.hypot(w, d)


def _model_discs(r):
    """(x, z, radius) discs for the Meshy models already in the room."""
    return [(m.position[0], m.position[2], 0.42 * max(m.actual[0], m.actual[2])) for m in r.models]


def _hits_box(r, x, z, pad):
    """True when (x, z) padded by `pad` overlaps any authored box (solid, water blocker or walkable deck)."""
    for b in r.boxes:
        a = math.radians(b.yaw)
        c, s = math.cos(a), math.sin(a)
        lx = (x - b.center[0]) * c - (z - b.center[2]) * s
        lz = (x - b.center[0]) * s + (z - b.center[2]) * c
        if abs(lx) <= b.size[0] / 2 + pad and abs(lz) <= b.size[2] / 2 + pad:
            return True
    return False


def _ok_reach(result, before):
    if result.get('spawn_blocked'):
        return False
    return all(v for k, v in result.items() if isinstance(v, bool))


def choose(theme, index):
    """Three of the five props, rotated per room index so neighbours differ."""
    kit = DECOR[theme]
    start = (index * 2) % len(kit)
    return [kit[(start + j) % len(kit)] for j in range(PER_ROOM)]


def dress(theme, r):
    """Place the room's decor props; returns the keys placed (missing models are skipped silently)."""
    if theme not in DECOR:
        return []
    rng = random.Random(rm.stable_seed(r.key, 'decor-2026-10'))
    placed = []
    before = rm.reachable(r)
    for key, size, long_axis in choose(theme, r.index):
        if rm.glb_unity_bounds(key) is None:
            continue
        scale = rng.uniform(0.9, 1.1)
        target = (size[0] * scale, size[1] * scale, size[2] * scale)
        tall = target[1] > TALL
        for _ in range(TRIES):
            yaw = rng.uniform(0, 360)
            x = rng.uniform(-11.0, 11.0)
            z = rng.uniform(3.0, LENGTH - 3.0)
            if tall and not (x > 6.5 or z > 30.0):
                continue
            rad = _radius(target[0], target[2])
            if abs(x) + rad > 12.6:
                continue
            if not r.is_clear(x, z, rad * 0.5):
                continue
            if any((x - px) ** 2 + (z - pz) ** 2 < (rad * 0.8 + pr) ** 2 for px, pz, pr in _model_discs(r)):
                continue
            if _hits_box(r, x, z, rad * 0.6):
                continue
            spawn = r.player_spawn
            if (x - spawn[0]) ** 2 + (z - spawn[2]) ** 2 < (rad + 2.5) ** 2:
                continue
            if any((x - e[0]) ** 2 + (z - e[2]) ** 2 < (rad + 1.6) ** 2 for e in r.enemies):
                continue
            p = r.model(key, (x, 0, z), target, yaw, collider='auto', long_axis=long_axis)
            after = rm.reachable(r)
            lost = before.get('reached_m2', 0) - after.get('reached_m2', 0)
            footprint = (target[0] * 0.66 + 0.56) * (target[2] * 0.66 + 0.56)
            if p is not None and _ok_reach(after, before) and lost <= footprint + 2.0:
                placed.append(key)
                before = after
                break
            r.models.remove(p)
    return placed
