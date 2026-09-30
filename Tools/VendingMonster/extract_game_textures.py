import json,struct,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Liminal/Art/VendingMonster/Textures';OUT.mkdir(parents=True,exist_ok=True)
manifest=[]
for part in ['arm','leg','cabinet']:
    path=ROOT/'Tools/VendingMonster/Source/GameMeshes'/(part+'_game.glb');raw=path.read_bytes()
    size,kind=struct.unpack_from('<II',raw,12);doc=json.loads(raw[20:20+size]);offset=20+size;bs,bk=struct.unpack_from('<II',raw,offset);data=raw[offset+8:offset+8+bs]
    mat=doc['materials'][0];pbr=mat.get('pbrMetallicRoughness',{})
    roles={'baseColor':pbr.get('baseColorTexture'),'normal':mat.get('normalTexture')}
    if part=='cabinet':roles['metallicRoughness']=pbr.get('metallicRoughnessTexture')
    for role,info in roles.items():
        if info is None:continue
        image=doc['images'][doc['textures'][info['index']]['source']];view=doc['bufferViews'][image['bufferView']]
        extension='.png' if image['mimeType']=='image/png' else '.jpg';target=OUT/(part+'_'+role+extension)
        start=view.get('byteOffset',0);target.write_bytes(data[start:start+view['byteLength']])
        manifest.append({'part':part,'role':role,'path':str(target.relative_to(ROOT)).replace('\\','/'),'linear':role!='baseColor'})
(ROOT/'Tools/VendingMonster/Source/GameMeshes/textures.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
