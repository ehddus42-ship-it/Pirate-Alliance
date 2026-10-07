#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>GPU regression check in an isolated preview scene; never opens or saves an authored scene.</summary>
    public static class TelegraphVisibilityValidation
    {
        const int Size = 384;
        const string Output = "Library/TelegraphValidation";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] sealed class Case
        {
            public string surface, shape;
            public float progress;
            public int previousPixels, visiblePixels, progressChangedPixels;
        }

        [Serializable] sealed class Report
        {
            public string status = "passed", utc = DateTime.UtcNow.ToString("O");
            public List<Case> cases = new List<Case>();
            public List<string> checks = new List<string>(), errors = new List<string>();
        }

        public static void RunBatch()
        {
            string json = Run();
            Debug.Log("TELEGRAPH_VISIBILITY_VALIDATION " + json);
            if (JsonUtility.FromJson<Report>(json).status != "passed")
                throw new InvalidOperationException("Telegraph visibility failed. See " + Output + "/report.json");
        }

        public static string Run()
        {
            var report = new Report();
            var owned = new List<Object>();
            var previousActive = RenderTexture.active;
            var scene = default(Scene);
            GameObject root = null;
            Camera camera = null;
            RenderTexture target = null;
            Texture2D image = null;
            var cache = typeof(Telegraph).GetField("outlineMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            var previousCache = cache?.GetValue(null) as Material;
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    report.errors.Add(message);
            };
            Application.logMessageReceived += log;
            try
            {
                Directory.CreateDirectory(Output);
                Require(cache != null, "The shared outline material cache must be available for isolated cleanup.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required (omit -nographics).");
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Telegraph visibility preview");
                SceneManager.MoveGameObjectToScene(root, scene);
                var cameraObject = new GameObject("Validation camera");
                cameraObject.transform.SetParent(root.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.cullingMask = ~0;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.03f, .04f, .05f, 1);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.fieldOfView = 42;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 50;
                camera.transform.position = new Vector3(0, 10, -8);
                camera.transform.LookAt(Vector3.zero);
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Telegraph validation target" };
                target.Create();
                camera.targetTexture = target;
                image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Surface under test";
                ground.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.localScale = new Vector3(20, .1f, 20);
                var surfaceRenderer = ground.GetComponent<Renderer>();
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var flatMaterial = new Material(RequireShader("Universal Render Pipeline/Unlit"));
                flatMaterial.SetColor("_BaseColor", new Color(.32f, .24f, .16f, 1));
                owned.Add(flatMaterial);
                var waterMaterial = new Material(RequireShader("Liminal/Quiet Water"));
                owned.Add(waterMaterial);
                var oldRim = LegacyMaterial(3002);
                owned.Add(oldRim);
                var telegraph = Telegraph.Create(root.transform, "Warning under test");
                var renderers = telegraph.GetComponentsInChildren<MeshRenderer>();
                Require(renderers.Length == 1, "A warning must have only one center-guide renderer and no fill renderer.");
                var fixedMaterials = new[] { renderers[0].sharedMaterial };
                foreach (var filter in telegraph.GetComponentsInChildren<MeshFilter>()) owned.Add(filter.sharedMesh);

                var names = new[] { "floor", "water", "planks_013", "planks_020", "bridge_097" };
                var heights = new[] { 0f, .045f, .13f, .2f, .97f };
                var shapes = new[] { "circle", "fan", "line" };
                var progresses = new[] { .15f, .55f, .98f };
                var referenceVertices = new Dictionary<string, Vector3[]>();
                for (int surface = 0; surface < names.Length; surface++)
                {
                    ground.transform.position = new Vector3(0, heights[surface] - .05f, 0);
                    surfaceRenderer.sharedMaterial = surface == 1 ? waterMaterial : flatMaterial;
                    foreach (string shape in shapes)
                    {
                        Color32[] previousProgress = null;
                        foreach (float progress in progresses)
                        {
                            Draw(telegraph, shape, progress);
                            string key = shape + "_" + progress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                            ValidateCenterGuide(telegraph, key, shape);
                            var vertices = WorldVertices(telegraph);
                            if (surface == 0) referenceVertices[key] = vertices;
                            else Require(SameVertices(vertices, referenceVertices[key]), "Surface height moved the " + key + " attack footprint.");
                            foreach (var renderer in renderers) renderer.enabled = false;
                            var hidden = Capture(camera, image, target);
                            for (int i = 0; i < renderers.Length; i++)
                            {
                                renderers[i].enabled = true;
                                renderers[i].sharedMaterial = oldRim;
                            }
                            var before = Capture(camera, image, target);
                            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = fixedMaterials[i];
                            var after = Capture(camera, image, target);
                            var result = new Case { surface = names[surface], shape = shape, progress = progress,
                                previousPixels = Changed(before, hidden), visiblePixels = Changed(after, hidden),
                                progressChangedPixels = previousProgress == null ? 0 : Changed(after, previousProgress) };
                            report.cases.Add(result);
                            if (progress == .55f || (surface == 4 && shape == "circle"))
                            {
                                Save(image, after, names[surface] + "_" + key + "_after.png");
                                Save(image, before, names[surface] + "_" + key + "_before.png");
                            }
                            int minimumPixels = shape == "circle" ? 12 : 60;
                            Require(result.visiblePixels > minimumPixels, names[surface] + " " + key + " center guide is not visibly rendered.");
                            if (surface == 0)
                                Require(result.previousPixels > minimumPixels, "Depth-tested center guide did not render on ordinary floor; comparison is invalid.");
                            else
                                Require(result.previousPixels < result.visiblePixels * .05f,
                                    names[surface] + " did not reproduce the legacy depth-occlusion bug.");
                            if (previousProgress != null)
                                Require(result.progressChangedPixels > minimumPixels, names[surface] + " " + shape + " no longer shows charge progress.");
                            Require(!InteriorUnchanged(camera, after, hidden, new Vector3(0, .038f, 0)),
                                names[surface] + " " + key + " does not mark the center of its attack area.");
                            var interior = new Vector3(shape == "circle" ? .6f : .45f, .038f, 0);
                            var edge = shape == "circle" ? new Vector3(2.8f, .038f, 0) :
                                shape == "fan" ? new Vector3(Mathf.Sin(55 * Mathf.Deg2Rad) * 2, .038f, -1.8f + Mathf.Cos(55 * Mathf.Deg2Rad) * 2) :
                                new Vector3(1.4f, .038f, 0);
                            Require(InteriorUnchanged(camera, after, hidden, interior) && InteriorUnchanged(camera, after, hidden, edge),
                                names[surface] + " " + key + " draws away from its center guide or still shows a perimeter.");
                            previousProgress = after;
                            telegraph.Hide();
                            Require(!telegraph.Visible && Changed(Capture(camera, image, target), hidden) < 8, "Hide left visible warning pixels.");
                        }
                    }
                }
                report.checks.Add("45 rendered circle/fan/line cases cover progress 0.15/0.55/0.98 on ordinary floor, actual Quiet Water, planks at 0.13/0.20 m and bridge at 0.97 m.");
                report.checks.Add("Each raised surface reproduces depth-tested guide disappearance, while the overlay remains visible. Charge progress, Hide and world-space positions are preserved.");
                report.checks.Add("Every warning has one renderer and white 0.035 m wide center guides. Fan/line warnings use one central line, circles use a compact central cross; perimeter and off-center interior pixels stay unchanged.");

                Draw(telegraph, "circle", .55f);
                foreach (var renderer in renderers) renderer.enabled = false;
                var alphaHidden = Capture(camera, image, target);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(1, 1, 1, 0));
                foreach (var renderer in renderers) { renderer.enabled = true; renderer.SetPropertyBlock(block); }
                Require(Changed(Capture(camera, image, target), alphaHidden) < 8, "Zero alpha is ignored by the outline shader.");
                Draw(telegraph, "circle", .98f);
                Require(telegraph.Visible, "The warning must be visible immediately before release.");
                telegraph.Release();
                Require(!telegraph.Visible && !renderers[0].enabled && Changed(Capture(camera, image, target), alphaHidden) < 8,
                    "Release must immediately hide the outline without an additional ground flash.");
                report.checks.Add("Zero BaseColor alpha fully hides the outline; Release immediately removes the warning without a ground flash.");
                ValidateRedField(root.transform, camera, image, target, owned, report);
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (camera) camera.targetTexture = null;
                // Dispose generated meshes/materials first so runtime OnDestroy never calls delayed Destroy in Edit Mode.
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
                if (root) Object.DestroyImmediate(root);
                if (cache != null && !previousCache)
                {
                    var created = cache.GetValue(null) as Material;
                    cache.SetValue(null, previousCache);
                    if (created) Object.DestroyImmediate(created);
                }
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (image) Object.DestroyImmediate(image);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                Application.logMessageReceived -= log;
            }
            if (report.errors.Count > 0) report.status = "failed";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/report.json", json);
            return json;
        }

        static void ValidateRedField(Transform parent, Camera camera, Texture2D image, RenderTexture target, List<Object> owned, Report report)
        {
            const string path = "Assets/Liminal/Art/TrafficLightBoss/Materials/";
            var sources = new[] { AssetDatabase.LoadAssetAtPath<Material>(path + "FieldRed.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(path + "FieldSafeTelegraph.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(path + "FieldSafeActive.mat") };
            foreach (var source in sources) Require(source, "Missing authored boss warning material.");
            var snapshots = new[] { EditorJsonUtility.ToJson(sources[0]), EditorJsonUtility.ToJson(sources[1]), EditorJsonUtility.ToJson(sources[2]) };
            var go = new GameObject("Boss field under test");
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<TrafficLightRedField>();
            field.floorMaterial = sources[0]; field.telegraphMaterial = sources[1]; field.safeMaterial = sources[2];
            try
            {
                field.Show(Vector3.zero, Quaternion.identity, new Vector2(7, 7), new[] { Vector3.zero }, 2f, .1f);
                typeof(TrafficLightRedField).GetField("phaseTime", PrivateInstance).SetValue(field, .6f);
                typeof(TrafficLightRedField).GetMethod("Apply", PrivateInstance).Invoke(field, null);
                var safe = go.transform.Find("SafeZone0").GetComponent<Renderer>();
                var floor = go.transform.Find("RedFloor").GetComponent<Renderer>();
                var safeOutline = go.transform.Find("SafeZoneOutline0").GetComponent<Telegraph>();
                Require(!floor.enabled && !safe.enabled && safeOutline.Visible,
                    "Field windup must show only a safe-zone center mark, with no red floor or filled disc.");
                ValidateCenterGuide(safeOutline, "boss safe zone", "circle");
                var withWhite = Capture(camera, image, target);
                safeOutline.Hide();
                var noField = Capture(camera, image, target);
                Require(Changed(withWhite, noField) > 12, "White safe-zone center mark is hidden by the raised bridge.");
                Require(!InteriorUnchanged(camera, withWhite, noField, new Vector3(0, .038f, 0)) &&
                    InteriorUnchanged(camera, withWhite, noField, new Vector3(.6f, .038f, 0)) &&
                    InteriorUnchanged(camera, withWhite, noField, new Vector3(2f, .038f, 0)),
                    "The safe-zone warning must mark only its center, leaving its perimeter and off-center interior clear.");
                typeof(TrafficLightRedField).GetMethod("Apply", PrivateInstance).Invoke(field, null);
                Save(image, withWhite, "boss_white_safe_zone.png");
                field.Judge();
                Require(floor.enabled && safe.enabled && !safeOutline.Visible,
                    "Judgement must replace the warning outline with the active danger field and safe zone.");
                var withGreen = Capture(camera, image, target);
                safe.enabled = false;
                var judgedRedOnly = Capture(camera, image, target);
                safe.enabled = true;
                Require(Changed(withGreen, judgedRedOnly) > 100 && Changed(withWhite, withGreen) > 100,
                    "Green safe-zone judgement is hidden or indistinguishable from its white warning.");
                Require(Changed(judgedRedOnly, noField) > 1000, "The active red danger field is not rendered.");
                var texture = sources[2].GetTexture("_BaseMap");
                Require(texture && safe.sharedMaterial.GetTexture("_BaseMap") == texture, "Authored active safe-zone ring mask was lost.");
                safe.sharedMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                var withoutMask = Capture(camera, image, target);
                Require(Changed(withGreen, withoutMask) > 100, "Active safe-zone ring texture does not affect rendered pixels.");
                safe.sharedMaterial.SetTexture("_BaseMap", texture);
                Save(image, withGreen, "boss_green_safe_zone.png");
                for (int i = 0; i < sources.Length; i++)
                    Require(EditorJsonUtility.ToJson(sources[i]) == snapshots[i], "Boss field modified an authored material.");
                report.checks.Add("Boss windup shows only a thin white safe-zone center cross above the bridge. Red danger fill and textured green safe ground appear at judgement; source materials are unchanged.");
            }
            finally
            {
                foreach (var warning in go.GetComponentsInChildren<Telegraph>())
                    foreach (var filter in warning.GetComponentsInChildren<MeshFilter>())
                        if (filter.sharedMesh) owned.Add(filter.sharedMesh);
                foreach (string name in new[] { "floorOverlay", "telegraphOverlay", "safeOverlay", "quad", "disc" })
                {
                    var item = typeof(TrafficLightRedField).GetField(name, PrivateInstance)?.GetValue(field) as Object;
                    if (item) owned.Add(item);
                }
            }
        }

        static void Draw(Telegraph telegraph, string shape, float progress)
        {
            if (shape == "circle") telegraph.Circle(Vector3.zero, 2.8f, progress);
            else if (shape == "fan") telegraph.Fan(new Vector3(0, 0, -1.8f), Vector3.forward, 4f, 55, progress);
            else telegraph.Line(new Vector3(0, 0, -2.2f), Vector3.forward, 4.4f, 1.4f, progress);
        }

        static void ValidateCenterGuide(Telegraph telegraph, string name, string shape)
        {
            Require(Mathf.Approximately(Telegraph.OutlineWidth, .035f), "Attack center guides must stay 0.035 m thin.");
            var renderers = telegraph.GetComponentsInChildren<MeshRenderer>();
            var filters = telegraph.GetComponentsInChildren<MeshFilter>();
            Require(renderers.Length == 1 && filters.Length == 1, name + " must have one center-guide mesh and no fill mesh.");
            Color materialColor = renderers[0].sharedMaterial.GetColor("_BaseColor");
            Require(White(materialColor), name + " material must be white.");
            var mesh = filters[0].sharedMesh;
            var vertices = WorldVertices(telegraph);
            var colors = mesh.colors;
            Require(vertices.Length == (shape == "circle" ? 8 : 4) && mesh.triangles.Length == vertices.Length / 4 * 6,
                name + " must contain only one central line or two central cross strokes.");
            Require(colors.Length == vertices.Length, name + " must color every center-guide vertex.");
            foreach (Color color in colors)
                Require(White(color) && color.a >= .58f - .001f && color.a <= .95f + .001f,
                    name + " uses a colored or overbright warning vertex.");
            for (int i = 0; i < vertices.Length; i += 4)
            {
                Require(Mathf.Abs(Vector3.Distance(vertices[i], vertices[i + 1]) - .035f) < .00001f &&
                    Mathf.Abs(Vector3.Distance(vertices[i + 2], vertices[i + 3]) - .035f) < .00001f,
                    name + " contains a thick guide or a filled strip.");
                if (shape == "circle")
                    Require(Vector3.Distance((vertices[i] + vertices[i + 1]) * .5f, (vertices[i + 2] + vertices[i + 3]) * .5f) <= .501f,
                        name + " center cross extends beyond its compact 0.5 m size.");
            }
        }

        static bool White(Color color) => Mathf.Abs(color.r - 1) < .001f && Mathf.Abs(color.g - 1) < .001f && Mathf.Abs(color.b - 1) < .001f;

        static bool InteriorUnchanged(Camera camera, Color32[] visible, Color32[] hidden, Vector3 world)
        {
            Vector3 pixel = camera.WorldToScreenPoint(world);
            int x = Mathf.RoundToInt(pixel.x), y = Mathf.RoundToInt(pixel.y);
            if (x < 2 || x >= Size - 2 || y < 2 || y >= Size - 2) return false;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int i = (y + dy) * Size + x + dx;
                    if (Math.Abs(visible[i].r - hidden[i].r) + Math.Abs(visible[i].g - hidden[i].g) + Math.Abs(visible[i].b - hidden[i].b) > 12)
                        return false;
                }
            return true;
        }

        static Material LegacyMaterial(int queue)
        {
            var material = new Material(RequireShader("Universal Render Pipeline/Particles/Unlit")) { renderQueue = queue };
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetColor("_BaseColor", Color.white);
            return material;
        }

        static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            Require(shader && shader.isSupported, "Required supported shader is unavailable: " + name);
            return shader;
        }

        static Color32[] Capture(Camera camera, Texture2D image, RenderTexture target)
        {
            camera.Render();
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply(false);
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }

        static void Save(Texture2D image, Color32[] pixels, string name)
        {
            image.SetPixels32(pixels); image.Apply(false);
            File.WriteAllBytes(Output + "/" + name, image.EncodeToPNG());
        }

        static int Changed(Color32[] a, Color32[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 12) count++;
            return count;
        }

        static Vector3[] WorldVertices(Telegraph telegraph)
        {
            var vertices = new List<Vector3>();
            foreach (var filter in telegraph.GetComponentsInChildren<MeshFilter>())
                foreach (var vertex in filter.sharedMesh.vertices) vertices.Add(filter.transform.TransformPoint(vertex));
            return vertices.ToArray();
        }

        static bool SameVertices(Vector3[] a, Vector3[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > 1e-8f) return false;
            return true;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
