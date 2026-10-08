"""URP Lit materials and surface textures for the variation rooms.

Existing concept materials keep their names and GUIDs (rooms, gates and the old kit still reference them).
Emissive materials are written with the _EMISSION keyword and an emissive GI flag: URP's material validation
(BaseShaderGUI.SetMaterialKeywords) strips _EMISSION whenever globalIlluminationFlags has no emissive bit,
which is why the first kit's runes, circuits and crystals were saved without any glow.
"""
import dataclasses
import pathlib
import re

import unityasset as ua

ROOT = pathlib.Path(__file__).resolve().parents[3]
MATERIALS = ROOT / 'Assets/StageConcepts/Materials'
TEXTURES = ROOT / 'Assets/StageConcepts/Textures'
V2 = TEXTURES / 'V2'
TEMPLATE = MATERIALS / 'Forest_Moss.mat'
LIT_SHADER = '{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}'
ASSET_VERSION_SCRIPT = '{fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}'

# Original 256 px procedural grain maps (Texture2D .asset, reference type 2).
GRAIN = {
    'leaf': 'LeafLitter_v1', 'rock': 'StratifiedRock_v1', 'concrete': 'WeatheredConcrete_v1',
    'asphalt': 'AsphaltAggregate_v1', 'panel': 'BrushedPanel_v1', 'moss': 'MossGrain_v1',
}


@dataclasses.dataclass
class Mat:
    name: str
    color: str                 # sRGB hex, multiplied with the base map
    metallic: float = 0.0
    smoothness: float = 0.2
    texture: str = None        # key in GRAIN or a V2 surface name
    tiling: float = 0.85       # base map scale in 1/m (UVs are planar metres)
    bump: float = 0.7
    emission: str = None       # sRGB hex
    emission_strength: float = 0.0
    emission_map: bool = False  # use <texture>_Emission.png
    cull: int = 2              # 0 = double sided

    @property
    def emissive(self):
        return bool(self.emission) and self.emission_strength > 0


def hex_rgb(value):
    value = value.lstrip('#')
    return tuple(int(value[i:i + 2], 16) / 255 for i in (0, 2, 4))


def texture_refs(mat, guids):
    """(base, normal, emission) texture reference strings for a material."""
    none = '{fileID: 0}'
    if not mat.texture:
        return none, none, none
    if mat.texture in GRAIN:
        stem = GRAIN[mat.texture]
        base = '{fileID: 2800000, guid: %s, type: 2}' % guids[stem + '_Base']
        normal = '{fileID: 2800000, guid: %s, type: 2}' % guids[stem + '_Normal']
        return base, normal, none
    base = '{fileID: 2800000, guid: %s, type: 3}' % guids[mat.texture + '_Base']
    normal = '{fileID: 2800000, guid: %s, type: 3}' % guids[mat.texture + '_Normal']
    emission = '{fileID: 2800000, guid: %s, type: 3}' % guids[mat.texture + '_Emission'] if mat.emission_map else none
    return base, normal, emission


def material_yaml(mat, guids, asset_version_id):
    template = TEMPLATE.read_text(encoding='utf-8')
    base, normal, emission_map = texture_refs(mat, guids)
    r, g, b = hex_rgb(mat.color)
    keywords = []
    if mat.emissive:
        keywords.append('_EMISSION')
    if mat.texture:
        keywords.append('_NORMALMAP')
    text = template
    text = re.sub(r'  m_Name: .*', f'  m_Name: {mat.name}', text, count=1)
    text = re.sub(r'  m_ValidKeywords:(\n  - .*)*', '  m_ValidKeywords:' + (''.join('\n  - ' + k for k in sorted(keywords)) or ' []'), text, count=1)
    # 2 = BakedEmissive keeps URP's validation from stripping _EMISSION; 4 = EmissiveIsBlack for plain surfaces.
    text = re.sub(r'  m_LightmapFlags: \d+', f'  m_LightmapFlags: {2 if mat.emissive else 4}', text, count=1)

    def tex(name, ref, scale):
        nonlocal text
        pattern = r'(    - %s:\n        m_Texture: )\{[^}]*\}(\n        m_Scale: )\{[^}]*\}' % re.escape(name)
        text = re.sub(pattern, lambda m: m.group(1) + ref + m.group(2) + '{x: %s, y: %s}' % (ua.f32(scale), ua.f32(scale)), text, count=1)

    tex('_BaseMap', base, mat.tiling if mat.texture else 1)
    tex('_MainTex', base, mat.tiling if mat.texture else 1)
    tex('_BumpMap', normal, 1)
    tex('_EmissionMap', emission_map, 1)

    def flt(name, value):
        nonlocal text
        text = re.sub(r'(    - %s: )[-0-9.e]+' % re.escape(name), lambda m: m.group(1) + ua.f32(value), text, count=1)

    flt('_BumpScale', mat.bump if mat.texture else 1)
    flt('_Metallic', mat.metallic)
    flt('_Smoothness', mat.smoothness)
    flt('_Cull', mat.cull)

    def col(name, rgba):
        nonlocal text
        text = re.sub(r'(    - %s: )\{[^}]*\}' % re.escape(name), lambda m: m.group(1) + ua.color(rgba), text, count=1)

    col('_BaseColor', (r, g, b, 1))
    col('_Color', (r, g, b, 1))
    if mat.emissive:
        er, eg, eb = hex_rgb(mat.emission)
        k = mat.emission_strength
        col('_EmissionColor', (er * k, eg * k, eb * k, 1))
    else:
        col('_EmissionColor', (0, 0, 0, 1))
    text = re.sub(r'--- !u!114 &-?\d+', f'--- !u!114 &{asset_version_id}', text, count=1)
    return text


