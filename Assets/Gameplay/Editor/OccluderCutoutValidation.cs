#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.Editor
{
    /// <summary>
    /// Play Mode capture of <see cref="OccluderCutout"/>: a wall between the isometric camera and the player,
    /// rendered with and without the cutout. Run from batch mode without -quit:
    /// Unity -batchmode -projectPath . -executeMethod AcRoguelike.Editor.OccluderCutoutValidation.RunBatch
    /// </summary>
    [InitializeOnLoad]
    public static class OccluderCutoutValidation
    {
        const string Output = "Library/OccluderCutoutValidation";
        const string PendingKey = "AcRoguelike.OccluderCutoutValidation.Pending";
        const string ExitKey = "AcRoguelike.OccluderCutoutValidation.Exit";

        [Serializable] sealed class Report
        {
            public string status = "passed", utc = DateTime.UtcNow.ToString("O");
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }

        static OccluderCutoutValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                {
                    SessionState.SetBool(PendingKey, false);
                    Start();
                }
            };
        }

        public static void RunBatch()
        {
            SessionState.SetBool(ExitKey, true);
            Run();
        }

        [MenuItem("Tools/Validation/Occluder Cutout")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        static void Start()
        {
            var report = new Report();
            if (Camera.main) Object.Destroy(Camera.main.gameObject);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material Solid(Shader shader, Color color)
            {
                var material = new Material(shader);
                material.SetColor("_BaseColor", color);
                return material;
            }
            GameObject Box(string name, Vector3 position, Vector3 size, float yaw, Material material)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
                go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = material;
                return go;
            }

            Box("Floor", new Vector3(0, -.1f, 0), new Vector3(30, .2f, 30), 0, Solid(lit, new Color(.55f, .55f, .5f)));
            var wallMaterial = Solid(lit, new Color(.25f, .3f, .55f));
            var towardCamera = new Vector3(-1, 0, -1).normalized;
            var wall = Box("Wall", towardCamera * 1.3f + Vector3.up * 2, new Vector3(6, 4, .3f), 45, wallMaterial);

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = Vector3.up;
            player.GetComponent<Renderer>().sharedMaterial = Solid(unlit, new Color(1, .1f, .1f));
            Object.Destroy(player.GetComponent<Collider>());
            player.AddComponent<CharacterController>();

            // A transparent floor telegraph behind the wall must stay visible through the cutout.
            var telegraph = GameObject.CreatePrimitive(PrimitiveType.Quad);
            telegraph.name = "Telegraph";
            Object.Destroy(telegraph.GetComponent<Collider>());
            telegraph.transform.SetPositionAndRotation(new Vector3(.9f, .02f, .9f), Quaternion.Euler(90, 0, 0));
            telegraph.transform.localScale = Vector3.one * 1.2f;
            var glow = Solid(unlit, new Color(1, .8f, 0, .8f));
            glow.SetFloat("_Surface", 1);
            glow.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glow.SetFloat("_ZWrite", 0);
            glow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glow.renderQueue = 3000;
            telegraph.GetComponent<Renderer>().sharedMaterial = glow;

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var cameraObject = new GameObject("Gameplay Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40;
            var target = new RenderTexture(960, 540, 24) { name = "OccluderCutoutValidation" };
            camera.targetTexture = target;
            var follow = cameraObject.AddComponent<IsometricFollowCamera>();
            follow.target = player.transform;
            var cutout = cameraObject.GetComponent<OccluderCutout>();
            follow.Snap();

            int frames = 0;
            float started = Time.realtimeSinceStartup;
            Texture2D on = null;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                try
                {
                    camera.Render();
                    if (++frames < 30 || Time.realtimeSinceStartup - started < 1f) return;
                    EditorApplication.update -= tick;

                    void Check(bool condition, string description)
                    {
                        if (!condition) throw new InvalidOperationException(description);
                        report.checks.Add(description);
                    }
                    Check(cutout, "The isometric follow camera adds an OccluderCutout.");
                    Check(cutout.IsCutting, "The wall between the camera and the player is detected as an occluder.");
                    Check(wall.GetComponent<Renderer>().sharedMaterial != wallMaterial, "The occluding wall is drawn with a late-queue clone.");
                    Check(player.GetComponent<Renderer>().sharedMaterial.renderQueue <= 2500, "The player itself is never cut.");

                    on = Capture(camera, target, "on");
                    cutout.enabled = false;
                    Check(wall.GetComponent<Renderer>().sharedMaterial == wallMaterial, "Disabling the cutout restores the wall's material.");
                    var off = Capture(camera, target, "off");

                    Vector2 playerPixel = camera.WorldToScreenPoint(Vector3.up * 1.1f);
                    Color playerOn = Sample(on, playerPixel), playerOff = Sample(off, playerPixel);
                    report.checks.Add($"player pixel on={playerOn} off={playerOff}");
                    Check(playerOff.r < .5f, "Without the cutout the wall hides the player.");
                    Check(playerOn.r > playerOff.r + .3f && playerOn.r < .97f, "With the cutout the player shows through a still-tinted wall.");

                    // A telegraph corner beside the capsule, hidden by the wall without the cutout.
                    Vector2 telegraphPixel = camera.WorldToScreenPoint(new Vector3(.45f, .02f, 1.35f));
                    Color telegraphOn = Sample(on, telegraphPixel), telegraphOff = Sample(off, telegraphPixel);
                    report.checks.Add($"telegraph pixel on={telegraphOn} off={telegraphOff}");
                    Check(telegraphOn.g > telegraphOff.g + .2f, "Transparent telegraphs behind the wall show through the cutout.");

                    Vector2 farWall = camera.WorldToScreenPoint(wall.transform.position + wall.transform.right * 2.7f + Vector3.up * 1.5f);
                    Check(Difference(Sample(on, farWall), Sample(off, farWall)) < .02f, "Wall pixels outside the circle stay opaque.");
                    Vector2 floor = camera.WorldToScreenPoint(new Vector3(3, 0, 3));
                    Check(Difference(Sample(on, floor), Sample(off, floor)) < .02f, "Unoccluded floor pixels are unchanged.");
                }
                catch (Exception exception)
                {
                    report.status = "failed";
                    report.errors.Add(exception.ToString());
                }
                Finish(report);
            };
            EditorApplication.update += tick;
        }

        static Texture2D Capture(Camera camera, RenderTexture target, string name)
        {
            camera.Render();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(Output);
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
            return texture;
        }

        static Color Sample(Texture2D texture, Vector2 pixel) => texture.GetPixel((int)pixel.x, (int)pixel.y);
        static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

        static void Finish(Report report)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("OCCLUDER_CUTOUT_VALIDATION " + report.status + " (" + report.checks.Count + " checks)");
            bool exit = SessionState.GetBool(ExitKey, false);
            SessionState.SetBool(ExitKey, false);
            if (exit) EditorApplication.Exit(report.status == "passed" ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
