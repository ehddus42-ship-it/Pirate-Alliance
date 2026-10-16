#if UNITY_EDITOR && MAGICACLOTH2
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MagicaCloth2;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class GBHNClothTests
{
    const string ReportFolder = "Documentation/GBHN/Validation";
    readonly List<GameObject> owned = new List<GameObject>();
    RenderTexture target;
    Action before, after;
    UnityEngine.SceneManagement.Scene showcase;
    [Serializable] class ClothResult { public string name; public int particles; public float maxDisplacementMetres, rootDriftMetres; }
    [Serializable] class Report
    {
        public string character, status, unityVersion, cpu, gpu;
        public int particles, sampleFrames, spawnCount;
        public double warmSimulationMedianMs, warmSimulationP95Ms, warmSimulationMaxMs, warmSpawnMedianMs, warmSpawnMaxMs;
        public bool distanceCull, distanceResume, cameraCull, cameraResume, teleportStable, disableResume, litVariant;
        public List<ClothResult> cloth = new List<ClothResult>();
    }
    [Serializable] class CrowdReport
    {
        public string status, cpu, gpu;
        public int characters, particles, samples;
        public double simulationMedianMs, simulationP95Ms, simulationMaxMs;
        public bool allCulled;
    }
    struct BoneSample { public Transform bone, anchor; public Vector3 position; public bool root; }

    [UnityTest, Timeout(360000)] public IEnumerator GeumbiClothMovesAndCulls() => Validate("Geumbi");
    [UnityTest, Timeout(360000)] public IEnumerator HaneulClothMovesAndCulls() => Validate("Haneul");

    GameObject Own(GameObject obj) { owned.Add(obj); return obj; }
    static GameObject Prefab(string name, bool lit=false) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/{name}/Magica/{name}_Magica{(lit?"_Lit":"")}.prefab");

    IEnumerator Validate(string name)
    {
        Directory.CreateDirectory(ReportFolder);
        Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        Time.captureDeltaTime=1f/60;
        var prefab=Prefab(name); Assert.NotNull(prefab);
        Assert.NotNull(Prefab(name,true));
        var camera=Own(new GameObject("Cloth Test Camera")).AddComponent<Camera>();camera.tag="MainCamera";
        target=new RenderTexture(1000,1000,24);camera.targetTexture=target;
        camera.fieldOfView=34;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.2f);
        var sun=Own(new GameObject("Sun")).AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.85f;sun.transform.rotation=Quaternion.Euler(30,20,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
        var obj=Own(Object.Instantiate(prefab));
        var animator=obj.GetComponent<Animator>();
        animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Characters/Geumbi/Animations/Geumbi_Walk.controller");
        animator.applyRootMotion=false;
        var cloths=obj.GetComponentsInChildren<MagicaCloth>();
        Assert.AreEqual(3,cloths.Length);
        var report=new Report{character=name,status="running",unityVersion=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName};
        var samples=new Dictionary<MagicaCloth,List<BoneSample>>();
        foreach(var c in cloths)
        {
            Assert.AreEqual(ClothProcess.ClothType.BoneCloth,c.SerializeData.clothType);
            var pre=c.GetSerializeData2().preBuildData;
            Assert.IsTrue(pre.UsePreBuild());Assert.IsFalse(pre.DataValidate().IsError(),c.name);
            int count=pre.GetSharePreBuildData().proxyMesh.attributes.count;
            report.particles+=count;report.cloth.Add(new ClothResult{name=c.name,particles=count});
            Assert.AreEqual(0,c.SerializeData.sourceRenderers.Count,"BoneCloth must not rewrite dense meshes");
            Assert.AreEqual(SelfCollisionConstraint.SelfCollisionMode.None,c.SerializeData.selfCollisionConstraint.selfMode);
            var list=new List<BoneSample>();
            foreach(var root in c.SerializeData.rootBones)
                foreach(var b in root.GetComponentsInChildren<Transform>())
                    list.Add(new BoneSample{bone=b,anchor=root.parent,position=root.parent.InverseTransformPoint(b.position),root=b==root});
            samples[c]=list;
        }
        Assert.LessOrEqual(report.particles,160);
        Action follow=()=> {camera.transform.position=obj.transform.position+new Vector3(0,1.1f,name=="Geumbi"?-3.5f:3.5f);camera.transform.LookAt(obj.transform.position+Vector3.up*.95f);};
        follow();
        float deadline=Time.realtimeSinceStartup+90;
        while(cloths.Any(c=>!c.Process.IsRunning()) && Time.realtimeSinceStartup<deadline)yield return null;
        foreach(var c in cloths)Assert.IsTrue(c.Process.IsRunning(),c.name+": "+c.Process.Result.GetResultString());
        for(int i=0;i<90;i++)yield return null;

        var timings=new List<double>();long began=0;bool measure=false;string pendingCapture=null;
        before=()=>began=Stopwatch.GetTimestamp();
        after=()=>
        {
            if(!measure)return;
            timings.Add((Stopwatch.GetTimestamp()-began)*1000.0/Stopwatch.Frequency);
            // BoneCloth restores animation transforms at the start of the next frame. Sample after
            // the simulation has written its results, rather than from the coroutine's Update phase.
            for(int ci=0;ci<cloths.Length;ci++) foreach(var sample in samples[cloths[ci]])
            {
                float displacement=Vector3.Distance(sample.bone.position,sample.anchor.TransformPoint(sample.position));
                if(float.IsNaN(displacement)||float.IsInfinity(displacement))displacement=float.PositiveInfinity;
                if(sample.root)report.cloth[ci].rootDriftMetres=Mathf.Max(report.cloth[ci].rootDriftMetres,displacement);
                else report.cloth[ci].maxDisplacementMetres=Mathf.Max(report.cloth[ci].maxDisplacementMetres,displacement);
            }
            if(pendingCapture!=null){Capture(camera,pendingCapture);pendingCapture=null;}
        };
        MagicaManager.OnPreSimulation+=before;MagicaManager.OnPostSimulation+=after;
        for(int frame=0;frame<540;frame++)
        {
            // Walk, run, turn and stop. Root movement and the supplied humanoid clip both drive cloth.
            float t=frame/60f;
            if(frame<360)
            {
                float speed=frame<120?1.4f:3.5f;
                float turn=frame<240?0:(frame-240)*1.5f;
                obj.transform.rotation=Quaternion.Euler(0,turn,0);
                obj.transform.position+=obj.transform.forward*(speed/60);
            }
            follow();measure=frame>=60;
            if(frame==150 || frame==330)pendingCapture=Path.Combine(ReportFolder,name+(frame==150?"-walk.png":"-turn.png"));
            yield return null;
            measure=false;
            for(int ci=0;ci<cloths.Length;ci++)
            {
                var c=cloths[ci];
                Assert.IsFalse(c.Process.Result.IsError(),c.name);
                Assert.Less(report.cloth[ci].maxDisplacementMetres,c.SerializeData.motionConstraint.maxDistance.value+.06f,c.name+" stability");
            }
            if(frame==150 || frame==330)
            {
                CheckMeshes(obj);
                Assert.IsNull(pendingCapture,"Capture the simulated pose after the cloth step");
            }
        }
        foreach(var c in report.cloth)
        {
            Assert.Greater(c.maxDisplacementMetres,.001f,c.name+" must move relative to its animated anchor");
            Assert.Less(c.rootDriftMetres,.001f,c.name+" root must stay attached");
        }
        report.sampleFrames=timings.Count;Assert.Greater(timings.Count,300);
        timings.Sort();report.warmSimulationMedianMs=Percentile(timings,.5);report.warmSimulationP95Ms=Percentile(timings,.95);report.warmSimulationMaxMs=timings.Last();
        // This is measured between MC2 pre/post simulation events, including the completed worker jobs,
        // excluding rendering, inspector gizmos, screenshots, warm-up/JIT, and mesh validation.

        camera.transform.position=obj.transform.position+new Vector3(0,2,-40);camera.transform.LookAt(obj.transform.position+Vector3.up);
        for(int i=0;i<45;i++)yield return null;
        report.distanceCull=cloths.All(c=>Team(c).IsDistanceCullingInvisible && !Team(c).IsProcess);
        Assert.IsTrue(report.distanceCull,"Distance culling must stop processing");
        follow();for(int i=0;i<45;i++)yield return null;
        report.distanceResume=cloths.All(c=>!Team(c).IsDistanceCullingInvisible && Team(c).IsProcess);
        Assert.IsTrue(report.distanceResume,"Return from distance culling");

        camera.transform.rotation*=Quaternion.Euler(0,180,0);
        for(int i=0;i<45;i++)yield return null;
        report.cameraCull=cloths.All(c=>Team(c).IsCameraCullingInvisible && !Team(c).IsProcess);
        Assert.IsTrue(report.cameraCull,"Offscreen culling must stop processing");
        follow();for(int i=0;i<45;i++)yield return null;
        report.cameraResume=cloths.All(c=>!Team(c).IsCameraCullingInvisible && Team(c).IsProcess);
        Assert.IsTrue(report.cameraResume);

        obj.transform.position+=Vector3.right*35;follow();
        for(int i=0;i<45;i++)yield return null;
        CheckMeshes(obj);report.teleportStable=cloths.All(c=>samples[c].All(s=>Vector3.Distance(s.bone.position,s.anchor.TransformPoint(s.position))<.2f));Assert.IsTrue(report.teleportStable);
        obj.SetActive(false);yield return null;obj.SetActive(true);
        for(int i=0;i<60;i++)yield return null;
        report.disableResume=cloths.All(c=>c.Process.IsRunning() && Team(c).IsProcess);Assert.IsTrue(report.disableResume);

        var spawn=new List<double>();
        // AssetDatabase and shader loading are editor cold-load costs, not a warm prefab spawn.
        var litPrefab=Prefab(name,true);
        var warm=Own(Object.Instantiate(litPrefab));warm.transform.position=obj.transform.position;
        for(int i=0;i<45;i++)yield return null;
        Object.Destroy(warm);yield return null;
        // Sequential warm spawns also catch missing remapped bones in prefab prebuild references.
        for(int n=0;n<4;n++)
        {
            long start=Stopwatch.GetTimestamp();var clone=Own(Object.Instantiate(n==3?litPrefab:prefab));
            clone.transform.position=obj.transform.position;obj.SetActive(false);
            var cloneCloths=clone.GetComponentsInChildren<MagicaCloth>();
            float until=Time.realtimeSinceStartup+30;
            while(cloneCloths.Any(c=>!c.Process.IsRunning()) && Time.realtimeSinceStartup<until)yield return null;
            spawn.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);
            foreach(var c in cloneCloths)Assert.IsTrue(c.Process.IsRunning(),c.name);
            for(int i=0;i<20;i++)yield return null;
            CheckMeshes(clone);
            if(n==3) {report.litVariant=true;Capture(camera,Path.Combine(ReportFolder,name+"-lit.png"));}
            Object.Destroy(clone);yield return null;
        }
        spawn.Sort();report.spawnCount=spawn.Count;report.warmSpawnMedianMs=Percentile(spawn,.5);report.warmSpawnMaxMs=spawn.Last();
        report.status="passed";File.WriteAllText(Path.Combine(ReportFolder,name+".json"),JsonUtility.ToJson(report,true));
    }

    [UnityTest, Timeout(360000)] public IEnumerator FourCharactersStayWithinClothBudget()
    {
        Directory.CreateDirectory(ReportFolder);
        Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously=true;
        Time.captureDeltaTime=1f/60;
        var camera=Own(new GameObject("Crowd Camera")).AddComponent<Camera>();camera.tag="MainCamera";
        target=new RenderTexture(1280,720,24);camera.targetTexture=target;
        camera.transform.position=new Vector3(0,1.3f,-6);camera.transform.LookAt(Vector3.up);camera.fieldOfView=45;
        var models=new List<GameObject>();var cloths=new List<MagicaCloth>();
        for(int i=0;i<4;i++)
        {
            var model=Own(Object.Instantiate(Prefab(i%2==0?"Geumbi":"Haneul")));
            model.transform.position=new Vector3((i-1.5f)*1.1f,0,0);models.Add(model);
            cloths.AddRange(model.GetComponentsInChildren<MagicaCloth>());
        }
        for(int i=0;i<120;i++)yield return null;
        Assert.IsTrue(cloths.All(c=>c.Process.IsRunning()&&Team(c).IsProcess));
        var report=new CrowdReport{status="running",characters=4,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,
            particles=cloths.Sum(c=>Team(c).ParticleCount)};
        Assert.AreEqual(258,report.particles);
        var timings=new List<double>();long start=0;
        before=()=>start=Stopwatch.GetTimestamp();
        after=()=>timings.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);
        MagicaManager.OnPreSimulation+=before;MagicaManager.OnPostSimulation+=after;
        for(int frame=0;frame<360;frame++)
        {
            for(int i=0;i<models.Count;i++)models[i].transform.rotation=Quaternion.Euler(0,Mathf.Sin(frame/60f*2+i)*60,0);
            yield return null;
        }
        MagicaManager.OnPreSimulation-=before;MagicaManager.OnPostSimulation-=after;before=null;after=null;
        timings.Sort();report.samples=timings.Count;report.simulationMedianMs=Percentile(timings,.5);report.simulationP95Ms=Percentile(timings,.95);report.simulationMaxMs=timings.Last();
        Assert.Greater(report.samples,300);
        Assert.Less(report.simulationP95Ms,4,"Four-character MC2 simulation should stay below a 4ms P95 budget on the validation machine");
        camera.transform.position+=Vector3.back*40;
        for(int i=0;i<45;i++)yield return null;
        report.allCulled=cloths.All(c=>Team(c).IsDistanceCullingInvisible&&!Team(c).IsProcess);Assert.IsTrue(report.allCulled);
        report.status="passed";File.WriteAllText(Path.Combine(ReportFolder,"four-characters.json"),JsonUtility.ToJson(report,true));
    }

    [UnityTest, Timeout(180000)] public IEnumerator ShowcaseSceneRuns()
    {
        Directory.CreateDirectory(ReportFolder);
        Time.captureDeltaTime=1f/60;
        showcase=UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Characters/GBHN/GBHN_ClothShowcase.unity",
            new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
        yield return null;
        var roots=showcase.GetRootGameObjects();
        var camera=roots.SelectMany(r=>r.GetComponentsInChildren<Camera>()).Single();
        var cloths=roots.SelectMany(r=>r.GetComponentsInChildren<MagicaCloth>()).ToArray();
        Assert.AreEqual(6,cloths.Length);
        foreach(var root in roots)foreach(var t in root.GetComponentsInChildren<Transform>())
            Assert.AreEqual(0,GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        target=new RenderTexture(1600,1000,24);camera.targetTexture=target;
        bool capture=false, captured=false;
        after=()=> {if(capture){Capture(camera,Path.Combine(ReportFolder,"showcase.png"));capture=false;captured=true;}};
        MagicaManager.OnPostSimulation+=after;
        for(int frame=0;frame<150;frame++){capture=frame==120;yield return null;}
        Assert.IsTrue(captured);
        Assert.IsTrue(cloths.All(c=>c.Process.IsRunning()&&!c.Process.Result.IsError()));
        foreach(var root in roots)CheckMeshes(root);
    }

    static TeamManager.TeamData Team(MagicaCloth c)=>MagicaManager.Team.GetTeamDataRef(c.Process.TeamId);
    static double Percentile(List<double> ordered,double p)=>ordered[Mathf.Clamp((int)Math.Ceiling(ordered.Count*p)-1,0,ordered.Count-1)];
    static void CheckMeshes(GameObject root)
    {
        var bake=new Mesh();
        try
        {
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Assert.IsTrue(skin.bones.All(b=>b),skin.name+" missing bone");
                skin.BakeMesh(bake);
                foreach(var v in bake.vertices)
                {
                    Assert.IsFalse(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.sqrMagnitude),skin.name+" finite vertices");
                    Assert.Less(v.magnitude,4,skin.name+" exploded mesh");
                }
            }
        }
        finally{Object.Destroy(bake);}
    }
    static void Capture(Camera camera,string path)
    {
        var rt=camera.targetTexture;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        var prior=RenderTexture.active;
        try {camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally {RenderTexture.active=prior;Object.Destroy(tex);}
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        MagicaManager.OnPreSimulation-=before;MagicaManager.OnPostSimulation-=after;before=null;after=null;
        foreach(var obj in owned)if(obj)Object.Destroy(obj);owned.Clear();Time.captureDeltaTime=0;
        if(showcase.IsValid()&&showcase.isLoaded)yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(showcase);
        showcase=default;
        yield return null;
        if(target)Object.Destroy(target);target=null;
    }
}
#endif
