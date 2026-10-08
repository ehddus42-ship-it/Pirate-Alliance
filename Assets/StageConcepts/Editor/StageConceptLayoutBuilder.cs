using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>
    /// Rebuilds the variation rooms inside Unity from Assets/StageConcepts/Layouts/*.json.
    /// The layouts, decoration meshes and materials are produced by Tools/StageConcepts/variations/build.py;
    /// the committed prefabs were written by that tool, and this menu regenerates the same prefabs through
    /// Unity's own prefab API (useful after editing a layout or if an asset needs re-serialising).
    /// </summary>
    public static class StageConceptLayoutBuilder
    {
        public const string LayoutFolder = StageConceptBuilder.Root + "/Layouts";
        const string MaterialFolder = StageConceptBuilder.Root + "/Materials/";
        const string MeshyFolder = StageConceptBuilder.Root + "/Art/Meshy/";
        public const int RoomsPerTheme = 7;
        public const int StartIndex = 1, EndIndex = 5;
        public static readonly int[] PoolIndices = { 2, 3, 4, 6, 7 };

        [Serializable] sealed class Layout
        {
            public string roomId, displayName, kind, notes;
            public Vector3 playerSpawn;
            public Vector3[] enemySpawns = new Vector3[0];
            public Decor[] decor = new Decor[0];
            public Model[] models = new Model[0];
            public BoxDef[] colliders = new BoxDef[0];
            public LightDef[] lights = new LightDef[0];
        }
        [Serializable] sealed class Decor { public string material, mesh; public bool castShadows; }
        [Serializable] sealed class Model { public string key; public Vector3 position, targetSize, offset; public float yaw, modelYaw, scale; }
        [Serializable] sealed class BoxDef { public string name; public Vector3 center, size; public float yaw, pitch; public bool walkable; }
        [Serializable] sealed class LightDef { public string name, color; public Vector3 position; public float intensity, range; }

        [MenuItem("AC Roguelike/Stage Concepts/Rebuild Variation Rooms From Layout")]
        public static void RebuildMenu() { Debug.Log(RebuildAll()); }

        public static string RebuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before rebuilding rooms.");
            if (!AssetDatabase.IsValidFolder(LayoutFolder)) throw new DirectoryNotFoundException(LayoutFolder + " is missing; run Tools/StageConcepts/variations/build.py first.");
            int rooms = 0;
            for (int theme = 0; theme < StageConceptBuilder.Keys.Length; theme++)
                rooms += RebuildTheme(theme);
            AssetDatabase.SaveAssets();
            return "Rebuilt " + rooms + " variation rooms from layouts and updated four stage definitions.";
        }

        public static int RebuildTheme(int theme)
        {
            string key = StageConceptBuilder.Keys[theme];
            var built = new LiminalRoom[RoomsPerTheme + 1];
            for (int index = 1; index <= RoomsPerTheme; index++)
            {
                string id = key + "_" + index.ToString("00");
                string layoutPath = LayoutFolder + "/" + id + ".json";
                if (!File.Exists(layoutPath)) throw new FileNotFoundException("Missing room layout: " + layoutPath);
                built[index] = BuildRoom(theme, index, JsonUtility.FromJson<Layout>(File.ReadAllText(layoutPath)));
            }
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(StageConceptBuilder.Root + "/Stages/" + key + ".asset");
            if (stage)
            {
                stage.startRoom = built[StartIndex];
                var apocalypseArena = key == "Ruins"
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(AcRoguelike.RuinsBoss.Editor.RuinsBossBuilder.ArenaPath) : null;
                stage.endRoom = apocalypseArena ? apocalypseArena.GetComponent<LiminalRoom>() : built[EndIndex];
                stage.roomPool = PoolIndices.Select(i => built[i]).ToArray();
                stage.middleRoomCount = 3;
                EditorUtility.SetDirty(stage);
                if (apocalypseArena) AcRoguelike.RuinsBoss.Editor.RuinsBossBuilder.ConnectStage();
            }
            return RoomsPerTheme;
        }

        static LiminalRoom BuildRoom(int theme, int index, Layout layout)
        {
            string path = StageConceptBuilder.Root + "/Prefabs/Rooms/" + layout.roomId + ".prefab";
            bool loaded = File.Exists(path);
            GameObject root = loaded ? PrefabUtility.LoadPrefabContents(path) : StageConceptBuilder.CreateShell(theme, index, layout.roomId);
            try
            {
                var room = root.GetComponent<LiminalRoom>();
                room.displayName = layout.displayName;
                room.kind = (LiminalRoomKind)Enum.Parse(typeof(LiminalRoomKind), layout.kind);
                room.designNotes = layout.notes;
                foreach (string group in new[] { "Props", "Lighting" })
                {
                    Transform old = root.transform.Find(group);
                    if (old) Object.DestroyImmediate(old.gameObject);
                }
                Transform props = Child(root.transform, "Props");
                Transform lighting = Child(root.transform, "Lighting");
                props.SetSiblingIndex(3);
                lighting.SetSiblingIndex(4);
                foreach (var decor in layout.decor) AddDecor(props, decor);
                foreach (var model in layout.models) AddModel(props, model);
                foreach (var box in layout.colliders)
                {
                    Transform t = Child(props, box.name, false);
                    t.localPosition = box.center;
                    t.localRotation = Quaternion.Euler(box.pitch, box.yaw, 0);
                    t.gameObject.AddComponent<BoxCollider>().size = box.size;
                }
                foreach (var def in layout.lights)
                {
                    Transform t = Child(lighting, def.name, false);
                    t.localPosition = def.position;
                    var light = t.gameObject.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = ColorUtility.TryParseHtmlString(def.color, out Color c) ? c : Color.white;
                    light.intensity = def.intensity;
                    light.range = def.range;
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.Auto;
                }
                ApplyMarkers(root.transform, room, layout);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved.GetComponent<LiminalRoom>();
            }
            finally
            {
                if (loaded) PrefabUtility.UnloadPrefabContents(root);
                else if (root) Object.DestroyImmediate(root);
            }
        }

        static void AddDecor(Transform props, Decor decor)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(decor.mesh);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + decor.material + ".mat");
            if (!mesh || !material)
            {
                Debug.LogWarning("[Stage concepts] Missing decoration mesh or material: " + decor.mesh + " / " + decor.material);
                return;
            }
            Transform t = Child(props, decor.material + " (batched)", false);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = decor.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        static void AddModel(Transform props, Model model)
        {
            Transform slot = Child(props, "MeshySlot__" + model.key, false);
            slot.localPosition = model.position;
            slot.localRotation = Quaternion.Euler(0, model.yaw, 0);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(MeshyFolder + model.key + "/" + model.key + ".glb");
            if (!source)
            {
                Debug.LogWarning("[Stage concepts] Missing Meshy model " + model.key + "; the slot is kept empty.", slot);
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, slot);
            instance.name = "Meshy__" + model.key;
            instance.transform.localPosition = model.offset;
            instance.transform.localRotation = Quaternion.Euler(0, model.modelYaw, 0);
            instance.transform.localScale = Vector3.one * model.scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        static void ApplyMarkers(Transform root, LiminalRoom room, Layout layout)
        {
            Transform gameplay = root.Find("Gameplay");
            if (room.playerSpawn) room.playerSpawn.localPosition = layout.playerSpawn;
            var markers = gameplay.Cast<Transform>().Where(t => t.name.StartsWith("Enemy_", StringComparison.Ordinal)).ToList();
            while (markers.Count > layout.enemySpawns.Length)
            {
                Object.DestroyImmediate(markers[markers.Count - 1].gameObject);
                markers.RemoveAt(markers.Count - 1);
            }
            while (markers.Count < layout.enemySpawns.Length)
                markers.Add(Child(gameplay, "Enemy_" + (char)('A' + markers.Count), false));
            for (int i = 0; i < markers.Count; i++) markers[i].localPosition = layout.enemySpawns[i];
            room.enemySpawns = markers.ToArray();
        }

        static Transform Child(Transform parent, string name, bool reuse = true)
        {
            if (reuse)
            {
                Transform existing = parent.Find(name);
                if (existing) return existing;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
