using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Opt-in validation of saved Game assets. Isolated preview scenes never save changes to authored scenes.</summary>
    [InitializeOnLoad]
    public static class GameThemeValidation
    {
        public const string Root = "Assets/GameTheme";
        public const string ScenePath = Root + "/Scenes/StageConcept_Game.unity";
        public const string StagePath = Root + "/Data/Stage_Game.asset";
        public const string ReportPath = "Documentation/GameTheme/validation.json";
        public const string Output = "Documentation/GameTheme/Previews";
        const string PlayPath = "Documentation/GameTheme/play-validation.json";
        const string ActiveKey = "GameTheme.Validation.Active", PlayKey = "GameTheme.Validation.Report", BatchKey = "GameTheme.Validation.BatchExit";
        const float Width = 26, Length = 36.4f, Tolerance = .03f;
        const float Cell = .5f, Radius = .24f, Height = 1.65f, Step = .24f, MaxFloorHeight = 1.2f;
        const long RoomBudget = 337270, RouteBudget = 1476948, SingleMeshBudget = 50000;
        static readonly int[] Pool = { 2, 3, 4, 6, 7 };

        [Serializable] public sealed class Report
        {
            public string status, utc, unityVersion;
            public string triangleScope = "Every active placed static/skinned scenery mesh including architecture. Player and runtime enemies excluded; not an FPS measurement.";
            public string captureScope = "Actual Unity URP renders in isolated Edit Mode scenes: overview and gameplay-camera framing, with the player. Runtime combat is verified separately.";
            public int expectedRooms = 8, passedRooms, captures;
            public long roomTriangleBudget = RoomBudget, maximumRouteTriangleBudget = RouteBudget, maximumRouteTriangles, uniqueMeshTriangles;
            public int distinctSeededRoutes;
            public bool openScenesUnchanged;
            public List<RoomCheck> rooms = new List<RoomCheck>();
            public List<MeshCheck> meshes = new List<MeshCheck>();
            public List<CaptureCheck> images = new List<CaptureCheck>();
            public List<string> checks = new List<string>(), errors = new List<string>();
        }
        [Serializable] public sealed class RoomCheck
        {
            public string roomId, path, status;
            public long placedTriangles;
            public int renderers, materials, colliders, meshColliders, lights, realtimeShadowLights, meshyInstances;
            public int sampledCells, walkableCells, reachedCells, reachableTargets;
            public Vector3 capturePoint;
            public List<TraversalFailure> traversalFailures = new List<TraversalFailure>();
            public List<EnemySpawnCheck> enemySpawnChecks = new List<EnemySpawnCheck>();
            public List<string> errors = new List<string>();
        }
        [Serializable] public sealed class EnemySpawnCheck
        {
            public string spawn, role, prefab, status;
            public Vector3 localPosition;
            public float radius, height, skinWidth, feetAboveFloor;
            public List<string> blockingObjects = new List<string>();
        }
        [Serializable] public sealed class TraversalFailure
        {
            public string target, reason;
            public Vector3 localPosition;
            public List<string> blockingObjects = new List<string>();
        }
        [Serializable] public sealed class MeshCheck
        {
            public string path, name;
            public int vertices, instances;
            public long triangles;
        }
        [Serializable] public sealed class CaptureCheck
        {
            public string room, framing, path;
            public int width = 1600, height = 1000;
            public Vector3 focus;
            public float distance, pitch, yaw, fieldOfView;
        }
        sealed class SceneSnapshot
        {
            public Scene scene;
            public string path;
            public bool dirty, loaded;
            public int[] roots;
        }

        [MenuItem("AC Roguelike/Game Theme/Validate and Capture All Rooms")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run validation in Edit Mode.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var original = SnapshotScenes();
            Scene active = SceneManager.GetActiveScene(), isolated = default;
            var meshes = new Dictionary<Mesh, MeshCheck>();
            try
            {
                isolated = EditorSceneManager.NewPreviewScene();
                for (int index = 1; index <= 8; index++)
                {
                    var check = new RoomCheck { roomId = RoomId(index), path = RoomPath(index) };
                    report.rooms.Add(check);
                    try { CheckRoom(index, isolated, check, meshes); }
                    catch (Exception ex) { check.errors.Add(ex.ToString()); }
                    check.status = check.errors.Count == 0 ? "passed" : "failed";
                    if (check.errors.Count == 0) report.passedRooms++;
                    report.errors.AddRange(check.errors.Select(e => check.roomId + ": " + e));
                }
                Attempt(() => CheckStage(report, isolated), "Stage");
                Attempt(() => CheckScene(report), "Scene");
                Attempt(() => CheckCatalog(report), "Gate catalog");
                foreach (var room in report.rooms) Attempt(() => CaptureRoom(room, report), room.roomId + " capture");
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
            finally
            {
                if (isolated.IsValid()) EditorSceneManager.ClosePreviewScene(isolated);
                if (active.IsValid() && SceneManager.GetActiveScene() != active) SceneManager.SetActiveScene(active);
                Physics.SyncTransforms();
                report.openScenesUnchanged = SceneManager.sceneCount == original.Count && SceneManager.GetActiveScene() == active && original.All(s =>
                    s.scene.IsValid() && s.scene.path == s.path && s.scene.isDirty == s.dirty && s.scene.isLoaded == s.loaded &&
                    (!s.loaded || s.roots.SequenceEqual(s.scene.GetRootGameObjects().Select(g => g.GetInstanceID()))));
                if (!report.openScenesUnchanged) report.errors.Add("Open scene state changed during isolated validation.");
            }
            report.meshes = meshes.Values.OrderByDescending(m => m.triangles).ToList();
            report.uniqueMeshTriangles = report.meshes.Sum(m => m.triangles);
            report.captures = report.images.Count;
            if (report.captures != 16) report.errors.Add("Expected sixteen overview/gameplay-camera captures; got " + report.captures + ".");
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            WriteJson(ReportPath, report);
            Debug.Log("GAME_THEME_VALIDATION: " + report.status + " (" + report.passedRooms + "/8 rooms; " + report.captures + " captures). " + ReportPath);
            void Attempt(Action action, string label) { try { action(); } catch (Exception ex) { report.errors.Add(label + ": " + ex); } }
        }

        static string RoomId(int n) => n == 8 ? "Game_08_Boss" : "Game_" + n.ToString("00");
        static string RoomPath(int n) => n == 8 ? GameTetrominoBossBuilder.ArenaPath : Root + "/Prefabs/Rooms/" + RoomId(n) + ".prefab";
        static void CheckRoom(int index, Scene scene, RoomCheck check, Dictionary<Mesh, MeshCheck> meshes)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(check.path);
            Require(source, "Missing room prefab: " + check.path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            try
            {
                instance.transform.SetPositionAndRotation(new Vector3(10000, 0, 10000), Quaternion.identity);
                var room = instance.GetComponent<LiminalRoom>();
                Require(room, "Missing LiminalRoom.");
                Check(room.roomId == RoomId(index), "Room ID does not match its asset path.");
                Check(!string.IsNullOrWhiteSpace(room.displayName), "Display name is empty.");
                Check(instance.activeInHierarchy && Near(instance.transform.localScale, Vector3.one), "Root must be active with unit scale.");
                Check(instance.GetComponent<GameThemeRoom>(), "Missing GameThemeRoom monster opt-in.");
                if (index == 1) Check(instance.GetComponent<GameThemeAtmosphere>(), "Arrival room needs GameThemeAtmosphere so lobby missions receive the same visual environment as the standalone scene.");
                Check(Mathf.Abs(room.localBounds.size.x - Width) < Tolerance && Mathf.Abs(room.localBounds.size.z - Length) < Tolerance &&
                    Mathf.Abs(room.localBounds.center.x) < Tolerance && Mathf.Abs(room.localBounds.min.z) < Tolerance, "Bounds must be 26 x 36.4 m, centered on x=0, beginning at z=0.");
                var expectedKind = index == 1 ? LiminalRoomKind.Arrival : index == 5 ? LiminalRoomKind.Threshold : index == 8 ? LiminalRoomKind.Boss : LiminalRoomKind.Combat;
                Check(room.kind == expectedKind, "Unexpected room kind: " + room.kind + "; expected " + expectedKind + ".");
                Require(room.entry && room.exit && room.playerSpawn, "Entry, exit and playerSpawn references are required.");
                Check(Near(room.transform.InverseTransformPoint(room.entry.position), Vector3.zero), "Entry socket is not at (0,0,0).");
                Check(Near(room.transform.InverseTransformPoint(room.exit.position), new Vector3(0, 0, Length)), "Exit socket is not at (0,0,36.4).");
                Check(Vector3.Angle(room.entry.forward, room.transform.forward) < .1f && Vector3.Angle(room.exit.forward, room.transform.forward) < .1f, "Socket directions must both be +Z.");
                Check(room.playerSpawn.IsChildOf(room.transform) && room.Contains(room.playerSpawn.position, .4f), "Player spawn is outside its room.");
                if (room.kind == LiminalRoomKind.Combat) Check(room.enemySpawns != null && room.enemySpawns.Length >= 3, "Combat room needs at least three enemy spawns.");
                if (room.kind == LiminalRoomKind.Boss) Check(room.enemySpawns != null && room.enemySpawns.Length == 1, "Drop Keeper arena needs exactly one boss spawn.");
                foreach (var spawn in room.enemySpawns ?? Array.Empty<Transform>())
                    Check(spawn && spawn.IsChildOf(room.transform) && room.Contains(spawn.position, .4f), "Enemy spawn is missing or outside its room.");
                CheckGate(room.entranceGate, true);
                CheckGate(room.exitGate, false);
                foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                {
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, transform.name + ": missing script.");
                    foreach (var component in transform.GetComponents<Component>())
                    {
                        if (!component) continue;
                        using (var serialized = new SerializedObject(component))
                        {
                            var iterator = serialized.GetIterator();
                            while (iterator.NextVisible(true))
                                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                                    Check(false, transform.name + "." + iterator.propertyPath + ": broken object reference.");
                        }
                    }
                }
                var materials = new HashSet<Material>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    check.renderers++;
                    Check(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m && m.shader && m.shader.name != "Hidden/InternalErrorShader"), renderer.name + ": missing material/shader.");
                    foreach (var material in renderer.sharedMaterials) if (material) materials.Add(material);
                    Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    Check(mesh && mesh.vertexCount > 0, renderer.name + ": missing or empty mesh.");
                    if (!mesh) continue;
                    if (!meshes.TryGetValue(mesh, out MeshCheck summary))
                    {
                        summary = new MeshCheck { path = AssetDatabase.GetAssetPath(mesh), name = mesh.name, triangles = Triangles(mesh), vertices = mesh.vertexCount };
                        meshes.Add(mesh, summary);
                    }
                    Check(summary.triangles <= SingleMeshBudget, mesh.name + ": single mesh exceeds 50,000 triangles (" + summary.triangles + ").");
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        summary.instances++;
                        check.placedTriangles += summary.triangles;
                        if (summary.path.StartsWith(Root + "/Art/Meshy/", StringComparison.Ordinal) && summary.path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) check.meshyInstances++;
                    }
                }
                check.materials = materials.Count;
                Check(check.placedTriangles <= RoomBudget, "Scenery exceeds Ruins room upper guide of " + RoomBudget + " triangles: " + check.placedTriangles + ".");
                Check(check.meshyInstances > 0, "No active imported Meshy model in this room.");
                var colliders = instance.GetComponentsInChildren<Collider>(true);
                check.colliders = colliders.Length;
                check.meshColliders = colliders.OfType<MeshCollider>().Count();
                Check(check.meshColliders == 0, "Use simple box colliders rather than MeshCollider for scenery.");
                var lights = instance.GetComponentsInChildren<Light>(true);
                check.lights = lights.Length;
                check.realtimeShadowLights = lights.Count(l => l.enabled && l.shadows != LightShadows.None);
                Check(check.realtimeShadowLights == 0, "Room accent lights must not cast real-time shadows.");
                room.SetGates(false, false);
                Physics.SyncTransforms();
                CheckTraversal(room, scene.GetPhysicsScene(), check);
                if (room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss)
                {
                    // The encounter activates with both doors shut, and enemies are larger than the player.
                    room.SetGates(true, true);
                    Physics.SyncTransforms();
                    CheckEnemySpawns(room, scene.GetPhysicsScene(), check);
                }
                void Check(bool ok, string message) { if (!ok) check.errors.Add(message); }
                void CheckGate(GameObject gate, bool entrance)
                {
                    string label = entrance ? "Entrance gate" : "Exit gate";
                    Check(gate && gate.transform.IsChildOf(room.transform), label + " reference missing or external.");
                    if (!gate) return;
                    var boxes = gate.GetComponentsInChildren<BoxCollider>(true).Where(b => b.enabled && !b.isTrigger).ToArray();
                    Check(boxes.Length > 0, label + " has no solid box collider.");
                    if (boxes.Length == 0) return;
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    foreach (var box in boxes) for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 p = room.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, new Vector3(x, 0, z))));
                        min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x);
                    }
                    Check(Mathf.Abs(max - min - 6.8f) <= .15f, label + " must span the 6.8 m doorway; actual " + (max - min) + ".");
                    Vector3 local = room.transform.InverseTransformPoint(gate.transform.position);
                    Check(Mathf.Abs(local.x) < Tolerance && (entrance ? local.z >= -Tolerance && local.z <= 1.6f : local.z >= Length - 1.6f && local.z <= Length + Tolerance), label + " is misaligned.");
                }
            }
            finally { Object.DestroyImmediate(instance); Physics.SyncTransforms(); }
        }

        static void CheckEnemySpawns(LiminalRoom room, PhysicsScene physics, RoomCheck check)
        {
            var theme = room.GetComponent<GameThemeRoom>();
            var library = AssetDatabase.LoadAssetAtPath<GameVoxelMonsterLibrary>(Root + "/Resources/GameTheme/GameVoxelMonsterLibrary.asset");
            bool isBoss = room.kind == LiminalRoomKind.Boss;
            var bossPrefab = isBoss ? AssetDatabase.LoadAssetAtPath<GameObject>(GameTetrominoBossBuilder.PrefabPath) : null;
            if (!theme || (isBoss ? !bossPrefab : !library) || !physics.IsValid())
            {
                check.errors.Add("Enemy body validation needs the theme marker, the matching monster/boss prefab and a valid physics scene.");
                return;
            }
            var overlaps = new Collider[256];
            var floorHits = new RaycastHit[64];
            for (int index = 0; index < (room.enemySpawns?.Length ?? 0); index++)
            {
                var spawn = room.enemySpawns[index];
                string role = isBoss ? "DropKeeper" : theme.RoleAt(index, false).ToString();
                var prefab = isBoss ? bossPrefab : library.Prefab(theme.RoleAt(index, false));
                var body = prefab ? prefab.GetComponent<CharacterController>() : null;
                var result = new EnemySpawnCheck { spawn = spawn ? spawn.name : "Enemy_" + index, role = role, prefab = prefab ? AssetDatabase.GetAssetPath(prefab) : "", status = "failed" };
                check.enemySpawnChecks.Add(result);
                if (!spawn || !body)
                {
                    check.errors.Add(result.spawn + " (" + role + "): spawn or prefab CharacterController missing.");
                    continue;
                }
                // Normal enemies use room.y + .05; the dedicated boss preserves its authored marker height.
                Vector3 position = spawn.position;
                if (!isBoss) position.y = room.transform.position.y + .05f;
                result.localPosition = room.transform.InverseTransformPoint(position);
                Vector3 scale = prefab.transform.localScale;
                float radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                result.radius = body.radius * radialScale;
                result.height = Mathf.Max(body.height * Mathf.Abs(scale.y), result.radius * 2);
                result.skinWidth = body.skinWidth * Mathf.Max(radialScale, Mathf.Abs(scale.y));
                Vector3 look = (room.entry ? room.entry.position : room.transform.position) - position;
                look.y = 0;
                Quaternion facing = Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : room.transform.forward);
                Vector3 center = position + facing * Vector3.Scale(body.center, scale);
                float halfSegment = result.height * .5f - result.radius;
                Vector3 bottom = center - Vector3.up * halfSegment, top = center + Vector3.up * halfSegment;
                float feetY = center.y - result.height * .5f;
                // Require a small skin-width margin from scenery. The supporting floor below the feet is
                // checked separately; it must not turn a valid grounded spawn into a false wall overlap.
                int overlapCount = physics.OverlapCapsule(bottom, top, result.radius + result.skinWidth, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (overlapCount == overlaps.Length) result.blockingObjects.Add("OVERLAP_BUFFER_FULL");
                for (int n = 0; n < overlapCount; n++)
                {
                    var obstacle = overlaps[n];
                    if (!obstacle || !obstacle.transform.IsChildOf(room.transform)) continue;
                    if (obstacle.bounds.max.y <= feetY + .002f) continue;
                    bool gate = (room.entranceGate && obstacle.transform.IsChildOf(room.entranceGate.transform)) ||
                        (room.exitGate && obstacle.transform.IsChildOf(room.exitGate.transform));
                    result.blockingObjects.Add((gate ? "Closed gate: " : "Scenery: ") + RelativePath(obstacle.transform, room.transform));
                }
                float floor = float.NegativeInfinity;
                int floorCount = physics.Raycast(new Vector3(center.x, feetY + .1f, center.z), Vector3.down, floorHits, .6f, ~0, QueryTriggerInteraction.Ignore);
                if (floorCount == floorHits.Length) result.blockingObjects.Add("FLOOR_BUFFER_FULL");
                for (int n = 0; n < floorCount; n++)
                    if (floorHits[n].collider && floorHits[n].collider.transform.IsChildOf(room.transform) && floorHits[n].normal.y > .7f)
                        floor = Mathf.Max(floor, floorHits[n].point.y);
                result.feetAboveFloor = float.IsNegativeInfinity(floor) ? -999 : feetY - floor;
                if (float.IsNegativeInfinity(floor) || result.feetAboveFloor < -.002f || result.feetAboveFloor > .12f)
                    result.blockingObjects.Add("Missing or misaligned supporting floor (feet gap " + result.feetAboveFloor.ToString("F3") + " m).");
                result.blockingObjects = result.blockingObjects.Distinct().ToList();
                result.status = result.blockingObjects.Count == 0 ? "passed" : "failed";
                if (result.status != "passed") check.errors.Add(result.spawn + " (" + role + ") at " + result.localPosition +
                    ": actual enemy capsule radius " + result.radius.ToString("F3") + ", height " + result.height.ToString("F3") +
                    ", skin " + result.skinWidth.ToString("F3") + " is not clear: " + string.Join("; ", result.blockingObjects));
            }
        }

        static void CheckStage(Report report, Scene preview)
        {
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(StagePath);
            Require(stage, "Missing Game stage definition.");
            Require(stage.startRoom && AssetDatabase.GetAssetPath(stage.startRoom) == RoomPath(1), "Start must be Game_01.");
            Require(stage.endRoom && AssetDatabase.GetAssetPath(stage.endRoom) == RoomPath(8) && stage.endRoom.kind == LiminalRoomKind.Boss, "End must be the Game_08_Boss arena.");
            Require(stage.middleRoomCount == 3 && stage.roomPool != null && stage.roomPool.Length == 5, "Stage must draw three middle rooms from five candidates.");
            Require(stage.roomPool.All(r => r) && stage.roomPool.Select(AssetDatabase.GetAssetPath).OrderBy(p => p).SequenceEqual(Pool.Select(RoomPath).OrderBy(p => p)), "Middle pool must contain 02,03,04,06,07 once each.");
            Require(!stage.isBossStage, "Game exhibition must retain the five-room route instead of the standalone boss-stage route.");
            long fixedCount = report.rooms.Where(r => r.roomId == RoomId(1) || r.roomId == RoomId(8)).Sum(r => r.placedTriangles);
            report.maximumRouteTriangles = fixedCount + report.rooms.Where(r => Pool.Any(n => r.roomId == "Game_" + n.ToString("00"))).OrderByDescending(r => r.placedTriangles).Take(3).Sum(r => r.placedTriangles);
            Require(report.maximumRouteTriangles <= RouteBudget, "Maximum five-room combination exceeds Ruins guide: " + report.maximumRouteTriangles + ".");
            var signatures = new HashSet<string>();
            var visited = new HashSet<string>();
            for (int seed = 1; seed <= 64; seed++)
            {
                var route = stage.ChooseRoute(seed, 0);
                Require(route.Length == 5 && route.Distinct().Count() == 5, "Route does not contain five distinct rooms at seed " + seed + ".");
                Require(route[0] == stage.startRoom && route[4] == stage.endRoom, "Route does not end in Drop Keeper at seed " + seed + ".");
                string signature = string.Join("|", route.Select(r => r.roomId));
                Require(signature == string.Join("|", stage.ChooseRoute(seed, 0).Select(r => r.roomId)), "Non-deterministic route at seed " + seed + ".");
                signatures.Add(signature); visited.UnionWith(route.Select(r => r.roomId));
            }
            report.distinctSeededRoutes = signatures.Count;
            Require(signatures.Count > 1 && visited.Count == 7, "Seeds do not cover the complete room pool.");
            var host = new GameObject("__GameRouteValidation");
            SceneManager.MoveGameObjectToScene(host, preview);
            try
            {
                host.transform.position = new Vector3(10000, 0, 10000);
                var director = host.AddComponent<LiminalRunDirector>();
                director.stages = new[] { stage }; director.seed = 73029; director.GeneratePreview(0);
                Require(director.Rooms.Count == 5, "Director did not assemble five rooms.");
                for (int i = 1; i < director.Rooms.Count; i++)
                {
                    var previous = director.Rooms[i - 1]; var current = director.Rooms[i];
                    Require(Vector3.Distance(previous.exit.position, current.entry.position) < .01f && Quaternion.Angle(previous.exit.rotation, current.entry.rotation) < .1f, "Room socket seam mismatch at " + i + ".");
                    for (int j = 0; j < i; j++)
                    {
                        Bounds a = WorldBounds(current), b = WorldBounds(director.Rooms[j]);
                        Require(Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) < .05f || Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) < .05f, "Room footprint overlap.");
                    }
                }
            }
            finally { Object.DestroyImmediate(host); }
            report.checks.Add("Eight room assets; deterministic five-room routes with three unique middle rooms and final Drop Keeper arena, matching seams, no overlapping footprints and maximum-combination triangle guide checked.");
        }

        static void CheckScene(Report report)
        {
            Require(File.Exists(ScenePath), "Missing saved execution scene.");
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var directors = roots.SelectMany(r => r.GetComponentsInChildren<LiminalRunDirector>(true)).ToArray();
                Require(directors.Length == 1, "Expected one LiminalRunDirector.");
                var run = directors[0];
                Require(run.stages != null && run.stages.Length == 1 && run.stages[0] == AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(StagePath), "Scene must run only Stage_Game.");
                Require(run.player && run.player.gameObject.scene == scene && run.player.GetComponent<PlayerMotor>() && run.player.GetComponent<CharacterController>(), "Scene player is missing or external.");
                var body = run.player.GetComponent<CharacterController>();
                Require(body.enabled && body.radius <= Radius + .01f && body.height <= Height + .01f, "Player controller does not match traversal dimensions.");
                var cameras = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Where(c => c.enabled && c.CompareTag("MainCamera")).ToArray();
                Require(cameras.Length == 1, "Expected one enabled MainCamera.");
                var rig = cameras[0].GetComponent<IsometricFollowCamera>();
                Require(rig && rig.target == run.player, "Camera is not following this player.");
                Require(Mathf.Abs(rig.pitch - 55) < .1f && Mathf.Abs(rig.yaw - 35) < .1f && Mathf.Abs(rig.distance - 24) < .1f && Mathf.Abs(cameras[0].fieldOfView - 36) < .1f, "Game scene must preserve the authored 55/35/24/FOV36 camera.");
                Require(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ScenePath), "Game execution scene is missing from enabled build settings.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            report.checks.Add("Saved scene player, camera, stage reference and build registration checked.");
        }

        static void CheckCatalog(Report report)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            Require(catalog && catalog.missions != null, "Gate catalog is missing.");
            var missions = catalog.missions.Where(m => m != null && m.id == "game_exhibition").ToArray();
            Require(missions.Length == 1, "Gate catalog needs exactly one game_exhibition mission.");
            Require(missions[0].code == "G-06", "Game mission must retain its stable G-06 code when other missions are removed.");
            Require(missions[0].IsAvailable && missions[0].stages.Length == 1 && missions[0].stages[0] == AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(StagePath), "Game mission does not resolve to Stage_Game.");
            report.checks.Add("Gate mission G-06 game_exhibition resolves to the playable Game stage.");
        }

        static void CaptureRoom(RoomCheck check, Report report)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(check.path);
            Require(source && File.Exists(ScenePath), "Room or scene missing for capture.");
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Color background = new Color(.025f, .035f, .065f);
                foreach (var root in scene.GetRootGameObjects())
                {
                    var authoredCamera = root.GetComponent<Camera>();
                    if (authoredCamera) background = authoredCamera.backgroundColor;
                    if (root.GetComponent<Light>() || root.GetComponent<Volume>()) continue;
                    Object.DestroyImmediate(root);
                }
                var room = ((GameObject)PrefabUtility.InstantiatePrefab(source, scene)).GetComponent<LiminalRoom>();
                room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); room.SetGates(false, false);
                var cameraObject = new GameObject("Game theme review camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false; camera.scene = scene; camera.cameraType = CameraType.Game;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = background;
                camera.nearClipPlane = .1f; camera.farClipPlane = 300; camera.allowHDR = true; camera.aspect = 1.6f;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true; data.volumeLayerMask = 1; data.allowXRRendering = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                target.Create(); camera.targetTexture = target;
                var playerSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Astraia/AstraiaPlayer.prefab");
                Require(playerSource, "Astraia prefab missing for scale reference.");
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerSource, scene);
                player.transform.position = check.reachedCells > 0 ? check.capturePoint + Vector3.up * .05f : room.playerSpawn.position + Vector3.up * .05f;
                player.transform.rotation = Quaternion.Euler(0, 35, 0);
                var animator = player.GetComponentInChildren<Animator>();
                if (animator && animator.runtimeAnimatorController)
                {
                    var idle = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (idle) idle.SampleAnimation(animator.gameObject, .25f);
                }
                Shot("Overview", new Vector3(0, 2.5f, 18), 58, 48, 32, 44);
                Shot("Gameplay", player.transform.position + Vector3.up * .55f, 24, 55, 35, 36);
                void Shot(string label, Vector3 focus, float distance, float pitch, float yaw, float fov)
                {
                    camera.fieldOfView = fov;
                    Quaternion q = Quaternion.Euler(pitch, yaw, 0);
                    camera.transform.SetPositionAndRotation(focus + q * Vector3.back * distance, q);
                    string path = Output + "/" + check.roomId + "_" + label + ".png";
                    SaveImage(camera, target, path);
                    report.images.Add(new CaptureCheck { room = check.roomId, framing = label, path = path, focus = focus, distance = distance, pitch = pitch, yaw = yaw, fieldOfView = fov });
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = asynchronous; target.Release(); Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void SaveImage(Camera camera, RenderTexture target, string path)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP SingleCameraRequest unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); image.Apply(false, false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (image) Object.DestroyImmediate(image); }
        }

        static long Triangles(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
                else if (mesh.GetTopology(i) == MeshTopology.Quads) count += mesh.GetIndexCount(i) / 4 * 2;
            }
            return count;
        }
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= Tolerance;
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void WriteJson(string path, object data) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(data, true)); }
        static string RelativePath(Transform current, Transform root)
        {
            string path = current.name;
            while (current.parent && current.parent != root) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }
        static Bounds WorldBounds(LiminalRoom room)
        {
            var bounds = new Bounds(room.transform.TransformPoint(room.localBounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(room.transform.TransformPoint(room.localBounds.center + Vector3.Scale(room.localBounds.extents, new Vector3(x, y, z))));
            return bounds;
        }
        static List<SceneSnapshot> SnapshotScenes()
        {
            var result = new List<SceneSnapshot>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                result.Add(new SceneSnapshot { scene = scene, path = scene.path, dirty = scene.isDirty, loaded = scene.isLoaded, roots = scene.isLoaded ? scene.GetRootGameObjects().Select(g => g.GetInstanceID()).ToArray() : Array.Empty<int>() });
            }
            return result;
        }

        static void CheckTraversal(LiminalRoom room, PhysicsScene physics, RoomCheck check)
        {
            if (!physics.IsValid()) { check.errors.Add("No valid physics scene for traversal checks."); return; }
            int columns = Mathf.CeilToInt(room.localBounds.size.x / Cell), rows = Mathf.CeilToInt(room.localBounds.size.z / Cell);
            int count = columns * rows;
            var blocked = new bool[count];
            var visited = new bool[count];
            var floorHeights = new float[count];
            var causes = new List<string>[count];
            var overlaps = new Collider[128];
            var hits = new RaycastHit[128];
            check.sampledCells = count;
            for (int i = 0; i < count; i++)
            {
                causes[i] = CapsuleObstacles(World(i), out floorHeights[i]);
                blocked[i] = causes[i].Count > 0;
                if (!blocked[i]) check.walkableCells++;
            }
            int first = Node(room.playerSpawn.position);
            var queue = new Queue<int>();
            if (!blocked[first]) { visited[first] = true; queue.Enqueue(first); }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                check.reachedCells++;
                foreach (int next in Neighbours(current))
                {
                    if (visited[next] || blocked[next] || Mathf.Abs(floorHeights[current] - floorHeights[next]) > Step) continue;
                    Vector3 from = World(current), to = World(next);
                    float floor = Mathf.Max(floorHeights[current], floorHeights[next]);
                    Vector3 bottom = new Vector3(from.x, floor + Step + Radius + .002f, from.z);
                    Vector3 top = new Vector3(from.x, floor + Height - Radius, from.z);
                    Vector3 direction = to - from;
                    int hitCount = physics.CapsuleCast(bottom, top, Radius, direction.normalized, hits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
                    if (hitCount == hits.Length) throw new InvalidOperationException("Capsule sweep buffer overflowed.");
                    bool clear = true;
                    for (int k = 0; k < hitCount; k++)
                        if (hits[k].collider && hits[k].collider.transform.IsChildOf(room.transform)) { clear = false; break; }
                    if (!clear) continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            int capture = Enumerable.Range(0, count).Where(i => visited[i]).OrderBy(i =>
                (room.transform.InverseTransformPoint(World(i)) - new Vector3(0, 0, 18)).sqrMagnitude).DefaultIfEmpty(first).First();
            check.capturePoint = room.transform.InverseTransformPoint(World(capture));
            check.capturePoint.y = floorHeights[capture] - room.transform.position.y;
            if (room.roomId == "Game_01")
                for (int i = 1; i <= 10; i++) Target("Arrival forward walk " + (i * .25f), room.playerSpawn.position + room.transform.forward * (i * .25f));
            Target("Player spawn", room.playerSpawn.position);
            Target("Entry approach", room.entry.position + room.entry.forward * .8f);
            Target("Exit approach", room.exit.position - room.exit.forward * .8f);
            if (room.enemySpawns != null)
                foreach (var spawn in room.enemySpawns) if (spawn) Target(spawn.name, spawn.position);

            List<string> CapsuleObstacles(Vector3 world, out float floor)
            {
                var result = new List<string>();
                floor = float.NegativeInfinity;
                // Highest walkable surface under the sample, so bridge decks and ramps count as floor and cells beneath them do not.
                int hitCount = physics.Raycast(world + Vector3.up * (MaxFloorHeight + Step + .05f), Vector3.down, hits, MaxFloorHeight + Step + .2f, ~0, QueryTriggerInteraction.Ignore);
                if (hitCount == hits.Length) throw new InvalidOperationException("Floor raycast buffer overflowed.");
                for (int i = 0; i < hitCount; i++)
                    if (hits[i].collider.transform.IsChildOf(room.transform) && hits[i].normal.y > .7f) floor = Mathf.Max(floor, hits[i].point.y);
                if (float.IsNegativeInfinity(floor)) { result.Add("NO_WALKABLE_FLOOR"); return result; }
                Vector3 bottom = new Vector3(world.x, floor + Step + Radius + .002f, world.z);
                Vector3 top = new Vector3(world.x, floor + Height - Radius, world.z);
                int overlapCount = physics.OverlapCapsule(bottom, top, Radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (overlapCount == overlaps.Length) throw new InvalidOperationException("Capsule overlap buffer overflowed.");
                for (int i = 0; i < overlapCount; i++)
                    if (overlaps[i].transform.IsChildOf(room.transform)) result.Add(RelativePath(overlaps[i].transform, room.transform));
                return result.Distinct().ToList();
            }
            void Target(string name, Vector3 position)
            {
                Vector3 planePosition = position;
                planePosition.y = room.transform.position.y;
                var exactObstacles = CapsuleObstacles(planePosition, out _);
                int target = Node(position);
                if (visited[target] && exactObstacles.Count == 0) { check.reachableTargets++; return; }
                var failure = new TraversalFailure
                {
                    target = name, localPosition = room.transform.InverseTransformPoint(position),
                    reason = exactObstacles.Count > 0 ? "Target capsule overlaps geometry or lacks walkable floor." : "No continuous capsule route from player spawn."
                };
                failure.blockingObjects.AddRange(exactObstacles);
                if (blocked[target]) failure.blockingObjects.AddRange(causes[target]);
                if (blocked[first]) failure.blockingObjects.AddRange(causes[first]);
                if (failure.blockingObjects.Count == 0)
                    foreach (int boundary in Enumerable.Range(0, count).Where(i => blocked[i] && Neighbours(i).Any(n => visited[n]))
                        .OrderBy(i => (World(i) - position).sqrMagnitude).Take(6)) failure.blockingObjects.AddRange(causes[boundary]);
                failure.blockingObjects = failure.blockingObjects.Distinct().Take(16).ToList();
                check.traversalFailures.Add(failure);
                check.errors.Add(name + ": " + failure.reason);
            }
            Vector3 World(int node) => room.transform.TransformPoint(new Vector3(room.localBounds.min.x + (node % columns + .5f) * Cell,
                0, Mathf.Min((node / columns + .5f) * Cell, room.localBounds.max.z - .05f)));
            int Node(Vector3 world)
            {
                Vector3 local = room.transform.InverseTransformPoint(world);
                return Mathf.Clamp(Mathf.FloorToInt((local.z - room.localBounds.min.z) / Cell), 0, rows - 1) * columns +
                       Mathf.Clamp(Mathf.FloorToInt((local.x - room.localBounds.min.x) / Cell), 0, columns - 1);
            }
            IEnumerable<int> Neighbours(int node)
            {
                int x = node % columns, z = node / columns;
                if (x > 0) yield return node - 1;
                if (x < columns - 1) yield return node + 1;
                if (z > 0) yield return node - columns;
                if (z < rows - 1) yield return node + columns;
            }
        }


        [Serializable] public sealed class CombatObservation
        {
            public int roomIndex, attackCountAtStart, observedAttacks, frameCountAtStart, observedFrames;
            public string roomId, status = "observing", completionReason;
            public float gameSeconds, wallSeconds;
            public List<string> diagnostics = new List<string>();
            public string capture;
        }

        [Serializable] public sealed class PlayReport
        {
            public string status = "running", utc;
            public string scope = "Actual PlayerMotor walk/dash, three voxel encounters, one Meshy Drop Keeper encounter with a natural attack, combat gate locking, test-damage clear and five-room Victory. Not a balance test; room transitions are teleported after their static paths are checked.";
            public int physicalWalkChecks, physicalDashChecks, combatRoomsCleared, voxelEnemiesSpawned, observedAttacks, completedRooms;
            public int bossesSpawned, bossRoomsCleared, observedBossAttacks;
            public float walkDistance, dashDistance;
            public List<CombatObservation> combatObservations = new List<CombatObservation>();
            public CombatObservation bossObservation;
            public List<string> checks = new List<string>(), errors = new List<string>();
        }
        static PlayReport playReport;
        static LiminalRunDirector run;
        static PlayerMotor motor;
        static LiminalPlayerHealth playerHealth;
        static string state = "AwaitRun";
        static int wantedRoom, beforeDashCount;
        static double stateStart, nextTick;
        static float combatGameStart;
        static CombatObservation combatObservation;
        static GameTetrominoBoss observedBoss;
        static Vector3 movementStart;

        static GameThemeValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += mode =>
            {
                if (mode != PlayModeStateChange.EnteredEditMode) return;
                if (SessionState.GetBool(ActiveKey, false)) FinishPlay("Play Mode ended before validation finished.");
                RestoreProgress();
                if (SessionState.GetBool(BatchKey, false))
                {
                    SessionState.SetBool(BatchKey, false);
                    var completed = JsonUtility.FromJson<PlayReport>(SessionState.GetString(PlayKey, "{}"));
                    EditorApplication.Exit(completed != null && completed.status == "passed" ? 0 : 1);
                }
            };
            if (SessionState.GetBool(ActiveKey, false))
            {
                playReport = JsonUtility.FromJson<PlayReport>(SessionState.GetString(PlayKey, "{}"));
                state = "AwaitRun"; stateStart = EditorApplication.timeSinceStartup;
            }
        }

        [MenuItem("AC Roguelike/Game Theme/Validate Five Room Playthrough")]
        public static void BeginPlayValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before starting validation.");
            SessionState.SetBool(BatchKey, false);
            for (int i = 0; i < SceneManager.sceneCount; i++) Require(!SceneManager.GetSceneAt(i).isDirty, "Save open scene changes before starting play validation.");
            Require(File.Exists(ScenePath), "Build the Game scene first.");
            string visitedKey=HunterProgress.StageDiscoveryKey("concept_game");
            SessionState.SetBool(ActiveKey+".PrefsCaptured",true);
            SessionState.SetBool(ActiveKey+".VisitedExisted",PlayerPrefs.HasKey(visitedKey));
            SessionState.SetInt(ActiveKey+".VisitedValue",PlayerPrefs.GetInt(visitedKey));
            playReport = new PlayReport { utc = DateTime.UtcNow.ToString("O") };
            wantedRoom = 0; run = null; motor = null; playerHealth = null; observedBoss = null;
            SavePlay(); SessionState.SetBool(ActiveKey, true); Next("AwaitRun");
            try { EditorSceneManager.OpenScene(ScenePath); EditorApplication.isPlaying = true; }
            catch (Exception ex) { FinishPlay(ex.ToString()); RestoreProgress(); throw; }
        }

        static void RestoreProgress()
        {
            if(!SessionState.GetBool(ActiveKey+".PrefsCaptured",false))return;
            string key=HunterProgress.StageDiscoveryKey("concept_game");
            if(SessionState.GetBool(ActiveKey+".VisitedExisted",false))PlayerPrefs.SetInt(key,SessionState.GetInt(ActiveKey+".VisitedValue",0));
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();SessionState.SetBool(ActiveKey+".PrefsCaptured",false);
        }

        /// <summary>Command-line entry point: omit -quit; waits for asynchronous play validation and exits after returning to Edit Mode.</summary>
        public static void ValidateBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("ValidateBatch is only for an isolated batch Editor. Use BeginPlayValidation in an interactive Editor.");
            try { BeginPlayValidation(); SessionState.SetBool(BatchKey, true); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false) || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < nextTick) return;
            nextTick = now + .06;
            if (state == "ObserveCombat" && combatObservation != null)
            {
                combatObservation.gameSeconds = Mathf.Max(0, Time.time - combatGameStart);
                combatObservation.wallSeconds = (float)(now - stateStart);
                if (combatObservation.wallSeconds >= 40)
                {
                    if (run && wantedRoom < run.Rooms.Count)
                        combatObservation.observedAttacks = Mathf.Max(0, run.Rooms[wantedRoom].GetComponentsInChildren<GameVoxelMonster>().Sum(m => m.Attacks) - combatObservation.attackCountAtStart);
                    combatObservation.status = "failed";
                    combatObservation.completionReason = combatObservation.gameSeconds < 5 ? "insufficient_game_time_before_wall_timeout" : "wall_clock_timeout";
                    playReport.observedAttacks += combatObservation.observedAttacks;
                    FinishPlay($"Combat observation {combatObservation.roomId} reached the 40 wall-second limit: {combatObservation.gameSeconds:F2} game seconds, {combatObservation.wallSeconds:F2} wall seconds, {combatObservation.observedAttacks} attacks. At least 5 game seconds and one natural attack are required; no-attack observation stops at 15 game seconds.");
                    return;
                }
            }
            if (state == "ObserveBoss" && playReport?.bossObservation != null)
            {
                var observation = playReport.bossObservation;
                observation.gameSeconds = Mathf.Max(0, Time.time - combatGameStart);
                observation.wallSeconds = (float)(now - stateStart);
                observation.observedFrames = Time.frameCount - observation.frameCountAtStart;
                observation.observedAttacks = observedBoss ? Mathf.Max(0, observedBoss.Attacks - observation.attackCountAtStart) : 0;
                if (observation.wallSeconds >= 40)
                {
                    observation.status = "failed";
                    observation.completionReason = "wall_clock_timeout";
                    FinishPlay($"Drop Keeper observation reached 40 wall seconds after {observation.gameSeconds:F2} game seconds with {observation.observedAttacks} attacks.");
                    return;
                }
            }
            if (now - stateStart > 60) { FinishPlay("Timed out at " + state + "."); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (playerHealth) playerHealth.GrantInvulnerability(3);
                switch (state)
                {
                    case "AwaitRun":
                        run = Object.FindFirstObjectByType<LiminalRunDirector>();
                        if (!run || run.Rooms.Count == 0) return;
                        Require(run.Phase == LiminalRunPhase.Exploring && run.Rooms.Count == 5 && run.stages.Length == 1, "Expected an exploring, five-room Game run.");
                        Require(run.Rooms.All(r => r.roomId.StartsWith("Game_", StringComparison.Ordinal)), "Wrong theme loaded.");
                        Require(run.ActiveRoomIndex == 0 && run.ClearedRoomCount == 1, "Arrival did not activate and clear.");
                        Require(GameThemeAtmosphere.IsApplied && GameThemeAtmosphere.LeaseCount == 1, "Game environment must have exactly one runtime lease.");
                        motor = run.player.GetComponent<PlayerMotor>(); playerHealth = run.player.GetComponent<LiminalPlayerHealth>();
                        Require(motor && motor.viewCamera && playerHealth, "Missing player motor, camera or health.");
                        playerHealth.GrantInvulnerability(3);
                        movementStart = motor.transform.position;
                        MoveWorld(Vector3.forward, false); Next("Walk"); break;
                    case "Walk":
                        if (motor.transform.position.z - movementStart.z < 2.2f) return;
                        MoveWorld(Vector3.zero, false);
                        playReport.walkDistance = Vector3.ProjectOnPlane(motor.transform.position - movementStart, Vector3.up).magnitude;
                        Require(Mathf.Abs(motor.transform.position.y - movementStart.y) < .5f, "Player fell or climbed unexpectedly during forward walk.");
                        playReport.physicalWalkChecks++;
                        // Test the same proven clear arrival corridor in the opposite direction; no extra clearance assumption.
                        movementStart = motor.transform.position;
                        beforeDashCount = motor.DashCount;
                        MoveWorld(Vector3.back, true); Next("Dash"); break;
                    case "Dash":
                        MoveWorld(Vector3.zero, false);
                        if (now - stateStart < .7) return;
                        Require(motor.DashCount == beforeDashCount + 1, "Automation did not execute exactly one dash.");
                        playReport.dashDistance = Vector3.ProjectOnPlane(motor.transform.position - movementStart, Vector3.up).magnitude;
                        Require(playReport.dashDistance >= 2.5f && playReport.dashDistance <= 3.5f, "Unobstructed arrival dash did not travel approximately 3m: " + playReport.dashDistance + ".");
                        playReport.physicalDashChecks++;
                        wantedRoom = 1; EnterRoom(); break;
                    case "AwaitRoom":
                        if (run.ActiveRoomIndex != wantedRoom) return;
                        var room = run.Rooms[wantedRoom];
                        if (room.kind == LiminalRoomKind.Combat)
                        {
                            var monsters = room.GetComponentsInChildren<GameVoxelMonster>();
                            Require(monsters.Length == room.enemySpawns.Length && monsters.Length >= 3, "Voxel roster does not match the room's spawn markers.");
                            Require(run.LivingEnemyCount == monsters.Length, "Enemy counter includes a foreign monster or misses a voxel monster.");
                            Require(room.GetComponentsInChildren<LiminalPropMonster>().All(m => m is GameVoxelMonster), "Office monsters leaked into the Game roster.");
                            Require(monsters.All(m => m.Health && m.Health.IsAlive && m.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled && r.GetComponent<MeshFilter>()?.sharedMesh)), "Missing living voxel body or mesh.");
                            Require(room.entranceGate.activeSelf && room.exitGate.activeSelf, "Combat gates did not lock.");
                            Require(!run.TryUseExit(), "Uncleared room allowed stage exit.");
                            playReport.voxelEnemiesSpawned += monsters.Length;
                            // Give the slow guardian enough game time to reach the stationary player and finish its windup.
                            Next("ObserveCombat");
                            combatGameStart = Time.time;
                            combatObservation = new CombatObservation
                            {
                                roomIndex = wantedRoom, roomId = room.roomId,
                                attackCountAtStart = monsters.Sum(m => m.Attacks), frameCountAtStart = Time.frameCount
                            };
                            playReport.combatObservations.Add(combatObservation);
                            RecordCombatState("start", monsters, false);
                        }
                        else if (room.kind == LiminalRoomKind.Boss)
                        {
                            var bosses = room.GetComponentsInChildren<GameTetrominoBoss>();
                            Require(wantedRoom == 4 && room.roomId == RoomId(8), "Drop Keeper must occupy the final Game room.");
                            Require(bosses.Length == 1 && room.enemySpawns.Length == 1 && run.LivingEnemyCount == 1, "Final Game room must register exactly one Drop Keeper.");
                            Require(room.GetComponentsInChildren<TrafficLightBoss>().Length == 0 && room.GetComponentsInChildren<GameVoxelMonster>().Length == 0,
                                "Foreign traffic-light or voxel boss leaked into the final Game room.");
                            observedBoss = bosses[0];
                            Require(observedBoss.Health && observedBoss.Health.IsAlive && observedBoss.GetComponent<CharacterController>().enabled,
                                "Drop Keeper must have a living health component and enabled body.");
                            Require(room.entranceGate.activeSelf && room.exitGate.activeSelf && !run.TryUseExit(), "Boss encounter must lock both gates and stage exit.");
                            playReport.bossesSpawned++;
                            playReport.bossObservation = new CombatObservation
                            {
                                roomIndex = wantedRoom, roomId = room.roomId,
                                attackCountAtStart = observedBoss.Attacks, frameCountAtStart = Time.frameCount
                            };
                            combatGameStart = Time.time;
                            RecordBossState("start", false);
                            Next("ObserveBoss");
                        }
                        else Next("CheckClear");
                        break;
                    case "ObserveCombat":
                        var active = run.Rooms[wantedRoom].GetComponentsInChildren<GameVoxelMonster>();
                        combatObservation.observedAttacks = Mathf.Max(0, active.Sum(m => m.Attacks) - combatObservation.attackCountAtStart);
                        combatObservation.observedFrames = Time.frameCount - combatObservation.frameCountAtStart;
                        if (combatObservation.gameSeconds < 5 || (combatObservation.observedAttacks == 0 && combatObservation.gameSeconds < 15)) return;
                        playReport.observedAttacks += combatObservation.observedAttacks;
                        combatObservation.status = combatObservation.observedAttacks > 0 ? "passed" : "failed";
                        combatObservation.completionReason = combatObservation.observedAttacks > 0 ? "natural_attack_after_minimum_game_time" : "no_attack_within_15_game_seconds";
                        RecordCombatState("finish", active, true);
                        Require(combatObservation.observedAttacks >= 1,
                            $"Combat room {combatObservation.roomId} produced no natural attack after {combatObservation.gameSeconds:F2} game seconds ({combatObservation.wallSeconds:F2} wall seconds). Check approach movement and line of sight.");
                        foreach (var enemy in active) enemy.Health.TakeDamage(enemy.Health.maxHealth + 1);
                        playReport.combatRoomsCleared++;
                        Next("CheckClear"); break;
                    case "ObserveBoss":
                        Require(observedBoss && observedBoss.Health && observedBoss.Health.IsAlive, "Drop Keeper vanished before attack observation completed.");
                        bool patternCompleted = playReport.bossObservation.observedAttacks > 0 &&
                            (observedBoss.Volleys >= 3 || observedBoss.BarsThrown > 0 || observedBoss.SummonWaves > 0);
                        if (playReport.bossObservation.gameSeconds < 15 && !patternCompleted) return;
                        playReport.bossObservation.status = patternCompleted ? "passed" : "failed";
                        playReport.bossObservation.completionReason = patternCompleted ? "natural_boss_pattern_emitted" : "no_boss_pattern_within_15_game_seconds";
                        playReport.observedBossAttacks = playReport.bossObservation.observedAttacks;
                        RecordBossState("finish", true);
                        Require(patternCompleted, "Drop Keeper did not emit a natural attack pattern within 15 game seconds.");
                        observedBoss.Health.TakeDamage(observedBoss.Health.maxHealth + 1);
                        playReport.bossRoomsCleared++;
                        Next("CheckClear"); break;
                    case "CheckClear":
                        if (run.LivingEnemyCount != 0 && now - stateStart < 2) return;
                        Require(run.LivingEnemyCount == 0, "Defeated enemies remain in room counter.");
                        Require(!run.Rooms[wantedRoom].entranceGate.activeSelf, "Entrance gate remained locked after clear.");
                        if (wantedRoom < 4)
                        {
                            Require(!run.Rooms[wantedRoom].exitGate.activeSelf, "Combat exit did not unlock.");
                            wantedRoom++; EnterRoom();
                        }
                        else
                        {
                            Require(run.ClearedRoomCount == 5, "Not all five rooms cleared.");
                            motor.ResetAt(run.Rooms[4].exit.position - run.Rooms[4].exit.forward * 2.5f + Vector3.up * .05f);
                            Next("Exit");
                        }
                        break;
                    case "Exit":
                        Require(run.ExitAvailable && run.TryUseExit(), "Cleared stage exit unavailable.");
                        Require(run.Phase == LiminalRunPhase.Victory, "Standalone Game stage did not end in Victory.");
                        playReport.completedRooms = run.ClearedRoomCount;
                        Require(playReport.combatRoomsCleared == 3 && playReport.voxelEnemiesSpawned >= 9, "Expected three populated Game combat rooms.");
                        Require(playReport.combatObservations.Count == 3 && playReport.combatObservations.All(o => o.status == "passed" && o.gameSeconds >= 5 && o.observedAttacks >= 1),
                            "Every combat room must complete at least five game seconds of observation and show at least one natural attack.");
                        Require(playReport.bossesSpawned == 1 && playReport.bossRoomsCleared == 1 && playReport.observedBossAttacks >= 1 &&
                            playReport.bossObservation != null && playReport.bossObservation.status == "passed", "Expected one naturally attacking Drop Keeper and a successful final boss clear.");
                        playReport.checks.Add("Actual CharacterController walk and dash, natural attacks in all three voxel encounters, one natural Drop Keeper pattern within 15 game seconds, gate locking/unlocking, all five room clears and Victory verified.");
                        FinishPlay(null); break;
                }
            }
            catch (Exception ex) { FinishPlay(ex.ToString()); }
        }
        static void RecordCombatState(string label, GameVoxelMonster[] monsters, bool capture)
        {
            var room = run.Rooms[wantedRoom];
            combatObservation.diagnostics.Add(label + " playerLocal=" + room.transform.InverseTransformPoint(motor.transform.position).ToString("F2") + " alive=" + playerHealth.IsAlive);
            foreach(var monster in monsters)
            {
                var controller = monster.GetComponent<CharacterController>();
                Vector3 origin = monster.transform.position + Vector3.up;
                Vector3 to = playerHealth.transform.position + Vector3.up * .9f - origin;
                var blockers = Physics.RaycastAll(origin, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.transform.IsChildOf(monster.transform) && !h.collider.GetComponentInParent<LiminalPlayerHealth>() && !h.collider.GetComponentInParent<LiminalPropMonster>())
                    .Select(h => h.collider.name).ToArray();
                combatObservation.diagnostics.Add(label + " " + monster.role + " local=" + room.transform.InverseTransformPoint(monster.transform.position).ToString("F2")
                    + " state=" + monster.State + " attacks=" + monster.Attacks + " enabled=" + monster.isActiveAndEnabled + " body=" + (controller && controller.enabled)
                    + " losBlockers=" + string.Join(",", blockers) + " dt=" + Time.deltaTime.ToString("F6") + " minMove=" + controller.minMoveDistance.ToString("F5")
                    + " " + monster.MovementDiagnostics);
                var theme = room.GetComponent<GameThemeRoom>();
                var route = theme.RouteDirection(monster.transform.position, playerHealth.transform.position);
                var nav = typeof(GameThemeRoom).GetField("navigation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(theme);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                int cell = (int)nav.GetType().GetMethod("CellAt",flags).Invoke(nav,new object[]{monster.transform.position});
                int near = (int)nav.GetType().GetMethod("NearestWalkable",flags).Invoke(nav,new object[]{cell});
                var point = near >= 0 ? (Vector3)nav.GetType().GetMethod("World",flags).Invoke(nav,new object[]{near}) : Vector3.zero;
                var sweepHits = Physics.SphereCastAll(monster.transform.position + Vector3.up*.9f, controller.radius*.85f, route, 1.1f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.transform.IsChildOf(monster.transform) && !h.collider.GetComponentInParent<LiminalPlayerHealth>()).Select(h=>h.collider.name).ToArray();
                combatObservation.diagnostics.Add(label + " route="+route.ToString("F3")+" cell="+cell+" near="+near+" nearLocal="+room.transform.InverseTransformPoint(point).ToString("F3")+" sweepHits="+string.Join(",",sweepHits));
            }
            if(!capture || !Camera.main) return;
            CapturePlayRoom(combatObservation, "Combat");
        }

        static void RecordBossState(string label, bool capture)
        {
            var observation = playReport.bossObservation;
            var room = run.Rooms[wantedRoom];
            observation.diagnostics.Add(label + " bossLocal=" + room.transform.InverseTransformPoint(observedBoss.transform.position).ToString("F2") +
                " playerLocal=" + room.transform.InverseTransformPoint(motor.transform.position).ToString("F2") + " state=" + observedBoss.State +
                " attacks=" + observedBoss.Attacks + " volleys=" + observedBoss.Volleys + " shots=" + observedBoss.Shots +
                " bars=" + observedBoss.BarsThrown + " summons=" + observedBoss.SummonWaves);
            if (capture && Camera.main) CapturePlayRoom(observation, "Boss");
        }

        static void CapturePlayRoom(CombatObservation observation, string suffix)
        {
            observation.capture = "Documentation/GameTheme/PlayValidation/" + observation.roomId + "_" + suffix + ".png";
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try { target.Create(); SaveImage(Camera.main, target, observation.capture); }
            finally { target.Release(); Object.DestroyImmediate(target); }
        }

        static void MoveWorld(Vector3 direction, bool dash)
        {
            Vector3 forward = Vector3.ProjectOnPlane(motor.viewCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(motor.viewCamera.transform.right, Vector3.up).normalized;
            motor.SetAutomationInput(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), motor.transform.position + (direction.sqrMagnitude > .01f ? direction : Vector3.forward) * 10, dash);
        }
        static void EnterRoom() { motor.ResetAt(run.Rooms[wantedRoom].playerSpawn.position + Vector3.up * .05f); Next("AwaitRoom"); }
        static void Next(string next) { state = next; stateStart = EditorApplication.timeSinceStartup; }
        static void OnLog(string message, string stack, LogType type)
        {
            if (SessionState.GetBool(ActiveKey, false) && playReport != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) playReport.errors.Add(message);
        }
        static void SavePlay() { WriteJson(PlayPath, playReport); SessionState.SetString(PlayKey, JsonUtility.ToJson(playReport)); }
        static void FinishPlay(string error)
        {
            SessionState.SetBool(ActiveKey, false);
            if (playReport == null) playReport = new PlayReport { utc = DateTime.UtcNow.ToString("O") };
            if (error != null) playReport.errors.Add(error);
            playReport.status = playReport.errors.Count == 0 ? "passed" : "failed";
            SavePlay();
            if (motor) motor.ReleaseAutomation();
            Time.timeScale = 1;
            EditorApplication.isPlaying = false;
            Debug.Log("GAME_THEME_PLAY_VALIDATION: " + playReport.status + " (" + PlayPath + ")");
        }
    }
}
