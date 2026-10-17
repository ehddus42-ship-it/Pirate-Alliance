#if UNITY_EDITOR && MAGICACLOTH2
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateAlliance.Characters.Editor
{
    public static class GBHNClothSetup
    {
        public const string SharedRoot = "Assets/Characters/GBHN";
        public const string ScenePath = SharedRoot + "/GBHN_ClothShowcase.unity";
        const string ReportPath = "Documentation/GBHN/cloth-setup.txt";
        sealed class Chain
        {
            public Transform[] bones;
            public Vector3[] rest;
        }
        sealed class Rig
        {
            public GameObject model;
            public Dictionary<string, Transform> bones;
            public Dictionary<string, SkinnedMeshRenderer> skins;
            public Dictionary<string, ColliderComponent> colliders = new Dictionary<string, ColliderComponent>();
            public SecondaryMotionMeshSet meshes;
            public string folder;
            public StringBuilder report;
        }

        [MenuItem("Tools/Geumbi and Haneul/Rebuild Magica Cloth Prefabs")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var report = new StringBuilder("Geumbi / Haneul — Magica Cloth 2 BoneCloth\n");
            foreach (string name in new[] { "Geumbi", "Haneul" }) Build(name, report);
            BuildShowcase();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        public static void BatchBuild()
        {
            try { Rebuild(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static string PrefabPath(string name, bool lit = false) => $"Assets/Characters/{name}/Magica/{name}_Magica{(lit ? "_Lit" : "")}.prefab";

        static void Build(string name, StringBuilder report)
        {
            string folder = $"Assets/Characters/{name}/Magica";
            Directory.CreateDirectory(folder + "/PreBuild");
            AssetDatabase.Refresh();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/{name}/{name}.prefab");
                if (!original) throw new InvalidOperationException("Source prefab missing: " + name);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(original, preview);
                model.name = name + "_Magica";
                var r = new Rig { model = model, folder = folder, report = report };
                r.bones = model.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                r.skins = model.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(s => s.name);
                string meshPath = folder + "/SecondaryMotionMeshes.asset";
                r.meshes = AssetDatabase.LoadAssetAtPath<SecondaryMotionMeshSet>(meshPath);
                if (!r.meshes) { r.meshes = ScriptableObject.CreateInstance<SecondaryMotionMeshSet>(); AssetDatabase.CreateAsset(r.meshes, meshPath); }
                var animator = model.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var skin in r.skins.Values) skin.updateWhenOffscreen = false;
                AddBodyColliders(r, name == "Haneul");
                report.AppendLine("\n" + name);
                if (name == "Geumbi") BuildGeumbi(r); else BuildHaneul(r);
                var cloths = model.GetComponentsInChildren<MagicaCloth>();
                int particles = cloths.Sum(c => c.GetSerializeData2().preBuildData.GetSharePreBuildData().proxyMesh.attributes.count);
                if (particles > 160) throw new InvalidOperationException(name + " exceeds the 160-particle budget: " + particles);
                report.AppendLine($"Total: {cloths.Length} cloth teams, {particles} particles. All BoneCloth; no runtime mesh rebuilds.");
                if (!PrefabUtility.SaveAsPrefabAsset(model, PrefabPath(name))) throw new InvalidOperationException("Could not save " + name);
                UnityEngine.Object.DestroyImmediate(model);
                // The Lit variant shares exactly the same rig and prebuild data, only materials differ.
                var lit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)), preview);
                var litSource = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/{name}/{name}_Lit.prefab");
                var litMaterials = litSource.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(s => s.name, s => s.sharedMaterials);
                foreach (var skin in lit.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterials = litMaterials[skin.name];
                lit.name = name + "_Magica_Lit";
                PrefabUtility.SaveAsPrefabAsset(lit, PrefabPath(name, true));
                UnityEngine.Object.DestroyImmediate(lit);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void BuildGeumbi(Rig r)
        {
            var hair = r.skins["Geumbi_Hair"];
            // Eight radial locks; the crown and attachment to the scalp retain the imported skinning.
            var hairChains = RadialChains(r, "Hair", r.bones["Head"], WorldVertices(hair), 8, 3, 1.60f, 1.37f);
            Bind(r, hair, hairChains, p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.605f, 1.54f, p.y)));
            Cloth(r, "Hair", hairChains, .025f, .55f, .008f, new[] { "Head", "Neck" });

            var top = r.skins["Geumbi_Top"];
            var coat = RadialChains(r, "CoatHem", r.bones["Hips"], WorldVertices(top), 6, 3, 1.10f, .88f);
            Bind(r, top, coat, p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.10f, 1.015f, p.y)));
            Cloth(r, "CoatHem", coat, .055f, .6f, .007f, new[] { "Hips", "LeftThigh", "RightThigh" });

            var pants = r.skins["Geumbi_Pants"];
            var points = WorldVertices(pants);
            var cuffs = new List<Chain>();
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform leg = r.bones[side + "LowerLeg"];
                var region = points.Where(p => Mathf.Sign(p.x) == Mathf.Sign(leg.position.x)).ToArray();
                cuffs.AddRange(RadialChains(r, side + "TrouserHem", leg, region, 2, 3, .39f, .17f));
            }
            Bind(r, pants, cuffs.ToArray(), p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.39f, .29f, p.y)));
            Cloth(r, "TrouserHems", cuffs.ToArray(), .015f, .8f, .004f, new[] { "LeftShin", "RightShin" });
        }

        static void BuildHaneul(Rig r)
        {
            var hair = r.skins["Haneul_Hair"];
            var points = WorldVertices(hair);
            var chains = new List<Chain>();
            const int chainCount = 7, segments = 6;
            for (int c = 0; c < chainCount; c++)
            {
                float horizontal = Mathf.Lerp(-1, 1, c / (float)(chainCount - 1));
                var path = new Vector3[segments];
                for (int i = 0; i < segments; i++)
                {
                    float depth = i / (float)(segments - 1), y = Mathf.Lerp(1.61f, .75f, depth);
                    float x = horizontal * Mathf.Lerp(.08f, .34f, Mathf.Sqrt(depth));
                    var sample = points.OrderBy(p => (p.y-y)*(p.y-y)*2 + (p.x-x)*(p.x-x)).Take(100).ToArray();
                    path[i] = new Vector3(x, y, sample.Average(p => p.z));
                }
                chains.Add(MakeChain("LongHair_" + c, r.bones["Head"], path));
            }
            Bind(r, hair, chains.ToArray(), p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.61f, 1.51f, p.y)));
            Cloth(r, "LongHair", chains.ToArray(), .12f, .38f, .012f, new[] { "Head", "Neck", "Chest", "Hips", "LeftThigh", "RightThigh" });

            var locks = new List<Chain>();
            foreach (var skin in r.skins.Values.Where(s => s.name.StartsWith("Haneul_Sidelock")))
            {
                var v = WorldVertices(skin);
                float hi = v.Max(p => p.y), lo = v.Min(p => p.y);
                Vector3 mid = v.Aggregate(Vector3.zero, (a, b) => a+b)/v.Length;
                var chain = MakeChain(skin.name, r.bones["Head"], Enumerable.Range(0,3).Select(i => new Vector3(mid.x, Mathf.Lerp(hi,lo,i/2f),mid.z)).ToArray());
                locks.Add(chain);
                Bind(r, skin, new[] { chain }, p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hi-.01f, hi-.04f, p.y)));
            }
            Cloth(r, "Sidelocks", locks.ToArray(), .012f, .7f, .003f, new[] { "Head" });

            var jacket = r.skins["Haneul_Jacket"];
            var hem = RadialChains(r, "JacketHem", r.bones["Chest"], WorldVertices(jacket), 6, 3, 1.24f, 1.10f);
            Bind(r, jacket, hem, p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.24f, 1.17f, p.y)));
            Cloth(r, "JacketHem", hem, .025f, .7f, .004f, new[] { "Chest" });
        }

        static Vector3[] WorldVertices(SkinnedMeshRenderer skin)
        {
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            // BakeMesh includes the renderer scale; do not apply it twice.
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            var v = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            UnityEngine.Object.DestroyImmediate(mesh);
            return v;
        }

        static Chain[] RadialChains(Rig r, string name, Transform parent, Vector3[] points, int count, int segments, float top, float bottom)
        {
            var region = points.Where(p => p.y < top+.02f && p.y > bottom-.02f).ToArray();
            if(region.Length == 0) throw new InvalidOperationException("Empty cloth region " + name);
            float cx = region.Average(p => p.x), cz = region.Average(p => p.z);
            var result = new Chain[count];
            for(int c=0;c<count;c++)
            {
                float angle = c * Mathf.PI * 2 / count;
                var sector = region.Where(p => Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z-cz,p.x-cx)*Mathf.Rad2Deg,angle*Mathf.Rad2Deg)) <= 180f/count + 8).ToArray();
                if(sector.Length == 0) sector=region;
                var path = new Vector3[segments];
                for(int i=0;i<segments;i++)
                {
                    float y=Mathf.Lerp(top,bottom,i/(float)(segments-1));
                    var samples=sector.OrderBy(p=>Mathf.Abs(p.y-y)).Take(Mathf.Min(60,sector.Length)).ToArray();
                    path[i]=new Vector3(samples.Average(p=>p.x),y,samples.Average(p=>p.z));
                }
                result[c]=MakeChain(name+"_"+c,parent,path);
            }
            return result;
        }

        static Chain MakeChain(string name, Transform parent, Vector3[] path)
        {
            var chain=new Chain { bones = new Transform[path.Length], rest=path };
            for(int i=0;i<path.Length;i++)
            {
                var bone=new GameObject("MC2_"+name+"_"+i).transform;
                bone.SetParent(i==0?parent:chain.bones[i-1],false);
                bone.position=path[i];
                bone.rotation=Quaternion.identity;
                chain.bones[i]=bone;
            }
            return chain;
        }

        static void Bind(Rig r, SkinnedMeshRenderer skin, Chain[] chains, Func<Vector3,float> influence)
        {
            Mesh source=skin.sharedMesh;
            Mesh mesh=UnityEngine.Object.Instantiate(source); mesh.name=skin.name+"_SecondaryMotion";
            var positions=WorldVertices(skin);
            // The supplied prefab's humanoid rest pose differs from the FBX bind pose. Rebind the
            // generated mesh in that authored pose so added bones do not snap hair back to FBX space.
            var bakedRest=new Mesh();skin.BakeMesh(bakedRest);
            mesh.vertices=positions.Select(skin.transform.InverseTransformPoint).ToArray();
            mesh.normals=bakedRest.normals;mesh.tangents=bakedRest.tangents;
            UnityEngine.Object.DestroyImmediate(bakedRest);
            var bones=skin.bones.ToList();
            var bindposes=bones.Select(b=>b.worldToLocalMatrix*skin.transform.localToWorldMatrix).ToList();
            var index=new Dictionary<Transform,int>();
            foreach(var chain in chains) foreach(var bone in chain.bones)
            {
                index[bone]=bones.Count; bones.Add(bone); bindposes.Add(bone.worldToLocalMatrix*skin.transform.localToWorldMatrix);
            }
            var counts=source.GetBonesPerVertex(); var original=source.GetAllBoneWeights();
            var newCounts=new NativeArray<byte>(source.vertexCount,Allocator.Temp);
            var weights=new List<BoneWeight1>(); int cursor=0, affected=0;
            for(int v=0;v<positions.Length;v++)
            {
                float blend=Mathf.Clamp01(influence(positions[v]));
                var chosen=new Dictionary<int,float>();
                for(int w=0;w<counts[v];w++) {var bw=original[cursor++]; chosen[bw.boneIndex]=bw.weight*(1-blend);}
                if(blend>0)
                {
                    affected++;
                    // Blend the two closest strands and along their segments; avoids a hard split between locks.
                    var nearest=chains.Select(ch=>NearestSegment(ch,positions[v])).OrderBy(s=>s.distance).Take(2).ToArray();
                    float total=nearest.Sum(s=>1f/Mathf.Max(.0001f,s.distance));
                    foreach(var s in nearest)
                    {
                        float share=(1f/Mathf.Max(.0001f,s.distance))/total*blend;
                        AddWeight(chosen,index[s.chain.bones[s.segment]],share*(1-s.t));
                        AddWeight(chosen,index[s.chain.bones[s.segment+1]],share*s.t);
                    }
                }
                var best=chosen.Where(p=>p.Value>1e-6f).OrderByDescending(p=>p.Value).Take(4).ToArray();
                float sum=best.Sum(p=>p.Value); if(sum<=0)throw new InvalidOperationException("Unweighted vertex");
                newCounts[v]=(byte)best.Length;
                weights.AddRange(best.Select(p=>new BoneWeight1 { boneIndex=p.Key,weight=p.Value/sum }));
            }
            var newWeights=new NativeArray<BoneWeight1>(weights.ToArray(),Allocator.Temp);
            mesh.SetBoneWeights(newCounts,newWeights);newCounts.Dispose();newWeights.Dispose();counts.Dispose();original.Dispose();
            mesh.bindposes=bindposes.ToArray();
            string path=AssetDatabase.GetAssetPath(r.meshes);
            var existing=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m=>m.name==mesh.name);
            if(existing) {EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
            else AssetDatabase.AddObjectToAsset(mesh,r.meshes);
            EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(r.meshes);AssetDatabase.SaveAssetIfDirty(r.meshes);
            skin.sharedMesh=mesh;skin.bones=bones.ToArray();skin.quality=SkinQuality.Bone4;
            // Conservative static bounds cover motion without forcing offscreen skinning every frame.
            var bounds=skin.localBounds;bounds.Expand(.35f/Mathf.Max(.0001f,skin.transform.lossyScale.x));skin.localBounds=bounds;
            var baked=WorldVertices(skin);float error=positions.Zip(baked,(a,b)=>Vector3.Distance(a,b)).Max();
            if(error>.0002f)throw new InvalidOperationException($"{skin.name}: rest mesh changed by {error}m");
            r.report.AppendLine($"  {skin.name}: {affected}/{mesh.vertexCount} vertices weighted, rest error {error:F7}m, max 4 weights/vertex");
        }

        static void AddWeight(Dictionary<int,float> weights,int bone,float w) {weights[bone]=weights.TryGetValue(bone,out float prior)?prior+w:w;}
        static (Chain chain,int segment,float t,float distance) NearestSegment(Chain chain,Vector3 p)
        {
            int best=0;float bestT=0,dist=float.MaxValue;
            for(int i=0;i<chain.rest.Length-1;i++)
            {
                Vector3 a=chain.rest[i],d=chain.rest[i+1]-a;
                float t=Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(d.sqrMagnitude,1e-9f));
                float sq=(p-(a+d*t)).sqrMagnitude;
                if(sq<dist){dist=sq;best=i;bestT=t;}
            }
            return(chain,best,bestT,dist);
        }

        static void Cloth(Rig r,string name,Chain[] chains,float travel,float stiffness,float radius,string[] colliders)
        {
            var go=new GameObject("Magica_"+name);go.transform.SetParent(r.model.transform,false);
            var cloth=go.AddComponent<MagicaCloth>();var s=cloth.SerializeData;
            s.clothType=ClothProcess.ClothType.BoneCloth;
            s.rootBones=chains.Select(c=>c.bones[0]).ToList();
            s.connectionMode=RenderSetupData.BoneConnectionMode.Line;
            s.rootRotation=0; s.rotationalInterpolation=.5f;
            s.gravity=2; s.gravityFalloff=1;
            s.damping=new CurveSerializeData(.22f);
            s.radius=new CurveSerializeData(radius);
            s.angleRestorationConstraint.useAngleRestoration=true;
            s.angleRestorationConstraint.stiffness=new CurveSerializeData(stiffness,1,.65f);
            s.angleLimitConstraint.useAngleLimit=true;
            s.angleLimitConstraint.limitAngle=new CurveSerializeData(name=="LongHair"?28:18);
            s.motionConstraint.useMaxDistance=true;s.motionConstraint.maxDistance=new CurveSerializeData(travel,.15f,1);
            s.colliderCollisionConstraint.mode=ColliderCollisionConstraint.Mode.Edge;
            s.colliderCollisionConstraint.colliderList=colliders.Select(n=>r.colliders[n]).ToList();
            s.selfCollisionConstraint.selfMode=SelfCollisionConstraint.SelfCollisionMode.None;
            s.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.None;
            s.wind.influence=.08f;s.wind.movingWind=.1f;
            s.inertiaConstraint.worldInertia=.65f;s.inertiaConstraint.localInertia=.7f;
            s.inertiaConstraint.particleSpeedLimit=new CheckSliderSerializeData(true,2);
            s.inertiaConstraint.movementSpeedLimit=new CheckSliderSerializeData(true,6);
            s.inertiaConstraint.localMovementSpeedLimit=new CheckSliderSerializeData(true,4);
            s.inertiaConstraint.rotationSpeedLimit=new CheckSliderSerializeData(true,540);
            s.inertiaConstraint.localRotationSpeedLimit=new CheckSliderSerializeData(true,540);
            s.inertiaConstraint.teleportMode=InertiaConstraint.TeleportMode.Reset;
            s.inertiaConstraint.teleportDistance=2;s.inertiaConstraint.teleportRotation=100;
            s.cullingSettings.cameraCullingMode=CullingSettings.CameraCullingMode.Reset;
            s.cullingSettings.cameraCullingMethod=CullingSettings.CameraCullingMethod.ManualRenderer;
            s.cullingSettings.cameraCullingRenderers=r.skins.Values.Cast<Renderer>().ToList();
            s.cullingSettings.distanceCullingLength=new CheckSliderSerializeData(true,18);
            s.cullingSettings.distanceCullingFadeRatio=.25f;
            s.stablizationTimeAfterReset=.15f;
            s.DataValidate();
            string path=r.folder+"/PreBuild/"+name+".asset";
            var pre=cloth.GetSerializeData2().preBuildData;
            var asset=ScriptableObject.CreateInstance<PreBuildScriptableObject>();
            pre.preBuildScriptableObject=asset;
            var result=PreBuildDataCreation.CreatePreBuildData(cloth,false);
            if(result.IsError())throw new InvalidOperationException(name+": "+result.GetResultString());
            var old=AssetDatabase.LoadAssetAtPath<PreBuildScriptableObject>(path);
            if(old){EditorUtility.CopySerialized(asset,old);UnityEngine.Object.DestroyImmediate(asset);asset=old;}
            else AssetDatabase.CreateAsset(asset,path);
            pre.preBuildScriptableObject=asset;pre.enabled=true;EditorUtility.SetDirty(asset);
            var data=pre.GetSharePreBuildData();
            if(data==null||pre.DataValidate().IsError())throw new InvalidOperationException("Invalid prebuild "+name);
            r.report.AppendLine($"  Magica_{name}: {data.proxyMesh.attributes.count} particles, {colliders.Length} body colliders, max travel {travel}m, prebuild valid");
        }

        static void AddBodyColliders(Rig r,bool haneul)
        {
            Vector3 head=r.bones["Head"].position;
            Sphere(r,"Head","Head",head+Vector3.up*(haneul?.06f:.045f),haneul?.067f:.075f);
            Capsule(r,"Neck","Neck",r.bones["Neck"].position,r.bones["Head"].position,.04f);
            Capsule(r,"Chest","Chest",r.bones["Spine"].position,r.bones["UpperChest"].position,.09f);
            Sphere(r,"Hips","Hips",r.bones["Hips"].position,.09f);
            foreach(string side in new[]{"Left","Right"})
            {
                Vector3 a=r.bones[side+"UpperLeg"].position,b=r.bones[side+"LowerLeg"].position,c=r.bones[side+"Foot"].position;
                Capsule(r,side+"Thigh",side+"UpperLeg",a,b,.05f);
                Capsule(r,side+"Shin",side+"LowerLeg",b,c,.032f);
            }
        }
        static void Sphere(Rig r,string name,string bone,Vector3 center,float radius)
        {
            var go=new GameObject("MC2_Collider_"+name);go.transform.SetParent(r.bones[bone],false);go.transform.position=center;
            var collider=go.AddComponent<MagicaSphereCollider>();collider.SetSize(radius/go.transform.lossyScale.x);r.colliders[name]=collider;
        }
        static void Capsule(Rig r,string name,string bone,Vector3 start,Vector3 end,float radius)
        {
            var go=new GameObject("MC2_Collider_"+name);go.transform.SetParent(r.bones[bone],false);go.transform.position=(start+end)*.5f;
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,end-start);
            var collider=go.AddComponent<MagicaCapsuleCollider>();collider.direction=MagicaCapsuleCollider.Direction.Y;collider.alignedOnCenter=true;
            float scale=go.transform.lossyScale.x;collider.SetSize(radius/scale,radius/scale,(Vector3.Distance(start,end)+radius*2)/scale);r.colliders[name]=collider;
        }

        static void BuildShowcase()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
                camera.transform.position=new Vector3(0,1.12f,-4.5f);camera.transform.LookAt(new Vector3(0,.95f,0));camera.fieldOfView=32;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.2f);
                var sun=new GameObject("Key Light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(35,-25,0);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.65f,.75f);
                foreach(string name in new[]{"Geumbi","Haneul"})
                {
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)),scene);
                    go.transform.position=new Vector3(name=="Geumbi"?-.65f:.65f,0,0);
                    go.transform.rotation=Quaternion.Euler(0,180,0);
                    var motion=go.AddComponent<ClothShowcaseMotion>();
                    motion.phase=name=="Geumbi"?0:1.4f;
                    motion.relaxedArms=name=="Haneul";
                }
                EditorSceneManager.SaveScene(scene,ScenePath);
        }
    }
}
#endif
