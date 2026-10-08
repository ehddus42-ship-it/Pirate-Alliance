#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>Graphical batch test of the live blue fire gate, its lifecycle and its mission interaction.</summary>
    [InitializeOnLoad]
    public static class BlueFireGateValidation
    {
        const string Active = "Lobby.BlueFireGateValidation", Folder = "Library/BlueFireGateValidation";
        const string Captures = "Documentation/Liminal/BlueFireGate", Frames = Folder + "/frames";
        [Serializable] sealed class Report
        {
            public string status = "running", utc, unityVersion, graphicsDevice, frameFolder = Frames;
            public double elapsedSeconds;
            public int ribbons, meshes, triangles, lights, particleSystems, peakParticles, frameRate = 12;
            public List<float> frameTimes = new List<float>();
            public List<string> checks = new List<string>(), errors = new List<string>(), captures = new List<string>();
        }
        [Serializable] sealed class Pref { public string key; public bool existed; public int value; }
        [Serializable] sealed class Snapshot { public List<Pref> values = new List<Pref>(); }
        sealed class Wait { public Func<bool> condition; public float seconds; public string label; }

        sealed class GateResources
        {
            public int gateId;
            public int[] meshes, filters, lights, particles, materials;
            public static GateResources Take(BlueFireGate gate)
            {
                var filters = gate.GetComponentsInChildren<MeshFilter>(true);
                return new GateResources
                {
                    gateId = gate.GetInstanceID(),
                    filters = filters.Select(m => m.GetInstanceID()).OrderBy(i => i).ToArray(),
                    meshes = filters.Where(m => m.sharedMesh).Select(m => m.sharedMesh.GetInstanceID()).Distinct().OrderBy(i => i).ToArray(),
                    lights = gate.GetComponentsInChildren<Light>(true).Select(l => l.GetInstanceID()).OrderBy(i => i).ToArray(),
                    particles = gate.GetComponentsInChildren<ParticleSystem>(true).Select(p => p.GetInstanceID()).OrderBy(i => i).ToArray(),
                    materials = gate.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m)
                        .Select(m => m.GetInstanceID()).Distinct().OrderBy(i => i).ToArray()
                };
            }
            public bool Matches(BlueFireGate gate)
            {
                var b = Take(gate);
                return gateId == b.gateId && meshes.SequenceEqual(b.meshes) && filters.SequenceEqual(b.filters)
                    && lights.SequenceEqual(b.lights) && particles.SequenceEqual(b.particles) && materials.SequenceEqual(b.materials);
            }
        }
        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static double started, deadline;
        static int lastFrame = -1, originalCaptureFramerate;
        static LiminalRunDirector run;
        static PlayerMotor motor;
        static BlueFireGate gate;
        static Camera clipCamera;

        static BlueFireGateValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (report != null && SessionState.GetBool(Active, false) &&
                    (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    report.errors.Add(message + "\n" + stack);
            };
            EditorApplication.playModeStateChanged += mode =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (mode == PlayModeStateChange.EnteredPlayMode)
                {
                    report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                    started = EditorApplication.timeSinceStartup; lastFrame = -1;
                    originalCaptureFramerate = Time.captureFramerate; routine = Run();
                }
                else if (mode == PlayModeStateChange.EnteredEditMode)
                {
                    RestorePrefs();
                    if (report == null) report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                    if (!SessionState.GetBool(Active + ".finished", false)) report.errors.Add("Play Mode ended before validation completed.");
                    report.status = report.errors.Count == 0 ? "passed" : "failed"; Save();
                    SessionState.SetBool(Active, false); EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
                }
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use a separate graphical batch editor without -quit or -nographics.");
            report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName };
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Shader validation requires a graphics device.");
                EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity", OpenSceneMode.Single);
                CapturePrefs(); Save(); SessionState.SetBool(Active + ".finished", false); SessionState.SetBool(Active, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception ex)
            {
                RestorePrefs(); report.errors.Add(ex.ToString()); report.status = "failed"; Save(); EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 180) throw new TimeoutException("Blue gate validation exceeded 180 wall seconds.");
                if (waiting != null)
                {
                    if (!waiting.condition()) { if (now > deadline) throw new TimeoutException(waiting.label); return; }
                    waiting = null;
                }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (!routine.MoveNext()) { Finish(null); return; }
                // PNG rendering/encoding can take seconds inside MoveNext. Start each new wait afterwards.
                if (routine.Current is Wait wait) { waiting = wait; deadline = EditorApplication.timeSinceStartup + wait.seconds; }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }
        static Wait Until(Func<bool> condition, float seconds, string label) => new Wait { condition = condition, seconds = seconds, label = label };
        static Wait Seconds(float seconds)
        { float end = Time.time + seconds; return Until(() => Time.time >= end, seconds * 4 + 4, "Game time wait " + seconds); }
        static Wait RealSeconds(double seconds)
        { double end = EditorApplication.timeSinceStartup + seconds; return Until(() => EditorApplication.timeSinceStartup >= end, (float)seconds + 3, "Wall time observation"); }
        static void Check(bool passed, string label)
        { if (!passed) throw new InvalidOperationException(label); report.checks.Add(label); Save(); }

        static IEnumerator Run()
        {
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            // Automated runs skip augment offers so random augments cannot change what this validation measures.
            LiminalRunDirector.SuppressAugmentOffers = true;
            motor = run.player.GetComponent<PlayerMotor>();
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            var gates = run.Lobby.GetComponentsInChildren<BlueFireGate>(true);
            Check(gates.Length == 1, "Exactly one blue fire gate exists in the live lobby."); gate = gates[0];
            Check(gate.isActiveAndEnabled && gate.transform.parent && gate.transform.parent.name == "Gate", "Blue flames run under the existing gate structure.");
            Check(gate.GetComponentsInChildren<Collider>(true).Length == 0, "The fire effect adds no colliders.");
            Check(run.Lobby.Officials.Count == 13, "All 13 existing lobby characters remain intact.");
            var shader = Resources.Load<Shader>("LiminalLobby/BlueFireGate");
            Check(shader && shader.name == "PirateAlliance/BlueFireGate" && shader.isSupported, "The dedicated blue flame shader is included and supported.");
            var filters = gate.GetComponentsInChildren<MeshFilter>(true);
            report.meshes = filters.Length;
            report.triangles = filters.Sum(f => f.sharedMesh ? Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (int)f.sharedMesh.GetIndexCount(s) / 3) : 0);
            report.lights = gate.GetComponentsInChildren<Light>(true).Length;
            report.particleSystems = gate.GetComponentsInChildren<ParticleSystem>(true).Length;
            report.ribbons = gate.FlameRibbonCount;
            Check(report.meshes > 0 && report.triangles > 0 && report.triangles < 100000, "Flame geometry is bounded: " + report.triangles + " triangles.");
            Check(report.ribbons > 20 && report.lights <= 3 && report.particleSystems <= 2, "Layered flames use a fixed small set of lights and particle systems.");
            Check(gate.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported && !m.shader.name.Contains("Error"))),
                "Every gate renderer has a valid supported material.");
            var baseline = GateResources.Take(gate);
            float before = gate.Elapsed; yield return Seconds(.45f);
            Check(gate.Elapsed > before + .20f, "The explicit flame animation clock advances.");
            Check(gate.ParticleCount <= gate.MaxParticleCount, "Blue embers remain inside the particle budget.");

            motor.ResetAt(run.Lobby.SpawnPoint);
            var walk = WalkTo(new Vector3(0, .05f, 6), "central approach");
            while (walk.MoveNext()) yield return walk.Current;
            var follow = Camera.main.GetComponent<IsometricFollowCamera>(); if (follow) follow.Snap();
            yield return Seconds(.2f);
            Capture(Camera.main, Captures + "/blue-fire-player-approach.png", 1800, 1200, true);
            CaptureView("blue-fire-gate", new Vector3(0, 3.6f, 13.5f), new Vector3(5, 4, -8), 5.5f);
            CaptureView("blue-fire-overview", new Vector3(0, 1, 2), new Vector3(24, 31, -30), 23);
            ValidateShader(shader);

            before = gate.Elapsed; Time.timeScale = 0; yield return RealSeconds(.45f);
            Check(Mathf.Abs(gate.Elapsed - before) < .00001f, "Time.timeScale=0 freezes the gate animation clock.");
            Time.timeScale = 1; yield return Seconds(.25f);
            Check(gate.Elapsed > before + .10f, "Blue flames resume after unpausing.");
            // This lifecycle probe temporarily removes the floor as well as the gate. Preserve the player
            // position so the enabled motor cannot leave the subsequent camera/route tests underground.
            Vector3 positionBeforeDisable = motor.transform.position;
            run.Lobby.gameObject.SetActive(false); before = gate.Elapsed; yield return RealSeconds(.35f);
            Check(!gate.isActiveAndEnabled && Mathf.Abs(gate.Elapsed - before) < .00001f, "Disabling the lobby stops fire time without resetting it.");
            run.Lobby.gameObject.SetActive(true);
            Physics.SyncTransforms();
            motor.ResetAt(positionBeforeDisable);
            yield return Seconds(.25f);
            CheckPlayerHeight("after restoring the temporary disabled lobby");
            Check(gate.Elapsed > before + .10f && baseline.Matches(gate), "Re-enabling resumes the same flame resources.");

            // 36 actual simulation frames, 12 fps, with a fixed camera for a three-second review clip.
            clipCamera = ReviewCamera(new Vector3(0, 3.6f, 13.5f), new Vector3(5, 4, -8), 5.5f);
            Directory.CreateDirectory(Frames); Time.captureFramerate = 12; yield return null;
            for (int i = 0; i < 36; i++)
            {
                report.frameTimes.Add(gate.Elapsed); report.peakParticles = Mathf.Max(report.peakParticles, gate.ParticleCount);
                Capture(clipCamera, Frames + "/frame-" + i.ToString("D3") + ".png", 960, 640, false);
                if (i < 35) yield return null;
            }
            Time.captureFramerate = originalCaptureFramerate;
            Object.DestroyImmediate(clipCamera.gameObject); clipCamera = null;
            Check(report.frameTimes.Count == 36 && report.frameTimes.Zip(report.frameTimes.Skip(1), (a, b) => b > a).All(v => v),
                "Recorded 36 advancing flame frames at fixed 12 fps simulation cadence.");
            Check(report.frameTimes.Zip(report.frameTimes.Skip(1), (a, b) => Mathf.Abs((b - a) - 1f / 12f) < .0001f).All(v => v),
                "Every recorded flame time interval matches 1/12 second within floating-point tolerance.");
            Check(report.peakParticles <= gate.MaxParticleCount && baseline.Matches(gate), "The recording stays within particle limits without creating extra gate resources.");

            walk = WalkTo(new Vector3(0, .05f, 9.1f), "gate mission interaction");
            while (walk.MoveNext()) yield return walk.Current;
            if (follow) follow.Snap();
            yield return null;
            Capture(Camera.main, Captures + "/blue-fire-player-view.png", 1800, 1200, true);
            Check(run.Lobby.Nearest != null && run.Lobby.Nearest.label == "게이트 진입", "The real player reaches the gate interaction through the blue flames.");
            run.Lobby.Nearest.use(); yield return null;
            var window = GameObject.Find("LobbyWindow");
            Check(window && run.Lobby.WindowOpen, "The gate opens the existing mission selection window.");
            CheckMissionText(window);
            var mission = run.AvailableMissions.FirstOrDefault(m => m.id == "forest_survey" && m.IsAvailable)
                ?? run.AvailableMissions.First(m => m.IsAvailable);
            window.GetComponentsInChildren<Button>().First(b => b.name == "Mission_" + mission.id).onClick.Invoke();
            yield return null;
            var enter = window.GetComponentsInChildren<Button>().First(b => b.name == "Enter");
            Check(enter.interactable, "The selected mission retains an enabled departure button."); enter.onClick.Invoke();
            yield return Until(() => run.Phase == LiminalRunPhase.Exploring && !run.Lobby.gameObject.activeSelf, 15, "Selected mission departure");
            Check(run.ActiveMission != null && run.ActiveMission.id == mission.id, "Gate departure starts the explicitly selected mission.");
            before = gate.Elapsed; yield return Seconds(.35f);
            Check(!gate.isActiveAndEnabled && Mathf.Abs(gate.Elapsed - before) < .00001f, "Dormant lobby flames remain stopped during dungeon play.");
            run.ReturnToLobby(); yield return Until(() => run.Phase == LiminalRunPhase.Lobby && gate.isActiveAndEnabled, 8, "Return from mission");
            yield return Seconds(.30f);
            Check(gate.Elapsed > before && baseline.Matches(gate), "Returning resumes the original gate resources.");
            for (int i = 0; i < 2; i++)
            {
                Check(run.EnterMission(mission.id), "Repeat departure " + (i + 1) + " starts the selected mission."); yield return null;
                Check(!gate.isActiveAndEnabled, "Repeat departure " + (i + 1) + " deactivates the lobby effect.");
                run.ReturnToLobby(); yield return Seconds(.15f);
                Check(run.Lobby.GetComponentsInChildren<BlueFireGate>(true).Length == 1 && baseline.Matches(gate),
                    "Repeat return " + (i + 1) + " creates no duplicate gate meshes, lights, particles or materials.");
            }
            ValidateShader(shader);
            Check(report.errors.Count == 0, "No runtime, assertion or graphics errors were logged.");
        }

        static IEnumerator WalkTo(Vector3 localPoint, string label)
        {
            var target = run.Lobby.transform.TransformPoint(localPoint); double limit = EditorApplication.timeSinceStartup + 12;
            while (Vector3.ProjectOnPlane(target - motor.transform.position, Vector3.up).magnitude > .24f)
            {
                AssertPlayerHeight(label);
                if (EditorApplication.timeSinceStartup > limit) throw new TimeoutException("Blocked route: " + label + ", at " + run.Lobby.transform.InverseTransformPoint(motor.transform.position));
                var direction = Vector3.ProjectOnPlane(target - motor.transform.position, Vector3.up).normalized;
                var view = motor.viewCamera ? motor.viewCamera.transform : null;
                var right = PlayerMotor.CameraRelative(Vector2.right, view); var forward = PlayerMotor.CameraRelative(Vector2.up, view);
                motor.SetAutomationInput(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), target, false);
                yield return null;
            }
            motor.SetAutomationInput(Vector2.zero, target + Vector3.forward, false);
            yield return Seconds(.15f);
            CheckPlayerHeight(label);
            Check(true, "Walked the real player route: " + label + ".");
        }

        static float AssertPlayerHeight(string label)
        {
            float y = run.Lobby.transform.InverseTransformPoint(motor.transform.position).y;
            // The plaza is y=0; allow only a small contact/skin tolerance below the surface.
            if (y < -.03f || y > .5f)
                throw new InvalidOperationException("Player left the plaza surface during " + label + ": localY=" + y.ToString("F4"));
            return y;
        }

        static void CheckPlayerHeight(string label)
        {
            float y = AssertPlayerHeight(label);
            Check(true, "Player stays at plaza height " + label + " (localY=" + y.ToString("F4") + ").");
        }

        static void ValidateShader(Shader shader)
        {
            Check(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Liminal/Resources/LiminalLobby/BlueFireGate.shader") == shader,
                "The live shader resolves to the authored blue fire asset.");
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
            if (errors.Length > 0) throw new InvalidOperationException("Shader compilation errors: " + string.Join("\n", errors.Select(e => e.message)));
            Check(shader.isSupported && errors.Length == 0, "Blue fire shader reports zero compiler errors after live rendering.");
        }

        static void CheckMissionText(GameObject window)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var text in window.GetComponentsInChildren<TextMeshProUGUI>())
            {
                text.ForceMeshUpdate();
                string value = new string(text.GetParsedText().Where(c => !char.IsControl(c)).ToArray());
                if (value.Contains("\ufffd") || !text.font.HasCharacters(value, out uint[] missing, true, true) || text.isTextOverflowing || text.isTextTruncated)
                    throw new InvalidOperationException("Gate selection text is broken or clipped: " + text.name);
            }
            Check(true, "Mission selection retains its Korean glyphs and unclipped text.");
        }

        static Camera ReviewCamera(Vector3 localFocus, Vector3 offset, float size)
        {
            var go = new GameObject("Temporary blue gate review camera");
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = size; camera.nearClipPlane = .1f; camera.farClipPlane = 130;
            var source = Camera.main.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var focus = run.Lobby.transform.TransformPoint(localFocus);
            go.transform.position = focus + run.Lobby.transform.TransformDirection(offset);
            go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position); return camera;
        }

        static void CaptureView(string name, Vector3 focus, Vector3 offset, float size)
        {
            var camera = ReviewCamera(focus, offset, size);
            try { Capture(camera, Captures + "/" + name + ".png", 1800, 1200, true); }
            finally { Object.DestroyImmediate(camera.gameObject); }
        }

        static void Capture(Camera camera, string path, int width, int height, bool review)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            float aspect = camera.aspect; var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                target.Create(); camera.aspect = width / (float)height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP capture unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, pixels.EncodeToPNG());
                if (review) { report.captures.Add(path); Save(); }
            }
            finally { camera.aspect = aspect; RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
        }

        static void CapturePrefs()
        {
            var keys = new HashSet<string> { "Hunter.MagicStones" };
            foreach (HunterProgress.Upgrade upgrade in Enum.GetValues(typeof(HunterProgress.Upgrade))) keys.Add("Hunter.Upgrade." + upgrade);
            var catalog = Resources.Load<GateMissionCatalog>("GateMissions");
            if (catalog) foreach (var mission in catalog.Missions)
            {
                keys.Add(HunterProgress.DestinationDiscoveryKey(mission.destinationId));
                foreach (var stage in mission.stages) if (stage) keys.Add(HunterProgress.StageDiscoveryKey(stage.stageId));
            }
            var snapshot = new Snapshot();
            foreach (var key in keys) snapshot.values.Add(new Pref { key = key, existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetInt(key) });
            SessionState.SetString(Active + ".prefs", JsonUtility.ToJson(snapshot));
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
        static void RestorePrefs()
        {
            string json = SessionState.GetString(Active + ".prefs", ""); if (string.IsNullOrEmpty(json)) return;
            foreach (var item in JsonUtility.FromJson<Snapshot>(json).values)
                if (item.existed) PlayerPrefs.SetInt(item.key, item.value); else PlayerPrefs.DeleteKey(item.key);
            PlayerPrefs.Save();
        }
        static void Save()
        {
            Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true);
            SessionState.SetString(Active + ".report", json); File.WriteAllText(Folder + "/validation.json", json);
        }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            if (motor) motor.ReleaseAutomation();
            if (clipCamera) { Object.DestroyImmediate(clipCamera.gameObject); clipCamera = null; }
            if (run && run.Lobby) run.Lobby.gameObject.SetActive(true);
            HitFeedback.CancelHitStop(); Time.timeScale = 1; Time.captureFramerate = originalCaptureFramerate; RestorePrefs();
            if (error != null) report.errors.Add(error);
            report.elapsedSeconds = EditorApplication.timeSinceStartup - started;
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("BLUE_FIRE_GATE_VALIDATION: " + report.status + (error == null ? "" : "\n" + error)); EditorApplication.ExitPlaymode();
        }
    }
}
#endif
