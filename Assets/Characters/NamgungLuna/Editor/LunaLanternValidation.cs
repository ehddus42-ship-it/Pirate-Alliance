#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NamgungLuna.Motion;
using UnityEditor;
using UnityEngine;

namespace NamgungLuna.Editor
{
    /// <summary>Measures the battle prefab's actual lantern pivot without entering Play mode or saving the prefab.</summary>
    public static class LunaLanternValidation
    {
        const string PrefabPath = "Assets/Characters/NamgungLuna/Stage/NamgungLuna_Battle.prefab";
        const string Output = "Documentation/NamgungLuna/Validation/lantern-validation.json";
        const float ActiveSeconds = 4f, SettleSeconds = 2f;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] sealed class Measurement
        {
            public string scenario, cadence;
            public bool allFinite = true;
            public bool residualFinite;
            public bool angularSpeedLimitApplied;
            public int frameCount, nonFiniteFrames;
            public float maxAngleDegrees, maxFrameAngularSpeedDegreesPerSecond, residualAngleAfterTwoSeconds;
        }

        [Serializable] sealed class Report
        {
            public string prefab = PrefabPath;
            public string status = "running", error;
            public string angleSpace = "lantern local rotation relative to its authored rest rotation";
            public string input = "Root translation/yaw and visual pitch; actual ReadMotion/UpdateWeights with transform-derived velocity, then UpdateLantern.";
            public float activeSeconds = ActiveSeconds, settleSeconds = SettleSeconds;
            public float configuredSmoothTime, configuredMaxAngle;
            public float maxResidualAngleDegrees = 1f, maxNormalAngularSpeedDegreesPerSecond = 95f;
            public bool allFinite = true;
            public List<Measurement> measurements = new List<Measurement>();
            public List<string> failures = new List<string>();
        }

        static readonly string[] Scenarios = { "idle", "straight_4.2mps", "accelerate_stop", "turn_90_degrees", "dash_13mps", "sprint_22deg_banked", "large_teleport", "reactivate_OnEnable", "long_frame_0.35s" };
        static readonly string[] Cadences = { "30fps", "60fps", "120fps", "mixed" };
        static readonly float[] MixedSteps = { 1f / 30f, 1f / 120f, 1f / 60f, .041f, .008f, .05f };

