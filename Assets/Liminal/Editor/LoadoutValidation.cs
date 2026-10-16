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
    /// <summary>Demo integration: Proi-only lobby, both supports, combat/augments, both missions,
    /// victory rewards, permanent upgrades and defeat/retry. Room clearing uses scripted damage;
    /// combat skills are separately exercised on live targets. Restores all touched progression.</summary>
    [InitializeOnLoad]
    public static class LoadoutValidation
    {
        const string Active = "Liminal.LoadoutValidation";
        const string ScenePath = "Assets/Liminal/Scenes/LiminalRun.unity";
        const string Folder = "Documentation/Demo/Validation";
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
        static readonly string[] ProgressKeys = { "Hunter.MagicStones", "Hunter.Upgrade.Vitality", "Hunter.Upgrade.Swordsmanship", "Hunter.Upgrade.Evasion", "Hunter.Upgrade.Counter", "Hunter.Destination.liminal", "Hunter.Destination.game", "Hunter.VisitedStage.liminal_1", "Hunter.VisitedStage.concept_game" };

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
            foreach (string key in ProgressKeys)
            {
                SessionState.SetBool(Active + key + ".exists", PlayerPrefs.HasKey(key));
                SessionState.SetInt(Active + key, PlayerPrefs.GetInt(key));
                PlayerPrefs.DeleteKey(key);
            }
            // The player's own party and progression are put back at the end.
            hadHunter = PlayerPrefs.HasKey(HunterKey); savedHunter = PlayerPrefs.GetString(HunterKey, "");
            hadSupport = PlayerPrefs.HasKey(SupportKey); savedSupport = PlayerPrefs.GetString(SupportKey, "");
            SessionState.SetString(Active + ".hunter", hadHunter ? savedHunter : "\u0000");
            SessionState.SetString(Active + ".support", hadSupport ? savedSupport : "\u0000");
            PlayerPrefs.SetString(HunterKey, "test");
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
                if (EditorApplication.timeSinceStartup - started > 420) throw new TimeoutException("Loadout validation exceeded 420 seconds.");
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

            Check(roster.Count == 1 && roster.CurrentId == "proi" && roster.SelectedIndex == 0,
                "only completed Proi is playable; removed saved hunter migrates to Proi");
            Check(HunterLoadout.HunterId == "proi" && roster.WearsLobbyForm && !roster.CurrentUsesSceneModel,
                "default Proi wears her own everyday body in the lobby");
            Check(lobby.LoadoutOfficer && lobby.Officials.Contains(lobby.LoadoutOfficer), "loadout officer is available");
            Check(kokkio && yuni && SupportCharacterCatalog.All.Count == 2, "both supports are available");
            typeof(LiminalLobby).GetMethod("OpenLoadout", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(lobby, null);
            yield return Seconds(.3f);
            var window = FindWindow(run);
            Check(window && Find(window, "Hunter_proi") && !Find(window, "Hunter_test") && !Find(window, "Hunter_namgung_luna"),
                "loadout window offers Proi without Astraia or unfinished Luna");
            Button Card(string name) => Find(window, name).GetComponent<Button>();
            Card("Support_kokkio").onClick.Invoke();
            yield return Seconds(.2f);
            Check(HunterLoadout.SupportId == SupportCharacterCatalog.Kokkio, "loadout selects and saves 꼬끼오");
            Card("Support_yuni").onClick.Invoke();
            yield return Seconds(.2f);
            Check(HunterLoadout.SupportId == SupportCharacterCatalog.Yuni, "loadout selects and saves 유니");
            Card("Support_kokkio").onClick.Invoke();
            Capture("loadout_window.png");
            Find(window, "Close").GetComponent<Button>().onClick.Invoke();
            yield return Seconds(.2f);
            Check(!lobby.WindowOpen && motor.enabled, "closing loadout releases movement");
            run.EnterDungeon();
            yield return Until(() => run.Phase == LiminalRunPhase.Exploring, 20, "gate entered");
            var proi = motor.visual.gameObject;
            Check(roster.CurrentId == "proi" && !roster.WearsLobbyForm && motor.animator.runtimeAnimatorController.name == "Proi",
                "the selected Proi enters the gate in her battle body");

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

            HunterLoadout.SupportId = SupportCharacterCatalog.Yuni;
            var comboTarget = Enemy(arena.transform, Arena + Vector3.forward * 2.2f, 100000);
            Physics.SyncTransforms();
            yield return Seconds(.2f);
            Check(yuni.TryActivate(), "유니 activates bonus stage");
            yield return Until(() => yuni.OneUpActive, 5, "유니 meteor lands");
            int targetBefore = comboTarget.Health;
            int chompers = yuni.ChompersFired;
            var combat = run.player.GetComponent<PlayerCombat>();
            int combos = combat.CompletedComboCount;
            combat.SetAutomationInput(true, true);
            yield return Until(() => combat.AttackCount >= 3, 8, "Proi three attacks");
            combat.SetAutomationInput(false, false);
            yield return Until(() => !combat.IsAttacking, 5, "combo finishes");
            combat.ReleaseAutomation();
            Check(combat.CompletedComboCount > combos && comboTarget.Health < targetBefore && yuni.ChompersFired > chompers,
                "Proi's live three-hit combo damages target and fires 유니 bonus projectiles");
            var ultimate = proi.GetComponent<ProiHunterUltimate>();
            motor.SetAutomationInput(Vector2.zero, comboTarget.transform.position);
            Check(ultimate && ultimate.TryActivate(), "Proi ultimate activates");
            yield return Until(() => !ultimate.Busy, 18, "ultimate finishes");
            Capture("proi_combat.png");
            Object.Destroy(comboTarget.gameObject);
            HunterLoadout.SupportId = SupportCharacterCatalog.Kokkio;
            yield return Seconds(1.5f);
            motor.ResetAt(Arena + Vector3.up * .05f);
            motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);

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

            Object.Destroy(arena);
            run.enabled = true;
            run.ReturnToLobby();
            yield return Seconds(.2f);
            LiminalRunDirector.SuppressAugmentOffers = false;
            foreach (string missionId in new[] { "liminal_campaign", "game_exhibition" })
            {
                int wallet = HunterProgress.Currency;
                Check(run.EnterMission(missionId), "enter demo mission " + missionId);
                yield return Seconds(.3f);
                Check(run.Rooms.Count == 4 && roster.CurrentId == "proi", "four-room mission starts as Proi");
                run.PlayerHealth.GrantInvulnerability(300);
                for (int i = 0; i < run.Rooms.Count; i++)
                {
                    var room = run.Rooms[i];
                    motor.ResetAt(room.playerSpawn.position + Vector3.up * .05f);
                    motor.SetAutomationInput(Vector2.zero, room.playerSpawn.position + room.transform.forward * 5);
                    yield return Until(() => run.ActiveRoomIndex == i && run.LivingEnemyCount > 0, 10, "room activates " + i);
                    Check(!run.TryUseExit(), "uncleared room blocks exit");
                    var enemies = new List<TrainingEnemy>();
                    run.GetLivingEnemies(enemies);
                    foreach (var enemy in enemies) enemy.TakeDamage(1000000);
                    yield return Until(() => run.LivingEnemyCount == 0, 5, "room clears");
                    if (room.kind == LiminalRoomKind.Combat)
                    {
                        Check(run.Phase == LiminalRunPhase.AugmentChoice && run.AugmentOffers.Count > 0,
                            "room clear offers augments for Proi");
                        int picks = run.Augments.PickCount;
                        run.SelectAugment(0);
                        Check(run.Phase == LiminalRunPhase.Exploring && run.Augments.PickCount == picks + 1,
                            "augment selection grants one choice and resumes combat");
                    }
                    yield return Seconds(.15f);
                }
                var last = run.Rooms[run.Rooms.Count - 1];
                motor.ResetAt(last.exit.position - last.exit.forward * 2.5f + Vector3.up * .05f);
                Check(run.TryUseExit() && run.Phase == LiminalRunPhase.Victory, "boss clear and exit reach victory: " + missionId);
                Capture(missionId + "_victory.png");
                int reward = run.PendingReward;
                Check(reward > 0, "victory has earned currency");
                run.ReturnToLobby();
                yield return Seconds(.2f);
                Check(HunterProgress.Currency == wallet + reward && run.PendingReward == 0 && roster.WearsLobbyForm,
                    "return pays rewards once and restores Proi everyday body");
                run.ReturnToLobby();
                Check(HunterProgress.Currency == wallet + reward, "repeated lobby return cannot duplicate rewards");
            }
            Check(HunterProgress.TryBuy(HunterProgress.Upgrade.Vitality), "earned rewards purchase a permanent upgrade");
            Check(run.EnterMission("game_exhibition"), "enter after upgrade");
            Check(run.PlayerHealth.maximumHealth == 112, "next run applies purchased maximum-health upgrade");
            yield return Seconds(1.1f);
            Check(run.PlayerHealth.TakeDamage(100000) && run.Phase == LiminalRunPhase.Defeat, "player death opens defeat");
            run.RetryMission();
            yield return Seconds(.3f);
            Check(run.Phase == LiminalRunPhase.Exploring && run.ActiveMission.id == "game_exhibition" && run.PlayerHealth.IsAlive,
                "defeat retries the same mission with restored health");
            run.ReturnToLobby();
            Capture("demo_lobby.png");
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
            foreach (string key in ProgressKeys)
                if (SessionState.GetBool(Active + key + ".exists", false)) PlayerPrefs.SetInt(key, SessionState.GetInt(Active + key, 0));
                else PlayerPrefs.DeleteKey(key);
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
