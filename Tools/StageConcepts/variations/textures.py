"""Seamless 1024 px floor and surface textures for the variation rooms.

The quarter-view camera spends most of the frame on the ground, so each theme gets real surface maps
instead of the original 256 px grain: soil with leaf litter, moss, cobbles, tech panels (with an
emission mask), asphalt with crack networks, paving slabs, cave rock and a water ripple normal map.
Every map is periodic (FFT-filtered noise, wrapped Voronoi), so tiling never shows seams.
"""
import pathlib

import numpy as np
from PIL import Image

SIZE = 1024


def _rng(seed):
    return np.random.default_rng(seed)


def band_noise(seed, low, high, size=SIZE):
    """Periodic noise with energy between spatial frequencies low..high (cycles per tile), normalised 0..1."""
    rng = _rng(seed)
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    radius = np.sqrt(fx * fx + fy * fy)
    window = np.exp(-((np.log(np.maximum(radius, 1e-3)) - np.log(np.sqrt(low * high))) ** 2) / (2 * (np.log(high / low) / 2.6) ** 2))
    window[0, 0] = 0
    field = np.real(np.fft.ifft2(np.fft.fft2(white) * window))
    field -= field.min()
    return field / max(field.max(), 1e-9)


def fractal(seed, base=4, octaves=6, gain=0.55):
    total = np.zeros((SIZE, SIZE))
    amp, norm = 1.0, 0.0
    for k in range(octaves):
        f = base * 2 ** k
        total += band_noise(seed + k * 17, f * 0.7, f * 1.4) * amp
        norm += amp
        amp *= gain
    return total / norm


def voronoi(seed, cells, jitter=0.85):
    """Wrapped Voronoi: returns (F1 distance, F2 - F1 edge distance, cell id), distances in cell units."""
    rng = _rng(seed)
    n = cells
    points = (np.arange(n)[:, None, None] + 0.5 + (rng.random((n, n, 2)) - 0.5) * jitter)
    grid = np.stack(np.meshgrid(np.arange(n), np.arange(n), indexing='ij'), axis=-1)
    points = grid + 0.5 + (rng.random((n, n, 2)) - 0.5) * jitter
    ys, xs = np.meshgrid(np.linspace(0, n, SIZE, endpoint=False), np.linspace(0, n, SIZE, endpoint=False), indexing='ij')
    f1 = np.full((SIZE, SIZE), 1e9)
    f2 = np.full((SIZE, SIZE), 1e9)
    ident = np.zeros((SIZE, SIZE), dtype=np.int64)
    cy, cx = np.floor(ys).astype(int), np.floor(xs).astype(int)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gy, gx = (cy + dy) % n, (cx + dx) % n
            py = points[gy, gx, 0] + (cy + dy - gy)
            px = points[gy, gx, 1] + (cx + dx - gx)
            d = np.sqrt((ys - py) ** 2 + (xs - px) ** 2)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            ident = np.where(closer, gy * n + gx, ident)
            f1 = np.where(closer, d, f1)
    return f1, f2 - f1, ident


def normal_map(height, strength):
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(height)], axis=-1)   # +Y up in image space for Unity normal maps
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).round().clip(0, 255).astype(np.uint8)


def to_rgb(arr):
    return (np.clip(arr, 0, 1) * 255).round().astype(np.uint8)


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def palette(*hexes):
    return [np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]) for h in hexes]


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def scatter_mask(seed, count, rx, ry, rotate=True):
    """Periodic mask of many small rotated ellipses (leaves, pebbles). Returns coverage and an id map."""
    rng = _rng(seed)
    cover = np.zeros((SIZE, SIZE))
    ident = np.zeros((SIZE, SIZE))
    height = np.zeros((SIZE, SIZE))
    yy, xx = np.mgrid[-24:25, -24:25]
    for i in range(count):
        cx, cy = rng.integers(0, SIZE, 2)
        a = rng.random() * np.pi if rotate else 0
        sx, sy = rx * rng.uniform(0.6, 1.3), ry * rng.uniform(0.6, 1.3)
        u = (xx * np.cos(a) + yy * np.sin(a)) / sx
        v = (-xx * np.sin(a) + yy * np.cos(a)) / sy
        d = u * u + v * v
        blob = np.clip(1 - d, 0, 1)
        if not blob.any():
            continue
        ys = (np.arange(cy - 24, cy + 25) % SIZE)[:, None]
        xs = (np.arange(cx - 24, cx + 25) % SIZE)[None, :]
        take = blob > cover[ys, xs] * 0.0 + 0.0
        region_cover = cover[ys, xs]
        stamp = np.where(blob > 0.02, 1.0, 0.0)
        new_cover = np.maximum(region_cover, stamp)
        ident[ys, xs] = np.where(stamp > 0, rng.random(), ident[ys, xs])
        height[ys, xs] = np.maximum(height[ys, xs] * (1 - stamp), np.sqrt(blob) * stamp)
        cover[ys, xs] = new_cover
    return cover, ident, height


