"""Read-only Blender validation of the peaceful model and Idle/Walk/Dash FBX clips.

Run from any directory:
  blender --background --python Tools/NamgungLuna/validate_peaceful_prayer.py
Optional arguments after Blender's -- separator:
  --candidate-dir PATH   Directory containing the four LunaPeaceful*_Game.fbx files.
  --report-dir PATH      Output directory for numeric_qa.json.
Defaults inspect Temp/NamgungLuna/Prayer and save the report under Documentation.
The tool does not modify model, animation, source, or Unity assets. Visual render
review is recorded separately by the reviewer; numeric pass does not replace it.
"""
from pathlib import Path
import bpy,json,math,hashlib,argparse,sys
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--candidate-dir',type=Path,default=ROOT/'Temp/NamgungLuna/Prayer')
parser.add_argument('--report-dir',type=Path,default=ROOT/'Documentation/NamgungLuna/PeacefulPrayerQA')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
CAND=args.candidate_dir.resolve();OUT=args.report_dir.resolve();OUT.mkdir(parents=True,exist_ok=True);OLD=ROOT/'Tools/NamgungLuna/Source/PeacefulBeforePrayer'
files=[CAND/'LunaPeaceful_Game.fbx']+[CAND/f'LunaPeaceful{k}_Game.fbx' for k in ['Idle','Walk','Dash']]
def info(p):return {'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
before={str(p):info(p) for p in files+list(OLD.glob('*.fbx'))}
def reset():bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.scene.render.fps=30;bpy.context.scene.render.fps_base=1
def imp(path):
 old=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False);new=[o for o in bpy.data.objects if o not in old]
 return next(o for o in new if o.type=='ARMATURE'),[o for o in new if o.type=='MESH']
def points(m):return np.array([list(m.matrix_world@v.co) for v in m.data.vertices],dtype=float)
def bound(p):return {'min':p.min(0).tolist(),'max':p.max(0).tolist(),'size':(p.max(0)-p.min(0)).tolist()}
def mats(ms):
 return {mat.name:{'color':list(mat.diffuse_color),'texture_paths':[im.image.filepath for im in mat.node_tree.nodes if im.type=='TEX_IMAGE' and im.image] if mat.use_nodes else []} for m in ms for mat in m.data.materials if mat}
def summary(r,ms):
 report={'bones':list(r.data.bones.keys()),'bone_count':len(r.data.bones),'mesh_count':len(ms),'meshes':{},'materials':mats(ms)}
 pp=[]
 for m in ms:
  p=points(m);pp.append(p);weight_sums=[sum(g.weight for g in v.groups) for v in m.data.vertices]
  weighted=[g.group for v in m.data.vertices for g in v.groups if g.weight>1e-6]
  used={i for poly in m.data.polygons for i in poly.vertices};edges={}
  for poly in m.data.polygons:
   vv=list(poly.vertices)
   for a,b in zip(vv,vv[1:]+vv[:1]):key=tuple(sorted((a,b)));edges[key]=edges.get(key,0)+1
  report['meshes'][m.name]={'vertices':len(p),'used_vertices':len(used),'polygons':len(m.data.polygons),'triangles':sum(len(poly.vertices)-2 for poly in m.data.polygons),'bounds':bound(p),'finite':bool(np.isfinite(p).all()),'maximum_skin_influences':max(len(v.groups) for v in m.data.vertices),'maximum_weight_sum_error':max(abs(s-1) for s in weight_sums),'unweighted_vertices':sum(s<1e-6 for s in weight_sums),'weighted_bones':sorted(set(m.vertex_groups[i].name for i in weighted)),'topological_boundary_edges':sum(v==1 for v in edges.values()),'note':'Boundary-edge counts include FBX UV seam splits; not a count of visible holes.'}
 report['bounds']=bound(np.concatenate(pp));report['total_vertices']=sum(len(m.data.vertices) for m in ms);report['total_triangles']=sum(sum(len(p.vertices)-2 for p in m.data.polygons) for m in ms)
 return report
