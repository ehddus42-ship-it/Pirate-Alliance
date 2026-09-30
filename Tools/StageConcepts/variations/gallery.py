"""Add the variation rooms to the shared room gallery scene without opening Unity.

Mirrors StageConceptGallery.AppendToScene (Assets/StageConcepts/Editor/StageConceptGallery.cs) on
Assets/Liminal/Scenes/LiminalRoomGallery.unity: every room prefab of the four themes gets one prefab instance at
(260 + theme * 52, 0, (index - 1) * 65) inside its theme group, with a sign in front of it; room signs whose title
changed are renamed, the theme and walkway signs show the new room counts, the theme floor grows to cover seven
rows and the Instructions text reports the new total. Nothing outside the THEME VARIATIONS subtree and the
Instructions text is modified (the original liminal rooms are untouched), and running it twice changes nothing.

Usage: python3 gallery.py [--check]   (--check only reports what would change)
"""
import argparse
import pathlib
import random
import re
import sys

import prefabdoc as pd
import unityasset as ua

ROOT = pathlib.Path(__file__).resolve().parents[3]
SCENE = ROOT / 'Assets/Liminal/Scenes/LiminalRoomGallery.unity'
ROOMS = ROOT / 'Assets/StageConcepts/Prefabs/Rooms'
THEME_ROOT = 'THEME VARIATIONS / Forest - Digital - Ruins - Cave'
KEYS = ('Forest', 'Digital', 'Ruins', 'Cave')
TITLES = ('달빛 고목의 숲', '프로그램 감옥', '멸망한 지구', '심연의 수정 동굴')
ROOMS_PER_THEME = 7
ORIGINAL_ROOMS = 20


def decode(value):
    """Unity YAML scalar (plain or double-quoted, possibly folded over indented lines) -> str."""
    value = value.strip()
    if not value.startswith('"'):
        return re.sub(r'\s*\n\s*', ' ', value)
    body = value[1:-1]
    body = re.sub(r'[ \t]*\n[ \t]*', ' ', body)  # a folded line break reads as one space
    return re.sub(r'\\(u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2}|["\\/nt])', lambda m: {
        'n': '\n', 't': '\t', '"': '"', '\\': '\\', '/': '/'}.get(m.group(1)) or chr(int(m.group(1)[1:], 16)), body)


def field_span(doc, name):
    """(start, end) of a top-level field value, continuation lines included."""
    m = re.search(r'^  ' + re.escape(name) + r': (.*(?:\n    .*)*)', doc.body, re.M)
    return m


def get(doc, name):
    m = field_span(doc, name)
    return decode(m.group(1)) if m else None


def put(doc, name, value):
    m = field_span(doc, name)
    doc.body = doc.body[:m.start(1)] + ua.yaml_string(value) + doc.body[m.end(1):]


def set_vec(doc, name, value):
    doc.body = re.sub(r'^  %s: \{[^}]*\}' % re.escape(name), lambda m: f'  {name}: {ua.vec3(value)}', doc.body, count=1, flags=re.M)


def vec_of(doc, name):
    values = dict(re.findall(r'(\w+): (-?[0-9.eE+-]+)', doc.field(name)))
    return tuple(float(values[k]) for k in 'xyz')


class Scene:
    def __init__(self, text):
        self.p = pd.Prefab(text)
        self.rng = random.Random(0x5EED)
        self.taken = set(self.p.by_id)

    def new_id(self):
        while True:
            value = self.rng.randrange(100_000_000, 2_147_483_647)
            if value not in self.taken:
                self.taken.add(value)
                return value

    def go(self, transform):
        return self.p.by_id[transform.ref('m_GameObject')]

    def name(self, transform):
        return get(self.go(transform), 'm_Name')

    def child(self, transform, name):
        for c in self.p.children(transform):
            t = self.p.by_id[c]
            if not t.stripped and self.name(t) == name:
                return t
        return None

    def subtree(self, transform, out=None):
        out = [] if out is None else out
        go = self.go(transform)
        out.append(go)
        for m in re.finditer(r'component: \{fileID: (-?\d+)\}', go.body):
            out.append(self.p.by_id[int(m.group(1))])
        for c in self.p.children(transform):
            self.subtree(self.p.by_id[c], out)
        return out

    def label(self, sign):
        return next(d for d in self.subtree(sign) if d.cls == 102)


