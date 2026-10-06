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

namespace AcRoguelike.Ruins.Editor
{
    /// <summary>Opt-in batch integration run. Restores progression and never saves the opened lobby scene.</summary>
    [InitializeOnLoad]
    public static class RuinsMonsterValidation
    {
        const string Active = "Ruins.MonsterValidation";
        const string Folder = "Library/RuinsMonsterValidation";
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

        static RuinsMonsterValidation()
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
            report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                RuinsMonsterBuilder.Build();
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
                if (now - started > 240) throw new TimeoutException("Ruins validation exceeded 240 wall seconds.");
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

        static IEnumerator Run()
        {
            ValidatePrefabs();
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            var mission = run.AvailableMissions.Single(m => m.id == "ruins_clearance");
            Check(mission.code == "G-04" && run.EnterMission(mission.id), "G-04 starts through the real lobby mission entry.");
            yield return Until(() => run.Rooms.Count == 5 && run.ActiveRoomIndex == 0, 10, "Ruins route startup");
            Check(run.CurrentStage.stageId == "concept_ruins", "The apocalypse roster is scoped to concept_ruins.");
            motor = run.player.GetComponent<PlayerMotor>();
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            var seen = new HashSet<RuinsMonsterKind>();
            for (int index = 1; index <= 3; index++)
            {
                int wanted = index;
                motor.ResetAt(run.Rooms[index].playerSpawn.position + Vector3.up * .05f);
                yield return Until(() => run.ActiveRoomIndex == wanted && run.LivingEnemyCount > 0, 8, "Enter ruins combat room " + index);
                var room = run.Rooms[index]; var monsters = room.GetComponentsInChildren<RuinsMonster>();
                Check(room.kind == LiminalRoomKind.Combat && monsters.Length == 3 && run.LivingEnemyCount == 3 &&
                    room.entranceGate.activeSelf && room.exitGate.activeSelf, "Ruins room " + index + " registers three living monsters and locks both gates.");
                Check(room.GetComponentsInChildren<CopierMonster>().Length == 0 && room.GetComponentsInChildren<LockerMonster>().Length == 0 &&
                    room.GetComponentsInChildren<MonitorTurret>().Length == 0, "Ruins room " + index + " contains no office-example enemies.");
                foreach (var other in monsters) { other.CancelAttack(); other.enabled = false; }
                foreach (var monster in monsters)
                {
                    bool first = seen.Add(monster.kind);
                    if (!first) continue;
                    monster.CancelAttack();
                    monster.enabled = false;
                    var rig = monster.visualRig;
                    Check(rig && rig.model && monster.Health.aimAnchor && monster.GetComponent<CharacterController>().enabled,
                        monster.kind + " has a Meshy visual, animation rig, aim anchor and grounded collision.");
                    if (rig.humanoidAnimator)
                    {
                        var skin = rig.model.GetComponentInChildren<SkinnedMeshRenderer>();
                        var rotations = skin.bones.Select(b => b.localRotation).ToArray();
                        yield return Seconds(.3f);
                        Check(rig.UsesAnimationClips && skin.bones.Where((b, n) => Quaternion.Angle(rotations[n], b.localRotation) > .2f).Any(),
                            monster.kind + " skeletal playback visibly changes bone poses.");
                    }
                    var actualBounds = GeometryBounds(monster.transform);
                    Check(actualBounds.size.y >= .5f && actualBounds.size.y <= 3.5f,
                        monster.kind + " idle geometry satisfies the Figma normal-monster height rule: " + actualBounds.size.y.ToString("F2") + " m.");
                    if (rig.humanoidAnimator) Check(actualBounds.min.y > -.2f && actualBounds.min.y < .25f,
                        monster.kind + " idle feet remain close to the grounded body pivot (min=" + actualBounds.min.y.ToString("F3") + ").");
                    monster.enabled = true;
                    monster.CancelAttack();
                    Vector3 place = monster.transform.position - monster.transform.forward * 5;
                    motor.ResetAt(place + Vector3.up * .05f);
                    Camera.main.GetComponent<IsometricFollowCamera>().Snap(); yield return null;
                    CaptureFront(monster);
                    int patterns = monster.kind == RuinsMonsterKind.ScrapBulwark || monster.kind == RuinsMonsterKind.CarrionDrone ? 1 : 2;
                    for (int pattern = 0; pattern < patterns; pattern++)
                    {
                        monster.CancelAttack();
                        int attacks = monster.Attacks, shots = monster.Shots, strikes = monster.AreaStrikes;
                        Check(monster.StartAttack(pattern), monster.kind + " pattern " + pattern + " starts from idle.");
                        yield return Seconds(.2f);
                        bool closeOrConnected = monster.kind == RuinsMonsterKind.PenitentHusk || monster.kind == RuinsMonsterKind.MourningMatron ||
                            (monster.kind == RuinsMonsterKind.OssuaryMedusa && pattern == 1);
                        Check(monster.IsWindingUp && monster.TelegraphVisible == closeOrConnected &&
                            !room.GetComponentsInChildren<RuinsGroundPulse>().Any(p => p.WarningVisible),
                            monster.kind + " pattern " + pattern + " reserves ground outlines for close or connected attacks.");
                        if (monster.kind == RuinsMonsterKind.OssuaryMedusa && pattern == 0)
                        {
                            Check(monster.AreaStrikes == strikes + 3 && rig.GeneratedMeshes.Length == 8, "Medusa prepares three unmarked ground strikes and eight flexible tentacles.");
                            var mesh = rig.GeneratedMeshes[0]; var before = mesh.vertices;
                            yield return Seconds(.2f);
                            Check(before.Where((v, i) => Vector3.Distance(v, mesh.vertices[i]) > .002f).Any(), "Medusa tentacle vertices deform during anticipation.");
                            Time.timeScale = 0; yield return RealSeconds(.06);
                            float stateTime = monster.StateTime; before = mesh.vertices; Vector3 position = monster.transform.position;
                            yield return RealSeconds(.2);
                            Check(monster.StateTime == stateTime && position == monster.transform.position && before.SequenceEqual(mesh.vertices),
                                "Pause freezes body movement, attack timing and flexible tentacles.");
                            Time.timeScale = 1;
                        }
                        yield return Until(() => monster.AttackCueVisible, 3, monster.kind + " pre-attack flash");
                        Capture(Camera.main, monster.kind + "-warning-" + pattern);
                        yield return Until(() => monster.Attacks == attacks + 1 && monster.State == RuinsMonsterState.Recovery, 8, monster.kind + " attack/recovery");
                        Check(monster.Attacks == attacks + 1 && !monster.TelegraphVisible, monster.kind + " executes once and exposes a recovery window.");
                        if (monster.kind == RuinsMonsterKind.ScrapBulwark || monster.kind == RuinsMonsterKind.CarrionDrone)
                            Check(monster.Shots == shots + 3, monster.kind + " emits exactly three projectiles.");
                    }
                    if (rig.humanoidAnimator)
                    {
                        Check(rig.idleClip && rig.walkClip && rig.runClip && rig.attackClip && rig.deathClip &&
                            rig.model.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.bones.Length > 12),
                            monster.kind + " has a real skinned Meshy skeleton and five motion roles.");
                    }
                    monster.CancelAttack(); monster.enabled = false;
                }
                foreach (var monster in monsters)
                {
                    monster.enabled = true; monster.CancelAttack(); monster.StartAttack();
                    monster.Health.TakeDamage(monster.Health.maxHealth * 100);
                    Check(!monster.Health.IsAlive && !monster.TelegraphVisible && !monster.GetComponent<CharacterController>().enabled,
                        monster.kind + " death immediately disables collision and pending warnings.");
                }
                HitFeedback.CancelHitStop();
                yield return Until(() => run.LivingEnemyCount == 0 && !room.exitGate.activeSelf, 5, "Ruins room clear " + index);
                yield return Seconds(.1f);
                Check(room.GetComponentsInChildren<RuinsProjectile>().Length == 0 && room.GetComponentsInChildren<RuinsGroundPulse>().Length == 0,
                    "Ruins room " + index + " has no orphaned projectiles or delayed strikes after death.");
            }
            Check(seen.Count == 5, "All five apocalypse creatures appear during one three-room dungeon run.");
            var exit = run.Rooms[4]; motor.ResetAt(exit.playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 4, 8, "Exit room activation");
            var finale = exit.GetComponent<AcRoguelike.RuinsBoss.RuinsBossArena>();
            if (finale)
            {
                Check(finale.Boss && run.LivingEnemyCount == 1, "The ordinary creature route now ends in its dedicated apocalypse boss encounter.");
                finale.Boss.Health.TakeDamage(100000); HitFeedback.CancelHitStop();
                yield return Until(() => run.LivingEnemyCount == 0, 5, "Apocalypse finale clear");
            }
            motor.ResetAt(exit.exit.position - exit.exit.forward * 2.5f + Vector3.up * .05f); yield return null;
            Check(run.ExitAvailable && run.TryUseExit() && run.Phase == LiminalRunPhase.Victory, "Clearing apocalypse rooms enables the mission exit and Victory.");
            run.RetryMission(); yield return null;
            Check(run.ActiveMission == mission && run.Phase == LiminalRunPhase.Exploring && run.Rooms.Count == 5, "Retry preserves G-04 and rebuilds the route.");
            run.ReturnToLobby(); yield return null;
            Check(run.Phase == LiminalRunPhase.Lobby && run.Rooms.Count == 0 && Object.FindObjectsByType<RuinsMonster>(FindObjectsSortMode.None).Length == 0,
                "Returning to the lobby clears the apocalypse roster.");
            Check(run.EnterMission("game_exhibition"), "G-06 remains available.");
            yield return Until(() => run.Rooms.Count == 5, 8, "Other-theme route");
            motor.ResetAt(run.Rooms[1].playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 1 && run.LivingEnemyCount > 0, 8, "Other-theme encounter");
            Check(run.Rooms[1].GetComponentsInChildren<RuinsMonster>().Length == 0 &&
                run.Rooms[1].GetComponentsInChildren<AcRoguelike.GameTheme.GameVoxelMonster>().Length > 0,
                "The Game dungeon still uses its own voxel roster.");
            run.ReturnToLobby(); yield return null;
            var projectileChecks = RuinsProjectileValidation.Run(text => report.checks.Add(text));
            try { while (projectileChecks.MoveNext()) yield return projectileChecks.Current; }
            finally { (projectileChecks as IDisposable)?.Dispose(); }
        }