reset();oldrig,oldmeshes=imp(OLD/'LunaPeaceful_Game.fbx');oldinfo=summary(oldrig,oldmeshes);oldbind={b.name:np.array(oldrig.matrix_world@b.matrix_local) for b in oldrig.data.bones};oldpoints=points(oldmeshes[0]);oldfaces=len(oldmeshes[0].data.polygons)
reset();rig,meshes=imp(files[0]);modelinfo=summary(rig,meshes);newbind={b.name:np.array(rig.matrix_world@b.matrix_local) for b in rig.data.bones}
main=max(meshes,key=lambda m:len(m.data.vertices));mainpoints=points(main)
tree=KDTree(len(oldpoints))
for i,p in enumerate(oldpoints):tree.insert(Vector(p),i)
tree.balance();dist=np.array([tree.find(Vector(p))[2] for p in mainpoints])
comparison={'new_bones':sorted(set(newbind)-set(oldbind)),'removed_bones':sorted(set(oldbind)-set(newbind)),'common_bind_max_absolute_error':max(float(abs(newbind[k]-v).max()) for k,v in oldbind.items() if k in newbind),'main_mesh_vertices_matching_source_within_1e_5':int(sum(dist<1e-5)),'main_mesh_vertex_count':len(mainpoints),'main_mesh_vertex_source_distance_max':float(dist.max()),'source_main_faces':oldfaces,'candidate_main_faces':len(main.data.polygons),'source_bounds':oldinfo['bounds'],'candidate_bounds':modelinfo['bounds'],'original_source_hashes':{str(p.relative_to(ROOT)):before[str(p)] for p in OLD.glob('*.fbx')}}
report={'scope':'Independent FBX import, 9 sampled poses per clip, skin normalization, bind preservation, loop endpoints, edge stretch, and rigid separated boots. Does not run Unity or automatically judge visual appearance.','candidate_files':{str(p):before[str(p)] for p in files},'source':oldinfo,'model':modelinfo,'comparison':comparison,'clips':{},'checks':{},'warnings':[]}
for kind in ['Idle','Walk','Dash']:
 reset();rig,meshes=imp(files[0]);clip,unused=imp(CAND/f'LunaPeaceful{kind}_Game.fbx');action=clip.animation_data.action
 first,last=map(float,action.frame_range);frames=np.linspace(first,last,9).tolist();track=[];bone_track=[];samples=[]
 rest_geometry={}
 for m in meshes:
  base=points(m);edge=np.array([list(e.vertices) for e in m.data.edges]);length=np.linalg.norm(base[edge[:,1]]-base[edge[:,0]],axis=1)
  assigned=[]
  for v in m.data.vertices:
   weights={m.vertex_groups[g.group].name:g.weight for g in v.groups};assigned.append(next((s for s in ['Left','Right'] if weights.get('Luna'+s+'Boot',0)>.999),None))
  category=[]
  for a,b in edge:
   aa,bb=assigned[a],assigned[b]
   category.append(('same_'+aa+'_boot' if aa==bb else 'cross_left_right_boot') if aa and bb else ('boot_to_nonboot' if aa or bb else 'other'))
  rest_geometry[m.name]=(base,edge,length,np.array(category))
 inv=rig.matrix_world.inverted();bones=list(rig.pose.bones)
 for frame in frames:
  bpy.context.scene.frame_set(math.floor(frame),subframe=frame%1)
  for b in bones:
   b.matrix=inv@clip.matrix_world@clip.pose.bones[b.name].matrix;bpy.context.view_layer.update()
  ps=[];permesh={}
  dg=bpy.context.evaluated_depsgraph_get()
  for m in meshes:
   e=m.evaluated_get(dg);p=points(e);ps.append(p);permesh[m.name]=bound(p)
   base,edges,length,category=rest_geometry[m.name];posedlength=np.linalg.norm(p[edges[:,1]]-p[edges[:,0]],axis=1);ratio=posedlength/np.maximum(length,1e-5);worst=np.argsort(ratio)[-3:][::-1]
   permesh[m.name]['edge_stretch_max']=float(ratio.max());permesh[m.name]['edges_stretched_over_4x']=int(sum((ratio>4)&(posedlength>.015)))
   permesh[m.name]['boot_edge_categories']={}
   for label in sorted(set(category)):
    jj=np.where(category==label)[0];k=jj[np.argmax(ratio[jj])]
    permesh[m.name]['boot_edge_categories'][label]={'edges':len(jj),'maximum_ratio':float(ratio[jj].max()),'maximum_length_error':float(abs(posedlength[jj]-length[jj]).max()),'worst_rest_points':base[edges[k]].tolist(),'worst_posed_points':p[edges[k]].tolist(),'worst_weights':[{m.vertex_groups[g.group].name:float(g.weight) for g in m.data.vertices[i].groups} for i in edges[k]]}
   permesh[m.name]['worst_edges']=[{'ratio':float(ratio[k]),'rest_length':float(length[k]),'posed_length':float(posedlength[k]),'rest_points':base[edges[k]].tolist(),'posed_points':p[edges[k]].tolist(),'weights':[{m.vertex_groups[g.group].name:float(g.weight) for g in m.data.vertices[i].groups} for i in edges[k]]} for k in worst]
  p=np.concatenate(ps);bp=np.array([list((rig.matrix_world@b.matrix).translation) for b in bones]);bm=np.array([np.array(rig.matrix_world@b.matrix) for b in bones])
  boot_scales={name:np.linalg.svd(np.array((rig.matrix_world@rig.pose.bones[name].matrix)@(rig.matrix_world@rig.data.bones[name].matrix_local).inverted())[:3,:3],compute_uv=False).tolist() for name in ['LunaLeftBoot','LunaRightBoot']}
  track.append(p);bone_track.append(bm);samples.append({'frame':frame,'seconds':(frame-first)/30,'finite_geometry':bool(np.isfinite(p).all()),'finite_bone_matrices':bool(np.isfinite(bm).all()),'bounds':bound(p),'mesh_bounds':permesh,'boot_relative_scales':boot_scales})
 vdiff=np.linalg.norm(track[-1]-track[0],axis=1);bdiff=abs(bone_track[-1]-bone_track[0]);body_names=[b.name for b in bones];hips_index=body_names.index('Hips');hippositions=np.array([b[hips_index,:3,3] for b in bone_track]);handindices=[body_names.index(f'Luna{s}PrayerHand') for s in ['Left','Right']];hands=np.array([b[handindices,:3,3] for b in bone_track]);handsdistance=np.linalg.norm(hands[:,0]-hands[:,1],axis=1)
 first_reference=track[0];maxmove=max(float(np.linalg.norm(p-first_reference,axis=1).max()) for p in track)
 report['clips'][kind.lower()]={'bone_count':len(clip.data.bones),'missing_model_bones':sorted(set(rig.data.bones.keys())-set(clip.data.bones.keys())),'extra_clip_bones':sorted(set(clip.data.bones.keys())-set(rig.data.bones.keys())),'fps':30,'frame_range':[first,last],'duration_seconds':(last-first)/30,'sample_count':9,'samples':samples,'loop_vertex_displacement_max':float(vdiff.max()),'loop_vertex_displacement_rms':float(np.sqrt(np.mean(vdiff**2))),'loop_bone_matrix_absolute_error_max':float(bdiff.max()),'maximum_vertex_movement_from_first_sample':maxmove,'hips_translation_range':(hippositions.max(0)-hippositions.min(0)).tolist(),'prayer_wrist_distance_min':float(handsdistance.min()),'prayer_wrist_distance_max':float(handsdistance.max())}
