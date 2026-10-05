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
    /// <summary>Explicit batch play test of the live lobby, its authored cast and the existing player interactions.</summary>
    [InitializeOnLoad]
    public static class LobbyExpansionValidation
    {
        const string Active = "Lobby.ExpansionValidation";
        const string Folder = "Library/LobbyExpansionValidation";
        static string Captures => SessionState.GetString(Active + ".output", "Documentation/Liminal/LobbyExpansion");
        [Serializable] sealed class Report
        {
            public string status = "running", utc, unityVersion;
            public double elapsedSeconds;
            public List<string> checks = new List<string>(), errors = new List<string>(), captures = new List<string>();
        }
        [Serializable] sealed class Pref { public string key; public bool existed; public int value; }
        [Serializable] sealed class Snapshot { public List<Pref> values = new List<Pref>(); }
        sealed class Wait { public Func<bool> condition; public float seconds; public string label; }
        sealed class Pose
        {
            public Transform[] bones;
            public Quaternion[] rotations;
            public Vector3[] positions;
            public static Pose Take(LobbyNpc npc)
            {
                var names = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
                    HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                var bones = names.Select(npc.Animator.GetBoneTransform).Where(b => b).ToArray();
                return new Pose { bones = bones, rotations = bones.Select(b => b.localRotation).ToArray(), positions = bones.Select(b => b.localPosition).ToArray() };
            }
            public bool Changed(float degrees = .15f, float metres = .0015f)
            {
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] && (Quaternion.Angle(bones[i].localRotation, rotations[i]) > degrees || Vector3.Distance(bones[i].localPosition, positions[i]) > metres)) return true;
                return false;
            }
        }
        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static double started, deadline;
        static int lastFrame = -1;
        static LiminalRunDirector run;
        static PlayerMotor motor;
        static LobbyNpc[] cast;
        static LobbyRoutine[] routines;

        static LobbyExpansionValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (report != null && SessionState.GetBool(Active, false) &&
                    (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) report.errors.Add(message + "\n" + stack);
            };
            EditorApplication.playModeStateChanged += mode =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (mode == PlayModeStateChange.EnteredPlayMode)
                {
                    report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                    started = EditorApplication.timeSinceStartup; lastFrame = -1; routine = Run();
                }
                if (mode == PlayModeStateChange.EnteredEditMode)
                {
                    RestorePrefs();
                    if (report == null) report = JsonUtility.FromJson<Report>(SessionState.GetString(Active + ".report", "{}"));
                    if (!SessionState.GetBool(Active + ".finished", false)) report.errors.Add("Play Mode ended before validation completed.");
                    report.status = report.errors.Count == 0 ? "passed" : "failed"; Save();
                    SessionState.SetBool(Active, false);
                    EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
                }
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use a separate batch editor in Edit Mode, without -quit.");
            report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int output = Array.IndexOf(args, "-lobbyValidationOutput");
                SessionState.SetString(Active + ".output", output >= 0 && output + 1 < args.Length ? args[output + 1] : "Documentation/Liminal/LobbyExpansion");
                EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity", OpenSceneMode.Single);
                CapturePrefs(); Save();
                SessionState.SetBool(Active + ".finished", false); SessionState.SetBool(Active, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception ex)
            {
                RestorePrefs(); report.errors.Add(ex.ToString()); report.status = "failed"; Save();
                EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 180) throw new TimeoutException("Lobby expansion validation exceeded 180 wall seconds.");
                if (waiting != null)
                {
                    if (!waiting.condition()) { if (now > deadline) throw new TimeoutException(waiting.label); return; }
                    waiting = null;
                }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (!routine.MoveNext()) { Finish(null); return; }
                // Capturing several review cameras may take seconds inside MoveNext; start the wait after that work.
                if (routine.Current is Wait wait) { waiting = wait; deadline = EditorApplication.timeSinceStartup + wait.seconds; }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }
        static Wait Until(Func<bool> condition, float timeout, string label) => new Wait { condition = condition, seconds = timeout, label = label };
        static Wait Seconds(float value)
        { float end = Time.time + value; return Until(() => Time.time >= end, value * 4 + 4, "Game-time wait " + value); }
        static Wait RealSeconds(double value)
        { double end = EditorApplication.timeSinceStartup + value; return Until(() => EditorApplication.timeSinceStartup >= end, (float)value + 2, "Pause observation"); }
        static void Check(bool passed, string label)
        { if (!passed) throw new InvalidOperationException(label); report.checks.Add(label); Save(); }

        static IEnumerator Run()
        {
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            motor = run.player.GetComponent<PlayerMotor>();
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            cast = run.Lobby.Officials.ToArray();
            routines = run.Lobby.GetComponentsInChildren<LobbyRoutine>();
            Check(cast.Length == 13 && cast.All(n => n), "The live lobby contains all 13 officials and hunters.");
            var ground = run.Lobby.GetComponentInChildren<LobbyRecoveryGround>();
            var streets = run.Lobby.GetComponentInChildren<LobbyRestoredStreets>();
            var symbols = run.Lobby.GetComponentInChildren<LobbySymbolFixtures>();
            var neighbourhood = run.Lobby.GetComponentInChildren<LobbyRecoveredBlock>();
            Check(ground && streets && symbols && neighbourhood, "Recovered paving, repaired streets, symbol-only fixtures and neighbourhood are present.");
            Check(ground.GetComponentsInChildren<Collider>().Length == 0, "Surface repairs introduce no collision steps or snags.");
            Check(symbols.FixtureCount == 5 && symbols.GetComponentsInChildren<Collider>().Length == 5, "All five former printed fixtures are replaced with symbol-only furniture.");
            foreach (var section in new Component[] { ground, streets, symbols, neighbourhood })
            {
                Check(section.GetComponentsInChildren<TextMesh>(true).Length == 0 && section.GetComponentsInChildren<TMP_Text>(true).Length == 0,
                    section.name + " uses geometric markings without readable street labels.");
                var geometry = section.GetComponentsInChildren<MeshFilter>();
                long triangles = geometry.Sum(f => f.sharedMesh ? Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (long)f.sharedMesh.GetIndexCount(s) / 3) : 0);
                Check(geometry.Length > 0 && triangles > 0 && triangles < 100000,
                    section.name + " has bounded static geometry: " + triangles + " triangles in " + geometry.Length + " meshes.");
                Check(section.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m && m.shader && !m.shader.name.Contains("Error"))),
                    section.name + " has valid render materials.");
            }
            var roles = new[] { Find("FieldMedic"), Find("GateEngineer"), Find("RookieHunter"), Find("VeteranHunter") };
            string[][] labels = { new[] { "idle", "walk", "talk", "greet", "inspect", "listen" },
                new[] { "idle", "walk", "talk", "greet", "phone", "inspect" },
                new[] { "idle", "walk", "talk", "greet", "warmup", "breath" },
                new[] { "idle", "walk", "talk", "greet", "drink", "look" } };
            for (int i = 0; i < roles.Length; i++)
            {
                var npc = roles[i];
                Check(npc.AnimationReady && npc.Animator.avatar && npc.Animator.avatar.isValid && npc.Animator.avatar.isHuman,
                    npc.name + " has a valid live Humanoid avatar and animation graph.");
                var skins = npc.GetComponentsInChildren<SkinnedMeshRenderer>();
                Check(skins.Any(r => r.sharedMesh && r.sharedMesh.vertexCount > 500 && r.bones.Length > 15), npc.name + " uses the authored skinned body, not a missing-model stand-in.");
                Check(skins.All(r => r.sharedMaterials.All(m => m && m.shader && !m.shader.name.Contains("Error"))), npc.name + " has valid body materials.");
                Check(npc.ClipCount == 6 && labels[i].All(npc.Has), npc.name + " resolves all six expected clips.");
                foreach (string label in labels[i])
                {
                    var clip = Resources.LoadAll<AnimationClip>(LobbyNpc.ClipPath(npc.Character, label)).FirstOrDefault(c => c && !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                    Check(clip && clip.humanMotion && clip.length > .2f, npc.name + "/" + label + " is a real Humanoid clip.");
                }
            }
            Check(Find("MedicalVisitor").Character == Find("RookieHunter").Character && Find("MedicalVisitor").AnimationReady,
                "The medical visitor reuses the hunter asset with a separate live animation graph.");

            // Exercise each authored state without the ambient routine overriding it midway through the observation.
            foreach (var r in routines) r.enabled = false;
            foreach (var npc in cast) npc.StopOnce();
            for (int clipIndex = 0; clipIndex < 6; clipIndex++)
            {
                for (int i = 0; i < roles.Length; i++) roles[i].Play(labels[i][clipIndex]);
                yield return Seconds(.4f);
                var poses = roles.Select(Pose.Take).ToArray();
                yield return Seconds(.65f);
                for (int i = 0; i < roles.Length; i++)
                    Check(poses[i].Changed(), roles[i].name + "/" + labels[i][clipIndex] + " actually moves humanoid bones.");
            }
            var engineer = Find("GateEngineer"); var veteran = Find("VeteranHunter"); var medic = Find("FieldMedic"); var rookie = Find("RookieHunter");
            engineer.Play("phone"); veteran.Play("drink"); medic.Play("talk"); rookie.Play("warmup"); Find("MedicalVisitor").Play("breath");
            yield return Seconds(.6f);
            Check(engineer.GetComponentInChildren<LobbyHandProp>().Visible && veteran.GetComponentInChildren<LobbyHandProp>().Visible,
                "Telephone and drink props follow their active hand animations.");
            CaptureView("validation-overview", new Vector3(0, 1, 2), new Vector3(24, 31, -30), 23);
            CaptureView("validation-repairs", new Vector3(-16.5f, .5f, -7), new Vector3(5, 7, -8), 5.5f);
            CaptureView("validation-crossing", new Vector3(-2, .5f, -16), new Vector3(6, 9, -9), 7);
            CaptureView("validation-facilities", new Vector3(13.2f, .9f, -4.6f), new Vector3(-5, 3, -4), 2.1f);
            Camera.main.GetComponent<IsometricFollowCamera>().Snap();
            Capture(Camera.main, "validation-player-view");
            CaptureView("validation-medical", Midpoint(medic, Find("MedicalVisitor")) + Vector3.up, new Vector3(5, 4.5f, -6), 3.7f);
            CaptureView("validation-workshop", engineer.transform.localPosition + new Vector3(0, 1, .8f), new Vector3(-5, 4, 6), 3.35f);
            CaptureView("validation-rest", rookie.transform.localPosition + new Vector3(0, 1, .9f), new Vector3(5, 5, 6), 4.1f);
            CaptureView("validation-briefing", veteran.transform.localPosition + new Vector3(.6f, 1, 0), new Vector3(6, 4, -5), 3.4f);
            engineer.Play("idle"); veteran.Play("idle");
            yield return Seconds(.45f);
            Check(!engineer.GetComponentInChildren<LobbyHandProp>().Visible && !veteran.GetComponentInChildren<LobbyHandProp>().Visible,
                "Hand props disappear when their task clips finish.");

            var pausePose = Pose.Take(rookie); var pausePosition = rookie.transform.position;
            Time.timeScale = 0; yield return RealSeconds(.6f);
            Check(!pausePose.Changed(.01f, .00001f) && rookie.transform.position == pausePosition, "Pausing freezes skeleton animation and ambient motion.");
            Time.timeScale = 1;
            foreach (var r in routines) r.enabled = true;
            var stationRoutines = roles.Select(n => n.GetComponent<LobbyRoutine>()).ToArray();
            var changes = stationRoutines.Select(r => r.ActivityChanges).ToArray();
            var patrol = routines.First(r => r.Kind == LobbyRoutine.Mode.Patrol);
            float travelled = patrol.DistanceTravelled;
            yield return Seconds(25);
            for (int i = 0; i < stationRoutines.Length; i++)
                Check(stationRoutines[i].ActivityChanges >= changes[i] + 2, roles[i].name + " naturally cycles more than one work activity.");
            Check(patrol.DistanceTravelled > travelled + 1 && patrol.CompletedStops > 0, "The existing clerk patrol still walks and pauses at its route stops.");

            // A fresh approach produces one greeting, then staying nearby must not retrigger it.
            motor.ResetAt(medic.transform.position + medic.transform.right * 1.8f + Vector3.up * .05f);
            var medicalRoutine = medic.GetComponent<LobbyRoutine>(); int greetings = medicalRoutine.GreetingCount;
            yield return Until(() => medicalRoutine.GreetingCount > greetings, 4, "Medic proximity greeting");
            Check(medic.Current == "greet", "The medic greets a player who approaches.");
            yield return Seconds(6);
            Check(medicalRoutine.GreetingCount == greetings + 1, "Standing beside an NPC does not repeatedly trigger greetings.");

            motor.ResetAt(run.Lobby.SpawnPoint);
            foreach (var point in new[] { new Vector3(0, .05f, -14), new Vector3(-16, .05f, -14), new Vector3(-16, .05f, -3) })
            {
                var perimeterWalk = WalkTo(point, "repaired plaza circulation " + point);
                while (perimeterWalk.MoveNext()) yield return perimeterWalk.Current;
            }

            // Follow playable floor routes, then use the same interaction delegates as E/gamepad A.
            motor.ResetAt(run.Lobby.SpawnPoint);
            var walk = WalkTo(new Vector3(0, .05f, -1), "central lobby approach");
            while (walk.MoveNext()) yield return walk.Current;
            walk = WalkTo(new Vector3(-6.7f, .05f, -1), "association counter approach");
            while (walk.MoveNext()) yield return walk.Current;
            walk = WalkTo(new Vector3(-6.7f, .05f, 1.8f), "association interaction space");
            while (walk.MoveNext()) yield return walk.Current;
            yield return null;
            Check(run.Lobby.Nearest != null && run.Lobby.Nearest.anchor == run.Lobby.Agent.transform, "The player can reach the original upgrade agent through the decorated lobby.");
            run.Lobby.Nearest.use(); yield return null;
            Check(run.Lobby.WindowOpen && run.Lobby.Agent.Talking && !motor.enabled, "The upgrade interaction still opens its modal and agent conversation.");
            CheckWindow("Upgrades");
            var window = GameObject.Find("LobbyWindow");
            var buy = window.GetComponentsInChildren<Button>().First(b => b.name == "Buy0");
            int before = HunterProgress.Level(HunterProgress.Upgrade.Vitality);
            Check(buy.interactable, "The original upgrade purchase button remains usable.");
            buy.onClick.Invoke();
            Check(HunterProgress.Level(HunterProgress.Upgrade.Vitality) == before + 1, "Upgrade purchase still changes the correct permanent stat.");
            window.GetComponentsInChildren<Button>().First(b => b.name == "Close").onClick.Invoke(); yield return null;
            Check(!run.Lobby.WindowOpen && motor.enabled && !run.Lobby.Agent.Talking, "Closing upgrades restores movement and the agent's idle state.");
            walk = WalkTo(new Vector3(0, .05f, 1.8f), "return to the central route");
            while (walk.MoveNext()) yield return walk.Current;
            walk = WalkTo(new Vector3(0, .05f, 9.1f), "gate interaction space");
            while (walk.MoveNext()) yield return walk.Current;
            yield return null;
            Check(run.Lobby.Nearest != null && run.Lobby.Nearest.label == "게이트 진입", "New NPCs and decorations keep the gate approach accessible.");
            run.Lobby.Nearest.use(); yield return null;
            CheckWindow("Gate missions");
            var enter = GameObject.Find("LobbyWindow").GetComponentsInChildren<Button>().First(b => b.name == "Enter");
            Check(enter.interactable, "Mission selection retains its working departure button.");
            var ids = cast.Select(n => n.GetInstanceID()).ToArray();
            enter.onClick.Invoke();
            yield return Until(() => run.Phase == LiminalRunPhase.Exploring && !run.Lobby.gameObject.activeSelf, 15, "Real gate transition");
            Check(cast.All(n => !n.isActiveAndEnabled), "Departing through the gate deactivates every lobby NPC.");
            run.ReturnToLobby();
            yield return Until(() => run.Phase == LiminalRunPhase.Lobby && run.Lobby.gameObject.activeSelf, 8, "Return to lobby");
            Check(cast.Select(n => n.GetInstanceID()).SequenceEqual(ids) && cast.All(n => n.AnimationReady && n.isActiveAndEnabled),
                "Returning reuses and resumes all 13 NPCs without duplicate graphs or cast objects.");
            var returnPoses = roles.Select(Pose.Take).ToArray(); yield return Seconds(.75f);
            Check(returnPoses.All(p => p.Changed()), "All four new roles animate again after the lobby is re-enabled.");
            Check(run.Lobby.Officials.Count == 13 && run.Lobby.GetComponentsInChildren<LobbyNpc>().Length == 13,
                "Repeated lobby entry does not duplicate the expanded cast.");
        }

        static LobbyNpc Find(string name)
        {
            var npc = cast.FirstOrDefault(n => n.name == name);
            if (!npc) throw new InvalidOperationException("Missing live NPC: " + name);
            return npc;
        }
        static Vector3 Midpoint(LobbyNpc first, LobbyNpc second) => (first.transform.localPosition + second.transform.localPosition) * .5f;

        static IEnumerator WalkTo(Vector3 localPoint, string label)
        {
            Vector3 target = run.Lobby.transform.TransformPoint(localPoint);
            double limit = EditorApplication.timeSinceStartup + 10;
            while (Vector3.ProjectOnPlane(target - motor.transform.position, Vector3.up).magnitude > .24f)
            {
                if (EditorApplication.timeSinceStartup > limit) throw new TimeoutException("Player route obstructed: " + label + " at " + run.Lobby.transform.InverseTransformPoint(motor.transform.position));
                Vector3 direction = Vector3.ProjectOnPlane(target - motor.transform.position, Vector3.up).normalized;
                var camera = motor.viewCamera ? motor.viewCamera.transform : null;
                Vector3 right = PlayerMotor.CameraRelative(Vector2.right, camera), forward = PlayerMotor.CameraRelative(Vector2.up, camera);
                motor.SetAutomationInput(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), target, false);
                yield return null;
            }
            motor.SetAutomationInput(Vector2.zero, target + Vector3.forward, false);
            yield return Seconds(.15f);
            Check(true, "Walked the real player route: " + label + ".");
        }

        static void CheckWindow(string label)
        {
            Canvas.ForceUpdateCanvases();
            var window = GameObject.Find("LobbyWindow");
            Check(window, label + " window is active.");
            foreach (var text in window.GetComponentsInChildren<TextMeshProUGUI>())
            {
                text.ForceMeshUpdate();
                string value = new string(text.GetParsedText().Where(c => !char.IsControl(c)).ToArray());
                if (value.Contains("\ufffd") || !text.font.HasCharacters(value, out uint[] missing, true, true) || text.isTextOverflowing || text.isTextTruncated)
                    throw new InvalidOperationException(label + " text is broken or clipped: " + text.name + ": " + value);
            }
            Check(true, label + " labels retain their Korean glyphs and fit their panels.");
        }

        static void CaptureView(string name, Vector3 localFocus, Vector3 offset, float size)
        {
            var go = new GameObject("Temporary lobby review camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = size;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                var mainData = Camera.main.GetUniversalAdditionalCameraData();
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = mainData.renderPostProcessing; data.volumeLayerMask = mainData.volumeLayerMask;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                Vector3 focus = run.Lobby.transform.TransformPoint(localFocus);
                go.transform.position = focus + run.Lobby.transform.TransformDirection(offset);
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position);
                Capture(camera, name);
            }
            finally { Object.DestroyImmediate(go); }
        }
        static void Capture(Camera camera, string name)
        {
            const int width = 1800, height = 1200;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            float previousAspect = camera.aspect;
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                target.Create(); camera.aspect = width / (float)height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP screenshot request unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Captures); string path = Captures + "/" + name + ".png";
                File.WriteAllBytes(path, pixels.EncodeToPNG()); report.captures.Add(path); Save();
            }
            finally { camera.aspect = previousAspect; RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
        }
        static void CapturePrefs()
        {
            var keys = new HashSet<string> { "Hunter.MagicStones" };
            foreach (HunterProgress.Upgrade upgrade in Enum.GetValues(typeof(HunterProgress.Upgrade))) keys.Add("Hunter.Upgrade." + upgrade);
            foreach (var mission in Resources.Load<GateMissionCatalog>("GateMissions").Missions)
            {
                keys.Add(HunterProgress.DestinationDiscoveryKey(mission.destinationId));
                foreach (var stage in mission.stages) keys.Add(HunterProgress.StageDiscoveryKey(stage.stageId));
            }
            var snapshot = new Snapshot();
            foreach (var key in keys) snapshot.values.Add(new Pref { key = key, existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetInt(key) });
            SessionState.SetString(Active + ".prefs", JsonUtility.ToJson(snapshot));
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.SetInt("Hunter.MagicStones", 99999); PlayerPrefs.Save();
        }
        static void RestorePrefs()
        {
            string json = SessionState.GetString(Active + ".prefs", ""); if (string.IsNullOrEmpty(json)) return;
            foreach (var item in JsonUtility.FromJson<Snapshot>(json).values)
                if (item.existed) PlayerPrefs.SetInt(item.key, item.value); else PlayerPrefs.DeleteKey(item.key);
            PlayerPrefs.Save();
        }
        static void Save()
        { Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true); SessionState.SetString(Active + ".report", json); File.WriteAllText(Folder + "/validation.json", json); }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            if (motor) motor.ReleaseAutomation(); HitFeedback.CancelHitStop(); Time.timeScale = 1; RestorePrefs();
            if (routines != null) foreach (var r in routines) if (r) r.enabled = true;
            if (error != null) report.errors.Add(error);
            report.elapsedSeconds = EditorApplication.timeSinceStartup - started;
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("LOBBY_EXPANSION_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
