using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>Independent Stage 1 derivatives. All authored outputs stay below StageConcepts.</summary>
    public static class StageConceptBuilder
    {
        public const string Root = "Assets/StageConcepts";
        public static readonly string[] Keys = StageConceptNavigator.Keys;
        static readonly Color[] Ambient = { new Color(.48f,.58f,.51f), new Color(.34f,.43f,.56f), new Color(.57f,.53f,.46f), new Color(.38f,.47f,.55f) };
        static readonly Color[] Accent = { new Color(.25f,1,.68f), new Color(.07f,.8f,1), new Color(1,.43f,.14f), new Color(.2f,.8f,1) };
        static readonly string[][] RoomTitles = {
            new[] { "반딧불의 입구", "고목의 제단", "거대 버섯 정원", "잊힌 룬의 성소", "달빛의 경계" },
            new[] { "부팅 구역", "방화벽 수용소", "메모리 미로", "중앙 연산실", "탈출 프로토콜" },
            new[] { "도시의 마지막 입구", "끊어진 고가도로", "무너진 주거 지구", "잿빛 대로", "대피소의 흔적" },
            new[] { "푸른 균열", "종유석 회랑", "수정의 심장", "지하 호수", "심연의 문" }
        };

        [MenuItem("AC Roguelike/Stage Concepts/Build Four Theme Dungeons")]
        public static void BuildMenu() { Debug.Log(BuildAll()); }

        public static string BuildAll()
        {
            RequireSavedEditMode();
            foreach (string folder in new[] { "Prefabs/Rooms", "Stages", "Scenes", "Materials", "Geometry", "Profiles" })
                Directory.CreateDirectory(Root + "/" + folder);
            Directory.CreateDirectory("Documentation/StageConcepts/Previews");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>("Assets/Liminal/Stages/Stage_01.asset");
            if (!source) throw new FileNotFoundException("Stage 1 definition was not found.");
            // Keep the baseline dimensions, sockets and run length as the experiment's control.
            if (source.middleRoomCount != 3 || source.startRoom.localBounds.size.x != 26)
                throw new InvalidOperationException("Stage 1 layout changed; review the concept dimensions before rebuilding.");
            for (int theme = 0; theme < 4; theme++)
                BuildTheme(theme, source);
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (string key in Keys)
            {
                string path = Root + "/Scenes/StageConcept_" + key + ".unity";
                scenes.RemoveAll(s => s.path == path);
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            StageConceptGallery.Sync();
            StageConceptGallery.OpenGallery();
            return "Created four Stage 1 sized dungeons and added twenty theme rooms to the shared room gallery.";
        }

        public static string RebuildOneTheme(int theme)
        {
            RequireSavedEditMode();
            if(theme<0 || theme>=Keys.Length) throw new ArgumentOutOfRangeException(nameof(theme));
            if(!Directory.Exists(Root+"/Stages")) throw new InvalidOperationException("Build the concept kit first.");
            var source=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>("Assets/Liminal/Stages/Stage_01.asset");
            BuildTheme(theme,source); AssetDatabase.SaveAssets();
            return "Rebuilt "+Keys[theme]+" with current Meshy resources.";
        }

        static void BuildTheme(int theme,LiminalStageDefinition source)
        {
            var rooms = new LiminalRoom[5];
            for (int roomIndex = 0; roomIndex < 5; roomIndex++) rooms[roomIndex] = CreateRoom(theme, roomIndex, source);
            string path = Root + "/Stages/" + Keys[theme] + ".asset";
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(path);
            if (!stage) { stage = Object.Instantiate(source); AssetDatabase.CreateAsset(stage, path); }
            stage.name = Keys[theme]; stage.stageId = "concept_" + Keys[theme].ToLowerInvariant();
            stage.title = StageConceptNavigator.Titles[theme];
            stage.subtitle = "스테이지 1 기반 테마 실험 · 다섯 공간을 지나 출구를 찾아라";
            stage.startRoom = rooms[0]; stage.endRoom = rooms[4];
            stage.roomPool = rooms.Skip(1).Take(3).ToArray(); stage.middleRoomCount = 3;
            stage.isBossStage = false; stage.ambientColor = Ambient[theme];
            EditorUtility.SetDirty(stage); CreateScene(theme, stage);
        }

        static LiminalRoom CreateRoom(int theme, int index, LiminalStageDefinition source)
        {
            string id = Keys[theme] + "_" + (index + 1).ToString("00");
            var root = new GameObject(id);
            try
            {
                var room = root.AddComponent<LiminalRoom>();
                room.roomId = id; room.displayName = RoomTitles[theme][index];
                room.kind = index == 0 ? LiminalRoomKind.Arrival : index == 4 ? LiminalRoomKind.Threshold : LiminalRoomKind.Combat;
                room.localBounds = source.startRoom.localBounds;
                room.designNotes = "Stage_01 baseline: 26 x 36.4 m, five rooms. Central 5m route and all spawn points remain clear. Meshy props use shared low-poly meshes and PBR normal textures.";
                var architecture = Group("Architecture", root.transform);
                var gameplay = Group("Gameplay", root.transform);
                var sockets = Group("Sockets", root.transform);
                room.entry = Marker("Entry", sockets, Vector3.zero);
                room.exit = Marker("Exit", sockets, new Vector3(0,0,36.4f));
                room.playerSpawn = Marker("PlayerSpawn", gameplay, new Vector3(0,.05f,4));
                Solid("WalkableFloor", architecture, new Vector3(0,-.25f,18.2f), new Vector3(26,.5f,36.4f));
                Solid("WestBoundary", architecture, new Vector3(-13.3f,2,18.2f), new Vector3(.6f,4,36.4f));
                Solid("EastBoundary", architecture, new Vector3(13.3f,2,18.2f), new Vector3(.6f,4,36.4f));
                foreach (float z in new[] { 0f, 36.4f })
                    foreach (float x in new[] { -8.2f, 8.2f })
                        Solid("DoorwayBoundary", architecture, new Vector3(x,1.5f,z), new Vector3(9.6f,3,.35f));
                room.entranceGate = Gate("EntranceGate", gameplay, 0, theme);
                room.exitGate = Gate("ExitGate", gameplay, 36.4f, theme);
                room.SetGates(false, false);
                room.enemySpawns = index > 0 && index < 4 ? new[] {
                    Marker("Enemy_A", gameplay, new Vector3(-1.4f,.08f,16)),
                    Marker("Enemy_B", gameplay, new Vector3(1.4f,.08f,23)),
                    Marker("Enemy_C", gameplay, new Vector3(0,.08f,29))
                } : Array.Empty<Transform>();
                StageConceptScenery.Build(root.transform, theme, index);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Rooms/" + id + ".prefab");
                return prefab.GetComponent<LiminalRoom>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject Gate(string name, Transform parent, float z, int theme)
        {
            var gate = new GameObject(name); gate.transform.SetParent(parent, false);
            gate.transform.localPosition = new Vector3(0,0,z);
            var collider = gate.AddComponent<BoxCollider>(); collider.center = new Vector3(0,1.5f,0); collider.size = new Vector3(6.8f,3,.4f);
            string path = Root + "/Materials/Gate_" + Keys[theme] + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", Accent[theme] * .45f); mat.SetColor("_EmissionColor", Accent[theme] * 2);
                mat.EnableKeyword("_EMISSION"); mat.enableInstancing = true; AssetDatabase.CreateAsset(mat,path);
            }
            for (int i = -3; i <= 3; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube); bar.name = "Seal";
                bar.transform.SetParent(gate.transform,false); bar.transform.localPosition = new Vector3(i,1.5f,0);
                bar.transform.localScale = new Vector3(.045f,3,.045f);
                bar.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(bar.GetComponent<Collider>());
            }
            return gate;
        }

        static void CreateScene(int theme, LiminalStageDefinition stage)
        {
            // Save a derivative of the actual Stage 1 run scene, carrying player, camera and HUD wiring.
            var scene = EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity", OpenSceneMode.Single);
            string scenePath = Root + "/Scenes/StageConcept_" + Keys[theme] + ".unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Could not save the independent concept scene: " + scenePath);
            var director = Object.FindFirstObjectByType<LiminalRunDirector>();
            if (!director || !director.player) throw new InvalidOperationException("Baseline scene has no wired player/director.");
            director.name = "STAGE CONCEPT / " + Keys[theme]; director.stages = new[] { stage };
            director.GenerateFirstStagePreview();
            var nav = director.gameObject.AddComponent<StageConceptNavigator>(); nav.theme = theme; nav.font = director.hudFont;
            director.player.position = director.Rooms[0].playerSpawn.position + Vector3.up * .05f;
            ConfigureEnvironment(theme);
            var camera = Object.FindFirstObjectByType<IsometricFollowCamera>();
            camera.distance = 24; camera.pitch = 55; camera.yaw = 35; camera.Snap();
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save " + scenePath);
        }

        public static void ConfigureEnvironment(int theme)
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Ambient[theme];
            RenderSettings.skybox = null; RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = theme == 2 ? new Color(.24f,.21f,.17f) : Ambient[theme] * .22f;
            RenderSettings.fogDensity = theme == 2 ? .005f : .007f;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && light.name != "Soft sky fill")
                {
                    light.color = theme == 2 ? new Color(1,.83f,.63f) : theme == 0 ? new Color(.68f,.83f,1) : new Color(.64f,.78f,1);
                    light.intensity = theme == 2 ? 1.8f : theme == 0 ? 2f : 1.65f;
                    light.shadows = LightShadows.Soft; light.shadowStrength = .55f;
                    light.transform.rotation = Quaternion.Euler(52,-28,0);
                }
            var fillObject = GameObject.Find("Soft sky fill");
            var fill = fillObject ? fillObject.GetComponent<Light>() : new GameObject("Soft sky fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.shadows = LightShadows.None;
            fill.color = theme == 2 ? new Color(.69f,.78f,1) : new Color(.60f,.78f,1);
            fill.intensity = .6f; fill.transform.rotation = Quaternion.Euler(35,150,0);
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) Object.DestroyImmediate(volume.gameObject);
            string profilePath = Root + "/Profiles/" + Keys[theme] + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
            var bloom = GetOrAdd<Bloom>(profile); bloom.threshold.Override(1); bloom.intensity.Override(theme == 1 ? .45f : .3f); bloom.scatter.Override(.55f);
            var tone = GetOrAdd<Tonemapping>(profile); tone.mode.Override(TonemappingMode.ACES);
            var grade = GetOrAdd<ColorAdjustments>(profile); grade.contrast.Override(6); grade.saturation.Override(theme == 2 ? -12 : 7); grade.postExposure.Override(.55f);
            var vignette = GetOrAdd<Vignette>(profile); vignette.intensity.Override(.18f); vignette.smoothness.Override(.45f);
            EditorUtility.SetDirty(profile);
            var global = new GameObject("Theme atmosphere").AddComponent<Volume>(); global.isGlobal = true; global.sharedProfile = profile;
            var camera = Camera.main;
            if (camera)
            {
                camera.backgroundColor = RenderSettings.fogColor; camera.allowHDR = true;
                var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.volumeLayerMask = 1;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }

        public static string RefreshAtmospheres()
        {
            RequireSavedEditMode();
            for(int theme=0;theme<4;theme++)
            {
                string path=Root+"/Scenes/StageConcept_"+Keys[theme]+".unity";
                var scene=EditorSceneManager.OpenScene(path);
                var stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root+"/Stages/"+Keys[theme]+".asset");
                stage.ambientColor=Ambient[theme]; EditorUtility.SetDirty(stage);
                ConfigureEnvironment(theme); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets(); OpenTheme(0); return "Updated four theme atmospheres.";
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var component)) return component;
            component = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(component,profile); return component;
        }

        [MenuItem("AC Roguelike/Stage Concepts/01 Open Fantasy Forest")]
        public static void OpenForest() => OpenTheme(0);
        [MenuItem("AC Roguelike/Stage Concepts/02 Open Program Prison")]
        public static void OpenDigital() => OpenTheme(1);
        [MenuItem("AC Roguelike/Stage Concepts/03 Open Ruined Earth")]
        public static void OpenRuins() => OpenTheme(2);
        [MenuItem("AC Roguelike/Stage Concepts/04 Open Crystal Cave")]
        public static void OpenCave() => OpenTheme(3);

        public static void OpenTheme(int theme)
        {
            RequireSavedEditMode();
            EditorSceneManager.OpenScene(Root + "/Scenes/StageConcept_" + Keys[theme] + ".unity");
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0,1,17),Quaternion.Euler(55,35,0),32);
        }

        static void RequireSavedEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring concepts.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes before authoring concepts.");
        }
        static Transform Group(string name, Transform parent) { var go = new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }
        static Transform Marker(string name, Transform parent, Vector3 position) { var t = Group(name,parent); t.localPosition=position; return t; }
        static void Solid(string name, Transform parent, Vector3 center, Vector3 size)
        { var t=Group(name,parent); t.localPosition=center; var box=t.gameObject.AddComponent<BoxCollider>(); box.size=size; }
    }
}
