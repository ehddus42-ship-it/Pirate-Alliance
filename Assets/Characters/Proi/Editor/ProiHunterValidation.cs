using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 게임 안 Proi (헌터 교체) 배치 검증: LiminalRun 씬을 실제로 재생해서
/// 교체(F) → 몸 / 애니메이터 / 콤보 설정 → 3타 콤보가 적에게 데미지 → 대쉬 후 이동 동작 복귀 → 궁극기(R) 피해 / 입력 잠금 / 배속 →
/// 다시 교체 → 로비 왕복(로비와 다음 게이트 모두 고른 헌터, 저장된 편성은 검증 뒤 원래대로)
/// 을 확인하고 Documentation/Proi/ 에 보고서와 화면을 남깁니다.
/// Unity -batchmode -projectPath . -executeMethod ProiHunterValidation.RunBatch -logFile Logs/proi-hunter.log
/// </summary>
[InitializeOnLoad]
public static class ProiHunterValidation
{
    const string Active = "Proi.HunterValidation";
    const string ScenePath = "Assets/Liminal/Scenes/LiminalRun.unity";
    const string Folder = "Documentation/Proi";

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
    static string savedHunter;
    static IEnumerator routine;
    static Wait waiting;
    static double deadline, started;
    static readonly Vector3 Arena = new Vector3(1000, 0, 1000);
    static readonly int[] AttackStates =
    {
        Animator.StringToHash("Base Layer.Attack1"), Animator.StringToHash("Base Layer.Attack2"), Animator.StringToHash("Base Layer.Attack3")
    };
    static readonly int LocomotionState = Animator.StringToHash("Base Layer.Locomotion");

    static ProiHunterValidation()
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
        if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch is for an isolated batch editor.");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Active, true);
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (routine == null || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 180) throw new TimeoutException("Proi hunter validation exceeded 180 seconds.");
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
            if (routine.Current is Wait wait)
            {
                waiting = wait;
                deadline = EditorApplication.timeSinceStartup + wait.timeout;
            }
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
        savedHunter = HunterLoadout.HunterId;
        LiminalRunDirector director = null;
        yield return Until(() => (director = Object.FindFirstObjectByType<LiminalRunDirector>()) && director.Roster && director.Phase == LiminalRunPhase.Lobby, 30, "director in lobby");
        var roster = director.Roster;
        var motor = roster.GetComponent<PlayerMotor>();
        var combat = roster.GetComponent<PlayerCombat>();
        var melee = roster.GetComponent<MeleeSlash>();
        var hunter = roster.GetComponent<HunterCharacter>();
        var health = roster.GetComponent<LiminalPlayerHealth>();
        Check(roster.Count >= 2, $"roster has the scene hunter and Proi (count={roster.Count})");
        // The saved lobby loadout may have Proi picked; this check starts the gate with the scene's own hunter.
        if (roster.SelectedIndex != 0) roster.Choose(0);
        var astraia = motor.visual.gameObject;
        int astraiaCombo = combat.ComboLength;

        director.EnterDungeon();
        yield return Until(() => director.Phase == LiminalRunPhase.Exploring, 20, "gate entered");
        Check(roster.CurrentIndex == 0 && motor.visual.gameObject == astraia && melee.bladeEffects, "gate starts with Astraia and her katana");

        // ---- swap to Proi
        yield return Until(() => roster.CanSwap, 5, "swap allowed in the gate");
        Check(roster.TrySwap(), "F swap accepted in the gate");
        var proi = motor.visual.gameObject;
        var animator = motor.animator;
        Check(proi != astraia && proi.activeInHierarchy && !astraia.activeSelf, $"Proi body worn ({proi.name}), Astraia hidden");
        Check(animator && animator.runtimeAnimatorController && animator.runtimeAnimatorController.name == "Proi", "PlayerMotor drives Proi's Animator (Proi.controller)");
        Check(combat.ComboLength == 3 && !melee.bladeEffects && melee.swings.Length == 3, $"three-hit unarmed combo (combo={combat.ComboLength}, katana combo was {astraiaCombo})");
        Check(hunter.displayName == "프로이" && hunter.characterId == "proi", "hunter identity is Proi");
        Check(!roster.TrySwap(), "swap cooldown blocks an immediate second swap");
        var kit = proi.GetComponent<ProiHunterKit>();
        var blaze = proi.GetComponent<ProiBlazeCombo>();
        Check(kit && kit.isActiveAndEnabled && blaze, "Proi kit and fire combo are on the body");

