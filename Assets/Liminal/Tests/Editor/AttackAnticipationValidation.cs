#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>Isolated edit-mode coverage for cue placement, lifecycle and world-space geometry.</summary>
    public static class AttackAnticipationValidation
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Output = "Library/AttackAnticipationValidation/report.json";

        [Serializable] sealed class AnchorRecord
        {
            public string prefab, anchor;
            public string[] headCandidates;
            public Vector3 cuePosition;
        }
        [Serializable] sealed class Report
        {
            public string status = "passed";
            public string capture;
            public List<string> checks = new List<string>(), errors = new List<string>();
            public List<AnchorRecord> anchors = new List<AnchorRecord>();
        }

        public static string Run()
        {
            var report = new Report();
            if (Application.isPlaying)
            {
                report.status = "failed";
                report.errors.Add("Run in edit mode; this helper does not alter the active gameplay scene.");
                return JsonUtility.ToJson(report, true);
            }
            Scene scene = EditorSceneManager.NewPreviewScene();
            var materialField = typeof(AttackAnticipation).GetField("material", BindingFlags.Static | BindingFlags.NonPublic);
            var previousMaterial = materialField?.GetValue(null) as Material;
            var outlineMaterialField = typeof(Telegraph).GetField("outlineMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            var previousOutlineMaterial = outlineMaterialField?.GetValue(null) as Material;
            try
            {
                Check(report, "HeadFront takes priority and follows the animated face", () =>
                {
                    var owner = Owner(scene, "Face priority");
                    var visual = Child(owner, "Visual", Vector3.zero);
                    Child(visual, "Head", Vector3.up * 2);
                    var face = Child(visual, "HeadFront", new Vector3(0, 2.1f, .3f));
                    AttackAnticipation.Show(owner, .4f);
                    var cue = owner.GetComponent<AttackAnticipation>();
                    Require(cue.Anchor == face, "HeadFront was not selected.");
                    Near(cue.CuePosition, face.position, "Initial face position");
                    face.localPosition += new Vector3(.2f, .3f, .1f);
                    Near(cue.CuePosition, face.position, "Animated face position");
                });
                Check(report, "Namespaced Head bone resolves as a face anchor", () =>
                {
                    var owner = Owner(scene, "Namespaced head");
                    var head = Child(owner, "mixamorig:Head", Vector3.up * 2);
                    AttackAnticipation.Show(owner, .4f);
                    Require(owner.GetComponent<AttackAnticipation>().Anchor == head, "Namespaced Head was not selected.");
                });
                Check(report, "Explicit face anchor overrides discovery", () =>
                {
                    var owner = Owner(scene, "Explicit face");
                    Child(owner, "HeadFront", Vector3.up * 2);
                    var anchor = Child(owner, "Muzzle center", new Vector3(.1f, 1.4f, .4f));
                    AttackAnticipation.Show(owner, .5f, anchor);
                    Require(owner.GetComponent<AttackAnticipation>().Anchor == anchor, "Explicit anchor was ignored.");
                });
                Check(report, "Faceless body uses current visible mesh bounds and clamps buried cues", () =>
                {
                    var owner = Owner(scene, "Faceless body");
                    var visual = Child(owner, "Visual", Vector3.zero);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.name = "Body"; body.transform.SetParent(visual, false);
                    body.transform.localPosition = new Vector3(.3f, 1.4f, .2f);
                    body.transform.localScale = new Vector3(2, 2.4f, 1);
                    AttackAnticipation.Show(owner, .4f);
                    var cue = owner.GetComponent<AttackAnticipation>();
                    Require(!cue.Anchor, "Faceless actor must use bounds rather than inventing a head.");
                    Near(cue.CuePosition, body.GetComponent<Renderer>().bounds.center, "Body center");
                    visual.localPosition = Vector3.down * 5;
                    Require(Mathf.Abs(cue.CuePosition.y - (owner.position.y + .25f)) < .001f, "Buried cue must stay above the floor.");
                });
                Check(report, "Windup flashes late once, hides immediately, and repeats on the next attack", () =>
                {
                    var owner = Owner(scene, "Cue lifecycle");
                    AttackAnticipation.Show(owner, .2f);
                    var cue = owner.GetComponent<AttackAnticipation>();
                    Require(!cue.Visible, "Early windup should not display the glint.");
                    AttackAnticipation.Show(owner, .79f);
                    Require(cue.Visible, "Late windup did not display the glint.");
                    Set(cue, "startedAt", Time.time - .3f);
                    AttackAnticipation.Show(owner, .9f);
                    Require(!cue.Visible, "The same windup replayed an expired glint.");
                    AttackAnticipation.Show(owner, .1f);
                    AttackAnticipation.Show(owner, .79f);
                    Require(cue.Visible, "A fresh windup did not reset the one-shot glint.");
                    AttackAnticipation.Hide(owner);
                    Require(!cue.Visible, "Hide left the glint visible.");
                    AttackAnticipation.Show(owner, .79f);
                    Require(cue.Visible, "A new attack after Hide did not flash.");
                    owner.gameObject.SetActive(false);
                    Require(!cue.Visible, "Deactivation left the glint visible.");
                });
                Check(report, "A defeated owner clears its active cue", () =>
                {
                    var owner = Owner(scene, "Defeated owner");
                    var health = owner.gameObject.AddComponent<TrainingEnemy>();
                    health.Configure(10, false); health.deferDeathVisuals = true;
                    AttackAnticipation.Show(owner, .79f);
                    var cue = owner.GetComponent<AttackAnticipation>();
                    Require(cue.Visible, "Live owner did not flash.");
                    health.TakeDamage(10);
                    Invoke(cue, "LateUpdate");
                    Require(!cue.Visible, "Defeated owner still displays its cue.");
                });
                Check(report, "A rotated, nonuniformly scaled owner preserves glint size and perpendicular rays", () =>
                {
                    var baseline = Owner(scene, "Unit-scale baseline");
                    AttackAnticipation.Show(baseline, .79f);
                    var a = baseline.Find("Attack anticipation glint");
                    var scaled = Owner(scene, "Scaled actor");
                    scaled.rotation = Quaternion.Euler(25, 43, 11);
                    scaled.localScale = new Vector3(2, 3, .5f);
                    AttackAnticipation.Show(scaled, .79f);
                    var b = scaled.Find("Attack anticipation glint");
                    Vector3[] av = a.GetComponent<MeshFilter>().sharedMesh.vertices, bv = b.GetComponent<MeshFilter>().sharedMesh.vertices;
                    Vector3 ax = a.TransformPoint(av[2]) - a.position, ay = a.TransformPoint(av[8]) - a.position;
                    Vector3 bx = b.TransformPoint(bv[2]) - b.position, by = b.TransformPoint(bv[8]) - b.position;
                    Require(Mathf.Abs(ax.magnitude - bx.magnitude) < .002f && Mathf.Abs(ay.magnitude - by.magnitude) < .002f,
                        "Nonuniform parent scaling distorted the glint size: " + bx.magnitude + ", " + by.magnitude + ".");
                    Require(Mathf.Abs(Vector3.Dot(bx.normalized, by.normalized)) < .002f, "Nonuniform parent scaling sheared the glint.");
                });
                Check(report, "White line warnings contain only a central guide at the configured world-space width", () =>
                {
                    var owner = Owner(scene, "Scaled center guide");
                    owner.localScale = new Vector3(2, 3, .5f); owner.rotation = Quaternion.Euler(0, 43, 0);
                    var warning = Telegraph.Create(owner);
                    var origin = new Vector3(2, .5f, 3);
                    warning.Line(origin, Vector3.forward, 6, .8f, .7f);
                    var mesh = warning.GetComponent<MeshFilter>().sharedMesh;
                    Require(warning.Visible && mesh.vertexCount == 4 && mesh.triangles.Length == 6, "Line warning must contain one central guide without perimeter or area-fill geometry.");
                    var v = mesh.vertices;
                    for (int i = 0; i < v.Length; i += 4)
                    {
                        float width = Vector3.Distance(warning.transform.TransformPoint(v[i]), warning.transform.TransformPoint(v[i + 1]));
                        Require(Mathf.Abs(width - Telegraph.OutlineWidth) < .001f, "Scaled center-guide width changed.");
                    }
                    Near(warning.transform.TransformPoint((v[0] + v[1]) * .5f), origin + Vector3.up * .038f, "Center-guide origin");
                    Near(warning.transform.TransformPoint((v[2] + v[3]) * .5f), origin + Vector3.forward * 6 + Vector3.up * .038f, "Center-guide endpoint");
                    foreach (var color in mesh.colors)
                        Require(color.r == 1 && color.g == 1 && color.b == 1, "Attack center guide contains a colored vertex.");
                    warning.Release(); Require(!warning.Visible, "Release did not hide the center guide.");
                });
                InspectPrefabs(scene, report);
                Check(report, "Render face and body glints for visual inspection", () => report.capture = Capture(scene));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                var currentMaterial = materialField?.GetValue(null) as Material;
                if (currentMaterial && currentMaterial != previousMaterial) Object.DestroyImmediate(currentMaterial);
                materialField?.SetValue(null, previousMaterial);
                var currentOutlineMaterial = outlineMaterialField?.GetValue(null) as Material;
                if (currentOutlineMaterial && currentOutlineMaterial != previousOutlineMaterial) Object.DestroyImmediate(currentOutlineMaterial);
                outlineMaterialField?.SetValue(null, previousOutlineMaterial);
            }
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, json);
            return json;
        }

        static void InspectPrefabs(Scene scene, Report report)
        {
            string[] paths = {
                "ForestMonsters/Moonworm", "ForestMonsters/MossFrog", "ForestMonsters/Elderwood", "ForestMonsters/LunarButterfly", "ForestMonsters/VolatileSapling",
                "RuinsMonsters/ScrapBulwark", "RuinsMonsters/PenitentHusk", "RuinsMonsters/CarrionDrone", "RuinsMonsters/OssuaryMedusa", "RuinsMonsters/MourningMatron",
                "RuinsBoss/StormSovereign", "RuinsBoss/SiegeWalker", "RuinsBoss/MissileTurret", "RuinsBoss/IronRam", "GameTheme/TetrominoBoss"
            };
            foreach (string path in paths) Check(report, path + " resolves a finite cue position", () =>
            {
                var prefab = Resources.Load<GameObject>(path);
                Require(prefab, "Missing prefab: " + path);
                var actor = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(actor, scene);
                if (path.StartsWith("ForestMonsters/")) AcRoguelike.Forest.ForestAttackCueAnchors.Prepare(actor.transform);
                var candidates = new List<string>();
                foreach (var node in actor.GetComponentsInChildren<Transform>(true))
                {
                    string name = node.name.ToLowerInvariant();
                    if (name.Contains("head") || name.Contains("face") || name.Contains("eye")) candidates.Add(node.name);
                }
                AttackAnticipation.Show(actor.transform, .5f);
                var cue = actor.GetComponent<AttackAnticipation>(); Vector3 position = cue.CuePosition;
                Require(float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z), "Cue position is not finite.");
                report.anchors.Add(new AnchorRecord { prefab = path, anchor = cue.Anchor ? cue.Anchor.name : "body center", headCandidates = candidates.ToArray(), cuePosition = position });
            });
        }
        static string Capture(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", new Color(.2f, .28f, .32f, 1));
            var cameraRoot = Owner(scene, "Cue preview camera");
            var camera = cameraRoot.gameObject.AddComponent<Camera>();
            camera.scene = scene; camera.cullingMask = ~0; camera.allowHDR = false; camera.allowMSAA = false;
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.05f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .05f, 1);
            camera.nearClipPlane = .05f; camera.farClipPlane = 30; camera.aspect = 1.6f;
            Quaternion facing = Camera.main ? Camera.main.transform.rotation : Quaternion.identity;
            var center = new Vector3(0, 1.2f, 0);
            camera.transform.SetPositionAndRotation(center - facing * Vector3.forward * 8, facing);
            for (int i = 0; i < 2; i++)
            {
                var owner = Owner(scene, i == 0 ? "Left: face cue" : "Right: faceless body cue");
                owner.position = facing * Vector3.right * (i == 0 ? -1.3f : 1.3f);
                var visual = Child(owner, "Visual", Vector3.zero);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body"; body.transform.SetParent(visual, false);
                body.transform.localPosition = Vector3.up * 1.05f;
                body.transform.localScale = new Vector3(1.25f, 1.8f, .85f);
                body.GetComponent<Renderer>().sharedMaterial = material;
                if (i == 0)
                {
                    var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    head.name = "Head"; head.transform.SetParent(visual, false);
                    head.transform.localPosition = Vector3.up * 2.17f; head.transform.localScale = Vector3.one * .75f;
                    head.GetComponent<Renderer>().sharedMaterial = material;
                    Child(visual, "HeadFront", Vector3.up * 2.17f - facing * Vector3.forward * .4f);
                }
                AttackAnticipation.Show(owner, .79f);
            }
            var target = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                target.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP capture request is unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
                string path = "Library/AttackAnticipationValidation/face-and-body.png";
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, pixels.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels) Object.DestroyImmediate(pixels);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(material);
            }
        }
        static Transform Owner(Scene scene, string name)
        {
            var owner = new GameObject(name); SceneManager.MoveGameObjectToScene(owner, scene); return owner.transform;
        }
        static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform; child.SetParent(parent, false); child.localPosition = position; return child;
        }
        static void Check(Report report, string label, Action test)
        {
            try { test(); report.checks.Add(label); }
            catch (Exception error) { report.errors.Add(label + ": " + error.GetBaseException().Message); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Near(Vector3 actual, Vector3 expected, string label) => Require(Vector3.Distance(actual, expected) < .001f, label + " differs: " + actual + " / " + expected);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null);
    }
}
#endif
