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

namespace AcRoguelike.Forest.Editor
{
    /// <summary>Opt-in batch integration run. Restores progression and never saves the opened lobby scene.</summary>
    [InitializeOnLoad]
    public static class ForestMonsterValidation
    {
        const string Active = "Forest.MonsterValidation";
        const string Folder = "Library/ForestMonsterValidation";
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

        static ForestMonsterValidation()
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
                ForestMonsterBuilder.Build();
                EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity", OpenSceneMode.Single);
                ForestMonsterPreview.Capture();
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
                if (now - started > 360) throw new TimeoutException("Forest validation exceeded 360 wall seconds.");
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
            foreach (ForestMonsterKind kind in Enum.GetValues(typeof(ForestMonsterKind)))
            {
                var prefab = Resources.Load<GameObject>("ForestMonsters/" + kind);
                Check(prefab && prefab.GetComponent<LiminalPropMonster>(), kind + " prefab resolves from Resources.");
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                Check(filters.Length > 0 && filters.All(f => f.sharedMesh && f.sharedMesh.isReadable), kind + " has readable authored meshes for motion.");
                Check(prefab.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m && m.shader && !m.shader.name.Contains("Error"))), kind + " has valid materials.");
                int triangles = filters.Sum(f => f.sharedMesh.triangles.Length / 3);
                Check(triangles < 30000, kind + " mesh budget: " + triangles + " triangles.");
            }
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            var mission = run.AvailableMissions.Single(m => m.id == "forest_survey");
            Check(run.EnterMission(mission.id), "G-02 starts through the real lobby mission entry.");
            yield return Until(() => run.Rooms.Count == 4 && run.ActiveRoomIndex == 0, 10, "Forest route startup");
            Check(run.CurrentStage.stageId == "concept_forest", "Forest roster is scoped to concept_forest.");
            motor = run.player.GetComponent<PlayerMotor>();
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            var seen = new HashSet<Type>();
            for (int index = 0; index < 3; index++)
            {
                int wanted = index;
                motor.ResetAt(run.Rooms[index].playerSpawn.position + Vector3.up * .05f);
                yield return Until(() => run.ActiveRoomIndex == wanted && run.LivingEnemyCount > 0, 8, "Enter forest room " + index);
                var room = run.Rooms[index];
                var monsters = room.GetComponentsInChildren<LiminalPropMonster>();
                Check(room.kind == LiminalRoomKind.Combat && monsters.Length == 3 && run.LivingEnemyCount == 3 && room.entranceGate.activeSelf && room.exitGate.activeSelf,
                    "Forest room " + index + " registers three enemies and locks both gates.");
                Check(monsters.All(m => m.GetType().Namespace == "AcRoguelike.Forest"), "Forest room " + index + " uses only its themed roster.");
                foreach (var monster in monsters)
                {
                    seen.Add(monster.GetType());
                    Check(monster.Health && monster.Health.aimAnchor && (monster.GetComponent<CharacterController>().enabled || monster is ForestWorm), monster.GetType().Name + " health and collision are initialized.");
                }
                Camera.main.GetComponent<IsometricFollowCamera>().Snap();
                yield return Seconds(.2f);
                Capture(Camera.main, "forest-room-" + index);
                // Give the real room enough game time to exercise ambient movement and attack selection.
                yield return Seconds(2.5f);
                var focusEnemy = monsters.First(m => !(m is ForestWorm));
                Vector3 viewingPosition = focusEnemy.transform.position + focusEnemy.transform.forward * 4f;
                viewingPosition.y = room.transform.position.y + .05f;
                motor.ResetAt(viewingPosition);
                Camera.main.GetComponent<IsometricFollowCamera>().Snap();
                yield return Seconds(1f);
                Capture(Camera.main, "forest-combat-" + index);
                yield return Seconds(1.5f);
                Capture(Camera.main, "forest-action-" + index);
                foreach (var monster in monsters)
                {
                    // Underground worms deliberately reject damage; validation waits until their emergence.
                    if (monster.Health.IgnoreDamage)
                        yield return Until(() => !monster.Health.IgnoreDamage, 15, "Worm emerges for room cleanup");
                    monster.Health.TakeDamage(100000);
                }
                HitFeedback.CancelHitStop();
                yield return Until(() => run.LivingEnemyCount == 0 && !room.exitGate.activeSelf, 5, "Forest room clear " + index);
                yield return Seconds(.25f);
                Check(room.GetComponentsInChildren<ForestSapling>().Length == 0 && room.GetComponentsInChildren<ForestWindProjectile>().Length == 0 && room.GetComponentsInChildren<ForestRootWave>().Length == 0,
                    "Forest room " + index + " has no delayed attacks after owners die.");
            }
            Check(seen.Count == 4, "All four forest creatures appear in one three-room run.");
            var exit = run.Rooms[3]; motor.ResetAt(exit.playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 3, 8, "Exit room activation");
            motor.ResetAt(exit.exit.position - exit.exit.forward * 2.5f + Vector3.up * .05f); yield return null;
            Check(run.ExitAvailable && run.TryUseExit() && run.Phase == LiminalRunPhase.Victory, "Forest room clears unlock the mission exit and Victory.");
            run.RetryMission(); yield return null;
            Check(run.ActiveMission == mission && run.Rooms.Count == 4, "Retry preserves G-02 and rebuilds the forest route.");
            run.ReturnToLobby(); yield return null;
            Check(run.Phase == LiminalRunPhase.Lobby && run.Rooms.Count == 0 && !Object.FindFirstObjectByType<ForestFrog>() && !Object.FindFirstObjectByType<ForestTreant>(), "Lobby return clears the forest roster.");
            Check(run.EnterMission("game_exhibition"), "Another theme remains available.");
            yield return Until(() => run.Rooms.Count == 4, 8, "Other-theme route");
            motor.ResetAt(run.Rooms[0].playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 0 && run.LivingEnemyCount > 0, 8, "Other-theme encounter");
            Check(run.Rooms[0].GetComponentsInChildren<AcRoguelike.GameTheme.GameVoxelMonster>().Length > 0 && !Object.FindFirstObjectByType<ForestButterfly>(), "The Game dungeon keeps its own roster.");
            run.ReturnToLobby(); yield return null;
            var checks = ForestWormFrogValidation.Run(text => report.checks.Add(text));
            try { while (checks.MoveNext()) yield return checks.Current; } finally { (checks as IDisposable)?.Dispose(); }
            checks = ForestCanopyValidation.Run(text => report.checks.Add(text));
            try { while (checks.MoveNext()) yield return checks.Current; } finally { (checks as IDisposable)?.Dispose(); }
        }

        public static void CaptureAttack(Vector3 focus, string name)
        {
            var go = new GameObject("Temporary forest attack review camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 5.5f;
                go.transform.position = focus + new Vector3(6, 8, 8);
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position);
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
        { Directory.CreateDirectory(Folder); string json = JsonUtility.ToJson(report, true); SessionState.SetString(Active + ".report", json); File.WriteAllText(Folder + "/validation.json", json); }
        static void Finish(string error)
        {
            (routine as IDisposable)?.Dispose(); routine = null; waiting = null;
            if (motor) motor.ReleaseAutomation(); HitFeedback.CancelHitStop(); Time.timeScale = 1; RestorePrefs();
            if (error != null) report.errors.Add(error);
            report.status = report.errors.Count == 0 ? "passed" : "failed"; Save(); SessionState.SetBool(Active + ".finished", true);
            Debug.Log("FOREST_MONSTER_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
