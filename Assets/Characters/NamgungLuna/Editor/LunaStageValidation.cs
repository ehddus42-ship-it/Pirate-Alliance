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
        const string HunterKey = "Hunter.Loadout.Hunter";
        const string LunaId = "namgung_luna";
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
                    deadline = EditorApplication.timeSinceStartup + 210;
                    waitUntil = 0;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    RestoreSelection();
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
            SessionState.SetBool(Key + ".hadHunter", PlayerPrefs.HasKey(HunterKey));
            SessionState.SetString(Key + ".hunter", PlayerPrefs.GetString(HunterKey, ""));
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity");
            // Exercise the real startup migration from the previous two-entry roster.
            PlayerPrefs.SetString(HunterKey, "namgung_luna_peace");
            PlayerPrefs.Save();
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
            Check(roster.Count == 3, "The roster has exactly three hunters: Astraia, Proi and Namgung Luna.");
            int luna = Enumerable.Range(0, roster.Count).Single(i => roster.IdAt(i) == LunaId);
            Check(!Enumerable.Range(0, roster.Count).Any(i => roster.IdAt(i) == "namgung_luna_peace" || roster.IdAt(i) == "namgung_luna_battle"),
                "The previous peaceful and battle entries are absent from the selectable roster.");
            Check(director.Phase == LiminalRunPhase.Lobby && roster.SelectedIndex == luna && HunterLoadout.HunterId == LunaId
                && PlayerPrefs.GetString(HunterKey) == LunaId, "A saved peaceful-form ID migrates to the single Luna selection on startup.");
            CheckForm(roster, luna, true, "Startup lobby");
            var mainProfile = Resources.LoadAll<HunterProfile>(HunterRoster.ProfileFolder).Single(p => p.characterId == LunaId);
            Check(mainProfile.lobbyForm, "Luna's main profile references her dedicated lobby form.");

            // Use the actual officer interaction and card callbacks, so the UI also proves the single selection.
            var lobby = director.Lobby;
            Check(lobby && lobby.LoadoutOfficer, "The lobby loadout officer is available.");
            Vector3 officer = lobby.LoadoutOfficer.transform.position;
            motor.ResetAt(officer + (director.player.position - officer).normalized * 1.6f);
            motor.SetAutomationInput(Vector2.zero, officer);
            var followCamera = Object.FindFirstObjectByType<IsometricFollowCamera>();
            if (followCamera) followCamera.Snap();
            yield return .4f;
            Check(lobby.Nearest != null && lobby.Nearest.label == "헌터 편성", "Approaching the officer offers hunter selection.");
            lobby.Nearest.use();
            yield return .3f;
            Check(lobby.LoadoutOpen && lobby.WindowOpen && !motor.enabled, "The real loadout window opens.");
            var window = director.GetComponent<LiminalHud>().Canvas.Find("LobbyWindow/Window");
            Check(window, "The loadout window is attached to the HUD.");
            var cards = window.GetComponentsInChildren<UnityEngine.UI.Button>()
                .Where(b => b.name.StartsWith("Hunter_", StringComparison.Ordinal)).ToArray();
            Check(cards.Length == 3 && cards.Count(b => b.name == "Hunter_" + LunaId) == 1,
                "The loadout window shows exactly three hunter cards and one Luna card.");
            var lunaCard = cards.Single(b => b.name == "Hunter_" + LunaId);
            Check(lunaCard.transform.Find("Name").GetComponent<TMPro.TMP_Text>().text == "남궁루나",
                "The single Luna card is named 남궁루나.");
            cards.Single(b => b.name == "Hunter_" + roster.IdAt(0)).onClick.Invoke();
            yield return .8f; // Let the selection effects finish before the next selection/phase change.
            Check(roster.SelectedIndex == 0 && roster.CurrentIndex == 0, "The officer can select the existing Astraia.");
            lunaCard.onClick.Invoke();
            yield return .8f;
            CheckForm(roster, luna, true, "Luna card selection");
            CaptureLoadout();
            window.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "Close").onClick.Invoke();
            yield return .4f;
            Check(!lobby.WindowOpen && motor.enabled, "Closing the loadout window restores movement.");

            director.enabled = false;
            roster.GetComponent<LiminalPlayerHealth>().GrantInvulnerability(999);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "LunaValidationFloor";
            floor.transform.position = Arena + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(40, .5f, 40);
            ResetAtArena(motor);
            yield return .5f;
            var peacefulVisual = roster.CurrentVisual;
            var movement = ValidateMovement(roster, mainProfile.lobbyForm, false);
            while (movement.MoveNext()) yield return movement.Current;

            int swapsBeforeEntry = roster.SwapCount;
            director.EnterDungeon();
            yield return 1f;
            Check(director.Phase == LiminalRunPhase.Exploring, "Entering the dungeon starts the existing gate route.");
            CheckForm(roster, luna, false, "First dungeon entry");
            Check(roster.SwapCount == swapsBeforeEntry, "Changing form at dungeon entry does not count as selecting another hunter.");
            var battleVisual = roster.CurrentVisual;
            Check(battleVisual != peacefulVisual && !peacefulVisual.activeInHierarchy, "Entering the dungeon replaces and hides the peaceful body.");
            float originalMove = roster.SharedMoveSpeed, originalWalk = roster.SharedWalkSpeed;
            ResetAtArena(motor);
            yield return .5f;
            movement = ValidateMovement(roster, mainProfile, true);
            while (movement.MoveNext()) yield return movement.Current;

            roster.Select(0);
            yield return .8f;
            Check(combat.enabled && caster.enabled && melee.enabled && motor.allowDash && !roster.CurrentMovementOnly,
                "Astraia restores combat, talismans, melee and dash.");
            Check(Mathf.Approximately(motor.moveSpeed, originalMove) && Mathf.Approximately(motor.walkSpeed, originalWalk),
                "Original movement speeds restored.");
            int beforeDash = motor.DashCount;
            motor.SetAutomationInput(Vector2.right, Arena + Vector3.right * 5, true);
            yield return .3f;
            Check(motor.DashCount > beforeDash, "Existing hunter dash still executes.");
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            yield return .5f;
            int proi = Enumerable.Range(0, roster.Count).Single(i => roster.IdAt(i) == "proi");
            Check(roster.Select(proi), "Existing Proi remains selectable.");
            yield return .8f;
            Check(combat.enabled && motor.allowDash && combat.ComboLength == 3, "Proi retains its original three-hit combo and dash.");
            Check(roster.Select(luna), "Luna remains selectable as one hunter inside the dungeon.");
            yield return .8f;
            CheckForm(roster, luna, false, "Reselecting Luna in the dungeon");

            SetPhase(director, LiminalRunPhase.Paused);
            CheckForm(roster, luna, false, "Paused dungeon");
            Check(roster.CurrentVisual == battleVisual, "Pause keeps the existing battle body.");
            director.Resume();
            Check(director.Phase == LiminalRunPhase.Exploring, "Resume returns to exploration.");
            CheckForm(roster, luna, false, "Resumed dungeon");
            Check(director.OfferAugments() && director.Phase == LiminalRunPhase.AugmentChoice, "The actual augment choice opens.");
            CheckForm(roster, luna, false, "Augment choice");
            Check(roster.CurrentVisual == battleVisual, "Augment choice keeps the battle body.");
            // Avoid a reward augment: this validation should not grant currency to the saved account.
            int augment = Enumerable.Range(0, director.AugmentOffers.Count).First(i => director.AugmentOffers[i].id != AugmentCatalog.Deposit);
            director.SelectAugment(augment);
            Check(director.Phase == LiminalRunPhase.Exploring, "Picking an augment resumes exploration.");
            CheckForm(roster, luna, false, "After augment selection");

            int nextStage = director.StageIndex + 1;
            if (director.stages.Length > nextStage)
            {
                SetPhase(director, LiminalRunPhase.NextStageChoice);
                CheckForm(roster, luna, false, "Next-stage choice");
                Check(roster.CurrentVisual == battleVisual, "The next-stage choice keeps the battle body.");
                director.ContinueToNextStage();
                yield return .6f;
                Check(director.Phase == LiminalRunPhase.Exploring && director.StageIndex == nextStage, "Continue loads the actual next stage.");
                CheckForm(roster, luna, false, "Next stage");
                Check(roster.CurrentVisual == battleVisual, "The next stage retains the existing battle body.");
            }
            SetPhase(director, LiminalRunPhase.Victory);
            CheckForm(roster, luna, false, "Victory");
            SetPhase(director, LiminalRunPhase.Defeat);
            CheckForm(roster, luna, false, "Defeat");

            int swapsBeforeReturn = roster.SwapCount;
            director.EnterLobby();
            yield return .6f;
            Check(director.Phase == LiminalRunPhase.Lobby, "Returning to the lobby changes the environment.");
            CheckForm(roster, luna, true, "Lobby return");
            Check(roster.SwapCount == swapsBeforeReturn, "Returning to the peaceful form preserves the hunter swap count.");
            Check(roster.CurrentVisual == peacefulVisual && !battleVisual.activeInHierarchy, "Lobby return restores the same peaceful body and hides the battle body.");
            director.EnterDungeon();
            yield return .6f;
            Check(director.Phase == LiminalRunPhase.Exploring, "The selected Luna enters another dungeon.");
            CheckForm(roster, luna, false, "Second dungeon entry");
            Check(roster.CurrentVisual == battleVisual && !peacefulVisual.activeInHierarchy, "The second dungeon entry restores the same battle body.");
            director.EnterLobby();
            yield return .4f;
            director.StartNewRun(173019);
            yield return .6f;
            CheckForm(roster, luna, false, "Direct run start from lobby");
            Check(!director.Lobby.gameObject.activeSelf, "A direct run start also hides the lobby environment.");
            motor.ReleaseAutomation();
            director.enabled = true;
        }

        static void ResetAtArena(PlayerMotor motor)
        {
            motor.ResetAt(Arena + Vector3.up * .04f);
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            Physics.SyncTransforms();
        }

        static void CheckForm(HunterRoster roster, int index, bool peaceful, string context)
        {
            Check(roster.CurrentId == LunaId && roster.CurrentName == "남궁루나" && roster.IdAt(index) == LunaId
                && roster.NameAt(index) == "남궁루나" && roster.CurrentIndex == index && roster.SelectedIndex == index
                && HunterLoadout.HunterId == LunaId, context + ": identity, name, selection and saved ID stay canonical.");
            Check(roster.InLobby == peaceful && roster.CurrentVisual && roster.CurrentVisual.activeInHierarchy,
                context + ": the selected form is active in the correct environment.");
            var animator = roster.CurrentVisual.GetComponent<Animator>();
            var gait = roster.CurrentVisual.GetComponentInChildren<LunaMoonGait>();
            Check(peaceful ? animator && animator.runtimeAnimatorController && animator.runtimeAnimatorController.name == "LunaPeaceful" && !gait : gait != null,
                context + (peaceful ? ": the peaceful walk controller is worn." : ": the battle moon gait is worn."));
            Check(roster.CurrentMovementOnly && !roster.GetComponent<PlayerCombat>().enabled && !roster.GetComponent<TalismanCaster>().enabled
                && !roster.GetComponent<MeleeSlash>().enabled && roster.GetComponent<PlayerMotor>().allowDash == !peaceful,
                context + ": movement-only restrictions and form-specific dash settings are preserved.");
        }

        static IEnumerator ValidateMovement(HunterRoster roster, HunterProfile profile, bool keepsDash)
        {
            string id = keepsDash ? "namgung_luna_battle" : "namgung_luna_peace";
            var motor = roster.GetComponent<PlayerMotor>();
            var combat = roster.GetComponent<PlayerCombat>();
            var caster = roster.GetComponent<TalismanCaster>();
            var melee = roster.GetComponent<MeleeSlash>();
            var support = Object.FindFirstObjectByType<SupportCharacterSkill>();
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
                Quaternion lanternRest = (Quaternion)typeof(LunaMoonGait).GetField("lanternRest", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gait);
                float lanternPeak = 0f;
                bool lanternFinite = true;
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
                    float lanternAngle = Quaternion.Angle(lanternRest, gait.lantern.localRotation);
                    lanternFinite &= !float.IsNaN(lanternAngle) && !float.IsInfinity(lanternAngle);
                    lanternPeak = Mathf.Max(lanternPeak, lanternAngle);
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
                Check(lanternFinite && lanternPeak <= 14.1f,
                    $"{id}: lantern stays stable through the real dash and landing (peak {lanternPeak:F2} degrees).");
                Capture(roster.CurrentVisual, id + "-dash-landed");
            }
            Capture(roster.CurrentVisual, id + "-idle");
            Check(Mathf.Abs(motor.moveSpeed - profile.moveSpeedOverride) < .01f
                && Mathf.Abs(motor.walkSpeed - profile.walkSpeedOverride) < .01f, id + ": configured form movement speeds are used.");
        }

        static void CaptureLoadout()
        {
            var camera = Camera.main;
            Check(camera, "The lobby has a main camera for the loadout capture.");
            var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray();
            var distances = overlays.Select(c => c.planeDistance).ToArray();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                foreach (var canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + .05f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Output);
                string path = Output + "/loadout.png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                report.captures.Add(path);
            }
            finally
            {
                for (int i = 0; i < overlays.Length; i++)
                {
                    overlays[i].renderMode = RenderMode.ScreenSpaceOverlay;
                    overlays[i].worldCamera = cameras[i];
                    overlays[i].planeDistance = distances[i];
                }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(rt);
                Canvas.ForceUpdateCanvases();
            }
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
            RestoreSelection();
            Time.timeScale = 1;
            if (error != null) Debug.LogError(error);
            EditorApplication.ExitPlaymode();
        }

        static void RestoreSelection()
        {
            if (SessionState.GetBool(Key + ".hadHunter", false))
                PlayerPrefs.SetString(HunterKey, SessionState.GetString(Key + ".hunter", ""));
            else PlayerPrefs.DeleteKey(HunterKey);
            PlayerPrefs.Save();
        }
    }
}
#endif
