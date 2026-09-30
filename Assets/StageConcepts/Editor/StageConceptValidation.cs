using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>Read-only asset checks and isolated physics checks for the four concept dungeons.</summary>
    public static class StageConceptValidation
    {
        const string Root = "Assets/StageConcepts";
        const string ReportPath = "Documentation/StageConcepts/validation.json";
        const float Width = 26f, Length = 36.4f, Tolerance = .025f;
        // Match the shipped Astraia CharacterController, rather than the earlier Explorer capsule.
        const float Cell = .5f, Radius = .24f, Height = 1.65f, Step = .24f;
        const long RoomTriangleBudget = 200000, StageTriangleBudget = 750000;
        static readonly string[] Themes = { "Forest", "Digital", "Ruins", "Cave" };

        [Serializable] public sealed class Report
        {
            public string status, utc, unityVersion;
            public int expectedRooms = 20, roomCount, passedRooms, stageCount, passedStages;
            public int sceneCount, passedScenes, meshySlots, populatedMeshySlots, uniqueMeshes;
            public long totalPlacedTriangles, uniqueMeshTriangles;
            public long maximumRoomTriangles = RoomTriangleBudget, maximumStageTriangles = StageTriangleBudget;
            public float roomWidth = Width, roomLength = Length, stageLength = Length * 5;
            public float traversalSampleSpacing = Cell, capsuleRadius = Radius, capsuleHeight = Height, stepOffset = Step;
            public bool openScenesUnchanged;
            public List<RoomCheck> rooms = new List<RoomCheck>();
            public List<StageCheck> stages = new List<StageCheck>();
            public List<SceneCheck> scenes = new List<SceneCheck>();
            public List<MeshCheck> meshes = new List<MeshCheck>();
            public List<string> checks = new List<string>();
            public List<string> warnings = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class RoomCheck
        {
            public string roomId, path, status;
            public Vector3 boundsSize, entry, exit;
            public int meshRenderers, colliders, meshColliders, meshySlots, populatedMeshySlots;
            public long placedTriangles;
            public int sampledCells, walkableCells, reachedCells, reachableTargets;
            public List<TraversalFailure> traversalFailures = new List<TraversalFailure>();
            public List<string> warnings = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class TraversalFailure
        {
            public string target, reason;
            public Vector3 localPosition;
            public List<string> blockingObjects = new List<string>();
        }

        [Serializable] public sealed class StageCheck
        {
            public string theme, path, status;
            public int routeLength, distinctSeededRoutes;
            public long routeTriangles;
            public List<string> route = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class SceneCheck
        {
            public string theme, path, status;
            public int directors, cameras;
            public List<string> errors = new List<string>();
        }

        [Serializable] public sealed class MeshCheck
        {
            public string name, assetPath;
            public int vertices, placedInstances;
            public long triangles;
        }

        sealed class SceneSnapshot
        {
            public Scene scene;
            public string path;
            public bool dirty, loaded;
            public int[] roots;
        }

        [MenuItem("AC Roguelike/Stage Concepts/Validate All Concepts")]
        public static void ValidateMenu() => Debug.Log(Validate());

        public static string Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stage concept validation requires Edit Mode.");

            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var original = SnapshotScenes();
            Scene originalActive = SceneManager.GetActiveScene();
            var meshChecks = new Dictionary<Mesh, MeshCheck>();
            var roomChecks = new Dictionary<string, RoomCheck>();
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                foreach (string theme in Themes)
                {
                    for (int number = 1; number <= 5; number++)
                    {
                        string path = RoomPath(theme, number);
                        var check = new RoomCheck { roomId = theme + "_" + number.ToString("00"), path = path };
                        report.rooms.Add(check);
                        roomChecks[path] = check;
                        try { CheckRoom(path, preview, check, meshChecks); }
                        catch (Exception ex) { check.errors.Add(ex.ToString()); }
                        check.status = check.errors.Count == 0 ? "passed" : "failed";
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(path)) report.roomCount++;
                        if (check.status == "passed") report.passedRooms++;
                        report.meshySlots += check.meshySlots;
                        report.populatedMeshySlots += check.populatedMeshySlots;
                        report.totalPlacedTriangles += check.placedTriangles;
                        foreach (string issue in check.errors) report.errors.Add(check.roomId + ": " + issue);
                        foreach (string issue in check.warnings) report.warnings.Add(check.roomId + ": " + issue);
                    }
                    var stage = new StageCheck { theme = theme, path = Root + "/Stages/" + theme + ".asset" };
                    report.stages.Add(stage);
                    try { CheckStage(stage, preview, roomChecks); }
                    catch (Exception ex) { stage.errors.Add(ex.ToString()); }
                    stage.status = stage.errors.Count == 0 ? "passed" : "failed";
                    if (AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(stage.path)) report.stageCount++;
                    if (stage.status == "passed") report.passedStages++;
                    foreach (string issue in stage.errors) report.errors.Add(theme + " stage: " + issue);

                    var scene = new SceneCheck { theme = theme, path = Root + "/Scenes/StageConcept_" + theme + ".unity" };
                    report.scenes.Add(scene);
                    try { CheckScene(scene); }
                    catch (Exception ex) { scene.errors.Add(ex.ToString()); }
                    scene.status = scene.errors.Count == 0 ? "passed" : "failed";
                    if (File.Exists(scene.path)) report.sceneCount++;
                    if (scene.status == "passed") report.passedScenes++;
                    foreach (string issue in scene.errors) report.errors.Add(theme + " scene: " + issue);
                }
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (originalActive.IsValid() && SceneManager.GetActiveScene() != originalActive)
                    SceneManager.SetActiveScene(originalActive);
                Physics.SyncTransforms();
                CheckScenesUnchanged(original, originalActive, report);
            }

            report.meshes = meshChecks.Values.OrderByDescending(m => m.triangles).ThenBy(m => m.assetPath).ToList();
            report.uniqueMeshes = report.meshes.Count;
            report.uniqueMeshTriangles = report.meshes.Sum(m => m.triangles);
            if (report.roomCount != 20) report.errors.Add("Expected 20 concept room prefabs; found " + report.roomCount + ".");
            if (report.stageCount != 4) report.errors.Add("Expected 4 concept stage definitions; found " + report.stageCount + ".");
            if (report.sceneCount != 4) report.errors.Add("Expected 4 concept scenes; found " + report.sceneCount + ".");
            if (report.errors.Count == 0)
            {
                report.checks.Add("All 20 rooms have 26 x 36.4 m bounds, aligned sockets, gates, clear spawns and populated Meshy slots.");
                report.checks.Add("Every room passes floor-supported capsule traversal from player spawn to both doorways and all enemy spawns.");
                report.checks.Add("All four seeded five-room routes have unique middle rooms, aligned sockets and no overlapping room footprints.");
                report.checks.Add("All four saved scenes reference their own stage and have a player and following gameplay camera.");
                report.checks.Add("Triangle counts include every placed static and skinned mesh; unique mesh totals are also recorded.");
            }
            report.status = report.errors.Count == 0 ? "passed" : "failed";
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(ReportPath, json);
            return json;
        }

        static string RoomPath(string theme, int number) => Root + "/Prefabs/Rooms/" + theme + "_" + number.ToString("00") + ".prefab";

        static void CheckRoom(string path, Scene preview, RoomCheck check, Dictionary<Mesh, MeshCheck> meshes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) { check.errors.Add("Missing prefab."); return; }
            var source = prefab.GetComponent<LiminalRoom>();
            if (!source) { check.errors.Add("Missing LiminalRoom component."); return; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            try
            {
                instance.transform.SetPositionAndRotation(new Vector3(10000, 0, 10000), Quaternion.identity);
                var room = instance.GetComponent<LiminalRoom>();
                check.roomId = room.roomId;
                check.boundsSize = room.localBounds.size;
                Check(instance.activeInHierarchy, "Room root must be active.");
                Check(Near(instance.transform.localScale, Vector3.one), "Room root must have unit scale.");
                Check(!string.IsNullOrWhiteSpace(room.roomId) && !string.IsNullOrWhiteSpace(room.displayName), "Room ID and display name are required.");
                Check(Mathf.Abs(check.boundsSize.x - Width) < Tolerance && Mathf.Abs(check.boundsSize.z - Length) < Tolerance,
                    "Room footprint must be 26 x 36.4 m.");
                Check(Mathf.Abs(room.localBounds.center.x) < Tolerance && Mathf.Abs(room.localBounds.min.z) < Tolerance,
                    "Room bounds must be centered on x = 0 and start at z = 0.");
                foreach (string group in new[] { "Architecture", "Props", "Lighting", "Gameplay", "Sockets" })
                    Check(room.transform.Find(group), "Missing hierarchy group: " + group + ".");
                Check(room.entry && room.exit && room.playerSpawn, "Entry, exit and player spawn references are required.");
                if (room.entry && room.exit)
                {
                    check.entry = room.transform.InverseTransformPoint(room.entry.position);
                    check.exit = room.transform.InverseTransformPoint(room.exit.position);
                    Check(Near(check.entry, Vector3.zero), "Entry must be at local origin.");
                    Check(Near(check.exit, new Vector3(0, 0, Length)), "Exit must be at (0, 0, 36.4).");
                    Check(Vector3.Angle(room.entry.forward, room.transform.forward) < .1f &&
                          Vector3.Angle(room.exit.forward, room.transform.forward) < .1f, "Both socket forward axes must point along +Z.");
                }
                if (room.playerSpawn) Check(room.Contains(room.playerSpawn.position, .4f), "Player spawn is outside the room.");
                if (room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss)
                    Check(room.enemySpawns != null && room.enemySpawns.Length > 0, "Combat room needs enemy spawn markers.");
                if (room.enemySpawns != null)
                    foreach (var spawn in room.enemySpawns) Check(spawn && room.Contains(spawn.position, .4f), "Enemy spawn is missing or outside the room.");
                CheckGate(room.entranceGate, true);
                CheckGate(room.exitGate, false);
                room.SetGates(false, false);

                var colliders = instance.GetComponentsInChildren<Collider>(true);
                check.colliders = colliders.Length;
                check.meshColliders = colliders.OfType<MeshCollider>().Count();
                Check(colliders.Any(c => c.enabled && !c.isTrigger), "Room has no enabled solid colliders.");
                foreach (var collider in colliders.OfType<MeshCollider>()) Check(collider.sharedMesh, collider.name + ": MeshCollider has no mesh.");
                if (check.meshColliders > 20) check.warnings.Add("More than 20 MeshColliders; simple primitive colliders are preferred.");
                check.meshRenderers = instance.GetComponentsInChildren<Renderer>(true).Length;
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!renderer) continue;
                    Check(filter.sharedMesh, RelativePath(filter.transform, room.transform) + ": MeshFilter has no mesh.");
                    Count(filter.sharedMesh, renderer.enabled && renderer.gameObject.activeInHierarchy);
                    CheckMaterials(renderer);
                }
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Check(renderer.sharedMesh, renderer.name + ": SkinnedMeshRenderer has no mesh.");
                    Count(renderer.sharedMesh, renderer.enabled && renderer.gameObject.activeInHierarchy);
                    CheckMaterials(renderer);
                }
                Check(check.placedTriangles <= RoomTriangleBudget, "Room exceeds " + RoomTriangleBudget + " placed triangles: " + check.placedTriangles + ".");

                var slots = instance.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MeshySlot__", StringComparison.Ordinal)).ToArray();
                check.meshySlots = slots.Length;
                Check(slots.Length > 0, "Room has no MeshySlot__ anchors.");
                foreach (var slot in slots)
                {
                    bool namedModel = slot.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Meshy__", StringComparison.Ordinal));
                    bool populated = namedModel && (slot.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh && f.sharedMesh.vertexCount > 0 &&
                        f.gameObject.activeInHierarchy && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled &&
                        IsMeshyMesh(f.sharedMesh)) ||
                        slot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh && r.sharedMesh.vertexCount > 0 &&
                        r.enabled && r.gameObject.activeInHierarchy && IsMeshyMesh(r.sharedMesh)));
                    if (populated) check.populatedMeshySlots++;
                    Check(populated, slot.name + ": missing active Meshy__ model imported from Assets/StageConcepts/Art/Meshy.");
                }

                Physics.SyncTransforms();
                if (room.playerSpawn && room.entry && room.exit)
                    CheckTraversal(room, preview.GetPhysicsScene(), check);

                void Check(bool condition, string message) { if (!condition) check.errors.Add(message); }
                void CheckGate(GameObject gate, bool entrance)
                {
                    string label = entrance ? "Entrance" : "Exit";
                    Check(gate, label + " gate reference is missing.");
                    if (!gate) return;
                    Check(gate.transform.IsChildOf(room.transform), label + " gate must belong to this room.");
                    var gateColliders = gate.GetComponentsInChildren<Collider>(true);
                    Check(gateColliders.Any(c => c.enabled && !c.isTrigger), label + " gate needs a solid collider.");
                    Vector3 p = room.transform.InverseTransformPoint(gate.transform.position);
                    Check(Mathf.Abs(p.x) < Tolerance && (entrance ? p.z >= -Tolerance && p.z <= 1.6f + Tolerance : p.z >= Length - 1.6f - Tolerance && p.z <= Length + Tolerance),
                        label + " gate is not aligned with the doorway.");
                }
                void Count(Mesh mesh, bool active)
                {
                    if (!mesh) return;
                    if (!meshes.TryGetValue(mesh, out var summary))
                    {
                        summary = new MeshCheck { name = mesh.name, assetPath = AssetDatabase.GetAssetPath(mesh), vertices = mesh.vertexCount, triangles = Triangles(mesh) };
                        meshes.Add(mesh, summary);
                    }
                    if (active) { check.placedTriangles += summary.triangles; summary.placedInstances++; }
                    if (summary.triangles > 50000) Check(false, mesh.name + ": a single mesh exceeds 50,000 triangles (" + summary.triangles + ").");
                }
                void CheckMaterials(Renderer renderer)
                {
                    Check(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m && m.shader && m.shader.name != "Hidden/InternalErrorShader"),
                        RelativePath(renderer.transform, room.transform) + ": missing material or error shader.");
                }
            }
            finally { Object.DestroyImmediate(instance); Physics.SyncTransforms(); }
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
            Target("Player spawn", room.playerSpawn.position);
            Target("Entry approach", room.entry.position + room.entry.forward * .8f);
            Target("Exit approach", room.exit.position - room.exit.forward * .8f);
            if (room.enemySpawns != null)
                foreach (var spawn in room.enemySpawns) if (spawn) Target(spawn.name, spawn.position);

            List<string> CapsuleObstacles(Vector3 world, out float floor)
            {
                var result = new List<string>();
                floor = float.NegativeInfinity;
                int hitCount = physics.Raycast(world + Vector3.up * (Step + .05f), Vector3.down, hits, Step + .2f, ~0, QueryTriggerInteraction.Ignore);
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

        static void CheckStage(StageCheck check, Scene preview, Dictionary<string, RoomCheck> roomChecks)
        {
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(check.path);
            if (!stage) { check.errors.Add("Missing stage definition."); return; }
            Check(stage.startRoom && AssetDatabase.GetAssetPath(stage.startRoom) == RoomPath(check.theme, 1), "Start room must reference the theme's 01 prefab.");
            Check(stage.endRoom && AssetDatabase.GetAssetPath(stage.endRoom) == RoomPath(check.theme, 5), "End room must reference the theme's 05 prefab.");
            Check(stage.middleRoomCount == 3 && stage.roomPool != null && stage.roomPool.Length == 3, "Stage requires exactly three middle room candidates and selections.");
            if (stage.roomPool != null)
            {
                var actual = stage.roomPool.Where(r => r).Select(AssetDatabase.GetAssetPath).OrderBy(p => p).ToArray();
                var expected = Enumerable.Range(2, 3).Select(n => RoomPath(check.theme, n)).OrderBy(p => p).ToArray();
                Check(actual.SequenceEqual(expected), "Middle room pool must reference this theme's 02, 03 and 04 prefabs exactly once.");
            }
            var routes = new HashSet<string>();
            for (int seed = 1; seed <= 32; seed++)
            {
                var route = stage.ChooseRoute(seed, 0);
                string signature = string.Join("|", route.Select(r => r.roomId));
                routes.Add(signature);
                Check(signature == string.Join("|", stage.ChooseRoute(seed, 0).Select(r => r.roomId)), "Route is not deterministic for seed " + seed + ".");
                Check(route.Length == 5 && route.Distinct().Count() == 5, "Seed " + seed + " does not select five unique rooms.");
            }
            check.distinctSeededRoutes = routes.Count;
            Check(routes.Count > 1, "Multiple seeds should produce distinct middle-room orders.");
            var chosen = stage.ChooseRoute(73029, 0);
            check.routeLength = chosen.Length;
            check.route = chosen.Select(r => r.roomId).ToList();
            foreach (var room in chosen)
                if (roomChecks.TryGetValue(AssetDatabase.GetAssetPath(room), out var roomCheck)) check.routeTriangles += roomCheck.placedTriangles;
            Check(check.routeTriangles <= StageTriangleBudget, "Stage exceeds " + StageTriangleBudget + " placed triangles: " + check.routeTriangles + ".");

            var host = new GameObject("__StageConceptRouteValidation");
            SceneManager.MoveGameObjectToScene(host, preview);
            try
            {
                host.transform.position = new Vector3(10000, 0, 10000);
                var director = host.AddComponent<LiminalRunDirector>();
                director.stages = new[] { stage };
                director.seed = 73029;
                director.GeneratePreview(0);
                Check(director.Rooms.Count == 5, "Preview assembly did not produce five rooms.");
                for (int i = 1; i < director.Rooms.Count; i++)
                {
                    var previous = director.Rooms[i - 1];
                    var next = director.Rooms[i];
                    Check(Vector3.Distance(previous.exit.position, next.entry.position) < .01f, "Room " + i + " has an entry/exit gap.");
                    Check(Quaternion.Angle(previous.exit.rotation, next.entry.rotation) < .1f, "Room " + i + " has a socket rotation mismatch.");
                    for (int j = 0; j < i; j++)
                    {
                        Bounds a = WorldBounds(next), b = WorldBounds(director.Rooms[j]);
                        float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                        float overlapZ = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                        Check(overlapX < .05f || overlapZ < .05f, "Room " + i + " overlaps room " + j + ".");
                    }
                }
            }
            finally { Object.DestroyImmediate(host); }
            void Check(bool condition, string message) { if (!condition) check.errors.Add(message); }
        }

        static void CheckScene(SceneCheck check)
        {
            if (!File.Exists(check.path)) { check.errors.Add("Missing scene asset."); return; }
            Scene preview = EditorSceneManager.OpenPreviewScene(check.path);
            try
            {
                var roots = preview.GetRootGameObjects();
                var directors = roots.SelectMany(r => r.GetComponentsInChildren<LiminalRunDirector>(true)).ToArray();
                check.directors = directors.Length;
                if (directors.Length != 1) { check.errors.Add("Scene must contain exactly one LiminalRunDirector."); return; }
                var director = directors[0];
                var expected = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root + "/Stages/" + check.theme + ".asset");
                Check(director.stages != null && director.stages.Length == 1 && director.stages[0] == expected && expected,
                    "Scene must run only its matching theme stage.");
                Check(director.player && director.player.GetComponent<PlayerMotor>() && director.player.GetComponent<TalismanCaster>(),
                    "Scene needs a referenced player with PlayerMotor and TalismanCaster.");
                if (director.player)
                {
                    Check(director.player.gameObject.scene == preview, "Player reference belongs to another scene.");
                    var controller = director.player.GetComponent<CharacterController>();
                    Check(controller && controller.enabled, "Player needs an enabled CharacterController.");
                    if (controller) Check(controller.radius <= Radius + .01f && controller.height <= Height + .01f,
                        "Player capsule exceeds the traversal validation dimensions.");
                }
                var cameras = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
                check.cameras = cameras.Length;
                Check(cameras.Any(c => c.CompareTag("MainCamera") && c.GetComponent<IsometricFollowCamera>() &&
                    c.GetComponent<IsometricFollowCamera>().target == director.player), "Missing active MainCamera following this player.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            void Check(bool condition, string message) { if (!condition) check.errors.Add(message); }
        }

        static long Triangles(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += (long)mesh.GetIndexCount(i) / 3;
                else if (mesh.GetTopology(i) == MeshTopology.Quads) count += (long)mesh.GetIndexCount(i) / 4 * 2;
            }
            return count;
        }

        static bool IsMeshyMesh(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh).Replace('\\', '/');
            return path.StartsWith(Root + "/Art/Meshy/", StringComparison.Ordinal) &&
                   path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= Tolerance;
        static Bounds WorldBounds(LiminalRoom room)
        {
            var bounds = new Bounds(room.transform.TransformPoint(room.localBounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        bounds.Encapsulate(room.transform.TransformPoint(room.localBounds.center + Vector3.Scale(room.localBounds.extents, new Vector3(x, y, z))));
            return bounds;
        }

        static string RelativePath(Transform current, Transform root)
        {
            string path = current.name;
            while (current.parent && current.parent != root) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }

        static List<SceneSnapshot> SnapshotScenes()
        {
            var result = new List<SceneSnapshot>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                result.Add(new SceneSnapshot
                {
                    scene = scene, path = scene.path, dirty = scene.isDirty, loaded = scene.isLoaded,
                    roots = scene.isLoaded ? scene.GetRootGameObjects().Select(r => r.GetInstanceID()).ToArray() : Array.Empty<int>()
                });
            }
            return result;
        }

        static void CheckScenesUnchanged(List<SceneSnapshot> original, Scene active, Report report)
        {
            bool same = SceneManager.sceneCount == original.Count && SceneManager.GetActiveScene() == active;
            foreach (var item in original)
            {
                bool valid = item.scene.IsValid() && item.scene.path == item.path && item.scene.isDirty == item.dirty && item.scene.isLoaded == item.loaded;
                if (valid && item.loaded) valid = item.roots.SequenceEqual(item.scene.GetRootGameObjects().Select(r => r.GetInstanceID()));
                if (!valid) { same = false; report.errors.Add("Open scene state changed during validation: " + item.path); }
            }
            report.openScenesUnchanged = same;
            if (!same && !report.errors.Any(e => e.StartsWith("Open scene state changed", StringComparison.Ordinal)))
                report.errors.Add("Open scene count or active scene changed during validation.");
        }
    }
}
