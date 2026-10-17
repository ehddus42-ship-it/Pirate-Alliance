"""Magica Cloth 2 data for everyday Proi (Proi_General): Fixed/Move paint points per cloth and body colliders
(bind pose, Blender world). The battle outfit has its own script (proi_magica_data.py).

Proi_General is one mesh with one connected piece per material, so the cloths are cut by material and position:
  Hair    - the long loose waves (M_ProiG_Hair): the hair over the skull and the fringe over the glasses hold, the
            waves and side locks below move.
  Hem     - the loose sides and back of the oversized hoodie down to the ribbed band; the chest, the zipped front, the
            hood and the sleeves stay put.
            (The drawstrings stay too: the left one is modelled into the front panel at its tip.)
  Pants   - the wide track pant legs between the thighs and the cuffs gathered on the shoes.
Colliders are capsules/spheres along the bones, shrunk until every moving vertex starts outside them. The hair lies on an
oversized hoodie, so its torso colliders follow the hoodie, not the body: a wide Back capsule whose back is the hoodie's
back, and a Hood capsule across the neck for the hood folded behind it (the nape hair running under the hood stays put).

blender -b --python Tools/Proi/proi_general_magica_data.py -- --source Assets/Characters/Proi/Models/Proi_General.fbx \
        --output Assets/Characters/Proi/Magica/ProiGeneralMagicaData.json [--preview <dir>]
"""
import bpy, json, os, sys, argparse
import numpy as np
from mathutils import Vector

parser = argparse.ArgumentParser(description='Build Magica Cloth paint points and colliders for everyday Proi.')
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--preview')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
FIXED, MOVE = 1, 2

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath(args.source))
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = ob.data
n = len(me.vertices)
M = np.array(ob.matrix_world)
P = np.array([v.co for v in me.vertices]) @ M[:3, :3].T + M[:3, 3]          # bind pose
A = np.array(arm.matrix_world)
bone = {b.name: (np.array(A[:3, :3] @ np.array(b.head_local) + A[:3, 3]), np.array(A[:3, :3] @ np.array(b.tail_local) + A[:3, 3]))
        for b in arm.data.bones}
names = [g.name for g in ob.vertex_groups]
dom_name = np.array([names[max(v.groups, key=lambda g: g.weight).group] if len(v.groups) else '' for v in me.vertices])
mat = np.zeros(n, int)
for p in me.polygons:
    for v in p.vertices: mat[v] = p.material_index
mat_names = [m.name for m in me.materials]
def material(part):
    return mat == next(i for i, m in enumerate(mat_names) if m == 'M_ProiG_' + part)
hair_m, jacket_m, pants_m = material('Hair'), material('Jacket'), material('Pants')