# ---- surfaces -------------------------------------------------------------------------------------
def forest_ground():
    """Dark forest soil read from 24 m: broad damp/dry blotches, clumped leaf drifts and twigs."""
    broad = fractal(11, base=2, octaves=4)
    soil = fractal(12, base=5)
    grain = band_noise(13, 70, 220)
    dark, mid, light, damp = palette('231f19', '3b3226', '5a4b36', '1a1d16')
    base = lerp(lerp(dark * np.ones((SIZE, SIZE, 3)), mid, smoothstep(0.3, 0.62, soil)), light, smoothstep(0.64, 0.9, soil) * 0.55)
    base = lerp(base, damp * np.ones_like(base), smoothstep(0.55, 0.8, broad) * 0.6)
    base *= (0.88 + grain * 0.2)[..., None]
    drift = smoothstep(0.42, 0.7, fractal(14, base=3))
    cover, ident, leaf_h = scatter_mask(15, 1500, 16, 7)
    cover *= (drift > 0.25)
    leaf_cols = palette('5e4022', '7a5a2c', '4c4a26', '6a3520', '84703f')
    leaf = np.zeros_like(base)
    for k, c in enumerate(leaf_cols):
        leaf[((ident * len(leaf_cols)).astype(int) == k)] = c
    leaf *= (0.75 + 0.35 * band_noise(16, 90, 260))[..., None]
    base = np.where(cover[..., None] > 0, lerp(base, leaf, np.full(cover.shape, 0.85)), base)
    rng = _rng(17)
    twig = np.zeros((SIZE, SIZE))
    for _ in range(120):
        x0, y0 = rng.integers(0, SIZE, 2)
        a = rng.random() * np.pi
        for t in np.linspace(0, rng.integers(20, 70), 90):
            twig[int(y0 + np.sin(a) * t) % SIZE, int(x0 + np.cos(a) * t) % SIZE] = 1
    twig = np.maximum(twig, np.roll(twig, 1, 0))
    base = lerp(base, palette('3a2a1c')[0] * np.ones_like(base), twig * 0.9)
    height = soil * 0.5 + grain * 0.2 + leaf_h * cover * 0.6 + twig * 0.35 - smoothstep(0.55, 0.8, broad) * 0.15
    return to_rgb(base * 1.38), normal_map(height, 7)


def forest_trail():
    """Packed earth trail: smoother, warmer, with embedded pebbles and few leaves."""
    body = fractal(111, base=3)
    grain = band_noise(112, 120, 380)
    dust, packed, rut = palette('8a7458', '6b5942', '4d4032')
    base = lerp(lerp(rut * np.ones((SIZE, SIZE, 3)), packed, smoothstep(0.25, 0.55, body)), dust, smoothstep(0.6, 0.9, body) * 0.7)
    base *= (0.9 + 0.16 * grain)[..., None]
    cover, ident, peb_h = scatter_mask(113, 900, 7, 6)
    pebbles = palette('8d8a80', '6f6c64', '9c9282')
    peb = np.zeros_like(base)
    for k, c in enumerate(pebbles):
        peb[((ident * len(pebbles)).astype(int) == k)] = c
    base = np.where(cover[..., None] > 0, peb * (0.8 + 0.3 * peb_h)[..., None], base)
    leaves, lid, lh = scatter_mask(114, 160, 15, 7)
    base = np.where(leaves[..., None] > 0, lerp(base, palette('6e4a24')[0] * np.ones_like(base), np.full(leaves.shape, 0.8)), base)
    height = body * 0.35 + grain * 0.15 + peb_h * cover * 0.7 + lh * leaves * 0.3
    return to_rgb(base), normal_map(height, 8)


def forest_moss():
    """Cushion moss with visible tufts and blade streaks (reads as vegetation, not paint)."""
    clumps = fractal(21, base=4)
    cover, ident, tuft_h = scatter_mask(22, 5200, 5, 5)
    fine = band_noise(23, 180, 480)
    deep, mid, tip, dry = palette('17291a', '2d4a28', '5f7f3c', '6f6a3a')
    base = lerp(lerp(deep * np.ones((SIZE, SIZE, 3)), mid, smoothstep(0.25, 0.6, clumps)), tip, (tuft_h * cover) * 0.75)
    base = lerp(base, dry * np.ones_like(base), smoothstep(0.72, 0.9, fractal(24, base=2)) * 0.35)
    base *= (0.82 + fine * 0.3)[..., None]
    height = clumps * 0.4 + tuft_h * cover * 0.6 + fine * 0.15
    return to_rgb(base), normal_map(height, 11)


