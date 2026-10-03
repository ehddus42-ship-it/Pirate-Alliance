"""Read, edit and write Unity prefab YAML while keeping every untouched document byte-identical.

Room shells (floor, boundaries, gates, markers, sockets) stay exactly as Unity serialised them; only the
`Props` and `Lighting` subtrees are replaced. Object templates below are copied from Unity 6000.3 output
in Assets/StageConcepts/Prefabs/Rooms.
"""
import re

from unityasset import HEADER, file_id, f32, quat, vec3, color

GLTF_ROOT_GAMEOBJECT = -8098169881513260187   # identical in every Meshy GLB imported by glTFast 6.20
GLTF_ROOT_TRANSFORM = 3447742079981357586
GLTF_MAIN_OBJECT = 3150474306388093854
BUILTIN_CUBE = '{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}'


CLASS_NAMES = {1: 'GameObject', 4: 'Transform', 23: 'MeshRenderer', 33: 'MeshFilter', 65: 'BoxCollider',
               108: 'Light', 114: 'MonoBehaviour', 1001: 'PrefabInstance'}


class Doc:
    __slots__ = ('cls', 'fid', 'stripped', 'body')

    def __init__(self, cls, fid, body, stripped=False):
        # Every Unity YAML document starts with its class name line ("GameObject:", "Transform:", ...). Without it
        # the text is still valid YAML, but Unity rejects the whole prefab, so factory bodies get it here.
        if not body[:1].isalpha():
            body = CLASS_NAMES[cls] + ':\n' + body
        self.cls, self.fid, self.body, self.stripped = cls, fid, body, stripped

    def text(self):
        return f'--- !u!{self.cls} &{self.fid}{" stripped" if self.stripped else ""}\n{self.body}'

    def field(self, name):
        m = re.search(r'^  ' + re.escape(name) + r': (.*)$', self.body, re.M)
        return m.group(1) if m else None

    def ref(self, name):
        value = self.field(name)
        m = re.match(r'\{fileID: (-?\d+)', value or '')
        return int(m.group(1)) if m else 0


class Prefab:
    def __init__(self, text):
        assert text.startswith(HEADER)
        self.docs = []
        for chunk in text[len(HEADER):].split('--- !u!')[1:]:
            head, _, body = chunk.partition('\n')
            m = re.match(r'(\d+) &(-?\d+)( stripped)?$', head)
            self.docs.append(Doc(int(m.group(1)), int(m.group(2)), body, bool(m.group(3))))
        self.by_id = {d.fid: d for d in self.docs}

    def text(self):
        return HEADER + ''.join(d.text() for d in self.docs)

    # ---- graph helpers --------------------------------------------------------------------------
    def game_objects(self, name):
        return [d for d in self.docs if d.cls == 1 and d.field('m_Name') == name]

    def transform_of(self, go):
        for m in re.finditer(r'component: \{fileID: (-?\d+)\}', go.body):
            doc = self.by_id.get(int(m.group(1)))
            if doc and doc.cls == 4:
                return doc
        raise KeyError('GameObject without Transform')

    def children(self, transform):
        block = re.search(r'  m_Children:(.*?)\n  m_Father', transform.body, re.S).group(1)
        return [int(x) for x in re.findall(r'fileID: (-?\d+)', block)]

    def set_children(self, transform, ids):
        listing = ' []' if not ids else ''.join(f'\n  - {{fileID: {i}}}' for i in ids)
        transform.body = re.sub(r'  m_Children:.*?\n  m_Father', f'  m_Children:{listing}\n  m_Father',
                                transform.body, count=1, flags=re.S)

    def remove_subtree(self, transform_id):
        """Remove a transform, its GameObject, components and every descendant (nested prefabs included)."""
        doomed = set()
        stack = [transform_id]
        while stack:
            tid = stack.pop()
            t = self.by_id[tid]
            doomed.add(tid)
            if t.stripped:
                instance = t.ref('m_PrefabInstance')
                doomed.add(instance)
                doomed.update(d.fid for d in self.docs if d.stripped and d.ref('m_PrefabInstance') == instance)
                continue
            stack.extend(self.children(t))
            go = self.by_id[t.ref('m_GameObject')]
            doomed.add(go.fid)
            doomed.update(int(x) for x in re.findall(r'component: \{fileID: (-?\d+)\}', go.body))
        self.docs = [d for d in self.docs if d.fid not in doomed]
        self.by_id = {d.fid: d for d in self.docs}

    def add(self, *docs):
        for d in docs:
            assert d.fid not in self.by_id, d.fid
            self.docs.append(d)
            self.by_id[d.fid] = d

    def remap_ids(self):
        """Give every document a fresh fileID (used to derive new rooms from an existing shell)."""
        mapping = {d.fid: file_id() for d in self.docs}
        pattern = re.compile(r'(fileID: )(-?\d+)(\}|, guid)')

        def swap(m):
            old = int(m.group(2))
            if m.group(3) == '}' and old in mapping:
                return f'{m.group(1)}{mapping[old]}{m.group(3)}'
            return m.group(0)

        for d in self.docs:
            d.body = pattern.sub(swap, d.body)
            d.fid = mapping[d.fid]
        self.by_id = {d.fid: d for d in self.docs}
        return mapping