        static void ValidatePrefabs()
        {
            foreach (RuinsMonsterKind kind in Enum.GetValues(typeof(RuinsMonsterKind)))
            {
                var prefab = Resources.Load<GameObject>("RuinsMonsters/" + kind);
                Check(prefab && prefab.GetComponent<RuinsMonster>().kind == kind, kind + " prefab resolves from Resources.");
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Check(renderers.Length > 0 && renderers.All(r => r.sharedMaterials.All(m => m && m.shader && !m.shader.name.Contains("Error"))),
                    kind + " has valid renderers and material shaders.");
            }
        }

        static Bounds GeometryBounds(Transform root)
        {
            Bounds bounds = default; bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null; bool temporary = false;
                // Unity 6's true option compensates the skin transform scale; TransformPoint applies it once below.
                if (renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, true); temporary = true; }
                else { var filter = renderer.GetComponent<MeshFilter>(); if (filter) mesh = filter.sharedMesh; }
                if (!mesh) continue;
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 p = root.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
                if (temporary) Object.DestroyImmediate(mesh);
            }
            return bounds;
        }
        static void CaptureFront(RuinsMonster monster)
        {
            var go = new GameObject("Temporary apocalypse portrait camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                Vector3 focus = monster.transform.position + Vector3.up * 1.55f;
                go.transform.position = focus + monster.transform.forward * 6.5f + monster.transform.right * 2.2f + Vector3.up * 1.3f;
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position); camera.fieldOfView = 38;
                Capture(camera, monster.kind + "-front");
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
        { Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true); SessionState.SetString(Active + ".report", json); File.WriteAllText(Folder + "/validation.json", json); }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            if (motor) motor.ReleaseAutomation(); HitFeedback.CancelHitStop(); Time.timeScale = 1; RestorePrefs();
            if (error != null) report.errors.Add(error);
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("RUINS_MONSTER_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