def cobble(seed=31, cells=9, stone=('6f6f67', '8a887d', '5b5d57'), mortar='2b2a26'):
    f1, edge, ident = voronoi(seed, cells, jitter=0.75)
    stones = palette(*stone)
    mortar_c = palette(mortar)[0]
    rng = _rng(seed + 1)
    tint = rng.random(cells * cells)
    col = np.zeros((SIZE, SIZE, 3))
    for k, c in enumerate(stones):
        m = ((tint[ident] * len(stones)).astype(int) == k)
        col[m] = c
    wear = fractal(seed + 2, base=6)
    pits = band_noise(seed + 3, 120, 380)
    col *= (0.8 + 0.3 * wear + 0.12 * pits)[..., None]
    gap = smoothstep(0.02, 0.09, edge)
    col = lerp(mortar_c[None, None, :] * np.ones_like(col), col, gap)
    height = gap * (0.75 + 0.25 * np.sqrt(np.clip(1 - f1, 0, 1))) + pits * 0.08
    return to_rgb(col), normal_map(height, 10)


def tech_panel():
    """Dark metal floor panels (2 x 2 per tile) with bevels, bolts and scratches; emission mask of circuit traces."""
    u = np.linspace(0, 2, SIZE, endpoint=False)
    uu, vv = np.meshgrid(u, u)
    fu, fv = uu % 1, vv % 1
    edge = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv))
    bevel = smoothstep(0.004, 0.028, edge)
    inset = smoothstep(0.06, 0.075, edge) * (1 - smoothstep(0.075, 0.09, edge))
    scratches = band_noise(41, 200, 500) ** 6
    grime = fractal(42, base=3)
    base = np.full((SIZE, SIZE, 3), palette('1a2436')[0])
    base *= (0.75 + 0.3 * grime + 0.3 * scratches)[..., None]
    base = lerp(np.full_like(base, 0.03), base, bevel)
    bolt = np.zeros((SIZE, SIZE))
    for cx in (0.12, 0.88):
        for cy in (0.12, 0.88):
            d = np.sqrt((fu - cx) ** 2 + (fv - cy) ** 2)
            bolt = np.maximum(bolt, smoothstep(0.022, 0.012, d))
    base = lerp(base, np.full_like(base, 0.36), bolt * 0.8)
    height = bevel * 0.8 + bolt * 0.35 - inset * 0.15 + scratches * 0.03
    # Emission: thin traces following the panel inset plus a few right-angle runs.
    rng = _rng(43)
    trace = inset * 0.55
    for _ in range(7):
        y = rng.integers(0, SIZE)
        x0, length = rng.integers(0, SIZE), rng.integers(80, 360)
        xs = np.arange(x0, x0 + length) % SIZE
        trace[y % SIZE, xs] = 1
        trace[(y + 1) % SIZE, xs] = 1
        x1 = xs[-1]
        ys = np.arange(y, y + rng.integers(40, 180)) % SIZE
        trace[ys, x1] = 1
        trace[ys, (x1 + 1) % SIZE] = 1
    glow = np.clip(trace, 0, 1)
    glow = np.maximum(glow, np.clip(band_noise(44, 3, 6) - 0.72, 0, 1) * 0)  # reserved for pulses
    emission = to_rgb(np.repeat(glow[..., None], 3, axis=2))
    return to_rgb(base), normal_map(height, 12), emission


def asphalt():
    aggregate = band_noise(51, 180, 480)
    body = fractal(52, base=3)
    dark, mid = palette('26292a', '3b3f3f')
    base = lerp(dark * np.ones((SIZE, SIZE, 3)), mid, smoothstep(0.3, 0.8, body))
    base *= (0.82 + 0.35 * aggregate)[..., None]
    f1, edge, _ = voronoi(53, 5, jitter=0.95)
    crack_noise = band_noise(54, 20, 60)
    crack = 1 - smoothstep(0.0, 0.018 + crack_noise * 0.01, edge)
    crack *= smoothstep(0.35, 0.6, fractal(55, base=2))
    base = lerp(base, np.full_like(base, 0.06), crack * 0.9)
    tar = smoothstep(0.7, 0.78, fractal(56, base=4))
    base = lerp(base, np.full_like(base, 0.09), tar * 0.5)
    height = aggregate * 0.35 + body * 0.2 - crack * 0.6
    return to_rgb(base), normal_map(height, 8)