def room_prefab(room_id):
    path = ROOMS / f'{room_id}.prefab'
    if not path.exists():
        return None
    prefab = pd.Prefab(path.read_text(encoding='utf-8'))
    root_go = next(g for g in prefab.game_objects(room_id) if prefab.transform_of(g).ref('m_Father') == 0)
    behaviour = next(d for d in prefab.docs if d.cls == 114 and '  roomId: ' in d.body)
    return {'guid': ua.read_guid(pathlib.Path(str(path) + '.meta')), 'go': root_go.fid,
            'transform': prefab.transform_of(root_go).fid, 'title': get(behaviour, 'displayName')}


def instance_docs(scene, template, parent, room_id, info, position):
    """PrefabInstance + stripped root Transform, modelled on an existing theme-room instance."""
    fid = scene.new_id()
    stripped_id = fid + 1 if fid + 1 not in scene.taken else scene.new_id()
    scene.taken.add(stripped_id)
    body = template.body
    old_guid = re.search(r'm_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]+)', body).group(1)
    old_transform = int(re.findall(r'- target: \{fileID: (-?\d+), guid: [0-9a-f]+, type: 3\}\n      propertyPath: m_LocalPosition\.x', body)[0])
    old_go = int(re.findall(r'- target: \{fileID: (-?\d+), guid: [0-9a-f]+, type: 3\}\n      propertyPath: m_Name', body)[0])
    body = body.replace(f'fileID: {old_transform}, guid: {old_guid}', f'fileID: {info["transform"]}, guid: {info["guid"]}')
    body = body.replace(f'fileID: {old_go}, guid: {old_guid}', f'fileID: {info["go"]}, guid: {info["guid"]}')
    body = body.replace(f'guid: {old_guid}', f'guid: {info["guid"]}')
    body = re.sub(r'    m_TransformParent: \{fileID: -?\d+\}', f'    m_TransformParent: {{fileID: {parent.fid}}}', body, count=1)
    for axis, value in zip('xyz', position):
        body = re.sub(r'(propertyPath: m_LocalPosition\.%s\n      value: )[^\n]*' % axis, lambda m: m.group(1) + ua.f32(value), body, count=1)
    body = re.sub(r'(propertyPath: m_Name\n      value: )[^\n]*', lambda m: m.group(1) + room_id, body, count=1)
    assert old_guid not in body
    instance = pd.Doc(1001, fid, body)
    stripped = pd.Doc(4, stripped_id, 'Transform:\n'
                      f'  m_CorrespondingSourceObject: {{fileID: {info["transform"]}, guid: {info["guid"]}, type: 3}}\n'
                      f'  m_PrefabInstance: {{fileID: {fid}}}\n  m_PrefabAsset: {{fileID: 0}}\n', stripped=True)
    return instance, stripped


def clone_sign(scene, template_sign, parent, text, position):
    docs = scene.subtree(template_sign)
    mapping = {d.fid: scene.new_id() for d in docs}
    clones = []
    for d in docs:
        body = re.sub(r'\{fileID: (-?\d+)\}', lambda m: '{fileID: %d}' % mapping.get(int(m.group(1)), int(m.group(1))), d.body)
        clones.append(pd.Doc(d.cls, mapping[d.fid], body, d.stripped))
    root_transform = next(c for c in clones if c.cls == 4 and c.fid == mapping[template_sign.fid])
    root_go = next(c for c in clones if c.cls == 1 and c.fid == mapping[scene.go(template_sign).fid])
    root_transform.body = re.sub(r'  m_Father: \{fileID: -?\d+\}', f'  m_Father: {{fileID: {parent.fid}}}', root_transform.body, count=1)
    set_vec(root_transform, 'm_LocalPosition', position)
    put(root_go, 'm_Name', 'Gallery sign / ' + text)
    put(next(c for c in clones if c.cls == 102), 'm_Text', text)
    return root_transform, clones


