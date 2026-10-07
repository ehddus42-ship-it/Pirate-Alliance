using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using AcRoguelike.GameTheme;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Liminal.EditorTests
{
    /// <summary>Explicit integration run. Never saves scene/assets and restores every progression key it touches.</summary>
    [InitializeOnLoad]
    public static class GateMissionValidation
    {
        const string Prefix = "GateMission.Validation.";
        const string ScenePath = "Assets/Liminal/Scenes/LiminalRun.unity";
        [Serializable] public sealed class Pref { public string key; public bool existed; public int value; }
        [Serializable] public sealed class Snapshot { public List<Pref> values = new List<Pref>(); }
        [Serializable] public sealed class Report
        {
            public string status = "running", unityVersion, utc;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        static Report report;
        static IEnumerator routine;
        static int lastFrame = -1;
        static double started;
        static Camera captureCamera;
        static Canvas captureCanvas;
        static RenderTexture captureTexture, previousTarget;
        static RenderMode previousMode;
        static Camera previousCanvasCamera;
        static float previousPlane;

        sealed class EnvironmentSnapshot
        {
            public Color ambient, fogColor, background;
            public bool fog, post;
            public FogMode fogMode;
            public float density, fov, pitch, yaw, distance;
            public LayerMask volumeMask;
            public AntialiasingMode aa;
            public readonly Dictionary<Light, bool> lights = new Dictionary<Light, bool>();
            public readonly Dictionary<Volume, bool> volumes = new Dictionary<Volume, bool>();
            public static EnvironmentSnapshot Take()
            {
                var camera = Camera.main; var rig = camera.GetComponent<IsometricFollowCamera>();
                var data = camera.GetUniversalAdditionalCameraData();
                var s = new EnvironmentSnapshot { ambient = RenderSettings.ambientLight, fog = RenderSettings.fog, fogMode = RenderSettings.fogMode,
                    fogColor = RenderSettings.fogColor, density = RenderSettings.fogDensity, background = camera.backgroundColor, fov = camera.fieldOfView,
                    pitch = rig.pitch, yaw = rig.yaw, distance = rig.distance, post = data.renderPostProcessing, volumeMask = data.volumeLayerMask, aa = data.antialiasing };
                foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) s.lights.Add(light, light.enabled);
                foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) s.volumes.Add(volume, volume.enabled);
                return s;
            }
            public void RequireRestored()
            {
                var camera = Camera.main; var rig = camera.GetComponent<IsometricFollowCamera>(); var data = camera.GetUniversalAdditionalCameraData();
                Require(!GameThemeAtmosphere.IsApplied && GameThemeAtmosphere.LeaseCount == 0, "Game atmosphere lease leaked into the lobby.");
                Require(RenderSettings.ambientLight == ambient && RenderSettings.fog == fog && RenderSettings.fogMode == fogMode &&
                    RenderSettings.fogColor == fogColor && Mathf.Approximately(RenderSettings.fogDensity, density), "Game fog or ambient state was not restored in the lobby.");
                Require(camera.backgroundColor == background && Mathf.Approximately(camera.fieldOfView, fov) &&
                    Mathf.Approximately(rig.pitch, pitch) && Mathf.Approximately(rig.yaw, yaw) && Mathf.Approximately(rig.distance, distance), "Game camera framing leaked into the lobby.");
                Require(data.renderPostProcessing == post && data.volumeLayerMask == volumeMask && data.antialiasing == aa, "Game post-processing state leaked into the lobby.");
                Require(lights.All(s => s.Key && s.Key.enabled == s.Value) && volumes.All(s => s.Key && s.Key.enabled == s.Value), "Original lights or volumes were not re-enabled exactly.");
                Require(!GameObject.Find("Game theme runtime atmosphere"), "Game runtime environment object survived return to lobby.");
            }
        }

        static GateMissionValidation()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("AC Roguelike/Liminal/Validate Gate Missions and Lobby UI")]
        public static void Run() => BeginValidation(false);

        /// <summary>Focused batch entry point: each contract is the first departure in a freshly loaded scene.</summary>
        public static void RunRouteChecks() => BeginValidation(true);

        static void BeginValidation(bool routesOnly)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save or discard the open scene yourself before running validation.");
            string[] args = Environment.GetCommandLineArgs();
            int outputArg = Array.IndexOf(args, "-gateValidationOutput");
            string output = outputArg >= 0 && outputArg + 1 < args.Length ? args[outputArg + 1] :
                routesOnly ? "Documentation/Liminal/GateRouteValidation" : "Documentation/Liminal/GateMissionValidation";
            SessionState.SetString(Prefix + "Output", Path.GetFullPath(output));
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var catalog = Resources.Load<GateMissionCatalog>("GateMissions");
            Require(catalog && catalog.Missions.Count >= 5, "At least five gate missions are required.");
            Require(catalog.Missions.All(m => m != null && m.id != "digital_suppression" && m.destinationId != "digital" &&
                m.stages != null && m.stages.All(s => s && s.stageId != "concept_digital")),
                "The program prison theme must not appear in the playable mission catalog.");
            var keys = new HashSet<string> { "Hunter.MagicStones" };
            foreach (HunterProgress.Upgrade upgrade in Enum.GetValues(typeof(HunterProgress.Upgrade))) keys.Add("Hunter.Upgrade." + upgrade);
            foreach (var mission in catalog.Missions)
            {
                keys.Add(HunterProgress.DestinationDiscoveryKey(mission.destinationId));
                foreach (var stage in mission.stages) keys.Add(HunterProgress.StageDiscoveryKey(stage.stageId));
            }
            var snapshot = new Snapshot();
            foreach (var key in keys) snapshot.values.Add(new Pref { key = key, existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetInt(key) });
            SessionState.SetString(Prefix + "Prefs", JsonUtility.ToJson(snapshot));
            SessionState.SetString(Prefix + "Report", JsonUtility.ToJson(report));
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "Finished", false);
            SessionState.SetBool(Prefix + "RoutesOnly", routesOnly);
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.SetInt("Hunter.MagicStones", 99999);
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Prefix + "Active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                report = JsonUtility.FromJson<Report>(SessionState.GetString(Prefix + "Report", "{}"));
                started = EditorApplication.timeSinceStartup;
                routine = SessionState.GetBool(Prefix + "RoutesOnly", false) ? ValidateFreshDepartures() : Validate();
            }
            if (mode == PlayModeStateChange.EnteredEditMode)
            {
                RestorePrefs();
                if (report == null) report = JsonUtility.FromJson<Report>(SessionState.GetString(Prefix + "Report", "{}"));
                if (!SessionState.GetBool(Prefix + "Finished", false)) report.errors.Add("Play Mode exited before validation finished.");
                report.status = report.errors.Count == 0 ? "passed" : "failed";
                Save();
                SessionState.SetBool(Prefix + "Active", false);
                if (Application.isBatchMode) EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
            }
        }

        static void Update()
        {
            if (routine == null || !EditorApplication.isPlaying) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 110) throw new TimeoutException("Gate validation exceeded 110 seconds.");
                if (!routine.MoveNext()) Finish();
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); Finish(); }
        }

        static IEnumerator Validate()
        {
            LiminalRunDirector run = null;
            for (int i = 0; i < 90; i++)
            {
                run = UnityEngine.Object.FindFirstObjectByType<LiminalRunDirector>();
                if (run && run.Lobby && run.Phase == LiminalRunPhase.Lobby) break;
                yield return null;
            }
            Require(run && run.Lobby && run.Phase == LiminalRunPhase.Lobby, "LiminalRun must initialize in the lobby.");
            var missions = run.AvailableMissions.ToArray();
            Require(missions.Length >= 5 && missions.All(m => m.IsAvailable && m.DisplayDestination == "???"), "All unvisited destinations must be ???.");
            var font = HunterUi.CreateFont(null, out var source);
            Require(font && font.atlasPopulationMode == AtlasPopulationMode.Static, "Missing scene font must use the bundled static Korean main.");
            Require(font.HasCharacters("강화 게이트 미션 난이도 적 변이 숲 감옥 폐허 동굴 ???", out uint[] missing, true, true), "Korean font fallback has missing characters.");
            string missionCorpus = string.Join(" ", missions.Select(m => m.title + " " + m.destinationName + " " + m.briefing + " " + m.gimmickName + " " + m.gimmickDescription + " " + m.objective));
            Require(font.HasCharacters(missionCorpus, out uint[] missionMissing, true, true), "Actual mission catalog text has missing glyphs: " + string.Join(",", missionMissing ?? Array.Empty<uint>()));
            HunterUi.ReleaseFont(font, source);
            report.checks.Add("All available missions start undiscovered; bundled font loads without a scene reference.");

            BeginCapture(run, 1280, 720);
            Invoke(run.Lobby, "OpenGate");
            Require(!GameObject.Find("Mission_digital_suppression"), "The retired prison mission must not have a lobby card.");
            foreach (var mission in missions)
            {
                Invoke(run.Lobby, "SelectMission", mission.id);
                yield return null;
                Require(!mission.IsDiscovered, "Browsing a mission must not discover its destination.");
                CheckWindow();
            }
            for (int i = 0; i < 3; i++) yield return null;
            Capture("gate-undiscovered-1280x720");
            Invoke(run.Lobby, "CloseWindow");

            for (int cycle = 0; cycle < 4; cycle++)
            {
                Invoke(run.Lobby, "OpenUpgrades");
                yield return null;
                CheckWindow();
                var window = GameObject.Find("LobbyWindow");
                int instance = window.GetInstanceID();
                int before = HunterProgress.Level(HunterProgress.Upgrade.Vitality);
                var buy = window.GetComponentsInChildren<UnityEngine.UI.Button>().First(b => b.name == "Buy0");
                buy.onClick.Invoke();
                yield return null;
                Require(HunterProgress.Level(HunterProgress.Upgrade.Vitality) == before + 1, "Upgrade purchase did not update progression.");
                Require(GameObject.Find("LobbyWindow").GetInstanceID() == instance, "Buying an upgrade should update the existing window.");
                Require(UnityEngine.Object.FindObjectsByType<CanvasRenderer>(FindObjectsSortMode.None).Count(r => r.gameObject.name == "LobbyWindow") == 1,
                    "Multiple active lobby windows overlap after purchasing.");
                CheckWindow();
                if (cycle == 3) Capture("upgrades-1280x720");
                Invoke(run.Lobby, "CloseWindow");
                yield return null;
            }
            report.checks.Add("Repeated upgrade purchases update labels in place; no overlapping active windows or missing/clipped text.");

            ResizeCapture(1024, 768);
            Invoke(run.Lobby, "OpenUpgrades");
            for (int i = 0; i < 3; i++) yield return null;
            CheckWindow(); Capture("upgrades-1024x768");
            Invoke(run.Lobby, "OpenGate");
            for (int i = 0; i < 3; i++) yield return null;
            CheckWindow(); Capture("gate-undiscovered-1024x768");
            Invoke(run.Lobby, "CloseWindow");
            ResizeCapture(1280, 720);
            Require(!run.EnterMission("missing-mission") && run.Phase == LiminalRunPhase.Lobby,
                "An invalid mission must leave the player safely in the lobby.");
            Require(!run.EnterMission("digital_suppression") && run.Phase == LiminalRunPhase.Lobby && run.ActiveMission == null,
                "The retired prison mission must not be enterable by its old mission id.");
            report.checks.Add("Program prison is absent from the mission catalog and lobby; its old mission id cannot enter a dungeon.");

            foreach (var mission in missions)
            {
                bool game = mission.id == "game_exhibition";
                EnvironmentSnapshot beforeGame = game ? EnvironmentSnapshot.Take() : null;
                int previousSeed = run.seed;
                Require(run.EnterMission(mission.id), "Mission entry failed: " + mission.id + ": " + run.LastMissionError);
                yield return null;
                Require(run.seed != previousSeed, "Mission entry reused its previous seed: " + mission.id);
                Require(run.ActiveMission == mission && run.CurrentStage == mission.stages[0], "Selected mission stage was not loaded.");
                var expected = mission.stages[0].ChooseRoute(run.seed, 0);
                Require(run.Rooms.Select(r => r.roomId).SequenceEqual(expected.Select(r => r.roomId)), "Loaded rooms differ from the selected mission route.");
                CheckActiveDeparture(run, mission);
                Require(mission.IsDiscovered && mission.DisplayDestination == mission.destinationName, "Successful arrival must discover the destination.");
                Require(HunterProgress.IsStageDiscovered(mission.stages[0].stageId), "Loaded stage was not discovered.");
                foreach (var unseen in mission.stages.Skip(1)) Require(!HunterProgress.IsStageDiscovered(unseen.stageId), "Unvisited later stage was exposed.");
                if (game)
                {
                    CheckGameEnvironment();
                    for (int i = 0; i < 12; i++) yield return null;
                    Capture("G06-game-arrival-1280x720");
                }
                var dummy = new GameObject("GateValidationEnemy");
                dummy.transform.position = run.player.position + Vector3.right * 20;
                var enemy = dummy.AddComponent<TrainingEnemy>();
                enemy.Configure(100, false);
                var modifier = dummy.AddComponent<GateMissionEnemyModifier>();
                modifier.Apply(enemy, mission, run.PlayerHealth, run);
                int maximum = Mathf.RoundToInt(100 * mission.healthMultiplier);
                Require(enemy.maxHealth == maximum && enemy.Health == maximum, "Mission health multiplier is incorrect.");
                modifier.Apply(enemy, mission, run.PlayerHealth, run);
                Require(enemy.maxHealth == maximum, "Mission modifier was applied more than once.");
                enemy.TakeDamage(20);
                int damage = (mission.enemyGimmicks & GateEnemyGimmick.Armor) != 0 ? 15 : 20;
                Require(enemy.Health == maximum - damage, "Armor damage reduction differs from the briefing.");
                if ((mission.enemyGimmicks & GateEnemyGimmick.DeathBurst) != 0)
                {
                    dummy.transform.position = run.player.position;
                    int healthBeforeBurst = run.PlayerHealth.Health;
                    enemy.TakeDamage(10000);
                    yield return null;
                    Require(GameObject.Find("MissionDeathBurstWarning"), "Death burst warning was not created.");
                    float waitUntil = Time.time + GateMissionEnemyModifier.BurstDelay + .4f;
                    while (Time.time < waitUntil) yield return null;
                    Require(run.PlayerHealth.Health == healthBeforeBurst - GateMissionEnemyModifier.BurstDamage,
                        "Standing in the death burst must apply the advertised 14 damage.");
                }
                UnityEngine.Object.Destroy(dummy);
                int combatRoom = -1;
                for (int i = 0; i < run.Rooms.Count; i++)
                    if (run.Rooms[i].kind == LiminalRoomKind.Combat) { combatRoom = i; break; }
                if (combatRoom >= 0)
                {
                    Invoke(run, "ActivateRoom", combatRoom);
                    var enemies = run.Rooms[combatRoom].GetComponentsInChildren<TrainingEnemy>();
                    Require(enemies.Length > 0 && enemies.All(e => !e.IsAlive || e.GetComponent<GateMissionEnemyModifier>()),
                        "Actual spawned and authored enemies must receive the mission modifier.");
                    if (game)
                    {
                        Require(enemies.All(e => e.GetComponent<GameVoxelMonster>()), "Game mission spawned a non-voxel monster.");
                        run.PlayerHealth.GrantInvulnerability(5);
                        run.player.GetComponent<PlayerMotor>().ResetAt(run.Rooms[combatRoom].playerSpawn.position + Vector3.up * .05f);
                        Camera.main.GetComponent<IsometricFollowCamera>().Snap();
                        for (int i = 0; i < 16; i++) yield return null;
                        Capture("G06-game-combat-1280x720");
                    }
                }
                int retrySeed = run.seed;
                run.RetryMission();
                Require(run.seed != retrySeed, "Mission retry reused its previous seed: " + mission.id);
                Require(run.ActiveMission == mission && run.CurrentStage == mission.stages[0], "Retry must keep the selected mission.");
                if (game) CheckGameEnvironment();
                run.ReturnToLobby();
                yield return null;
                Require(run.Phase == LiminalRunPhase.Lobby, "Return to lobby failed.");
                Require(!GameObject.Find("MissionDeathBurstWarning"), "Death burst warning leaked into the lobby.");
                if (game)
                {
                    beforeGame.RequireRestored();
                    report.checks.Add("G06 lobby entry uses Game fog/post-processing/lighting/camera; retry keeps exactly one lease; return restores original scene state and removes the temporary environment.");
                }
                report.checks.Add(mission.id + ": route, discovery, health/armor, enemy registration, retry and return verified.");
            }
            Invoke(run.Lobby, "OpenGate");
            Invoke(run.Lobby, "SelectMission", missions[missions.Length - 1].id);
            for (int i = 0; i < 3; i++) yield return null;
            CheckWindow(); Capture("gate-discovered-1280x720");
            Invoke(run.Lobby, "CloseWindow");
            report.checks.Add("Mission and upgrade dialogs checked at 1280x720 and 1024x768; screenshots captured.");
        }

        static IEnumerator ValidateFreshDepartures()
        {
            var catalog = Resources.Load<GateMissionCatalog>("GateMissions");
            Require(catalog, "Missing gate mission catalog.");
            var missions = catalog.Missions.Where(m => m != null && m.IsAvailable).ToArray();
            foreach (var mission in missions)
            {
                Require(mission.StageCount == 1 && mission.RoomCount == 4,
                    mission.id + ": a playable mission must contain three random rooms and its fixed final room.");
                var stage = mission.stages[0];
                var pool = new HashSet<LiminalRoom>(stage.roomPool.Where(room => room));
                var visited = new HashSet<LiminalRoom>();
                var firstRooms = new HashSet<LiminalRoom>();
                var routes = new HashSet<string>();
                for (int seed = 1; seed <= 128; seed++)
                {
                    var route = stage.ChooseRoute(seed, 0);
                    string signature = string.Join("|", route.Select(room => room.roomId));
                    Require(signature == string.Join("|", stage.ChooseRoute(seed, 0).Select(room => room.roomId)),
                        mission.id + ": explicit seeds must remain reproducible.");
                    Require(route.Length == 4 && route.Distinct().Count() == 4 && route[0].kind == LiminalRoomKind.Combat &&
                        route[3] == stage.endRoom && !route.Contains(stage.startRoom) && route.Take(3).All(pool.Contains),
                        mission.id + ": route must begin in combat, exclude the old arrival, select unique variations, and preserve the final room.");
                    visited.UnionWith(route.Take(3)); firstRooms.Add(route[0]); routes.Add(signature);
                }
                Require(visited.SetEquals(pool) && routes.Count > 1,
                    mission.id + ": all ordinary variations must remain reachable across different seeds.");
                Require(pool.Count(room => room.kind == LiminalRoomKind.Combat) < 2 || firstRooms.Count > 1,
                    mission.id + ": the first combat room must vary when multiple combat candidates exist.");
                report.checks.Add(mission.id + ": 128 reproducible seeds, " + routes.Count + " routes, all " + pool.Count +
                    " candidates reached, random first combat room, no arrival or repeated room, fixed final room.");
            }

            for (int index = 0; index < missions.Length; index++)
            {
                if (index > 0)
                {
                    EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
                    yield return null;
                }
                LiminalRunDirector run = null;
                for (int frame = 0; frame < 180; frame++)
                {
                    run = UnityEngine.Object.FindFirstObjectByType<LiminalRunDirector>();
                    if (run && run.Lobby && run.Phase == LiminalRunPhase.Lobby) break;
                    yield return null;
                }
                Require(run && run.Lobby && run.Phase == LiminalRunPhase.Lobby && run.Rooms.Count == 0,
                    "Fresh scene must initialize in the lobby before each mission's first departure.");
                // Reloading a scene can deserialize a fresh catalog and new managed mission definitions.
                // Resolve by stable id, then check identity against the newly loaded catalog.
                string missionId = missions[index].id;
                var mission = run.AvailableMissions.Single(candidate => candidate.id == missionId);
                bool game = mission.id == "game_exhibition";
                var beforeGame = game ? EnvironmentSnapshot.Take() : null;
                int initialSeed = run.seed;
                Require(run.EnterMission(mission.id), "First mission departure failed: " + mission.id + ": " + run.LastMissionError);
                Require(run.seed != initialSeed, mission.id + ": first departure reused the serialized seed.");
                CheckActiveDeparture(run, mission);
                yield return null;
                if (game) CheckGameEnvironment();
                int retrySeed = run.seed;
                run.RetryMission();
                Require(run.seed != retrySeed, mission.id + ": retry reused its previous seed.");
                CheckActiveDeparture(run, mission);
                yield return null;
                if (game) CheckGameEnvironment();
                run.ReturnToLobby();
                yield return null;
                Require(run.Phase == LiminalRunPhase.Lobby && run.Rooms.Count == 0, mission.id + ": return left a route active.");
                if (game) beforeGame.RequireRestored();
                report.checks.Add(mission.id + ": fresh-scene first departure and retry choose new seeds, activate first-room enemies and gates, preserve final room, and return to lobby" +
                    (game ? "; game atmosphere has exactly one lease and restores on return." : "."));
            }
        }

        static void CheckActiveDeparture(LiminalRunDirector run, GateMissionDefinition mission)
        {
            Require(run.ActiveMission == mission && run.CurrentStage == mission.stages[0] && run.Phase == LiminalRunPhase.Exploring,
                mission.id + ": wrong active mission or phase.");
            var expected = mission.stages[0].ChooseRoute(run.seed, 0);
            Require(run.Rooms.Count == 4 && run.Rooms.Select(room => room.roomId).SequenceEqual(expected.Select(room => room.roomId)),
                mission.id + ": live route differs from its four-room seeded route.");
            Require(run.Rooms[3].roomId == mission.stages[0].endRoom.roomId &&
                run.Rooms.All(room => !mission.stages[0].startRoom || room.roomId != mission.stages[0].startRoom.roomId),
                mission.id + ": fixed final room changed or the removed arrival returned.");
            var first = run.Rooms[0];
            Require(run.ActiveRoomIndex == 0 && first.kind == LiminalRoomKind.Combat && run.ClearedRoomCount == 0 && run.LivingEnemyCount > 0,
                mission.id + ": first room must immediately begin combat with living enemies.");
            Require(first.entranceGate && first.entranceGate.activeSelf && first.exitGate && first.exitGate.activeSelf && !run.TryUseExit(),
                mission.id + ": first combat room must lock both gates until enemies are defeated.");
            var enemies = first.GetComponentsInChildren<TrainingEnemy>().Where(enemy => enemy.IsAlive).ToArray();
            Require(enemies.Length == run.LivingEnemyCount && enemies.All(enemy => enemy.GetComponent<GateMissionEnemyModifier>()),
                mission.id + ": first-room enemies were not counted or given mission modifiers.");
        }

        static object Invoke(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == method && m.GetParameters().Length == args.Length);
            return info.Invoke(target, args);
        }

        static void CheckGameEnvironment()
        {
            Require(GameThemeAtmosphere.IsApplied && GameThemeAtmosphere.LeaseCount == 1, "Game mission needs exactly one atmosphere lease.");
            Require(RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared &&
                RenderSettings.fogColor == new Color(.13f, .19f, .27f) && Mathf.Approximately(RenderSettings.fogDensity, .006f), "Game mission fog differs from the standalone scene.");
            var camera = Camera.main; var rig = camera.GetComponent<IsometricFollowCamera>();
            Require(Mathf.Approximately(rig.pitch, 55) && Mathf.Approximately(rig.yaw, 35) && Mathf.Approximately(rig.distance, 24) && Mathf.Approximately(camera.fieldOfView, 36), "Game mission camera differs from the authored 55/35/24/FOV36 framing.");
            var environment = GameObject.Find("Game theme runtime atmosphere");
            Require(environment && environment.GetComponentsInChildren<Light>().Count(l => l.enabled && l.type == LightType.Directional) == 2, "Game key and fill lights missing.");
            var volume = environment.GetComponent<Volume>();
            Require(volume && volume.enabled && volume.isGlobal && volume.sharedProfile && volume.sharedProfile.TryGet<Bloom>(out var bloom) &&
                Mathf.Approximately(bloom.intensity.value, .25f), "Game post-processing profile missing or incorrect.");
        }

        static void CheckWindow()
        {
            Canvas.ForceUpdateCanvases();
            var window = GameObject.Find("LobbyWindow");
            Require(window, "Lobby window is not active.");
            foreach (var text in window.GetComponentsInChildren<TextMeshProUGUI>())
            {
                text.ForceMeshUpdate();
                string value = new string(text.GetParsedText().Where(c => !char.IsControl(c)).ToArray());
                Record(!value.Contains("\ufffd"), "Replacement character in " + text.name);
                Record(text.font.HasCharacters(value, out uint[] missing, true, true), "Missing glyphs in " + text.name + ": " + value);
                Record(!text.isTextOverflowing && !text.isTextTruncated, "Text overflow in " + text.name + ": " + value);
            }
        }

        static void BeginCapture(LiminalRunDirector run, int width, int height)
        {
            captureCamera = Camera.main;
            captureCanvas = run.GetComponent<LiminalHud>().Canvas.GetComponent<Canvas>();
            Require(captureCamera && captureCanvas, "Camera and HUD canvas are required for screenshots.");
            previousTarget = captureCamera.targetTexture;
            previousMode = captureCanvas.renderMode;
            previousCanvasCamera = captureCanvas.worldCamera;
            previousPlane = captureCanvas.planeDistance;
            captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            captureCanvas.worldCamera = captureCamera;
            captureCanvas.planeDistance = captureCamera.nearClipPlane + .1f;
            ResizeCapture(width, height);
        }

        static void ResizeCapture(int width, int height)
        {
            if (captureTexture) { captureCamera.targetTexture = null; captureTexture.Release(); UnityEngine.Object.Destroy(captureTexture); }
            captureTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            captureTexture.Create();
            captureCamera.targetTexture = captureTexture;
            captureCamera.aspect = width / (float)height;
        }

        static void Capture(string name)
        {
            var old = RenderTexture.active;
            var image = new Texture2D(captureTexture.width, captureTexture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = captureTexture;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                string path = Path.Combine(SessionState.GetString(Prefix + "Output", ""), name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                report.screenshots.Add(path);
            }
            finally { RenderTexture.active = old; UnityEngine.Object.Destroy(image); }
        }

        static void Finish()
        {
            routine = null;
            if (captureCamera) { captureCamera.targetTexture = previousTarget; captureCamera.ResetAspect(); }
            if (captureCanvas) { captureCanvas.renderMode = previousMode; captureCanvas.worldCamera = previousCanvasCamera; captureCanvas.planeDistance = previousPlane; }
            if (captureTexture) { captureTexture.Release(); UnityEngine.Object.Destroy(captureTexture); }
            RestorePrefs();
            SessionState.SetBool(Prefix + "Finished", true);
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            Save();
            EditorApplication.isPlaying = false;
        }

        static void RestorePrefs()
        {
            string json = SessionState.GetString(Prefix + "Prefs", "");
            if (string.IsNullOrEmpty(json)) return;
            foreach (var item in JsonUtility.FromJson<Snapshot>(json).values)
                if (item.existed) PlayerPrefs.SetInt(item.key, item.value); else PlayerPrefs.DeleteKey(item.key);
            PlayerPrefs.Save();
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (report != null && SessionState.GetBool(Prefix + "Active", false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                report.errors.Add(message + "\n" + stack);
        }

        static void Save()
        {
            string json = JsonUtility.ToJson(report, true);
            SessionState.SetString(Prefix + "Report", json);
            File.WriteAllText(Path.Combine(SessionState.GetString(Prefix + "Output", ""), "validation.json"), json);
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Record(bool condition, string message) { if (!condition && !report.errors.Contains(message)) report.errors.Add(message); }
    }
}
