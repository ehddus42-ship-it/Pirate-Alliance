using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Assembles reviewed Meshy parts and a clear, bounded arena without rebuilding the exhibition rooms.</summary>
    public static class GameTetrominoBossBuilder
    {
        public const string PrefabPath = "Assets/GameTheme/Resources/GameTheme/TetrominoBoss.prefab";
        public const string ArenaPath = "Assets/GameTheme/Prefabs/Rooms/Game_08_Boss.prefab";
        public const string BodyPath = "Assets/GameTheme/Art/Meshy/game_tetromino_boss_body/game_tetromino_boss_body.glb";
        public const string EarbudPath = "Assets/GameTheme/Art/Meshy/game_tetromino_boss_earbud/game_tetromino_boss_earbud.glb";
        const string Art = "Assets/GameTheme/Art/Boss";

        [MenuItem("AC Roguelike/Game Theme/Build Tetromino Boss")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring.");
            Directory.CreateDirectory(Art);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RequireModel(BodyPath); RequireModel(EarbudPath);
            BuildBoss();
            BuildArena();
            ConnectStage();
            AssetDatabase.SaveAssets();
            Debug.Log("TETROMINO_BOSS_BUILD_OK: Meshy handheld, two earbud hands, flexible cables and final boss arena connected.");
        }

        public static void ConnectStage()
        {
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(GameThemeValidation.StagePath);
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPath);
            if (!stage || !arena) throw new InvalidOperationException("Game stage and boss arena are required.");
            stage.endRoom = arena.GetComponent<LiminalRoom>();
            EditorUtility.SetDirty(stage);
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            var mission = catalog ? catalog.Missions.FirstOrDefault(m => m != null && m.id == "game_exhibition") : null;
            if (mission == null) throw new InvalidOperationException("G-06 Game mission is missing.");
            mission.objective = "세 전투 구역 돌파 · 드롭 키퍼 격파 · 출구 도달";
            mission.gimmickName = "복셀 군단 · 드롭 키퍼";
            mission.gimmickDescription = "가속하는 블록 탄막 · 네 번 튕기는 I자 블록 · Z자 블록 세 마리의 돌진. 바닥 예고를 보고 피하라.";
            EditorUtility.SetDirty(catalog);
        }

        static void BuildBoss()
        {
            var root = new GameObject("Drop Keeper");
            try
            {
                var controller = root.AddComponent<CharacterController>();
                controller.radius = 1.02f; controller.height = 3.1f;
                controller.center = new Vector3(0, 1.58f, 0); controller.skinWidth = .06f;
                controller.minMoveDistance = 0; controller.stepOffset = .25f; controller.slopeLimit = 45;
                var visual = Child("Visual", root.transform);
                var pose = Child("Pose", visual);
                PlaceModel(BodyPath, Child("Model", pose), 3.4f, false);
                BuildScreen(pose);
                var left = Child("LeftHand", pose); left.localPosition = new Vector3(-1.85f, 1.6f, .35f);
                var right = Child("RightHand", pose); right.localPosition = new Vector3(1.85f, 1.6f, .35f);
                PlaceModel(EarbudPath, left, .66f, true);
                PlaceModel(EarbudPath, right, .66f, true);
                var jack = Child("JackAnchor", pose); jack.localPosition = new Vector3(1.05f, 1.35f, .05f);
                var cable = Material("BossCable", new Color(.09f, .07f, .15f), .1f, .38f);
                var metal = Material("JackMetal", new Color(.73f, .69f, .51f), .8f, .65f);
                var plug = Primitive("Inserted headphone jack", jack, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.115f, .19f, .115f), metal);
                plug.localRotation = Quaternion.Euler(0, 0, 90);
                var sleeve = Primitive("Jack rubber sleeve", jack, PrimitiveType.Cylinder, new Vector3(.25f, 0, 0), new Vector3(.18f, .15f, .18f), cable);
                sleeve.localRotation = Quaternion.Euler(0, 0, 90);
                var rig = pose.gameObject.AddComponent<GameBossCableRig>();
                rig.cableMaterial = cable;
                rig.RefreshRestPose();
                // Persistent copies provide a useful prefab preview; the runtime rig owns separate dynamic meshes.
                int index = 0;
                foreach (var generated in rig.GeneratedMeshes)
                {
                    if (!generated) continue;
                    string path = Art + "/Cable_" + index++ + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh) EditorUtility.CopySerialized(generated, mesh);
                    else { mesh = Object.Instantiate(generated); AssetDatabase.CreateAsset(mesh, path); }
                    foreach (var filter in pose.GetComponentsInChildren<MeshFilter>())
                        if (filter.sharedMesh == generated) filter.sharedMesh = mesh;
                    EditorUtility.SetDirty(mesh);
                }
                var health = root.AddComponent<TrainingEnemy>();
                health.maxHealth = 1000; health.respawnOnDeath = false; health.deferDeathVisuals = true;
                health.aimAnchor = Child("Aim", pose); health.aimAnchor.localPosition = new Vector3(0, 1.7f, 0);
                health.visibleRenderers = root.GetComponentsInChildren<Renderer>();
                var boss = root.AddComponent<GameTetrominoBoss>(); boss.displayName = "드롭 키퍼";
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildArena()
        {
            var root = new GameObject("Game_08_Boss");
            try
            {
                var room = root.AddComponent<LiminalRoom>();
                room.roomId = "Game_08_Boss"; room.displayName = "마지막 블록의 무대"; room.kind = LiminalRoomKind.Boss;
                room.localBounds = new Bounds(new Vector3(0, 4, 18.2f), new Vector3(26, 8, 36.4f));
                room.designNotes = "G-06 final boss. Clear central arena, continuous rebound walls, four-contact I-block lifetime. Defeating Drop Keeper opens the exit.";
                root.AddComponent<GameThemeRoom>();
                var gameplay = Child("Gameplay", root.transform); var sockets = Child("Sockets", root.transform);
                room.entry = Marker("Entry", sockets, Vector3.zero);
                room.exit = Marker("Exit", sockets, new Vector3(0, 0, 36.4f));
                room.playerSpawn = Marker("PlayerSpawn", gameplay, new Vector3(0, .05f, 4));
                room.enemySpawns = new[] { Marker("BossSpawn", gameplay, new Vector3(0, .05f, 23)) };
                var scenery = Child("Arena", root.transform);
                var floor = Existing("Floor"); var dark = Existing("Shell"); var glow = Existing("Glow");
                var cyan = Existing("Cyan"); var purple = Existing("Purple");
                Box("Arena floor", scenery, new Vector3(0, -.2f, 18.2f), new Vector3(26, .4f, 36.4f), floor, true);
                for (int x = -12; x <= 12; x += 2)
                    Box("Floor grid X", scenery, new Vector3(x, .008f, 18.2f), new Vector3(.035f, .014f, 36.2f), dark);
                for (int z = 2; z < 36; z += 2)
                    Box("Floor grid Z", scenery, new Vector3(0, .008f, z), new Vector3(25.8f, .014f, .035f), dark);
                // Low visible rails keep the camera clear; taller simple colliders prevent flying blocks escaping.
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Rebound wall", scenery, new Vector3(side * 13.25f, .4f, 18.2f), new Vector3(.5f, .8f, 36.9f), dark);
                    Solid("Rebound collision", scenery, new Vector3(side * 13.25f, 2, 18.2f), new Vector3(.5f, 4, 36.9f));
                    Box("Wall light", scenery, new Vector3(side * 12.98f, .76f, 18.2f), new Vector3(.06f, .06f, 36.4f), glow);
                    for (int z = 4; z <= 32; z += 7)
                    {
                        Box("Score pillar", scenery, new Vector3(side * 14.2f, 1.5f, z), new Vector3(1.2f, 3, 1.2f), dark);
                        Box("Score light", scenery, new Vector3(side * 13.57f, 1.5f, z), new Vector3(.05f, 2.4f, .6f), z % 2 == 0 ? cyan : purple);
                    }
                }
                foreach (float z in new[] { -.25f, 36.65f })
                    foreach (int side in new[] { -1, 1 })
                    {
                        Box("Gate wing", scenery, new Vector3(side * 8.2f, .4f, z), new Vector3(9.6f, .8f, .5f), dark);
                        Solid("Gate wing collision", scenery, new Vector3(side * 8.2f, 2, z), new Vector3(9.6f, 4, .5f));
                    }
                room.entranceGate = Gate("EntranceGate", gameplay, 0, glow);
                room.exitGate = Gate("ExitGate", gameplay, 36.4f, glow);
                room.SetGates(false, false);
                // Reviewed Meshy exhibition props stay beyond the rails, leaving room for readable ricochets.
                foreach (int side in new[] { -1, 1 })
                {
                    string path = "Assets/GameTheme/Art/Meshy/game_arcade_cabinet/game_arcade_cabinet.glb";
                    var display = Marker("Meshy arcade display", scenery, new Vector3(side * 16, 0, 10));
                    display.localRotation = Quaternion.Euler(0, side < 0 ? 60 : -60, 0);
                    PlaceModel(path, display, 3.6f, false);
                }
                PrefabUtility.SaveAsPrefabAsset(root, ArenaPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildScreen(Transform pose)
        {
            // Meshy's front LCD plane, measured from the reviewed GLB and normalized to 3.4 m body height.
            var screen = Marker("LCD tetromino board", pose, new Vector3(-.005f, 2.51f, .455f));
            var background = Material("LCDBackground", new Color(.055f, .14f, .15f), .05f, .3f);
            var pixel = Material("LCDPixel", new Color(.42f, .95f, .72f), .05f, .3f);
            pixel.EnableKeyword("_EMISSION"); pixel.SetColor("_EmissionColor", new Color(.2f, .65f, .45f));
            Box("LCD face", screen, Vector3.zero, new Vector3(.92f, .90f, .015f), background);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 10; column++)
                    if (column != 6 && (row == 0 || (column + row * 3) % 5 < 3))
                        Box("Stack cell", screen, new Vector3((column - 4.5f) * .079f, -.36f + row * .079f, .012f), new Vector3(.070f, .070f, .012f), pixel);
            var falling = Marker("Falling T", screen, new Vector3(-.12f, .3f, .012f));
            foreach (var p in new[] { new Vector2(-1, 0), Vector2.zero, new Vector2(1, 0), new Vector2(0, 1) })
                Box("Falling cell", falling, new Vector3(p.x * .079f, p.y * .079f, 0), new Vector3(.070f, .070f, .012f), pixel);
            screen.gameObject.AddComponent<GameBossScreen>().fallingPiece = falling;
        }

        static void RequireModel(string path)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new FileNotFoundException("Meshy model must finish importing before building: " + path);
        }
        static void PlaceModel(string path, Transform parent, float height, bool centered)
        {
            RequireModel(path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            Bounds bounds = default; bool found = false;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds; var matrix = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            if (!found || bounds.size.y < .001f) throw new InvalidOperationException("Empty Meshy geometry: " + path);
            float scale = height / bounds.size.y;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(-bounds.center.x, centered ? -bounds.center.y : -bounds.min.y, -bounds.center.z) * scale;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
        }
        static Material Existing(string key) => AssetDatabase.LoadAssetAtPath<Material>("Assets/GameTheme/Materials/Game_" + key + ".mat");
        static Material Material(string name, Color color, float metallic, float smoothness)
        {
            string path = Art + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat); return mat;
        }
        static Transform Child(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static Transform Marker(string name, Transform parent, Vector3 p) { var t = Child(name, parent); t.localPosition = p; return t; }
        static Transform Primitive(string name, Transform parent, PrimitiveType type, Vector3 p, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = p; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
        static void Box(string name, Transform parent, Vector3 p, Vector3 size, Material material, bool solid = false)
        {
            var t = Primitive(name, parent, PrimitiveType.Cube, p, size, material);
            if (solid) t.gameObject.AddComponent<BoxCollider>();
        }
        static void Solid(string name, Transform parent, Vector3 p, Vector3 size) { var t = Marker(name, parent, p); t.gameObject.AddComponent<BoxCollider>().size = size; }
        static GameObject Gate(string name, Transform parent, float z, Material material)
        {
            var t = Marker(name, parent, new Vector3(0, 0, z));
            var collider = t.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, 2, 0); collider.size = new Vector3(6.8f, 4, .5f);
            for (int x = -3; x <= 3; x++) Box("Gate pixel beam", t, new Vector3(x, 1.5f, 0), new Vector3(.065f, 3, .065f), material);
            return t.gameObject;
        }
    }
}