        // Head heights match so the hitbox and camera framing stay the same.
        var astraiaHead = astraia.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        var proiHead = ProiBlazeKit.FindDeep(proi.transform, "Head");
        Check(astraiaHead && proiHead, "head bones found");

        // ---- arena far from the route
        director.enabled = false;
        health.GrantInvulnerability(999);
        var arena = new GameObject("ProiValidationArena");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetParent(arena.transform);
        floor.transform.position = Arena + Vector3.down * .25f;
        floor.transform.localScale = new Vector3(40, .5f, 40);
        motor.ResetAt(Arena + Vector3.up * .05f);
        motor.visual.rotation = Quaternion.LookRotation(Vector3.forward);
        motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
        var cameraRig = Object.FindFirstObjectByType<IsometricFollowCamera>();
        if (cameraRig) cameraRig.Snap();
        Physics.SyncTransforms();
        yield return Seconds(.6f);
        Check(motor.GetComponent<CharacterController>().isGrounded, "Proi stands on the floor");
        float headRatio = (proiHead.position.y - Arena.y) / Mathf.Max(.01f, astraiaHead.position.y - Arena.y);
        report.checks.Add($"INFO Proi scale {proi.transform.localScale.x:F3}, head {proiHead.position.y - Arena.y:F2} m");
        Check(headRatio > .85f && headRatio < 1.15f, $"Proi's head sits at Astraia's height (ratio {headRatio:F2})");
        Capture("proi_ingame_idle.png", report);

