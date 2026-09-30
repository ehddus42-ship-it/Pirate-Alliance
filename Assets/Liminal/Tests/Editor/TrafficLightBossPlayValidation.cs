using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AcRoguelike.Liminal.EditorTests
{
    /// <summary>Plays the boss showcase and exercises every rule through the normal player, health and attack APIs.</summary>
    [InitializeOnLoad]
    public static class TrafficLightBossPlayValidation
    {
        const string ActiveKey = "Liminal.TrafficLightBoss.Validation.Active";
        const string ScenePath = "Assets/Liminal/Scenes/TrafficLightBossShowcase.unity";
        const string Folder = "Documentation/Liminal/Concepts/TrafficLightBoss";
        [Serializable] public sealed class Report
        {
            public string status = "running", utc, unityVersion;
            public List<string> lampSequence = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<string> checks = new List<string>(), errors = new List<string>();
        }
        sealed class Wait { public Func<bool> condition; public float timeout; public string label; }

        static Report report;
        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        // Game time, not wall time: a background editor renders slowly and Time.deltaTime is capped.
        static float wakeAt, deadline;
        static Wait waiting;
        static bool finishing;

        static TrafficLightBossPlayValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += mode =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (mode == PlayModeStateChange.EnteredPlayMode)
                {
                    report = new Report { utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion };
                    stack.Clear(); stack.Push(Routine()); waiting = null; wakeAt = 0; finishing = false;
                }
                else if (mode == PlayModeStateChange.EnteredEditMode) Conclude();
            };
        }

        [MenuItem("AC Roguelike/Liminal/Traffic Light Boss/Run Play Validation")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (report == null || stack.Count == 0 || !EditorApplication.isPlaying || finishing) return;
            if (waiting != null)
            {
                if (waiting.condition()) waiting = null;
                else if (Time.time > deadline) { Fail("Timed out waiting for: " + waiting.label); waiting = null; }
                else return;
            }
            else if (Time.time < wakeAt) return;
            try
            {
                var top = stack.Peek();
                if (!top.MoveNext()) stack.Pop();
                else if (top.Current is float seconds) wakeAt = Time.time + seconds;
                else if (top.Current is Wait wait) { waiting = wait; deadline = Time.time + wait.timeout; }
                else if (top.Current is IEnumerator nested) stack.Push(nested);
            }
            catch (Exception exception) { Fail(exception.ToString()); stack.Clear(); }
            if (stack.Count == 0) { finishing = true; EditorApplication.isPlaying = false; }
        }

        static void Conclude()
        {
            SessionState.SetBool(ActiveKey, false);
            if (report == null) return;
            if (report.status == "running") report.status = report.errors.Count == 0 ? "passed" : "failed";
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/validation.json", JsonUtility.ToJson(report, true));
            Debug.Log("TRAFFIC_LIGHT_BOSS_VALIDATION: " + report.status + " (" + report.checks.Count + " checks, " + report.errors.Count + " errors)");
            report = null;
        }

        static void Check(bool ok, string message) { if (ok) report.checks.Add(message); else report.errors.Add("FAILED: " + message); }
        static void Fail(string message) { report.errors.Add(message); }
        static Wait Until(Func<bool> condition, float timeout, string label) => new Wait { condition = condition, timeout = timeout, label = label };
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static IEnumerator Shot(string name)
        {
            Directory.CreateDirectory(Folder + "/Review");
            ScreenCapture.CaptureScreenshot(Folder + "/Review/" + name + ".png");
            report.screenshots.Add(name + ".png");
            yield return .35f;
        }

        static IEnumerator Routine()
        {
            var boss = UnityEngine.Object.FindFirstObjectByType<TrafficLightBoss>();
            var health = UnityEngine.Object.FindFirstObjectByType<LiminalPlayerHealth>();
            var motor = health ? health.GetComponent<PlayerMotor>() : null;
            if (!boss || !health || !motor) { Fail("Showcase needs the boss, the player health and a PlayerMotor."); yield break; }
            var body = boss.GetComponent<CharacterController>();
            // The player's auto-aimed talismans would otherwise chip the boss into its second phase early.
            foreach (var attack in health.GetComponents<MonoBehaviour>()) if (attack is TalismanCaster || attack is PlayerCombat) attack.enabled = false;
            System.Func<bool> ready = () => boss.State == TrafficLightBossState.Idle || boss.State == TrafficLightBossState.Walk;
            void Reset(Vector3 position) { motor.ResetAt(position + Vector3.up * .05f); Physics.SyncTransforms(); }

            // --- Dormant signal ---------------------------------------------------------------------------------
            // The scene may already have woken the boss; restart it from a true dormant state.
            boss.enabled = false; boss.detectionRadius = 8; boss.attackRange = 0;
            Reset(new Vector3(0, 0, -16));
            boss.Initialize(health, null, 0);
            yield return .3f;
            boss.enabled = true;
            yield return .6f;
            Check(boss.State == TrafficLightBossState.Dormant && !boss.Health.CanBeTargeted, "Boss waits as a dormant, untargetable signal until the player is near.");
            Check(boss.limbRenderers.All(r => !r.enabled) && Mathf.Approximately(body.height, TrafficLightBossRig.DormantHeight), "Dormant boss hides the wire limbs and is a 7.2m signal.");
            Check(boss.Lamp == TrafficLampColor.Off && boss.lampLenses.All(l => !l.enabled), "All three lamps are dark while dormant.");

            // --- Awakening ---------------------------------------------------------------------------------------
            Reset(boss.transform.position + boss.transform.forward * 6);
            yield return Until(() => boss.State == TrafficLightBossState.Awakening, 2, "awakening begins");
            Check(boss.limbRenderers.All(r => r.enabled) && boss.Health.CanBeTargeted, "Awakening reveals all four wire limbs and makes the boss targetable.");
            int flickerStart = boss.LampChanges;
            yield return 1.5f;
            yield return Shot("01_awakening_arms");
            yield return Until(ready, 8, "awakening completes");
            Check(boss.LampChanges - flickerStart >= 6, "Lamps flicker while the boss awakens.");
            Check(Mathf.Abs(body.height - TrafficLightBossRig.StandingHeight) < .05f, "Awake boss stands 12.0m tall.");

            // --- Lamp cycle: red, green, yellow, frequently ------------------------------------------------------
            Reset(new Vector3(0, 0, -16));
            var sequence = new List<TrafficLampColor>(); var changeTimes = new List<float>();
            var last = boss.Lamp; sequence.Add(last); float started = Time.time;
            while (Time.time - started < 7 && ready())
            {
                if (boss.Lamp != last) { last = boss.Lamp; sequence.Add(last); changeTimes.Add(Time.time); }
                yield return 0f;
            }
            report.lampSequence = sequence.Select(c => c.ToString()).ToList();
            bool order = true;
            for (int i = 1; i < sequence.Count; i++)
            {
                var previous = sequence[i - 1]; var expected = previous == TrafficLampColor.Red ? TrafficLampColor.Green : previous == TrafficLampColor.Green ? TrafficLampColor.Yellow : TrafficLampColor.Red;
                if (sequence[i] != expected) order = false;
            }
            float longest = 0; for (int i = 1; i < changeTimes.Count; i++) longest = Mathf.Max(longest, changeTimes[i] - changeTimes[i - 1]);
            Check(sequence.Count >= 6 && order, "Lamps cycle red, green, yellow, red... (" + string.Join(" ", report.lampSequence) + ").");
            Check(longest <= .95f, "Lamp changes stay under one second apart (longest " + longest.ToString("F2") + "s).");

            // --- Pattern 1: red field, feet-based judgement ------------------------------------------------------
            health.ResetHealth(); yield return 1.2f;
            yield return Until(ready, 8, "idle before field");
            Reset(new Vector3(0, 0, -12)); yield return .3f;
            Check(boss.StartAttack(TrafficLightBossState.FieldCast), "Boss can start the red field pattern.");
            var field = boss.field;
            yield return Until(() => field.Phase == RedFieldPhase.Telegraph, 2, "field telegraph");
            boss.GetArena(out Vector3 arenaCenter, out Quaternion arenaRotation, out Vector2 arenaSize);
            var centers = field.Centers.ToArray();
            Check(boss.Lamp == TrafficLampColor.Red, "The signal holds red while the field charges.");
            Check(centers.Length == boss.safeZoneCount && Mathf.Approximately(field.Radius, boss.safeRadius), "The field draws " + centers.Length + " safe circles of radius " + field.Radius + ".");
            Check(centers.All(c => { var l = Quaternion.Inverse(arenaRotation) * (c - arenaCenter); return Mathf.Abs(l.x) <= arenaSize.x * .5f && Mathf.Abs(l.z) <= arenaSize.y * .5f; }), "Every safe circle lies inside the arena.");
            float nearest = centers.Min(c => Vector2.Distance(Flat(c), Flat(motor.transform.position)));
            Check(nearest - field.Radius <= 7.5f, "A safe circle starts within 7.5m of the player (nearest edge " + (nearest - field.Radius).ToString("F1") + "m).");
            yield return .4f;
            yield return Shot("02_field_telegraph");
            Vector3 exposed = Vector3.zero; bool found = false;
            for (float x = -12; x <= 12 && !found; x += 1.5f) for (float z = -16; z <= 16 && !found; z += 1.5f)
            {
                var candidate = arenaCenter + arenaRotation * new Vector3(x, 0, z);
                if (!field.IsSafe(candidate) && Vector2.Distance(Flat(candidate), Flat(boss.transform.position)) > 6) { exposed = candidate; found = true; }
            }
            Check(found, "An exposed floor position exists outside every circle.");
            Reset(exposed);
            int healthBefore = health.Health;
            yield return Until(() => field.Phase == RedFieldPhase.Judgement, 4, "field judgement");
            yield return Shot("03_field_judgement");
            Check(boss.Lamp == TrafficLampColor.Green, "The signal turns green at the judgement.");
            Check(field.IsSafe(centers[0]) && field.IsSafe(centers[0] + Vector3.right * (field.Radius - .05f)) && !field.IsSafe(centers[0] + Vector3.right * (field.Radius + .05f) + Vector3.forward * 0),
                "Circle edge is judged by the feet at the exact radius.");
            yield return .2f;
            Check(health.Health == healthBefore - boss.fieldDamage && boss.FieldHits == 1, "Standing outside the circles costs " + boss.fieldDamage + " health (" + health.Health + " left).");
            yield return Until(() => field.Phase == RedFieldPhase.Hidden, 4, "field clears");

            health.ResetHealth(); yield return 1.2f;
            yield return Until(ready, 8, "idle before safe test");
            Check(boss.StartAttack(TrafficLightBossState.FieldCast), "Second field pattern starts.");
            yield return Until(() => field.Phase == RedFieldPhase.Telegraph, 2, "second telegraph");
            var safePoint = field.Centers[0] + Vector3.right * (field.Radius * .4f);
            Reset(safePoint);
            int hitsBefore = boss.FieldHits;
            yield return Until(() => field.Phase == RedFieldPhase.Judgement, 4, "second judgement");
            yield return 1.0f;
            Check(health.Health == health.maximumHealth && boss.FieldHits == hitsBefore, "Standing inside a safe circle takes no damage.");
            yield return Until(() => field.Phase == RedFieldPhase.Hidden, 4, "field clears again");

            // --- Pattern 2: thrown cars on a locked straight line -------------------------------------------------
            health.ResetHealth(); yield return 1.2f;
            yield return Until(ready, 8, "idle before cars");
            Vector3 playerSpot = new Vector3(0, 0, -12); Reset(playerSpot); yield return .3f;
            int thrown = boss.CarsThrown;
            Check(boss.StartAttack(TrafficLightBossState.CarThrow), "Boss can start the car throw.");
            yield return Until(() => boss.StateTime >= 1.4f, 3, "aim lock");
            Check(boss.Lamp == TrafficLampColor.Yellow, "The signal shows yellow while the car is wound up.");
            yield return Shot("04_car_windup");
            yield return Until(() => boss.CarsThrown == thrown + 1, 4, "first car released");
            var car = UnityEngine.Object.FindFirstObjectByType<TrafficLightCarProjectile>();
            Check(car && car.Launched, "A car projectile exists after the release.");
            float resting = car ? car.halfExtents.y + car.groundClearance + .1f : 1.6f;   // centre height when on the floor; the van is 4m tall
            var samples = new List<Vector3>(); float minY = 99;
            while (car && samples.Count < 900)
            {
                samples.Add(car.transform.position); minY = Mathf.Min(minY, car.transform.position.y);
                if (samples.Count == 25) { report.screenshots.Add("05_car_flight.png"); ScreenCapture.CaptureScreenshot(Folder + "/Review/05_car_flight.png"); }
                yield return 0f;
            }
            Check(samples.Count > 20, "The car stays alive long enough to travel (" + samples.Count + " frames).");
            if (samples.Count > 20)
            {
                Vector2 direction = (Flat(samples[samples.Count - 1]) - Flat(samples[0])).normalized; float worst = 0;
                foreach (var p in samples) { var offset = Flat(p) - Flat(samples[0]); worst = Mathf.Max(worst, Mathf.Abs(offset.x * direction.y - offset.y * direction.x)); }
                Check(worst < .05f, "The car moves along one straight heading (max deviation " + worst.ToString("F3") + "m).");
                Check(samples[0].y > 4 && minY <= resting, "The car drops onto the floor line (" + samples[0].y.ToString("F1") + "m down to " + minY.ToString("F1") + "m, resting " + resting.ToString("F1") + "m).");
            }
            Check(health.Health == health.maximumHealth - boss.carDamage && TrafficLightCarProjectile.HitCount >= 1, "A car on the locked line hits for " + boss.carDamage + " (" + health.Health + " left, hits " + TrafficLightCarProjectile.HitCount + ", player " + motor.transform.position + ", dashing " + motor.IsDashing + ").");

            health.ResetHealth(); yield return 1.2f;
            yield return Until(ready, 8, "idle before dodge test");
            Reset(playerSpot); yield return .3f;
            int impacts = TrafficLightCarProjectile.ImpactCount, hits = TrafficLightCarProjectile.HitCount;
            Check(boss.StartAttack(TrafficLightBossState.CarThrow), "Second car throw starts.");
            yield return Until(() => boss.StateTime >= 1.45f, 3, "second aim lock");
            Reset(playerSpot + new Vector3(7, 0, 0));   // step off the telegraphed line after the aim is fixed
            yield return Until(() => boss.CarsThrown == thrown + 2, 4, "second car released");
            yield return Until(() => !UnityEngine.Object.FindFirstObjectByType<TrafficLightCarProjectile>(), 10, "dodged car leaves the arena");
            Check(health.Health == health.maximumHealth && TrafficLightCarProjectile.HitCount == hits, "Stepping off the locked line avoids the car.");
            Check(TrafficLightCarProjectile.ImpactCount == impacts + 1, "A car that misses is removed at the arena wall.");

            health.ResetHealth(); yield return 1.2f;
            yield return Until(ready, 8, "idle before third car");
            Reset(new Vector3(0, 0, -14));
            Check(boss.StartAttack(TrafficLightBossState.CarThrow), "Third car throw starts.");
            yield return Until(() => boss.CarsThrown == thrown + 3, 5, "third car released");
            Check(boss.CarVariantsUsed == 7, "Sedan, taxi and van all appeared in three throws (mask " + boss.CarVariantsUsed + ").");
            yield return Until(() => !UnityEngine.Object.FindFirstObjectByType<TrafficLightCarProjectile>(), 10, "third car leaves");

            // --- Second phase and defeat -------------------------------------------------------------------------
            health.ResetHealth(); Reset(new Vector3(0, 0, -16)); yield return 1.2f;
            boss.Health.TakeDamage(boss.Health.maxHealth / 2 + 10);
            Check(boss.Enraged, "Below half health the boss enters its second phase.");
            yield return Until(ready, 8, "idle in second phase");
            Check(boss.StartAttack(TrafficLightBossState.FieldCast), "Second-phase field starts.");
            yield return Until(() => field.Phase == RedFieldPhase.Telegraph, 2, "enraged telegraph");
            Check(field.Centers.Length == Mathf.Max(2, boss.safeZoneCount - 1) && field.Radius < boss.safeRadius, "Second phase uses fewer, smaller safe circles (" + field.Centers.Length + " x " + field.Radius.ToString("F2") + "m).");
            boss.Health.TakeDamage(boss.Health.Health);
            yield return .4f;
            Check(boss.State == TrafficLightBossState.Dead && field.Phase == RedFieldPhase.Hidden && boss.Lamp == TrafficLampColor.Off, "Defeat stops the field and darkens the lamps.");
            Check(!UnityEngine.Object.FindFirstObjectByType<TrafficLightCarProjectile>(), "Defeat removes flying cars.");
        }
    }
}