def seg(Q, a, b):
    ab = b - a; t = np.clip(((Q - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(Q - (a + t[:, None] * ab), axis=1), t

# ---------- hair ----------
h0, h1 = bone['Head']
head_c = h0 + (h1 - h0) * 0.45
# hair over the skull (down to just below ear level) holds, and so does the fringe in front of the face (it would swing
# through the glasses); the long waves and the side locks below move
scalp = (np.linalg.norm(P - head_c, axis=1) < 0.15) & (P[:, 2] > head_c[2] - 0.03)
fringe = (P[:, 1] < head_c[1] - 0.05) & (np.abs(P[:, 0]) < 0.11) & (P[:, 2] > head_c[2] - 0.12)
# the back of the hoodie, per height: the outer surface behind the spine (the folded hood on top of it near the neck)
neck_z = bone['Neck'][0][2]
def back_surface(z):
    s = jacket_m & (np.abs(P[:, 2] - z) < 0.02) & (np.abs(P[:, 0]) < 0.1) & (P[:, 1] > 0)
    return float(np.percentile(P[s][:, 1], 97)) if s.any() else np.nan
back_z = np.arange(1.22, neck_z + 0.18, 0.02)
back_y = np.array([back_surface(z) for z in back_z])
back_at = lambda z: np.interp(z, back_z[~np.isnan(back_y)], back_y[~np.isnan(back_y)])
# the inner layers at the nape run under the folded hood: they stay with the neck (a hood collider would push them out
# through it)
under_hood = hair_m & (P[:, 2] > neck_z - 0.06) & (P[:, 1] > 0) & (np.abs(P[:, 0]) < 0.15) & (P[:, 1] < back_at(P[:, 2]) - 0.025)
hair = hair_m
hair_attr = np.where(scalp | fringe | under_hood, FIXED, MOVE)

# ---------- hoodie hem ----------
# The loose lower body of the hoodie (below the chest, down to the ribbed band) sways a little; the chest, hood, sleeves
# and the drawstrings stay with the body.
chest_z = bone['Chest'][0][2] + 0.05
hem_member = jacket_m & (P[:, 2] < chest_z + 0.06) & (np.abs(P[:, 0]) < 0.26)
# the zipped front panel stays with the body (swinging back, it would let the thighs stepping forward poke through it);
# the sides and the back sway
hem_attr = np.where(hem_member & (P[:, 2] < chest_z) & (P[:, 1] > -0.06), MOVE, FIXED)

# ---------- track pants ----------
knee_z = min(bone[s + 'LowerLeg'][0][2] for s in ('Left', 'Right'))
ankle_z = max(bone[s + 'Foot'][0][2] for s in ('Left', 'Right'))
shin = np.min([seg(P, *bone[s + 'LowerLeg'])[0] for s in ('Left', 'Right')], axis=0)
# the wide legs below mid-thigh swing; the waist and hips hold, and the cuffs gathered on the shoes follow the legs
pants_top = knee_z + 0.18
cuff = (P[:, 2] < ankle_z + 0.09) & (shin < 0.08)
pants_member = pants_m & (P[:, 2] < pants_top + 0.06)
pants_attr = np.where(pants_member & (P[:, 2] < pants_top) & ~cuff, MOVE, FIXED)

groups = {
    'Hair': (hair, hair_attr),
    'Hem': (hem_member, hem_attr),
    'Pants': (pants_member, pants_attr),
}

def downsample(Q, a, cell=0.004):
    key = np.floor(Q / cell).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.reshape(-1); k = inv.max() + 1
    pos = np.zeros((k, 3)); np.add.at(pos, inv, Q); c = np.bincount(inv, minlength=k); pos /= c[:, None]
    nfix = np.bincount(inv, weights=(a == FIXED).astype(float), minlength=k)
    return pos, np.where(nfix * 2 >= c, FIXED, MOVE)

out = {'space': 'blender_world_meters_bind_pose', 'groups': [], 'colliders': []}
moving = {}
for name, (member, attr) in groups.items():
    pos, att = downsample(P[member], attr[member])
    moving[name] = P[member & (attr == MOVE)]
    out['groups'].append({'name': name, 'vertices': int(member.sum()), 'move': int((member & (attr == MOVE)).sum()),
                          'points': [round(float(x), 5) for p, a in zip(pos, att) for x in (*p, a)]})

# ---------- colliders along bones ----------
CLEAR = 0.008
cloth = np.zeros(n, bool)
for member, attr in groups.values(): cloth |= member & (attr == MOVE)
body = ~hair_m & ~cloth
def fit(spec, users, tolerance=0.002):
    # shrink until at most 0.2 % of the moving vertices start inside: a few inner hair layers resting on the body
    # must not collapse a collider to nothing
    Q = np.concatenate([moving[u] for u in users])
    a, b = np.array(spec['start']), np.array(spec['end'])
    for _ in range(80):
        if spec['type'] == 'sphere': c = np.linalg.norm(Q - a, axis=1) - spec['r0']
        else:
            d, t = seg(Q, a, b); c = d - (spec['r0'] * (1 - t) + spec['r1'] * t)
        if (c < CLEAR).mean() <= tolerance: break
        spec['r0'] *= 0.97; spec['r1'] *= 0.97
    spec['min_clearance'] = float(c.min()); spec['inside_fraction'] = float((c < CLEAR).mean()); spec['used_by'] = users
    spec['limited_by'] = users[int(np.searchsorted(np.cumsum([len(moving[u]) for u in users]), int(np.argmin(c)), side='right'))]
    return spec
def bone_radius(names_, a, b, pct=55, sel_extra=None):
    sel = body & np.isin(dom_name, names_)
    if sel_extra is not None: sel &= sel_extra
    d, _ = seg(P[sel], a, b)
    return float(np.percentile(d, pct)) if sel.any() else 0.05
specs = []
head_skin = material('Head') & (dom_name == 'Head')
r_head = float(np.percentile(np.linalg.norm(P[head_skin] - head_c, axis=1), 10))
specs.append(fit({'name': 'Head', 'bone': 'Head', 'type': 'sphere', 'start': head_c.tolist(), 'end': head_c.tolist(), 'r0': r_head, 'r1': r_head}, ['Hair']))
a, b = bone['Neck']
specs.append(fit({'name': 'Neck', 'bone': 'Neck', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.07, 'r1': 0.07}, ['Hair']))
# the hood folded down behind the neck, across the shoulders: a capsule from side to side whose back is the hood's surface
hood_z = float(neck_z) + 0.05; r = 0.07
hood_y = float(back_at(hood_z)) - r
# (the hair lying on the hood starts on its surface, so a few percent of it starts inside and settles onto it at once)
specs.append(fit({'name': 'Hood', 'bone': 'Neck', 'type': 'capsule', 'start': [-0.12, hood_y, hood_z], 'end': [0.12, hood_y, hood_z], 'r0': r, 'r1': r}, ['Hair'],
                 tolerance=0.04))
# the back of the oversized hoodie (the hair lies on it), not the slim spine inside: two wide capsules side by side whose
# backs are the hoodie's back from the shoulder blades down to the waist
r = 0.11
top, bottom = float(neck_z) - 0.05, float(bone['Chest'][0][2])
for side, x in (('Left', 0.08), ('Right', -0.08)):
    specs.append(fit({'name': 'Back' + side, 'bone': 'UpperChest', 'type': 'capsule', 'start': [x, float(back_at(top)) - r, top],
                      'end': [x, float(back_at(bottom)) - r, bottom], 'r0': r, 'r1': r}, ['Hair'], tolerance=0.01))
# the waist inside the hoodie hem
a, b = bone['Chest']
r = bone_radius(['UpperChest', 'Chest', 'Spine'], a, b, 35, jacket_m)
specs.append(fit({'name': 'Spine', 'bone': 'Chest', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': r, 'r1': r}, ['Hem']))
a, b = bone['Spine']
# the track pants' waist under the ribbed band: the band rests on it and never swings in behind it (the black pants would
# show under the white hoodie)
hem_bottom = float(P[hem_member][:, 2].min())
axis = bone['Hips'][0][:2]
band = pants_m & (P[:, 2] > hem_bottom) & (P[:, 2] < hem_bottom + 0.06)
r = float(np.percentile(np.linalg.norm(P[band][:, :2] - axis, axis=1), 50))
specs.append(fit({'name': 'Waist', 'bone': 'Hips', 'type': 'capsule', 'start': [float(axis[0]), float(axis[1]), hem_bottom - 0.02],
                  'end': [float(axis[0]), float(axis[1]), hem_bottom + 0.12], 'r0': r, 'r1': r * 0.9}, ['Hem'], tolerance=0.05))
for s in ('Left', 'Right'):
    # the hoodie's padded shoulder, out to the upper arm
    a = bone[s + 'Shoulder'][0]; b = bone[s + 'UpperArm'][0] + (bone[s + 'UpperArm'][1] - bone[s + 'UpperArm'][0]) * 0.3
    r = bone_radius([s + 'Shoulder', s + 'UpperArm', 'UpperChest', 'Neck'], a, b, 70, jacket_m)
    specs.append(fit({'name': s + 'Shoulder', 'bone': s + 'Shoulder', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': r, 'r1': r}, ['Hair'],
                     tolerance=0.02))
    for part in ('UpperArm', 'LowerArm'):
        a, b = bone[s + part]; r = bone_radius([s + part], a, b, 50, jacket_m)
        specs.append(fit({'name': s + part, 'bone': s + part, 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': r, 'r1': r * 0.9}, ['Hair']))
    # no leg mesh under the trousers: slim legs along the bones
    a, b = bone[s + 'UpperLeg']
    specs.append(fit({'name': s + 'UpperLeg', 'bone': s + 'UpperLeg', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.075, 'r1': 0.055}, ['Pants']))
    a, b = bone[s + 'LowerLeg']
    specs.append(fit({'name': s + 'LowerLeg', 'bone': s + 'LowerLeg', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.05, 'r1': 0.035}, ['Pants']))
out['colliders'] = specs
# Magica needs at most 65,535 vertices per renderer, and each cloth simulates only its own renderer: the Unity tool
# splits these materials into their own renderers (everything else stays on the original one)
out['split_materials'] = [{'group': 'Hair', 'materials': ['M_ProiG_Hair']},
                          {'group': 'Jacket', 'materials': ['M_ProiG_Jacket']},
                          {'group': 'Pants', 'materials': ['M_ProiG_Pants']}]
# the bind-pose mesh bounds let the Unity tool find the Blender -> mesh-space mapping
out['bounds_min'] = [round(float(x), 5) for x in P.min(0)]
out['bounds_max'] = [round(float(x), 5) for x in P.max(0)]
os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
with open(args.output, 'w', encoding='utf8') as f: json.dump(out, f, separators=(',', ':'))
for g in out['groups']: print('GROUP', g['name'], g['vertices'], 'move', g['move'], 'points', len(g['points']) // 4)
for s in specs: print('COLLIDER', s['name'], s['type'], round(s['r0'], 4), round(s['r1'], 4), 'clear', round(s['min_clearance'], 4), 'inside', round(s['inside_fraction'], 4), 'limited by', s['limited_by'])
print('chest_z', round(chest_z, 3), 'knee_z', round(knee_z, 3), 'ankle_z', round(ankle_z, 3))

if args.preview:
    arm.data.pose_position = 'REST'
    c = np.full((n, 4), 0.3); c[:, 3] = 1
    for name, (member, attr) in groups.items():
        base = {'Hair': (0.9, 0.3, 0.1), 'Hem': (0.1, 0.8, 0.9), 'Pants': (0.6, 0.3, 0.9)}[name]
        c[member & (attr == MOVE), :3] = base
        c[member & (attr == FIXED), :3] = np.array(base) * 0.35
    ca = me.color_attributes.new('magica', 'BYTE_COLOR', 'POINT'); ca.data.foreach_set('color', c.astype(np.float32).ravel())
    me.color_attributes.active_color = ca
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'VERTEX'
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'; scene.render.resolution_x = 700; scene.render.resolution_y = 1000
    for v, p in (('front', (0, -5, 1.0)), ('side', (5, 0, 1.0)), ('back', (0, 5, 1.0))):
        cam.location = p; cam.rotation_euler = (Vector((0, 0, 1.0)) - Vector(p)).to_track_quat('-Z', 'Y').to_euler(); cam.data.ortho_scale = 2.1
        scene.render.filepath = os.path.join(args.preview, f'proi_general_magica_{v}.png'); bpy.ops.render.render(write_still=True)
    # the colliders over the mesh with the hair hidden (upper body, side and back)
    import bmesh
    hair_faces = [p.index for p in me.polygons if mat_names[p.material_index] == 'M_ProiG_Hair']
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in hair_faces], context='FACES'); bm.to_mesh(me); bm.free()
    cmat = bpy.data.materials.new('collider'); cmat.diffuse_color = (1.0, 0.85, 0.1, 1)
    def solid(o):
        o.data.materials.append(cmat); o.color = (1.0, 0.85, 0.1, 1)
    for s in specs:
        a, b = Vector(s['start']), Vector(s['end'])
        for p_, r_ in ((a, s['r0']), (b, s['r1'])):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=r_, location=p_); solid(bpy.context.object)
        if s['type'] == 'capsule' and (b - a).length > 1e-4:
            bpy.ops.mesh.primitive_cone_add(radius1=s['r0'], radius2=s['r1'], depth=(b - a).length, location=(a + b) / 2)
            o = bpy.context.object; o.rotation_euler = (b - a).to_track_quat('Z', 'Y').to_euler(); solid(o)
    scene.display.shading.color_type = 'OBJECT'
    for o in bpy.data.objects:
        if o.type == 'MESH' and o != ob: o.color = (1.0, 0.85, 0.1, 1)
    ob.color = (0.55, 0.6, 0.75, 1)
    for v, p in (('side', (5, 0, 1.45)), ('back', (0, 5, 1.45))):
        cam.location = p; cam.rotation_euler = (Vector((0, 0, 1.45)) - Vector(p)).to_track_quat('-Z', 'Y').to_euler(); cam.data.ortho_scale = 1.0
        scene.render.filepath = os.path.join(args.preview, f'proi_general_colliders_{v}.png'); bpy.ops.render.render(write_still=True)
