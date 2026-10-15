#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AcRoguelike;
using AcRoguelike.Liminal;
using NamgungLuna.Motion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NamgungLuna.Editor
{
    /// <summary>Runs the real roster and player motor, then restores the saved loadout and edit scene.</summary>
    [InitializeOnLoad]
    public static class LunaStageValidation
    {
        const string Key = "Luna.Stage.Validation";
        const string Output = "Documentation/NamgungLuna/Validation";
        [Serializable] class Report
        {
            public string status = "running", utc;
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
            public string error;
        }
        static Report report;
        static IEnumerator routine;
        static float waitUntil;
        static double deadline;
        static readonly Vector3 Arena = new Vector3(1000, 0, 1000);
        public static bool Running => SessionState.GetBool(Key, false);

        static LunaStageValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!Running) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    report = new Report { utc = DateTime.UtcNow.ToString("O") };
                    routine = Run();
                    deadline = EditorApplication.timeSinceStartup + 150;
                    waitUntil = 0;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    HunterLoadout.HunterId = SessionState.GetString(Key + ".hunter", "");
                    routine = null;
                    SessionState.SetBool(Key, false);
                    string scene = SessionState.GetString(Key + ".scene", "");
                    if (!string.IsNullOrEmpty(scene) && ScenePath() != scene) EditorSceneManager.OpenScene(scene);
                }
            };
        }

        static string ScenePath() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        [MenuItem("Tools/Namgung Luna/Stage Capture/Validate Both Forms")]
        public static void Start()
        {
            if (EditorApplication.isPlaying || Running) throw new InvalidOperationException("Stop Play mode before validation.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the active scene before validation.");
            SessionState.SetString(Key + ".scene", ScenePath());
            SessionState.SetString(Key + ".hunter", HunterLoadout.HunterId);
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity");
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Movement validation timed out.");
                if (Time.time < waitUntil) return;
                if (!routine.MoveNext()) { Finish(null); return; }
                waitUntil = Time.time + (routine.Current is float seconds ? seconds : 0f);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        static void Check(bool pass, string description)
        {
            if (!pass) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }

        static void SetPhase(LiminalRunDirector director, LiminalRunPhase phase)
            => typeof(LiminalRunDirector).GetMethod("SetPhase", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(director, new object[] { phase });

        static IEnumerator Run()
        {
            yield return 1f;
            var director = Object.FindFirstObjectByType<LiminalRunDirector>();
            Check(director && director.Roster, "LiminalRun booted with its existing player and roster.");
            var roster = director.Roster;
            var motor = roster.GetComponent<PlayerMotor>();
            var combat = roster.GetComponent<PlayerCombat>();
            var caster = roster.GetComponent<TalismanCaster>();
            var melee = roster.GetComponent<MeleeSlash>();
            var support = Object.FindFirstObjectByType<SupportCharacterSkill>();
            roster.Choose(0);
            float originalMove = motor.moveSpeed, originalWalk = motor.walkSpeed;
            director.EnterDungeon();
            yield return 1f;
            Check(director.Phase == LiminalRunPhase.Exploring, "Existing gate route starts.");
            director.enabled = false;
            roster.GetComponent<LiminalPlayerHealth>().GrantInvulnerability(999);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "LunaValidationFloor";
            floor.transform.position = Arena + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(40, .5f, 40);
            motor.ResetAt(Arena + Vector3.up * .04f);
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            Physics.SyncTransforms();
            yield return .5f;

            foreach (string id in new[] { "namgung_luna_peace", "namgung_luna_battle" })
            {
                int index = Enumerable.Range(0, roster.Count).Where(i => roster.IdAt(i) == id).DefaultIfEmpty(-1).First();
                Check(index >= 0, id + ": profile is registered.");
                Check(roster.Select(index), id + ": selected through the gameplay roster.");
                yield return .5f;
                // The battle form keeps the shared dash and plays its floating leap; the peaceful form only walks.
                bool keepsDash = id == "namgung_luna_battle";
                Check(roster.CurrentMovementOnly && motor.allowDash == keepsDash && !combat.enabled && !caster.enabled && !melee.enabled,
                    id + (keepsDash ? ": attacks, talismans and melee disabled; dash kept." : ": attacks, talismans, melee and dash disabled."));
                Check(!support || !support.TryActivate(), id + ": support skill blocked.");
                Check(roster.CurrentVisual.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)),
                    id + ": all renderers have supported materials.");
                Check(roster.CurrentVisual.GetComponentsInChildren<Transform>().All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0),
                    id + ": no missing scripts.");
                var transforms = roster.CurrentVisual.GetComponentsInChildren<Transform>();
                var rests = transforms.Select(t => t.localRotation).ToArray();
                Vector3 start = motor.transform.position;
                int dashCount = motor.DashCount, attacks = combat.AttackCount, casts = caster.CastCount;
                combat.SetAutomationInput(true, true);
                motor.SetAutomationInput(Vector2.up, Arena + Vector3.forward * 10, true);
                yield return 1.2f;
                Check(Vector3.ProjectOnPlane(motor.transform.position - start, Vector3.up).magnitude > motor.moveSpeed * .9f,
                    id + ": movement changes world position.");
                Check(transforms.Where((t, i) => Quaternion.Angle(t.localRotation, rests[i]) > 2f).Count() >= 2,
                    id + ": multiple joints animate during movement.");
                Check((keepsDash ? motor.DashCount > dashCount : motor.DashCount == dashCount) && combat.AttackCount == attacks && caster.CastCount == casts,
                    id + (keepsDash ? ": dash input dashes; attack input produces no attack or cast." : ": simulated attack/dash input produces no attack, cast or dash."));
                Capture(roster.CurrentVisual, id + "-moving");
                motor.SetAutomationInput(Vector2.up, motor.transform.position + Vector3.forward * 10, false, true);
                yield return .8f;
                Check(Mathf.Abs(motor.PlanarVelocity.magnitude - motor.walkSpeed) < .05f && motor.walkSpeed < motor.moveSpeed,
                    id + ": slow-walk input uses the lower movement speed.");
                var animator = roster.CurrentVisual.GetComponent<Animator>();
                if (animator && animator.runtimeAnimatorController)
                    Check(Mathf.Abs(animator.GetFloat("Speed") - motor.walkSpeed / motor.moveSpeed) < .05f,
                        id + ": slow-walk animation follows the normalized movement speed.");
                motor.SetAutomationInput(Vector2.zero, motor.transform.position);
                combat.SetAutomationInput(false, false);
                yield return .6f;
                Check(motor.PlanarVelocity.magnitude < .03f, id + ": released input stops movement.");
                if (keepsDash)
                {
                    // A dash from a standstill: the root slides on, the body leaps over it and lands with a ripple.
                    var gait = roster.CurrentVisual.GetComponentInChildren<LunaMoonGait>();
                    Check(gait, id + ": moon gait drives the body.");
                    var events = new List<LunaGaitEventKind>();
                    System.Action<LunaGaitEvent> listen = e => events.Add(e.kind);
                    gait.GaitEvent += listen;
                    yield return .5f;   // dash interval and stamina
                    Vector3 before = motor.transform.position;
                    int dashes = motor.DashCount;
                    motor.SetAutomationInput(Vector2.right, motor.transform.position + Vector3.right * 5, true);
                    float lift = 0f, phase = -1f;
                    for (float t = 0f; t < .7f; t += Time.deltaTime)
                    {
                        yield return 0f;
                        lift = Mathf.Max(lift, gait.DashLiftNow);
                        phase = Mathf.Max(phase, motor.DashPhase);
                        motor.SetAutomationInput(Vector2.zero, motor.transform.position);
                    }
                    gait.GaitEvent -= listen;
                    float travel = Vector3.ProjectOnPlane(motor.transform.position - before, Vector3.up).magnitude;
                    report.checks.Add($"{id}: dash travel {travel:F2} m, body lift {lift:F2} m, events {string.Join("/", events)}");
                    Check(motor.DashCount == dashes + 1 && phase > .5f && travel > motor.dashSpeed * motor.dashDuration * .8f,
                        id + ": the shared dash moves her.");
                    Check(lift > .2f, id + ": the body floats up in a leap during the dash.");
                    Check(events.Contains(LunaGaitEventKind.Dash) && events.Contains(LunaGaitEventKind.Land),
                        id + ": the leap bursts at the start and lands with a ripple.");
                    Capture(roster.CurrentVisual, id + "-dash-landed");
                }
                Capture(roster.CurrentVisual, id + "-idle");
                SetPhase(director, LiminalRunPhase.Paused);
                SetPhase(director, LiminalRunPhase.Exploring);
                Check(!combat.enabled && !caster.enabled && motor.allowDash == keepsDash, id + ": pause/resume preserves restrictions.");
                float expected = Resources.LoadAll<HunterProfile>(HunterRoster.ProfileFolder).First(p => p.characterId == id).moveSpeedOverride;
                Check(Mathf.Abs(motor.moveSpeed - expected) < .01f, id + ": configured movement speed is used.");
            }

            roster.Select(0);
            yield return .3f;
            Check(combat.enabled && caster.enabled && melee.enabled && motor.allowDash && !roster.CurrentMovementOnly,
                "Astraia restores combat, talismans, melee and dash.");
            Check(Mathf.Approximately(motor.moveSpeed, originalMove) && Mathf.Approximately(motor.walkSpeed, originalWalk),
                "Original movement speeds restored.");
            int beforeDash = motor.DashCount;
            motor.SetAutomationInput(Vector2.right, Arena + Vector3.right * 5, true);
            yield return .3f;
            Check(motor.DashCount > beforeDash, "Existing hunter dash still executes.");
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            int proi = Enumerable.Range(0, roster.Count).Where(i => roster.IdAt(i) == "proi").DefaultIfEmpty(-1).First();
            Check(proi >= 0 && roster.Select(proi), "Existing Proi remains selectable.");
            yield return .3f;
            Check(combat.enabled && motor.allowDash && combat.ComboLength == 3, "Proi retains its original three-hit combo and dash.");
            int peace = Enumerable.Range(0, roster.Count).First(i => roster.IdAt(i) == "namgung_luna_peace");
            roster.Select(peace);
            director.enabled = true;
            director.EnterLobby();
            yield return .6f;
            Check(roster.CurrentId == "namgung_luna_peace" && roster.CurrentMovementOnly, "Peaceful form persists through lobby return.");
            director.EnterDungeon();
            yield return .6f;
            Check(roster.CurrentId == "namgung_luna_peace" && !combat.enabled && !motor.allowDash,
                "Peaceful form and movement-only limits persist in the next gate.");
            motor.ReleaseAutomation();
        }

        static void Capture(GameObject visual, string name)
        {
            Directory.CreateDirectory(Output);
            var go = new GameObject("LunaValidationCamera");
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .11f, .18f);
            camera.fieldOfView = 32;
            Vector3 target = visual.transform.position + Vector3.up * .85f;
            camera.transform.position = target + visual.transform.rotation * new Vector3(2.2f, .7f, 3.5f);
            camera.transform.LookAt(target);
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 60;
            var occlusion = visual.GetComponent<LunaOcclusionFade>();
            bool fadeEnabled = occlusion && occlusion.enabled;
            // This front inspection camera is different from the game's camera behind the body.
            if (fadeEnabled) occlusion.enabled = false;
            var rt = new RenderTexture(720, 900, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(720, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 720, 900), 0, 0);
            image.Apply();
            string path = Output + "/" + name + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            report.captures.Add(path);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            if (fadeEnabled) occlusion.enabled = true;
        }

        static void Finish(string error)
        {
            routine = null;
            report.status = error == null ? "passed" : "failed";
            report.error = error;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/movement-validation.json", JsonUtility.ToJson(report, true));
            HunterLoadout.HunterId = SessionState.GetString(Key + ".hunter", "");
            Time.timeScale = 1;
            if (error != null) Debug.LogError(error);
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
