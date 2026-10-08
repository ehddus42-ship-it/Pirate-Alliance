using System;
using System.Linq;
using AcRoguelike.Liminal;
using AcRoguelike.Liminal.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>Adds theme variations to the existing room collection without replacing authored rooms.</summary>
    public static class StageConceptGallery
    {
        public const string RoomFolder = StageConceptBuilder.Root + "/Prefabs/Rooms";
        const string RootName = "THEME VARIATIONS / Forest - Digital - Ruins - Cave";

        [MenuItem("AC Roguelike/Stage Concepts/00 Open Shared Room Gallery")]
        public static void OpenGallery()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before opening the gallery.");
            var scene = SceneManager.GetSceneByPath(LiminalMapBuilder.GalleryPath);
            if (!scene.isLoaded)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes before opening the gallery.");
                scene = EditorSceneManager.OpenScene(LiminalMapBuilder.GalleryPath);
            }
            FrameThemeArea();
        }

        [MenuItem("AC Roguelike/Stage Concepts/Add Missing Themes to Shared Gallery")]
        public static void SyncMenu() { Debug.Log(Sync()); }

        public static string Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before updating the gallery.");
            var scene = SceneManager.GetSceneByPath(LiminalMapBuilder.GalleryPath);
            bool alreadyOpen = scene.isLoaded;
            if (alreadyOpen && scene.isDirty) throw new InvalidOperationException("Save the gallery before adding missing rooms.");
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(LiminalMapBuilder.GalleryPath, OpenSceneMode.Additive);
            try
            {
                int added = AppendToScene(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the shared room gallery.");
                return "Shared gallery updated: " + added + " new theme rooms; original rooms preserved.";
            }
            finally { if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true); }
        }

        // Also used when the original room kit regenerates its gallery.
        public static int AppendToScene(Scene scene)
        {
            int count = StageConceptLayoutBuilder.RoomsPerTheme;
            var assets = StageConceptBuilder.Keys.SelectMany(key => Enumerable.Range(1, count)
                .Select(i => AssetDatabase.LoadAssetAtPath<GameObject>(RoomFolder + "/" + key + "_" + i.ToString("00") + ".prefab"))).Where(go => go).ToArray();
            if (assets.Length == 0) return 0;
            var root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
            if (!root) { root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); }
            var existing = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<LiminalRoom>(true)).Select(r => r.roomId).ToHashSet();
            var concrete = AssetDatabase.LoadAssetAtPath<Material>(LiminalMapBuilder.Root + "/Materials/Concrete.mat");
            var walkway = Child(root.transform, "Connecting walkways");
            // Rooms sit 65 m apart along +z; the floor runs from the theme signs to 12 m past the last room.
            float floorEnd = FloorEnd(count);
            Box(walkway, "Theme gallery floor", new Vector3(338, -.35f, (floorEnd - 14.5f) / 2), new Vector3(209, .38f, floorEnd + 14.5f), concrete, true, true);
            Box(walkway, "Link to original variations", new Vector3(208, -.35f, -6), new Vector3(56, .38f, 14), concrete, true);
            int added = 0;
            for (int theme = 0; theme < StageConceptBuilder.Keys.Length; theme++)
            {
                string key = StageConceptBuilder.Keys[theme];
                var group = Child(root.transform, (theme + 1).ToString("00") + " " + key + " / " + StageConceptNavigator.Titles[theme]);
                for (int index = 0; index < count; index++)
                {
                    string id = key + "_" + (index + 1).ToString("00");
                    var asset = assets.FirstOrDefault(go => go.name == id);
                    if (!asset) continue;
                    var position = new Vector3(260 + theme * 52, 0, index * 65);
                    if (!existing.Contains(id))
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                        instance.transform.SetParent(group, false);
                        instance.transform.localPosition = position;
                        var room = instance.GetComponent<LiminalRoom>();
                        room.SetGates(false, false);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(room.entranceGate);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(room.exitGate);
                        existing.Add(id); added++;
                    }
                    // Room titles change when a layout is regenerated; replace signs that show an older title.
                    ReplaceSign(group, id + " / ", id + " / " + asset.GetComponent<LiminalRoom>().displayName, position + new Vector3(0, 1.4f, -4), 23, .32f);
                }
                ReplaceSign(group, StageConceptNavigator.Titles[theme] + " / ", StageConceptNavigator.Titles[theme] + " / " + count + "개 바리에이션",
                    new Vector3(260 + theme * 52, 2.5f, -13), 27, .5f);
            }
            ReplaceSign(walkway, "기존 맵 20개", "기존 맵 20개  <  |  >  신규 테마 " + StageConceptBuilder.Keys.Length * count + "개", new Vector3(208, 2.5f, -13), 32, .48f);
            var guide = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<UnityEngine.UI.Text>(true)).FirstOrDefault(t => t.name == "Instructions");
            if (guide) guide.text = "맵 바리에이션 갤러리 / 총 " + existing.Count + "개 방\n왼쪽: 기존 맵 20개 / 오른쪽: 숲 · 프로그램 · 폐허 · 동굴\nWASD 이동 / SHIFT 대시 / SCROLL 확대\nRoom Workshop에서 방 선택 → 씬에서 보기";
            EditorSceneManager.MarkSceneDirty(scene);
            return added;
        }

        static float FloorEnd(int roomsPerTheme) => (roomsPerTheme - 1) * 65 + 48.5f;

        public static void FrameThemeArea()
        {
            var scene = SceneManager.GetSceneByPath(LiminalMapBuilder.GalleryPath);
            if (!scene.isLoaded) return;
            var root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
            if (!root) return;
            Selection.activeGameObject = root;
            float floorEnd = FloorEnd(StageConceptLayoutBuilder.RoomsPerTheme);
            SceneView.lastActiveSceneView?.LookAt(new Vector3(338, 0, (floorEnd - 14.5f) / 2), Quaternion.Euler(65, 0, 0), 205 * (floorEnd + 14.5f) / 323);
        }

        static Transform Child(Transform parent, string name)
        {
            var child = FindChild(parent, name);
            if (child) return child;
            child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }

        static Transform FindChild(Transform parent, string name) => parent.Cast<Transform>().FirstOrDefault(t => t.name == name);

        static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider, bool resize = false)
        {
            var found = FindChild(parent, name);
            if (found)
            {
                if (resize) { found.localPosition = position; found.localScale = size; }
                return;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        /// <summary>Keeps one sign per prefix: signs whose text starts with the prefix but differs are removed.</summary>
        static void ReplaceSign(Transform parent, string prefix, string text, Vector3 position, float width, float size)
        {
            foreach (var stale in parent.Cast<Transform>().Where(t => t.name.StartsWith("Gallery sign / " + prefix, StringComparison.Ordinal) &&
                         t.name != "Gallery sign / " + text).ToList())
                Object.DestroyImmediate(stale.gameObject);
            Sign(parent, text, position, width, size);
        }

        static void Sign(Transform parent, string text, Vector3 position, float width, float size)
        {
            string name = "Gallery sign / " + text;
            if (FindChild(parent, name)) return;
            var sign = Child(parent, name); sign.localPosition = position;
            Box(sign, "Panel", Vector3.zero, new Vector3(width, 1.15f, .12f), AssetDatabase.LoadAssetAtPath<Material>(LiminalMapBuilder.Root + "/Materials/Sign_Dark.mat"), false);
            var mesh = Child(sign, "Label").gameObject.AddComponent<TextMesh>();
            mesh.transform.localPosition = new Vector3(0, 0, -.072f);
            mesh.font = AssetDatabase.LoadAssetAtPath<Font>(LiminalMapBuilder.Root + "/Art/Fonts/NotoSansKR-Regular.ttf");
            mesh.text = text; mesh.fontSize = 48; mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = new Color(.82f, .9f, .84f);
            var renderer = mesh.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(LiminalMapBuilder.Root + "/Materials/SignageFont.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
