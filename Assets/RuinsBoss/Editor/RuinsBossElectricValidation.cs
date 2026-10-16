#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    /// <summary>Focused runtime rendering check. Never enters a mission or reads/writes PlayerPrefs.</summary>
    [InitializeOnLoad]
    public static class RuinsBossElectricValidation
    {
        const string Active = "RuinsBoss.ElectricValidation";
        const string Folder = "Library/RuinsBossValidation";
        const string Output = Folder + "/electric-visual-validation.json";
        const string SourceModel = "Assets/RuinsBoss/Art/Meshy/storm_sovereign/storm_sovereign_rig.glb";
        [Serializable] sealed class Report
        {
            public string status = "running", utc, closeup;
            public float frameInterval = .075f, pausedMeanPixelDifference;
            public int lightningBolts, boneAnchors, changingFrames, peakVisibleBolts;
            public List<string> checks = new List<string>(), errors = new List<string>(), frames = new List<string>();
        }
        [Serializable] sealed class SceneRestore { public List<string> paths = new List<string>(); public string active; }
        sealed class Wait { public Func<bool> condition; public double timeout; public string label; }
        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static Studio studio;
        static double started, deadline;
        static int lastFrame = -1;
        static float savedScale, savedCaptureDelta;

        static RuinsBossElectricValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (SessionState.GetBool(Active, false) && report != null &&
                    (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    report.errors.Add(message + "\n" + stack);
            };
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("AC Roguelike/Ruins/Validate Electrical Boss Visuals")]
        public static void Run() => Begin(false);
        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            if (SessionState.GetBool(Active, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start electrical visual validation in Edit Mode with no validation already running.");
            if (batch && !Application.isBatchMode) throw new InvalidOperationException("RunBatch is only for a separate batch editor.");
            var restore = new SceneRestore { active = SceneManager.GetActiveScene().path };
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount > 0))
                    throw new InvalidOperationException("Save the current scene before starting the visual validation; it opens an empty temporary studio.");
                if (!string.IsNullOrEmpty(scene.path)) restore.paths.Add(scene.path);
            }
            report = new Report { utc = DateTime.UtcNow.ToString("O") };
            SessionState.SetString(Active + ".scenes", JsonUtility.ToJson(restore));
            SessionState.SetBool(Active + ".batch", batch);
            SessionState.SetBool(Active + ".finished", false);
            try
            {
                ValidatePrefab();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Save(); SessionState.SetBool(Active, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString()); report.status = "failed"; Save();
                SessionState.SetBool(Active, false); RestoreScenes();
                if (batch) EditorApplication.Exit(1);
                else Debug.LogException(exception);
            }
        }

        static void ValidatePrefab()
        {
            var prefab = Resources.Load<GameObject>("RuinsBoss/StormSovereign");
            Check(prefab, "The authored electrical boss prefab exists.");
            var rig = prefab.GetComponentInChildren<RuinsBossRig>(true);
            Check(rig && rig.model && rig.idleClip && rig.castClip && rig.deathClip, "The prefab retains its Meshy model and real idle, cast and death clips.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModel);
            Check(source, "The source Meshy atlas is available for identity verification.");
            var sourceMaterial = source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m);
            Texture atlas = sourceMaterial.HasProperty("baseColorTexture") ? sourceMaterial.GetTexture("baseColorTexture") : sourceMaterial.GetTexture("_BaseMap");
            Check(atlas && atlas.width >= 512 && atlas.height >= 512, "The source face and hair texture is a real high-resolution atlas.");
            var renderers = rig.model.GetComponentsInChildren<Renderer>(true);
            Check(renderers.Length > 0 && renderers.All(r => r.shadowCastingMode == ShadowCastingMode.Off && !r.receiveShadows),
                "The translucent body neither casts opaque shadows nor receives wet surface shading.");
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var colorMaterials = materials.Where(m => m && m.HasProperty("_ColorMask") && m.GetFloat("_ColorMask") > 0).ToArray();
            var depthMaterials = materials.Where(m => m && m.HasProperty("_ColorMask") && m.GetFloat("_ColorMask") == 0).ToArray();
            Check(colorMaterials.Length > 0 && depthMaterials.Length > 0, "A transparent color body is paired with a nearest-surface depth renderer for readable facial features.");
            foreach (var material in depthMaterials)
                Check(material.GetFloat("_ZWrite") == 1 && material.renderQueue == 2999 && material.GetTexture("_BaseMap") == atlas,
                    "The depth renderer writes no color, preserves the same atlas, and draws before the transparent body.");
            foreach (var material in colorMaterials)
            {
                Check(material && material.shader && material.shader.name == "PirateAlliance/StormSovereignElectric" && material.shader.isSupported,
                    "Body rendering uses the supported electrical shader.");
                Check(material.GetTexture("_BaseMap") == atlas, "The original Meshy face and hair atlas survives the material conversion.");
                Check(material.renderQueue >= 3000 && material.renderQueue < 4000 && material.GetTag("RenderType", false) == "Transparent" && material.GetFloat("_ZWrite") == 0 &&
                    material.GetFloat("_Opacity") >= .35f && material.GetFloat("_Opacity") <= .45f,
                    "Body opacity is 35–45% with the transparent render queue and tag.");
                Check(material.GetFloat("_CrackleStrength") > .5f && material.GetColor("_EmissionColor").maxColorComponent > 1,
                    "White-cyan electrical strokes retain HDR intensity.");
                RequireNoShaderErrors(material.shader);
            }
        }

        static void OnPlayMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                savedScale = Time.timeScale; savedCaptureDelta = Time.captureDeltaTime;
                Time.timeScale = 1; Time.captureDeltaTime = 1f / 60;
                started = EditorApplication.timeSinceStartup; lastFrame = -1; routine = CheckRuntime();
            }
            else if (mode == PlayModeStateChange.EnteredEditMode)
            {
                if (report == null) report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                if (!SessionState.GetBool(Active + ".finished", false)) report.errors.Add("Play Mode ended before the electrical visual checks completed.");
                report.status = report.errors.Count == 0 ? "passed" : "failed"; Save();
                bool batch = SessionState.GetBool(Active + ".batch", false);
                SessionState.SetBool(Active, false); RestoreScenes();
                if (batch) EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
            }
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 120) throw new TimeoutException("Electrical visual validation exceeded 120 wall seconds.");
                if (waiting != null)
                {
                    if (!waiting.condition()) { if (now > deadline) throw new TimeoutException(waiting.label); return; }
                    waiting = null;
                }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (!routine.MoveNext()) { Finish(null); return; }
                if (routine.Current is Wait wait) { waiting = wait; deadline = now + wait.timeout; }
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        static IEnumerator CheckRuntime()
        {
            studio = new Studio();
            var rig = studio.Rig;
            yield return GameSeconds(.2f);
            Check(rig.UsesAnimationClips && rig.LightningBoltCount >= 10 && rig.LightningAnchorCount >= 12,
                "Live prefab animates through its imported skeleton and binds at least ten bolts to twelve real bones.");
            report.lightningBolts = rig.LightningBoltCount; report.boneAnchors = rig.LightningAnchorCount;
            var cores = rig.GetComponentsInChildren<LineRenderer>(true).Where(l => l.name.StartsWith("Body lightning ", StringComparison.Ordinal) && l.name.EndsWith("white core", StringComparison.Ordinal)).ToArray();
            var allLightning = rig.GetComponentsInChildren<LineRenderer>(true).Where(l => l.name.StartsWith("Body lightning ", StringComparison.Ordinal)).ToArray();
            Check(cores.Length == rig.LightningBoltCount && allLightning.Length == cores.Length * 3 && allLightning.All(l => l.sharedMaterial && l.positionCount >= 4),
                "Each body bolt has a white core, blue edge and angular fork with a real render material.");
            Check(rig.GetComponentsInChildren<LineRenderer>(true).Where(l => l.name == "Electric halo" || l.name.StartsWith("Coil arc ", StringComparison.Ordinal)).All(l => !l.enabled),
                "The old smooth halo and coil loops stay hidden on the sovereign.");

            studio.Frame(true);
            report.closeup = Folder + "/electric-boss-runtime-closeup.png";
            var closeup = Capture(studio.Camera, report.closeup, 1000, 1200);
            Check(MagentaFraction(closeup) < .002f, "Rendered close-up contains no missing-shader magenta surfaces.");
            foreach (var shader in rig.model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Select(m => m.shader).Distinct()) RequireNoShaderErrors(shader);
            studio.Frame(false);

            var bones = rig.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b).Distinct().ToArray();
            Vector3[] firstBones = bones.Select(b => b.position).ToArray();
            var previous = Snapshot.Take(allLightning);
            Color32[] previousPixels = null;
            int attachedSamples = 0, pixelChanges = 0;
            float castingStart = Time.time;
            for (int frame = 0; frame < 20; frame++)
            {
                rig.SetState(RuinsBossState.Windup, Mathf.Min(1.3f, Time.time - castingStart));
                yield return GameSeconds(report.frameInterval);
                report.peakVisibleBolts = Mathf.Max(report.peakVisibleBolts, rig.VisibleLightningBolts);
                var current = Snapshot.Take(allLightning);
                if (!previous.Matches(current)) report.changingFrames++;
                previous = current;
                foreach (var line in cores.Where(l => l.enabled))
                {
                    Vector3 a = WorldPoint(line, 0), b = WorldPoint(line, line.positionCount - 1);
                    Require(bones.Min(t => Vector3.Distance(a, t.position)) < .70f && bones.Min(t => Vector3.Distance(b, t.position)) < .70f,
                        "A visible electrical bolt detached from the moving skeleton: " + line.name);
                    float bend = 0;
                    for (int i = 1; i < line.positionCount - 1; i++)
                        bend = Mathf.Max(bend, Vector3.Cross(WorldPoint(line, i) - a, (b - a).normalized).magnitude);
                    Require(bend > .002f, "The electrical bolt became a straight fluid-like ribbon: " + line.name);
                    attachedSamples++;
                }
                string path = Folder + "/electric-frame-" + frame.ToString("D2") + ".png";
                var pixels = Capture(studio.Camera, path, 900, 1100); report.frames.Add(path);
                if (previousPixels != null && MeanDifference(previousPixels, pixels) > .015f) pixelChanges++;
                previousPixels = pixels;
            }
            Check(attachedSamples > 50 && report.peakVisibleBolts >= 5,
                "Visible angular bolts remain attached to the animated skeleton across twenty rendered frames.");
            Check(report.changingFrames >= 15 && pixelChanges >= 15,
                "Electrical geometry, visibility and rendered pixels change repeatedly during the casting sequence.");
            Check(bones.Select((b, i) => Vector3.Distance(firstBones[i], b.position)).Max() > .03f,
                "The imported casting skeleton moves under the attached electrical effects.");

            Time.timeScale = 0;
            int settleFrame = Time.frameCount + 2;
            yield return new Wait { condition = () => Time.frameCount >= settleFrame, timeout = 5, label = "Pause frame settling" };
            var paused = Snapshot.Take(allLightning);
            Vector3[] pausedBones = bones.Select(b => b.position).ToArray();
            int pausedTick = rig.LightningTick;
            var pausedPixels = Capture(studio.Camera, Folder + "/electric-paused-a.png", 900, 1100);
            double resumeAfter = EditorApplication.timeSinceStartup + .3;
            yield return new Wait { condition = () => EditorApplication.timeSinceStartup >= resumeAfter, timeout = 3, label = "Pause rendering comparison" };
            var stillPixels = Capture(studio.Camera, Folder + "/electric-paused-b.png", 900, 1100);
            report.pausedMeanPixelDifference = MeanDifference(pausedPixels, stillPixels);
            Check(rig.LightningTick == pausedTick && paused.Matches(Snapshot.Take(allLightning)) &&
                bones.Select((b, i) => Vector3.Distance(pausedBones[i], b.position)).Max() < .00001f,
                "Pause freezes electrical ticks, all bolt geometry and the underlying skeleton.");
            Check(report.pausedMeanPixelDifference < .10f,
                "Pause also freezes the rendered body crackle and shader flicker. Mean byte difference: " + report.pausedMeanPixelDifference.ToString("F4"));
            Time.timeScale = 1;
            yield return GameSeconds(.12f);
            Check(rig.LightningTick != pausedTick && !paused.Matches(Snapshot.Take(allLightning)), "Electrical flicker resumes after unpausing.");

            rig.PlayDeath();
            Check(rig.VisibleLightningBolts == 0 && !rig.ActivationVisible && allLightning.All(l => !l.enabled),
                "Death immediately hides all attached bolts, blue edges, forks and activation electricity.");
            yield return GameSeconds(.15f);
            Check(rig.VisibleLightningBolts == 0 && allLightning.All(l => !l.enabled), "No body lightning reappears on subsequent death frames.");
            Capture(studio.Camera, Folder + "/electric-death-effects-hidden.png", 900, 1100);
        }

        static Wait GameSeconds(float seconds)
        {
            float end = Time.time + seconds;
            return new Wait { condition = () => Time.time >= end, timeout = seconds * 12 + 8, label = "Visual animation wait" };
        }
        static void Check(bool condition, string message) { Require(condition, message); report.checks.Add(message); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void RequireNoShaderErrors(Shader shader)
        {
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
            Require(errors.Length == 0, shader.name + " compilation failed: " + string.Join("\n", errors.Select(m => m.message)));
        }
        static Vector3 WorldPoint(LineRenderer line, int index) => line.useWorldSpace ? line.GetPosition(index) : line.transform.TransformPoint(line.GetPosition(index));
        static float MagentaFraction(Color32[] pixels) => pixels.Count(p => p.r > 160 && p.b > 160 && p.g < 50) / (float)pixels.Length;
        static float MeanDifference(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) throw new InvalidOperationException("Capture size changed.");
            long sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3f);
        }

        sealed class Snapshot
        {
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<float> values = new List<float>();
            public static Snapshot Take(LineRenderer[] lines)
            {
                var snapshot = new Snapshot();
                foreach (var line in lines)
                {
                    snapshot.values.Add(line.enabled ? 1 : 0); snapshot.values.Add(line.widthMultiplier);
                    for (int i = 0; i < line.positionCount; i++) snapshot.points.Add(WorldPoint(line, i));
                    foreach (var key in line.colorGradient.alphaKeys) snapshot.values.Add(key.alpha);
                }
                return snapshot;
            }
            public bool Matches(Snapshot other)
            {
                if (points.Count != other.points.Count || values.Count != other.values.Count) return false;
                for (int i = 0; i < points.Count; i++) if (Vector3.Distance(points[i], other.points[i]) > .000001f) return false;
                for (int i = 0; i < values.Count; i++) if (Mathf.Abs(values[i] - other.values[i]) > .000001f) return false;
                return true;
            }
        }

        sealed class Studio : IDisposable
        {
            readonly Scene previous, scene;
            readonly GameObject root;
            readonly List<Object> owned = new List<Object>();
            readonly Color ambient;
            readonly AmbientMode ambientMode;
            readonly bool fog;
            public Camera Camera { get; }
            public RuinsBossRig Rig { get; }
            public Studio()
            {
                previous = SceneManager.GetActiveScene();
                scene = SceneManager.CreateScene("Temporary electrical visual studio"); SceneManager.SetActiveScene(scene);
                ambient = RenderSettings.ambientLight; ambientMode = RenderSettings.ambientMode; fog = RenderSettings.fog;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f, .45f, .52f); RenderSettings.fog = false;
                root = new GameObject("Owned electrical visual studio");
                var actor = Object.Instantiate(Resources.Load<GameObject>("RuinsBoss/StormSovereign"), root.transform);
                actor.GetComponent<RuinsStormBoss>().enabled = false;
                foreach (var collider in actor.GetComponentsInChildren<Collider>()) collider.enabled = false;
                Rig = actor.GetComponentInChildren<RuinsBossRig>(); Rig.Initialize(); Rig.SetDormant(false);
                var cameraObject = new GameObject("Electrical inspection camera"); cameraObject.transform.SetParent(root.transform, false);
                Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false;
                Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.035f, .045f, .065f);
                Camera.cullingMask = 1 << 30; Camera.nearClipPlane = .1f; Camera.farClipPlane = 80; Camera.allowHDR = true;
                var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>(); cameraData.renderPostProcessing = true; cameraData.volumeLayerMask = 1 << 30;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>(); owned.Add(profile);
                var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.1f); bloom.intensity.Override(.35f); bloom.scatter.Override(.55f);
                var volumeObject = new GameObject("Electrical preview bloom"); volumeObject.transform.SetParent(root.transform, false);
                var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = profile;
                Material dark = Surface(new Color(.07f, .09f, .115f)), light = Surface(new Color(.105f, .135f, .165f));
                for (int y = 0; y < 12; y++) for (int x = 0; x < 14; x++)
                    Tile(new Vector3((x - 6.5f) * .55f, y * .55f, -1.6f), new Vector3(.55f, .55f, .05f), (x + y) % 2 == 0 ? dark : light);
                Tile(new Vector3(0, -.08f, 0), new Vector3(12, .12f, 10), dark);
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 30;
                Frame(false);
            }
            Material Surface(Color color)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", color); owned.Add(material); return material;
            }
            void Tile(Vector3 position, Vector3 scale, Material material)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(root.transform, false);
                go.transform.localPosition = position; go.transform.localScale = scale; Object.DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = material;
            }
            public void Frame(bool closeup)
            {
                Vector3 focus = new Vector3(0, closeup ? 3.3f : 2.45f, 0);
                Camera.transform.position = focus + new Vector3(2.1f, .8f, 10);
                Camera.transform.LookAt(focus); Camera.orthographic = true; Camera.orthographicSize = closeup ? 1.8f : 2.9f;
            }
            public void Dispose()
            {
                if (root) Object.DestroyImmediate(root);
                foreach (var item in owned) if (item) Object.DestroyImmediate(item);
                RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode; RenderSettings.fog = fog;
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }

        static Color32[] Capture(Camera camera, string path, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; float aspect = camera.aspect; Texture2D pixels = null;
            try
            {
                texture.Create(); camera.aspect = width / (float)height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render requests are unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Folder); File.WriteAllBytes(path, pixels.EncodeToPNG()); return pixels.GetPixels32();
            }
            finally
            {
                camera.aspect = aspect; RenderTexture.active = previous;
                if (pixels) Object.DestroyImmediate(pixels); texture.Release(); Object.DestroyImmediate(texture);
            }
        }
        static void Save()
        {
            Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true);
            SessionState.SetString(Active + ".report", json); File.WriteAllText(Output, json);
        }
        static void RestoreScenes()
        {
            var json = SessionState.GetString(Active + ".scenes", ""); if (string.IsNullOrEmpty(json)) return;
            var restore = JsonUtility.FromJson<SceneRestore>(json);
            if (restore.paths.Count == 0) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
            {
                for (int i = 0; i < restore.paths.Count; i++) EditorSceneManager.OpenScene(restore.paths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
                var active = SceneManager.GetSceneByPath(restore.active); if (active.IsValid()) SceneManager.SetActiveScene(active);
            }
        }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            try { studio?.Dispose(); }
            catch (Exception cleanup) { report.errors.Add(cleanup.ToString()); }
            finally { studio = null; Time.timeScale = savedScale; Time.captureDeltaTime = savedCaptureDelta; }
            if (error != null) report.errors.Add(error);
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("RUINS_BOSS_ELECTRIC_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
