#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonLuna.Editor
{
    /// <summary>Runs the existing real-roster combat checks, preserving the validator's saved player preferences.</summary>
    [InitializeOnLoad]
    public static class LunaCombatValidationBatch
    {
        const string Key = "Luna.Combat.Batch";
        static readonly string ReportPath = LunaCombatValidation.Output + "/combat-validation.json";
        static LunaCombatValidationBatch()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
        }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new System.InvalidOperationException("Use the interactive Validate Combat menu in an open editor.");
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The validator requires a saved scene and restores it when finished.
            EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update += Poll;
            LunaCombatValidation.Start();
        }
        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || LunaCombatValidation.Running) return;
            EditorApplication.update -= Poll; SessionState.SetBool(Key, false);
            string json = File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : "(no report)";
            bool passed = json.Contains("\"status\": \"passed\"");
            Debug.Log("Luna combat validation " + (passed ? "passed" : "failed") + "\n" + json);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
