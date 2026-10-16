using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    // Opt-in packaged-player check. Normal demo launches never create this component.
    public sealed class DemoPlayerSmokeTest : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public string status = "running", utc;
            public List<string> checks = new List<string>(), errors = new List<string>();
        }
        readonly Dictionary<string, string> savedStrings = new Dictionary<string, string>();
        readonly Dictionary<string, int?> savedInts = new Dictionary<string, int?>();
        readonly string[] missions = { "liminal_campaign", "game_exhibition" };
        readonly Report report = new Report();
        LiminalRunDirector run;
        float deadline, nextStep, lobbyReadyAt = -1;
        int step;
        bool finished;
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "-demoSmokeTest") < 0) return;
            var host = new GameObject("Demo packaged-player validation");
            DontDestroyOnLoad(host);
            host.AddComponent<DemoPlayerSmokeTest>();
        }

        void Awake()
        {
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (string key in new[] { "Hunter.Loadout.Hunter", "Hunter.Loadout.Support" })
                savedStrings[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            foreach (string key in new[] { "Hunter.MagicStones", "Hunter.Destination.liminal", "Hunter.Destination.game", "Hunter.VisitedStage.liminal_1", "Hunter.VisitedStage.concept_game" })
                savedInts[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            report.utc = DateTime.UtcNow.ToString("O");
            deadline = Time.realtimeSinceStartup + 60;
            Application.logMessageReceived += OnLog;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                report.errors.Add(message + "\n" + stack);
        }

        void Update()
        {
            if (finished) return;
            try
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Packaged demo startup/mission check timed out at step " + step);
                if (Time.realtimeSinceStartup < nextStep) return;
                if (step == 0)
                {
                    run = FindFirstObjectByType<LiminalRunDirector>();
                    if (!run || !run.Lobby || run.Phase != LiminalRunPhase.Lobby) return;
                    if (lobbyReadyAt < 0) { lobbyReadyAt = Time.realtimeSinceStartup + .5f; return; }
                    if (Time.realtimeSinceStartup < lobbyReadyAt) return;
                    Require(run.Roster && run.Roster.Count == 1 && run.Roster.CurrentId == "proi" && run.Roster.WearsLobbyForm, "Packaged player starts as everyday Proi in the lobby.");
                    Require(run.AvailableMissions.Count == 2 && SupportCharacterCatalog.All.Count == 2, "Packaged player includes two missions and two supports.");
                    var hud = run.GetComponent<LiminalHud>();
                    Require(hud && hud.Font && hud.Font.HasCharacters("프로이 유니 꼬끼오 강화 게이트", out uint[] missing, true, true), "Packaged Korean UI font renders demo labels.");
                    Capture("smoke-lobby.png");
                    step = 1; nextStep = Time.realtimeSinceStartup + 1;
                }
                else if (step <= 2)
                {
                    string mission = missions[step - 1];
                    Require(run.EnterMission(mission), "Packaged mission loads: " + mission);
                    Require(run.Phase == LiminalRunPhase.Exploring && run.Rooms.Count == 4 && run.LivingEnemyCount > 0 && !run.Roster.WearsLobbyForm,
                        "Packaged mission starts combat in Proi's battle form: " + mission);
                    step += 2; nextStep = Time.realtimeSinceStartup + 1;
                }
                else if (step == 3 || step == 4)
                {
                    Capture("smoke-" + missions[step - 3] + ".png");
                    run.ReturnToLobby();
                    Require(run.Phase == LiminalRunPhase.Lobby && run.Rooms.Count == 0 && run.Roster.WearsLobbyForm, "Packaged mission returns cleanly to the lobby.");
                    step = step == 3 ? 2 : 5; nextStep = Time.realtimeSinceStartup + 1;
                }
                else Finish();
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); Finish(); }
        }

        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.checks.Add(label);
        }

        void Capture(string name)
        {
            // Batch players have no screen backbuffer. Render the actual camera and overlay UI
            // explicitly so the same check also works with a hidden player window.
            var camera = Camera.main;
            if (!camera) throw new InvalidOperationException("Missing game camera.");
            var overlays = new List<Canvas>();
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) overlays.Add(canvas);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                foreach (var canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + .1f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
            }
            finally
            {
                foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                texture.Release();
                Destroy(texture); Destroy(pixels);
            }
        }

        void Restore()
        {
            foreach (var item in savedStrings)
                if (item.Value == null) PlayerPrefs.DeleteKey(item.Key); else PlayerPrefs.SetString(item.Key, item.Value);
            foreach (var item in savedInts)
                if (!item.Value.HasValue) PlayerPrefs.DeleteKey(item.Key); else PlayerPrefs.SetInt(item.Key, item.Value.Value);
            PlayerPrefs.Save();
        }

        void Finish()
        {
            finished = true;
            Restore();
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            File.WriteAllText(Path.Combine(folder, "smoke-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("DEMO_PLAYER_SMOKE: " + report.status);
            Application.Quit(report.errors.Count == 0 ? 0 : 1);
        }

        void OnDestroy() { Application.logMessageReceived -= OnLog; Restore(); }
    }
}
