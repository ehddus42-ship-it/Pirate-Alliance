using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Liminal.EditorTests
{
    /// <summary>Explicitly invoked integration checks. Uses normal player, enemy, gate and reward APIs.</summary>
    [InitializeOnLoad]
    public static class LiminalPlayValidation
    {
        const string ActiveKey = "Liminal.Validation.Active";
        const string ReportKey = "Liminal.Validation.Report";
        const string ScenePath = "Assets/Liminal/Scenes/LiminalRun.unity";
        const string ReportPath = "Documentation/liminal-validation.json";
        [Serializable] public sealed class Report
        {
            public string status = "running";
            public string utc;
            public string unityVersion;
            public int stagesCompleted;
            public int combatRoomsCleared;
            public bool bossTelegraphObserved;
            public bool bossDamageObserved;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }

        static Report report;
        static LiminalRunDirector run;
        static string state = "AwaitRun";
        static double stateStarted;
        static double nextTick;
        static int wantedRoom, originalSeed, healthBeforeBoss;
        static string initialRoute;
        static VendingMonster arrivalAmbush;

        static LiminalPlayValidation()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += mode =>
            {
                if (mode == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ActiveKey, false))
                    Finish(false, "Play Mode ended before the validation completed.");
            };
            if (SessionState.GetBool(ActiveKey, false))
            {
                report = JsonUtility.FromJson<Report>(SessionState.GetString(ReportKey, "{}"));
                stateStarted = EditorApplication.timeSinceStartup;
            }
        }

        public static string Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before starting validation.");
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Build LiminalRun first.", ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            report = NewReport();
            try { CheckStructure(report); }
            catch (Exception ex)
            {
                report.status = "failed";
                report.errors.Add(ex.ToString());
                SaveReport();
                return JsonUtility.ToJson(report, true);
            }
            state = "AwaitRun";
            stateStarted = EditorApplication.timeSinceStartup;
            SessionState.SetString(ReportKey, JsonUtility.ToJson(report));
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.isPlaying = true;
            return "Validation started; report will be written to " + ReportPath;
        }

        public static string ValidateStructure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Structural checks require Edit Mode.");
            report = NewReport();
            try { CheckStructure(report); report.status = "structural_checks_passed"; }
            catch (Exception ex) { report.status = "failed"; report.errors.Add(ex.ToString()); }
            SaveReport();
            return JsonUtility.ToJson(report, true);
        }

        static Report NewReport() => new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };

        static void CheckStructure(Report result)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<LiminalRunDirector>();
            Require(source && source.stages != null && source.stages.Length == 4, "Four stages must be connected in the run scene.");
            Require(source.stages[3].isBossStage, "The final stage must be the boss stage.");
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject host = null;
            try
            {
                host = new GameObject("LiminalValidationPreview");
                SceneManager.MoveGameObjectToScene(host, previewScene);
                var preview = host.AddComponent<LiminalRunDirector>();
                preview.stages = source.stages;
                preview.seed = source.seed;
                for (int s = 0; s < source.stages.Length; s++)
                {
                    var definition = source.stages[s];
                    Require(definition, $"Stage {s + 1} is null.");
                    string route = RouteIds(definition.ChooseRoute(73029, s));
                    Require(route == RouteIds(definition.ChooseRoute(73029, s)), $"Stage {s + 1} is not deterministic.");
                    var distinctRoutes = new HashSet<string>();
                    for (int seed = 1; seed <= 32; seed++) distinctRoutes.Add(RouteIds(definition.ChooseRoute(seed, s)));
                    if (definition.roomPool.Length > 1 && definition.middleRoomCount > 0)
                        Require(distinctRoutes.Count > 1, $"Stage {s + 1} ignores the seed.");
                    var chosen = definition.ChooseRoute(73029, s);
                    if (definition.middleRoomCount <= definition.roomPool.Distinct().Count())
                        Require(chosen.Skip(1).Take(definition.middleRoomCount).Distinct().Count() == definition.middleRoomCount,
                            $"Stage {s + 1} repeats a room before its pool is exhausted.");
                    preview.GeneratePreview(s);
                    Physics.SyncTransforms();
                    for (int i = 0; i < preview.Rooms.Count; i++)
                    {
                        var room = preview.Rooms[i];
                        Require(room.entry && room.exit && room.playerSpawn, room.name + " is missing a socket/spawn.");
                        Require(room.Contains(room.playerSpawn.position, .4f), room.name + " player spawn is outside the room.");
                        if (i > 0)
                        {
                            var preceding = preview.Rooms[i - 1];
                            Require(Vector3.Distance(preceding.exit.position, room.entry.position) < .001f, room.name + " has a socket gap.");
                            Require(Quaternion.Angle(preceding.exit.rotation, room.entry.rotation) < .01f, room.name + " has a socket rotation mismatch.");
                        }
                        for (int j = 0; j < i; j++)
                        {
                            Bounds a = WorldBounds(room), b = WorldBounds(preview.Rooms[j]);
                            float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                            float overlapZ = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                            Require(overlapX < .05f || overlapZ < .05f, room.name + " overlaps room " + j);
                        }
                        var points = new List<Transform> { room.playerSpawn };
                        if (room.enemySpawns != null) points.AddRange(room.enemySpawns.Where(t => t));
                        if (room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss)
                            Require(points.Count > 1, room.name + " has no enemy spawn markers.");
                        foreach (var point in points)
                        {
                            Require(room.Contains(point.position, .4f), room.name + "/" + point.name + " is outside the room.");
                            // Geometry is checked in each collider's own space so prefab rotation and scale are respected.
                            foreach (var box in room.GetComponentsInChildren<BoxCollider>())
                            {
                                if (!box.enabled || !box.gameObject.activeInHierarchy || box.isTrigger) continue;
                                Vector3 p = box.transform.InverseTransformPoint(point.position + Vector3.up);
                                var interior = new Bounds(box.center, box.size);
                                Require(!interior.Contains(p), room.name + "/" + point.name + " is inside " + box.name);
                            }
                        }
                    }
                    result.checks.Add($"Stage {s + 1}: deterministic route, {distinctRoutes.Count} seeded routes, unique shuffle bag, sockets aligned, rooms do not overlap, spawn markers clear.");
                }
            }
            finally
            {
                if (host) UnityEngine.Object.DestroyImmediate(host);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Serializable] public sealed class TraversalReport
        {
            public string status, utc;
            public int roomCount, passedRooms;
            public float sampleSpacing = .5f, playerRadius = .24f, playerHeight = 1.6f, stepOffset = .22f;
            public List<string> checks = new List<string>();
            public List<TraversalFailure> failures = new List<TraversalFailure>();
            public List<string> errors = new List<string>();
        }
        [Serializable] public sealed class TraversalFailure
        {
            public string roomId, target, reason;
            public Vector3 targetLocalPosition;
            public List<BlockedCell> cells = new List<BlockedCell>();
        }
        [Serializable] public sealed class BlockedCell
        {
            public Vector3 roomLocalPosition;
            public string[] blockingObjects;
        }

        /// <summary>Run once after Meshy placement. Actual Physics queries test every authored prefab independently.</summary>
        public static string ValidateTraversal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Traversal validation requires Edit Mode.");
            var result = new TraversalReport { status = "running", utc = DateTime.UtcNow.ToString("O") };
            GameObject host = null;
            try
            {
                // A distant location isolates queries from the open Run/Gallery scene while retaining the real physics world.
                host = new GameObject("__LiminalTraversalValidation");
                host.transform.position = new Vector3(10000, 0, 10000);
                var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Liminal/Prefabs/Rooms" })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
                foreach (string path in paths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!prefab || !prefab.GetComponent<LiminalRoom>()) continue;
                    var room = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, host.transform)).GetComponent<LiminalRoom>();
                    try
                    {
                        room.transform.localPosition = Vector3.zero;
                        room.transform.localRotation = Quaternion.identity;
                        room.SetGates(false, false);
                        Physics.SyncTransforms();
                        int failuresBefore = result.failures.Count;
                        CheckPhysicsTraversal(room, result);
                        result.roomCount++;
                        if (result.failures.Count == failuresBefore)
                        {
                            result.passedRooms++;
                            result.checks.Add(room.roomId + ": player spawn reaches entry, exit and every enemy spawn with gates open.");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(room.gameObject); }
                }
                Require(result.roomCount >= 20, "Expected at least 20 authored room variations.");
                result.status = result.failures.Count == 0 ? "passed" : "failed";
            }
            catch (Exception ex) { result.status = "failed"; result.errors.Add(ex.ToString()); }
            finally { if (host) UnityEngine.Object.DestroyImmediate(host); Physics.SyncTransforms(); }
            Directory.CreateDirectory("Documentation");
            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText("Documentation/liminal-traversal-validation.json", json);
            return json;
        }

        static void CheckPhysicsTraversal(LiminalRoom room, TraversalReport report)
        {
            const float cell = .5f, radius = .24f, height = 1.6f, stepOffset = .22f;
            Vector3 min = room.localBounds.min;
            int width = Mathf.CeilToInt(room.localBounds.size.x / cell), depth = Mathf.CeilToInt(room.localBounds.size.z / cell);
            int count = width * depth;
            var blocked = new bool[count];
            var reasons = new string[count][];
            var seen = new bool[count];
            var capsuleHits = new Collider[64];
            var floorHits = new RaycastHit[64];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    int node = z * width + x;
                    Vector3 world = World(node);
                    var objects = new HashSet<string>();
                    float floorHeight = float.NegativeInfinity;
                    int found = Physics.RaycastNonAlloc(world + Vector3.up * (stepOffset + .03f), Vector3.down,
                        floorHits, stepOffset + .15f, ~0, QueryTriggerInteraction.Ignore);
                    Require(found < floorHits.Length, room.name + " floor query overflowed.");
                    for (int i = 0; i < found; i++)
                        if (floorHits[i].collider.transform.IsChildOf(room.transform) && floorHits[i].normal.y > .7f)
                            floorHeight = Mathf.Max(floorHeight, floorHits[i].point.y);
                    if (float.IsNegativeInfinity(floorHeight)) objects.Add("NO_WALKABLE_FLOOR");
                    else
                    {
                        // The bottom .22 m may step over low coping, matching the existing CharacterController.
                        Vector3 lower = new Vector3(world.x, floorHeight + stepOffset + radius + .002f, world.z);
                        Vector3 upper = new Vector3(world.x, floorHeight + height - radius, world.z);
                        found = Physics.OverlapCapsuleNonAlloc(lower, upper, radius, capsuleHits, ~0, QueryTriggerInteraction.Ignore);
                        Require(found < capsuleHits.Length, room.name + " capsule query overflowed.");
                        for (int i = 0; i < found; i++)
                        {
                            var hit = capsuleHits[i];
                            if (hit.transform.IsChildOf(room.transform)) objects.Add(RelativePath(hit.transform, room.transform));
                        }
                    }
                    blocked[node] = objects.Count > 0;
                    reasons[node] = objects.ToArray();
                }
            int start = Node(room.playerSpawn.position);
            var queue = new Queue<int>();
            if (!blocked[start]) { seen[start] = true; queue.Enqueue(start); }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in Neighbours(current))
                    if (!seen[next] && !blocked[next]) { seen[next] = true; queue.Enqueue(next); }
            }
            Check("Player Spawn", room.playerSpawn.position);
            Check("Entry approach", room.entry.position + room.entry.forward * .8f);
            Check("Exit approach", room.exit.position - room.exit.forward * .8f);
            if (room.enemySpawns != null)
                foreach (var marker in room.enemySpawns) if (marker) Check(marker.name, marker.position);

            void Check(string label, Vector3 position)
            {
                int target = Node(position);
                if (seen[target]) return;
                var failure = new TraversalFailure
                {
                    roomId = room.roomId, target = label, targetLocalPosition = room.transform.InverseTransformPoint(position),
                    reason = blocked[target] ? "Target capsule overlaps geometry or lacks floor." : "No continuous route from player spawn."
                };
                var candidates = new List<int>();
                if (blocked[target]) candidates.Add(target);
                if (blocked[start] && start != target) candidates.Add(start);
                candidates.AddRange(Enumerable.Range(0, count).Where(n => blocked[n] && Neighbours(n).Any(t => seen[t]))
                    .OrderBy(n => (World(n) - position).sqrMagnitude).Take(20));
                foreach (int n in candidates.Distinct()) failure.cells.Add(new BlockedCell
                { roomLocalPosition = room.transform.InverseTransformPoint(World(n)), blockingObjects = reasons[n] });
                report.failures.Add(failure);
            }
            Vector3 World(int node) => room.transform.TransformPoint(new Vector3(min.x + (node % width + .5f) * cell, 0, min.z + (node / width + .5f) * cell));
            int Node(Vector3 world)
            {
                Vector3 p = room.transform.InverseTransformPoint(world);
                return Mathf.Clamp(Mathf.FloorToInt((p.z - min.z) / cell), 0, depth - 1) * width
                    + Mathf.Clamp(Mathf.FloorToInt((p.x - min.x) / cell), 0, width - 1);
            }
            IEnumerable<int> Neighbours(int node)
            {
                int x = node % width, z = node / width;
                if (x > 0) yield return node - 1;
                if (x < width - 1) yield return node + 1;
                if (z > 0) yield return node - width;
                if (z < depth - 1) yield return node + width;
            }
        }

        static string RelativePath(Transform current, Transform root)
        {
            string path = current.name;
            while (current.parent && current.parent != root) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }

        static Bounds WorldBounds(LiminalRoom room)
        {
            var b = new Bounds(room.transform.TransformPoint(room.localBounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        b.Encapsulate(room.transform.TransformPoint(room.localBounds.center + Vector3.Scale(room.localBounds.extents, new Vector3(x, y, z))));
            return b;
        }

        static string RouteIds(IEnumerable<LiminalRoom> rooms) => string.Join("|", rooms.Select(r => r.roomId));

        static void Update()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + .12;
            try
            {
                Require(EditorApplication.timeSinceStartup - stateStarted < 45, "Validation timed out in " + state);
                if (!run) run = UnityEngine.Object.FindFirstObjectByType<LiminalRunDirector>();
                if (!run) return;
                Require(run.Phase != LiminalRunPhase.InvalidConfiguration, "Runtime configuration failed. Inspect the Console.");
                var motor = run.player ? run.player.GetComponent<PlayerMotor>() : null;
                if (motor) motor.SetAutomationInput(Vector2.zero, motor.transform.position + Vector3.forward * 3);
                var caster = run.player ? run.player.GetComponent<TalismanCaster>() : null;
                // Do not inject mouse events; the test exercises damage via the same public health API used by projectiles.
                switch (state)
                {
                    case "AwaitRun":
                        if (run.Rooms.Count == 0 || run.ActiveRoomIndex != 0) return;
                        originalSeed = run.seed;
                        initialRoute = RouteIds(run.Rooms);
                        Require(!run.TryUseExit(), "The stage exited before the route was cleared.");
                        report.checks.Add("Premature stage exit rejected.");
                        arrivalAmbush = run.Rooms[0].GetComponentInChildren<VendingMonster>();
                        Require(arrivalAmbush && arrivalAmbush.enabled, "Stage 1 arrival has no enabled authored vending ambush.");
                        Require(arrivalAmbush.State == VendingMonsterState.Dormant && !arrivalAmbush.Health.CanBeTargeted,
                            "Authored appliance was not dormant at the player spawn.");
                        Require(run.Rooms.Skip(1).SelectMany(r => r.GetComponentsInChildren<VendingMonster>()).All(m => !m.enabled),
                            "A future room's ambush was activated early.");
                        Require(!run.Rooms.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Any(t => t.name == "MeshySlot__vending_machine"),
                            "An old decoration was left alongside the monster.");
                        motor.ResetAt(arrivalAmbush.transform.position + arrivalAmbush.transform.forward * 4 + Vector3.up * .05f);
                        Next("AwaitArrivalAmbush");
                        break;
                    case "AwaitArrivalAmbush":
                        if (arrivalAmbush.State == VendingMonsterState.Dormant) return;
                        Require(arrivalAmbush.State == VendingMonsterState.Awakening, "Arrival ambush skipped its emergence.");
                        report.checks.Add("Stage 1 authored appliance is dormant at spawn and awakens on approach at its original placement.");
                        Next("ObserveArrivalAmbush");
                        break;
                    case "ObserveArrivalAmbush":
                        if (arrivalAmbush.State == VendingMonsterState.Awakening) return;
                        Require(arrivalAmbush.Health.CanBeTargeted && arrivalAmbush.limbRenderers.All(r => r.enabled),
                            "Arrival ambush did not finish unfolding into a targetable monster.");
                        arrivalAmbush.Health.TakeDamage(arrivalAmbush.Health.maxHealth + 1);
                        Require(run.LivingEnemyCount == 0, "Optional arrival ambush changed the room-clear counter.");
                        report.checks.Add("Unvisited rooms stay dormant; existing decorations are removed; arrival ambush death preserves route progress.");
                        wantedRoom = 1;
                        EnterWantedRoom(motor);
                        break;
                    case "AwaitEntry":
                        if (run.ActiveRoomIndex != wantedRoom) return;
                        var room = run.Rooms[wantedRoom];
                        bool combat = room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss;
                        var placedAmbushes = room.GetComponentsInChildren<VendingMonster>();
                        Require(placedAmbushes.All(m => m.enabled && m.Target == run.PlayerHealth), "Authored ambushes were not initialized on room entry.");
                        Require(placedAmbushes.Length == run.CurrentStage.ChooseRoute(run.seed, run.StageIndex)[wantedRoom].GetComponentsInChildren<VendingMonster>().Length,
                            "Runtime duplicated an authored vending monster.");
                        if (combat)
                        {
                            int roomEnemies = room.GetComponentsInChildren<TrainingEnemy>().Count(e => e.IsAlive);
                            Require(run.LivingEnemyCount == roomEnemies,
                                "Room-clear count does not include every monster in the room (vending ambushes, office monsters, monitors) exactly once.");
                            Require(room.kind == LiminalRoomKind.Boss || room.GetComponentsInChildren<LiminalPropMonster>().Length >= Mathf.Max(1, room.enemySpawns.Length),
                                "A combat room did not spawn its office monsters.");
                            Require(!room.GetComponentsInChildren<Transform>().Any(t => t.name == "MeshySlot__photocopier" || t.name == "MeshySlot__lockers"),
                                "A copier or locker prop was left next to its monster.");
                            Require(run.LivingEnemyCount > 0, room.name + " spawned no enemies.");
                            Require(room.entranceGate && room.entranceGate.activeSelf && room.exitGate && room.exitGate.activeSelf, room.name + " did not lock its gates.");
                            Require(!run.TryUseExit(), "An uncleared combat room permitted a stage exit.");
                            if (room.kind == LiminalRoomKind.Boss)
                            {
                                var boss = room.GetComponentsInChildren<TrafficLightBoss>().First();
                                healthBeforeBoss = run.PlayerHealth.Health;
                                motor.ResetAt(boss.transform.position + room.transform.right * 5);
                                Next("ObserveBoss");
                            }
                            else Next("ClearCombat");
                        }
                        else Next("CheckClear");
                        break;
                    case "ObserveBoss":
                        var activeBoss = run.Rooms[wantedRoom].GetComponentsInChildren<TrafficLightBoss>().First();
                        report.bossTelegraphObserved |= activeBoss.State == TrafficLightBossState.FieldCast || activeBoss.State == TrafficLightBossState.CarThrow
                            || (activeBoss.warning && activeBoss.warning.enabled);
                        if (run.PlayerHealth.Health < healthBeforeBoss)
                        {
                            report.bossDamageObserved = true;
                            Require(report.bossTelegraphObserved, "Boss damage occurred without an observed wind-up.");
                            report.checks.Add("Boss displayed its floor telegraph, then damaged a stationary player.");
                            Next("ClearCombat");
                        }
                        break;
                    case "ClearCombat":
                        foreach (var enemy in run.Rooms[wantedRoom].GetComponentsInChildren<TrainingEnemy>())
                            if (enemy.IsAlive) enemy.TakeDamage(enemy.maxHealth + 1);
                        report.combatRoomsCleared++;
                        Next("CheckClear");
                        break;
                    case "CheckClear":
                        Require(run.LivingEnemyCount == 0, "Dead enemies still block the route.");
                        var clearedRoom = run.Rooms[wantedRoom];
                        Require(!clearedRoom.entranceGate || !clearedRoom.entranceGate.activeSelf, "Entrance stayed locked after clear.");
                        if (wantedRoom < run.Rooms.Count - 1)
                        {
                            Require(!clearedRoom.exitGate || !clearedRoom.exitGate.activeSelf, "Exit stayed locked after clear.");
                            wantedRoom++;
                            EnterWantedRoom(motor);
                        }
                        else
                        {
                            motor.ResetAt(clearedRoom.exit.position - clearedRoom.exit.forward * 2.5f + Vector3.up * .05f);
                            Next("UseExit");
                        }
                        break;
                    case "UseExit":
                        Require(run.ExitAvailable && run.TryUseExit(), "Cleared stage exit was unavailable.");
                        report.stagesCompleted++;
                        if (run.StageIndex == 3)
                        {
                            Require(run.Phase == LiminalRunPhase.Victory, "Final boss exit did not enter Victory.");
                            report.checks.Add("All four stages completed with sequential room gates and a final boss victory.");
                            run.StartNewRun(originalSeed);
                            Require(run.StageIndex == 0 && run.Phase == LiminalRunPhase.Exploring, "Victory restart failed.");
                            Require(RouteIds(run.Rooms) == initialRoute, "Same-seed restart changed the first route.");
                            Require(run.PlayerHealth.maximumHealth == 100 && run.PlayerHealth.Health == 100, "Restart retained health upgrades.");
                            Next("TestDefeat");
                        }
                        else Next("SelectAugment");
                        break;
                    case "SelectAugment":
                        Require(run.Phase == LiminalRunPhase.AugmentChoice, "Stage clear skipped augment choice.");
                        int stage = run.StageIndex;
                        run.SelectAugment(stage % 3);
                        Require(run.Phase == LiminalRunPhase.NextStageChoice, "Augment choice did not reveal the next stage.");
                        float cooldown = caster.cooldown;
                        int flames = caster.flameCount, maxHealth = run.PlayerHealth.maximumHealth;
                        run.SelectAugment((stage + 1) % 3);
                        Require(caster.cooldown == cooldown && caster.flameCount == flames && run.PlayerHealth.maximumHealth == maxHealth, "A duplicate augment was awarded.");
                        run.ContinueToNextStage();
                        run.ContinueToNextStage();
                        Require(run.StageIndex == stage + 1 && run.Phase == LiminalRunPhase.Exploring, "Next-stage choice skipped or duplicated a stage.");
                        report.checks.Add($"Stage {stage + 1}: one augment only, one next-stage choice only.");
                        wantedRoom = 1;
                        EnterWantedRoom(motor);
                        break;
                    case "TestDefeat":
                        if (EditorApplication.timeSinceStartup - stateStarted < 1.5) return;
                        if (!run.PlayerHealth.TakeDamage(10000)) return;
                        Require(run.Phase == LiminalRunPhase.Defeat, "Player death did not enter Defeat.");
                        run.StartNewRun(unchecked(originalSeed + 104729));
                        Require(run.seed == unchecked(originalSeed + 104729) && run.StageIndex == 0 && run.PlayerHealth.IsAlive, "New-seed restart failed.");
                        report.checks.Add("Defeat, same-seed restart, new-seed restart, and augment reset passed.");
                        motor.ReleaseAutomation();
                        Finish(true, null);
                        break;
                }
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        static void EnterWantedRoom(PlayerMotor motor)
        {
            var room = run.Rooms[wantedRoom];
            motor.ResetAt(room.playerSpawn.position + Vector3.up * .05f);
            Next("AwaitEntry");
        }

        static void Next(string next) { state = next; stateStarted = EditorApplication.timeSinceStartup; }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        static void Finish(bool success, string error)
        {
            report ??= NewReport();
            report.status = success ? "passed" : "failed";
            if (!string.IsNullOrEmpty(error)) report.errors.Add(error);
            SaveReport();
            SessionState.SetBool(ActiveKey, false);
            if (success) Debug.Log("Liminal validation passed: " + ReportPath);
            else Debug.LogError("Liminal validation failed: " + error);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        static void SaveReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
    }
}
