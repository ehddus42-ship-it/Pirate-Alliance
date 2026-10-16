#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Read-only scene isolation: test a synthetic prop without touching an authored scene.</summary>
    public static class GameThemeOccluderValidation
    {
        [Serializable] sealed class Report
        {
            public string status, utc;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }

        public static void RunBatch()
        {
            var report = new Report { status = "passed", utc = DateTime.UtcNow.ToString("O") };
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                void Check(bool condition, string description)
                {
                    if (!condition) throw new InvalidOperationException(description);
                    report.checks.Add(description);
                }
                var root = new GameObject("OcclusionTestProp");
                SceneManager.MoveGameObjectToScene(root, scene);
                Renderer Box(string name, Vector3 position, Transform parent)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = name;
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetParent(parent, false);
                    go.transform.position = position;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    return go.GetComponent<Renderer>();
                }
                var front = Box("Front", new Vector3(0, .6f, 4), root.transform);
                var side = Box("Side", new Vector3(4, .6f, 4), root.transform);
                var behind = Box("BehindPlayer", new Vector3(0, .6f, 12), root.transform);
                var alreadyHidden = Box("PreHidden", new Vector3(0, .6f, 6), root.transform);
                alreadyHidden.forceRenderingOff = true;
                var disabled = Box("Disabled", new Vector3(0, .6f, 5), root.transform);
                disabled.enabled = false;
                var unrelated = Box("Unrelated", new Vector3(0, .6f, 3), null);
                var cameraObject = new GameObject("OcclusionTestCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0, .6f, 0);
                var player = new GameObject("OcclusionTestPlayer");
                SceneManager.MoveGameObjectToScene(player, scene);
                player.transform.position = new Vector3(0, 0, 10);
                var occluder = root.AddComponent<GameThemeOccluder>();
                occluder.Evaluate(camera, player.transform);
                Check(front.forceRenderingOff, "A renderer between camera and player is cut away.");
                Check(!side.forceRenderingOff && !behind.forceRenderingOff, "Side and behind-player renderers remain visible.");
                Check(!unrelated.forceRenderingOff, "A renderer outside the decorated prop remains untouched.");
                Check(!disabled.enabled && !disabled.forceRenderingOff, "An already-disabled renderer keeps its original state.");
                Check(alreadyHidden.forceRenderingOff, "An already-hidden renderer stays hidden while occluded.");

                front.transform.position = new Vector3(5, .6f, 4);
                alreadyHidden.transform.position = new Vector3(-5, .6f, 6);
                occluder.Evaluate(camera, player.transform);
                Check(!front.forceRenderingOff, "A renderer restores visibility when it clears the player ray.");
                Check(alreadyHidden.forceRenderingOff, "Clearing the ray restores the pre-existing hidden state.");

                front.transform.position = new Vector3(0, .6f, 4);
                occluder.Evaluate(camera, player.transform);
                occluder.Evaluate(camera, null);
                Check(!front.forceRenderingOff, "A missing player target releases the temporary override.");
                occluder.Evaluate(camera, player.transform);
                occluder.enabled = false;
                // Evaluate also covers Edit Mode, where this non-ExecuteAlways component has no lifecycle ticks.
                occluder.Evaluate(camera, player.transform);
                Check(!front.forceRenderingOff, "Disabling the component restores visibility.");
                occluder.enabled = true;
                front.forceRenderingOff = true;
                occluder.Evaluate(camera, player.transform);
                front.transform.position = new Vector3(5, .6f, 4);
                occluder.Evaluate(camera, player.transform);
                Check(front.forceRenderingOff, "Each new override records the current renderer state rather than a stale initial value.");
            }
            catch (Exception exception)
            {
                report.status = "failed";
                report.errors.Add(exception.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            const string path = "Documentation/GameTheme/occluder-validation.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("GAME_OCCLUDER_VALIDATION " + report.status + " (" + report.checks.Count + " checks)");
            if (report.status != "passed") throw new InvalidOperationException("Occluder validation failed; inspect " + path);
        }
    }
}
#endif
