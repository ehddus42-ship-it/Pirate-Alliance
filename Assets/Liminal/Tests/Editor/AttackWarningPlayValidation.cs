#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AcRoguelike.GameTheme.Editor;
using AcRoguelike.RuinsBoss.Editor;
using AcRoguelike.Ruins.Editor;
using UnityEditor;
using UnityEngine;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>Runs existing isolated combat arenas without opening, saving or replacing an authored scene.</summary>
    public static class AttackWarningPlayValidation
    {
        public const string ReportPath = "Library/AttackWarningValidation/play-report.json";
        const double MaximumSeconds = 165;
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static readonly FieldInfo WaitSeconds = typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic);
        static Report report;
        static object pending;
        static float resumeAt, originalTimeScale;
        static int lastFrame;
        static double startedAt;
        public static bool IsRunning { get; private set; }

        [Serializable] sealed class Report
        {
            public string status = "running", utc = DateTime.UtcNow.ToString("O"), currentSuite;
            public double elapsedSeconds;
            public int completedSuites;
            public List<string> checks = new List<string>(), errors = new List<string>();
        }

        public static void Start()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter PlayMode before starting attack warning validation.");
            if (IsRunning) throw new InvalidOperationException("Attack warning validation is already running.");
            report = new Report();
            startedAt = EditorApplication.timeSinceStartup;
            originalTimeScale = Time.timeScale;
            if (Time.timeScale <= 0) Time.timeScale = 1;
            pending = null;
            lastFrame = -1;
            routines.Push(RunAll());
            IsRunning = true;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Save();
        }

        static IEnumerator RunAll()
        {
            report.currentSuite = nameof(RuinsProjectileValidation);
            Save();
            yield return RuinsProjectileValidation.Run(Record);
            report.completedSuites++;
            report.currentSuite = nameof(GameTetrominoProjectileValidation);
            Save();
            yield return GameTetrominoProjectileValidation.Run(Record);
            report.completedSuites++;
            report.currentSuite = nameof(RuinsBossProjectileValidation);
            Save();
            yield return RuinsBossProjectileValidation.Run(Record);
            report.completedSuites++;
            report.currentSuite = nameof(RuinsBossMachineValidation);
            Save();
            yield return RuinsBossMachineValidation.Run(Record);
            report.completedSuites++;
        }

        static void Record(string message)
        {
            report.checks.Add(report.currentSuite + ": " + message);
            Save();
        }

        static void Tick()
        {
            if (!IsRunning) return;
            try
            {
                if (!Application.isPlaying) throw new InvalidOperationException("PlayMode ended before validation completed.");
                if (EditorApplication.timeSinceStartup - startedAt > MaximumSeconds)
                    throw new TimeoutException("Attack warning validation exceeded its 165-second wall-clock limit.");
                // Arena.Step simulates its own physics scene once per game frame, including while timeScale is zero.
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (pending is WaitForSeconds && Time.time < resumeAt) return;
                if (pending is CustomYieldInstruction custom && custom.keepWaiting) return;
                if (pending is AsyncOperation operation && !operation.isDone) return;
                pending = null;

                for (int immediate = 0; immediate < 100; immediate++)
                {
                    if (routines.Count == 0) { Finish(null); return; }
                    var routine = routines.Peek();
                    if (!routine.MoveNext())
                    {
                        routines.Pop();
                        (routine as IDisposable)?.Dispose();
                        continue;
                    }
                    object yielded = routine.Current;
                    if (yielded is WaitForSeconds seconds)
                    {
                        if (WaitSeconds == null) throw new NotSupportedException("This Unity version does not expose WaitForSeconds duration.");
                        resumeAt = Time.time + (float)WaitSeconds.GetValue(seconds);
                        pending = yielded;
                        return;
                    }
                    if (yielded is CustomYieldInstruction || yielded is AsyncOperation)
                    {
                        pending = yielded;
                        return;
                    }
                    if (yielded is IEnumerator child) { routines.Push(child); continue; }
                    if (yielded != null) throw new NotSupportedException("Unsupported validation yield: " + yielded.GetType().FullName);
                    return;
                }
                throw new InvalidOperationException("Validation yielded too many immediate nested steps in one frame.");
            }
            catch (Exception exception) { Finish(exception); }
        }

        static void PlayModeChanged(PlayModeStateChange state)
        {
            if (IsRunning && state == PlayModeStateChange.ExitingPlayMode)
                Finish(new OperationCanceledException("PlayMode was stopped during validation."));
        }

        static void BeforeReload()
        {
            if (IsRunning) Finish(new OperationCanceledException("Assembly reload interrupted validation."));
        }

        static void Finish(Exception failure)
        {
            if (!IsRunning) return;
            IsRunning = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            if (failure != null) report.errors.Add(report.currentSuite + ": " + failure);
            // Dispose every active iterator so each arena's using/finally cleanup runs even after a failed check.
            while (routines.Count > 0)
            {
                try { (routines.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { report.errors.Add("Arena cleanup: " + exception); }
            }
            pending = null;
            HitFeedback.CancelHitStop();
            Time.timeScale = originalTimeScale;
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            Save();
            Debug.Log("ATTACK_WARNING_PLAY_VALIDATION " + report.status + ": " + report.checks.Count + " checks; " + ReportPath);
        }

        static void Save()
        {
            report.elapsedSeconds = EditorApplication.timeSinceStartup - startedAt;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
    }
}
#endif
