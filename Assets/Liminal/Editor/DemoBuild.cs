using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AcRoguelike.Liminal.Editor
{
    public static class DemoBuild
    {
        public const string EntryScene = "Assets/Liminal/Scenes/LiminalRun.unity";

        [Serializable] sealed class Report
        {
            public string status, utc, unityVersion, output;
            public int errors, warnings;
            public ulong bytes;
            public double seconds;
        }

        [MenuItem("Tools/Liminal/Build Windows Demo")]
        public static void BuildWindows()
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
                    if (new FileInfo(path).Length <= 256 && File.ReadAllText(path).StartsWith("version https://git-lfs.github.com/spec/v1"))
                        throw new InvalidOperationException("Run git lfs pull before building. Missing asset: " + path);
                var catalog = Resources.Load<GateMissionCatalog>("GateMissions");
                if (!catalog || catalog.Missions.Count != 2 || catalog.Missions.Any(m => !m.IsAvailable)
                    || !catalog.Missions.Any(m => m.id == "liminal_campaign")
                    || !catalog.Missions.Any(m => m.id == "game_exhibition"))
                    throw new InvalidOperationException("Demo mission catalog must contain the completed Liminal and Game missions.");
                string output = Path.GetFullPath("Builds/PirateAllianceDemo/PirateAllianceDemo.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { EntryScene },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                var summary = result.summary;
                Directory.CreateDirectory("Documentation/Demo");
                File.WriteAllText("Documentation/Demo/build-report.json", JsonUtility.ToJson(new Report
                {
                    status = summary.result.ToString(), utc = DateTime.UtcNow.ToString("O"),
                    unityVersion = Application.unityVersion, output = output,
                    errors = summary.totalErrors, warnings = summary.totalWarnings,
                    bytes = summary.totalSize, seconds = summary.totalTime.TotalSeconds
                }, true));
                if (summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Demo build failed: " + summary.result);
                Debug.Log("DEMO_BUILD_SUCCEEDED: " + output);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
    }
}
