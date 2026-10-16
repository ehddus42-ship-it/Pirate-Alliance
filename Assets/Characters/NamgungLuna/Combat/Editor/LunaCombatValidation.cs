#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NamgungLuna.Editor
{
    /// <summary>Exercises Luna on the real LiminalRun roster; all fixtures exist only during Play mode.</summary>
    [InitializeOnLoad]
    public static class LunaCombatValidation
    {
        const string Key = "Luna.Combat.Validation";
        const string HunterKey = "Hunter.Loadout.Hunter";
        const string LunaId = "namgung_luna";
        public const string Output = "Documentation/NamgungLuna/Combat/Validation";
        static readonly Vector3 Arena = new Vector3(1000, 0, 1000);

        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class SavedPreference
        {
            public string key, text;
            public bool exists, isString;
            public int number;
        }
        [Serializable] sealed class SavedState
        {
            public SavedScene[] scenes;
            public SavedPreference[] preferences;
            public string[] selection;
            public float timeScale, fixedDeltaTime, captureDeltaTime;
            public bool paused, captureFrames, eyePreview;
        }
        [Serializable] sealed class CaptureInfo { public string path; public float gameTime; }
        [Serializable] sealed class Report
        {
            public string status = "running", utc, error;
            public string method = "Real LiminalRun player/roster, isolated live TrainingEnemy fixtures, no time acceleration or tuning overrides.";
            public List<string> checks = new List<string>();
            public List<CaptureInfo> captures = new List<CaptureInfo>();
            public float normalAttackCooldown, awakenedAttackCooldown, moonlightSeconds, ultimateSeconds;
            public int directHits, splashHits, missedSplashHits;
            public int eyeFrames, eyeFramesPerSecond;
        }

        static SavedState saved;
        static Report report;
        static IEnumerator routine;
        static GameObject fixtures;
        static readonly List<Material> materials = new List<Material>();
        static PlayerMotor motor;
        static LunaCombatController combat;
        static bool finishing;
        static int lastFrame = -1;
        static int pendingPreviewFrame = -1;
        static float waitUntil;
        static double deadline;
        public static bool Running => SessionState.GetBool(Key, false);

        static LunaCombatValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.delayCall += () =>
            {
                if (Running && SessionState.GetBool(Key + ".entered", false) && routine == null && EditorApplication.isPlaying)
                    Finish("Combat validation was interrupted by an assembly reload.");
            };
        }

        [MenuItem("Tools/Namgung Luna/Combat/Validate Combat")]
        public static void Start() => Start(false);

        [MenuItem("Tools/Namgung Luna/Combat/Validate Combat With Short Frame Capture")]
        public static void StartWithFrames() => Start(true);

        [MenuItem("Tools/Namgung Luna/Combat/Capture Golden Eye Preview")]
        public static void StartEyePreview() => Start(false, true);

        public static void Start(bool captureFrames) => Start(captureFrames, false);

        static void Start(bool captureFrames, bool eyePreview)
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before combat validation.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab mode before combat validation.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (scene.isLoaded && string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("Save all open scenes before validation. The tool never saves edit scenes.");
            }
            saved = new SavedState
            {
                scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray(),
                selection = Selection.objects.Select(o => GlobalObjectId.GetGlobalObjectIdSlow(o).ToString()).ToArray(),
                preferences = SnapshotPreferences(), paused = EditorApplication.isPaused, captureFrames = captureFrames, eyePreview = eyePreview,
                timeScale = Time.timeScale, fixedDeltaTime = Time.fixedDeltaTime, captureDeltaTime = Time.captureDeltaTime
            };
            SessionState.SetString(Key + ".saved", JsonUtility.ToJson(saved));
            SessionState.SetBool(Key + ".entered", false);
            SessionState.SetBool(Key, true);
            try
            {
                PlayerPrefs.SetString(HunterKey, LunaId);
                PlayerPrefs.Save();
                EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity");
                EditorApplication.EnterPlaymode();
            }
            catch { Restore(); throw; }
        }

        static SavedPreference[] SnapshotPreferences()
        {
            var strings = new[] { HunterKey, "Hunter.Loadout.Support" };
            var integers = new HashSet<string> { "Hunter.MagicStones" };
            foreach (HunterProgress.Upgrade upgrade in Enum.GetValues(typeof(HunterProgress.Upgrade)))
                integers.Add("Hunter.Upgrade." + upgrade);
            foreach (string guid in AssetDatabase.FindAssets("t:LiminalStageDefinition"))
            {
                var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (stage && !string.IsNullOrEmpty(stage.stageId)) integers.Add(HunterProgress.StageDiscoveryKey(stage.stageId));
            }
            foreach (string guid in AssetDatabase.FindAssets("t:GateMissionCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog) foreach (var mission in catalog.Missions)
                    if (mission != null && !string.IsNullOrEmpty(mission.destinationId))
                        integers.Add(HunterProgress.DestinationDiscoveryKey(mission.destinationId));
            }
            return strings.Select(key => new SavedPreference { key = key, exists = PlayerPrefs.HasKey(key), isString = true, text = PlayerPrefs.GetString(key, "") })
                .Concat(integers.Select(key => new SavedPreference { key = key, exists = PlayerPrefs.HasKey(key), number = PlayerPrefs.GetInt(key, 0) })).ToArray();
        }

        static void PlayStateChanged(PlayModeStateChange state)
        {
            if (!Running) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                saved = JsonUtility.FromJson<SavedState>(SessionState.GetString(Key + ".saved", ""));
                report = new Report { utc = DateTime.UtcNow.ToString("O") };
                finishing = false;
                SessionState.SetBool(Key + ".entered", true);
                EditorApplication.isPaused = saved.eyePreview;
                if (saved.eyePreview)
                {
                    Time.captureDeltaTime = 1f / 24;
                    report.eyeFramesPerSecond = 24;
                    report.method = "Real roster eye preview; 24 FPS captureDeltaTime and one paused Editor Step per frame, restored afterwards. Functional combat checks remain in combat-validation.json.";
                }
                deadline = EditorApplication.timeSinceStartup + 240;
                waitUntil = 0;
                lastFrame = -1;
                pendingPreviewFrame = -1;
                routine = saved.eyePreview ? RunEyePreview() : Run();
                WriteReport();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !finishing)
            {
                if (report == null) report = new Report();
                report.status = "failed";
                report.error = "Play mode was stopped before combat validation completed.";
                WriteReport();
                Cleanup();
            }
            else if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying || finishing) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Combat validation exceeded its wall-clock timeout.");
                if (saved.eyePreview)
                {
                    TickEyePreview();
                    return;
                }
                if (Time.frameCount == lastFrame || Time.time < waitUntil) return;
                lastFrame = Time.frameCount;
                if (!routine.MoveNext()) { Finish(null); return; }
                waitUntil = Time.time + (routine.Current is float seconds ? seconds : 0f);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        static void TickEyePreview()
        {
            if (pendingPreviewFrame >= 0)
            {
                if (Time.frameCount <= pendingPreviewFrame) return;
                if (Time.frameCount != pendingPreviewFrame + 1)
                    throw new InvalidOperationException("Eye preview must advance one frame at a time; another editor operation resumed Play mode.");
                pendingPreviewFrame = -1;
            }
            if (Time.time >= waitUntil)
            {
                if (!routine.MoveNext()) { Finish(null); return; }
                waitUntil = Time.time + (routine.Current is float seconds ? seconds : 0f);
            }
            pendingPreviewFrame = Time.frameCount;
            EditorApplication.Step();
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
            WriteReport();
        }

        static void CheckEyesOff(LunaEyeFire eye, string context)
            => Check(eye && !eye.Active && eye.LiveParticleCount == 0 && eye.VisibleRendererCount == 0,
                context + ": no active eye fire, live trail particles or visible effect renderers.");

        static IEnumerator RunEyePreview()
        {
            yield return 1f;
            var director = Object.FindFirstObjectByType<LiminalRunDirector>();
            Check(director && director.Roster, "The eye preview uses the actual LiminalRun roster.");
            var roster = director.Roster;
            int luna = Enumerable.Range(0, roster.Count).Single(i => roster.IdAt(i) == LunaId);
            roster.Choose(luna);
            director.EnterDungeon();
            yield return 1f;
            director.enabled = false;
            motor = roster.GetComponent<PlayerMotor>();
            combat = roster.CurrentVisual.GetComponent<LunaCombatController>();
            var fx = roster.CurrentVisual.GetComponent<LunaCombatVfx>();
            Check(combat && fx && fx.EyeFire && combat.CanAct, "The battle controller and eye-fire component are available.");
            roster.GetComponent<LiminalPlayerHealth>().GrantInvulnerability(240);
            fixtures = new GameObject("LunaEyePreviewFixtures");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(fixtures.transform);
            floor.transform.position = Arena + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(40, .5f, 40);
            floor.GetComponent<Renderer>().sharedMaterial = Material(new Color(.14f, .17f, .23f));
            ResetPlayer();
            yield return .6f;

            // Build the lazy anchors without leaving any fire in the baseline image.
            var eye = fx.EyeFire;
            eye.PrepareAnchors();
            CheckEyesOff(eye, "Preparing the baseline eye anchors without an ultimate");
            var closeup = CaptureEyes(fx, "eye-baseline-front-no-fire", false);
            while (closeup.MoveNext()) yield return closeup.Current;
            Check(combat.TryUltimate(), "The eye preview starts the real ten-second ultimate.");
            yield return 2.9f; // The transient casting burst lasts 2.8 s; inspect persistent iris fire alone.
            Check(combat.IsAwakened && eye.Active && eye.LiveParticleCount > 0 && eye.VisibleRendererCount > 0 && fx.ActiveBursts == 0,
                "Persistent eye fire remains after the initial casting burst has finished.");
            closeup = CaptureEyes(fx, "eye-fire-front-after-cast");
            while (closeup.MoveNext()) yield return closeup.Current;
            CaptureGame("eye-preview-game-after-cast");

            var camera = InspectionCamera("Luna Moving Eye Preview Camera", Vector3.zero, Vector3.forward, 32);
            camera.farClipPlane = 2.5f;
            var previousCamera = eye.CaptureCamera;
            try
            {
                eye.CaptureCamera = camera;
                PositionEyeCamera(camera, eye, 25);
                yield return 0f;
                for (int frame = 0; frame < 48; frame++)
                {
                    // A half-second still introduction, then normal locomotion through a front-to-side view.
                    motor.SetAutomationInput(frame < 12 ? Vector2.zero : Vector2.up,
                        motor.transform.position + Vector3.forward * 10);
                    PositionEyeCamera(camera, eye, Mathf.Lerp(25, 65, frame / 47f));
                    yield return 0f;
                    PositionEyeCamera(camera, eye, Mathf.Lerp(25, 65, frame / 47f));
                    eye.SetActiveFire(true);
                    Capture(camera, "Frames/Eye/eye-" + frame.ToString("D3"), 960, 720);
                    report.eyeFrames++;
                    if (frame % 4 == 0) CaptureGame("Frames/EyeGame/game-" + (frame / 4).ToString("D3"));
                    if (frame == 0) Capture(camera, "eye-fire-threequarter-still", 960, 720);
                    if (frame == 47) Capture(camera, "eye-fire-threequarter-moving", 960, 720);
                }
                motor.SetAutomationInput(Vector2.zero, motor.transform.position + Vector3.forward * 5);
                Check(report.eyeFrames == 48 && combat.IsAwakened, "Captured two seconds of eye fire at 24 FPS before the ultimate expires.");
            }
            finally
            {
                eye.CaptureCamera = previousCamera;
                Object.DestroyImmediate(camera.gameObject);
            }
            var gallery = CaptureGallery(fx);
            while (gallery.MoveNext()) yield return gallery.Current;
            combat.CancelActions();
            CheckEyesOff(eye, "Cancelling the preview ultimate");
        }

        static void PositionEyeCamera(Camera camera, LunaEyeFire eye, float yaw)
        {
            Vector3 center = (eye.LeftEyeAnchor.position + eye.RightEyeAnchor.position) * .5f;
            var visual = eye.Visual ? eye.Visual : eye.transform;
            Vector3 outward = Quaternion.AngleAxis(yaw, Vector3.up) * visual.forward;
            camera.transform.position = center + outward * .8f + Vector3.up * .06f;
            camera.transform.LookAt(center + Vector3.up * .08f);
        }

        static IEnumerator Run()
        {
            yield return 1f;
            var director = Object.FindFirstObjectByType<LiminalRunDirector>();
            Check(director && director.Roster, "The actual LiminalRun player and roster are available.");
            var roster = director.Roster;
            motor = roster.GetComponent<PlayerMotor>();
            int luna = Enumerable.Range(0, roster.Count).Single(i => roster.IdAt(i) == LunaId);
            roster.Choose(luna);
            yield return .3f;
            Check(roster.InLobby && roster.WearsLobbyForm, "Luna starts in her peaceful lobby form.");
            Check(!roster.GetComponentsInChildren<LunaCombatController>().Any(c => c.CanAct), "The peaceful lobby form cannot attack.");
            // The G preview may show the battle body in the lobby; its combat gate must still refuse actions.
            Check(roster.TryChangeOutfit(), "The real outfit preview can expose the battle form in the lobby.");
            yield return .3f;
            var lobbyCombat = roster.CurrentVisual.GetComponent<LunaCombatController>();
            Check(lobbyCombat && !lobbyCombat.TryAttack() && !lobbyCombat.TryMoonlight() && !lobbyCombat.TryUltimate(),
                "Battle appearance in the lobby still refuses attacks, moonlight and ultimate.");

            director.EnterDungeon();
            yield return 1f;
            Check(director.Phase == LiminalRunPhase.Exploring && !roster.WearsLobbyForm, "Dungeon entry selects the canonical battle form.");
            combat = roster.CurrentVisual.GetComponent<LunaCombatController>();
            var fx = roster.CurrentVisual.GetComponent<LunaCombatVfx>();
            Check(combat && fx && fx.EyeFire, "Battle combat, visual effects and eye fire are installed on the real body.");
            CheckEyesOff(fx.EyeFire, "Initial battle appearance before any ultimate");
            Check(AssetDatabase.LoadMainAssetAtPath("Assets/Characters/NamgungLuna/Combat/LunaCombatFxLibrary.asset"), "The authored combat effect library exists.");
            Check(Mathf.Approximately(combat.moonlightDuration, 8) && Mathf.Approximately(combat.ultimateDuration, 10)
                && Mathf.Approximately(combat.awakenedAttackSpeed, 2), "Authored moonlight, ultimate and attack-speed values match the brief: 8 s / 10 s / x2.");
            director.enabled = false;
            roster.GetComponent<LiminalPlayerHealth>().GrantInvulnerability(240);
            fixtures = new GameObject("LunaCombatValidationFixtures");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Combat Test Floor";
            floor.transform.SetParent(fixtures.transform);
            floor.transform.position = Arena + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(55, .5f, 55);
            floor.GetComponent<Renderer>().sharedMaterial = Material(new Color(.14f, .17f, .23f));
            ResetPlayer();
            yield return .6f;
            Check(combat.CanAct, "The battle body can act in a live dungeon.");
            fx.EyeFire.SetActiveFire(true);
            CheckEyesOff(fx.EyeFire, "An external activation request before the ultimate is rejected immediately");
            yield return .2f;
            CheckEyesOff(fx.EyeFire, "An external activation request stays blocked after normal updates");
            fx.SetAwakened(true);
            Check(!fx.Awakened, "A VFX request cannot create an ultimate without its real gameplay state.");
            CheckEyesOff(fx.EyeFire, "A VFX awakening request before the ultimate");
            motor.SetAutomationInput(Vector2.up, Arena + Vector3.forward * 8);
            yield return .5f;
            CheckEyesOff(fx.EyeFire, "Ordinary movement before the ultimate");
            ResetPlayer();
            yield return .1f;

            var direct = Target("Direct target", new Vector3(0, 0, 4), new Color(.7f, .3f, .24f));
            var splash = Target("Splash target", new Vector3(combat.explosionRadius * .7f, 0, 4), new Color(.3f, .6f, .8f));
            var immune = Target("Immune target", new Vector3(-combat.explosionRadius * .7f, 0, 4), new Color(.65f, .6f, .3f));
            var untargetable = Target("Untargetable target", new Vector3(0, 0, 4 + combat.explosionRadius * .7f), new Color(.5f, .4f, .7f));
            immune.IgnoreDamage = true;
            untargetable.CanBeTargeted = false;
            yield return .1f;
            Aim(direct.AimPoint);
            yield return 0f;
            for (int phase = 0; phase < 3; phase++)
            {
                while (!combat.CanAct || combat.AttackCooldownRemaining > 0) yield return 0f;
                int before = direct.HitCount;
                Check(combat.TryAttack() && combat.LastAttackPhase == phase, $"Normal attack {phase + 1} emits the correct moon phase.");
                if (phase == 0) report.normalAttackCooldown = combat.AttackCooldownRemaining;
                yield return .12f;
                CaptureGame("attack-" + (phase + 1));
                float until = Time.time + 1f;
                while (direct.HitCount == before && Time.time < until) yield return 0f;
                Check(direct.HitCount > before, $"Attack {phase + 1} damages a live TrainingEnemy through the real projectile.");
                CheckEyesOff(fx.EyeFire, $"Normal attack {phase + 1} before the ultimate");
                if (phase < 2) Check(splash.HitCount == 0, $"Attack {phase + 1} does not apply the full-moon area hit.");
            }
            yield return .2f;
            report.directHits = direct.HitCount;
            report.splashHits = splash.HitCount;
            Check(splash.HitCount > 0, "The third hit damages a nearby target outside the direct projectile path.");
            Check(immune.HitCount == 0 && untargetable.HitCount == 0, "Area damage respects immunity and untargetable enemies.");
            direct.gameObject.SetActive(false);
            splash.gameObject.SetActive(false);
            Aim(immune.AimPoint);
            yield return .6f;
            Check(combat.TryAttack(), "An aimed shot can be attempted towards the protected targets.");
            yield return 1f;
            Check(immune.HitCount == 0 && untargetable.HitCount == 0, "Direct projectiles also respect immunity and untargetable enemies.");
            immune.gameObject.SetActive(false);
            untargetable.gameObject.SetActive(false);

            combat.CancelActions();
            yield return combat.comboResetTime + .1f;
            ResetPlayer();
            // Beyond acquisition range and collision range, but inside the final full-moon blast.
            var missed = Target("Missed-shot splash target", new Vector3(0, 0, combat.projectileRange + .4f + combat.explosionRadius * .6f), new Color(.3f, .7f, .5f));
            Aim(Arena + Vector3.forward * (combat.projectileRange + 6));
            yield return .1f;
            for (int phase = 0; phase < 3; phase++)
            {
                while (!combat.CanAct || combat.AttackCooldownRemaining > 0) yield return 0f;
                Check(combat.TryAttack() && combat.LastAttackPhase == phase, $"Miss scenario keeps combo phase {phase + 1}.");
                if (phase < 2) yield return combat.attackInterval + .02f;
            }
            yield return combat.projectileRange / Mathf.Max(.1f, ProjectileTuning.ScaleSpeed(combat.projectileSpeed)) + .5f;
            report.missedSplashHits = missed.HitCount;
            Check(missed.HitCount > 0, "A full moon that hits no enemy still detonates and deals area damage at its range endpoint.");
            missed.gameObject.SetActive(false);
            combat.CancelActions();
            yield return .2f;

            direct.gameObject.SetActive(true);
            ResetPlayer();
            Aim(direct.AimPoint);
            yield return .2f;
            while (!combat.CanAct || combat.AttackCooldownRemaining > 0) yield return 0f;
            Check(combat.TryAttack(), "A normal attack establishes the moonlight priority target.");
            yield return .6f;
            Check(combat.LastHitTarget == direct, "The real projectile records its last hit target.");
            splash.transform.position = Arena + new Vector3(-1.5f, 0, 2);
            splash.gameObject.SetActive(true);
            Physics.SyncTransforms();
            Check(combat.TryMoonlight(), "The moonlight skill activates during real dungeon play.");
            float moonlightStart = Time.time;
            int moonlightHits = direct.HitCount;
            yield return .3f;
            Check(combat.IsMoonlightActive && combat.MoonlightTarget == direct, "Moonlight prioritizes the basic-attack hit over a nearer alternative target.");
            CheckEyesOff(fx.EyeFire, "Casting E moonlight before the ultimate");
            direct.transform.position += Vector3.right * 1.5f;
            Physics.SyncTransforms();
            yield return 1.2f;
            Check(combat.MoonlightTarget == direct && direct.HitCount > moonlightHits, "Moonlight follows its moving priority target and applies real damage.");
            CheckEyesOff(fx.EyeFire, "E moonlight tracking and damage before the ultimate");
            CaptureGame("moonlight-follow");
            while (combat.IsMoonlightActive && Time.time - moonlightStart < 9f) yield return 0f;
            report.moonlightSeconds = Time.time - moonlightStart;
            Check(!combat.IsMoonlightActive && report.moonlightSeconds >= 7.8f && report.moonlightSeconds < 8.6f,
                "Moonlight expires after its authored eight seconds without time acceleration.");

            combat.CancelActions();
            yield return .3f;
            Check(combat.TryUltimate(), "The ultimate activates on the real battle body.");
            float ultimateStart = Time.time;
            yield return .2f;
            Check(combat.IsAwakened && fx.EyeFire.Active && fx.EyeFire.LiveParticleCount > 0 && fx.EyeFire.VisibleRendererCount > 0,
                "The ultimate enables the golden eye fire and produces live flame particles.");
            for (int i = 0; i < 3; i++)
            {
                while (!combat.CanAct || combat.AttackCooldownRemaining > 0) yield return 0f;
                Check(combat.TryAttack() && combat.LastAttackPhase == 2, "Every awakened attack uses the full-moon phase.");
                if (i == 0) report.awakenedAttackCooldown = combat.AttackCooldownRemaining;
                yield return 0f;
            }
            Check(Mathf.Abs(report.normalAttackCooldown - report.awakenedAttackCooldown * 2) < .08f,
                "Awakening halves the real attack cooldown (twice the attack rate).");
            CaptureGame("ultimate-game-camera");
            var closeup = CaptureEyes(fx, "ultimate-eye-closeup");
            while (closeup.MoveNext()) yield return closeup.Current;
            var gallery = CaptureGallery(fx);
            while (gallery.MoveNext()) yield return gallery.Current;
            motor.SetAutomationInput(Vector2.right, motor.transform.position + Vector3.right * 5);
            float motionEnd = Time.time + 2.4f;
            int frame = 0;
            while (Time.time < motionEnd)
            {
                if (saved.captureFrames) CaptureGame("Frames/ultimate-move-" + (frame++).ToString("D3"));
                yield return .1f;
            }
            closeup = CaptureEyes(fx, "ultimate-moving-trail-closeup");
            while (closeup.MoveNext()) yield return closeup.Current;
            motor.SetAutomationInput(Vector2.zero, motor.transform.position + Vector3.forward * 5);
            while (combat.IsAwakened && Time.time - ultimateStart < 11f) yield return 0f;
            report.ultimateSeconds = Time.time - ultimateStart;
            Check(!combat.IsAwakened && report.ultimateSeconds >= 9.8f && report.ultimateSeconds < 10.6f,
                "Awakening expires after its authored ten seconds without time acceleration.");
            yield return .2f;
            CheckEyesOff(fx.EyeFire, "The expired ultimate clears its eye effects completely");
            fx.EyeFire.SetActiveFire(true);
            yield return .1f;
            CheckEyesOff(fx.EyeFire, "An external activation request after ultimate expiry is also rejected");

            ResetPlayer();
            while (!combat.CanAct || combat.AttackCooldownRemaining > 0) yield return 0f;
            Check(combat.TryAttack(), "A projectile can be launched immediately before a hunter swap.");
            var oldCombat = combat;
            while (oldCombat.Busy) yield return 0f;
            Check(roster.Select(0), "The existing Astraia can be selected after Luna's attacks.");
            yield return .3f;
            Check(!oldCombat.isActiveAndEnabled && oldCombat.ActiveProjectileCount == 0 && !oldCombat.IsAwakened
                && !oldCombat.IsMoonlightActive && !fx.EyeFire.Active && fx.EyeFire.LiveParticleCount == 0
                && fx.EyeFire.VisibleRendererCount == 0 && fx.ActiveBursts == 0,
                "Disabling Luna clears projectiles, skill state and eye effects.");
            Check(roster.GetComponent<PlayerCombat>().enabled && roster.GetComponent<TalismanCaster>().enabled
                && roster.GetComponent<MeleeSlash>().enabled && motor.allowDash, "Returning to Astraia restores her existing combat, talismans, melee and dash.");
            Check(roster.Select(luna), "Luna remains selectable after cleanup.");
            yield return .3f;
            combat = roster.CurrentVisual.GetComponent<LunaCombatController>();
            Check(combat.CanAct && combat.ActiveProjectileCount == 0 && !combat.IsAwakened && !combat.IsMoonlightActive,
                "Reactivating Luna begins with clean combat effects.");
            director.EnterLobby();
            yield return .4f;
            Check(roster.WearsLobbyForm && !combat.isActiveAndEnabled && !combat.TryAttack(), "Returning to the lobby restores peaceful appearance and blocks combat.");
            motor.ReleaseAutomation();
        }

        static void Aim(Vector3 point) => motor.SetAutomationInput(Vector2.zero, point);

        static void ResetPlayer()
        {
            motor.ResetAt(Arena + Vector3.up * .04f);
            Aim(Arena + Vector3.forward * 5);
            Physics.SyncTransforms();
            var follow = Object.FindFirstObjectByType<IsometricFollowCamera>();
            if (follow) follow.Snap();
        }

        static Material Material(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            materials.Add(material);
            return material;
        }

        static TrainingEnemy Target(string name, Vector3 offset, Color color)
        {
            var root = new GameObject(name);
            root.transform.SetParent(fixtures.transform);
            root.transform.position = Arena + offset;
            var shape = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shape.transform.SetParent(root.transform, false);
            shape.transform.localPosition = Vector3.up * .65f;
            shape.transform.localScale = new Vector3(.5f, .65f, .5f);
            shape.GetComponent<Renderer>().sharedMaterial = Material(color);
            var anchor = new GameObject("Aim Anchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = Vector3.up * 1.15f;
            var enemy = root.AddComponent<TrainingEnemy>();
            enemy.aimAnchor = anchor;
            enemy.Configure(10000, false);
            Physics.SyncTransforms();
            return enemy;
        }

        static void CaptureGame(string name)
        {
            if (!Camera.main) throw new InvalidOperationException("The live game camera is unavailable.");
            Capture(Camera.main, name, 1280, 720);
        }

        static Camera InspectionCamera(string name, Vector3 position, Vector3 target, float fieldOfView)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fixtures.transform);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .045f, .085f);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 60;
            camera.fieldOfView = fieldOfView;
            camera.transform.position = position;
            camera.transform.LookAt(target);
            return camera;
        }

        static IEnumerator CaptureEyes(LunaCombatVfx fx, string name, bool fire = true)
        {
            var eye = fx.EyeFire;
            Check(eye.LeftEyeAnchor && eye.RightEyeAnchor, "Both eye-fire anchors exist on the current visual.");
            Vector3 center = (eye.LeftEyeAnchor.position + eye.RightEyeAnchor.position) * .5f;
            var visual = eye.Visual ? eye.Visual : fx.transform;
            var camera = InspectionCamera("Luna Eye Inspection Camera", center + visual.forward * .7f + Vector3.up * .06f,
                center + Vector3.up * .08f, 32);
            camera.farClipPlane = 2.5f;
            var previousCamera = eye.CaptureCamera;
            try
            {
                eye.CaptureCamera = camera;
                yield return 0f; // Let the world-space quads face the inspection camera in LateUpdate.
                center = (eye.LeftEyeAnchor.position + eye.RightEyeAnchor.position) * .5f;
                camera.transform.position = center + visual.forward * .7f + Vector3.up * .06f;
                camera.transform.LookAt(center + Vector3.up * .08f);
                if (fire) eye.SetActiveFire(true);
                Capture(camera, name, 960, 720);
            }
            finally
            {
                eye.CaptureCamera = previousCamera;
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static IEnumerator CaptureGallery(LunaCombatVfx fx)
        {
            var root = new GameObject("Luna Phase Gallery");
            root.transform.SetParent(fixtures.transform);
            root.transform.position = Arena + new Vector3(0, 10, 0);
            var camera = InspectionCamera("Luna Phase Gallery Camera", root.transform.position + Vector3.back * 5,
                root.transform.position, 30);
            camera.farClipPlane = 10;
            try
            {
                for (int phase = 0; phase < 3; phase++)
                {
                    var visual = fx.CreateProjectileVisual(phase, false, root.transform);
                    visual.transform.localPosition = Vector3.right * ((phase - 1) * 1.5f);
                    visual.transform.localScale = Vector3.one;
                    var billboard = visual.GetComponent<LunaProjectileVisual>();
                    if (billboard) billboard.CaptureCamera = camera;
                }
                yield return .12f;
                Capture(camera, "projectile-phases-crescent-half-full", 1440, 720);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static void Capture(Camera camera, string name, int width, int height)
        {
            string path = Output + "/" + name + ".png";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                report.captures.Add(new CaptureInfo { path = path, gameTime = Time.time });
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
            }
        }

        static void WriteReport()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + (saved != null && saved.eyePreview ? "/eye-preview.json" : "/combat-validation.json"), JsonUtility.ToJson(report, true));
        }

        static void Finish(string error)
        {
            if (finishing) return;
            finishing = true;
            try
            {
                if (report == null) report = new Report { utc = DateTime.UtcNow.ToString("O") };
                report.status = error == null ? "passed" : "failed";
                report.error = error;
                WriteReport();
                if (error != null) Debug.LogError("[LunaCombatValidation] " + error);
                else Debug.Log("[LunaCombatValidation] Passed " + report.checks.Count + " checks. " + Output);
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
            (routine as IDisposable)?.Dispose();
            routine = null;
            if (motor) motor.ReleaseAutomation();
            if (combat) combat.CancelActions();
            if (fixtures) Object.DestroyImmediate(fixtures);
            foreach (var material in materials) if (material) Object.DestroyImmediate(material);
            materials.Clear();
            HitFeedback.CancelHitStop();
        }

        static void Restore()
        {
            try
            {
                Cleanup();
                if (saved == null) saved = JsonUtility.FromJson<SavedState>(SessionState.GetString(Key + ".saved", ""));
                if (saved == null) return;
                foreach (var preference in saved.preferences)
                {
                    if (!preference.exists) PlayerPrefs.DeleteKey(preference.key);
                    else if (preference.isString) PlayerPrefs.SetString(preference.key, preference.text);
                    else PlayerPrefs.SetInt(preference.key, preference.number);
                }
                PlayerPrefs.Save();
                Time.timeScale = saved.timeScale;
                Time.fixedDeltaTime = saved.fixedDeltaTime;
                Time.captureDeltaTime = saved.captureDeltaTime;
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
            }
        }
    }
}
#endif
