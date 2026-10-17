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

namespace MoonLuna.Magica.Tests
{
    public class LunaBattlePerformanceTests
    {
        const string Folder = "Documentation/MoonLuna/Performance/Runtime";
        const string Prefab = "Assets/Characters/MoonLuna/Stage/MoonLuna_Battle.prefab";
        [Serializable] class Timing { public double medianMs, p95Ms, maxMs; }
        [Serializable] class Phase
        {
            public string name; public int frames;
            public Timing frame, simulation, clothAndFollowers;
        }
        [Serializable] class Report
        {
            public string label, unity, cpu, gpu;
            public double instantiateMs, readyMs;
            public int cloths, prebuiltCloths, particles, renderVertices;
            public List<Phase> phases = new List<Phase>();
        }
        GameObject model, cameraObject, lightObject;
        RenderTexture texture;
        Action before, after;
        MagicaManager.UpdateMethod delayed;
        bool oldBurstSync;
        float oldCaptureDelta;
        static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        static Timing Summarize(List<double> values)
        {
            if (values.Count == 0) return new Timing();
            values.Sort();
            return new Timing { medianMs = values[values.Count / 2], p95Ms = values[Math.Min(values.Count - 1, (int)(values.Count * .95))], maxMs = values[values.Count - 1] };
        }

