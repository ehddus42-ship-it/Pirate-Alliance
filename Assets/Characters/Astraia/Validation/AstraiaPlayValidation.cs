using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Validation
{
    /// <summary>Add to an empty object in Play Mode. Uses real motor, combat, projectile and collision paths.</summary>
    public sealed class AstraiaPlayValidation : MonoBehaviour
    {
        [Serializable] public sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string expected;
            public string observed;
        }

        [Serializable] public sealed class Report
        {
            public string status = "running";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public float elapsedSeconds;
            public List<CheckResult> checks = new List<CheckResult>();
        }

        public bool runOnStart = true;
        public bool IsRunning { get; private set; }
        public Report Result { get; private set; }
        public string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Documentation/Astraia/play-validation.json"));

        readonly Vector3 arenaOrigin = new Vector3(1000, 0, 1000);
        PlayerMotor motor;
        PlayerCombat combat;
        TalismanCaster caster;
        LiminalRunDirector director;
        GameObject arena, wall;
        Vector3 savedPosition, savedAimMarker;
        Quaternion savedRotation, savedVisualRotation;
        bool savedMotorEnabled, savedCombatEnabled, savedCasterEnabled, savedDirectorEnabled, savedHold;
        float savedTimeScale, startedAt;
        bool captured, completed;

        void Start() { if (runOnStart) Begin(); }

        public void Begin()
        {
            if (!Application.isPlaying || IsRunning) return;
            StartCoroutine(Validate());
        }

        IEnumerator Validate()
        {
            IsRunning = true;
            completed = false;
            startedAt = Time.realtimeSinceStartup;
            Result = new Report { startedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion };
            // Let the scene's Start methods create their director, camera and player first.
            yield return null;
            yield return null;
            try
            {
                motor = FindFirstObjectByType<PlayerMotor>();
                combat = motor ? motor.GetComponent<PlayerCombat>() : null;
                caster = motor ? motor.GetComponent<TalismanCaster>() : null;
                director = FindFirstObjectByType<LiminalRunDirector>();
                bool ready = motor && combat && caster && motor.visual && motor.GetComponent<CharacterController>();
                Check("Player setup", ready, "PlayerMotor + PlayerCombat + TalismanCaster + visual + controller", ready ? "present" : "missing component or visual");
                if (!ready) yield break;

                CaptureState();
                Time.timeScale = 1;
                if (director) director.enabled = false;
                motor.enabled = caster.enabled = combat.enabled = true;
                combat.holdToAttack = true;
                BuildArena();
                ResetPlayer();
                yield return Wait(.35f);
                Check("Ground contact", motor.GetComponent<CharacterController>().isGrounded, "standing on temporary floor", motor.transform.position.ToString("F3"));

                Move(Vector2.up, true);
                yield return Wait(.8f);
                float walk = motor.PlanarVelocity.magnitude;
                Check("Walking speed", Mathf.Abs(walk - Mathf.Min(motor.walkSpeed, motor.moveSpeed)) < .35f,
                    Mathf.Min(motor.walkSpeed, motor.moveSpeed).ToString("F3") + " m/s +/- 0.35", walk.ToString("F3") + " m/s");

                Move(Vector2.up);
                yield return Wait(.8f);
                float run = motor.PlanarVelocity.magnitude;
                Check("Running speed", Mathf.Abs(run - motor.moveSpeed) < .4f,
                    motor.moveSpeed.ToString("F3") + " m/s +/- 0.40", run.ToString("F3") + " m/s");

                Move(Vector2.zero);
                yield return Wait(.4f);
                Check("Decelerate to stop", motor.PlanarVelocity.magnitude < .08f, "speed < 0.08 m/s", motor.PlanarVelocity.magnitude.ToString("F3") + " m/s");

                ResetPlayer();
                yield return Wait(.15f);
                Vector3 dashStart = motor.transform.position;
                int dashCount = motor.DashCount;
                Move(Vector2.zero, false, true);
                yield return Wait(motor.dashDuration + .12f);
                float distance = Vector3.ProjectOnPlane(motor.transform.position - dashStart, Vector3.up).magnitude;
                float expectedDistance = motor.dashSpeed * motor.dashDuration;
                Check("Free dash distance", motor.DashCount == dashCount + 1 && Mathf.Abs(distance - expectedDistance) < .3f && !motor.IsDashing,
                    expectedDistance.ToString("F3") + " m +/- 0.30; exactly one dash; finished", distance.ToString("F3") + " m; dash delta=" + (motor.DashCount - dashCount) + "; active=" + motor.IsDashing);

                ResetPlayer();
                wall = Cube("ValidationWall", arenaOrigin + Vector3.forward * 1.35f + Vector3.up * 1.5f, new Vector3(10, 3, .4f));
                Physics.SyncTransforms();
                yield return Wait(.15f);
                Move(Vector2.zero, false, true);
                yield return Wait(Mathf.Max(.12f, motor.dashDuration * .85f));
                float wallTravel = motor.transform.position.z - arenaOrigin.z;
                bool stoppedAtWall = !motor.IsDashing;
                yield return Wait(.15f);
                float maximumZ = arenaOrigin.z + 1.15f - motor.GetComponent<CharacterController>().radius + .15f;
                Check("Wall stops dash", stoppedAtWall && wallTravel > .15f && motor.transform.position.z <= maximumZ,
                    "dash cancelled on contact; player center z <= " + maximumZ.ToString("F3"),
                    "travel=" + wallTravel.ToString("F3") + " m; center z=" + motor.transform.position.z.ToString("F3") + "; cancelled=" + stoppedAtWall);
                wall.SetActive(false);

                ResetPlayer();
                var targetRoot = new GameObject("ValidationTarget");
                targetRoot.transform.SetParent(arena.transform);
                targetRoot.transform.position = arenaOrigin + Vector3.forward * 5;
                var targetBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                targetBody.name = "TargetBody";
                targetBody.transform.SetParent(targetRoot.transform, false);
                targetBody.transform.localPosition = Vector3.up;
                var target = targetRoot.AddComponent<TrainingEnemy>();
                target.Configure(100000, false);
                Physics.SyncTransforms();
                yield return Wait(.15f);
                int attacksBefore = combat.AttackCount, castsBefore = caster.CastCount, combosBefore = combat.CompletedComboCount;
                var observedIndices = new HashSet<int>();
                combat.SetAutomationInput(true, true);
                float comboWait = 0;
                while (combat.AttackCount - attacksBefore < 3 && comboWait < 6)
                {
                    yield return Wait(.04f);
                    comboWait += .04f;
                    if (combat.AttackIndex >= 0) observedIndices.Add(combat.AttackIndex);
                }
                combat.SetAutomationInput(false, false);
                while (combat.IsAttacking && comboWait < 8)
                {
                    yield return Wait(.04f);
                    comboWait += .04f;
                    if (combat.AttackIndex >= 0) observedIndices.Add(combat.AttackIndex);
                }
                yield return Wait(.6f);
                int attackDelta = combat.AttackCount - attacksBefore, castDelta = caster.CastCount - castsBefore;
                Check("Three-hit combo and projectiles", attackDelta == 3 && castDelta == 3 && combat.CompletedComboCount == combosBefore + 1
                    && observedIndices.Count == 3 && target.Health < target.maxHealth && !combat.IsAttacking,
                    "attack indices 0/1/2; exactly 3 attacks and 3 casts; one completed combo; target damaged",
                    "attacks=" + attackDelta + "; casts=" + castDelta + "; observed states=" + observedIndices.Count
                    + "; combos=" + (combat.CompletedComboCount - combosBefore) + "; damage=" + (target.maxHealth - target.Health));

                ResetPlayer();
                yield return Wait(.15f);
                castsBefore = caster.CastCount;
                dashCount = motor.DashCount;
                combat.SetAutomationInput(true);
                // Resume after the Update that starts the attack, before its next combat tick.
                yield return null;
                bool beganAttack = combat.IsAttacking;
                Move(Vector2.zero, false, true);
                yield return Wait(motor.dashDuration + .3f);
                Check("Dash cancels attack before hit", beganAttack && caster.CastCount == castsBefore && !combat.IsAttacking && motor.DashCount == dashCount + 1,
                    "attack started, then one dash; no cast; attack idle", "started=" + beganAttack + "; casts=" + (caster.CastCount - castsBefore)
                    + "; dashes=" + (motor.DashCount - dashCount) + "; attack index=" + combat.AttackIndex);

                ResetPlayer();
                yield return Wait(.15f);
                castsBefore = caster.CastCount;
                combat.SetAutomationInput(true);
                yield return null;
                beganAttack = combat.IsAttacking;
                motor.ResetAt(arenaOrigin + Vector3.left * 3 + Vector3.up * .05f);
                combat.SetAutomationInput(false);
                Move(Vector2.zero);
                yield return Wait(.8f);
                Check("Reset cancels pending attack", beganAttack && !combat.IsAttacking && !motor.IsDashing && caster.CastCount == castsBefore,
                    "attack cancelled; no delayed cast or dash", "started=" + beganAttack + "; casts=" + (caster.CastCount - castsBefore)
                    + "; attack index=" + combat.AttackIndex + "; dash=" + motor.IsDashing);
                completed = true;
            }
            finally
            {
                if (!completed) Check("Validation completed", false, "all checks executed", "aborted or interrupted; inspect Unity Console if unexpected");
                RestoreState();
                Result.status = Result.checks.TrueForAll(item => item.passed) ? "passed" : "failed";
                Result.finishedUtc = DateTime.UtcNow.ToString("o");
                Result.elapsedSeconds = Time.realtimeSinceStartup - startedAt;
                SaveReport();
                IsRunning = false;
            }
        }

        void CaptureState()
        {
            savedPosition = motor.transform.position;
            savedRotation = motor.transform.rotation;
            savedVisualRotation = motor.visual.rotation;
            if (motor.aimMarker) savedAimMarker = motor.aimMarker.position;
            savedMotorEnabled = motor.enabled;
            savedCombatEnabled = combat.enabled;
            savedCasterEnabled = caster.enabled;
            savedDirectorEnabled = director && director.enabled;
            savedTimeScale = Time.timeScale;
            savedHold = combat.holdToAttack;
            captured = true;
        }

        void BuildArena()
        {
            arena = new GameObject("AstraiaValidationArena_RuntimeOnly");
            arena.transform.position = arenaOrigin;
            Cube("ValidationFloor", arenaOrigin + Vector3.down * .25f, new Vector3(60, .5f, 60));
            Physics.SyncTransforms();
        }

        GameObject Cube(string label, Vector3 position, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = label;
            cube.transform.SetParent(arena.transform);
            cube.transform.position = position;
            cube.transform.localScale = size;
            return cube;
        }

        void ResetPlayer()
        {
            combat.SetAutomationInput(false);
            motor.ResetAt(arenaOrigin + Vector3.up * .05f);
            motor.visual.rotation = Quaternion.LookRotation(Vector3.forward);
            Move(Vector2.zero);
        }

        void Move(Vector2 worldInput, bool walking = false, bool dash = false)
        {
            Vector3 world = new Vector3(worldInput.x, 0, worldInput.y);
            Transform cameraTransform = motor.viewCamera ? motor.viewCamera.transform : null;
            Vector3 right = PlayerMotor.CameraRelative(Vector2.right, cameraTransform);
            Vector3 forward = PlayerMotor.CameraRelative(Vector2.up, cameraTransform);
            Vector2 relativeInput = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, forward));
            motor.SetAutomationInput(relativeInput, arenaOrigin + Vector3.forward * 5, dash, walking);
        }

        static IEnumerator Wait(float duration)
        {
            // Bounded scaled waits keep the test independent of editor polling frequency.
            for (float remaining = duration; remaining > 0; remaining -= .1f)
                yield return new WaitForSeconds(Mathf.Min(.1f, remaining));
        }

        void Check(string label, bool passed, string expected, string observed)
        {
            Result.checks.Add(new CheckResult { name = label, passed = passed, expected = expected, observed = observed });
        }

        void RestoreState()
        {
            if (!captured) return;
            captured = false;
            if (combat) { combat.CancelAttack(); combat.ReleaseAutomation(); combat.holdToAttack = savedHold; }
            if (motor)
            {
                motor.ResetAt(savedPosition);
                motor.transform.rotation = savedRotation;
                if (motor.visual) motor.visual.rotation = savedVisualRotation;
                if (motor.aimMarker) motor.aimMarker.position = savedAimMarker;
                motor.ReleaseAutomation();
                motor.enabled = savedMotorEnabled;
            }
            if (combat) combat.enabled = savedCombatEnabled;
            if (caster) caster.enabled = savedCasterEnabled;
            if (director) director.enabled = savedDirectorEnabled;
            Time.timeScale = savedTimeScale;
            foreach (var projectile in FindObjectsByType<TalismanProjectile>(FindObjectsSortMode.None))
                if ((projectile.transform.position - arenaOrigin).sqrMagnitude < 10000) Destroy(projectile.gameObject);
            foreach (var flame in FindObjectsByType<SpiritFlame>(FindObjectsSortMode.None))
                if ((flame.transform.position - arenaOrigin).sqrMagnitude < 10000) Destroy(flame.gameObject);
            if (arena) Destroy(arena);
            var cameraRig = FindFirstObjectByType<IsometricFollowCamera>();
            if (cameraRig) cameraRig.Snap();
        }

        void SaveReport()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, JsonUtility.ToJson(Result, true));
                var text = new StringBuilder("Astraia play validation: " + Result.status + "\n");
                foreach (var check in Result.checks)
                    text.AppendLine((check.passed ? "PASS" : "FAIL") + " " + check.name + " | expected: " + check.expected + " | observed: " + check.observed);
                File.WriteAllText(Path.ChangeExtension(ReportPath, ".txt"), text.ToString());
                Debug.Log("Astraia play validation " + Result.status + ": " + ReportPath, this);
            }
            catch (Exception ex) { Debug.LogError("Could not save Astraia validation report: " + ex, this); }
        }

        void OnDisable()
        {
            if (!IsRunning) return;
            StopAllCoroutines();
            RestoreState();
            if (IsRunning && Result != null)
            {
                Check("Validation interrupted", false, "validation reaches completion", "component disabled before completion");
                Result.status = "failed";
                Result.finishedUtc = DateTime.UtcNow.ToString("o");
                SaveReport();
                IsRunning = false;
            }
        }
    }
}