# ---- document factories (Unity 6000.3 serialisation) ----------------------------------------------
def game_object(fid, name, components, active=True):
    comps = ''.join(f'  - component: {{fileID: {c}}}\n' for c in components)
    return Doc(1, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                    f'  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n{comps}  m_Layer: 0\n'
                    f'  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n'
                    f'  m_StaticEditorFlags: 0\n  m_IsActive: {1 if active else 0}\n')


def transform(fid, go, position, rotation=(0, 0, 0, 1), scale=(1, 1, 1), children=(), father=0, euler_hint=(0, 0, 0)):
    listing = ' []' if not children else ''.join(f'\n  - {{fileID: {c}}}' for c in children)
    return Doc(4, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                    f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n'
                    f'  m_LocalRotation: {quat(rotation)}\n  m_LocalPosition: {vec3(position)}\n'
                    f'  m_LocalScale: {vec3(scale)}\n  m_ConstrainProportionsScale: 0\n  m_Children:{listing}\n'
                    f'  m_Father: {{fileID: {father}}}\n  m_LocalEulerAnglesHint: {vec3(euler_hint)}\n')


def mesh_filter(fid, go, mesh_guid):
    return Doc(33, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                     f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n'
                     f'  m_Mesh: {{fileID: 4300000, guid: {mesh_guid}, type: 2}}\n')


def mesh_renderer(fid, go, material_guids, cast_shadows=True):
    mats = ''.join(f'  - {{fileID: 2100000, guid: {g}, type: 2}}\n' for g in material_guids)
    return Doc(23, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                     f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n'
                     f'  m_CastShadows: {1 if cast_shadows else 0}\n  m_ReceiveShadows: 1\n  m_DynamicOccludee: 1\n'
                     '  m_StaticShadowCaster: 0\n  m_MotionVectors: 1\n  m_LightProbeUsage: 1\n  m_ReflectionProbeUsage: 1\n'
                     '  m_RayTracingMode: 2\n  m_RayTraceProcedural: 0\n  m_RayTracingAccelStructBuildFlagsOverride: 0\n'
                     '  m_RayTracingAccelStructBuildFlags: 1\n  m_SmallMeshCulling: 1\n  m_ForceMeshLod: -1\n'
                     '  m_MeshLodSelectionBias: 0\n  m_RenderingLayerMask: 1\n  m_RendererPriority: 0\n'
                     f'  m_Materials:\n{mats}  m_StaticBatchInfo:\n    firstSubMesh: 0\n    subMeshCount: 0\n'
                     '  m_StaticBatchRoot: {fileID: 0}\n  m_ProbeAnchor: {fileID: 0}\n  m_LightProbeVolumeOverride: {fileID: 0}\n'
                     '  m_ScaleInLightmap: 1\n  m_ReceiveGI: 1\n  m_PreserveUVs: 0\n  m_IgnoreNormalsForChartDetection: 0\n'
                     '  m_ImportantGI: 0\n  m_StitchLightmapSeams: 1\n  m_SelectedEditorRenderState: 3\n  m_MinimumChartSize: 4\n'
                     '  m_AutoUVMaxDistance: 0.5\n  m_AutoUVMaxAngle: 89\n  m_LightmapParameters: {fileID: 0}\n'
                     '  m_GlobalIlluminationMeshLod: 0\n  m_SortingLayerID: 0\n  m_SortingLayer: 0\n  m_SortingOrder: 0\n'
                     '  m_MaskInteraction: 0\n  m_AdditionalVertexStreams: {fileID: 0}\n')


