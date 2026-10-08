#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Batch-only live combat regression: timing, committed aim, swept bolts, pause and death cleanup.</summary>
    [InitializeOnLoad]
    public static class GameThemeMonsterCombatValidation
    {
        const string Active = "GameTheme.MonsterCombatValidation";
        const string Folder = "Documentation/GameTheme/MonsterValidation";
        [Serializable] sealed class Report
        {
            public string status = "running", utc;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> captures = new List<string>();
        }
        sealed class Wait
        {
            public Func<bool> condition;
            public float timeout;
            public string label;
        }
        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static double deadline, started;
        static Camera camera;
        static LiminalRoom room;
        static LiminalPlayerHealth player;

        static GameThemeMonsterCombatValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    report = new Report { utc = DateTime.UtcNow.ToString("O") };
                    started = EditorApplication.timeSinceStartup;
                    routine = Run();
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.SetBool(Active, false);
                    EditorApplication.Exit(SessionState.GetInt(Active + ".exit", 1));
                }
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch is reserved for an isolated batch editor; it never replaces an interactive scene.");
            GameThemeMonsterBuilder.ValidateBuiltAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 150) throw new TimeoutException("Monster validation exceeded 150 seconds.");
                if (waiting != null)
                {
                    if (!waiting.condition())
                    {
                        if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(waiting.label);
                        return;
                    }
                    waiting = null;
                }
                if (!routine.MoveNext()) { Finish(true, null); return; }
                if (routine.Current is Wait wait)
                {
                    waiting = wait;
                    deadline = EditorApplication.timeSinceStartup + wait.timeout;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        static Wait Until(Func<bool> condition, float timeout, string label) => new Wait { condition = condition, timeout = timeout, label = label };
        static Wait Seconds(float value)
        {
            float end = Time.time + value;
            return Until(() => Time.time >= end, value * 4 + 4, "Wait for game time " + value);
        }
        static Wait RealSeconds(double value)
        {
            double end = EditorApplication.timeSinceStartup + value;
            return Until(() => EditorApplication.timeSinceStartup >= end, (float)value + 2, "Wait for pause observation");
        }
        static void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text);
            report.checks.Add(text);
        }

        static IEnumerator Run()
        {
            SetupArena();
            foreach (GameVoxelRole role in Enum.GetValues(typeof(GameVoxelRole)))
            {
                PlacePlayer(new Vector3(0, .05f, -(role == GameVoxelRole.StackGuardian ? 2.8f : role == GameVoxelRole.BitSentry ? 8 : 4)));
                var monster = GameVoxelMonster.Create(role, new Vector3(0, .05f, 0), Quaternion.Euler(0, 180, 0), room.transform);
                monster.Setup(player, room, 0);
                Vector3 tinyStepStart = monster.transform.position;
                monster.GetComponent<CharacterController>().Move(Vector3.right * .00001f);
                Check(monster.transform.position.x - tinyStepStart.x > .000001f,
                    role + ": submillimetre controller movement is not discarded at high frame rates.");
                int before = player.Health;
                Capture(role + "_Idle", monster.transform.position);
                yield return Until(() => monster.IsWindingUp && (role == GameVoxelRole.BitSentry || monster.TelegraphVisible),
                    12, role + " initial windup");
                Check(monster.TelegraphVisible == (role != GameVoxelRole.BitSentry) && player.Health == before,
                    role + ": only body attacks show a floor outline before damage.");
                yield return Seconds(.55f);
                Check(monster.TelegraphVisible == (role != GameVoxelRole.BitSentry),
                    role + ": projectile anticipation stays free of floor and trajectory previews.");
                Capture(role + "_Warning", monster.transform.position);
                Vector3 locked = monster.LockedDirection;
                PlacePlayer(player.transform.position + Vector3.right * 5);
                yield return Seconds(.1f);
                Check(Vector3.Angle(locked, monster.LockedDirection) < .1f, role + ": committed aim does not chase a late sidestep.");
                Time.timeScale = 0;
                yield return RealSeconds(.05);
                Vector3 position = monster.transform.position;
                Quaternion pose = monster.transform.Find("Visual/Pose").localRotation;
                int attacks = monster.Attacks;
                yield return RealSeconds(.3);
                Check(Vector3.Distance(position, monster.transform.position) < .001f && Quaternion.Angle(pose, monster.transform.Find("Visual/Pose").localRotation) < .01f && attacks == monster.Attacks,
                    role + ": pausing freezes movement, pose and attack timing.");
                Time.timeScale = 1;
                yield return Until(() => monster.AttackCueVisible, 2, role + " pre-attack glint");
                Check(player.Health == before, role + ": the face or body glint is visible before attack damage.");
                yield return Until(() => monster.State == GameVoxelState.Recover, 8, role + " attack recovery");
                Check(player.Health == before, role + ": sidestepping the committed warning avoids damage.");
                if (role == GameVoxelRole.BitSentry) Check(monster.Shots == 3, "BitSentry: exactly three staggered bolts per volley.");

                // Repeat with the player deliberately staying inside the committed attack area.
                Vector3 strikePosition = monster.transform.position + monster.transform.forward * (role == GameVoxelRole.StackGuardian ? 2.5f : role == GameVoxelRole.BitSentry ? 7 : 4);
                PlacePlayer(strikePosition);
                yield return Until(() => player.Health < before, 15, role + " damages the stationary target");
                Check(player.Health < before, role + ": the real attack hits a stationary target in its committed area.");
                HitFeedback.CancelHitStop();
                monster.enabled = false;
                foreach (var bolt in Object.FindObjectsByType<GamePixelBolt>(FindObjectsSortMode.None)) Object.Destroy(bolt.gameObject);
                yield return Seconds(.15f);
                PlacePlayer(new Vector3(0, .05f, 7));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetPositionAndRotation(new Vector3(0, 1.5f, 3), Quaternion.identity);
                wall.transform.localScale = new Vector3(6, 3, .3f);
                Physics.SyncTransforms();
                before = player.Health;
                GamePixelBolt.Fire(new Vector3(0, .85f, 0), Vector3.forward, monster.Health, player, room, 10);
                yield return Seconds(1);
                Check(player.Health == before && Object.FindObjectsByType<GamePixelBolt>(FindObjectsSortMode.None).Length == 0,
                    role + ": a swept pixel bolt stops at a solid wall.");
                Object.Destroy(wall);
                yield return Seconds(.1f);
                GamePixelBolt.Fire(new Vector3(-8, .85f, 0), Vector3.forward, monster.Health, player, room, 10);
                yield return Seconds(.03f);
                var pausedBolt = Object.FindFirstObjectByType<GamePixelBolt>();
                Check(pausedBolt, role + ": bolt exists for the pause test.");
                Time.timeScale = 0;
                yield return RealSeconds(.05);
                Vector3 boltPosition = pausedBolt.transform.position;
                yield return RealSeconds(.25);
                Check(pausedBolt && Vector3.Distance(boltPosition, pausedBolt.transform.position) < .001f,
                    role + ": pausing freezes the projectile sweep.");
                Time.timeScale = 1;
                monster.enabled = true;
                PlacePlayer(monster.transform.position + monster.transform.forward * 2.8f);
                yield return Until(() => monster.IsWindingUp, 12, role + " death during anticipation");
                attacks = monster.Attacks;
                monster.Health.TakeDamage(monster.Health.maxHealth * 100);
                Check(!monster.TelegraphVisible && !monster.AttackCueVisible && !monster.GetComponent<CharacterController>().enabled,
                    role + ": death immediately cancels the outline, glint and collision.");
                yield return Seconds(.3f);
                Check(monster.Attacks == attacks && Object.FindObjectsByType<GamePixelBolt>(FindObjectsSortMode.None).Length == 0,
                    role + ": death prevents pending attacks and removes owned bolts.");
                yield return Until(() => monster.DeathFinished, 10, role + " death finish");
                bool hidden = true;
                foreach (var renderer in monster.Health.visibleRenderers) if (renderer && renderer.enabled) hidden = false;
                Check(hidden, role + ": knockback death finishes with every body renderer hidden.");
                Object.Destroy(monster.gameObject);
                yield return Seconds(.1f);
            }
        }

        static void SetupArena()
        {
            var roomObject = new GameObject("Combat Validation Arena");
            room = roomObject.AddComponent<LiminalRoom>();
            room.localBounds = new Bounds(new Vector3(0, 3, 0), new Vector3(36, 8, 36));
            roomObject.AddComponent<GameThemeRoom>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Validation Floor";
            floor.transform.SetParent(room.transform);
            floor.transform.position = Vector3.down * .15f;
            floor.transform.localScale = new Vector3(36, .3f, 36);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(.48f, .53f, .62f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var playerObject = new GameObject("Validation Player");
            var body = playerObject.AddComponent<CharacterController>();
            body.height = 1.8f; body.radius = .32f; body.center = Vector3.up * .9f;
            player = playerObject.AddComponent<LiminalPlayerHealth>();
            player.SetMaximum(10000, true);
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 36; camera.farClipPlane = 80; camera.backgroundColor = new Color(.09f, .11f, .17f);
            cameraObject.AddComponent<AudioListener>();
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .6f, .72f);
            Physics.SyncTransforms();
        }

        static void PlacePlayer(Vector3 position)
        {
            var body = player.GetComponent<CharacterController>();
            body.enabled = false; player.transform.position = position; body.enabled = true;
            Physics.SyncTransforms();
        }

        static void Capture(string name, Vector3 subject)
        {
            Directory.CreateDirectory(Folder);
            Quaternion angle = Quaternion.Euler(55, 35, 0);
            camera.transform.SetPositionAndRotation(subject + Vector3.up * .65f + Vector3.back * 1.3f + angle * Vector3.back * 12, angle);
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            report.captures.Add(path);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.Destroy(texture); Object.Destroy(target);
        }

        static void Finish(bool passed, string error)
        {
            routine = null;
            waiting = null;
            Time.timeScale = 1;
            report.status = passed ? "passed" : "failed";
            if (error != null) report.errors.Add(error);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/combat-validation.json", JsonUtility.ToJson(report, true));
            Debug.Log("GAME_MONSTER_COMBAT_VALIDATION " + report.status + (error == null ? "" : ": " + error));
            SessionState.SetInt(Active + ".exit", passed ? 0 : 1);
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
