using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>
    /// Batch play-mode check of the lobby loadout, the support character 꼬끼오, Proi's basic skill and her six
    /// augments, on the real LiminalRun scene:
    /// loadout officer and terminal → loadout window cards → pick Proi (worn in the lobby, in her everyday clothes) and 꼬끼오
    /// (HUD card) → back to Astraia's everyday clothes and to Proi → gate keeps Proi (battle clothes) → 꼬끼오 drop / pickup heal / stage clear →
    /// throw, fireball, 맹화돌진 + 폭발확장 → 화염질주, 불씨수호, 반사의화염, 업화해방 offer rules.
    /// The player's saved loadout is restored afterwards. Report and captures go to Documentation/Liminal/Loadout.
    /// Unity -batchmode -projectPath . -executeMethod AcRoguelike.Liminal.Editor.LoadoutValidation.RunBatch -logFile Logs/loadout.log
    /// In the open editor: Tools > Liminal > Validate Loadout In Game (leaves the editor open).
    /// </summary>
    [InitializeOnLoad]
    public static class LoadoutValidation
    {
        const string Active = "Liminal.LoadoutValidation";
        const string ScenePath = "Assets/Liminal/Scenes/LiminalRun.unity";
        const string Folder = "Documentation/Liminal/Loadout";
        const string HunterKey = "Hunter.Loadout.Hunter", SupportKey = "Hunter.Loadout.Support";
        static readonly Vector3 Arena = new Vector3(1400, 0, 1400);

        [Serializable] sealed class Report
        {
            public string status = "running", utc;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> captures = new List<string>();
        }

        sealed class Wait { public Func<bool> condition; public float timeout; public string label; }

        static Report report;
        static IEnumerator routine;
        static Wait waiting;
        static double deadline, started;
        static string savedHunter, savedSupport;
        static bool hadHunter, hadSupport;

        static LoadoutValidation()
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
                    // a run in the open editor (RunInEditor) leaves it open
                    if (SessionState.GetBool(Active + ".live", false)) SessionState.SetBool(Active + ".live", false);
                    else EditorApplication.Exit(SessionState.GetInt(Active + ".exit", 1));
                }
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch is for an isolated batch editor.");
            Begin(false);
        }

        /// <summary>The same check in the open editor: plays LiminalRun, writes the report and leaves the editor open.</summary>
        [MenuItem("Tools/Liminal/Validate Loadout In Game")]
        public static void RunInEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Begin(true);
        }

        static void Begin(bool live)
        {
            // Start from the default party (Astraia + 유니); the player's own pick is put back at the end.
            hadHunter = PlayerPrefs.HasKey(HunterKey); savedHunter = PlayerPrefs.GetString(HunterKey, "");
            hadSupport = PlayerPrefs.HasKey(SupportKey); savedSupport = PlayerPrefs.GetString(SupportKey, "");
            SessionState.SetString(Active + ".hunter", hadHunter ? savedHunter : "\u0000");
            SessionState.SetString(Active + ".support", hadSupport ? savedSupport : "\u0000");
            PlayerPrefs.DeleteKey(HunterKey);
            PlayerPrefs.DeleteKey(SupportKey);
            PlayerPrefs.Save();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Active + ".live", live);
            SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 180) throw new TimeoutException("Loadout validation exceeded 180 seconds.");
                if (waiting != null)
                {
                    if (!waiting.condition())
                    {
                        if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(waiting.label);
                        return;
                    }
                    waiting = null;
                }
                if (!routine.MoveNext()) { Finish(null); return; }
                if (routine.Current is Wait wait) { waiting = wait; deadline = EditorApplication.timeSinceStartup + wait.timeout; }
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        static Wait Until(Func<bool> condition, float timeout, string label) => new Wait { condition = condition, timeout = timeout, label = label };
        static Wait Seconds(float value)
        {
            float end = Time.time + value;
            return Until(() => Time.time >= end, value * 4 + 4, "game time " + value);
        }

        static void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + text);
            report.checks.Add("PASS " + text);
        }

        static IEnumerator Run()
        {
            LiminalRunDirector.SuppressAugmentOffers = true;
            LiminalRunDirector run = null;
            yield return Until(() => (run = Object.FindFirstObjectByType<LiminalRunDirector>()) && run.Lobby && run.Roster && run.Phase == LiminalRunPhase.Lobby, 30, "lobby startup");
            var lobby = run.Lobby;
            var roster = run.Roster;
            var motor = run.player.GetComponent<PlayerMotor>();
            var health = run.PlayerHealth;
            var augments = run.player.GetComponent<HunterAugments>();
            var yuni = Object.FindFirstObjectByType<SupportCharacterSkill>();
            var kokkio = run.GetComponent<KokkioSupport>();
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward);

            // ---- lobby officer, terminal and window
            Check(lobby.LoadoutOfficer && lobby.Officials.Contains(lobby.LoadoutOfficer), "the loadout officer stands in the lobby");
            Check(lobby.transform.Find("MonitorScreen") && lobby.transform.Find("TerminalSign"), "the blue LCD terminal is built beside her");
            Check(kokkio && yuni, "both support characters are on the run (유니 in the scene, 꼬끼오 added by the director)");
            Check(roster.SelectedIndex == 0 && HunterLoadout.SupportId == SupportCharacterCatalog.Yuni, "default party: Astraia + 유니");
            Vector3 officer = lobby.LoadoutOfficer.transform.position;
            motor.ResetAt(officer + (run.player.position - officer).normalized * 1.6f);
            var lobbyCamera = Object.FindFirstObjectByType<IsometricFollowCamera>();
            if (lobbyCamera) lobbyCamera.Snap();
            yield return Seconds(.3f);
            Check(lobby.LoadoutOfficer.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0, "the officer is a rigged lobby character, not a stand-in");
            Capture("loadout_officer.png");
            Check(lobby.Nearest != null && lobby.Nearest.label == "헌터 편성", "walking up shows the 'E 헌터 편성' prompt");
            var astraia = motor.visual.gameObject;
            var casual = astraia.transform.Find("CasualOutfit");
            Check(casual && casual.gameObject.activeSelf, "Astraia walks the lobby in everyday clothes");
            lobby.Nearest.use();
            yield return Seconds(.2f);
            Check(lobby.LoadoutOpen && lobby.WindowOpen && !motor.enabled, "the loadout window opens and holds the hunter still");
            var window = FindWindow(run);
            Check(window, "window is on the HUD canvas");
            Button Card(string name) => Find(window, name)?.GetComponent<Button>();
            Check(Card("Hunter_" + roster.IdAt(0)) && Card("Hunter_proi") && Card("Support_yuni") && Card("Support_kokkio"),
                "cards: Astraia, Proi, 유니, 꼬끼오");
            var proiArt = Find(Find(window, "Hunter_proi"), "Illustration")?.GetComponent<RawImage>();
            var kokkioArt = Find(Find(window, "Support_kokkio"), "Illustration")?.GetComponent<RawImage>();
            Check(proiArt && proiArt.texture && kokkioArt && kokkioArt.texture, "Proi and 꼬끼오 cards show their Figma illustrations");
            Capture("loadout_window_yuni.png");

            Card("Hunter_proi").onClick.Invoke();
            Card("Support_kokkio").onClick.Invoke();
            yield return Seconds(.3f);
            var lobbyProi = motor.visual.gameObject;
            Check(roster.SelectedIndex == 1 && lobbyProi != astraia && lobbyProi.activeInHierarchy && !astraia.activeSelf && roster.WearsLobbyForm,
                "picking Proi puts her on in the lobby, in her everyday clothes");
            Check(motor.animator && motor.animator.runtimeAnimatorController && motor.animator.runtimeAnimatorController.name == "Proi_General",
                "the motor drives Proi's everyday animator");
            Check(PlayerPrefs.GetString(HunterKey) == "proi" && PlayerPrefs.GetString(SupportKey) == SupportCharacterCatalog.Kokkio, "the party is saved (proi + kokkio)");
            var kokkioCard = run.GetComponent<LiminalHud>().Canvas.Find("KokkioSupportCard");
            var yuniCard = GameObject.Find("SupportSkillCard");
            Check(kokkioCard && kokkioCard.gameObject.activeSelf && !yuniCard, "the HUD shows 꼬끼오's card instead of 유니's");
            Capture("loadout_window.png");

            Card("Hunter_" + roster.IdAt(0)).onClick.Invoke();
            yield return Seconds(.3f);
            Check(roster.SelectedIndex == 0 && motor.visual.gameObject == astraia && casual.gameObject.activeSelf, "back to Astraia: everyday clothes again");
            Card("Hunter_proi").onClick.Invoke();
            yield return Seconds(.3f);
            Check(roster.CurrentIndex == 1 && !casual.gameObject.activeInHierarchy, "and Proi again");
            Find(window, "Close").GetComponent<Button>().onClick.Invoke();
            yield return Seconds(.2f);
            Check(!lobby.WindowOpen && motor.enabled, "closing the window frees the hunter");
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward);
            if (lobbyCamera) lobbyCamera.Snap();
            yield return Seconds(.3f);
            Capture("loadout_lobby_proi.png");

            // ---- the gate keeps the pick
            run.EnterDungeon();
            yield return Until(() => run.Phase == LiminalRunPhase.Exploring, 20, "gate entered");
            var proi = motor.visual.gameObject;
            Check(roster.CurrentIndex == 1 && proi != lobbyProi && !lobbyProi.activeSelf && !roster.WearsLobbyForm,
                "the gate starts with the picked hunter (Proi), in her battle clothes");
            Check(motor.animator && motor.animator.runtimeAnimatorController && motor.animator.runtimeAnimatorController.name == "Proi", "the motor drives Proi's battle animator");
            Check(!AugmentCatalog.IsOfferable(AugmentCatalog.Find(AugmentCatalog.AutoSupport), augments, "proi"), "알아하쇼 stays out of the pool while the support is passive (꼬끼오)");

            // ---- arena far from the route (no attackers: the validation enemies are bare TrainingEnemy targets)
            run.enabled = false;
            var arena = new GameObject("LoadoutValidationArena");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(arena.transform);
            floor.transform.position = Arena + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(60, .5f, 60);
            motor.ResetAt(Arena + Vector3.up * .05f);
            motor.visual.rotation = Quaternion.LookRotation(Vector3.forward);
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            var cameraRig = Object.FindFirstObjectByType<IsometricFollowCamera>();
            if (cameraRig) cameraRig.Snap();
            Physics.SyncTransforms();
            yield return Seconds(.5f);

            // ---- 꼬끼오: a kill drops a skewer where the health hit zero; walking in heals 3 %
            kokkio.dropChance = 1;
            var victim = Enemy(arena.transform, Arena + new Vector3(3, 0, 3), 10);
            Vector3 deathSpot = victim.transform.position;
            typeof(KokkioSupport).GetMethod("OnEnemyKilled", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(kokkio, new object[] { victim });
            Check(kokkio.Skewers.Count == 1 && Vector3.Distance(Flat(kokkio.Skewers[0].transform.position), Flat(deathSpot)) < .05f, "a kill leaves a skewer at the spot where the enemy went down");
            yield return Seconds(.4f);
            Capture("kokkio_skewer.png");
            health.SetMaximum(1000, true);
            yield return Seconds(1.1f);
            Check(health.TakeDamage(200), "the hunter takes a hit before eating");
            int before = health.Health;
            motor.ResetAt(kokkio.Skewers[0].transform.position + Vector3.up * .05f);
            yield return Seconds(.2f);
            Check(kokkio.Pickups == 1 && health.Health == before + 30 && kokkio.Skewers.Count == 0, $"walking into it heals 3% of max health ({before} → {health.Health} of 1000)");
            kokkio.Drop(Arena + new Vector3(-4, 0, 4));
            kokkio.ClearSkewers();
            Check(kokkio.Skewers.Count == 0, "skewers left on the floor are cleared for the next stage");
            kokkio.dropChance = .33f;
            health.SetMaximum(health.baseMaximum, true);

            // ---- basic skill: grab and throw
            motor.ResetAt(Arena + Vector3.up * .05f);
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            yield return Seconds(.3f);
            var skill = proi.GetComponent<ProiHunterThrow>();
            Check(skill && skill.isActiveAndEnabled, "Proi wears her basic skill (ProiHunterThrow)");
            var grabbed = Enemy(arena.transform, Arena + Vector3.forward * 1.8f, 100000);
            var bystander = Enemy(arena.transform, Arena + Vector3.forward * 7.5f, 100000);
            Physics.SyncTransforms();
            yield return Seconds(.2f);
            int grabbedHealth = grabbed.Health, bystanderHealth = bystander.Health;
            Check(skill.TryActivate(), "right click starts the skill");
            yield return Seconds(.55f);
            Check(!grabbed.CanBeTargeted, "the enemy is in her hand");
            Capture("proi_skill_grab.png");
            yield return Until(() => skill.ThrowCount == 1 && grabbed.Health < grabbedHealth, 6, "thrown enemy lands and bursts");
            Check(Vector3.Distance(Flat(grabbed.transform.position), Flat(Arena)) > 3f && grabbed.CanBeTargeted, $"the enemy flew off and landed ({Vector3.Distance(Flat(grabbed.transform.position), Flat(Arena)):F1} m)");
            yield return Until(() => !skill.Busy, 3, "skill ends");
            report.checks.Add($"INFO thrown enemy took {grabbedHealth - grabbed.Health}, bystander took {bystanderHealth - bystander.Health}");
            Object.Destroy(grabbed.gameObject);
            Object.Destroy(bystander.gameObject);

            // ---- fireball with nobody to grab
            yield return Until(() => skill.CooldownRemaining <= 0, 5, "skill cooldown");
            var far = Enemy(arena.transform, Arena + Vector3.forward * 6f, 100000);
            int farHealth = far.Health;
            motor.ResetAt(Arena + Vector3.up * .05f);
            // the throw handed control back (ReleaseAutomation): aim again, or an open editor's mouse would steer the fireball
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
            Physics.SyncTransforms();
            yield return Seconds(.2f);
            Check(skill.TryActivate() && skill.FireballCount == 0, "with nobody in reach the skill forms a fireball");
            yield return Until(() => skill.FireballCount == 1, 3, "fireball thrown");
            Capture("proi_skill_fireball.png");
            yield return Until(() => far.Health < farHealth, 3, "fireball bursts on the enemy");
            yield return Until(() => !skill.Busy, 3, "skill ends");
            Object.Destroy(far.gameObject);

            // ---- 맹화돌진 + 폭발확장
            augments.Acquire(AugmentCatalog.Find(ProiAugments.BlazeRush));
            augments.Acquire(AugmentCatalog.Find(ProiAugments.BlastExpand));
            yield return Until(() => skill.CooldownRemaining <= 0, 5, "skill cooldown");
            motor.ResetAt(Arena + Vector3.up * .05f);
            // Beside the lane, so the hunter's body is not stopped by its collider while the rush still sweeps it.
            var lane = Enemy(arena.transform, Arena + new Vector3(1f, 0, 3f), 100000);
            int laneHealth = lane.Health;
            Physics.SyncTransforms();
            yield return Seconds(.2f);
            Vector3 rushStart = run.player.position;
            Check(skill.TryActivate(), "맹화돌진 replaces the throw");
            yield return Until(() => !skill.Busy, 3, "rush ends");
            float rushed = Vector3.Distance(Flat(run.player.position), Flat(rushStart));
            Check(skill.RushCount == 1 && rushed > 4f && lane.Health < laneHealth, $"the rush carries her forward ({rushed:F1} m) through the enemy");
            Object.Destroy(lane.gameObject);

            // ---- 화염질주
            augments.Acquire(AugmentCatalog.Find(ProiAugments.BlazeDash));
            var kit = proi.GetComponent<ProiAugmentKit>();
            motor.ResetAt(Arena + Vector3.up * .05f);
            var dashTarget = Enemy(arena.transform, Arena + Vector3.forward * 2.2f, 100000);
            Physics.SyncTransforms();
            yield return Seconds(.6f);
            motor.SetAutomationInput(new Vector2(0, 1), Arena + Vector3.forward * 8, true);
            yield return Seconds(.1f);
            motor.SetAutomationInput(new Vector2(0, 1), Arena + Vector3.forward * 8, false);
            yield return Seconds(.4f);
            motor.SetAutomationInput(Vector2.zero, run.player.position + Vector3.forward * 3);
            Check(kit.DashHits >= 1, "화염질주: the dash burns the enemy on its path");
            Object.Destroy(dashTarget.gameObject);

            // ---- 불씨수호 / 반사의화염
            yield return Seconds(1f);
            augments.Acquire(AugmentCatalog.Find(ProiAugments.EmberGuard));
            int guarded = health.Health;
            Check(!health.TakeDamage(10) && health.Health == guarded && kit.Blocks == 1, "불씨수호 blocks one hit");
            yield return Seconds(.7f);
            augments.Acquire(AugmentCatalog.Find(ProiAugments.ReflectFlame));
            var nearby = Enemy(arena.transform, run.player.position + Vector3.right * 2f, 100000);
            int nearbyHealth = nearby.Health;
            Check(!health.TakeDamage(10) && kit.Reflections == 1 && nearby.Health < nearbyHealth, $"반사의화염 blocks and burns back ({nearbyHealth - nearby.Health} damage)");
            yield return Seconds(.7f);
            Check(health.TakeDamage(5) && health.Health == guarded - 5, "with both shields spent the next hit lands");

            // ---- offer rules
            var hellfire = AugmentCatalog.Find(ProiAugments.Hellfire);
            Check(hellfire != null && AugmentCatalog.IsOfferable(hellfire, augments, "proi") && !AugmentCatalog.IsOfferable(hellfire, augments, HunterCharacter.TestHunterId),
                "Proi's augments are offered to Proi only");
            int proiAugments = 0;
            foreach (var augment in AugmentCatalog.All) if (augment.characterId == ProiAugments.CharacterId) proiAugments++;
            Check(proiAugments == 6, "six Proi augments are registered");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        static TrainingEnemy Enemy(Transform parent, Vector3 position, int healthPoints)
        {
            var target = new GameObject("ValidationEnemy");
            target.transform.SetParent(parent);
            target.transform.position = position;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(target.transform, false);
            body.transform.localPosition = Vector3.up;
            var enemy = target.AddComponent<TrainingEnemy>();
            enemy.Configure(healthPoints, false);
            return enemy;
        }

        static Transform FindWindow(LiminalRunDirector run)
        {
            var canvas = run.GetComponent<LiminalHud>().Canvas;
            var window = canvas ? canvas.Find("LobbyWindow") : null;
            return window ? window.Find("Window") : null;
        }

        static Transform Find(Transform root, string name) => root ? FindDeep(root, name) : null;

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found) return found;
            }
            return null;
        }

        /// <summary>Renders the main camera with the HUD canvases drawn into it (overlay canvases are not in camera renders).</summary>
        static void Capture(string file)
        {
            var camera = Camera.main;
            if (!camera) return;
            const int w = 1280, h = 720;
            var overlays = new List<Canvas>();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay) overlays.Add(canvas);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var previous = camera.targetTexture;
            camera.targetTexture = rt;
            foreach (var canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .05f; }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = previous;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            report.captures.Add(path.Replace('\\', '/'));
        }

        static void Finish(string error)
        {
            routine = null;
            waiting = null;
            if (error != null) report.errors.Add(error);
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "loadout-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[LoadoutValidation] " + report.status + "\n" + string.Join("\n", report.checks) + (error != null ? "\n" + error : ""));
            LiminalRunDirector.SuppressAugmentOffers = false;
            Restore(HunterKey, SessionState.GetString(Active + ".hunter", "\u0000"));
            Restore(SupportKey, SessionState.GetString(Active + ".support", "\u0000"));
            PlayerPrefs.Save();
            SessionState.SetInt(Active + ".exit", report.errors.Count == 0 ? 0 : 1);
            EditorApplication.ExitPlaymode();
        }

        static void Restore(string key, string value)
        {
            if (value == "\u0000") PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetString(key, value);
        }
    }
}