def existing_asset_version_id(path):
    if not path.exists():
        return None
    m = re.search(r'--- !u!114 &(-?\d+)', path.read_text(encoding='utf-8'))
    return int(m.group(1)) if m else None


def write_material(mat, guids):
    path = MATERIALS / (mat.name + '.mat')
    asset_version = existing_asset_version_id(path) or ua.file_id()
    ua.write_text(path, material_yaml(mat, guids, asset_version))
    return ua.ensure_meta(path, lambda g: ua.native_meta(g, 2100000))


# ---- textures ------------------------------------------------------------------------------------
def png_meta(guid, kind):
    """TextureImporter settings copied from Assets/Liminal/Textures, with trilinear x4 anisotropic filtering
    for floors seen at a 55 degree pitch."""
    template = (ROOT / 'Assets/Liminal/Textures/Surface_concrete_Normal.png.meta').read_text(encoding='utf-8')
    text = re.sub(r'guid: [0-9a-f]+', f'guid: {guid}', template, count=1)
    srgb = 0 if kind == 'normal' else 1
    text = re.sub(r'sRGBTexture: \d', f'sRGBTexture: {srgb}', text, count=1)
    text = re.sub(r'  textureType: \d', f'  textureType: {1 if kind == "normal" else 0}', text, count=1)
    text = re.sub(r'    filterMode: \d', '    filterMode: 2', text, count=1)
    text = re.sub(r'    aniso: \d', '    aniso: 4', text, count=1)
    text = text.replace('maxTextureSize: 2048', 'maxTextureSize: 1024')
    return text


def texture_guids():
    """GUIDs of the grain .asset maps and the V2 PNG surfaces (V2 metas are created on demand)."""
    guids = {}
    for stem in GRAIN.values():
        for suffix in ('_Base', '_Normal'):
            guids[stem + suffix] = ua.read_guid(TEXTURES / f'{stem}{suffix}.asset.meta')
    ua.ensure_meta(V2, ua.folder_meta)
    for png in sorted(V2.glob('*.png')):
        kind = 'normal' if png.stem.endswith('_Normal') else 'color'
        guids[png.stem] = ua.ensure_meta(png, lambda g, k=kind: png_meta(g, k))
    return guids


def srgb_to_linear(x):
    """Extended sRGB curve (values above 1 follow the same power law, as Unity's GammaToLinearSpace)."""
    return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4


def preview_material(mat):
    r, g, b = hex_rgb(mat.color)
    out = {'name': mat.name, 'color': [srgb_to_linear(c) for c in (r, g, b)], 'metallic': mat.metallic,
           'roughness': 1 - mat.smoothness, 'texture': mat.texture, 'tiling': mat.tiling if mat.texture else 1,
           'bump': mat.bump, 'doubleSided': mat.cull == 0, 'emission': None}
    if mat.emissive:
        er, eg, eb = hex_rgb(mat.emission)
        k = mat.emission_strength
        out['emission'] = [srgb_to_linear(c * k) for c in (er, eg, eb)]
        out['emissionMap'] = bool(mat.emission_map)
    return out