report['checks']['all_finite']=all(s['finite_geometry'] and s['finite_bone_matrices'] for c in report['clips'].values() for s in c['samples'])
report['checks']['ten_added_bones']=len(comparison['new_bones'])==10
report['checks']['six_added_arm_bones']=sum('Prayer' in n for n in comparison['new_bones'])==6
report['checks']['four_added_leg_bones']=sum('Thigh' in n or 'Shin' in n for n in comparison['new_bones'])==4
report['checks']['all_clip_bones_match_model']=all(not c['missing_model_bones'] and not c['extra_clip_bones'] for c in report['clips'].values())
report['checks']['common_bind_preserved']=comparison['common_bind_max_absolute_error']<1e-4
report['checks']['all_loop_endpoints_under_1mm']=all(c['loop_vertex_displacement_max']<.001 for c in report['clips'].values())
report['checks']['all_weights_normalized']=all(m['maximum_weight_sum_error']<1e-4 and m['unweighted_vertices']==0 for m in modelinfo['meshes'].values())
report['edge_stretch_summary']={kind:{'maximum':max(m['edge_stretch_max'] for s in c['samples'] for m in s['mesh_bounds'].values()),'maximum_edges_over_4x_at_one_sample':max(sum(m['edges_stretched_over_4x'] for m in s['mesh_bounds'].values()) for s in c['samples'])} for kind,c in report['clips'].items()}
report['checks']['no_severe_edge_stretch']=all(v['maximum_edges_over_4x_at_one_sample']==0 for v in report['edge_stretch_summary'].values())
report['rigid_boots']={}
for side in ['Left','Right']:
 meshname='Luna'+side+'SeparatedBoot';bonename='Luna'+side+'Boot';stats=modelinfo['meshes'].get(meshname)
 samples=[sample for c in report['clips'].values() for sample in c['samples']]
 errors=[sample['mesh_bounds'][meshname]['boot_edge_categories']['same_'+side+'_boot']['maximum_length_error'] for sample in samples if meshname in sample['mesh_bounds'] and 'same_'+side+'_boot' in sample['mesh_bounds'][meshname]['boot_edge_categories']]
 scale_error=max(abs(scale-1) for sample in samples for scale in sample['boot_relative_scales'][bonename])
 report['rigid_boots'][side]={'mesh_present':stats is not None,'single_correct_bone':bool(stats and stats['weighted_bones']==[bonename]),'maximum_edge_length_error':max(errors) if errors else None,'maximum_relative_scale_error':scale_error}
report['checks']['separated_boots_rigid']=all(b['mesh_present'] and b['single_correct_bone'] and b['maximum_edge_length_error'] is not None and b['maximum_edge_length_error']<1e-4 and b['maximum_relative_scale_error']<1e-4 for b in report['rigid_boots'].values())
report['checks']['body_has_no_boot_influence']=all('LunaLeftBoot' not in m['weighted_bones'] and 'LunaRightBoot' not in m['weighted_bones'] for name,m in modelinfo['meshes'].items() if name not in ['LunaLeftSeparatedBoot','LunaRightSeparatedBoot'])
after={str(p):info(p) for p in files+list(OLD.glob('*.fbx'))};report['checks']['inputs_unchanged_during_qa']=before==after
report['passed']=all(report['checks'].values());(OUT/'numeric_qa.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'passed':report['passed'],'checks':report['checks'],'comparison':comparison,'clips':{k:{a:b for a,b in v.items() if a!='samples'} for k,v in report['clips'].items()}},indent=2))
if not report['passed']:raise SystemExit(1)
