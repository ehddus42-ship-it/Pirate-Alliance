using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Proi 캐릭터 플레이 장면 자동 세팅.
/// - Proi_Character.fbx : 메시 + 블렌드셰이프 + Idle / Run / WW_Motion 클립 (Blender 에서 익스포트)
/// - 조작: WASD 이동 (카메라 기준), 마우스 좌클릭 3타 콤보
/// 메뉴: Tools > Proi > Setup Playground Scene
/// </summary>
public static class ProiSetup
{
    const string Root = "Assets/Characters/Proi";
    const string Fbx = Root + "/Models/Proi_Character.fbx";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string ControllerPath = Root + "/Animations/Proi.controller";
    const string ScenePath = Root + "/Scenes/ProiPlayground.unity";
    const string ToonShader = "Proi/Toon";
    // Blender 에서 측정한 Run 클립의 지면 속도(m/s). 이동 속도에 맞춰 재생 속도를 조절해 발 미끄러짐을 줄입니다.
    const float RunClipGroundSpeed = 2.67f;
    const float MoveSpeed = 3.2f;
    static readonly string[] ClipNames = { "Idle", "Run", "WW_Motion", "Attack1", "Attack2", "Attack3", "Recover2" };

    [MenuItem("Tools/Proi/Setup Playground Scene")]
    public static void SetupFromMenu() => Setup();

    // Unity -batchmode -executeMethod ProiSetup.RunBatch
    public static void RunBatch() => RunAndExit(Setup);

    // Unity -batchmode -executeMethod ProiSetup.CaptureBatch   (미리보기 PNG 저장)
    public static void CaptureBatch() => RunAndExit(Capture);

