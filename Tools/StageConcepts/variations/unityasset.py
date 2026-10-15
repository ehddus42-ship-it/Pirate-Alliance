"""Unity text-serialisation writers for the variation kit.

Every template below mirrors a file Unity 6000.3 wrote for this project (StageConcepts prefabs, meshes,
materials and stage definitions), so assets produced here load exactly like the originals. The layout
JSON written next to each room lets Unity rebuild the same prefabs through its own API as well
(AC Roguelike > Stage Concepts > Rebuild Variation Rooms From Layout).
"""
import json
import math
import pathlib
import random
import struct
import uuid

import numpy as np

_rand = random.Random()


def seed_ids(seed):
    """Deterministic fileIDs/GUIDs so regenerating a room does not churn every reference."""
    _rand.seed(seed)


def file_id():
    return _rand.randrange(10 ** 17, 2 ** 63 - 1)


def new_guid():
    return uuid.UUID(int=_rand.getrandbits(128), version=4).hex


def f32(value):
    """Shortest float32 text, as Unity writes it (0 instead of 0.0, no exponent for normal ranges)."""
    x = float(np.float32(value))
    if x == 0:
        return '0'
    if math.isfinite(x) and x == int(x) and abs(x) < 1e15:
        return str(int(x))
    text = np.format_float_positional(np.float32(x), trim='-', unique=True)
    if 'e' in text or len(text) > 16:
        text = repr(float(np.float32(x)))
    return text


def vec3(v):
    return '{x: %s, y: %s, z: %s}' % (f32(v[0]), f32(v[1]), f32(v[2]))


def quat(q):
    return '{x: %s, y: %s, z: %s, w: %s}' % (f32(q[0]), f32(q[1]), f32(q[2]), f32(q[3]))


def color(c):
    return '{r: %s, g: %s, b: %s, a: %s}' % (f32(c[0]), f32(c[1]), f32(c[2]), f32(c[3] if len(c) > 3 else 1))


def yaw_quaternion(degrees):
    r = math.radians(degrees) * 0.5
    return (0.0, math.sin(r), 0.0, math.cos(r))


def euler_quaternion(pitch, yaw, roll):
    """Unity Quaternion.Euler (ZXY order) as (x, y, z, w)."""
    cx, sx = math.cos(math.radians(pitch) / 2), math.sin(math.radians(pitch) / 2)
    cy, sy = math.cos(math.radians(yaw) / 2), math.sin(math.radians(yaw) / 2)
    cz, sz = math.cos(math.radians(roll) / 2), math.sin(math.radians(roll) / 2)
    # q = qy * qx * qz
    x = cy * sx * cz + sy * cx * sz
    y = sy * cx * cz - cy * sx * sz
    z = cy * cx * sz - sy * sx * cz
    w = cy * cx * cz + sy * sx * sz
    return (x, y, z, w)


def yaml_string(text):
    """Double-quoted YAML scalar with Unity-style \\u escapes for non-ASCII."""
    out = []
    for ch in text:
        code = ord(ch)
        if ch == '"':
            out.append('\\"')
        elif ch == '\\':
            out.append('\\\\')
        elif ch == '\n':
            out.append('\\n')
        elif ch == '\t':
            out.append('\\t')
        elif code < 0x20:
            out.append('\\x%02X' % code)
        elif code < 0x80:
            out.append(ch)
        elif code <= 0xFF:
            out.append('\\x%02X' % code)
        elif code <= 0xFFFF:
            out.append('\\u%04X' % code)
        else:
            out.append('\\U%08X' % code)
    return '"' + ''.join(out) + '"'


HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'

# ---------------------------------------------------------------------------------------------------
# Mesh assets (Mesh serializedVersion 12, position/normal/tangent/uv0 interleaved, 48-byte stride)
# ---------------------------------------------------------------------------------------------------
_EMPTY_CHANNEL = '    - stream: 0\n      offset: 0\n      format: 0\n      dimension: 0\n'


