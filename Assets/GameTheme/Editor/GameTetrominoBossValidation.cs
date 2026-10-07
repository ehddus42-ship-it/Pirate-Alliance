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

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Opt-in batch integration run. Restores progression and never saves the opened lobby scene.</summary>
    [InitializeOnLoad]
    public static class GameTetrominoBossValidation
    {
        const string Active = "GameTheme.TetrominoBossValidation";
        const string Folder = "Library/GameBossValidation";
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

        static GameTetrominoBossValidation()
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
                GameTetrominoBossBuilder.Build();
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
                if (now - started > 180) throw new TimeoutException("Boss validation exceeded 180 wall seconds.");
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
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Phase == LiminalRunPhase.Lobby, 20, "Lobby startup");
            // Automated runs skip augment offers so random augments cannot change what this validation measures.
            LiminalRunDirector.SuppressAugmentOffers = true;
            var mission = run.AvailableMissions.Single(m => m.id == "game_exhibition");
            Check(mission.code == "G-06" && run.EnterMission(mission.id), "G-06 starts through the real lobby mission entry.");
            yield return Until(() => run.Rooms.Count == 4 && run.ActiveRoomIndex == 0, 10, "Game route startup");
            Check(run.Rooms[3].roomId == "Game_08_Boss" && run.Rooms[3].kind == LiminalRoomKind.Boss && GameThemeAtmosphere.LeaseCount == 1,
                "Four-room Game route ends in the dedicated arena and owns one atmosphere lease.");
            motor = run.player.GetComponent<PlayerMotor>(); motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward, false);
            for (int index = 0; index < 3; index++)
            {
                int wanted = index;
                motor.ResetAt(run.Rooms[index].playerSpawn.position + Vector3.up * .05f);
                yield return Until(() => run.ActiveRoomIndex == wanted && run.LivingEnemyCount > 0, 8, "Enter normal room " + index);
                var room = run.Rooms[index]; var monsters = room.GetComponentsInChildren<GameVoxelMonster>();
                Check(room.kind == LiminalRoomKind.Combat && monsters.Length >= 3 && monsters.Length == run.LivingEnemyCount &&
                    room.entranceGate.activeSelf && room.exitGate.activeSelf, "Normal room " + index + " keeps its registered voxel roster and locked gates.");
                foreach (var enemy in monsters) enemy.Health.TakeDamage(enemy.Health.maxHealth * 100);
                HitFeedback.CancelHitStop();
                yield return Until(() => run.LivingEnemyCount == 0 && !room.exitGate.activeSelf, 5, "Normal room clear " + index);
            }
            var arena = run.Rooms[3]; motor.ResetAt(arena.playerSpawn.position + Vector3.up * .05f);
            yield return Until(() => run.ActiveRoomIndex == 3 && run.LivingEnemyCount == 1, 8, "Boss room activation");
            var bosses = arena.GetComponentsInChildren<GameTetrominoBoss>();
            Check(bosses.Length == 1 && arena.GetComponentsInChildren<TrafficLightBoss>().Length == 0 &&
                arena.GetComponentsInChildren<GameVoxelMonster>().Length == 0, "Final room spawns exactly one Drop Keeper and no foreign boss.");
            var boss = bosses[0]; var rig = boss.cableRig;
            Check(rig && rig.leftHand && rig.rightHand && rig.GeneratedMeshes.Length == 5 && rig.CableVertexCount > 1000,
                "Meshy boss has two earbud hands and five flexible cable sections.");
            Check(arena.entranceGate.activeSelf && arena.exitGate.activeSelf && !run.TryUseExit(), "Boss encounter locks both doors and mission exit.");
            boss.CancelAttack();
            motor.ResetAt(arena.transform.TransformPoint(new Vector3(0, .05f, 13)));
            Camera.main.GetComponent<IsometricFollowCamera>().Snap(); yield return null;
            CaptureFront(boss);

            int shots = boss.Shots, bars = boss.BarsThrown, waves = boss.SummonWaves, summons = boss.SummonedChargers;
            Vector3 hand = rig.leftHand.localPosition;
            var cable = rig.GeneratedMeshes[3]; Vector3[] cableBefore = cable.vertices;
            Check(boss.StartAttack(0), "Volley pattern starts from idle."); yield return Seconds(.55f);
            Vector3[] cableAfter = cable.vertices;
            Check(!boss.TelegraphVisible && Vector3.Distance(hand, rig.leftHand.localPosition) > .05f &&
                cableBefore.Where((v, i) => Vector3.Distance(v, cableAfter[i]) > .005f).Any(), "Volley anticipation bends the cable and moves its earbud hand without a floor preview.");
            Time.timeScale = 0; yield return RealSeconds(.08);
            hand = rig.leftHand.localPosition; Quaternion pose = rig.transform.localRotation; float stateTime = boss.StateTime;
            cableBefore = cable.vertices; Vector3 bodyPosition = boss.transform.position;
            yield return RealSeconds(.3);
            Check(Vector3.Distance(hand, rig.leftHand.localPosition) < .0001f && Quaternion.Angle(pose, rig.transform.localRotation) < .001f &&
                Mathf.Abs(boss.StateTime - stateTime) < .0001f && Vector3.Distance(bodyPosition, boss.transform.position) < .0001f &&
                cableBefore.SequenceEqual(cable.vertices), "Time scale zero freezes body, hands, cable vertices and attack timing.");
            Time.timeScale = 1;
            yield return Until(() => boss.AttackCueVisible, 2, "Volley pre-attack glint");
            Check(boss.Shots == shots && !boss.TelegraphVisible, "Volley glints on the boss before firing with no trajectory preview.");
            yield return Until(() => boss.Shots >= shots + 9, 8, "Nine soft-drop shots");
            Check(boss.Shots == shots + 9 && boss.Volleys == 3, "The announced volley emits exactly nine blocks in three fans.");
            Capture(Camera.main, "attack-0-soft-drop");

            boss.CancelAttack(); Check(boss.StartAttack(1), "Spinning I pattern starts from idle.");
            yield return Seconds(.8f); Capture(Camera.main, "attack-1-spinning-I");
            Check(!boss.TelegraphVisible, "Thrown I block has no floor or trajectory preview.");
            yield return Until(() => boss.AttackCueVisible, 2, "I block pre-attack glint");
            Check(boss.BarsThrown == bars, "The boss glints before releasing its I block.");
            yield return Until(() => boss.BarsThrown == bars + 1, 8, "I block release");
            var bar = Object.FindObjectsByType<TetrominoProjectile>(FindObjectsSortMode.None).FirstOrDefault(p => p.Ricochet);
            Check(bar, "I pattern releases one real ricochet projectile."); Quaternion spin = bar.transform.GetChild(0).localRotation;
            yield return Seconds(.08f);
            Check(bar && Quaternion.Angle(spin, bar.transform.GetChild(0).localRotation) > 10 && boss.BarsThrown == bars + 1,
                "Released I block visibly spins without duplicate throws.");

            boss.CancelAttack(); Check(boss.StartAttack(2), "Three-Z summon pattern finds three clear positions.");
            yield return Seconds(.55f);
            Check(boss.GetComponentsInChildren<Telegraph>().All(t => !t.Visible), "Summon preparation has no detached floor markers.");
            yield return Until(() => boss.AttackCueVisible, 2, "Summon pre-attack glint");
            yield return Until(() => boss.SummonWaves == waves + 1, 8, "Z summon release");
            var chargers = arena.GetComponentsInChildren<TetrominoCharger>();
            Check(boss.SummonedChargers == summons + 3 && chargers.Length == 3 && chargers.All(c => c.WindingUp && c.Health.IsAlive),
                "The boss summons exactly three living, attackable Z chargers with anticipation.");
            Capture(Camera.main, "attack-2-three-Z");
            boss.CancelAttack(); Check(boss.StartAttack(0), "Death cleanup probe starts with a pending attack."); yield return null;
            TetrominoProjectile.Fire(arena.transform.TransformPoint(new Vector3(-8, 1, 15)), Vector3.forward, boss.Health, run.PlayerHealth, arena, 1, TetrominoShape.I, true);
            int attacks = boss.Attacks;
            boss.Health.TakeDamage(boss.Health.maxHealth * 100); HitFeedback.CancelHitStop();
            Check(!boss.Health.IsAlive && !boss.TelegraphVisible && !boss.AttackCueVisible && !boss.GetComponent<CharacterController>().enabled &&
                boss.GetComponentsInChildren<Telegraph>(true).All(t => !t.Visible), "Boss death immediately cancels pending warnings and body collision.");
            yield return Seconds(.15f);
            Check(boss.Attacks == attacks && Object.FindObjectsByType<TetrominoProjectile>(FindObjectsSortMode.None).Length == 0 &&
                Object.FindObjectsByType<TetrominoCharger>(FindObjectsSortMode.None).Length == 0, "Boss death removes owned shots and summons and prevents pending attacks.");
            Check(run.LivingEnemyCount == 0 && run.ClearedRoomCount == 4 && !arena.entranceGate.activeSelf, "Boss defeat clears the final registered encounter and unlocks the entrance.");
            motor.ResetAt(arena.exit.position - arena.exit.forward * 2.5f + Vector3.up * .05f); yield return null;
            Check(run.ExitAvailable && run.TryUseExit() && run.Phase == LiminalRunPhase.Victory, "Defeating Drop Keeper enables the mission exit and Victory.");
            run.RetryMission(); yield return null;
            Check(run.ActiveMission == mission && run.Phase == LiminalRunPhase.Exploring && run.Rooms.Count == 4 &&
                run.Rooms[3].roomId == "Game_08_Boss" && GameThemeAtmosphere.LeaseCount == 1, "Retry preserves G-06, its final boss and one atmosphere lease.");
            run.ReturnToLobby(); yield return null;
            Check(run.Phase == LiminalRunPhase.Lobby && run.ActiveMission == null && run.Rooms.Count == 0 && !GameThemeAtmosphere.IsApplied &&
                Object.FindObjectsByType<GameTetrominoBoss>(FindObjectsSortMode.None).Length == 0, "Returning to the lobby clears the boss route and restores the environment.");
            var projectileChecks = GameTetrominoProjectileValidation.Run(text => report.checks.Add(text));
            try { while (projectileChecks.MoveNext()) yield return projectileChecks.Current; }
            finally { (projectileChecks as IDisposable)?.Dispose(); }
        }

        static void CaptureFront(GameTetrominoBoss boss)
        {
            var go = new GameObject("Temporary boss portrait camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                Vector3 focus = boss.transform.position + Vector3.up * 1.8f;
                go.transform.position = focus + boss.transform.forward * 9 + boss.transform.right * 2.8f + Vector3.up * 2.2f;
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position); camera.fieldOfView = 36;
                Capture(camera, "boss-front");
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
            Debug.Log("GAME_TETROMINO_BOSS_VALIDATION: " + report.status + (error == null ? "" : "\n" + error));
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
