using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    public static class VendingMonsterBuilder
    {
        public const string Folder = "Assets/Liminal/Art/VendingMonster";
        public const string PrefabPath = "Assets/Liminal/Prefabs/Enemies/VendingMachineMonster.prefab";
        public const string DemoPath = "Assets/Liminal/Scenes/VendingMonsterShowcase.unity";
        const string Review = "Documentation/Liminal/Concepts/VendingMachine/ModelReview";
        const string ControllerPath = Folder + "/Animations/VendingMonster.controller";

        [MenuItem("AC Roguelike/Liminal/Vending Monster/Build Rigged Monster")]
        public static string Build()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            Directory.CreateDirectory(Folder+"/Animations"); Directory.CreateDirectory(Folder+"/Materials");
            Directory.CreateDirectory(Folder+"/Audio");Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            Directory.CreateDirectory(Review);AssetDatabase.Refresh();
            ConfigureGameTextures();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/vending_monster_rig.glb");
            var machineSource=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/vending_machine_game.glb");
            if(!source || !machineSource) throw new InvalidOperationException("Meshy rig and source vending machine must be imported.");
            var preview=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("VendingMachineMonster");SceneManager.MoveGameObjectToScene(root,preview);
            try
            {
                var visual=new GameObject("Visual").transform;visual.SetParent(root.transform,false);
                var skeleton=Object.Instantiate(source,visual);skeleton.name="MeshyLimbRig";
                var animator=visual.gameObject.AddComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                visual.gameObject.AddComponent<VendingMonsterAnimationEvents>();
                var rig=visual.gameObject.AddComponent<VendingMonsterRig>();
                Transform Find(string n)=>skeleton.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
                rig.cabinet=Find("Cabinet");
                VendingMonsterRig.Limb Arm(string s)=>new VendingMonsterRig.Limb {upper=Find("UpperArm_"+s),lower=Find("Forearm_"+s),end=Find("Hand_"+s),
                    fingers=skeleton.GetComponentsInChildren<Transform>(true).Where(t=>t.name.EndsWith("_"+s)&&new[]{"Thumb","Index","Middle","Ring","Little"}.Any(f=>t.name.StartsWith(f))).ToArray()};
                VendingMonsterRig.Limb Leg(string s)=>new VendingMonsterRig.Limb {upper=Find("Thigh_"+s),lower=Find("Shin_"+s),end=Find("Foot_"+s),tip=Find("Toes_"+s)};
                rig.leftArm=Arm("L");rig.rightArm=Arm("R");rig.leftLeg=Leg("L");rig.rightLeg=Leg("R");
                var machine=Object.Instantiate(machineSource);machine.name="OriginalVendingMachine";SceneManager.MoveGameObjectToScene(machine,preview);
                var machineRenderers=machine.GetComponentsInChildren<Renderer>();
                Bounds bounds=Combined(machineRenderers);float scale=1.9f/bounds.size.y;
                machine.transform.localScale=Vector3.one*scale;
                bounds=Combined(machineRenderers);machine.transform.position=new Vector3(-bounds.center.x,1.16f-bounds.min.y,-bounds.center.z);
                machine.transform.SetParent(rig.cabinet,true);
                foreach(var c in machine.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                string cabinetMaterialPath=Folder+"/Materials/Cabinet.mat";
                var cabinetMaterial=AssetDatabase.LoadAssetAtPath<Material>(cabinetMaterialPath);
                if(!cabinetMaterial){cabinetMaterial=new Material(machineRenderers[0].sharedMaterial);AssetDatabase.CreateAsset(cabinetMaterial,cabinetMaterialPath);}
                else cabinetMaterial.CopyPropertiesFromMaterial(machineRenderers[0].sharedMaterial);
                BindGameTextures(cabinetMaterial,"cabinet");
                foreach(var r in machineRenderers)r.sharedMaterial=cabinetMaterial;
                foreach(var r in skeleton.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.quality=SkinQuality.Bone4;r.updateWhenOffscreen=true;
                    r.localBounds=new Bounds(new Vector3(0,1.4f,0),new Vector3(5,5,5));
                    // Shared source textures, with a darker desaturated creature skin in game lighting.
                    var mats=r.sharedMaterials;
                    for(int i=0;i<mats.Length;i++)
                    {
                        string matPath=Folder+"/Materials/"+(r.name.StartsWith("arm")?"ArmSkin":"LegSkin")+".mat";
                        var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if(!mat) {mat=new Material(mats[i]);mat.name=Path.GetFileNameWithoutExtension(matPath);AssetDatabase.CreateAsset(mat,matPath);}
                        mat.CopyPropertiesFromMaterial(mats[i]);
                        if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",new Color(.84f,.83f,.8f));
                        if(mat.HasProperty("baseColorFactor"))mat.SetColor("baseColorFactor",new Color(.84f,.83f,.8f));
                        if(mat.HasProperty("metallicFactor"))mat.SetFloat("metallicFactor",0);
                        if(mat.HasProperty("roughnessFactor"))mat.SetFloat("roughnessFactor",.95f);
                        if(mat.HasProperty("metallicRoughnessTexture"))mat.SetTexture("metallicRoughnessTexture",null);
                        BindGameTextures(mat,r.name.StartsWith("arm")?"arm":"leg");
                        mats[i]=mat;
                    }
                    r.sharedMaterials=mats;
                }
                Transform Socket(string n,Transform parent,Vector3 worldPosition)
                {var t=new GameObject(n).transform;t.SetParent(parent,false);t.position=worldPosition;return t;}
                var dispenser=Socket("CanDispenserSocket",rig.cabinet,new Vector3(0,1.68f,.43f));rig.dispenser=dispenser;
                var grip=Socket("CanGrip",rig.rightArm.end,rig.rightArm.end.position+new Vector3(0,-.13f,.06f));grip.rotation=Quaternion.identity;
                var aim=Socket("AimAnchor",rig.cabinet,new Vector3(0,2.05f,0));
                var body=root.AddComponent<CharacterController>();body.radius=.53f;body.height=1.9f;body.center=Vector3.up*.95f;body.stepOffset=.18f;body.skinWidth=.045f;
                var health=root.AddComponent<TrainingEnemy>();health.maxHealth=165;health.respawnOnDeath=false;health.aimAnchor=aim;
                var monster=root.AddComponent<VendingMonster>();monster.animator=animator;monster.rig=rig;monster.canGrip=grip;monster.dispenserSocket=dispenser;
                monster.limbRenderers=skeleton.GetComponentsInChildren<SkinnedMeshRenderer>();
                monster.canPrefab=BuildCan();
                var warningObject=new GameObject("ChargeTelegraph");warningObject.transform.SetParent(root.transform,false);
                var warning=warningObject.AddComponent<LineRenderer>();warning.useWorldSpace=true;warning.numCornerVertices=3;warning.numCapVertices=3;
                warning.shadowCastingMode=ShadowCastingMode.Off;warning.receiveShadows=false;warning.widthMultiplier=.045f;
                warning.sharedMaterial=Material("Warning",new Color(1,.28f,.055f),true);warning.startColor=warning.endColor=new Color(1,.4f,.07f);warning.enabled=false;monster.warning=warning;
                monster.voice=root.AddComponent<AudioSource>();monster.voice.spatialBlend=1;monster.voice.minDistance=2;monster.voice.maxDistance=24;monster.voice.playOnAwake=false;
                monster.emergeSound=Sound("CabinetUnfold",2.8f,0);monster.footstepSound=Sound("WeightedStep",.19f,1);
                monster.canPullSound=Sound("CanRattle",.24f,2);monster.throwSound=Sound("ThrowWhoosh",.21f,3);monster.impactSound=Sound("MetalImpact",.38f,4);
                rig.CacheRestPose();
                BakeAnimations(rig,animator);
                rig.Sample(VendingMonsterState.Dormant,0);
                foreach(var r in monster.limbRenderers)r.enabled=false;
                health.visibleRenderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r!=warning).ToArray();
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Review+"/authoring-summary.json",JsonUtility.ToJson(new AuthoringReport(),true));
                return "Created "+PrefabPath+"; four skinned limbs, 46 joints, 9 editable clips, two attack patterns.";
            }
            finally {Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
        }
        [Serializable] class AuthoringReport {public string prefab=PrefabPath;public int bones=46,skinnedLimbs=4,animationClips=9;public float canGrabSeconds=VendingMonsterRig.CanGrabTime,canReleaseSeconds=VendingMonsterRig.CanReleaseTime;}
        static Bounds Combined(Renderer[] renderers) {var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
        static void ConfigureGameTextures()
        {
            foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{Folder+"/Textures"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)continue;
                bool color=path.Contains("_baseColor");
                var settings=importer.GetPlatformTextureSettings("Standalone");
                bool already=importer.maxTextureSize==2048&&importer.sRGBTexture==color&&!importer.isReadable&&settings.overridden&&settings.format==TextureImporterFormat.BC7;
                if(already)continue;
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=color;importer.isReadable=false;importer.mipmapEnabled=true;importer.streamingMipmaps=true;
                importer.maxTextureSize=2048;importer.anisoLevel=4;importer.textureCompression=TextureImporterCompression.CompressedHQ;
                settings.name="Standalone";settings.overridden=true;settings.maxTextureSize=2048;settings.format=TextureImporterFormat.BC7;settings.compressionQuality=80;
                importer.SetPlatformTextureSettings(settings);importer.SaveAndReimport();
            }
        }
        static void BindGameTextures(Material material,string part)
        {
            foreach(string role in new[]{"baseColor","normal","metallicRoughness"})
            {
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+part+"_"+role+".png")??AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+part+"_"+role+".jpg");
                if(texture&&material.HasProperty(role+"Texture"))material.SetTexture(role+"Texture",texture);
            }
            EditorUtility.SetDirty(material);
        }
        static Material Material(string name,Color color,bool unlit=false)
        {
            string path=Folder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.color=color;return m;
        }
        static GameObject BuildCan()
        {
            string path="Assets/Liminal/Prefabs/Enemies/VendingMonsterCan.prefab";
            var root=new GameObject("ThrownDrinkCan");
            void Part(string n,PrimitiveType shape,Vector3 p,Vector3 s,Material mat)
            {var o=GameObject.CreatePrimitive(shape);o.name=n;o.transform.SetParent(root.transform,false);o.transform.localPosition=p;o.transform.localScale=s;
                Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=mat;}
            var label=Material("CanLabel",new Color(.035f,.58f,.61f));var metal=Material("CanAluminium",new Color(.7f,.73f,.73f));
            metal.SetFloat("_Metallic",.8f);metal.SetFloat("_Smoothness",.75f);
            Part("Can",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.13f,.105f,.13f),label);
            Part("TopRim",PrimitiveType.Cylinder,new Vector3(0,.105f,0),new Vector3(.134f,.007f,.134f),metal);
            Part("BottomRim",PrimitiveType.Cylinder,new Vector3(0,-.105f,0),new Vector3(.134f,.006f,.134f),metal);
            Part("PullTab",PrimitiveType.Cube,new Vector3(0,.115f,.005f),new Vector3(.025f,.005f,.045f),metal);
            Part("CreamLabelStripe",PrimitiveType.Cylinder,new Vector3(0,-.01f,0),new Vector3(.131f,.024f,.131f),Material("CanCream",new Color(.85f,.87f,.76f)));
            root.AddComponent<VendingCanProjectile>();var result=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);return result;
        }
        static void BakeAnimations(VendingMonsterRig rig,Animator animator)
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine=controller.layers[0].stateMachine;
            foreach(var state in machine.states)machine.RemoveState(state.state);
            var transforms=rig.GetComponentsInChildren<Transform>(true).Where(t=>t!=rig.transform &&
                (t==rig.cabinet||t.name.StartsWith("UpperArm_")||t.name.StartsWith("Forearm_")||t.name.StartsWith("Hand_")||t.name.StartsWith("Thigh_")||t.name.StartsWith("Shin_")||t.name.StartsWith("Foot_")||
                new[]{"Thumb","Index","Middle","Ring","Little"}.Any(f=>t.name.StartsWith(f)))).ToArray();
            string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
            foreach(VendingMonsterState motion in Enum.GetValues(typeof(VendingMonsterState)))
            {
                if(motion==VendingMonsterState.Dead)continue;
                float duration=VendingMonsterRig.Duration(motion);int frames=Mathf.CeilToInt(duration*60);
                var curves=new List<Keyframe>[transforms.Length,10];for(int j=0;j<transforms.Length;j++)for(int k=0;k<10;k++)curves[j,k]=new List<Keyframe>();
                var previous=new Quaternion[transforms.Length];
                for(int f=0;f<=frames;f++)
                {
                    float t=duration*f/frames;rig.Sample(motion,t);
                    for(int j=0;j<transforms.Length;j++)
                    {
                        var tr=transforms[j];var q=tr.localRotation;
                        if(f>0&&Quaternion.Dot(q,previous[j])<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[j]=q;
                        var p=tr.localPosition;var s=tr.localScale;float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};
                        for(int k=0;k<10;k++)curves[j,k].Add(new Keyframe(t,values[k]));
                    }
                }
                string path=Folder+"/Animations/"+motion+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(!clip){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}clip.ClearCurves();clip.name=motion.ToString();clip.frameRate=60;
                for(int j=0;j<transforms.Length;j++)for(int k=0;k<10;k++)
                {
                    var keys=Simplify(curves[j,k],k<3?.00035f:.0008f);var curve=new AnimationCurve(keys.ToArray());
                    for(int n=0;n<curve.length;n++) {AnimationUtility.SetKeyLeftTangentMode(curve,n,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,n,AnimationUtility.TangentMode.Linear);}
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(transforms[j],rig.transform),typeof(Transform),props[k]),curve);
                }
                clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime=motion==VendingMonsterState.Idle||motion==VendingMonsterState.Chase||motion==VendingMonsterState.Charging||motion==VendingMonsterState.Dormant;
                settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
                AnimationUtility.SetAnimationEvents(clip,motion==VendingMonsterState.CanThrow?new[]{new AnimationEvent{time=VendingMonsterRig.CanGrabTime,functionName="GrabCan"},new AnimationEvent{time=VendingMonsterRig.CanReleaseTime,functionName="ReleaseCan"}}:new AnimationEvent[0]);
                var state=machine.AddState(motion.ToString());state.motion=clip;state.writeDefaultValues=true;
                if(motion==VendingMonsterState.Dormant)machine.defaultState=state;
            }
            animator.runtimeAnimatorController=controller;EditorUtility.SetDirty(controller);rig.RestoreRestPose();
        }
        static List<Keyframe> Simplify(List<Keyframe> keys,float tolerance)
        {
            var kept=new SortedSet<int>{0,keys.Count-1};
            void Segment(int a,int b)
            {
                float worst=tolerance;int index=-1;
                for(int i=a+1;i<b;i++){float t=(keys[i].time-keys[a].time)/(keys[b].time-keys[a].time);float error=Mathf.Abs(keys[i].value-Mathf.Lerp(keys[a].value,keys[b].value,t));if(error>worst){worst=error;index=i;}}
                if(index<0)return;kept.Add(index);Segment(a,index);Segment(index,b);
            }
            Segment(0,keys.Count-1);return kept.Select(i=>keys[i]).ToList();
        }
        static AudioClip Sound(string name,float duration,int kind)
        {
            string path=Folder+"/Audio/"+name+".wav";int sampleRate=22050,count=(int)(sampleRate*duration);
            var random=new System.Random(920+kind);
            using(var writer=new BinaryWriter(File.Open(path,FileMode.Create)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(sampleRate);writer.Write(sampleRate*2);writer.Write((short)2);writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
                for(int i=0;i<count;i++)
                {
                    float t=(float)i/sampleRate,u=t/duration,noise=(float)random.NextDouble()*2-1;
                    float env=Mathf.Sin(Mathf.PI*u)*Mathf.Exp(-u*(kind==0?1:4));
                    float frequency=kind==0?54+19*Mathf.Sin(t*4):kind==1?65:kind==2?870:kind==3?190:125;
                    float v=(Mathf.Sin(t*frequency*2*Mathf.PI)*.52f+Mathf.Sin(t*frequency*3.17f*2*Mathf.PI)*.15f+noise*(kind==3?.75f:.21f))*env*.55f;
                    writer.Write((short)(Mathf.Clamp(v,-1,1)*32760));
                }
            }
            AssetDatabase.ImportAsset(path);return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        [MenuItem("AC Roguelike/Liminal/Vending Monster/Create Playable Showcase")]
        public static string CreateShowcase()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="ShowcaseFloor";floor.transform.position=new Vector3(0,-.1f,2);floor.transform.localScale=new Vector3(18,.2f,26);
            floor.GetComponent<Renderer>().sharedMaterial=Material("ShowcaseTile",new Color(.5f,.51f,.43f));
            for(int side=-1;side<=1;side+=2)
            {
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="HallWall";wall.transform.position=new Vector3(side*8,2.2f,2);wall.transform.localScale=new Vector3(.2f,4.4f,26);
                wall.GetComponent<Renderer>().sharedMaterial=Material("ShowcaseWall",new Color(.65f,.62f,.43f));
            }
            for(int z=-8;z<=12;z+=5)
            {
                var lamp=new GameObject("FluorescentPool");lamp.transform.position=new Vector3(0,4,z);lamp.transform.rotation=Quaternion.Euler(90,0,0);
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.range=9;light.intensity=2;light.color=new Color(.85f,.95f,.84f);light.shadows=LightShadows.Soft;
            }
            var sun=new GameObject("SoftKey").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(45,-35,0);sun.shadows=LightShadows.Soft;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.36f,.39f,.38f);
            var monster=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);monster.transform.position=new Vector3(0,0,3);monster.transform.rotation=Quaternion.Euler(0,180,0);
            var playerSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Astraia/AstraiaPlayer.prefab");
            if(!playerSource)playerSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Explorer/LiminalExplorer.prefab");
            var player=(GameObject)PrefabUtility.InstantiatePrefab(playerSource,scene);player.name="ShowcasePlayer";player.transform.position=new Vector3(0,.05f,-6);
            if(!player.GetComponent<LiminalPlayerHealth>())player.AddComponent<LiminalPlayerHealth>();
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(7.5f,8,-10);camera.transform.LookAt(new Vector3(0,1,0));camera.orthographic=true;camera.orthographicSize=7;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.gameObject.AddComponent<AudioListener>();
            var follow=camera.gameObject.AddComponent<IsometricFollowCamera>();follow.target=player.transform;
            EditorSceneManager.SaveScene(scene,DemoPath);EditorSceneManager.CloseScene(scene,true);
            return DemoPath;
        }

        [MenuItem("AC Roguelike/Liminal/Vending Monster/Capture Poses")]
        public static string CapturePoses()
        {
            Directory.CreateDirectory(Review);
            var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("PosePreview");SceneManager.MoveGameObjectToScene(root,scene);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var creature=Object.Instantiate(prefab,root.transform);var monster=creature.GetComponent<VendingMonster>();monster.animator.enabled=false;
            foreach(var r in monster.limbRenderers)r.enabled=true;
            var camera=new GameObject("CaptureCamera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.transform.position=new Vector3(4.4f,2.5f,7);
            camera.scene=scene;
            camera.transform.LookAt(new Vector3(0,1.5f,0));camera.orthographic=true;camera.orthographicSize=1.95f;camera.nearClipPlane=.1f;camera.farClipPlane=50;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.14f);
            void Light(string n,Vector3 p,float intensity){var l=new GameObject(n).AddComponent<Light>();l.transform.SetParent(root.transform,false);l.type=LightType.Directional;l.intensity=intensity;l.transform.rotation=Quaternion.Euler(p);l.shadows=LightShadows.None;}
            Light("Key",new Vector3(40,-35,0),1.3f);Light("Fill",new Vector3(30,145,0),.8f);
            var rt=new RenderTexture(1080,1080,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
            try
            {
                var poses=new[]{(VendingMonsterState.Dormant,0f,"01_dormant"),(VendingMonsterState.Awakening,1.1f,"02_arms_emerge"),(VendingMonsterState.Awakening,2.2f,"03_legs_emerge"),
                    (VendingMonsterState.Idle,.3f,"04_awake"),(VendingMonsterState.Charging,.12f,"05_charge"),(VendingMonsterState.CanThrow,.88f,"06_grab"),(VendingMonsterState.CanThrow,1.57f,"07_throw_load"),(VendingMonsterState.CanThrow,1.81f,"08_release")};
                foreach(var pose in poses)
                {
                    monster.rig.Sample(pose.Item1,pose.Item2);
                    foreach(var r in monster.limbRenderers)r.enabled=pose.Item1!=VendingMonsterState.Dormant;
                    // Preview scenes do not tick the skinning player loop. Bake current bones for an exact still pose.
                    var bakedObjects=new List<GameObject>();var bakedMeshes=new List<Mesh>();
                    if(pose.Item1!=VendingMonsterState.Dormant)foreach(var renderer in monster.limbRenderers)
                    {
                        var skin=(SkinnedMeshRenderer)renderer;var mesh=new Mesh();skin.BakeMesh(mesh);
                        var o=new GameObject("CapturedSkin");o.transform.SetParent(skin.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;
                        o.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;bakedObjects.Add(o);bakedMeshes.Add(mesh);
                    }
                    GameObject can=null;if(pose.Item1==VendingMonsterState.CanThrow&&pose.Item2>=VendingMonsterRig.CanGrabTime){can=Object.Instantiate(monster.canPrefab,monster.canGrip);can.transform.localPosition=Vector3.zero;can.transform.localRotation=Quaternion.identity;}
                    for(int pass=0;pass<3;pass++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                    var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1080,1080,TextureFormat.RGB24,false);
                    tex.ReadPixels(new Rect(0,0,1080,1080),0,0);tex.Apply();File.WriteAllBytes(Review+"/"+pose.Item3+".png",tex.EncodeToPNG());RenderTexture.active=previous;Object.DestroyImmediate(tex);if(can)Object.DestroyImmediate(can);
                    foreach(var o in bakedObjects)Object.DestroyImmediate(o);foreach(var mesh in bakedMeshes)Object.DestroyImmediate(mesh);
                }
            }
            finally{camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
            return Review;
        }
    }
}
