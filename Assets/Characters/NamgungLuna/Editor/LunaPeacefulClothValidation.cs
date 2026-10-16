#if UNITY_EDITOR && MAGICACLOTH2
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicaCloth2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NamgungLuna.Editor
{
    /// <summary>Runs the shipped peaceful cloth in a disposable scene and compares it to identical, un-simulated skinning.</summary>
    [InitializeOnLoad]
    public static class LunaPeacefulClothValidation
    {
        const string Key = "Luna.PeacefulCloth.Validation";
        public const string Output = "Documentation/NamgungLuna/PeacefulCloth/Validation";

        [Serializable] public sealed class Options
        {
            public int framesPerSecond = 60, expectedClothCount = 5, sampleEveryFrames = 1;
            public float warmupSeconds = 2f, buildTimeoutSeconds = 12f, wallTimeoutSeconds = 600f;
            public float walkSpeed = .65f, dashSpeed = 13f, dashSeconds = .19f;
            public float maxDisplacement = .18f, maxFrameOffsetChange = .10f;
            public float maxPenetrationWorsening = .01f, maxIdleDrift = .025f;
            public float minimumMotion = .0005f, maxRecoveryRmsSpeed = .06f;
            public float maxRecoveryDisplacement = .08f, minimumWorldY = -.005f;
            public bool requirePreBuild = true;
            public bool captureVideoFrames = false;
            public int videoEveryFrames = 2;
        }

        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class SavedState
        {
            public SavedScene[] scenes;
            public string[] selection;
            public float captureDeltaTime, timeScale, fixedDeltaTime;
            public bool paused;
            public Options options;
        }
        [Serializable] sealed class ClothInfo
        {
            public string name, buildResult;
            public string[] renderers, colliders;
            public bool preBuild, built, usingCustomMesh;
            public float maxDisplacement, maxDynamicOffsetChange;
        }
        [Serializable] sealed class Measurement
        {
            public string scenario, renderer;
            public int samples, vertexSamples, customMeshSamples;
            public bool allFinite = true;
            public float maxDisplacement, rmsDisplacement, maxOffsetChange, rmsOffsetSpeed;
            public float maxPenetration, ghostMaxPenetration, maxPenetrationWorsening;
            public float firstRmsDisplacement, finalRmsDisplacement, maxIdleDrift;
            public float lateRmsOffsetSpeed, lateMaxDisplacement;
            public int worstPenetrationVertex = -1, worstPenetrationScenarioFrame = -1;
            public string worstPenetrationCollider, worstPenetrationSourceAttribute;
            public Vector3 worstPenetrationActualBodyLocal, worstPenetrationGhostBodyLocal;
            public float worstPenetrationActualDepth, worstPenetrationGhostDepth, worstPenetrationTime;
            public int worstOffsetChangeVertex = -1, worstOffsetChangeScenarioFrame = -1;
            public string worstOffsetChangeSourceAttribute;
            public Vector3 worstOffsetChangeActualBodyLocal, worstOffsetChangeGhostBodyLocal;
            public float worstOffsetChangeTime;
            public float minActualWorldY = float.MaxValue, minGhostWorldY = float.MaxValue;
            public int minActualVertex = -1, minGhostVertex = -1;
            public string minActualSourceAttribute, minGhostSourceAttribute;
            public Vector3 minActualBodyLocal, minGhostBodyLocal;
            public float minFixedWorldY = float.MaxValue, minMoveWorldY = float.MaxValue;
            public int minFixedVertex = -1, minMoveVertex = -1;
            public Vector3 minFixedActualBodyLocal, minFixedGhostBodyLocal, minMoveActualBodyLocal, minMoveGhostBodyLocal;
            [NonSerialized] public double squaredDisplacement, squaredSpeed, lateSquaredSpeed;
            [NonSerialized] public int speedSamples, lateSpeedSamples;
            [NonSerialized] public Vector3[] firstOffsets;
        }
        [Serializable] sealed class Report
        {
            public string status = "running", utc, error;
            public string prefab = LunaStageSetup.PeacePrefab;
            public string method = "Paused Play mode, one Editor Step per captured frame; samples after Magica afterRenderingDelegate. BakeMesh(true) on the custom skin and a cloth-free ghost with identical bone transforms. Penetration is geometric surface depth, relative to that same animated ghost, without particle thickness.";
            public Options options;
            public List<ClothInfo> cloths = new List<ClothInfo>();
            public List<Measurement> measurements = new List<Measurement>();
            public List<string> checks = new List<string>(), failures = new List<string>(), captures = new List<string>();
            public int steppedFrames, videoFrames;
            public float simulatedSeconds, actualDashDistance;
        }

        sealed class SkinPair
        {
            public string path;
            public SkinnedMeshRenderer actual, ghost;
            public MagicaCloth[] owners;
            public ColliderComponent[] colliders;
            public VertexAttribute[] sourceAttributes;
            public Mesh actualBake = new Mesh(), ghostBake = new Mesh();
            public readonly List<Vector3> actualVertices = new List<Vector3>(), ghostVertices = new List<Vector3>();
            public Vector3[] previousOffsets;
            public float previousSampleTime;
        }
        struct PosePair { public Transform actual, ghost; }
        struct Shape
        {
            public string colliderPath;
            public Vector3 start, end;
            public float startRadius, endRadius;
            public float Penetration(Vector3 point)
            {
                Vector3 axis = end - start;
                float length = axis.magnitude;
                if (length < .000001f)
                    return Mathf.Max(0, Mathf.Max(startRadius, endRadius) - Vector3.Distance(point, start));
                Vector3 direction = axis / length, relative = point - start;
                float axial = Vector3.Dot(relative, direction);
                float slope = (endRadius - startRadius) / length;
                if (Mathf.Abs(slope) >= .99999f)
                    return Mathf.Max(0, startRadius - relative.magnitude, endRadius - Vector3.Distance(point, end));
                float perpendicular = (relative - direction * axial).magnitude;
                float distance = Mathf.Clamp(axial + slope * perpendicular / Mathf.Sqrt(1 - slope * slope), 0, length);
                float radius = Mathf.Lerp(startRadius, endRadius, distance / length);
                return Mathf.Max(0, radius - Vector3.Distance(point, start + direction * distance));
            }
        }

        static Report report;
        static SavedState saved;
        static IEnumerator routine;
        static GameObject body, ghost, stage;
        static Animator animator;
        static Camera camera;
        static MagicaCloth[] cloths;
        static SkinPair[] skins;
        static PosePair[] poses;
        static int pendingFrame = -1, renderedFrame = -1;
        static double deadline;
        static bool finishing;
        static float Clock => report.steppedFrames / (float)saved.options.framesPerSecond;
        static float Dt => 1f / saved.options.framesPerSecond;
        public static bool Running => SessionState.GetBool(Key, false);

        static LunaPeacefulClothValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.delayCall += () =>
            {
                if (Running && SessionState.GetBool(Key + ".entered", false) && routine == null && EditorApplication.isPlaying)
                    Finish("The validation was interrupted by an assembly reload.");
            };
        }

        [MenuItem("Tools/Namgung Luna/Stage Capture/Validate Peaceful Cloth")]
        public static void Start() => Start(new Options());

        public static void Start(Options options)
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before cloth validation.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab mode before cloth validation.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (scene.isLoaded && string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("Save all open scenes before cloth validation; no scene is saved automatically.");
            }
            if (options == null || options.framesPerSecond < 15 || options.framesPerSecond > 240 || options.sampleEveryFrames < 1 || options.videoEveryFrames < 1)
                throw new ArgumentException("Use 15–240 FPS and a positive sample interval.", nameof(options));
            saved = new SavedState
            {
                scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray(),
                selection = Selection.objects.Select(o => GlobalObjectId.GetGlobalObjectIdSlow(o).ToString()).ToArray(),
                captureDeltaTime = Time.captureDeltaTime, timeScale = Time.timeScale, fixedDeltaTime = Time.fixedDeltaTime,
                paused = EditorApplication.isPaused, options = options
            };
            SessionState.SetString(Key + ".saved", JsonUtility.ToJson(saved));
            SessionState.SetBool(Key + ".entered", false);
            SessionState.SetBool(Key, true);
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch { Restore(); throw; }
        }

        static void PlayStateChanged(PlayModeStateChange state)
        {
            if (!Running) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                saved = JsonUtility.FromJson<SavedState>(SessionState.GetString(Key + ".saved", ""));
                report = new Report { utc = DateTime.UtcNow.ToString("O"), options = saved.options };
                WriteReport();
                finishing = false;
                SessionState.SetBool(Key + ".entered", true);
                Time.captureDeltaTime = Dt;
                Time.fixedDeltaTime = Dt;
                Time.timeScale = 1;
                EditorApplication.isPaused = true;
                pendingFrame = renderedFrame = -1;
                deadline = EditorApplication.timeSinceStartup + saved.options.wallTimeoutSeconds;
                routine = Run();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !finishing)
            {
                if (report != null) { report.status = "failed"; report.error = "Play mode was stopped before validation completed."; WriteReport(); }
                Cleanup();
            }
            else if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }

        static void MarkRenderedFrame() => renderedFrame = Time.frameCount;

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying || finishing) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Cloth validation exceeded its wall-clock timeout.");
                if (pendingFrame >= 0)
                {
                    if (Time.frameCount <= pendingFrame || renderedFrame <= pendingFrame) return;
                    if (Time.frameCount != pendingFrame + 1)
                        throw new InvalidOperationException("The editor advanced multiple frames; reproducible stepping was interrupted.");
                    report.steppedFrames++;
                    report.simulatedSeconds = Clock;
                    pendingFrame = -1;
                }
                if (!routine.MoveNext()) { Finish(null); return; }
                EditorApplication.isPaused = true;
                pendingFrame = Time.frameCount;
                EditorApplication.Step();
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        static IEnumerator Run()
        {
            Setup();
            MagicaManager.afterRenderingDelegate += MarkRenderedFrame;
            int buildFrames = Mathf.CeilToInt(saved.options.buildTimeoutSeconds / Dt);
            while (buildFrames-- > 0 && !cloths.All(c => c.IsValid() && c.Process.IsRunning()))
            {
                foreach (var cloth in cloths)
                    if (cloth.Process.Result.IsError() || cloth.Process.InitDataResult.IsError())
                        throw new InvalidOperationException(cloth.name + ": " + cloth.Process.Result.GetResultString() + "; init=" + cloth.Process.InitDataResult.GetResultString());
                yield return null;
            }
            Require(cloths.All(c => c.IsValid() && c.Process.IsRunning()), "All cloth groups finished building and are running.");
            for (int i = 0; i < cloths.Length; i++)
            {
                report.cloths[i].built = true;
                report.cloths[i].buildResult = cloths[i].Process.Result.GetResultString();
            }
            for (int i = 0; i < Mathf.CeilToInt(saved.options.warmupSeconds / Dt); i++) yield return null;
            AssertCustomMeshes();
            report.checks.Add("Every source renderer displays Magica's custom mesh, distinct from its original mesh.");

            var scenarios = new[] { "idle", "walk", "dash", "turn_180", "recovery", "teleport_20m", "reactivation" };
            foreach (string scenario in scenarios)
            {
                var run = Scenario(scenario);
                while (run.MoveNext()) yield return run.Current;
                WriteReport();
            }
            foreach (var cloth in report.cloths)
                Record(cloth.maxDynamicOffsetChange >= saved.options.minimumMotion,
                    cloth.name + ": dynamic cloth movement exceeds " + saved.options.minimumMotion.ToString("F4") + " m.");
        }

        static void Setup()
        {
            if (saved.options.captureVideoFrames)
            {
                string framesFolder = Output + "/Frames";
                Directory.CreateDirectory(framesFolder);
                foreach (string previousFrame in Directory.GetFiles(framesFolder, "frame_*.png", SearchOption.TopDirectoryOnly))
                    File.Delete(previousFrame);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LunaStageSetup.PeacePrefab);
            if (!prefab) throw new InvalidOperationException("The peaceful stage prefab is missing.");
            stage = new GameObject("LunaPeacefulClothValidationStage");
            stage.SetActive(false);
            body = Object.Instantiate(prefab, stage.transform);
            body.name = "Simulated";
            ghost = Object.Instantiate(prefab, stage.transform);
            ghost.name = "UnsimulatedGhost";
            foreach (var cloth in ghost.GetComponentsInChildren<MagicaCloth>(true)) Object.DestroyImmediate(cloth);
            foreach (var collider in ghost.GetComponentsInChildren<ColliderComponent>(true)) Object.DestroyImmediate(collider);
            foreach (var a in ghost.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            animator = body.GetComponentInChildren<Animator>(true);
            Require(animator && animator.runtimeAnimatorController, "The shipped peaceful Animator controller is present.");
            Require(animator.parameters.Any(p => p.name == "Speed" && p.type == AnimatorControllerParameterType.Float)
                && animator.parameters.Any(p => p.name == "Dashing" && p.type == AnimatorControllerParameterType.Bool),
                "The controller contains Speed and Dashing parameters.");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            cloths = body.GetComponentsInChildren<MagicaCloth>(true).OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
            Require(cloths.Length > 0 && (saved.options.expectedClothCount <= 0 || cloths.Length == saved.options.expectedClothCount),
                "Expected cloth count: " + cloths.Length + ".");
            foreach (var cloth in cloths)
            {
                Require(cloth.SerializeData.clothType == ClothProcess.ClothType.MeshCloth, cloth.name + " uses MeshCloth.");
                Require(cloth.SerializeData.sourceRenderers.Count > 0 && cloth.SerializeData.sourceRenderers.All(r => r is SkinnedMeshRenderer),
                    cloth.name + " has skinned source renderers.");
                bool preBuild = cloth.GetSerializeData2().preBuildData.UsePreBuild();
                if (saved.options.requirePreBuild) Require(preBuild, cloth.name + " has enabled prebuild data.");
                report.cloths.Add(new ClothInfo { name = cloth.name, preBuild = preBuild,
                    renderers = cloth.SerializeData.sourceRenderers.Select(r => PathOf(r.transform, body.transform)).ToArray(),
                    colliders = cloth.SerializeData.colliderCollisionConstraint.colliderList.Where(c => c).Select(c => PathOf(c.transform, body.transform)).ToArray() });
            }
            var ghostTransforms = ghost.GetComponentsInChildren<Transform>(true).ToDictionary(t => PathOf(t, ghost.transform));
            poses = body.GetComponentsInChildren<Transform>(true).Select(t => new PosePair { actual = t, ghost = ghostTransforms[PathOf(t, body.transform)] }).ToArray();
            var ghostSkins = ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => PathOf(r.transform, ghost.transform));
            skins = cloths.SelectMany(c => c.SerializeData.sourceRenderers).Cast<SkinnedMeshRenderer>().Distinct().Select(r =>
            {
                string path = PathOf(r.transform, body.transform);
                var owners = cloths.Where(c => c.SerializeData.sourceRenderers.Contains(r)).ToArray();
                var pair = new SkinPair { path = path, actual = r, ghost = ghostSkins[path], owners = owners,
                    colliders = owners.SelectMany(c => c.SerializeData.colliderCollisionConstraint.colliderList).Where(c => c).Distinct().ToArray() };
                // LunaPeacefulClothSetup writes one selection entry per original source vertex, in the same order.
                // These are authored source attributes, not the reduced proxy's effective attributes.
                var selection = owners.Length == 1 && owners[0].SerializeData.sourceRenderers.Count == 1
                    ? owners[0].GetSerializeData2().selectionData : null;
                if (selection != null && selection.IsValid() && selection.attributes.Length == r.sharedMesh.vertexCount)
                    pair.sourceAttributes = selection.attributes.ToArray();
                r.updateWhenOffscreen = pair.ghost.updateWhenOffscreen = true;
                foreach (var c in pair.colliders)
                {
                    if (!(c is MagicaSphereCollider) && !(c is MagicaCapsuleCollider))
                        throw new InvalidOperationException("Collision measurement currently requires sphere/capsule colliders: " + c.name);
                    if (c.symmetryMode != ColliderSymmetryMode.None)
                        throw new InvalidOperationException("Use explicit collider children for measurement; mirrored collider instances are not enumerated: " + c.name);
                }
                return pair;
            }).ToArray();
            camera = new GameObject("ClothValidationCamera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 1.15f;
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 40;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .14f);
            var light = new GameObject("ClothValidationLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(40, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .65f, .7f);
            FollowCamera();
            stage.SetActive(true);
            animator.Play("Base Layer.Locomotion", 0, 0);
            animator.SetFloat("Speed", 0);
            animator.SetBool("Dashing", false);
        }

        static IEnumerator Scenario(string scenario)
        {
            float seconds = scenario == "walk" || scenario == "recovery" ? 3f : 2f;
            int frames = Mathf.CeilToInt(seconds / Dt);
            var measurements = skins.Select(s => new Measurement { scenario = scenario, renderer = s.path }).ToArray();
            report.measurements.AddRange(measurements);
            foreach (var skin in skins) skin.previousOffsets = null;
            animator.SetFloat("Speed", scenario == "walk" || scenario == "turn_180" ? 1f : 0f);
            animator.SetBool("Dashing", scenario == "dash");
            if (scenario == "dash")
            {
                Require(animator.HasState(0, Animator.StringToHash("Base Layer.Dash")), "The actual controller has a Dash state.");
                animator.CrossFadeInFixedTime("Base Layer.Dash", .06f, 0, 0f);
            }
            Vector3 dashStart = body.transform.position;
            Quaternion startRotation = body.transform.rotation;
            if (scenario == "teleport_20m") body.transform.position += new Vector3(20, 0, 0);
            if (scenario == "reactivation")
            {
                body.SetActive(false);
                yield return null;
                yield return null;
                body.SetActive(true);
                animator.SetFloat("Speed", 0);
                animator.SetBool("Dashing", false);
            }
            for (int frame = 0; frame < frames; frame++)
            {
                float elapsed = frame * Dt;
                if (scenario == "walk") body.transform.position += body.transform.forward * (saved.options.walkSpeed * Dt);
                else if (scenario == "dash")
                {
                    float dashStep = Mathf.Clamp(saved.options.dashSeconds - elapsed, 0, Dt);
                    body.transform.position += body.transform.forward * (saved.options.dashSpeed * dashStep);
                    animator.SetBool("Dashing", elapsed < saved.options.dashSeconds);
                }
                else if (scenario == "turn_180")
                {
                    body.transform.rotation = startRotation * Quaternion.Euler(0, 180f * Mathf.Clamp01((elapsed - .4f) / .2f), 0);
                    body.transform.position += body.transform.forward * (saved.options.walkSpeed * Dt);
                }
                FollowCamera();
                yield return null;
                if (frame % saved.options.sampleEveryFrames == 0 || frame == frames - 1)
                    Measure(measurements, elapsed >= seconds - .5f, frame);
                if (saved.options.captureVideoFrames && frame % saved.options.videoEveryFrames == 0)
                {
                    Capture("Frames/frame_" + report.videoFrames.ToString("D5"), 640, 800, false);
                    report.videoFrames++;
                }
                if (scenario == "dash" && frame == Mathf.CeilToInt(saved.options.dashSeconds / Dt) - 1)
                    Capture("dash_peak");
            }
            if (scenario == "dash")
            {
                report.actualDashDistance = Vector3.Distance(dashStart, body.transform.position);
                Record(Mathf.Abs(report.actualDashDistance - saved.options.dashSpeed * saved.options.dashSeconds) < .001f,
                    "Dash translates exactly speed × duration (" + report.actualDashDistance.ToString("F3") + " m).");
            }
            AssertCustomMeshes();
            foreach (var measurement in measurements) Validate(measurement);
            Capture(scenario + "_end");
            CaptureView(scenario + "_rear", new Vector3(-2.3f, .4f, -4.2f));
            CaptureView(scenario + "_side", new Vector3(4.2f, .4f, 0));
        }

        static void Measure(Measurement[] measurements, bool late, int scenarioFrame)
        {
            foreach (var pair in poses)
            {
                pair.ghost.localPosition = pair.actual.localPosition;
                pair.ghost.localRotation = pair.actual.localRotation;
                pair.ghost.localScale = pair.actual.localScale;
            }
            for (int index = 0; index < skins.Length; index++)
            {
                var pair = skins[index];
                var m = measurements[index];
                Mesh custom = pair.owners[0].GetCustomMesh(pair.actual);
                bool assigned = custom && pair.actual.sharedMesh == custom;
                if (assigned) m.customMeshSamples++;
                pair.actual.BakeMesh(pair.actualBake, true);
                pair.ghost.BakeMesh(pair.ghostBake, true);
                pair.actualBake.GetVertices(pair.actualVertices);
                pair.ghostBake.GetVertices(pair.ghostVertices);
                if (pair.actualVertices.Count == 0 || pair.actualVertices.Count != pair.ghostVertices.Count)
                    throw new InvalidOperationException(pair.path + ": custom and original vertex counts differ.");
                var shapes = pair.colliders.Where(c => c.isActiveAndEnabled).Select(ColliderShape).ToArray();
                int count = pair.actualVertices.Count;
                if (m.firstOffsets == null) m.firstOffsets = new Vector3[count];
                float sampleDt = Clock - pair.previousSampleTime;
                double squaredFrame = 0;
                var offsets = new Vector3[count];
                for (int vertex = 0; vertex < count; vertex++)
                {
                    Vector3 actual = pair.actual.transform.TransformPoint(pair.actualVertices[vertex]);
                    Vector3 baseline = pair.ghost.transform.TransformPoint(pair.ghostVertices[vertex]);
                    if (!Finite(actual) || !Finite(baseline))
                    {
                        m.allFinite = false;
                        throw new InvalidOperationException(m.scenario + "/" + pair.path + ": non-finite skinned vertex.");
                    }
                    if (actual.y < m.minActualWorldY)
                    {
                        m.minActualWorldY = actual.y;
                        m.minActualVertex = vertex;
                        m.minActualSourceAttribute = SourceAttribute(pair, vertex);
                        m.minActualBodyLocal = body.transform.InverseTransformPoint(actual);
                    }
                    if (baseline.y < m.minGhostWorldY)
                    {
                        m.minGhostWorldY = baseline.y;
                        m.minGhostVertex = vertex;
                        m.minGhostSourceAttribute = SourceAttribute(pair, vertex);
                        m.minGhostBodyLocal = body.transform.InverseTransformPoint(baseline);
                    }
                    if (pair.sourceAttributes != null)
                    {
                        var attribute = pair.sourceAttributes[vertex];
                        if (attribute.IsFixed() && actual.y < m.minFixedWorldY)
                        {
                            m.minFixedWorldY = actual.y;
                            m.minFixedVertex = vertex;
                            m.minFixedActualBodyLocal = body.transform.InverseTransformPoint(actual);
                            m.minFixedGhostBodyLocal = body.transform.InverseTransformPoint(baseline);
                        }
                        if (attribute.IsMove() && actual.y < m.minMoveWorldY)
                        {
                            m.minMoveWorldY = actual.y;
                            m.minMoveVertex = vertex;
                            m.minMoveActualBodyLocal = body.transform.InverseTransformPoint(actual);
                            m.minMoveGhostBodyLocal = body.transform.InverseTransformPoint(baseline);
                        }
                    }
                    // Removing root translation and yaw prevents ordinary locomotion from being counted as cloth motion.
                    Vector3 offset = Quaternion.Inverse(body.transform.rotation) * (actual - baseline);
                    offsets[vertex] = offset;
                    float displacement = offset.magnitude;
                    m.maxDisplacement = Mathf.Max(m.maxDisplacement, displacement);
                    squaredFrame += displacement * displacement;
                    if (m.samples == 0) m.firstOffsets[vertex] = offset;
                    else m.maxIdleDrift = Mathf.Max(m.maxIdleDrift, Vector3.Distance(offset, m.firstOffsets[vertex]));
                    if (pair.previousOffsets != null && sampleDt > 0)
                    {
                        float change = Vector3.Distance(offset, pair.previousOffsets[vertex]);
                        if (change > m.maxOffsetChange)
                        {
                            m.maxOffsetChange = change;
                            m.worstOffsetChangeVertex = vertex;
                            m.worstOffsetChangeScenarioFrame = scenarioFrame;
                            m.worstOffsetChangeSourceAttribute = SourceAttribute(pair, vertex);
                            m.worstOffsetChangeActualBodyLocal = body.transform.InverseTransformPoint(actual);
                            m.worstOffsetChangeGhostBodyLocal = body.transform.InverseTransformPoint(baseline);
                            m.worstOffsetChangeTime = Clock;
                        }
                        double squaredSpeed = change * change / (sampleDt * sampleDt);
                        m.squaredSpeed += squaredSpeed;
                        m.speedSamples++;
                        if (late) { m.lateSquaredSpeed += squaredSpeed; m.lateSpeedSamples++; }
                    }
                    if (late) m.lateMaxDisplacement = Mathf.Max(m.lateMaxDisplacement, displacement);
                    foreach (var shape in shapes)
                    {
                        float actualDepth = shape.Penetration(actual), ghostDepth = shape.Penetration(baseline);
                        m.maxPenetration = Mathf.Max(m.maxPenetration, actualDepth);
                        m.ghostMaxPenetration = Mathf.Max(m.ghostMaxPenetration, ghostDepth);
                        float worsening = actualDepth - ghostDepth;
                        if (worsening > m.maxPenetrationWorsening)
                        {
                            m.maxPenetrationWorsening = worsening;
                            m.worstPenetrationVertex = vertex;
                            m.worstPenetrationScenarioFrame = scenarioFrame;
                            m.worstPenetrationCollider = shape.colliderPath;
                            m.worstPenetrationSourceAttribute = SourceAttribute(pair, vertex);
                            m.worstPenetrationActualBodyLocal = body.transform.InverseTransformPoint(actual);
                            m.worstPenetrationGhostBodyLocal = body.transform.InverseTransformPoint(baseline);
                            m.worstPenetrationActualDepth = actualDepth;
                            m.worstPenetrationGhostDepth = ghostDepth;
                            m.worstPenetrationTime = Clock;
                        }
                    }
                }
                pair.previousOffsets = offsets;
                pair.previousSampleTime = Clock;
                m.samples++;
                m.vertexSamples += count;
                m.squaredDisplacement += squaredFrame;
                m.rmsDisplacement = Mathf.Sqrt((float)(m.squaredDisplacement / m.vertexSamples));
                m.finalRmsDisplacement = Mathf.Sqrt((float)(squaredFrame / count));
                if (m.samples == 1) m.firstRmsDisplacement = m.finalRmsDisplacement;
                m.rmsOffsetSpeed = m.speedSamples > 0 ? Mathf.Sqrt((float)(m.squaredSpeed / m.speedSamples)) : 0;
                m.lateRmsOffsetSpeed = m.lateSpeedSamples > 0 ? Mathf.Sqrt((float)(m.lateSquaredSpeed / m.lateSpeedSamples)) : 0;
                foreach (var owner in pair.owners)
                {
                    var info = report.cloths[Array.IndexOf(cloths, owner)];
                    info.usingCustomMesh |= assigned;
                    info.maxDisplacement = Mathf.Max(info.maxDisplacement, m.maxDisplacement);
                    if (m.scenario == "walk" || m.scenario == "dash" || m.scenario == "turn_180")
                        info.maxDynamicOffsetChange = Mathf.Max(info.maxDynamicOffsetChange, m.maxIdleDrift);
                }
            }
        }

        static string SourceAttribute(SkinPair pair, int vertex)
        {
            if (pair.sourceAttributes == null || vertex >= pair.sourceAttributes.Length) return "Unknown";
            var attribute = pair.sourceAttributes[vertex];
            return attribute.IsFixed() ? "Fixed" : attribute.IsMove() ? "Move" : "Invalid";
        }

        static Shape ColliderShape(ColliderComponent collider)
        {
            Vector3 center = collider.transform.TransformPoint(collider.center);
            Vector3 size = collider.GetSize(), scale = collider.transform.lossyScale;
            if (!Finite(center) || !Finite(size) || !Finite(scale)) throw new InvalidOperationException("Non-finite collider: " + collider.name);
            if (collider is MagicaSphereCollider)
                return new Shape { colliderPath = PathOf(collider.transform, body.transform), start = center, end = center,
                    startRadius = size.x * Mathf.Abs(scale.x), endRadius = size.x * Mathf.Abs(scale.x) };
            var capsule = (MagicaCapsuleCollider)collider;
            float axisScale = capsule.direction == MagicaCapsuleCollider.Direction.X ? scale.x
                : capsule.direction == MagicaCapsuleCollider.Direction.Y ? scale.y : scale.z;
            size *= Mathf.Abs(axisScale);
            Vector3 direction = capsule.transform.rotation * (capsule.GetLocalDir() * Mathf.Sign(axisScale));
            float startLength = Mathf.Max((capsule.alignedOnCenter ? size.z * .5f : 0f) - size.x, 0);
            float endLength = Mathf.Max((capsule.alignedOnCenter ? size.z * .5f : size.z - size.x) - size.y, 0);
            return new Shape { colliderPath = PathOf(collider.transform, body.transform), start = center + direction * startLength, end = center - direction * endLength,
                startRadius = size.x, endRadius = size.y };
        }

        static void AssertCustomMeshes()
        {
            foreach (var pair in skins)
            {
                Mesh custom = pair.owners[0].GetCustomMesh(pair.actual);
                Require(custom && custom != pair.owners[0].GetOriginalMesh(pair.actual) && pair.actual.sharedMesh == custom,
                    pair.path + ": custom mesh is currently assigned.");
            }
        }

        static void Validate(Measurement m)
        {
            string label = m.scenario + "/" + m.renderer;
            Record(m.allFinite && m.samples > 0, label + ": all sampled vertices are finite.");
            Record(m.minActualWorldY >= saved.options.minimumWorldY, label + ": cloth stays above the floor (" + m.minActualWorldY.ToString("F4") + " m).");
            Record(m.customMeshSamples == m.samples, label + ": custom mesh stayed assigned during every sample.");
            Record(m.maxDisplacement <= saved.options.maxDisplacement, label + ": displacement is within limit (" + m.maxDisplacement.ToString("F4") + " m).");
            Record(m.maxOffsetChange <= saved.options.maxFrameOffsetChange, label + ": per-sample cloth change is within limit (" + m.maxOffsetChange.ToString("F4") + " m).");
            Record(m.maxPenetrationWorsening <= saved.options.maxPenetrationWorsening, label + ": collider penetration worsening is within limit (" + m.maxPenetrationWorsening.ToString("F4") + " m).");
            if (m.scenario == "idle") Record(m.maxIdleDrift <= saved.options.maxIdleDrift, label + ": settled idle drift is within limit (" + m.maxIdleDrift.ToString("F4") + " m).");
            if (m.scenario == "recovery" || m.scenario == "teleport_20m" || m.scenario == "reactivation")
            {
                Record(m.lateRmsOffsetSpeed <= saved.options.maxRecoveryRmsSpeed, label + ": late residual speed is within limit (" + m.lateRmsOffsetSpeed.ToString("F4") + " m/s).");
                Record(m.lateMaxDisplacement <= saved.options.maxRecoveryDisplacement, label + ": late displacement is within limit (" + m.lateMaxDisplacement.ToString("F4") + " m).");
            }
        }

        static void FollowCamera()
        {
            Vector3 center = body.transform.position + Vector3.up * .9f;
            camera.transform.position = center + body.transform.rotation * new Vector3(2.3f, .4f, 4.2f);
            camera.transform.LookAt(center);
        }

        static void Capture(string name, int width = 800, int height = 1000, bool listCapture = true)
        {
            Directory.CreateDirectory(Output);
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var texture = new RenderTexture(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                string path = Output + "/" + name + ".png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                if (listCapture) report.captures.Add(path);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(texture);
            }
        }

        static void CaptureView(string name, Vector3 bodyRelativeOffset)
        {
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            try
            {
                Vector3 center = body.transform.position + Vector3.up * .9f;
                camera.transform.position = center + body.transform.rotation * bodyRelativeOffset;
                camera.transform.LookAt(center);
                Capture(name);
            }
            finally { camera.transform.SetPositionAndRotation(previousPosition, previousRotation); }
        }

        static string PathOf(Transform item, Transform root) => AnimationUtility.CalculateTransformPath(item, root);
        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }
        static void Record(bool condition, string message)
        {
            if (condition) report.checks.Add(message);
            else report.failures.Add(message);
        }
        static void WriteReport()
        {
            if (report == null) return;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/cloth-validation.json", JsonUtility.ToJson(report, true));
        }
        static void Finish(string error)
        {
            if (finishing) return;
            finishing = true;
            try
            {
                if (report == null) report = new Report();
                report.error = error;
                report.status = error == null && report.failures.Count == 0 ? "passed" : "failed";
                WriteReport();
                if (error != null) Debug.LogError("[LunaPeacefulClothValidation] " + error);
                else Debug.Log("[LunaPeacefulClothValidation] " + report.status + "; " + report.checks.Count + " checks; " + report.failures.Count + " failures. " + Output);
            }
            finally
            {
                Cleanup();
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                else Restore();
            }
        }
        static void Cleanup()
        {
            routine = null;
            MagicaManager.afterRenderingDelegate -= MarkRenderedFrame;
            if (skins != null) foreach (var pair in skins)
            {
                if (pair.actualBake) Object.DestroyImmediate(pair.actualBake);
                if (pair.ghostBake) Object.DestroyImmediate(pair.ghostBake);
            }
            skins = null;
            RestoreTime();
        }
        static void RestoreTime()
        {
            if (saved == null) saved = JsonUtility.FromJson<SavedState>(SessionState.GetString(Key + ".saved", ""));
            if (saved == null) return;
            Time.captureDeltaTime = saved.captureDeltaTime;
            Time.fixedDeltaTime = saved.fixedDeltaTime;
            Time.timeScale = saved.timeScale;
        }
        static void Restore()
        {
            try
            {
                Cleanup();
                if (saved == null) return;
                EditorSceneManager.RestoreSceneManagerSetup(saved.scenes.Select(s => new SceneSetup { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                Selection.objects = saved.selection.Select(id => GlobalObjectId.TryParse(id, out var parsed)
                    ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) : null).Where(o => o).ToArray();
                EditorApplication.isPaused = saved.paused;
            }
            finally
            {
                SessionState.SetBool(Key, false);
                SessionState.SetBool(Key + ".entered", false);
                finishing = false;
                pendingFrame = renderedFrame = -1;
            }
        }
    }
}
#endif
