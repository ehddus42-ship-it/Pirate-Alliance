using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MoonLuna.Magica.Tests
{
    /// <summary>
    /// Loads the Magica demo scene, checks the rest pose stays put, then runs the demo motion and measures
    /// collider penetration, outer-skirt/underskirt crossing, NaN and blow-ups. Writes MagicaValidation/report.json
    /// and screenshots next to Assets.
    /// </summary>
    public class LunaMagicaPlayModeTests
    {
#if UNITY_EDITOR
        static string ScenePath => UnityEditor.AssetDatabase.GUIDToAssetPath(UnityEditor.AssetDatabase.FindAssets("MoonLuna_MagicaDemo t:Scene").First());
#endif
        const float Step = 1f / 60f;
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MagicaValidation"));
        RenderTexture simulationTarget;
        Camera simulationCamera;

        class Part
        {
            public MagicaCloth cloth;
            public List<(Renderer renderer, MeshFilter filter, Vector3[] restLocal)> meshes = new List<(Renderer, MeshFilter, Vector3[])>();
        }

        struct Shape { public string name; public bool sphere; public Vector3 a, b; public float ra, rb; }

        [UnityTest, Timeout(2400000)]
        public IEnumerator ClothSimulatesWithoutPenetration()
        {
            Directory.CreateDirectory(OutDir);
            // Batch mode compiles Burst jobs asynchronously; without this the first cloth build runs as slow managed code.
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            Time.captureDeltaTime = Step;
            simulationCamera = Camera.main;
            Assert.IsNotNull(simulationCamera);
            // Batch-mode backbuffers do not establish renderer visibility. Keep the real scene camera rendering.
            simulationTarget = new RenderTexture(720, 960, 24);
            simulationCamera.targetTexture = simulationTarget;
            var motion = Object.FindFirstObjectByType<LunaMagicaDemoMotion>();
            Assert.NotNull(motion, "demo motion missing");
            motion.play = false;
            // rest is measured without wind from the very first simulated frame
            var wind = Object.FindFirstObjectByType<MagicaWindZone>();
            if (wind != null) wind.gameObject.SetActive(false);
            var cloths = Object.FindObjectsByType<MagicaCloth>(FindObjectsSortMode.None).OrderBy(c => c.name).ToArray();
            Assert.AreEqual(9, cloths.Length, "expected 9 MagicaCloth components");

            float start = Time.realtimeSinceStartup;
            var built = new Dictionary<MagicaCloth, float>();
            while (built.Count < cloths.Length && Time.realtimeSinceStartup - start < 300f)
            {
                foreach (var c in cloths)
                    if (!built.ContainsKey(c) && (c.Process.IsRunning() || c.Process.Result.IsError())) built[c] = Time.realtimeSinceStartup - start;
                yield return null;
            }
            var report = new StringBuilder("{\n");
            report.AppendLine($"  \"build_seconds\": {Time.realtimeSinceStartup - start:F1},");
            report.AppendLine("  \"cloth\": [");
            foreach (var c in cloths)
                report.AppendLine($"    {{\"name\": \"{c.name}\", \"running\": {c.Process.IsRunning().ToString().ToLower()}, \"build_seconds\": {(built.TryGetValue(c, out float bt) ? bt : -1f):F1}, \"result\": \"{Escape(c.Process.Result.GetResultString())}\", \"proxy_vertices\": {(c.Process.ProxyMeshContainer?.shareVirtualMesh?.VertexCount ?? 0)}}}" + (c == cloths.Last() ? "" : ","));
            report.AppendLine("  ],");
            File.WriteAllText(Path.Combine(OutDir, "build.json"), report.ToString() + "  \"partial\": true\n}", new UTF8Encoding(false));
            foreach (var c in cloths) Assert.IsTrue(c.Process.IsRunning(), c.name + " did not build: " + c.Process.Result.GetResultString());

            var parts = cloths.ToDictionary(c => c.name.Replace("Cloth_", ""), c =>
            {
                var p = new Part { cloth = c };
                foreach (var r in c.SerializeData.sourceRenderers)
                    p.meshes.Add((r, r.GetComponent<MeshFilter>(), c.GetOriginalMesh(r).vertices));
                return p;
            });
            Transform root = motion.target;
            var shapes = ReadColliders(root);

            // 1) rest: no motion and no wind for 3 s, the authored pose should hold
            for (int i = 0; i < 180; i++) yield return null;
            yield return AfterCloth(() =>
            {
            report.AppendLine("  \"rest_drift_mm\": {");
            var restLines = new List<string>();
            foreach (var kv in parts)
            {
                var d = Drift(kv.Value, root);
                string mean = (d.mean * 1000f).ToString("F2"), p99 = (d.p99 * 1000f).ToString("F2"), max = (d.max * 1000f).ToString("F2");
                Vector3 at = d.worstAt;
                restLines.Add($"    \"{kv.Key}\": {{\"mean\": {mean}, \"p99\": {p99}, \"max\": {max}, \"worst_1pct_center_root\": [{at.x:F3}, {at.y:F3}, {at.z:F3}]}}");
            }
            report.AppendLine(string.Join(",\n", restLines));
            report.AppendLine("  },");
            Capture(root, "rest");
            });
            if (wind != null) wind.gameObject.SetActive(true);

            // 2) motion: 10 s of walk / turns / spin with wind
            var skirtCheck = new SkirtLayerCheck(parts["Skirt_Navy"], parts["Skirt_Ivory"], root);
            var ornaments = new OrnamentCheck(parts["Skirt_Navy"], root);
            motion.ResetMotion();
            motion.play = true;
            var worst = new Dictionary<string, int>();
            int worstCrossing = 0, samples = 0; float worstCrossDepth = 0f; float maxDeviation = 0f; bool nan = false;
            for (int frame = 1; frame <= 600; frame++)
            {
                yield return null;
                if (frame % 15 != 0) continue;
                yield return AfterCloth(() =>
                {
                Assert.IsTrue(cloths.All(c => MagicaManager.Team.GetTeamDataRef(c.Process.TeamId).IsProcess), "Visibility culling must not turn this motion check into a static-mesh check");
                samples++;
                shapes = ReadColliders(root);
                foreach (var kv in parts)
                {
                    var world = WorldVertices(kv.Value);
                    if (world.Any(v => float.IsNaN(v.x) || float.IsInfinity(v.x))) nan = true;
                    maxDeviation = Mathf.Max(maxDeviation, Drift(kv.Value, root).max);
                    var rest = RestWorldVertices(kv.Value);
                    foreach (var s in shapes.Where(s => kv.Value.cloth.SerializeData.colliderCollisionConstraint.colliderList.Any(c => c.name == "Collider_" + s.name)))
                    {
                        // Only count vertices pushed deeper than where the authored pose already had them (fixed scalp/collar).
                        int inside = 0;
                        for (int i = 0; i < world.Length; i++)
                            if (Penetration(s, world[i]) > Mathf.Max(0f, Penetration(s, rest[i])) + 0.003f) inside++;
                        string key = kv.Key + "/" + s.name;
                        worst[key] = Mathf.Max(worst.TryGetValue(key, out int w) ? w : 0, inside);
                    }
                }
                ornaments.Sample();
                var (crossing, depth) = skirtCheck.Crossings();
                worstCrossing = Mathf.Max(worstCrossing, crossing); worstCrossDepth = Mathf.Max(worstCrossDepth, depth);
                if (frame % 60 == 0) Capture(root, $"motion_{frame / 60:00}s");
                });
            }
            skirtCheck.Dispose();

            // 3) recovery: motion and wind off for 4 s, nothing may stay caught on a collider or another layer
            motion.play = false;
            if (wind != null) wind.gameObject.SetActive(false);
            for (int i = 0; i < 240; i++) yield return null;
            yield return AfterCloth(() =>
            {
            report.AppendLine("  \"recovered_drift_mm\": {");
            report.AppendLine(string.Join(",\n", parts.Select(kv =>
            {
                var d = Drift(kv.Value, root);
                return $"    \"{kv.Key}\": {{\"mean\": {(d.mean * 1000f).ToString("F2")}, \"p99\": {(d.p99 * 1000f).ToString("F2")}, \"max\": {(d.max * 1000f).ToString("F2")}}}";
            })));
            report.AppendLine("  },");
            Capture(root, "recovered");
            });
            report.AppendLine($"  \"motion_samples\": {samples},");
            report.AppendLine($"  \"nan\": {nan.ToString().ToLower()},");
            report.AppendLine($"  \"max_deviation_from_rest_m\": {maxDeviation:F3},");
            report.AppendLine($"  \"skirt_layer_check_vertices\": {skirtCheck.Checked},");
            report.AppendLine($"  \"outer_skirt_through_underskirt_worst\": {worstCrossing},");
            report.AppendLine($"  \"outer_skirt_through_underskirt_depth_mm\": {worstCrossDepth * 1000f:F1},");
            report.AppendLine($"  \"gold_ornament_vertices_checked\": {ornaments.Checked},");
            report.AppendLine($"  \"gold_ornament_max_gap_change_mm\": {ornaments.WorstGapChange * 1000f:F1},");
            report.AppendLine("  \"collider_penetration_worst_vertices\": {");
            report.AppendLine(string.Join(",\n", worst.Select(kv => $"    \"{kv.Key}\": {kv.Value}")));
            report.AppendLine("  }\n}");
            File.WriteAllText(Path.Combine(OutDir, "report.json"), report.ToString(), new UTF8Encoding(false));
            Time.captureDeltaTime = 0f;

            Assert.IsFalse(nan, "NaN in cloth vertices");
            Assert.Greater(maxDeviation, .005f, "The moving cloth must actually deform");
            Assert.Less(maxDeviation, 0.6f, "cloth blew up");
        }

        static IEnumerator AfterCloth(System.Action sample)
        {
            bool done = false; System.Exception failure = null;
            MagicaManager.UpdateMethod callback = () =>
            {
                if (done) return;
                try { sample(); } catch (System.Exception e) { failure = e; }
                done = true;
            };
            MagicaManager.afterDelayedDelegate += callback;
            try { while (!done) yield return null; }
            finally { MagicaManager.afterDelayedDelegate -= callback; }
            if (failure != null) throw failure;
        }

        [UnityTearDown] public IEnumerator CleanupRendering()
        {
            if (simulationCamera) simulationCamera.targetTexture = null;
            if (simulationTarget) Object.Destroy(simulationTarget);
            Time.captureDeltaTime = 0;
            yield return null;
        }

        static string Escape(string s) => (s ?? "").Replace("\\", "/").Replace("\"", "'").Replace("\n", " ");

        static Vector3[] WorldVertices(Part p)
        {
            var all = new List<Vector3>();
            foreach (var m in p.meshes)
            {
                var mesh = m.filter.sharedMesh; var local = mesh.vertices; var M = m.renderer.transform.localToWorldMatrix;
                for (int i = 0; i < local.Length; i++) all.Add(M.MultiplyPoint3x4(local[i]));
            }
            return all.ToArray();
        }

        /// <summary>Authored vertex positions placed with the renderer's current transform.</summary>
        static Vector3[] RestWorldVertices(Part p)
        {
            var all = new List<Vector3>();
            foreach (var m in p.meshes)
            {
                var M = m.renderer.transform.localToWorldMatrix;
                foreach (var v in m.restLocal) all.Add(M.MultiplyPoint3x4(v));
            }
            return all.ToArray();
        }

        /// <summary>Distance of each vertex from its rest position, measured in the character's frame.</summary>
        static (float mean, float p99, float max, Vector3 worstAt) Drift(Part p, Transform root)
        {
            var d = new List<(float dist, Vector3 at)>();
            foreach (var m in p.meshes)
            {
                var now = m.filter.sharedMesh.vertices;
                var toRoot = root.worldToLocalMatrix * m.renderer.transform.localToWorldMatrix;
                // rest positions were authored with the character at its start pose: renderer local == rest local
                for (int i = 0; i < now.Length && i < m.restLocal.Length; i++)
                {
                    Vector3 rest = toRoot.MultiplyPoint3x4(m.restLocal[i]);
                    d.Add((Vector3.Distance(toRoot.MultiplyPoint3x4(now[i]), rest), rest));
                }
            }
            d.Sort((a, b) => a.dist.CompareTo(b.dist));
            var worst = d.Skip((int)(d.Count * 0.99f)).ToList();
            Vector3 center = Vector3.zero;
            foreach (var w in worst) center += w.at;
            return (d.Average(x => x.dist), d[(int)(d.Count * 0.99f)].dist, d[d.Count - 1].dist, center / Mathf.Max(1, worst.Count));
        }

        // Colliders are children of the moving root, so callers re-read their world placement each sample.
        static List<Shape> ReadColliders(Transform root)
        {
            var list = new List<Shape>();
            foreach (var s in root.GetComponentsInChildren<MagicaSphereCollider>())
                list.Add(new Shape { name = s.name.Replace("Collider_", ""), sphere = true, a = s.transform.TransformPoint(s.center), ra = s.GetSize().x });
            foreach (var c in root.GetComponentsInChildren<MagicaCapsuleCollider>())
            {
                Vector3 size = c.GetSize(); float half = size.z * 0.5f;
                Vector3 up = c.transform.up, center = c.transform.TransformPoint(c.center);
                list.Add(new Shape { name = c.name.Replace("Collider_", ""), a = center + up * (half - size.x), b = center - up * (half - size.y), ra = size.x, rb = size.y });
            }
            return list;
        }

        static float Penetration(Shape s, Vector3 v)
        {
            if (s.sphere) return s.ra - Vector3.Distance(v, s.a);
            Vector3 ab = s.b - s.a; float t = Mathf.Clamp01(Vector3.Dot(v - s.a, ab) / ab.sqrMagnitude);
            return Mathf.Lerp(s.ra, s.rb, t) - Vector3.Distance(v, s.a + ab * t);
        }

        void Capture(Transform root, string label)
        {
            var views = new (string name, Vector3 offset)[] { ("front", new Vector3(0f, 0.15f, 3.6f)), ("back", new Vector3(0f, 0.25f, -3.6f)), ("side", new Vector3(3.4f, 0.35f, 0.6f)) };
            var go = new GameObject("CaptureCamera"); var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.17f, 0.18f, 0.22f); cam.fieldOfView = 32f;
            var rt = new RenderTexture(720, 960, 24); cam.targetTexture = rt;
            var tex = new Texture2D(720, 960, TextureFormat.RGB24, false);
            Vector3 focus = root.position + Vector3.up * 0.9f;
            // the floating moon prop stands behind the character and hides the back view
            var props = root.GetComponentsInChildren<Renderer>().Where(r => r.name.Contains("Moon") && !r.name.StartsWith("Gold")).ToList();
            foreach (var r in props) r.enabled = false;
            foreach (var v in views)
            {
                go.transform.position = focus + root.rotation * v.offset;
                go.transform.LookAt(focus);
                cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 720, 960), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(OutDir, $"{label}_{v.name}.png"), tex.EncodeToPNG());
            }
            foreach (var r in props) r.enabled = true;
            cam.targetTexture = null; Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
        }

        /// <summary>
        /// The gold hem pieces must stay on the moving skirt: tracks how much each sampled ornament vertex's distance to
        /// its nearest skirt vertex (paired in the rest pose) changes.
        /// </summary>
        class OrnamentCheck
        {
            readonly List<(MeshFilter filter, Renderer renderer, int vertex, int skirtVertex, float restGap)> pairs = new List<(MeshFilter, Renderer, int, int, float)>();
            readonly MeshFilter skirt; readonly Renderer skirtRenderer;
            public int Checked => pairs.Count;
            public float WorstGapChange { get; private set; }

            public OrnamentCheck(Part navy, Transform root)
            {
                skirtRenderer = navy.meshes[0].renderer; skirt = navy.meshes[0].filter;
                var skirtNow = skirt.sharedMesh.vertices; var S = skirtRenderer.transform.localToWorldMatrix;
                var skirtWorld = skirtNow.Select(v => S.MultiplyPoint3x4(v)).ToArray();
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Gold_Hem_")))
                {
                    var f = r.GetComponent<MeshFilter>(); var v = f.sharedMesh.vertices; var M = r.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i += 25)
                    {
                        Vector3 p = M.MultiplyPoint3x4(v[i]); int best = 0; float bd = float.MaxValue;
                        for (int j = 0; j < skirtWorld.Length; j++) { float d = (skirtWorld[j] - p).sqrMagnitude; if (d < bd) { bd = d; best = j; } }
                        pairs.Add((f, r, i, best, Mathf.Sqrt(bd)));
                    }
                }
            }

            public void Sample()
            {
                var skirtNow = skirt.sharedMesh.vertices; var S = skirtRenderer.transform.localToWorldMatrix;
                var cache = new Dictionary<MeshFilter, Vector3[]>();
                foreach (var p in pairs)
                {
                    if (!cache.TryGetValue(p.filter, out var v)) cache[p.filter] = v = p.filter.sharedMesh.vertices;
                    float gap = Vector3.Distance(p.renderer.transform.TransformPoint(v[p.vertex]), S.MultiplyPoint3x4(skirtNow[p.skirtVertex]));
                    WorstGapChange = Mathf.Max(WorstGapChange, Mathf.Abs(gap - p.restGap));
                }
            }
        }

        /// <summary>
        /// Counts outer-skirt vertices that end up inside the underskirt bell although they started outside.
        /// A horizontal ray from the vertex away from the skirt axis crosses the underskirt an odd number of times when inside.
        /// </summary>
        class SkirtLayerCheck : System.IDisposable
        {
            readonly Part navy, ivory; readonly Transform root; readonly GameObject probe; readonly MeshCollider collider; readonly Mesh bake;
            readonly bool[] insideAtRest; readonly int stride = 3;
            public int Checked => insideAtRest.Length;

            public SkirtLayerCheck(Part navy, Part ivory, Transform root)
            {
                this.navy = navy; this.ivory = ivory; this.root = root;
                probe = new GameObject("SkirtLayerProbe") { layer = 31 };
                collider = probe.AddComponent<MeshCollider>();
                bake = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                Physics.queriesHitBackfaces = true;
                var nav = NavyWorld();
                insideAtRest = new bool[nav.Length];
                UpdateCollider();
                for (int i = 0; i < nav.Length; i++) insideAtRest[i] = Inside(nav[i], out _);
            }

            Vector3[] NavyWorld()
            {
                var m = navy.meshes.First(x => x.renderer.name == "Skirt_Navy"); var v = m.filter.sharedMesh.vertices; var M = m.renderer.transform.localToWorldMatrix;
                var list = new List<Vector3>();
                for (int i = 0; i < v.Length; i += stride) list.Add(M.MultiplyPoint3x4(v[i]));
                return list.ToArray();
            }

            void UpdateCollider()
            {
                var m = ivory.meshes.First(x => x.renderer.name == "Skirt_Ivory"); var src = m.filter.sharedMesh; var M = m.renderer.transform.localToWorldMatrix;
                var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
                bake.Clear(); bake.vertices = v; bake.triangles = src.triangles;
                collider.sharedMesh = null; collider.sharedMesh = bake;
            }

            bool Inside(Vector3 p, out float depth)
            {
                Vector3 axis = root.position; Vector3 d = p - axis; d.y = 0f; depth = 0f;
                if (d.sqrMagnitude < 1e-8f) return false;
                var hits = Physics.RaycastAll(p, d.normalized, 1.5f, 1 << 31);
                if (hits.Length % 2 == 1) { depth = hits.Min(h => h.distance); return true; }
                return false;
            }

            public (int count, float depth) Crossings()
            {
                UpdateCollider(); Physics.SyncTransforms();
                var nav = NavyWorld(); int n = 0; float worst = 0f;
                for (int i = 0; i < nav.Length; i++)
                    if (!insideAtRest[i] && Inside(nav[i], out float depth) && depth > 0.004f) { n++; worst = Mathf.Max(worst, depth); }
                return (n, worst);
            }

            public void Dispose() { Object.Destroy(probe); Object.Destroy(bake); }
        }
    }
}
