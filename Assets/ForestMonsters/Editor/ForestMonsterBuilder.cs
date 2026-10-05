using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.Forest.Editor
{
    /// <summary>Reproducible forest prefabs from the source Meshy models. Never edits a scene.</summary>
    public static class ForestMonsterBuilder
    {
        public const string Root = "Assets/ForestMonsters";
        public const string Prefabs = Root + "/Resources/ForestMonsters";
        const string Generated = Root + "/Art/Generated";
        static readonly string[] Keys = { "moonworm", "moss_frog", "lunar_butterfly", "elderwood", "volatile_sapling" };
        static readonly string[] Names = { "달빛 지렁이", "이끼 개구리", "달가루 나비", "달빛 고목", "성난 묘목" };
        static readonly string[] PrefabNames = { "Moonworm", "MossFrog", "LunarButterfly", "Elderwood", "VolatileSapling" };
        static readonly Type[] Behaviours = { typeof(ForestWorm), typeof(ForestFrog), typeof(ForestButterfly), typeof(ForestTreant), typeof(ForestSapling) };

        public static void BuildAndCapture()
        {
            Build();
            if (Application.isBatchMode)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/StageConcepts/Scenes/StageConcept_Forest.unity");
            ForestMonsterPreview.Capture();
        }

        [MenuItem("AC Roguelike/Forest/Build Forest Monsters")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring.");
            Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < Keys.Length; i++) Build(i);
            BuildEarthClod();
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            var mission = catalog ? catalog.Missions.FirstOrDefault(m => m != null && m.id == "forest_survey") : null;
            if (mission == null) throw new InvalidOperationException("Forest mission is missing.");
            mission.gimmickName = "숲의 생존 본능";
            mission.gimmickDescription = "지렁이의 기습 · 개구리의 혀 · 나비의 가루와 돌풍 · 고목의 묘목과 뿌리. 바닥 예고를 피하고, 달려드는 묘목을 먼저 제거해.";
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("FOREST_MONSTERS_BUILD_OK: four creatures and a destructible sapling.");
        }

        static void Build(int index)
        {
            var root = new GameObject(PrefabNames[index]);
            try
            {
                // Worm length and butterfly span are more stable sizing references than height.
                float[] dimensions = { 3f, 1.4f, 3f, 3.1f, .8f };
                float[] radii = { .58f, .74f, .5f, .7f, .3f };
                float[] heights = { 2.05f, 1.45f, 2.1f, 3.1f, .82f };
                var body = root.AddComponent<CharacterController>();
                body.radius = radii[index]; body.height = heights[index];
                body.center = Vector3.up * (heights[index] * .5f + .03f);
                body.skinWidth = .045f; body.minMoveDistance = 0; body.stepOffset = .2f; body.slopeLimit = 45;
                var pose = Child("Pose", Child("Visual", root.transform));
                var model = Child("Model", pose);
                PlaceModel(Keys[index], model, dimensions[index], index == 0 ? 2 : index == 2 ? 0 : 1);
                var health = root.AddComponent<TrainingEnemy>();
                health.respawnOnDeath = false; health.deferDeathVisuals = index != 4;
                health.aimAnchor = Child("Aim", pose);
                health.aimAnchor.localPosition = Vector3.up * (index == 2 ? .35f : heights[index] * .6f);
                health.visibleRenderers = root.GetComponentsInChildren<Renderer>(true);
                var behaviour = root.AddComponent(Behaviours[index]);
                if (behaviour is LiminalPropMonster monster) monster.displayName = Names[index];
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + PrefabNames[index] + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildEarthClod()
        {
            if (!File.Exists(Root + "/Art/Meshy/burrow_earth_clod/burrow_earth_clod.glb")) return;
            var root = new GameObject("EarthClod");
            try { PlaceModel("burrow_earth_clod", root.transform, 1f, 0); PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/EarthClod.prefab"); }
            finally { Object.DestroyImmediate(root); }
        }

        static void PlaceModel(string key, Transform parent, float size, int axis)
        {
            string path = Root + "/Art/Meshy/" + key + "/" + key + ".glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new FileNotFoundException("Meshy model must finish importing: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            int n = 0;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (!mesh) continue;
                // Store a readable copy for per-instance procedural animation, without changing other glTF imports.
                string meshPath = Generated + "/" + key + "_" + n++ + ".asset";
                var copy = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                var readable = new Mesh { name = key + "_deformable", indexFormat = mesh.indexFormat };
                readable.vertices = mesh.vertices; readable.normals = mesh.normals; readable.tangents = mesh.tangents;
                readable.uv = mesh.uv; readable.uv2 = mesh.uv2; readable.colors32 = mesh.colors32;
                readable.subMeshCount = mesh.subMeshCount;
                for (int s = 0; s < mesh.subMeshCount; s++) readable.SetIndices(mesh.GetIndices(s), mesh.GetTopology(s), s);
                readable.bounds = mesh.bounds;
                if (copy) { EditorUtility.CopySerialized(readable, copy); Object.DestroyImmediate(readable); }
                else { copy = readable; AssetDatabase.CreateAsset(copy, meshPath); }
                EditorUtility.SetDirty(copy); filter.sharedMesh = copy;
            }
            Bounds bounds = default; bool found = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.localBounds;
                var matrix = parent.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            if (!found || bounds.size[axis] < .001f) throw new InvalidOperationException("Empty Meshy geometry: " + key);
            float scale = size / bounds.size[axis];
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            Debug.Log("FOREST_MODEL " + key + " dimensions=" + (bounds.size * scale).ToString("F3"));
        }

        static Transform Child(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
    }
}
