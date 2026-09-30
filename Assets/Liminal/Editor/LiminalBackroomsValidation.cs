using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>Opt-in dimensional checks and isolated, reproducible review images. Never saves or replaces an open scene.</summary>
    public static class LiminalBackroomsValidation
    {
        public const string OutputFolder = "Documentation/Liminal/Previews/Backrooms";
        public const string ValidationPath = "Documentation/Liminal/backrooms-validation.json";
        const int ImageWidth = 1280, ImageHeight = 960;
        const float Tolerance = .025f;
        static readonly int[] RepresentativeNumbers = { 1, 3, 4, 5, 7, 12 };
        static readonly int[] CloseupNumbers = { 1, 3, 5, 12 };

        [Serializable] public sealed class ValidationReport
        {
            public string status, utc, unityVersion;
            public int roomCount, passedRooms, galleryRoomCount, meshySlots, populatedMeshySlots, missingMeshySlots;
            public Vector2 regularDimensions = new Vector2(26, 36.4f);
            public Vector2 bossDimensions = new Vector2(42, 54);
            public List<RoomCheck> rooms = new List<RoomCheck>();
            public ArchCheck reusablePoolArch;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class RoomCheck
        {
            public string roomId, prefabPath;
            public Vector3 rootScale, boundsCenter, boundsSize, entry, exit;
            public int meshySlots, populatedMeshySlots, missingMeshySlots;
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class ArchCheck
        {
            public string prefabPath;
            public int solidColliderCount;
            public float clearOpeningWidth;
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class CaptureReport
        {
            public string status, utc, unityVersion, catalogPath;
            public int width = ImageWidth, height = ImageHeight;
            public bool synchronousShaderCompilation = true;
            public int warmupRendersPerImage = 1;
            public CameraSettings gameplayCamera;
            public List<CaptureRecord> images = new List<CaptureRecord>();
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class CameraSettings
        {
            public float fieldOfView = 36, pitch = 58, yaw = 35, distance = 20;
            public float nearClip = .1f, farClip = 220;
            public bool renderPostProcessing = true;
            public int volumeLayerMask = 1;
            public Color background = new Color(.08f, .12f, .115f);
            public string source = "LiminalMapBuilder.CreatePlayer defaults";
        }

        [Serializable] public sealed class CaptureRecord
        {
            public string roomId, displayName, view, path;
            public bool representative;
            public Vector3 cameraPosition, cameraEuler, focus;
            public float cameraDistance;
        }

        [MenuItem("AC Roguelike/Liminal/Backrooms/Validate Dimensions and Gallery")]
        public static void ValidateMenu() { Debug.Log(Validate()); }

        [MenuItem("AC Roguelike/Liminal/Backrooms/Capture Room Catalog")]
        public static void CaptureMenu() { Debug.Log(CaptureAll()); }

        public static string Validate()
        {
            RequireEditMode();
            var state = OpenSceneState();
            var report = new ValidationReport { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                foreach (string id in LiminalMapBuilder.RoomIds)
                {
                    string path = LiminalMapBuilder.RoomFolder + "/" + id + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var check = new RoomCheck { roomId = id, prefabPath = path };
                    report.rooms.Add(check);
                    var room = prefab ? prefab.GetComponent<LiminalRoom>() : null;
                    if (!room) { check.errors.Add("Missing authored room prefab or LiminalRoom component."); continue; }
                    report.roomCount++;
                    CheckRoom(room, check);
                    report.meshySlots += check.meshySlots;
                    report.populatedMeshySlots += check.populatedMeshySlots;
                    report.missingMeshySlots += check.missingMeshySlots;
                    if (check.errors.Count == 0) report.passedRooms++;
                }
                if (report.roomCount != 20) report.errors.Add("Expected exactly 20 shipped room prefabs; found " + report.roomCount + ".");
                ValidateReusablePoolArch(report);
                ValidateGallery(report);
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
            finally { CheckOpenScenesUnchanged(state, report.errors); }
            foreach (var room in report.rooms)
                foreach (string error in room.errors) report.errors.Add(room.roomId + ": " + error);
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            return WriteJson(ValidationPath, report);
        }

        static void CheckRoom(LiminalRoom room, RoomCheck check)
        {
            bool boss = room.kind == LiminalRoomKind.Boss;
            float width = boss ? 42 : 26, length = boss ? 54 : 36.4f;
            check.rootScale = room.transform.localScale;
            check.boundsCenter = room.localBounds.center;
            check.boundsSize = room.localBounds.size;
            Test(Near(check.rootScale, Vector3.one), "Room root must have unit scale.");
            Test(Mathf.Abs(check.boundsSize.x - width) <= Tolerance && Mathf.Abs(check.boundsSize.z - length) <= Tolerance,
                "Expected footprint " + width + " x " + length + "; got " + check.boundsSize.x + " x " + check.boundsSize.z + ".");
            Test(Mathf.Abs(check.boundsCenter.x) <= Tolerance && Mathf.Abs(room.localBounds.min.z) <= Tolerance,
                "Bounds must run from z = 0 to exit, centered on x = 0.");
            Test(room.entry && room.exit && room.playerSpawn, "Entry, exit and player spawn are required.");
            if (room.entry && room.exit)
            {
                check.entry = room.transform.InverseTransformPoint(room.entry.position);
                check.exit = room.transform.InverseTransformPoint(room.exit.position);
                Test(Near(check.entry, Vector3.zero), "Entry must be at the local origin.");
                Test(Near(check.exit, new Vector3(0, 0, length)), "Exit does not match the far edge of the footprint.");
                Test(Vector3.Angle(room.transform.forward, room.entry.forward) < .1f &&
                     Vector3.Angle(room.transform.forward, room.exit.forward) < .1f, "Socket forward axes must point along +Z.");
            }
            if (room.playerSpawn) Test(room.Contains(room.playerSpawn.position, .4f), "Player spawn is outside the room.");
            if (room.enemySpawns != null)
                foreach (var spawn in room.enemySpawns)
                    Test(spawn && room.Contains(spawn.position, .4f), "Missing enemy marker or enemy marker outside the room.");
            var ambience = room.GetComponent<LiminalAmbience>();
            Test(ambience, "LiminalAmbience is required.");
            if (ambience)
            {
                Test(Mathf.Abs(ambience.localCenter.x) <= Tolerance && Mathf.Abs(ambience.localCenter.z - length * .5f) <= Tolerance,
                    "Ambience center does not follow the room footprint.");
                Test(Mathf.Abs(ambience.halfExtents.x - width * .5f) <= Tolerance && Mathf.Abs(ambience.halfExtents.z - length * .5f) <= Tolerance,
                    "Ambience extents do not match the room footprint.");
            }
            var floor = room.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Continuous walkable floor");
            Test(floor, "Continuous walkable floor is missing.");
            if (floor)
            {
                var box = floor.GetComponent<BoxCollider>();
                Test(box && box.enabled && !box.isTrigger, "Continuous floor needs an enabled solid BoxCollider.");
                if (box)
                {
                    Bounds bounds = BoxInRoomSpace(box, room.transform);
                    Test(Mathf.Abs(bounds.size.x - width) <= Tolerance && Mathf.Abs(bounds.size.z - length) <= Tolerance,
                        "Physical floor size disagrees with localBounds.");
                    Test(Mathf.Abs(bounds.center.x) <= Tolerance && Mathf.Abs(bounds.min.z) <= Tolerance,
                        "Physical floor is offset from the sockets/bounds.");
                }
            }
            CheckGate(room.entranceGate, "Entrance", true);
            CheckGate(room.exitGate, "Exit", false);
            var slots = room.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MeshySlot__")).ToArray();
            check.meshySlots = slots.Length;
            Test(slots.Length > 0, "Room has no Meshy prop slots.");
            foreach (var slot in slots)
            {
                bool populated = HasMeshyModel(slot);
                if (populated) check.populatedMeshySlots++;
                else { check.missingMeshySlots++; check.errors.Add(slot.name + ": Meshy model is missing, disabled, or has no mesh renderer."); }
                if (slot.name == "MeshySlot__poolroom_arch")
                {
                    var arch = CheckArchOpening(slot);
                    foreach (string error in arch.errors) check.errors.Add(slot.name + ": " + error);
                }
            }

            void Test(bool condition, string message) { if (!condition) check.errors.Add(message); }
            void CheckGate(GameObject gate, string label, bool entrance)
            {
                Test(gate, label + " gate is missing.");
                if (!gate) return;
                var box = gate.GetComponent<BoxCollider>();
                Test(box && box.enabled && !box.isTrigger, label + " gate has no solid collider.");
                Vector3 p = room.transform.InverseTransformPoint(gate.transform.position);
                Test(Mathf.Abs(p.x) <= Tolerance && (entrance ? p.z >= 0 && p.z <= 1.6f : p.z <= length && p.z >= length - 1.6f),
                    label + " gate does not match its doorway.");
            }
        }

        static bool HasMeshyModel(Transform slot)
        {
            return slot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != slot && t.name.StartsWith("Meshy / ") && ActiveBelow(t, slot))
                .Any(model => model.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer.enabled && ActiveBelow(renderer.transform, slot) &&
                    (renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh != null :
                        renderer.GetComponent<MeshFilter>() && renderer.GetComponent<MeshFilter>().sharedMesh)));
        }

        static bool ActiveBelow(Transform current, Transform root)
        {
            while (current)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
                current = current.parent;
            }
            return false;
        }

        static void ValidateReusablePoolArch(ValidationReport report)
        {
            string path = LiminalMapBuilder.PropFolder + "/poolroom_arch.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var holder = asset ? asset.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MeshySlot__poolroom_arch") : null;
            report.reusablePoolArch = holder ? CheckArchOpening(holder) : new ArchCheck();
            report.reusablePoolArch.prefabPath = path;
            if (!holder) report.reusablePoolArch.errors.Add("Reusable poolroom arch prefab or its Meshy slot is missing.");
            else if (!HasMeshyModel(holder)) report.reusablePoolArch.errors.Add("Reusable arch has no populated Meshy model.");
            foreach (string error in report.reusablePoolArch.errors) report.errors.Add("Reusable poolroom_arch: " + error);
            if (report.reusablePoolArch.errors.Count == 0)
                report.checks.Add("Reusable poolroom_arch: two solid piers, clear central player passage, populated Meshy model.");
        }

        static ArchCheck CheckArchOpening(Transform holder)
        {
            var result = new ArchCheck();
            var colliders = holder.GetComponentsInChildren<Collider>(true)
                .Where(c => c.enabled && !c.isTrigger && ActiveBelow(c.transform, holder)).ToArray();
            result.solidColliderCount = colliders.Length;
            if (colliders.Length != 2) result.errors.Add("Expected exactly two solid pier colliders; found " + colliders.Length + ".");
            // A 0.88 m central corridor gives the 0.48 m player capsule 0.20 m clearance on either side.
            // Its vertical interval matches the walkable capsule above the existing 0.22 m step allowance.
            var passage = new Bounds(new Vector3(0, .91f, 0), new Vector3(.88f, 1.38f, 20));
            var boxes = new List<Bounds>();
            foreach (var collider in colliders)
            {
                if (!(collider is BoxCollider box)) { result.errors.Add("Unexpected non-box arch collision: " + collider.name + "."); continue; }
                Bounds bounds = BoxInRoomSpace(box, holder);
                boxes.Add(bounds);
                if (bounds.Intersects(passage)) result.errors.Add("Collider blocks the central arch opening: " + collider.name + ".");
            }
            var left = boxes.Where(b => b.max.x < 0).ToArray();
            var right = boxes.Where(b => b.min.x > 0).ToArray();
            if (left.Length != 1 || right.Length != 1) result.errors.Add("Arch must have one pier on each side of the opening.");
            else
            {
                result.clearOpeningWidth = right[0].min.x - left[0].max.x;
                if (result.clearOpeningWidth < .88f) result.errors.Add("Arch opening is narrower than the required player clearance.");
            }
            return result;
        }

        static void ValidateGallery(ValidationReport report)
        {
            if (!File.Exists(LiminalMapBuilder.GalleryPath)) { report.errors.Add("Saved room gallery is missing."); return; }
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(LiminalMapBuilder.GalleryPath);
                var rooms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LiminalRoom>(true)).ToArray();
                report.galleryRoomCount = rooms.Length;
                if (rooms.Length != 20) report.errors.Add("Saved gallery must contain 20 rooms; found " + rooms.Length + ".");
                foreach (string id in LiminalMapBuilder.RoomIds)
                    if (rooms.Count(r => r.roomId == id) != 1) report.errors.Add("Gallery must contain exactly one " + id + ".");
                for (int i = 0; i < rooms.Length; i++)
                    for (int j = 0; j < i; j++)
                    {
                        Bounds a = WorldBounds(rooms[i]), b = WorldBounds(rooms[j]);
                        float x = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                        float z = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                        if (x > .05f && z > .05f)
                            report.errors.Add("Gallery overlap: " + rooms[i].roomId + " / " + rooms[j].roomId + " (" + x + " x " + z + " m).");
                    }
                report.checks.Add("Saved gallery checked in an isolated preview scene: room identities and pairwise world bounds.");
            }
            finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Twenty full-room PNGs, a numbered 4 x 5 JPG catalog, and four entry views using the saved gameplay camera.</summary>
        public static string CaptureAll() => CaptureInternal(0);

        /// <summary>Re-captures one shipped room (1..20) without generating the complete contact sheet.</summary>
        public static string CaptureRoom(int roomNumber)
        {
            if (roomNumber < 1 || roomNumber > LiminalMapBuilder.RoomIds.Length) throw new ArgumentOutOfRangeException(nameof(roomNumber));
            return CaptureInternal(roomNumber);
        }

        static string CaptureInternal(int onlyRoom)
        {
            RequireEditMode();
            var state = OpenSceneState();
            var report = new CaptureReport { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            Scene scene = default;
            Texture2D catalog = null;
            RenderTexture target = null;
            bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Directory.CreateDirectory(OutputFolder + "/Full");
                Directory.CreateDirectory(OutputFolder + "/Closeup");
                // The saved run supplies its own scene render settings and directional light; user scene state is never changed.
                scene = File.Exists(LiminalMapBuilder.RunPath) ? EditorSceneManager.OpenPreviewScene(LiminalMapBuilder.RunPath) : EditorSceneManager.NewPreviewScene();
                report.gameplayCamera = ReadGameplayCamera(scene);
                bool keptLight = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var light = root.GetComponent<Light>();
                    if (light && light.type == LightType.Directional) { keptLight = true; continue; }
                    if (root.GetComponent<Volume>()) continue;
                    Object.DestroyImmediate(root);
                }
                if (!keptLight)
                {
                    var fill = CreateInScene("Capture architectural fill", scene, typeof(Light)).GetComponent<Light>();
                    fill.type = LightType.Directional;
                    fill.color = new Color(.92f, .95f, .86f);
                    fill.intensity = .32f;
                    fill.shadows = LightShadows.Soft;
                    fill.transform.rotation = Quaternion.Euler(58, -35, 0);
                }
                var camera = CreateInScene("Backrooms review camera", scene, typeof(Camera)).GetComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.cameraType = CameraType.Game;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = report.gameplayCamera.background;
                camera.fieldOfView = report.gameplayCamera.fieldOfView;
                camera.nearClipPlane = report.gameplayCamera.nearClip;
                camera.farClipPlane = Mathf.Max(300, report.gameplayCamera.farClip);
                camera.aspect = (float)ImageWidth / ImageHeight;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderPostProcessing = report.gameplayCamera.renderPostProcessing;
                additional.volumeLayerMask = report.gameplayCamera.volumeLayerMask;
                additional.allowXRRendering = false;
                // The pipeline renders HDR internally, then tone maps into an sRGB output.
                // Reading linear HDR pixels straight into an RGB24 PNG would darken the image.
                target = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Backrooms capture", antiAliasing = 1 };
                target.Create();
                camera.targetTexture = target;
                if (onlyRoom == 0)
                {
                    catalog = new Texture2D(2560, 2600, TextureFormat.RGB24, false);
                    var pixels = new Color32[catalog.width * catalog.height];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(17, 22, 21, 255);
                    catalog.SetPixels32(pixels);
                }
                for (int index = 0; index < LiminalMapBuilder.RoomIds.Length; index++)
                {
                    if (onlyRoom != 0 && index + 1 != onlyRoom) continue;
                    string id = LiminalMapBuilder.RoomIds[index];
                    GameObject instance = null, explorer = null;
                    try
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(LiminalMapBuilder.RoomFolder + "/" + id + ".prefab");
                        if (!asset) throw new FileNotFoundException("Missing room prefab: " + id);
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var room = instance.GetComponent<LiminalRoom>();
                        room.SetGates(false, false);
                        RefreshText(instance);
                        Bounds bounds = VisualBounds(room);
                        Vector3 focus = bounds.center;
                        float distance = FrameBounds(camera, bounds, report.gameplayCamera);
                        var full = Render(camera, target);
                        string path = OutputFolder + "/Full/" + id + ".png";
                        try
                        {
                            File.WriteAllBytes(path, full.EncodeToPNG());
                            if (catalog) AddCatalogTile(catalog, full, index);
                        }
                        finally { Object.DestroyImmediate(full); }
                        Record("full-room", path, focus, distance);
                        if (CloseupNumbers.Contains(index + 1))
                        {
                            var explorerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Explorer/LiminalExplorer.prefab");
                            Vector3 playerPosition = room.playerSpawn ? room.playerSpawn.position + Vector3.up * .05f : new Vector3(0, .1f, 3.5f);
                            if (explorerAsset)
                            {
                                explorer = (GameObject)PrefabUtility.InstantiatePrefab(explorerAsset, scene);
                                explorer.transform.SetPositionAndRotation(playerPosition, Quaternion.identity);
                                foreach (var audio in explorer.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                            }
                            focus = playerPosition + Vector3.up * .55f;
                            PositionCamera(camera, focus, report.gameplayCamera.distance, report.gameplayCamera);
                            var closeup = Render(camera, target);
                            path = OutputFolder + "/Closeup/" + id + "_entry.png";
                            try { File.WriteAllBytes(path, closeup.EncodeToPNG()); }
                            finally { Object.DestroyImmediate(closeup); }
                            Record("gameplay-entry", path, focus, report.gameplayCamera.distance);
                        }

                        void Record(string view, string output, Vector3 center, float cameraDistance)
                        {
                            report.images.Add(new CaptureRecord { roomId = id, displayName = room.displayName, view = view, path = output,
                                representative = RepresentativeNumbers.Contains(index + 1), cameraPosition = camera.transform.position,
                                cameraEuler = camera.transform.eulerAngles, focus = center, cameraDistance = cameraDistance });
                        }
                    }
                    catch (Exception ex) { report.errors.Add(id + ": " + ex); }
                    finally
                    {
                        if (explorer) Object.DestroyImmediate(explorer);
                        if (instance) Object.DestroyImmediate(instance);
                    }
                }
                if (catalog)
                {
                    catalog.Apply(false, false);
                    report.catalogPath = OutputFolder + "/Catalog_20Rooms.jpg";
                    File.WriteAllBytes(report.catalogPath, catalog.EncodeToJPG(95));
                }
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (catalog) Object.DestroyImmediate(catalog);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                CheckOpenScenesUnchanged(state, report.errors);
            }
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            return WriteJson(OutputFolder + (onlyRoom == 0 ? "/capture-manifest.json" : "/capture-room-" + onlyRoom.ToString("00") + ".json"), report);
        }

        static CameraSettings ReadGameplayCamera(Scene scene)
        {
            var settings = new CameraSettings();
            var cameras = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true));
            foreach (var camera in cameras)
            {
                var follow = camera.GetComponent<AcRoguelike.IsometricFollowCamera>();
                if (!follow) continue;
                settings.fieldOfView = camera.fieldOfView;
                settings.pitch = follow.pitch;
                settings.yaw = follow.yaw;
                settings.distance = follow.distance;
                settings.nearClip = camera.nearClipPlane;
                settings.farClip = camera.farClipPlane;
                settings.background = camera.backgroundColor;
                var additional = camera.GetComponent<UniversalAdditionalCameraData>();
                if (additional)
                {
                    settings.renderPostProcessing = additional.renderPostProcessing;
                    settings.volumeLayerMask = additional.volumeLayerMask.value;
                }
                settings.source = LiminalMapBuilder.RunPath + " / " + camera.name;
                break;
            }
            return settings;
        }

        static GameObject CreateInScene(string name, Scene scene, params Type[] components)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static float FrameBounds(Camera camera, Bounds bounds, CameraSettings settings)
        {
            Quaternion rotation = Quaternion.Euler(settings.pitch, settings.yaw, 0);
            Quaternion inverse = Quaternion.Inverse(rotation);
            float tanY = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), tanX = tanY * camera.aspect;
            float distance = 1;
            foreach (var corner in Corners(bounds))
            {
                Vector3 p = inverse * (corner - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(p.x) / tanX - p.z, Mathf.Abs(p.y) / tanY - p.z);
            }
            distance = distance * 1.09f + 1;
            PositionCamera(camera, bounds.center, distance, settings);
            return distance;
        }

        static void PositionCamera(Camera camera, Vector3 focus, float distance, CameraSettings settings)
        {
            Quaternion rotation = Quaternion.Euler(settings.pitch, settings.yaw, 0);
            camera.transform.SetPositionAndRotation(focus + rotation * Vector3.back * distance, rotation);
        }

        static Texture2D Render(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                // Warm the exact visible material/light variants before the retained frame. Compilation is synchronous
                // for the whole capture scope, so a newly imported wall cannot disappear only in its first image.
                RenderCamera(camera, target);
                RenderCamera(camera, target);
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                image.Apply(false, false);
                return image;
            }
            catch { if (image) Object.DestroyImmediate(image); throw; }
            finally { RenderTexture.active = previous; }
        }

        static void RenderCamera(Camera camera, RenderTexture target)
        {
            // Camera.Render remains the built-in path. URP 17 explicitly supports isolated single-camera requests.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP single-camera rendering is unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else camera.Render();
        }

        static void AddCatalogTile(Texture2D catalog, Texture2D full, int index)
        {
            const int width = 640, height = 480, rowHeight = 520;
            var source = full.GetPixels32();
            var pixels = new Color32[width * height];
            // Exact two-to-one box filtering keeps the contact sheet sharp without another render.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int a = y * 2 * full.width + x * 2;
                    Color32 p = source[a], q = source[a + 1], r = source[a + full.width], s = source[a + full.width + 1];
                    pixels[y * width + x] = new Color32((byte)((p.r + q.r + r.r + s.r) / 4),
                        (byte)((p.g + q.g + r.g + s.g) / 4), (byte)((p.b + q.b + r.b + s.b) / 4), 255);
                }
            int left = index % 4 * width, bottom = catalog.height - (index / 4 + 1) * rowHeight;
            catalog.SetPixels32(left, bottom + 40, width, height, pixels);
            DrawNumber(catalog, index + 1, left + 14, bottom + 10);
        }

        static void DrawNumber(Texture2D texture, int value, int x, int y)
        {
            string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
                "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
            string digits = value.ToString("00");
            for (int n = 0; n < digits.Length; n++)
                for (int row = 0; row < 5; row++)
                    for (int column = 0; column < 3; column++)
                        if (glyphs[digits[n] - '0'][row * 3 + column] == '1')
                            for (int py = 0; py < 4; py++)
                                for (int px = 0; px < 4; px++)
                                    texture.SetPixel(x + n * 18 + column * 4 + px, y + (4 - row) * 4 + py, new Color(.88f, .86f, .70f));
        }

        static void RefreshText(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TextMesh>(true))
                if (text.font) text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
            foreach (var atlas in root.GetComponentsInChildren<LiminalSignageAtlas>(true))
                if (atlas.font && atlas.material) atlas.material.mainTexture = atlas.font.material.mainTexture;
        }

        static Bounds VisualBounds(LiminalRoom room)
        {
            Bounds bounds = WorldBounds(room);
            foreach (var renderer in room.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static Bounds WorldBounds(LiminalRoom room)
        {
            var result = new Bounds(room.transform.TransformPoint(room.localBounds.center), Vector3.zero);
            foreach (var corner in Corners(room.localBounds)) result.Encapsulate(room.transform.TransformPoint(corner));
            return result;
        }

        static Bounds BoxInRoomSpace(BoxCollider collider, Transform room)
        {
            var result = new Bounds(room.InverseTransformPoint(collider.transform.TransformPoint(collider.center)), Vector3.zero);
            foreach (var corner in Corners(new Bounds(collider.center, collider.size)))
                result.Encapsulate(room.InverseTransformPoint(collider.transform.TransformPoint(corner)));
            return result;
        }

        static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= Tolerance * Tolerance;
        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Backrooms validation and captures require Edit Mode.");
        }

        static Dictionary<int, bool> OpenSceneState()
        {
            var result = new Dictionary<int, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                result[scene.handle] = scene.isDirty;
            }
            return result;
        }

        static void CheckOpenScenesUnchanged(Dictionary<int, bool> before, List<string> errors)
        {
            var after = OpenSceneState();
            if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out bool dirty) || dirty != p.Value))
                errors.Add("Open scene membership or dirty state changed during the operation; no scenes were saved by this utility.");
        }

        static string WriteJson(string path, object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(path, json);
            return json;
        }
    }
}
