"""Magica Cloth 2 data for Proi: Fixed/Move paint points per cloth and body colliders (bind pose, Blender world).

Cloths (all on the single Proi_Body mesh):
  Hair    - the red hair pieces; scalp and the twin-tail ties stay fixed, lengths move.
  Tatters - the thin torn strips hanging from the belt (thin-sheet test: a ray into the surface meets the back face
            within a few centimetres); thick belt/pants above the knees next to them is the fixed anchor.
  Pants   - the wide balloon legs between the straps around the knees and the cuff gathered on the boot, with the
            straps and loose strip ends lying on them.
Colliders are capsules/spheres along the bones, shrunk until every moving vertex starts outside them.

blender -b --python Tools/Proi/proi_magica_data.py -- --source Assets/Characters/Proi/Models/Proi_Character.fbx \
        --output Assets/Characters/Proi/Magica/ProiMagicaData.json [--preview <dir>]
"""
import bpy, json, os, sys, argparse
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

parser = argparse.ArgumentParser(description='Build Magica Cloth paint points and colliders for Proi.')
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
N = np.array([v.normal for v in me.vertices]) @ M[:3, :3].T
N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
A = np.array(arm.matrix_world)
bone = {b.name: (np.array(A[:3, :3] @ np.array(b.head_local) + A[:3, 3]), np.array(A[:3, :3] @ np.array(b.tail_local) + A[:3, 3]))
        for b in arm.data.bones}

# dominant bone, texture colour, islands
names = [g.name for g in ob.vertex_groups]
dom = np.full(n, -1); domw = np.zeros(n)
for v in me.vertices:
    for g in v.groups:
        if g.weight > domw[v.index]: domw[v.index], dom[v.index] = g.weight, g.group
dom_name = np.array([names[d] if d >= 0 else '' for d in dom])
tex_dir = os.path.join(os.path.dirname(os.path.abspath(args.source)), '..', 'Textures')
images = {}
for mi, m in enumerate(me.materials):
    im = bpy.data.images.load(os.path.join(tex_dir, m.name.replace('M_', '') + '_Color.png'))
    w, h = im.size; images[mi] = (np.array(im.pixels[:]).reshape(h, w, 4), w, h)
uv = np.zeros(len(me.loops) * 2); me.uv_layers.active.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
col = np.zeros((n, 3)); cnt = np.zeros(n); mat = np.zeros(n, int)
for p in me.polygons:
    px, w, h = images[p.material_index]
    for li in p.loop_indices:
        vi = me.loops[li].vertex_index; u, v = uv[li]
        col[vi] += px[int((v % 1) * (h - 1)), int((u % 1) * (w - 1)), :3]; cnt[vi] += 1; mat[vi] = p.material_index
col /= np.maximum(cnt, 1)[:, None]
parent = np.arange(n)
def find(a):
    while parent[a] != a:
        parent[a] = parent[parent[a]]; a = parent[a]
    return a
for p in me.polygons:
    vs = list(p.vertices)
    for v in vs[1:]:
        ra, rb = find(vs[0]), find(v)
        if ra != rb: parent[rb] = ra
_, isl = np.unique([find(i) for i in range(n)], return_inverse=True)
size = np.bincount(isl)
char_mat = [i for i, m in enumerate(me.materials) if m.name == 'M_Proi_Char'][0]
red = (col[:, 0] > 0.35) & (col[:, 0] > 1.6 * col[:, 1])
isl_red = np.array([red[isl == k].mean() for k in range(isl.max() + 1)])
isl_mat = np.array([mat[isl == k][0] for k in range(isl.max() + 1)])
isl_minz = np.array([P[isl == k][:, 2].min() for k in range(isl.max() + 1)])

# ---------- hair ----------
hair_isl = [k for k in range(len(size)) if isl_mat[k] == char_mat and isl_red[k] > 0.6 and size[k] > 500]
hair = np.isin(isl, hair_isl)
h0, h1 = bone['Head']
head_c = h0 + (h1 - h0) * 0.45
ties = [bone['LeftTail1'][0], bone['RightTail1'][0]]
# hair over the skull (down to just below ear level) holds; side locks and tails below move
scalp = (np.linalg.norm(P - head_c, axis=1) < 0.15) & (P[:, 2] > head_c[2] - 0.03)
tie = np.min([np.linalg.norm(P - t, axis=1) for t in ties], axis=0) < 0.05
hair_attr = np.where(scalp | tie, FIXED, MOVE)

