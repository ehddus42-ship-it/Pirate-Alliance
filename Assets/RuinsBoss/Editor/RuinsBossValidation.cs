#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    /// <summary>Opt-in batch integration run. Restores progression and never saves the opened lobby scene.</summary>
    [InitializeOnLoad]
    public static class RuinsBossValidation
    {
        const string Active = "RuinsBoss.Validation";
        const string Folder = "Library/RuinsBossValidation";
        [Serializable] sealed class Report
        {
            public string status = "running", utc;
            public List<string> checks = new List<string>(), errors = new List<string>(), captures = new List<string>();
        }
        [Serializable] sealed class Pref { public string key; public bool existed; public int value; }
        [Serializable] sealed class Snapshot { public List<Pref> values = new List<Pref>(); }
        sealed class Wait { public Func<bool> condition; public float seconds; public string label; }
        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static double started, deadline;
        static int lastFrame = -1;
        static LiminalRunDirector run;
        static PlayerMotor motor;

        static RuinsBossValidation()
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
                    started = EditorApplication.timeSinceStartup; lastFrame = -1;
                    routine = SessionState.GetBool(Active + ".machinesOnly", false) ? RunMachinesOnly() : Run();
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
            => Begin(false);

        public static void RunMachineBatch()
            => Begin(true);

        static void Begin(bool machinesOnly)
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use a separate batch editor in Edit Mode, without -quit.");
            report = new Report { utc = DateTime.UtcNow.ToString("O") };
            SessionState.SetBool(Active + ".machinesOnly", machinesOnly);
            try
            {
                RuinsBossBuilder.Build();
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
                if (now - started > 240) throw new TimeoutException("Boss validation exceeded 240 wall seconds.");
                if (run && run.PlayerHealth) run.PlayerHealth.GrantInvulnerability(3);
                if (waiting != null)
                {
                    if (!waiting.condition()) { if (now > deadline) throw new TimeoutException(waiting.label); return; }
                    waiting = null;
                }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (!routine.MoveNext()) { Finish(null); return; }
                if (routine.Current is Wait wait) { waiting = wait; deadline = now + wait.seconds; }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }
        static Wait Until(Func<bool> condition, float timeout, string label) => new Wait { condition = condition, seconds = timeout, label = label };
        static Wait Seconds(float value)
        { float end = Time.time + value; return Until(() => Time.time >= end, value * 5 + 4, "Game-time wait " + value); }
        static Wait RealSeconds(double value)
        { double end = EditorApplication.timeSinceStartup + value; return Until(() => EditorApplication.timeSinceStartup >= end, (float)value + 2, "Pause observation"); }
        static void Check(bool passed, string label)
        { if (!passed) throw new InvalidOperationException(label); report.checks.Add(label); }

        static IEnumerator RunMachinesOnly()
        {
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            var checks = RuinsBossMachineValidation.Run(text => report.checks.Add(text));
            try { while (checks.MoveNext()) yield return checks.Current; }
            finally { (checks as IDisposable)?.Dispose(); }
        }

        static IEnumerator Run()
        {
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            var mission = run.AvailableMissions.Single(m => m.id == "ruins_clearance");
            Check(run.EnterMission(mission.id), "G-04 starts through the actual lobby.");
            yield return Until(() => run.Rooms.Count == 4 && run.ActiveRoomIndex == 0, 10, "Ruins route startup");
            Check(run.Rooms[3].GetComponent<RuinsBossArena>() && run.Rooms[3].kind == LiminalRoomKind.Boss,
                "G-04 retains three ordinary combat rooms and ends in the apocalypse boss arena.");
            motor = run.player.GetComponent<PlayerMotor>(); motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            for (int index = 0; index < 3; index++)
            {
                int wanted = index;
                motor.ResetAt(run.Rooms[index].playerSpawn.position + Vector3.up * .05f);
                yield return Until(() => run.ActiveRoomIndex == wanted && run.LivingEnemyCount > 0, 8, "Ordinary ruins room " + index);
                var monsters = run.Rooms[index].GetComponentsInChildren<AcRoguelike.Ruins.RuinsMonster>();
                Check(monsters.Length == 3, "Ordinary room " + index + " keeps the apocalypse creature roster.");
                foreach (var monster in monsters) monster.Health.TakeDamage(monster.Health.maxHealth * 100);
                HitFeedback.CancelHitStop();
                yield return Until(() => run.LivingEnemyCount == 0, 5, "Ordinary room clear");
            }
            var arena = run.Rooms[3]; motor.ResetAt(arena.playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 3 && run.LivingEnemyCount == 1, 8, "Boss room entry");
            var boss = arena.GetComponent<RuinsBossArena>().Boss;
            Check(boss && arena.GetComponentsInChildren<TrafficLightBoss>().Length == 0 && arena.GetComponentsInChildren<AcRoguelike.Ruins.RuinsMonster>().Length == 0,
                "Dedicated finale contains the electrical boss without office or ordinary monsters.");
            var left = boss.leftMachine; var right = boss.rightMachine;
            Check(left && right && boss.turret && left.kind != right.kind && left.IsDormant && right.IsDormant,
                "Two distinct machines start dormant at the left and right with a rear missile turret.");
            Check(!left.Health.CanBeTargeted && !right.Health.CanBeTargeted && !left.GetComponent<CharacterController>().enabled && !right.GetComponent<CharacterController>().enabled,
                "Dormant machines do not intercept target selection or melee attacks.");
            left.Health.TakeDamage(100000); Check(left.Health.IsAlive && left.Health.Health == left.Health.maxHealth, "Sleeping machine ignores even lethal area damage.");
            Check(arena.entranceGate.activeSelf && arena.exitGate.activeSelf && !run.TryUseExit(), "Boss fight locks its gates and mission exit.");
            var rig = boss.visualRig;
            var legs = right.GetComponentInChildren<RuinsWalkerLegRig>();
            Check(legs && legs.BoneCount == 13 && right.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.bones.Length == 13),
                "The four-legged war machine has thirteen mechanical bones over its original Meshy geometry.");
            Check(rig && rig.humanoidAnimator && rig.idleClip && rig.castClip && rig.deathClip && rig.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.bones.Length >= 20),
                "Sovereign uses a real Meshy skeleton with idle, cast and death clips.");
            motor.ResetAt(arena.transform.TransformPoint(new Vector3(0, .05f, 14)));
            Camera.main.GetComponent<IsometricFollowCamera>().Snap(); yield return null;
            CaptureEncounter(arena, "encounter-dormant");
            var bone = rig.GetComponentsInChildren<SkinnedMeshRenderer>().First().bones.First(t => t.name.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0);
            Quaternion rest = bone.localRotation;
            for (int pattern = 0; pattern < 3; pattern++)
            {
                yield return Until(() => boss.State == RuinsBossState.Idle, 15, "Boss idle before explicit pattern");
                int before = boss.ElectricShots, completed = boss.PatternsCompleted;
                Check(boss.StartPattern(pattern), "Electric pattern " + pattern + " starts with anticipation.");
                yield return Seconds(.55f);
                Check(boss.GetComponentsInChildren<Telegraph>().All(t => !t.Visible), "Pattern " + pattern + " has no projectile floor or trajectory previews.");
                if (pattern == 0)
                {
                    Check(Quaternion.Angle(rest, bone.localRotation) > .05f, "The imported skeleton changes pose during casting.");
                    Time.timeScale = 0; yield return RealSeconds(.08);
                    float elapsed = boss.StateTime; Vector3 p = left.transform.position; Quaternion q = bone.localRotation;
                    yield return RealSeconds(.2);
                    Check(Mathf.Abs(boss.StateTime - elapsed) < .0001f && Vector3.Distance(p, left.transform.position) < .0001f && Quaternion.Angle(q, bone.localRotation) < .001f,
                        "Pausing freezes casting, skeleton animation and machines."); Time.timeScale = 1;
                }
                yield return Until(() => boss.AttackCueVisible, 2, "Electric pattern pre-attack glint");
                Check(boss.ElectricShots == before, "Pattern " + pattern + " glints on the caster before firing.");
                CaptureEncounter(arena, "warning-" + pattern);
                yield return Until(() => boss.ElectricShots > before + 4, 7, "Pattern emits electric bullets");
                var bullets = arena.GetComponentsInChildren<RuinsBossProjectile>().Where(p => p.Kind == RuinsBossProjectileKind.Electric).ToArray();
                Check(bullets.Length > 0 && bullets.All(p => Vector3.Angle(p.Direction, boss.LockedSafeDirection) > boss.safeHalfAngle - .1f),
                    "Pattern " + pattern + " keeps its committed escape lane clear.");
                yield return Seconds(.65f);
                CaptureEncounter(arena, "electric-pattern-" + pattern);
                yield return Until(() => boss.PatternsCompleted > completed && boss.State == RuinsBossState.Recovery, 10, "Pattern recovery");
                Check(boss.ElectricShots > before, "Pattern " + pattern + " completes and exposes a recovery window.");
            }
            yield return Until(() => boss.MachinesActivated >= 1, 20, "First machine awakening");
            Check(left.ActivationCount == 1 && right.ActivationCount == 0, "The left machine wakes first, exactly once.");
            CaptureEncounter(arena, "first-machine-awake");
            boss.Health.TakeDamage(boss.Health.maxHealth / 2); HitFeedback.CancelHitStop();
            yield return Until(() => boss.MachinesActivated == 2, 25, "Second machine awakening");
            Check(right.ActivationCount == 1 && left.ActivationCount == 1, "The right machine wakes later, exactly once.");
            Check(left.Health.CanBeTargeted && right.Health.CanBeTargeted, "Awakened machines can be attacked and destroyed.");
            int machineHealth = left.Health.Health;
            left.Health.TakeDamage(100); HitFeedback.CancelHitStop();
            Check(left.Health.Health == machineHealth - 75, "Dormancy and awakening preserve the G-04 armor modifier.");
            foreach (var machine in new[] { left, right })
            {
                yield return Until(() => machine.State == RuinsBossState.Idle, 15, "Machine idle");
                int damageAttempts = machine.DamageAttempts;
                int motionFrames = legs.MotionFrames;
                Check(machine.StartAttack(0), machine.kind + " begins its charge warning.");
                yield return Seconds(.4f);
                Vector3 locked = machine.LockedDirection;
                Check(machine.TelegraphVisible, machine.kind + " charge has a visible corridor.");
                motor.ResetAt(arena.transform.TransformPoint(new Vector3(3, .05f, 13)));
                yield return Until(() => machine.AttackCueVisible, 2, "Machine charge glint");
                yield return Until(() => machine.State == RuinsBossState.Recovery, 8, "Charge ends");
                Check(machine.Travelled <= machine.chargeDistance + .01f && Vector3.Angle(locked, machine.LockedDirection) < .1f && machine.DamageAttempts <= damageAttempts + 1,
                    machine.kind + " commits its direction and respects the charge distance.");
                if (machine == right)
                    Check(legs.MotionFrames > motionFrames && legs.MaximumFootDisplacement > .03f, "Walking-machine feet articulate during the actual moving charge.");
                yield return Until(() => machine.State == RuinsBossState.Idle, 8, "Machine missile idle");
                int shots = machine.Shots;
                Check(machine.StartAttack(1), machine.kind + " begins its missile warning.");
                yield return Seconds(.4f);
                Check(!machine.TelegraphVisible, machine.kind + " missile windup has no floor preview.");
                yield return Until(() => machine.AttackCueVisible, 2, "Machine missile glint");
                Check(machine.Shots == shots, machine.kind + " glints before launching missiles.");
                yield return Until(() => machine.Shots >= shots + 2, 8, "Machine missile volley");
                Check(machine.Shots == shots + 2, machine.kind + " emits two weakly homing Meshy missiles.");
            }
            yield return Until(() => boss.ArtilleryShots >= 3 && boss.ArtilleryShots % 3 == 0, 5, "Complete artillery salvo");
            Check(boss.ArtilleryShots >= 3, "Rear artillery emits complete three-missile volleys on its cooldown.");
            CaptureEncounter(arena, "encounter-active");
            RuinsBossProjectile.LaunchArtillery(boss.turret.position + Vector3.up * 3, motor.transform.position, boss.Health, run.PlayerHealth, arena, 1);
            RuinsBossProjectile.FireHoming(left.transform.position + Vector3.up, Vector3.forward, left.Health, run.PlayerHealth, arena, 1);
            boss.Health.TakeDamage(100000); HitFeedback.CancelHitStop();
            Check(boss.EncounterCancelled && left.EncounterCancelled && right.EncounterCancelled && !boss.AttackCueVisible
                && !left.AttackCueVisible && !right.AttackCueVisible && !boss.GetComponent<CharacterController>().enabled,
                "Boss death shuts down both machines, all warning patterns and collision immediately.");
            yield return Seconds(.2f);
            Check(arena.GetComponentsInChildren<RuinsBossProjectile>().Length == 0 && arena.GetComponentsInChildren<RuinsBossImpact>().Length == 0,
                "Defeat leaves no live missile or delayed explosion behind.");
            Check(run.LivingEnemyCount == 0 && run.ClearedRoomCount == 4, "Defeating the sovereign clears the encounter without requiring dormant-machine kills.");
            motor.ResetAt(arena.exit.position - arena.exit.forward * 2.5f + Vector3.up * .05f); yield return null;
            Check(run.ExitAvailable && run.TryUseExit() && run.Phase == LiminalRunPhase.Victory, "Boss defeat enables exit and mission Victory.");
            run.RetryMission(); yield return null;
            Check(run.Phase == LiminalRunPhase.Exploring && run.Rooms.Count == 4 && run.Rooms[3].GetComponent<RuinsBossArena>(), "Retry restores the same apocalypse boss route.");
            run.ReturnToLobby(); yield return null;
            Check(Object.FindObjectsByType<RuinsStormBoss>(FindObjectsSortMode.None).Length == 0, "Lobby return unloads the encounter.");
            Check(run.EnterMission("game_exhibition"), "G-06 remains available.");
            yield return Until(() => run.Rooms.Count == 4, 8, "Game route");
            Check(run.Rooms[3].roomId == "Game_08_Boss" && !run.Rooms[3].GetComponent<RuinsBossArena>(), "Other theme keeps its own boss arena.");
            run.ReturnToLobby(); yield return null;
            var projectileChecks = RuinsBossProjectileValidation.Run(text => report.checks.Add(text));
            try { while (projectileChecks.MoveNext()) yield return projectileChecks.Current; }
            finally { (projectileChecks as IDisposable)?.Dispose(); }
            var machineChecks = RuinsBossMachineValidation.Run(text => report.checks.Add(text));
            try { while (machineChecks.MoveNext()) yield return machineChecks.Current; }
            finally { (machineChecks as IDisposable)?.Dispose(); }
        }

        static void CaptureEncounter(LiminalRoom arena, string name)
        {
            var go = new GameObject("Temporary encounter camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                Vector3 focus = arena.transform.TransformPoint(new Vector3(0, 1.7f, 23));
                go.transform.position = focus + arena.transform.TransformDirection(new Vector3(0, 17, -25));
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position);
                camera.orthographic = true; camera.orthographicSize = 14;
                Capture(camera, name);
            }
            finally { Object.DestroyImmediate(go); }
        }
        static void Capture(Camera camera, string name)
        {
            var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null; float aspect = camera.aspect;
            try
            {
                target.Create(); camera.aspect = 1.6f;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP screenshot request unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target; pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Folder); string path = Folder + "/" + name + ".png";
                File.WriteAllBytes(path, pixels.EncodeToPNG()); report.captures.Add(path);
            }
            finally { camera.aspect = aspect; RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
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
            foreach (var key in keys) PlayerPrefs.DeleteKey(key); PlayerPrefs.Save();
        }
        static void RestorePrefs()
        {
            string json = SessionState.GetString(Active + ".prefs", ""); if (string.IsNullOrEmpty(json)) return;
            foreach (var item in JsonUtility.FromJson<Snapshot>(json).values)
                if (item.existed) PlayerPrefs.SetInt(item.key, item.value); else PlayerPrefs.DeleteKey(item.key);
            PlayerPrefs.Save();
        }
        static void Save()
        { Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true); SessionState.SetString(Active + ".report", json); File.WriteAllText(Folder + (SessionState.GetBool(Active + ".machinesOnly", false) ? "/machine-validation.json" : "/validation.json"), json); }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            if (motor) motor.ReleaseAutomation(); HitFeedback.CancelHitStop(); Time.timeScale = 1; RestorePrefs();
            if (error != null) report.errors.Add(error);
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("RUINS_BOSS_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
