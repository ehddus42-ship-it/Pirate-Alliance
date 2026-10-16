#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NamgungLuna.Editor
{
    /// <summary>
    /// Runs Tools > Namgung Luna > Stage Capture > Validate Both Forms from the command line and exits with its result:
    /// Unity -batchmode -projectPath . -executeMethod NamgungLuna.Editor.LunaStageValidationBatch.Run
    /// The validation itself enters Play mode in LiminalRun, so this waits across the domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class LunaStageValidationBatch
    {
        const string Key = "Luna.Stage.Batch";
        const string ReportPath = "Documentation/NamgungLuna/Validation/movement-validation.json";

        static LunaStageValidationBatch()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.update += Poll;
            // Batch mode runs unthrottled at thousands of frames per second: a slow walk would then move less than the
            // CharacterController's minMoveDistance each frame and not move at all. Simulate at 60 fps instead.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Time.captureDeltaTime = 1f / 60f;
            };
        }

        public static void Run()
        {
            if (File.Exists(ReportPath)) File.Delete(ReportPath);   // never read an old result
            SessionState.SetBool(Key, true);
            EditorApplication.update += Poll;
            LunaStageValidation.Start();
        }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || LunaStageValidation.Running) return;
            EditorApplication.update -= Poll;
            SessionState.SetBool(Key, false);
            string json = File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : "(no report)";
            bool passed = json.Contains("\"status\": \"passed\"");
            Debug.Log("[Namgung Luna] Stage validation " + (passed ? "passed" : "failed") + "\n" + json);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