def box_collider(fid, go, size, center=(0, 0, 0)):
    return Doc(65, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                     f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_Material: {{fileID: 0}}\n'
                     '  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_ExcludeLayers:\n    serializedVersion: 2\n'
                     '    m_Bits: 0\n  m_LayerOverridePriority: 0\n  m_IsTrigger: 0\n  m_ProvidesContacts: 0\n  m_Enabled: 1\n'
                     f'  serializedVersion: 3\n  m_Size: {vec3(size)}\n  m_Center: {vec3(center)}\n')


def point_light(fid, go, rgb, intensity, light_range):
    return Doc(108, fid, '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
                      f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  serializedVersion: 12\n'
                      f'  m_Type: 2\n  m_Color: {color(tuple(rgb) + (1,))}\n  m_Intensity: {f32(intensity)}\n  m_Range: {f32(light_range)}\n'
                      '  m_SpotAngle: 30\n  m_InnerSpotAngle: 21.80208\n  m_CookieSize2D: {x: 0.5, y: 0.5}\n  m_Shadows:\n'
                      '    m_Type: 0\n    m_Resolution: -1\n    m_CustomResolution: -1\n    m_Strength: 1\n    m_Bias: 0.05\n'
                      '    m_NormalBias: 0.4\n    m_NearPlane: 0.2\n    m_CullingMatrixOverride:\n'
                      + ''.join(f'      e{r}{c}: {1 if r == c else 0}\n' for r in range(4) for c in range(4)) +
                      '    m_UseCullingMatrixOverride: 0\n  m_Cookie: {fileID: 0}\n  m_DrawHalo: 0\n  m_Flare: {fileID: 0}\n'
                      '  m_RenderMode: 0\n  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n'
                      '  m_RenderingLayerMask: 1\n  m_Lightmapping: 4\n  m_LightShadowCasterMode: 0\n  m_AreaSize: {x: 1, y: 1}\n'
                      '  m_BounceIntensity: 1\n  m_ColorTemperature: 6570\n  m_UseColorTemperature: 0\n'
                      '  m_BoundingSphereOverride: {x: 0, y: 0, z: 0, w: 0}\n  m_UseBoundingSphereOverride: 0\n'
                      '  m_UseViewFrustumForShadowCasterCull: 1\n  m_ForceVisible: 0\n  m_ShadowRadius: 0\n  m_ShadowAngle: 0\n'
                      '  m_LightUnit: 1\n  m_LuxAtDistance: 1\n  m_EnableSpotReflector: 1\n')


def gltf_instance(fid, parent_transform, glb_guid, name, position, scale, stripped_id=None):
    """Nested glTFast model instance plus its stripped root Transform (for the parent's m_Children)."""
    def mod(target, path, value):
        return (f'    - target: {{fileID: {target}, guid: {glb_guid}, type: 3}}\n      propertyPath: {path}\n'
                f'      value: {value}\n      objectReference: {{fileID: 0}}\n')
    mods = mod(GLTF_ROOT_GAMEOBJECT, 'm_Name', name)
    for axis, value in zip('xyz', scale):
        mods += mod(GLTF_ROOT_TRANSFORM, f'm_LocalScale.{axis}', f32(value))
    for axis, value in zip('xyz', position):
        mods += mod(GLTF_ROOT_TRANSFORM, f'm_LocalPosition.{axis}', f32(value))
    for axis, value in (('w', 1), ('x', 0), ('y', 0), ('z', 0)):
        mods += mod(GLTF_ROOT_TRANSFORM, f'm_LocalRotation.{axis}', value)
    for axis in 'xyz':
        mods += mod(GLTF_ROOT_TRANSFORM, f'm_LocalEulerAnglesHint.{axis}', 0)
    instance = Doc(1001, fid, '  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Modification:\n    serializedVersion: 3\n'
                            f'    m_TransformParent: {{fileID: {parent_transform}}}\n    m_Modifications:\n{mods}'
                            '    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n'
                            f'    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: {GLTF_MAIN_OBJECT}, guid: {glb_guid}, type: 3}}\n')
    stripped = Doc(4, stripped_id or file_id(), f'  m_CorrespondingSourceObject: {{fileID: {GLTF_ROOT_TRANSFORM}, guid: {glb_guid}, type: 3}}\n'
                                 f'  m_PrefabInstance: {{fileID: {fid}}}\n  m_PrefabAsset: {{fileID: 0}}\n', stripped=True)
    return instance, stripped