        [SetUp] public void SaveSettings()
        {
            oldBurstSync = Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            oldCaptureDelta = Time.captureDeltaTime;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator MeasureBattleClothCost()
        {
            Directory.CreateDirectory(Folder);
            oldBurstSync = Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            oldCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            cameraObject = new GameObject("Luna Performance Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.2f, 4.8f);
            camera.transform.LookAt(new Vector3(0, 1, 0)); camera.fieldOfView = 34;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .18f, .23f);
            texture = new RenderTexture(1280, 720, 24); camera.targetTexture = texture;
            lightObject = new GameObject("Luna Performance Light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.IsNotNull(prefab);
            var report = new Report { label = Environment.GetEnvironmentVariable("LUNA_BENCHMARK_LABEL") ?? "current", unity = Application.unityVersion, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName };
            long spawn = Stopwatch.GetTimestamp();
            model = Object.Instantiate(prefab);
            report.instantiateMs = Milliseconds(Stopwatch.GetTimestamp() - spawn);
            var cloths = model.GetComponentsInChildren<MagicaCloth>();
            var followers = model.GetComponentsInChildren<LunaClothFollower>();
            report.cloths = cloths.Length;
            report.prebuiltCloths = cloths.Count(c => c.GetSerializeData2().preBuildData.UsePreBuild());
            report.renderVertices = cloths.Sum(c => c.SerializeData.sourceRenderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount));
            float deadline = Time.realtimeSinceStartup + 180;
            while (cloths.Any(c => !c.Process.IsRunning()) && Time.realtimeSinceStartup < deadline) yield return null;
            report.readyMs = Milliseconds(Stopwatch.GetTimestamp() - spawn);
            foreach (var cloth in cloths) Assert.IsTrue(cloth.Process.IsRunning(), cloth.name + ": " + cloth.Process.Result.GetResultString());
            report.particles = cloths.Sum(c => MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).ParticleCount);
            for (int frame = 0; frame < 120; frame++) yield return null;

            foreach (string name in new[] { "full", "without_followers", "without_cloth" })
            {
                if (name != "full") foreach (var follower in followers) follower.enabled = false;
                if (name == "without_cloth") foreach (var cloth in cloths) cloth.enabled = false;
                for (int i = 0; i < 30; i++) yield return null;
                var frames = new List<double>(); var simulation = new List<double>(); var total = new List<double>();
                long began = 0; bool sampled = false;
                before = () => { began = Stopwatch.GetTimestamp(); sampled = true; };
                after = () => { if (sampled) simulation.Add(Milliseconds(Stopwatch.GetTimestamp() - began)); };
                delayed = () => { if (sampled) total.Add(Milliseconds(Stopwatch.GetTimestamp() - began)); sampled = false; };
                MagicaManager.OnPreSimulation += before; MagicaManager.OnPostSimulation += after; MagicaManager.afterDelayedDelegate += delayed;
                for (int frame = 0; frame < 360; frame++)
                {
                    float t = frame / 60f;
                    model.transform.SetPositionAndRotation(new Vector3(Mathf.Sin(t * 1.5f) * .4f, 0, Mathf.Sin(t) * .2f), Quaternion.Euler(0, Mathf.Sin(t * 2) * 65, 0));
                    long start = Stopwatch.GetTimestamp();
                    yield return null;
                    frames.Add(Milliseconds(Stopwatch.GetTimestamp() - start));
                }
                MagicaManager.OnPreSimulation -= before; MagicaManager.OnPostSimulation -= after; MagicaManager.afterDelayedDelegate -= delayed;
                before = after = null; delayed = null;
                report.phases.Add(new Phase { name = name, frames = frames.Count, frame = Summarize(frames), simulation = Summarize(simulation), clothAndFollowers = Summarize(total) });
                if (name == "full")
                {
                    foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                        foreach (var v in filter.sharedMesh.vertices) Assert.IsFalse(float.IsNaN(v.sqrMagnitude) || float.IsInfinity(v.sqrMagnitude), filter.name);
                    var prior = RenderTexture.active;
                    var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                    camera.Render(); RenderTexture.active = texture;
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.Combine(Folder, report.label + ".png"), image.EncodeToPNG());
                    RenderTexture.active = prior; Object.Destroy(image);
                }
            }
            File.WriteAllText(Path.Combine(Folder, report.label + ".json"), JsonUtility.ToJson(report, true));
            Assert.AreEqual(9, report.cloths);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator BattleUsesBakedDataAndResumesAfterCulling()
        {
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            Time.captureDeltaTime = 1f / 60f;
            cameraObject = new GameObject("Luna Lifecycle Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.2f, 4.8f); camera.transform.LookAt(Vector3.up);
            texture = new RenderTexture(640, 480, 24); camera.targetTexture = texture;
            model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            var cloths = model.GetComponentsInChildren<MagicaCloth>();
            Assert.AreEqual(9, cloths.Length);
            foreach (var cloth in cloths)
            {
                var data = cloth.GetSerializeData2().preBuildData;
                Assert.IsTrue(data.UsePreBuild(), cloth.name);
                Assert.IsFalse(data.DataValidate().IsError(), cloth.name);
            }
            foreach (var follower in model.GetComponentsInChildren<LunaClothFollower>())
                Assert.IsTrue(follower.UsesBakedBinding, "Character activation must not rebuild attachment weights");
            for (int i = 0; i < 90; i++) yield return null;
            Assert.IsTrue(cloths.All(c => c.Process.IsRunning()));
            Assert.LessOrEqual(cloths.Sum(c => MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).ParticleCount), 2500);
            camera.transform.position += Vector3.forward * 40;
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(cloths.All(c => !MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).IsProcess), "Far cloth must stop");
            foreach (var follower in model.GetComponentsInChildren<LunaClothFollower>())
                for (int i = 0; i < follower.followers.Length; i++)
                {
                    var original = follower.bindingData.parts[i].source.vertices;
                    var actual = follower.followers[i].GetComponent<MeshFilter>().sharedMesh.vertices;
                    for (int v = 0; v < original.Length; v++) Assert.Less(Vector3.Distance(original[v], actual[v]), .00001f, "Far ornaments must reset with the skirt");
                }
            camera.transform.position = new Vector3(0, 1.2f, 4.8f);
            for (int i = 0; i < 45; i++) yield return null;
            Assert.IsTrue(cloths.All(c => MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).IsProcess), "Cloth must resume");
            camera.transform.rotation *= Quaternion.Euler(0, 180, 0);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(cloths.All(c => !MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).IsProcess), "Offscreen cloth must stop");
            camera.transform.LookAt(Vector3.up);
            model.SetActive(false); yield return null; model.SetActive(true);
            model.transform.position = Vector3.right * 25; camera.transform.position += Vector3.right * 25;
            for (int i = 0; i < 60; i++) yield return null;
            Assert.IsTrue(cloths.All(c => c.Process.IsRunning() && MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).IsProcess));
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                foreach (var v in filter.sharedMesh.vertices)
                {
                    Assert.IsFalse(float.IsNaN(v.sqrMagnitude) || float.IsInfinity(v.sqrMagnitude));
                    Assert.Less(Vector3.Distance(model.transform.position, filter.transform.TransformPoint(v)), 4, filter.name + " teleport deformation");
                }
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            MagicaManager.OnPreSimulation -= before; MagicaManager.OnPostSimulation -= after; MagicaManager.afterDelayedDelegate -= delayed;
            before = after = null; delayed = null;
            if (model) Object.Destroy(model); if (cameraObject) Object.Destroy(cameraObject); if (lightObject) Object.Destroy(lightObject);
            yield return null;
            if (texture) Object.Destroy(texture);
            Time.captureDeltaTime = oldCaptureDelta;
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = oldBurstSync;
        }
    }
}
#endif