# ---------- tatters / pants (the outfit piece reaching the ankles) ----------
pants_isl = max((k for k in range(len(size)) if isl_mat[k] != char_mat and isl_minz[k] < 0.3 and size[k] > 3000), key=lambda k: size[k])
outfit = isl == pants_isl
idx = np.where(outfit)[0]
tris = [[ob.data.loops[i].vertex_index for i in p.loop_indices] for p in me.polygons]
bvh = BVHTree.FromPolygons([Vector(p) for p in P], tris)
thick = np.full(n, 1.0); backed = np.zeros(n, bool)
for i in idx:
    d = Vector(-N[i]); hit = bvh.ray_cast(Vector(P[i]) + d * 1e-4, d, 0.2)
    if hit[0] is None: continue
    thick[i] = hit[3]
    # straps are thin too, but another surface (the trousers) lies right behind them
    behind = bvh.ray_cast(hit[0] + d * 1e-4, d, 0.02)
    backed[i] = behind[0] is not None
thin = outfit & (thick < 0.018)
belt_z = P[outfit & (dom_name == 'Spine')][:, 2].min() if (outfit & (dom_name == 'Spine')).any() else 0.95
knee = {s: bone[s + 'LowerLeg'][0] for s in ('Left', 'Right')}
ankle = {s: bone[s + 'Foot'][0] for s in ('Left', 'Right')}
knee_z = min(k[2] for k in knee.values()); ankle_z = max(a[2] for a in ankle.values())
# the wide balloon below the straps around the knees swings; the strap band (knee +- 3.5 cm) holds it
pants_top = knee_z - 0.035
def seg_dist(Q, a, b):
    ab = b - a; t = np.clip(((Q - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(Q - (a + t[:, None] * ab), axis=1)
shin = np.min([seg_dist(P, *bone[s + 'LowerLeg']) for s in ('Left', 'Right')], axis=0)

# thin strips below the belt move; the thick surface they hang from (within 2.5 cm) anchors them - but only above the
# swinging balloon, which cannot hold a strip still
strip = thin & ~backed & (P[:, 2] < belt_z + 0.02)
sp = np.arange(n)
def sfind(a):
    while sp[a] != a:
        sp[a] = sp[sp[a]]; a = sp[a]
    return a
for p in me.polygons:
    vs = [v for v in p.vertices if strip[v]]
    for v in vs[1:]:
        ra, rb = sfind(vs[0]), sfind(v)
        if ra != rb: sp[rb] = ra
piece = np.array([sfind(i) if strip[i] else -1 for i in range(n)])
kd = KDTree(int(strip.sum()))
for j, i in enumerate(np.where(strip)[0]): kd.insert(Vector(P[i]), j)
kd.balance()
anchor = np.zeros(n, bool)
for i in idx:
    if not strip[i] and P[i, 2] >= pants_top and kd.find(Vector(P[i]))[2] < 0.025: anchor[i] = True
kda = KDTree(int(anchor.sum()))
for j, i in enumerate(np.where(anchor)[0]): kda.insert(Vector(P[i]), j)
kda.balance()
held = strip & (P[:, 2] >= belt_z - 0.03)
for i in np.where(strip)[0]:
    if kda.find(Vector(P[i]))[2] < 0.025: held[i] = True
held_piece = set(piece[held])
loose = strip & ~np.isin(piece, list(held_piece))
# loose strips lying on the balloon (a strap's dangling end) ride on the trousers; any other loose piece hangs from its top
on_pants = np.zeros(n, bool); hang_top = np.zeros(n, bool)
for k in set(piece[loose]):
    m = piece == k
    if P[m, 2].max() < pants_top + 0.05: on_pants |= m
    else: hang_top |= m & (P[:, 2] > P[m, 2].max() - 0.01)
tatter_member = (strip & ~on_pants) | anchor
tatter_attr = np.where(strip & (P[:, 2] < belt_z - 0.03) & ~hang_top, MOVE, FIXED)

# trousers: the thick cloth and the straps lying on it, plus the loose strips riding on it; the cuff gathered on the boot
# follows the leg
pants_member = (outfit & (~thin | backed) & (P[:, 2] < knee_z + 0.035)) | on_pants
cuff = (P[:, 2] < ankle_z + 0.13) & (shin < 0.07)
balloon = pants_member & (P[:, 2] < pants_top) & ~cuff
pants_attr = np.where(balloon, MOVE, FIXED)
print('strip pieces', len(set(piece[strip])), 'held', len(held_piece), 'on pants', len(set(piece[on_pants])),
      'hanging from their top', len(set(piece[hang_top])), 'balloon', int(balloon.sum()), 'cuff', int((pants_member & cuff).sum()))

groups = {
    'Hair': (hair, hair_attr),
    'Tatters': (tatter_member, tatter_attr),
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
body = ~hair & ~(tatter_member | pants_member)
def seg(Q, a, b):
    ab = b - a; t = np.clip(((Q - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(Q - (a + t[:, None] * ab), axis=1), t
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
def bone_radius(names_, a, b, pct=55):
    sel = body & np.isin(dom_name, names_)
    d, _ = seg(P[sel], a, b)
    return float(np.percentile(d, pct)) if sel.any() else 0.05
specs = []
r_head = float(np.percentile(np.linalg.norm(P[body & (dom_name == 'Head')] - head_c, axis=1), 10))
specs.append(fit({'name': 'Head', 'bone': 'Head', 'type': 'sphere', 'start': head_c.tolist(), 'end': head_c.tolist(), 'r0': r_head, 'r1': r_head}, ['Hair']))
for nm, bn, users in [('Neck', 'Neck', ['Hair']), ('Chest', 'UpperChest', ['Hair'])]:
    a, b = bone[bn]; r = bone_radius([bn, 'Chest'] if bn == 'UpperChest' else [bn], a, b)
    specs.append(fit({'name': nm, 'bone': bn, 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': r, 'r1': r}, users))
# waist: the belt would give ~17 cm, which swallows the roots of the strips hanging from it
a, b = bone['Spine']
specs.append(fit({'name': 'Spine', 'bone': 'Spine', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.13, 'r1': 0.13}, ['Hair', 'Tatters'], tolerance=0.0))
for s in ('Left', 'Right'):
    for part, users in [('UpperArm', ['Hair']), ('LowerArm', ['Hair'])]:
        a, b = bone[s + part]; r = bone_radius([s + part], a, b, 50)
        specs.append(fit({'name': s + part, 'bone': s + part, 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': r, 'r1': r * 0.9}, users))
    # no leg mesh under the trousers: slim legs along the bones
    a, b = bone[s + 'UpperLeg']
    specs.append(fit({'name': s + 'UpperLeg', 'bone': s + 'UpperLeg', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.075, 'r1': 0.055}, ['Hair', 'Tatters', 'Pants']))
    a, b = bone[s + 'LowerLeg']
    specs.append(fit({'name': s + 'LowerLeg', 'bone': s + 'LowerLeg', 'type': 'capsule', 'start': a.tolist(), 'end': b.tolist(), 'r0': 0.05, 'r1': 0.035}, ['Tatters', 'Pants']))
out['colliders'] = specs
# pieces the Unity tool splits into their own renderers (Magica needs at most 65,535 vertices per renderer)
out['split'] = [{'group': 'Hair', 'count': int(size[k]), 'centroid': P[isl == k].mean(0).round(5).tolist()} for k in hair_isl] +                [{'group': 'Lower', 'count': int(size[pants_isl]), 'centroid': P[isl == pants_isl].mean(0).round(5).tolist()}]
# the bind-pose mesh bounds let the Unity tool find the Blender -> mesh-space mapping
out['bounds_min'] = [round(float(x), 5) for x in P.min(0)]
out['bounds_max'] = [round(float(x), 5) for x in P.max(0)]
os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
with open(args.output, 'w', encoding='utf8') as f: json.dump(out, f, separators=(',', ':'))
for g in out['groups']: print('GROUP', g['name'], g['vertices'], 'move', g['move'], 'points', len(g['points']) // 4)
for s in specs: print('COLLIDER', s['name'], s['type'], round(s['r0'], 4), round(s['r1'], 4), 'clear', round(s['min_clearance'], 4), 'inside', round(s['inside_fraction'], 4), 'limited by', s['limited_by'])
print('belt_z', round(belt_z, 3), 'knee_z', round(knee_z, 3), 'ankle_z', round(ankle_z, 3), 'thin', int(thin.sum()))

if args.preview:
    for pb in arm.pose.bones: pb.matrix_basis.identity()
    if arm.animation_data: arm.animation_data_clear()
    c = np.full((n, 4), 0.3); c[:, 3] = 1
    for name, (member, attr) in groups.items():
        base = {'Hair': (0.9, 0.3, 0.1), 'Tatters': (0.1, 0.8, 0.9), 'Pants': (0.6, 0.3, 0.9)}[name]
        c[member & (attr == MOVE), :3] = base
        c[member & (attr == FIXED), :3] = np.array(base) * 0.35
    ca = me.color_attributes.new('magica', 'BYTE_COLOR', 'POINT'); ca.data.foreach_set('color', c.astype(np.float32).ravel())
    me.color_attributes.active_color = ca
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'VERTEX'
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'; scene.render.resolution_x = 700; scene.render.resolution_y = 1000
    for v, p in (('front', (0, -5, 1.0)), ('side', (5, 0, 1.0)), ('back', (0, 5, 1.0))):
        cam.location = p; cam.rotation_euler = (Vector((0, 0, 1.0)) - Vector(p)).to_track_quat('-Z', 'Y').to_euler(); cam.data.ortho_scale = 2.2
        scene.render.filepath = os.path.join(args.preview, f'proi_magica_{v}.png'); bpy.ops.render.render(write_still=True)