def mesh_yaml(name, positions, normals, tangents, uvs, triangles):
    positions = np.asarray(positions, dtype=np.float32)
    normals = np.asarray(normals, dtype=np.float32)
    tangents = np.asarray(tangents, dtype=np.float32)
    uvs = np.asarray(uvs, dtype=np.float32)
    tris = np.asarray(triangles, dtype=np.int64).reshape(-1)
    count = len(positions)
    wide = count > 65535
    index_bytes = tris.astype('<u4' if wide else '<u2').tobytes()
    interleaved = np.concatenate([positions, normals, tangents, uvs], axis=1).astype('<f4').tobytes()
    low, high = positions.min(axis=0), positions.max(axis=0)
    center, extent = (low + high) * 0.5, (high - low) * 0.5
    aabb = '      m_Center: %s\n      m_Extent: %s\n' % (vec3(center), vec3(extent))
    channels = (
        '    - stream: 0\n      offset: 0\n      format: 0\n      dimension: 3\n'
        '    - stream: 0\n      offset: 12\n      format: 0\n      dimension: 3\n'
        '    - stream: 0\n      offset: 24\n      format: 0\n      dimension: 4\n'
        + _EMPTY_CHANNEL
        + '    - stream: 0\n      offset: 40\n      format: 0\n      dimension: 2\n'
        + _EMPTY_CHANNEL * 9)
    compressed_block = ''.join('    %s:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n' % k
                               for k in ('m_Vertices', 'm_UV', 'm_Normals', 'm_Tangents'))
    compressed_block += ''.join('    %s:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n' % k
                                for k in ('m_Weights', 'm_NormalSigns', 'm_TangentSigns'))
    compressed_block += '    m_FloatColors:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n'
    compressed_block += ''.join('    %s:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n' % k
                                for k in ('m_BoneIndices', 'm_Triangles'))
    return (HEADER + '--- !u!43 &4300000\nMesh:\n'
            '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
            '  m_PrefabAsset: {fileID: 0}\n'
            f'  m_Name: {name}\n  serializedVersion: 12\n  m_SubMeshes:\n  - serializedVersion: 2\n'
            f'    firstByte: 0\n    indexCount: {len(tris)}\n    topology: 0\n    baseVertex: 0\n    firstVertex: 0\n'
            f'    vertexCount: {count}\n    localAABB:\n' + aabb.replace('      m_', '      m_') +
            '  m_Shapes:\n    vertices: []\n    shapes: []\n    channels: []\n    fullWeights: []\n'
            '  m_BindPose: []\n  m_BoneNameHashes: \n  m_RootBoneNameHash: 0\n  m_BonesAABB: []\n'
            '  m_VariableBoneCountWeights:\n    m_Data: \n  m_MeshCompression: 0\n  m_IsReadable: 1\n'
            '  m_KeepVertices: 1\n  m_KeepIndices: 1\n'
            f'  m_IndexFormat: {1 if wide else 0}\n  m_IndexBuffer: {index_bytes.hex()}\n'
            f'  m_VertexData:\n    serializedVersion: 3\n    m_VertexCount: {count}\n    m_Channels:\n{channels}'
            f'    m_DataSize: {len(interleaved)}\n    _typelessdata: {interleaved.hex()}\n'
            '  m_CompressedMesh:\n' + compressed_block + '    m_UVInfo: 0\n'
            '  m_LocalAABB:\n' + aabb.replace('      ', '    ') +
            '  m_MeshUsageFlags: 0\n  m_CookingOptions: 30\n  m_BakedConvexCollisionMesh: \n'
            "  m_BakedTriangleCollisionMesh: \n  'm_MeshMetrics[0]': 1\n  'm_MeshMetrics[1]': 1\n"
            '  m_MeshOptimizationFlags: 1\n  m_StreamData:\n    serializedVersion: 2\n    offset: 0\n    size: 0\n'
            '    path: \n  m_MeshLodInfo:\n    serializedVersion: 2\n    m_LodSelectionCurve:\n      serializedVersion: 1\n'
            '      m_LodSlope: 0\n      m_LodBias: 0\n    m_NumLevels: 1\n    m_SubMeshes:\n    - serializedVersion: 2\n'
            '      m_Levels:\n      - serializedVersion: 1\n        m_IndexStart: 0\n        m_IndexCount: 0\n')


def native_meta(guid, main_object_id):
    return (f'fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n'
            f'  mainObjectFileID: {main_object_id}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


def prefab_meta(guid):
    return (f'fileFormatVersion: 2\nguid: {guid}\nPrefabImporter:\n  externalObjects: {{}}\n  userData: \n'
            '  assetBundleName: \n  assetBundleVariant: \n')


def gltf_meta(guid):
    """glTFast ScriptedImporter settings identical to the twelve existing concept GLBs."""
    return (f'fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n'
            '  serializedVersion: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
            '  script: {fileID: 11500000, guid: 715df9372183c47e389bb6e19fbc3b52, type: 3}\n'
            '  editorImportSettings:\n    generateSecondaryUVSet: 0\n  importSettings:\n    nodeNameMethod: 1\n'
            '    animationMethod: 2\n    generateMipMaps: 1\n    texturesReadable: 0\n    defaultMinFilterMode: 9729\n'
            '    defaultMagFilterMode: 9729\n    anisotropicFilterLevel: 1\n  instantiationSettings:\n    mask: -1\n'
            '    layer: 0\n    skinUpdateWhenOffscreen: 1\n    lightIntensityFactor: 1\n    sceneObjectCreation: 2\n'
            '  assetDependencies: []\n  reportItems: []\n')


def default_meta(guid):
    return (f'fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n'
            '  assetBundleName: \n  assetBundleVariant: \n')


def text_meta(guid):
    return (f'fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n'
            '  assetBundleName: \n  assetBundleVariant: \n')


def folder_meta(guid):
    return (f'fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n'
            '  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


def read_guid(meta_path):
    for line in pathlib.Path(meta_path).read_text(encoding='utf-8').splitlines():
        if line.startswith('guid: '):
            return line[6:].strip()
    raise ValueError(f'{meta_path}: no guid')


def write_text(path, text):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    # Unity writes LF line endings for its YAML assets in this repository.
    with open(path, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(text)


def ensure_meta(path, factory, guid=None):
    """Reuse an existing .meta (keeps GUID references stable) or create one."""
    meta = pathlib.Path(str(path) + '.meta')
    if meta.exists():
        return read_guid(meta)
    guid = guid or new_guid()
    write_text(meta, factory(guid))
    return guid