        [MenuItem("Tools/Namgung Luna/Stage Capture/Measure Lantern")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Measure the lantern from Edit mode.");
            var report = new Report();
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                var gait = contents.GetComponentInChildren<LunaMoonGait>(true);
                if (!gait || !gait.visual || !gait.lantern || !gait.lantern.parent)
                    throw new InvalidOperationException("The battle prefab must contain its gait, visual and lantern pivot.");
                report.configuredSmoothTime = gait.lanternSmoothTime;
                report.configuredMaxAngle = gait.lanternMaxAngle;
                Vector3 rootPosition = gait.transform.position, visualPosition = gait.visual.localPosition;
                Quaternion rootRotation = gait.transform.rotation, visualRotation = gait.visual.localRotation;
                Quaternion lanternRest = gait.lantern.localRotation;
                foreach (string scenario in Scenarios)
                foreach (string cadence in Cadences)
                {
                    gait.transform.SetPositionAndRotation(rootPosition, rootRotation);
                    gait.visual.SetLocalPositionAndRotation(visualPosition, visualRotation);
                    gait.lantern.localRotation = lanternRest;
                    Invoke(gait, "Awake");
                    Invoke(gait, "ResetState");
                    // Isolate the pivot from gameplay motor/cloth updates, using the gait's real transform reader.
                    Set(gait, "source", null);
                    Set(gait, "clock", 0f);
                    Invoke(gait, "UpdateLantern", 1f / 60f);
                    Set(gait, "initialized", true);
                    var measurement = Measure(gait, scenario, cadence, rootPosition, rootRotation,
                        visualPosition, visualRotation, lanternRest);
                    report.measurements.Add(measurement);
                    report.allFinite &= measurement.allFinite;
                    Validate(report, measurement);
                }
                report.status = report.failures.Count == 0 ? "passed" : "failed";
                if (report.failures.Count > 0)
                    throw new InvalidOperationException("Lantern validation failed:\n" + string.Join("\n", report.failures));
            }
            catch (Exception exception)
            {
                report.status = "failed";
                report.error = exception.ToString();
                if (report.failures.Count == 0) report.failures.Add(exception.Message);
                throw;
            }
            finally
            {
                if (contents) PrefabUtility.UnloadPrefabContents(contents);
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(Output, JsonUtility.ToJson(report, true));
            }
            Debug.Log($"[LunaLanternValidation] {report.status}: {report.measurements.Count} scenarios, finite={report.allFinite}. {Output}");
        }

        static Measurement Measure(LunaMoonGait gait, string scenario, string cadence, Vector3 rootPosition,
            Quaternion rootRotation, Vector3 visualPosition, Quaternion visualRotation, Quaternion lanternRest)
        {
            var result = new Measurement { scenario = scenario, cadence = cadence,
                angularSpeedLimitApplied = scenario != "large_teleport" && scenario != "reactivate_OnEnable" && scenario != "long_frame_0.35s" };
            Vector3 displacement = Vector3.zero;
            Quaternion previous = gait.lantern.localRotation;
            float time = 0f;
            bool teleported = false, reactivated = false, longFrameApplied = false;
            while (time < ActiveSeconds + SettleSeconds - .00001f)
            {
                float step = Step(cadence, result.frameCount);
                if (scenario == "long_frame_0.35s" && !longFrameApplied && time >= 1.5f)
                {
                    step = .35f;
                    longFrameApplied = true;
                }
                float dt = Mathf.Min(step, ActiveSeconds + SettleSeconds - time);
                float sampleTime = time + dt * .5f;
                bool active = sampleTime < ActiveSeconds;
                float speed = 0f, yaw = 0f, pitch = 0f, roll = 0f, lift = 0f;
                if (scenario == "straight_4.2mps" || scenario == "reactivate_OnEnable" || scenario == "long_frame_0.35s")
                    speed = active ? 4.2f : 0f;
                else if (scenario == "accelerate_stop")
                    speed = active ? 4.2f * Mathf.Clamp01((sampleTime - .5f) / .6f) * (1f - Mathf.Clamp01((sampleTime - 2.7f) / .6f)) : 0f;
                else if (scenario == "turn_90_degrees")
                {
                    speed = active ? 4.2f : 0f;
                    yaw = 90f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sampleTime - 1.8f) / .35f));
                }
                else if (scenario == "sprint_22deg_banked")
                {
                    speed = active ? 4.2f : 0f;
                    // The stage uses the deep sprint lean at its 4.2 m/s base speed.
                    // Sweep through both +90 and -90 degrees before settling at the original heading.
                    yaw = active ? 90f * Mathf.Sin(sampleTime * 2f * Mathf.PI / ActiveSeconds) : 0f;
                }
                else if (scenario == "dash_13mps")
                {
                    speed = sampleTime >= 1f && sampleTime < 1.19f ? 13f : 0f;
                    float leap = Mathf.Clamp01((sampleTime - 1f) / .3f);
                    lift = .32f * Mathf.Sin(leap * Mathf.PI);
                    pitch = 14f * Mathf.Sin(leap * Mathf.PI);
                }
                else if (scenario == "large_teleport" && !teleported && sampleTime >= 1.5f)
                {
                    displacement += new Vector3(1000f, 50f, -750f);
                    teleported = true; // Deliberately do not ResetState: measure discontinuity handling.
                }
                if (scenario == "idle" && active) pitch = .6f * Mathf.Sin(sampleTime * 2f * Mathf.PI * .29f);
                else if (scenario == "sprint_22deg_banked")
                {
                    pitch = active ? 22f : 0f;
                    roll = active ? 14f * Mathf.Sin(sampleTime * 2f * Mathf.PI * .5f) : 0f;
                }
                else if (scenario != "dash_13mps") pitch = speed > 0f ? 9f * Mathf.Min(1f, speed / 4.2f) : 0f;
                Quaternion yawRotation = rootRotation * Quaternion.Euler(0f, yaw, 0f);
                displacement += yawRotation * Vector3.forward * (speed * dt);
                gait.transform.SetPositionAndRotation(rootPosition + displacement, yawRotation);
                gait.visual.SetLocalPositionAndRotation(visualPosition + Vector3.up * lift, visualRotation * Quaternion.Euler(pitch, 0f, roll));
                if (scenario == "reactivate_OnEnable" && !reactivated && sampleTime >= 1.5f)
                {
                    Invoke(gait, "OnEnable");
                    reactivated = true;
                }
                time += dt;
                Set(gait, "clock", time);
                Invoke(gait, "ReadMotion", dt);
                Invoke(gait, "UpdateWeights", dt);
                Invoke(gait, "UpdateLantern", dt);
                Set(gait, "initialized", true);
                Quaternion rotation = gait.lantern.localRotation;
                float angle = Quaternion.Angle(lanternRest, rotation);
                float angularSpeed = Quaternion.Angle(previous, rotation) / dt;
                bool finite = Finite(rotation.x) && Finite(rotation.y) && Finite(rotation.z) && Finite(rotation.w)
                    && Finite(angle) && Finite(angularSpeed) && Finite(gait.lantern.position);
                result.frameCount++;
                result.allFinite &= finite;
                result.residualFinite = finite;
                if (!finite) result.nonFiniteFrames++;
                // Keep the JSON valid even if the simulated component produces NaN or infinity.
                if (finite)
                {
                    result.maxAngleDegrees = Mathf.Max(result.maxAngleDegrees, angle);
                    result.maxFrameAngularSpeedDegreesPerSecond = Mathf.Max(result.maxFrameAngularSpeedDegreesPerSecond, angularSpeed);
                    result.residualAngleAfterTwoSeconds = angle;
                }
                previous = rotation;
            }
            return result;
        }

        static void Validate(Report report, Measurement result)
        {
            string label = result.scenario + " / " + result.cadence;
            if (!result.allFinite || !result.residualFinite)
                report.failures.Add(label + ": non-finite lantern rotation or measurement.");
            if (result.maxAngleDegrees > report.configuredMaxAngle + .1f)
                report.failures.Add($"{label}: maximum angle {result.maxAngleDegrees:F3} exceeds {report.configuredMaxAngle + .1f:F3} degrees.");
            if (result.residualAngleAfterTwoSeconds > report.maxResidualAngleDegrees)
                report.failures.Add($"{label}: residual angle {result.residualAngleAfterTwoSeconds:F3} exceeds {report.maxResidualAngleDegrees:F3} degrees after two seconds.");
            if (result.angularSpeedLimitApplied && result.maxFrameAngularSpeedDegreesPerSecond > report.maxNormalAngularSpeedDegreesPerSecond)
                report.failures.Add($"{label}: angular speed {result.maxFrameAngularSpeedDegreesPerSecond:F3} exceeds {report.maxNormalAngularSpeedDegreesPerSecond:F3} degrees/s.");
        }

        static float Step(string cadence, int frame)
            => cadence == "30fps" ? 1f / 30f : cadence == "60fps" ? 1f / 60f
                : cadence == "120fps" ? 1f / 120f : MixedSteps[frame % MixedSteps.Length];
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        static void Invoke(LunaMoonGait gait, string name, params object[] arguments)
        {
            var method = typeof(LunaMoonGait).GetMethod(name, PrivateInstance | BindingFlags.Public);
            if (method == null) throw new MissingMethodException(typeof(LunaMoonGait).FullName, name);
            method.Invoke(gait, arguments);
        }

        static void Set(LunaMoonGait gait, string name, object value)
        {
            var field = typeof(LunaMoonGait).GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(typeof(LunaMoonGait).FullName, name);
            field.SetValue(gait, value);
        }
    }
}
#endif
