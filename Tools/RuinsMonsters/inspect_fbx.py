"""Inspect generated FBX skeleton, animation clips and texture embedding with Blender."""
from pathlib import Path
import bpy
import json
import sys

ROOT = Path(__file__).resolve().parents[2]
key = sys.argv[sys.argv.index('--') + 1]
folder = ROOT / 'Assets/RuinsMonsters/Art/Meshy' / key
result = {}
for suffix in ('rig', 'motions', 'walking', 'running'):
    path = folder / (key + '_' + suffix + '.fbx')
    if not path.exists():
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_image_search=False)
    result[suffix] = dict(
        mesh_count=sum(o.type == 'MESH' for o in bpy.data.objects),
        bones=[b.name for a in bpy.data.armatures for b in a.bones],
        actions=[dict(name=a.name, frame_range=list(a.frame_range)) for a in bpy.data.actions],
        images=[dict(name=i.name, file=i.filepath, packed=bool(i.packed_file), size=list(i.size)) for i in bpy.data.images],
        materials=[dict(name=m.name, nodes=[dict(type=n.type, image=n.image.name if getattr(n,'image',None) else None) for n in m.node_tree.nodes]) for m in bpy.data.materials if m.use_nodes])
out = ROOT / 'Tools/RuinsMonsters/Source' / key / 'fbx_inspection.json'
out.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2), flush=True)