    static void RunAndExit(System.Action action)
    {
        try
        {
            action();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void Setup()
    {
        ConfigureTextures();
        var (charMat, outfitMat) = CreateMaterials();
        ConfigureModel(charMat, outfitMat);
        AnimatorController controller = BuildController();
        ProiAttackSetup.Ensure(false);          // combo states (Attack1~3, Recover2)
        BuildScene(controller);
        Debug.Log("[ProiSetup] Done.");
    }

    // ---------------------------------------------------------------- Textures / Materials

    static void ConfigureTextures()
    {
        foreach (string path in Directory.GetFiles(TexDir, "*.png").Select(p => p.Replace('\\', '/')))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.Contains("_Normal");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.maxTextureSize = 4096;   // 얼굴 디테일(눈)이 4K 아틀라스에 있음
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }

    static (Material, Material) CreateMaterials()
    {
        Shader shader = Shader.Find(ToonShader);
        if (shader == null) throw new System.Exception($"Shader '{ToonShader}' not found (compile error?)");

        Material Make(string name, Color shade)
        {
            string path = $"{MatDir}/M_Proi_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Proi_{name}_Color.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Proi_{name}_Normal.png"));
            mat.SetColor("_ShadeColor", shade);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        var charMat = Make("Char", new Color(0.9f, 0.74f, 0.76f));        // 피부·머리카락: 분홍빛 그림자
        var outfitMat = Make("Outfit", new Color(0.74f, 0.7f, 0.82f));   // 의상: 차가운 보랏빛 그림자
        AssetDatabase.SaveAssets();
        return (charMat, outfitMat);
    }

    // ---------------------------------------------------------------- Model

    static void ConfigureModel(Material charMat, Material outfitMat)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        mi.animationType = ModelImporterAnimationType.Generic;   // 본 회전을 그대로 재생 (트윈테일 본 포함)
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importBlendShapes = true;
        mi.importBlendShapeNormals = ModelImporterNormals.Calculate;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Proi_Char"), charMat);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Proi_Outfit"), outfitMat);

        var clips = mi.defaultClipAnimations
            .Where(c => !c.takeName.StartsWith("Key"))
            .Select(c =>
            {
                string name = ClipNames.FirstOrDefault(n => c.takeName.EndsWith(n)) ?? c.takeName;
                c.name = name;
                c.loopTime = name == "Idle" || name == "Run";
                c.loopPose = false;
                return c;
            })
            .GroupBy(c => c.name).Select(g => g.First())
            .ToArray();
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        Debug.Log("[ProiSetup] Clips: " + string.Join(", ", clips.Select(c => $"{c.name}<-{c.takeName}")));
    }

    static AnimationClip LoadClip(string name)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);
        if (clip == null) throw new System.Exception($"Clip '{name}' not found in {Fbx}");
        return clip;
    }

    // ---------------------------------------------------------------- Animator

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        var locomotion = sm.AddState("Locomotion", new Vector3(300, 0));
        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(LoadClip("Idle"), 0f);
        tree.AddChild(LoadClip("Run"), 1f);
        ChildMotion[] children = tree.children;
        children[1].timeScale = MoveSpeed / RunClipGroundSpeed;
        tree.children = children;
        locomotion.motion = tree;
        sm.defaultState = locomotion;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // ---------------------------------------------------------------- Scene

    static void BuildScene(AnimatorController controller)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10, 1, 10);
        ground.GetComponent<Renderer>().sharedMaterial = GroundMaterial();

        Light sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.1f;
        }

        var player = new GameObject("Player");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
        model.name = "Proi";
        model.transform.localRotation = Quaternion.Euler(0f, DetectModelYaw(model), 0f);

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        smr.updateWhenOffscreen = true;
        Bounds b = smr.bounds;
        float height = Mathf.Clamp(b.max.y, 1.5f, 2.2f);

        var cc = player.AddComponent<CharacterController>();
        cc.height = height * 0.95f;
        cc.radius = 0.28f;
        cc.center = new Vector3(0f, cc.height * 0.5f + 0.02f, 0f);
        cc.skinWidth = 0.03f;
        cc.stepOffset = 0.3f;

        var pc = player.AddComponent<ProiPlayerController>();
        var so = new SerializedObject(pc);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("moveSpeed").floatValue = MoveSpeed;
        so.ApplyModifiedPropertiesWithoutUndo();

        var blink = model.AddComponent<ProiBlink>();
        so = new SerializedObject(blink);
        so.FindProperty("face").objectReferenceValue = smr;
        so.ApplyModifiedPropertiesWithoutUndo();
        model.AddComponent<ProiTailSpring>();
        model.AddComponent<ProiFist>();

        Camera cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<ProiQuarterViewCamera>();
            if (follow == null) follow = cam.gameObject.AddComponent<ProiQuarterViewCamera>();
            follow.Configure(player.transform);      // also places the camera at the quarter-view position
            cam.fieldOfView = 35f;
            EditorUtility.SetDirty(follow);
            so = new SerializedObject(pc);
            so.FindProperty("cameraTransform").objectReferenceValue = cam.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        Debug.Log($"[ProiSetup] Scene saved: {ScenePath}  height={height:F2} yaw={model.transform.localEulerAngles.y}");
    }

    static Material GroundMaterial()
    {
        string path = $"{MatDir}/M_Ground.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", new Color(0.4f, 0.62f, 0.6f));
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>어깨 위치로 캐릭터 정면을 구해, 정면이 +Z 가 되도록 90도 단위 회전각을 반환합니다.</summary>
    static float DetectModelYaw(GameObject model)
    {
        Transform l = FindDeep(model.transform, "LeftUpperArm");
        Transform r = FindDeep(model.transform, "RightUpperArm");
        if (l == null || r == null) return 0f;
        Vector3 right = Vector3.ProjectOnPlane(r.position - l.position, Vector3.up);
        Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
        float angle = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
        return Mathf.Round(angle / 90f) * 90f;
    }

    // ---------------------------------------------------------------- Preview capture

    static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var pc = Object.FindFirstObjectByType<ProiPlayerController>();
        GameObject model = pc.GetComponentInChildren<Animator>().gameObject;
        Camera cam = Camera.main;
        string outDir = "D:/Proi/previews";
        Directory.CreateDirectory(outDir);

        void Shot(string file, AnimationClip clip, float t, Vector3 camPos, Vector3 look, float fov, int w = 960, int h = 720)
        {
            clip.SampleAnimation(model, t);
            cam.transform.position = camPos;
            cam.transform.LookAt(look);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{outDir}/{file}", tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        Vector3 p = pc.transform.position;
        Vector3 gameCam = cam.transform.position;   // 저장된 게임 카메라 위치
        AnimationClip idle = LoadClip("Idle"), run = LoadClip("Run");
        Shot("unity_game_view_idle.png", idle, 0.5f, gameCam, p + Vector3.up * 0.9f, 35f);
        Shot("unity_game_view_run.png", run, 0.4f, gameCam, p + Vector3.up * 0.9f, 35f);
        Shot("unity_front_idle.png", idle, 0.5f, p + new Vector3(0.8f, 1.3f, 3.2f), p + Vector3.up * 1.0f, 40f, 720, 960);
        Shot("unity_side_run.png", run, 0.35f, p + new Vector3(3.4f, 1.1f, 0.4f), p + Vector3.up * 0.95f, 40f, 720, 960);
        Shot("unity_face.png", idle, 0.5f, p + new Vector3(0.15f, 1.78f, 0.9f), p + new Vector3(0f, 1.74f, 0f), 30f, 720, 720);

        // Animator 를 실제로 돌려 본 상태 (재생 화면과 같은 경로: Animator Controller -> Blend Tree)
        var anim = model.GetComponent<Animator>();
        anim.Rebind();
        anim.SetFloat("Speed", 0f);
        for (int i = 0; i < 60; i++) anim.Update(1f / 30f);
        void ShotNow(string file, Vector3 camPos, Vector3 look, float fov, int w, int h)
        {
            cam.transform.position = camPos; cam.transform.LookAt(look); cam.fieldOfView = fov;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes($"{outDir}/{file}", tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        ShotNow("unity_animator_idle.png", gameCam, p + Vector3.up * 1.1f, 60f, 960, 720);
        ShotNow("unity_animator_idle_close.png", p + new Vector3(0f, 1.2f, 2.6f), p + Vector3.up * 0.95f, 40f, 720, 960);
        anim.SetFloat("Speed", 1f);
        for (int i = 0; i < 40; i++) anim.Update(1f / 30f);
        ShotNow("unity_animator_run_close.png", p + new Vector3(0f, 1.2f, 2.6f), p + Vector3.up * 0.95f, 40f, 720, 960);
        Debug.Log("[ProiSetup] Captured previews to " + outDir);
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
