"""Offline textured geometry review of the final GLBs (not a Unity beauty render)."""
import io
import numpy as np
from PIL import Image, ImageDraw

from meshy_monsters import ASSETS, OUT, ROOT
from optimize_textures import load_glb
from prepare_forms import read_accessor, require_single_mesh


def render(key, width=560, height=560):
    path = OUT / key / (key + '.glb')
    gltf, binary = load_glb(path)
    primitive = require_single_mesh(gltf)
    vertices = read_accessor(gltf, binary, primitive['attributes']['POSITION'])
    normals = read_accessor(gltf, binary, primitive['attributes']['NORMAL'])
    uv = read_accessor(gltf, binary, primitive['attributes']['TEXCOORD_0'])
    faces = read_accessor(gltf, binary, primitive['indices']).reshape(-1, 3)
    material = gltf['materials'][primitive.get('material', 0)]
    tex_index = material['pbrMetallicRoughness']['baseColorTexture']['index']
    source = gltf['textures'][tex_index]['source']
    view = gltf['bufferViews'][gltf['images'][source]['bufferView']]
    texture = np.asarray(Image.open(io.BytesIO(binary[view.get('byteOffset', 0):
                      view.get('byteOffset', 0) + view['byteLength']])).convert('RGB'))
    direction = np.array([1.1, .9, 2.4]) if key != 'lunar_butterfly' else np.array([.2, 2.5, 1.3])
    direction /= np.linalg.norm(direction)
    right = np.cross([0, 1, 0], direction)
    right /= np.linalg.norm(right)
    up = np.cross(direction, right)
    points = np.column_stack((vertices @ right, vertices @ up, vertices @ direction))
    low, high = points.min(axis=0), points.max(axis=0)
    scale = min((width - 76) / (high[0] - low[0]), (height - 108) / (high[1] - low[1]))
    points[:, 0] = (points[:, 0] - (low[0] + high[0]) / 2) * scale + width / 2
    points[:, 1] = -(points[:, 1] - (low[1] + high[1]) / 2) * scale + height / 2 - 10
    light = np.array([-.3, .8, .65]); light /= np.linalg.norm(light)
    shade = .62 + .38 * np.maximum(0, normals @ light)
    pixels = np.full((height, width, 3), [25, 31, 35], dtype=np.uint8)
    depth = np.full((height, width), -np.inf)
    for face in faces:
        p = points[face]
        min_x, max_x = max(0, int(np.floor(p[:, 0].min()))), min(width - 1, int(np.ceil(p[:, 0].max())))
        min_y, max_y = max(0, int(np.floor(p[:, 1].min()))), min(height - 1, int(np.ceil(p[:, 1].max())))
        if min_x > max_x or min_y > max_y: continue
        den = ((p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1]))
        if abs(den) < 1e-7: continue
        yy, xx = np.mgrid[min_y:max_y + 1, min_x:max_x + 1]
        a = ((p[1, 1] - p[2, 1]) * (xx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (yy - p[2, 1])) / den
        b = ((p[2, 1] - p[0, 1]) * (xx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (yy - p[2, 1])) / den
        c = 1 - a - b
        z = a * p[0, 2] + b * p[1, 2] + c * p[2, 2]
        old_depth = depth[min_y:max_y + 1, min_x:max_x + 1]
        mask = (a >= 0) & (b >= 0) & (c >= 0) & (z > old_depth)
        if not mask.any(): continue
        sampled = a[..., None] * uv[face[0]] + b[..., None] * uv[face[1]] + c[..., None] * uv[face[2]]
        tx = np.clip((sampled[..., 0] * texture.shape[1]).astype(int), 0, texture.shape[1] - 1)
        ty = np.clip((sampled[..., 1] * texture.shape[0]).astype(int), 0, texture.shape[0] - 1)
        lighting = a * shade[face[0]] + b * shade[face[1]] + c * shade[face[2]]
        colored = (texture[ty, tx] * lighting[..., None]).clip(0, 255).astype(np.uint8)
        pixels[min_y:max_y + 1, min_x:max_x + 1][mask] = colored[mask]
        old_depth[mask] = z[mask]
    result = Image.fromarray(pixels)
    draw = ImageDraw.Draw(result)
    draw.text((22, height - 41), key + '  /  ' + format(len(faces), ',') + ' tris', fill=(201, 220, 218))
    return result


if __name__ == '__main__':
    directory = ROOT / 'Documentation/ForestMonsters'
    sheet = Image.new('RGB', (1680, 1120), (25, 31, 35))
    for i, key in enumerate(ASSETS):
        img = render(key)
        img.save(directory / ('meshy-assets-final-' + key + '.png'))
        sheet.paste(img, ((i % 3) * 560, (i // 3) * 560))
    sheet.save(directory / 'meshy-assets-final-lineup.png')
    print('WROTE final geometry review previews and lineup')
