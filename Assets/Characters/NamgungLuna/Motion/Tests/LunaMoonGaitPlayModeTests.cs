using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace NamgungLuna.Motion.Tests
{
    /// <summary>
    /// Runs the moon gait demo's scripted tour (idle, walk, curved run with a dash, glide stop, chained dashes, a dash from a
    /// standstill, run) and checks the Columbina-style motion: the boots never sink into the floor, she hovers at rest,
    /// every bound ends in a toe touch with a ripple, running uses the fast motion (deep lean), and each dash covers its
    /// distance while the body floats over the floor and lands with a ripple. Report: Recordings/LunaMoonGait/.
    /// </summary>
    public class LunaMoonGaitPlayModeTests
    {
        const float Step = 1f / 60f;
        internal static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "LunaMoonGait"));
#if UNITY_EDITOR
        internal static string ScenePath => UnityEditor.AssetDatabase.GUIDToAssetPath(UnityEditor.AssetDatabase.FindAssets("NamgungLuna_MoonGaitDemo t:Scene").First());
#endif

        internal sealed class Rig
        {
            public LunaMoonMotor motor;
            public LunaMoonGait gait;
            public LunaMoonFx fx;
            public LunaMoonShowcase showcase;
            public MagicaCloth[] cloths;
        }

        internal static IEnumerator LoadDemo(Rig rig, float step)
        {
            // Batch mode compiles Burst jobs asynchronously; without this the first cloth build runs as slow managed code.
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            Time.captureDeltaTime = step;
            rig.motor = Object.FindFirstObjectByType<LunaMoonMotor>();
            Assert.NotNull(rig.motor, "LunaMoonMotor missing in the demo scene");
            rig.motor.readKeyboard = false;
            rig.gait = rig.motor.GetComponent<LunaMoonGait>();
            rig.fx = rig.motor.GetComponent<LunaMoonFx>();
            rig.showcase = rig.motor.GetComponent<LunaMoonShowcase>();
            Assert.NotNull(rig.gait, "LunaMoonGait missing");
            Assert.NotNull(rig.showcase, "LunaMoonShowcase missing");
            rig.showcase.idleDelay = 1e6f;   // the test starts the tour itself
            rig.cloths = Object.FindObjectsByType<MagicaCloth>(FindObjectsSortMode.None);
            float start = Time.realtimeSinceStartup;
            while (rig.cloths.Any(c => !c.Process.IsRunning() && !c.Process.Result.IsError()) && Time.realtimeSinceStartup - start < 300f)
                yield return null;
            foreach (var c in rig.cloths) Assert.IsTrue(c.Process.IsRunning(), c.name + " did not build: " + c.Process.Result.GetResultString());
            for (int i = 0; i < 60; i++) yield return null;   // settle
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator ShowcaseFloatsAndTouchesDownCleanly()
        {
            Directory.CreateDirectory(OutDir);
            var rig = new Rig();
            yield return LoadDemo(rig, Step);
            var gait = rig.gait;
            var attack = rig.motor.GetComponent<LunaBasicAttack>();

            var events = new Dictionary<LunaGaitEventKind, int>();
            gait.GaitEvent += e => events[e.kind] = (events.TryGetValue(e.kind, out int n) ? n : 0) + 1;
            int ripplesBefore = rig.fx != null ? rig.fx.RipplesSpawned : 0;
            int stepsBefore = gait.StepCount;

            float lowestGrounded = float.MaxValue, maxSprintPitch = 0f, maxSpeed = 0f, maxLift = 0f;
            var dashDistances = new List<float>();
            var dashSeconds = new List<float>();
            Vector3 dashStart = Vector3.zero, previousPosition = rig.motor.transform.position;
            float dashClock = 0f;
            bool wasDashing = false;
            var hover = new List<float>();
            var contactGaps = new List<float>();
            bool nan = false;
            float worstClothDistance = 0f;
            rig.showcase.Restart();
            int frames = Mathf.CeilToInt(rig.showcase.TotalDuration / Step);
            float previousPhase = gait.Phase;
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                Vector3 body = gait.visual.position;
                if (float.IsNaN(body.x) || float.IsNaN(body.y) || float.IsInfinity(body.y)) nan = true;
                maxSpeed = Mathf.Max(maxSpeed, gait.Speed);
                if (rig.motor.IsGrounded && gait.AirWeight < 0.01f) lowestGrounded = Mathf.Min(lowestGrounded, gait.LowestSoleHeight);
                bool attacking = attack != null && attack.Weight > 0f;   // the basic attack beat poses the legs itself
                if (rig.motor.IsGrounded && gait.MoveWeight < 0.02f && gait.AirWeight < 0.01f && gait.Speed < 0.05f && !attacking) hover.Add(gait.LowestSoleHeight);
                if (gait.SprintWeight > 0.95f && gait.AirWeight < 0.01f) maxSprintPitch = Mathf.Max(maxSprintPitch, gait.BodyPitch);
                // toe height right at each touch of a running bound
                if (gait.MoveWeight > 0.95f && gait.RunWeight > 0.95f && Mathf.FloorToInt(gait.Phase) != Mathf.FloorToInt(previousPhase))
                    contactGaps.Add(gait.LowestSoleHeight);
                previousPhase = gait.Phase;
                maxLift = Mathf.Max(maxLift, gait.DashLiftNow);
                bool dashing = rig.motor.IsDashing;
                // the dash has already moved one frame when IsDashing first reads true: measure from the frame before
                if (dashing && !wasDashing) { dashStart = previousPosition; dashClock = 0f; }
                if (dashing) dashClock += Step;
                previousPosition = rig.motor.transform.position;
                if (!dashing && wasDashing)
                {
                    // the frame that ends the dash still moves by the dash curve
                    dashDistances.Add(Vector3.ProjectOnPlane(rig.motor.transform.position - dashStart, Vector3.up).magnitude);
                    dashSeconds.Add(dashClock);
                }
                wasDashing = dashing;
                if (f % 30 == 0)
                {
                    foreach (var c in rig.cloths)
                        foreach (var r in c.SerializeData.sourceRenderers)
                        {
                            Bounds b = r.bounds;
                            if (float.IsNaN(b.center.x) || float.IsInfinity(b.extents.x)) nan = true;
                            else worstClothDistance = Mathf.Max(worstClothDistance, Vector3.Distance(b.center, gait.visual.position + Vector3.up * 0.9f));
                        }
                }
            }
            int steps = gait.StepCount - stepsBefore;
            int ripples = rig.fx != null ? rig.fx.RipplesSpawned - ripplesBefore : 0;
            float hoverMean = hover.Count > 0 ? hover.Average() : -1f;
            float contactMean = contactGaps.Count > 0 ? contactGaps.Average() : -1f;

            // The floating moon sits 0.8 m behind her: from a camera behind her it must fade, from the front stay solid.
            var moonFade = rig.motor.GetComponent<LunaOcclusionFade>();
            float fadedBehind = -1f, solidInFront = -1f;
            if (moonFade != null)
            {
                Camera cam = Camera.main;
                var orbit = cam.GetComponent<LunaOrbitCamera>();
                if (orbit != null) orbit.enabled = false;
                Transform luna = rig.motor.transform;
                cam.transform.SetPositionAndRotation(luna.position - luna.forward * 4.5f + Vector3.up * 1.4f, Quaternion.LookRotation(luna.forward * 4.5f - Vector3.up * 0.5f));
                for (int i = 0; i < 40; i++) yield return null;
                fadedBehind = moonFade.CurrentFade;
                cam.transform.SetPositionAndRotation(luna.position + luna.forward * 4.5f + Vector3.up * 1.4f, Quaternion.LookRotation(-luna.forward * 4.5f - Vector3.up * 0.5f));
                for (int i = 0; i < 40; i++) yield return null;
                solidInFront = moonFade.CurrentFade;
                if (orbit != null) orbit.enabled = true;
            }

            var report = new StringBuilder("{\n");
            report.AppendLine($"  \"seconds\": {frames * Step:F1},");
            report.AppendLine($"  \"max_speed\": {maxSpeed:F2},");
            report.AppendLine($"  \"lowest_sole_grounded_m\": {lowestGrounded:F4},");
            report.AppendLine($"  \"idle_hover_mean_m\": {hoverMean:F4},");
            report.AppendLine($"  \"idle_hover_samples\": {hover.Count},");
            report.AppendLine($"  \"run_touch_gap_mean_m\": {contactMean:F4},");
            report.AppendLine($"  \"run_touch_samples\": {contactGaps.Count},");
            report.AppendLine($"  \"steps\": {steps},");
            report.AppendLine($"  \"ripples\": {ripples},");
            report.AppendLine($"  \"events\": {{{string.Join(", ", events.Select(kv => $"\"{kv.Key}\": {kv.Value}"))}}},");
            report.AppendLine($"  \"max_sprint_pitch_deg\": {maxSprintPitch:F1},");
            report.AppendLine($"  \"dashes\": {dashDistances.Count},");
            report.AppendLine($"  \"dash_distance_m\": [{string.Join(", ", dashDistances.Select(d => d.ToString("F2")))}],");
            report.AppendLine($"  \"dash_seconds\": [{string.Join(", ", dashSeconds.Select(d => d.ToString("F2")))}],");
            report.AppendLine($"  \"max_dash_lift_m\": {maxLift:F3},");
            report.AppendLine($"  \"worst_cloth_center_distance_m\": {worstClothDistance:F2},");
            report.AppendLine($"  \"moon_fade_behind\": {fadedBehind:F2},");
            report.AppendLine($"  \"moon_fade_front\": {solidInFront:F2},");
            report.AppendLine($"  \"nan\": {nan.ToString().ToLower()}");
            report.AppendLine("}");
            File.WriteAllText(Path.Combine(OutDir, "report.json"), report.ToString(), new UTF8Encoding(false));
            Time.captureDeltaTime = 0f;

            Assert.IsFalse(nan, "NaN in the body or cloth");
            Assert.Greater(lowestGrounded, -0.01f, "a boot sank into the floor");
            Assert.That(hoverMean, Is.InRange(0.02f, 0.09f), "she should hover a few centimetres on pointed toes at rest");
            Assert.That(contactMean, Is.InRange(-0.01f, 0.03f), "running bounds should end with the toe just touching");
            Assert.GreaterOrEqual(steps, 12, "too few toe touches");
            Assert.GreaterOrEqual(ripples, steps, "every touch should leave a ripple");
            Assert.GreaterOrEqual(events.TryGetValue(LunaGaitEventKind.SprintStart, out int sprints) ? sprints : 0, 2, "setting off into the fast run");
            Assert.GreaterOrEqual(events.TryGetValue(LunaGaitEventKind.Dash, out int dashes) ? dashes : 0, 4, "dash bursts");
            Assert.GreaterOrEqual(events.TryGetValue(LunaGaitEventKind.Land, out int lands) ? lands : 0, 4, "every dash should land with a ripple");
            Assert.IsFalse(events.ContainsKey(LunaGaitEventKind.Jump), "there is no jump any more");
            Assert.GreaterOrEqual(maxSprintPitch, 15f, "the default run is the fast, deeply leaning motion");
            Assert.AreEqual(4, dashDistances.Count, "dashes in the tour");
            foreach (float d in dashDistances) Assert.That(d, Is.InRange(rig.motor.dashDistance * 0.95f, rig.motor.dashDistance * 1.05f), "dash distance");
            Assert.GreaterOrEqual(maxLift, 0.25f, "the body should float up during a dash");
            Assert.Less(worstClothDistance, 1.5f, "cloth blew away from the body");
            Assert.NotNull(moonFade, "LunaOcclusionFade missing");
            Assert.Less(fadedBehind, 0.4f, "the moon should fade while it hides her");
            Assert.Greater(solidInFront, 0.95f, "the moon should be solid when it is not in the way");
        }
    }

    /// <summary>
    /// Records the scripted tour as numbered PNG frames: a side camera that tracks her (left, shows the bounds) and the
    /// demo's own orbit camera (right). Run explicitly: -testFilter NamgungLuna.Motion.Tests.LunaMoonGaitVideoCapture
    /// Frames: Recordings/LunaMoonGait/video/ (encode with ffmpeg at 30 fps).
    /// </summary>
    [Explicit("records video frames")]
    public class LunaMoonGaitVideoCapture
    {
        const int Fps = 30, Width = 640, Height = 720;

        [UnityTest, Timeout(3600000)]
        public IEnumerator RecordShowcase()
        {
            string dir = Path.Combine(LunaMoonGaitPlayModeTests.OutDir, "video");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "f_*.png")) File.Delete(f);
            var rig = new LunaMoonGaitPlayModeTests.Rig();
            yield return LunaMoonGaitPlayModeTests.LoadDemo(rig, 1f / Fps);

            Camera game = Camera.main;
            // the game camera is rendered by hand below, so Camera.main is null while recording
            var moonFade = rig.motor.GetComponent<LunaOcclusionFade>();
            if (moonFade != null) moonFade.viewCamera = game;
            var sideGo = new GameObject("VideoSide");
            Camera side = sideGo.AddComponent<Camera>();
            side.CopyFrom(game);
            side.fieldOfView = 34f;
            side.enabled = false;
            if (game != null) game.enabled = false;
            var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
            var frame = new Texture2D(Width * 2, Height, TextureFormat.RGB24, false);
            Vector3 sideDir = Vector3.right;
            Transform luna = rig.motor.transform;

            rig.showcase.Restart();
            int frames = Mathf.CeilToInt((rig.showcase.TotalDuration + 0.5f) * Fps);
            for (int n = 0; n < frames; n++)
            {
                yield return null;

                Vector3 velocity = Vector3.ProjectOnPlane(rig.motor.Velocity, Vector3.up);
                Vector3 heading = velocity.magnitude > 0.5f ? velocity.normalized : luna.forward;
                Vector3 wantSide = Vector3.Cross(Vector3.up, heading);
                if (Vector3.Dot(wantSide, sideDir) < 0f) wantSide = -wantSide;   // stay on one side, no flips
                sideDir = Vector3.Slerp(sideDir, wantSide, 0.08f).normalized;
                Vector3 focus = luna.position + Vector3.up * 0.8f;
                side.transform.SetPositionAndRotation(focus + sideDir * 4.4f + Vector3.up * 0.25f, Quaternion.LookRotation(-sideDir - Vector3.up * 0.05f));
                Render(side, rt, frame, 0);
                Render(game, rt, frame, Width);
                frame.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, $"f_{n:0000}.png"), frame.EncodeToPNG());
            }
            if (game != null) game.enabled = true;
            Object.Destroy(sideGo);
            rt.Release();
            Time.captureDeltaTime = 0f;
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D frame, int x)
        {
            if (cam == null) return;
            var old = cam.targetTexture;
            float aspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = (float)Width / Height;
            cam.Render();
            cam.targetTexture = old;
            cam.aspect = aspect;
            RenderTexture.active = rt;
            frame.ReadPixels(new Rect(0, 0, Width, Height), x, 0, false);
            RenderTexture.active = null;
        }
    }
}