def paving():
    """1 m concrete paving slabs, 4 x 4 per tile, with chips, stains and a few cracked slabs."""
    u = np.linspace(0, 4, SIZE, endpoint=False)
    uu, vv = np.meshgrid(u, u)
    fu, fv = uu % 1, vv % 1
    edge = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv))
    joint = smoothstep(0.012, 0.03, edge)
    slab_id = (np.floor(uu) * 7 + np.floor(vv) * 13) % 16
    rng = _rng(61)
    tints = 0.9 + 0.18 * rng.random(16)
    grain = band_noise(62, 150, 420)
    stain = fractal(63, base=3)
    base = np.full((SIZE, SIZE, 3), palette('8c8b82')[0]) * tints[slab_id.astype(int)][..., None]
    base *= (0.85 + 0.2 * grain)[..., None] * (0.8 + 0.25 * stain)[..., None]
    _, cedge, _ = voronoi(64, 3, jitter=1.0)
    crack = (1 - smoothstep(0.0, 0.012, cedge)) * smoothstep(0.55, 0.7, fractal(65, base=2))
    base = lerp(base, np.full_like(base, 0.2), crack * 0.8)
    base = lerp(np.full_like(base, 0.16), base, joint)
    height = joint * 0.7 + grain * 0.15 - crack * 0.4
    return to_rgb(base), normal_map(height, 9)


def cave_rock():
    strata = band_noise(71, 3, 7)
    lines = np.sin((np.linspace(0, 1, SIZE, endpoint=False)[:, None] * 22 + strata * 3.5) * np.pi * 2)
    body = fractal(72, base=4)
    grit = band_noise(73, 160, 460)
    dark, mid, light = palette('1d2528', '34424a', '5a6a70')
    base = lerp(lerp(dark * np.ones((SIZE, SIZE, 3)), mid, smoothstep(0.25, 0.65, body)), light,
                smoothstep(0.6, 0.95, body * 0.6 + (lines * 0.5 + 0.5) * 0.4) * 0.55)
    base *= (0.82 + 0.3 * grit)[..., None]
    _, edge, _ = voronoi(74, 6, jitter=0.9)
    crack = 1 - smoothstep(0.0, 0.02, edge)
    base = lerp(base, np.full_like(base, 0.04), crack * 0.85)
    wet = smoothstep(0.66, 0.8, fractal(75, base=3))
    base = lerp(base, base * 0.55, wet[..., None].squeeze(-1) if False else wet)
    height = body * 0.45 + (lines * 0.5 + 0.5) * 0.15 + grit * 0.2 - crack * 0.5
    return to_rgb(base), normal_map(height, 9)


def water_ripples():
    """Neutral light base (the material colour tints the water) with gentle ripple normals."""
    h = band_noise(81, 8, 20) * 0.6 + band_noise(82, 24, 60) * 0.3 + band_noise(83, 70, 140) * 0.1
    base = np.full((SIZE, SIZE, 3), 0.78) * (0.9 + 0.16 * h)[..., None]
    return to_rgb(base), normal_map(h, 6)


SURFACES = {
    'ForestGround_v2': forest_ground,
    'ForestMoss_v2': forest_moss,
    'ForestTrail_v2': forest_trail,
    'Cobble_v2': cobble,
    'TechPanel_v2': tech_panel,
    'Asphalt_v2': asphalt,
    'Paving_v2': paving,
    'CaveRock_v2': cave_rock,
    'Water_v2': water_ripples,
}


def write_all(folder):
    folder = pathlib.Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    written = {}
    for name, factory in SURFACES.items():
        maps = factory()
        paths = {'base': folder / f'{name}_Base.png', 'normal': folder / f'{name}_Normal.png'}
        Image.fromarray(maps[0], 'RGB').save(paths['base'], optimize=True)
        Image.fromarray(maps[1], 'RGB').save(paths['normal'], optimize=True)
        if len(maps) > 2:
            paths['emission'] = folder / f'{name}_Emission.png'
            Image.fromarray(maps[2], 'RGB').save(paths['emission'], optimize=True)
        written[name] = paths
    return written


if __name__ == '__main__':
    import sys
    out = write_all(sys.argv[1] if len(sys.argv) > 1 else 'textures_out')
    for name, paths in out.items():
        print(name, {k: str(v) for k, v in paths.items()})
