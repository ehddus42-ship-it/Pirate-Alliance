"""Create low triangle meshes and transfer high-poly surface detail to tangent-space normal maps."""
import bpy, pathlib, json, time
ROOT=pathlib.Path(__file__).resolve().parents[2]
PARTS=ROOT/'Assets/Liminal/Art/Meshy/vending_monster_parts'
OUT=ROOT/'Tools/VendingMonster/Source/GameMeshes';OUT.mkdir(parents=True,exist_ok=True)
REPORT=ROOT/'Documentation/Liminal/Concepts/VendingMachine/ModelReview/optimization.json'
report=[]
for key,target in [('arm',5000),('leg',4000),('cabinet',20000)]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source=PARTS/key/(key+'_final.glb') if key!='cabinet' else ROOT/'Assets/Liminal/Art/Meshy/vending_machine/vending_machine.glb'
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes=[o for o in bpy.data.objects if o.type=='MESH']
    for obj in meshes:
        mw=obj.matrix_world.copy();obj.parent=None;obj.matrix_world=mw
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
    if len(meshes)>1:bpy.ops.object.join()
    high=bpy.context.object;high.name=key+'_HighSource';bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    before=sum(len(p.vertices)-2 for p in high.data.polygons)
    low=high.copy();low.data=high.data.copy();low.name=key+'_Game';bpy.context.collection.objects.link(low)
    bpy.ops.object.select_all(action='DESELECT');low.select_set(True);bpy.context.view_layer.objects.active=low
    dec=low.modifiers.new('Game topology reduction','DECIMATE');dec.ratio=target/before;dec.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    for face in low.data.polygons:face.use_smooth=True
    # The decimator preserves UV islands. Each game material gets a high-to-low tangent normal bake.
    for slot in low.material_slots:slot.material=slot.material.copy()
    image=bpy.data.images.new(key+'_BakedNormal_2K',width=2048,height=2048,alpha=False)
    image.colorspace_settings.name='Non-Color'
    for slot in low.material_slots:
        nodes=slot.material.node_tree.nodes
        node=nodes.new('ShaderNodeTexImage');node.image=image;node.name='HighToLow_BakedNormal';nodes.active=node
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
    scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.016 if key!='cabinet' else .035
    scene.render.bake.max_ray_distance=.065 if key!='cabinet' else .10
    scene.render.bake.margin=12;scene.render.bake.normal_space='TANGENT';scene.render.bake.use_clear=True
    bpy.ops.object.select_all(action='DESELECT');high.select_set(True);low.select_set(True);bpy.context.view_layer.objects.active=low
    start=time.time();bpy.ops.object.bake(type='NORMAL')
    normal_path=OUT/(key+'_normal_2k.png');image.filepath_raw=str(normal_path);image.file_format='PNG';image.save();image.pack()
    for slot in low.material_slots:
        mat=slot.material;nodes=mat.node_tree.nodes;links=mat.node_tree.links
        principled=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
        for link in list(principled.inputs['Normal'].links):links.remove(link)
        normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=1
        links.new(nodes['HighToLow_BakedNormal'].outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],principled.inputs['Normal'])
        for node in nodes:
            if node.type=='TEX_IMAGE' and node.image and node.image!=image:
                if max(node.image.size)>2048:node.image.scale(2048,2048)
                node.image.pack()
    bpy.data.objects.remove(high,do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT');low.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(OUT/(key+'_game.glb')),export_format='GLB',use_selection=True,export_animations=False)
    actual=sum(len(p.vertices)-2 for p in low.data.polygons)
    row={'part':key,'source_triangles':before,'game_triangles':actual,'reduction_percent':round(100*(1-actual/before),2),
         'normal_bake':'Cycles selected-to-active tangent-space; high geometry and source normal -> game mesh','normal_resolution':[2048,2048],
         'bake_seconds':round(time.time()-start,1),'uvs_preserved':bool(low.data.uv_layers)}
    report.append(row);REPORT.write_text(json.dumps(report,indent=2));print('OPTIMIZED '+json.dumps(row),flush=True)