        // ---- run: animator speed follows the motor
        motor.SetAutomationInput(Vector2.up, Arena + Vector3.forward * 8);
        yield return Seconds(.7f);
        Check(motor.PlanarVelocity.magnitude > motor.moveSpeed * .8f && animator.GetFloat("Speed") > .6f, $"runs with the run blend (speed {motor.PlanarVelocity.magnitude:F2} m/s, Speed {animator.GetFloat("Speed"):F2})");
        Capture("proi_ingame_run.png", report);
        motor.ResetAt(Arena + Vector3.up * .05f);
        motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);
        yield return Seconds(.3f);

        // ---- combo on a target
        var target = new GameObject("ValidationTarget");
        target.transform.SetParent(arena.transform);
        target.transform.position = Arena + Vector3.forward * 2.4f;
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(target.transform, false);
        body.transform.localPosition = Vector3.up;
        var enemy = target.AddComponent<TrainingEnemy>();
        enemy.Configure(100000, false);
        Physics.SyncTransforms();
        yield return Seconds(.2f);
        int attacks = combat.AttackCount, combos = combat.CompletedComboCount, hits = melee.HitCount;
        var states = new HashSet<int>();
        var steps = new HashSet<int>();
        bool captured = false;
        motor.SetAutomationInput(Vector2.zero, target.transform.position);
        combat.SetAutomationInput(true, true);
        float t0 = Time.time;
        while (combat.AttackCount - attacks < 3 && Time.time - t0 < 6)
        {
            yield return null;
            Observe(animator, states);
            if (kit.Busy) steps.Add(kit.CurrentStep);
            if (!captured && combat.AttackIndex == 2 && kit.Progress > .4f) { captured = true; Capture("proi_ingame_kick.png", report); }
        }
        combat.SetAutomationInput(false, false);
        while (combat.IsAttacking && Time.time - t0 < 8)
        {
            yield return null;
            Observe(animator, states);
            if (kit.Busy) steps.Add(kit.CurrentStep);
            if (!captured && combat.AttackIndex == 2 && kit.Progress > .4f) { captured = true; Capture("proi_ingame_kick.png", report); }
        }
        combat.ReleaseAutomation();
        int damage = enemy.maxHealth - enemy.Health;
        Check(combat.AttackCount - attacks == 3 && combat.CompletedComboCount == combos + 1, "one full three-hit combo");
        Check(states.Count == 3, $"Animator played Attack1, Attack2, Attack3 (saw {states.Count})");
        Check(steps.Count == 3, $"fire combo followed all three steps (saw {steps.Count})");
        Check(melee.HitCount - hits == 3 && damage > 0, $"every swing landed through MeleeSlash (hits {melee.HitCount - hits}, damage {damage})");
        Check(Vector3.Distance(motor.transform.position, Arena) > .15f, $"combo root travel moved the body ({Vector3.Distance(motor.transform.position, Arena):F2} m)");
        yield return Seconds(.5f);
        Check(Current(animator) == LocomotionState, "back to locomotion after the combo");

        // ---- dash returns to locomotion
        motor.ResetAt(Arena + Vector3.up * .05f);
        int dashes = motor.DashCount;
        motor.SetAutomationInput(Vector2.right, Arena + Vector3.right * 5, true);
        yield return null;
        motor.SetAutomationInput(Vector2.zero, Arena + Vector3.right * 5);
        yield return Seconds(motor.dashDuration + .5f);
        Check(motor.DashCount == dashes + 1 && Current(animator) == LocomotionState, "dash plays and hands back to locomotion");

        // ---- ultimate (R): charge, arch jump, slam, three branches of fire pillars
        var ultimate = proi.GetComponent<ProiHunterUltimate>();
        var ultCore = proi.GetComponent<ProiUltimate>();
        Check(ultimate && ultCore && proi.GetComponent<ProiBlazeUltimate>() && ReferenceEquals(roster.CurrentSkill, ultimate), "R ultimate is on Proi and is her hunter skill");
        target.SetActive(false);
        var ultTarget = new GameObject("UltimateTarget");
        ultTarget.transform.SetParent(arena.transform);
        ultTarget.transform.position = Arena + Vector3.forward * 5.4f;
        var ultBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        ultBody.transform.SetParent(ultTarget.transform, false);
        ultBody.transform.localPosition = Vector3.up;
        var ultEnemy = ultTarget.AddComponent<TrainingEnemy>();
        ultEnemy.Configure(100000, false);
        motor.ResetAt(Arena + Vector3.up * .05f);
        motor.visual.rotation = Quaternion.LookRotation(Vector3.forward);
        motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 6);
        Physics.SyncTransforms();
        yield return Seconds(.3f);
        motor.ReleaseAutomation();
        int ultHits = ultimate.HitCount;
        Vector3 ultStart = motor.transform.position;
        Check(ultimate.TryActivate(), "R starts the ultimate");
        Check(!roster.CanSwap && !roster.TrySwap(), "no swapping while the ultimate plays");
        Check(!ultimate.TryActivate(), "a second R does nothing while it plays");
        float ultAt = Time.time, firstHitAt = -1;
        bool ultShot = false, dashedDuring = false, frontShot = false, jumpShot = false, cinematic = false;
        int dashesBefore = motor.DashCount;
        var follow = Object.FindFirstObjectByType<IsometricFollowCamera>();
        float baseFov = Camera.main ? Camera.main.fieldOfView : 0;
        while (ultCore.Active && Time.time - ultAt < 10)
        {
            yield return null;
            if (follow && follow.InCinematic) cinematic = true;
            if (!frontShot && ultCore.SkillTime > 1.2f) { frontShot = true; Capture("proi_ingame_ultimate_front.png", report); }
            if (!jumpShot && ultCore.SkillTime > ultCore.DashStart + .35f) { jumpShot = true; Capture("proi_ingame_ultimate_jump.png", report); }
            if (motor.DashCount != dashesBefore) dashedDuring = true;
            if (firstHitAt < 0 && ultimate.HitCount > ultHits) firstHitAt = Time.time;
            if (!ultShot && firstHitAt > 0 && Time.time - firstHitAt > .45f) { ultShot = true; Capture("proi_ingame_ultimate.png", report); }
        }
        float travelled = Vector3.ProjectOnPlane(motor.transform.position - ultStart, Vector3.up).magnitude;
        Check(!ultCore.Active && Time.time - ultAt < 10, $"ultimate plays through ({Time.time - ultAt:F1} s)");
        Check(ultimate.HitCount - ultHits >= 2 && ultEnemy.Health < ultEnemy.maxHealth,
            $"slam and pillars hit the enemy (hits {ultimate.HitCount - ultHits}, damage {ultEnemy.maxHealth - ultEnemy.Health})");
        Check(travelled > 2.5f, $"the arch jump carried her forward ({travelled:F2} m)");
        Check(!dashedDuring, "no dash input during the ultimate");
        Check(cinematic, "the game camera takes the ultimate's cinematic shots (front, jump, dive, pillars)");
        yield return Until(() => !follow.InCinematic, 4, "camera back to the follow view");
        Check(Mathf.Abs(Camera.main.fieldOfView - baseFov) < .01f, "the follow view and its field of view come back after the ultimate");
        Check(ultimate.CooldownRemaining > 0 && !ultimate.TryActivate(), $"cooldown after use ({ultimate.CooldownRemaining:F1} s left)");
        yield return Seconds(.4f);
        Check(Mathf.Approximately(Time.timeScale, 1f), $"game speed untouched afterwards (timeScale {Time.timeScale:F2})");
        motor.SetAutomationInput(Vector2.right, Arena + Vector3.right * 8);
        yield return Seconds(.5f);
        Check(motor.PlanarVelocity.magnitude > motor.moveSpeed * .6f && Current(animator) == LocomotionState,
            $"control and locomotion come back ({motor.PlanarVelocity.magnitude:F2} m/s)");
        motor.SetAutomationInput(Vector2.zero, Arena + Vector3.forward * 5);

        // ---- swap back
        yield return Until(() => roster.CanSwap, 3, "swap cooldown over");
        Check(roster.TrySwap() && motor.visual.gameObject == astraia && !proi.activeSelf, "F again: Astraia is back");
        Check(combat.ComboLength == astraiaCombo && melee.bladeEffects, "katana combo restored");
        var fxRoot = GameObject.Find("ProiBlazeFX");
        Check(!fxRoot || !fxRoot.activeInHierarchy, "Proi's fire effects are hidden with her");

        // ---- lobby: the picked hunter walks the plaza (the loadout keeps her), and the next gate starts with her
        roster.Select(1);
        Object.Destroy(arena);
        director.enabled = true;
        director.ReturnToLobby();
        yield return Until(() => director.Phase == LiminalRunPhase.Lobby, 10, "back in the lobby");
        Check(roster.CurrentIndex == 1 && proi.activeSelf && !astraia.activeSelf && roster.SelectedIndex == 1, "lobby shows the picked Proi");
        Check(!roster.CanSwap && !roster.TrySwap(), "no swapping in the lobby");
        director.EnterDungeon();
        yield return Until(() => director.Phase == LiminalRunPhase.Exploring, 20, "gate entered again");
        Check(roster.CurrentIndex == 1 && motor.visual.gameObject == proi && !melee.bladeEffects, "next gate starts with the picked Proi");
        yield return Seconds(.5f);
        Capture("proi_ingame_gate.png", report);
    }

    static void Observe(Animator animator, HashSet<int> states)
    {
        int current = Current(animator);
        foreach (int s in AttackStates) if (current == s) states.Add(s);
        if (animator.IsInTransition(0))
        {
            int next = animator.GetNextAnimatorStateInfo(0).fullPathHash;
            foreach (int s in AttackStates) if (next == s) states.Add(s);
        }
    }

    static int Current(Animator animator) => animator.GetCurrentAnimatorStateInfo(0).fullPathHash;

    static void Capture(string file, Report into)
    {
        var camera = Camera.main;
        if (!camera) return;
        const int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        camera.targetTexture = previous;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        into.captures.Add(path.Replace('\\', '/'));
    }

    static void Finish(string error)
    {
        routine = null;
        waiting = null;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 ? "passed" : "failed";
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "ingame-validation.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[ProiHunterValidation] " + report.status + "\n" + string.Join("\n", report.checks) + (error != null ? "\n" + error : ""));
        LiminalRunDirector.SuppressAugmentOffers = false;
        // Swapping saved the loadout; the player's own pick comes back.
        if (savedHunter != null) HunterLoadout.HunterId = savedHunter;
        SessionState.SetInt(Active + ".exit", report.errors.Count == 0 ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }
}