def rename_sign(scene, sign, text):
    put(scene.go(sign), 'm_Name', 'Gallery sign / ' + text)
    put(scene.label(sign), 'm_Text', text)


def update(text):
    scene = Scene(text)
    p = scene.p
    changes = []
    root_go = next(g for g in p.docs if g.cls == 1 and get(g, 'm_Name') == THEME_ROOT)
    root = p.transform_of(root_go)
    walkway = scene.child(root, 'Connecting walkways')
    count = ROOMS_PER_THEME
    added = 0
    for theme, key in enumerate(KEYS):
        group = scene.child(root, f'{theme + 1:02d} {key} / {TITLES[theme]}')
        children = p.children(group)
        instances = {}
        signs = {}
        for c in children:
            t = p.by_id[c]
            if t.stripped:
                inst = p.by_id[t.ref('m_PrefabInstance')]
                guid = re.search(r'm_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]+)', inst.body).group(1)
                instances[guid] = (t, inst)
            else:
                signs[scene.name(t)] = t
        template_inst = next(iter(instances.values()))[1]
        template_sign = next(t for n, t in signs.items() if re.match(r'Gallery sign / \w+_\d\d / ', n))
        new_children = list(children)
        theme_sign_name = next((n for n in signs if n.startswith(f'Gallery sign / {TITLES[theme]} / ')), None)
        for index in range(1, count + 1):
            room_id = f'{key}_{index:02d}'
            info = room_prefab(room_id)
            if not info:
                continue
            position = (260 + theme * 52, 0, (index - 1) * 65)
            sign_text = f'{room_id} / {info["title"]}'
            if info['guid'] not in instances:
                instance, stripped = instance_docs(scene, template_inst, group, room_id, info, position)
                p.add(instance, stripped)
                sign, docs = clone_sign(scene, template_sign, group, sign_text, (position[0], 1.4, position[2] - 4))
                p.add(*docs)
                at = new_children.index(signs[theme_sign_name].fid) if theme_sign_name else len(new_children)
                new_children[at:at] = [stripped.fid, sign.fid]
                added += 1
                changes.append(f'added {room_id} at {position} with sign "{sign_text}"')
                continue
            current = [n for n in signs if n.startswith(f'Gallery sign / {room_id} / ')]
            for n in current:
                if n != 'Gallery sign / ' + sign_text:
                    rename_sign(scene, signs[n], sign_text)
                    changes.append(f'renamed "{n}" -> "{sign_text}"')
        p.set_children(group, new_children)
        if theme_sign_name:
            wanted = f'{TITLES[theme]} / {count}개 바리에이션'
            if theme_sign_name != 'Gallery sign / ' + wanted:
                rename_sign(scene, signs[theme_sign_name], wanted)
                changes.append(f'renamed "{theme_sign_name}" -> "{wanted}"')
    total = len(KEYS) * count
    for c in p.children(walkway):
        t = p.by_id[c]
        n = scene.name(t)
        if n.startswith('Gallery sign / 기존 맵 20개'):
            wanted = f'기존 맵 {ORIGINAL_ROOMS}개  <  |  >  신규 테마 {total}개'
            if n != 'Gallery sign / ' + wanted:
                rename_sign(scene, t, wanted)
                changes.append(f'renamed "{n}" -> "{wanted}"')
        if n == 'Theme gallery floor':
            floor_end = (count - 1) * 65 + 48.5
            position = vec_of(t, 'm_LocalPosition')
            scale = vec_of(t, 'm_LocalScale')
            want_p = (position[0], position[1], (floor_end - 14.5) / 2)
            want_s = (scale[0], scale[1], floor_end + 14.5)
            if (position, scale) != (want_p, want_s):
                set_vec(t, 'm_LocalPosition', want_p)
                set_vec(t, 'm_LocalScale', want_s)
                changes.append(f'theme floor z {position[2]:g}/{scale[2]:g} -> {want_p[2]:g}/{want_s[2]:g}')
    if added:
        instructions = next(g for g in p.docs if g.cls == 1 and get(g, 'm_Name') == 'Instructions')
        ui = next(d for d in p.docs if d.cls == 114 and f'm_GameObject: {{fileID: {instructions.fid}}}' in d.body and 'm_Text:' in d.body)
        old = get(ui, 'm_Text')
        m = re.search(r'총 (\d+)개 방', old)
        new = old.replace(m.group(0), f'총 {int(m.group(1)) + added}개 방')
        put(ui, 'm_Text', new)
        changes.append(f'instructions: {m.group(0)} -> 총 {int(m.group(1)) + added}개 방')
    p.docs.sort(key=lambda d: d.fid)  # Unity keeps scene documents ordered by fileID
    return p.text(), changes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    text = SCENE.read_text(encoding='utf-8')
    new_text, changes = update(text)
    for c in changes:
        print(c)
    if not changes:
        print('gallery already up to date')
    elif not args.check:
        ua.write_text(SCENE, new_text)
        print(f'wrote {SCENE.relative_to(ROOT)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())


def verify(text=None):
    """Structural checks of the saved gallery: local references resolve, parent/child lists agree, every theme
    room prefab has exactly one instance whose targets exist in the prefab, and room footprints do not overlap."""
    text = text or SCENE.read_text(encoding='utf-8')
    p = pd.Prefab(text)
    problems = []
    ids = set(p.by_id)
    for d in p.docs:
        for m in re.finditer(r'\{fileID: (-?\d+)\}', d.body):
            ref = int(m.group(1))
            if ref and ref not in ids:
                problems.append(f'{d.cls} &{d.fid}: dangling reference {ref}')
    for d in p.docs:
        if d.cls != 4 or d.stripped:
            continue
        for c in p.children(d):
            child = p.by_id.get(c)
            if child is None:
                continue
            father = child.ref('m_Father') if not child.stripped else None
            if child.stripped:
                inst = p.by_id[child.ref('m_PrefabInstance')]
                father = int(re.search(r'm_TransformParent: \{fileID: (-?\d+)\}', inst.body).group(1))
            if father != d.fid:
                problems.append(f'transform {c} listed under {d.fid} but its parent is {father}')
    guids = {}
    for key in KEYS:
        for index in range(1, ROOMS_PER_THEME + 1):
            info = room_prefab(f'{key}_{index:02d}')
            if info:
                guids[info['guid']] = (f'{key}_{index:02d}', info)
    found = {}
    for d in p.docs:
        if d.cls != 1001:
            continue
        guid = re.search(r'm_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]+)', d.body).group(1)
        if guid not in guids:
            continue
        room_id, info = guids[guid]
        found.setdefault(room_id, []).append(d)
        targets = {int(t) for t in re.findall(r'target: \{fileID: (-?\d+), guid: ' + guid, d.body)}
        if not targets <= {info['go'], info['transform']}:
            problems.append(f'{room_id}: modification targets {targets} not in the prefab root')
        stripped = [s for s in p.docs if s.stripped and f'm_PrefabInstance: {{fileID: {d.fid}}}' in s.body]
        if len(stripped) != 1:
            problems.append(f'{room_id}: expected one stripped root transform, found {len(stripped)}')
    for room_id, _ in guids.values():
        if len(found.get(room_id, [])) != 1:
            problems.append(f'{room_id}: {len(found.get(room_id, []))} gallery instances')
    positions = []
    for room_id, docs in found.items():
        body = docs[0].body
        pos = [float(re.search(r'propertyPath: m_LocalPosition\.%s\n      value: ([-0-9.e]+)' % a, body).group(1)) for a in 'xz']
        positions.append((room_id, pos))
    for i, (a, pa) in enumerate(positions):
        for b, pb in positions[:i]:
            if abs(pa[0] - pb[0]) < 26 and abs(pa[1] - pb[1]) < 36.4:
                problems.append(f'{a} overlaps {b}')
    return {'theme_rooms': sum(len(v) for v in found.values()), 'problems': problems}
