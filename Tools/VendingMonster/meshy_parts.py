"""Reproducible Meshy limb generation. API key is read only from the environment."""
import argparse, base64, json, os, pathlib, urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Liminal/Art/Meshy/vending_monster_parts'
OUT.mkdir(parents=True, exist_ok=True)
BASE = 'https://api.meshy.ai/openapi'
PROMPTS = {
 'arm': 'Single isolated extremely long gaunt humanoid RIGHT ARM game creature attachment, shoulder to five fingertips only. No torso, no head, no legs, no base. Arm hangs vertically down, shoulder at top, elbow nearly straight with slight bend, wrist straight, palm facing forward, five long separated straight relaxed fingers pointing down with clearly separated thumb. Long thin upper arm and longer forearm, sinewy tendon ridges, oversized narrow hand, subtly knobby elbow. Ash-grey desiccated rubbery skin, subtle veins and wrinkles, uncanny liminal horror, no blood or wounds, sealed rounded shoulder end. Anatomically coherent joints for skeletal animation. All fingers fully separated and uncurled, neutral rigging pose. Realistic clean continuous watertight surface, not skeletal bones.',
 'leg': 'Single isolated elongated gaunt humanoid RIGHT LEG creature attachment, upper thigh to bare foot only. No pelvis, no torso, no head, no arms, no second leg, no base. Vertical straight standing neutral rigging pose, thigh top above knee above ankle, knee very slightly bent, entire sole flat horizontal, long narrow bare foot extends forward with five toes. Slender sinewy thigh, pronounced knee, long thin shin, visible Achilles tendon, ash-grey desiccated rubbery skin with subtle veins and wrinkles. Same narrow gaunt organic anatomy as a liminal horror creature. Sealed rounded upper thigh end, no wounds, no blood, no clothes, no shoes. Anatomically coherent ankle and knee for skeletal animation. Complete clean continuous watertight 3D limb.'
}

def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data, headers={
        'Authorization': 'Bearer ' + os.environ['MESHY_API_KEY'],
        'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=90) as response:
        return json.load(response)

def save(path, obj):
    path.write_text(json.dumps(obj, indent=2), encoding='utf-8')

def main():
    p = argparse.ArgumentParser(); p.add_argument('action', choices=['balance','start','status','refine','download','image-start','image-status','image-download'])
    p.add_argument('--part', choices=list(PROMPTS)); args = p.parse_args()
    if args.action == 'balance': print(json.dumps(api('/v1/balance'))); return
    for key in ([args.part] if args.part else PROMPTS):
        folder = OUT / key; folder.mkdir(exist_ok=True)
        record = folder / 'provenance.json'
        state = json.loads(record.read_text()) if record.exists() else {'provider':'Meshy', 'prompt':PROMPTS[key]}
        if args.action.startswith('image-'):
            if args.action == 'image-start':
                if 'image_id' in state: print(key, 'already submitted'); continue
                payload = dict(image_url='data:image/png;base64,' + base64.b64encode((folder/'reference.png').read_bytes()).decode(),
                               ai_model='meshy-7.1', geometry_resolution='4k', should_remesh=True, topology='quad',
                               target_polycount=24000, should_texture=True, enable_pbr=True, texture_resolution='4k', target_formats=['glb'])
                save(folder/'image_request.json', {**payload, 'image_url':'reference.png (data URI sent)'})
                result = api('/v1/image-to-3d', payload)
                state['image_id'] = result['result']; state['selected_pipeline'] = 'image-to-3d'
                state['text_preview_rejected'] = 'Extra hand / unwanted torso; replaced by explicit isolated-limb reference.'
                save(record, state); print(key, 'image task', result['result'], flush=True)
            else:
                result = api('/v1/image-to-3d/' + state['image_id']); save(folder/'image_result.json', result)
                print(key, result['status'], result.get('progress'), 'credits', result.get('consumed_credits'), flush=True)
                if args.action == 'image-download' and result['status'] == 'SUCCEEDED':
                    for suffix,url in [('glb',result['model_urls']['glb']),('png',result.get('thumbnail_url'))]:
                        if url:
                            dest=folder/(key+'_final.'+suffix)
                            if not dest.exists(): urllib.request.urlretrieve(url,dest)
                            print('saved',dest,dest.stat().st_size,flush=True)
                state['image_status']=result['status'];state['image_credits']=result.get('consumed_credits',0);save(record,state)
            continue
        if args.action == 'start':
            if 'preview_id' in state: print(key, 'already submitted'); continue
            payload = dict(mode='preview', prompt=PROMPTS[key], ai_model='meshy-7.1', geometry_resolution='4k',
                           should_remesh=True, topology='quad', target_polycount=25000, target_formats=['glb'])
            save(folder/'preview_request.json', payload)
            result = api('/v2/text-to-3d', payload)
            state['preview_id'] = result['result']; save(record, state)
            print(key, 'preview submitted', result['result'], flush=True)
        elif args.action == 'refine':
            if 'refine_id' in state: print(key, 'already refined'); continue
            preview = api('/v2/text-to-3d/' + state['preview_id'])
            if preview['status'] != 'SUCCEEDED': print(key, preview['status']); continue
            payload = dict(mode='refine', preview_task_id=state['preview_id'], enable_pbr=True,
                           texture_resolution='4k', target_formats=['glb'],
                           texture_prompt='Ash grey taupe leathery skin, fine tendon relief and wrinkles, subtle mottling, dark fingernails or toenails, nonmetallic dry skin, realistic horror prop. No blood, no wounds, no clothing.')
            save(folder/'refine_request.json', payload)
            result = api('/v2/text-to-3d', payload)
            state['refine_id'] = result['result']; save(record, state)
            print(key, 'refine submitted', result['result'], flush=True)
        else:
            phase = 'refine' if 'refine_id' in state else 'preview'
            result = api('/v2/text-to-3d/' + state[phase+'_id'])
            save(folder/(phase+'_result.json'), result)
            print(key, phase, result['status'], result.get('progress'), 'credits', result.get('consumed_credits'), flush=True)
            if args.action == 'download' and result['status'] == 'SUCCEEDED':
                for suffix, url in [('glb', result['model_urls']['glb']), ('png', result.get('thumbnail_url'))]:
                    if not url: continue
                    dest = folder/(key+'_'+phase+'.'+suffix)
                    if not dest.exists(): urllib.request.urlretrieve(url, dest)
                    print('saved', str(dest), dest.stat().st_size, flush=True)
            state[phase+'_status'] = result['status']
            state[phase+'_credits'] = result.get('consumed_credits', 0); save(record, state)

if __name__ == '__main__': main()
