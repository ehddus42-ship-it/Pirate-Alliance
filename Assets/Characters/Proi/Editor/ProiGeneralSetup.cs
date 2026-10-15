using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 일상 프로이(Proi_General.fbx) 세팅: 텍스처 · Proi/Toon 머티리얼 · Generic 리그 · Idle(서있기) ↔ Run 컨트롤러,
/// ProiPlayground 씬의 Player 아래에 배치하고 F 키 전환(ProiCharacterSwitcher)을 붙입니다.
/// - Proi_General.fbx : Blender 에서 Proi_Armature(전투 프로이와 같은 리그)에 스키닝한 한 개의 메쉬(약 9.4만 삼각형), 클립 IdleStand / Run, 블렌드셰이프 Fist_L / Fist_R
/// 메뉴: Tools > Proi > Setup Daily Proi (F Switch)
/// </summary>
public static class ProiGeneralSetup
{
    const string Root = "Assets/Characters/Proi";
    const string Fbx = Root + "/Models/Proi_General.fbx";
    const string TexDir = Root + "/Textures/General";
    const string MatDir = Root + "/Materials/General";
    const string ControllerPath = Root + "/Animations/Proi_General.controller";
    const string ScenePath = Root + "/Scenes/ProiPlayground.unity";
    const string ToonShader = "Proi/Toon";
    const string ModelName = "Proi_General";
    // 전투 프로이와 같은 Run 클립: 이동 속도에 맞춰 재생 속도 조절 (ProiSetup 과 같은 값)
    const float RunClipGroundSpeed = 2.67f;
    const float DefaultMoveSpeed = 3.2f;

    // part -> (char 계열? , 헤어 하이라이트?)
    static readonly (string part, bool skin, bool hair)[] Parts =
    {
        ("Head", true, false), ("Hair", true, true), ("Body", true, false),
        ("Jacket", false, false), ("Pants", false, false), ("Shoes", false, false), ("Glasses", false, false),
    };

    [MenuItem("Tools/Proi/Setup Daily Proi (F Switch)")]
    public static void SetupFromMenu() => Setup();

    // Unity -batchmode -executeMethod ProiGeneralSetup.RunBatch
    public static void RunBatch() => RunAndExit(Setup);

    // Unity -batchmode -executeMethod ProiGeneralSetup.CaptureBatch   (미리보기 PNG 저장)
    public static void CaptureBatch() => RunAndExit(Capture);

    static void RunAndExit(System.Action action)
    {
        try { action(); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void Setup()
    {
        AssetDatabase.Refresh();
        ConfigureTextures();
        var mats = CreateMaterials();
        ConfigureModel(mats);
        AnimatorController controller = BuildController();
        PlaceInScene(controller);
        Debug.Log("[ProiGeneralSetup] Done.");
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
            ti.maxTextureSize = path.Contains("ProiG_Head_") ? 4096 : 2048;   // 얼굴(눈)만 4K
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }

    static System.Collections.Generic.Dictionary<string, Material> CreateMaterials()
    {
        Shader shader = Shader.Find(ToonShader);
        if (shader == null) throw new System.Exception($"Shader '{ToonShader}' not found (compile error?)");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root + "/Materials", "General");

        var result = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var (part, skin, hair) in Parts)
        {
            string path = $"{MatDir}/M_ProiG_{part}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/ProiG_{part}_Color.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/ProiG_{part}_Normal.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_BumpScale", 0.2f);
            mat.SetFloat("_AmbientStrength", 0.35f);
            mat.SetFloat("_ShadeThreshold", 0.45f);
            mat.SetFloat("_ShadeSoftness", 0.04f);
            mat.SetFloat("_RimStrength", 0.3f);
            // M_Proi_Char / M_Proi_Outfit 과 같은 값
            mat.SetColor("_ShadeColor", skin ? new Color(0.9f, 0.74f, 0.76f) : new Color(0.74f, 0.7f, 0.82f));
            mat.SetColor("_RimColor", skin ? new Color(1f, 0.9f, 0.9f) : new Color(0.9f, 0.88f, 1f));
            mat.SetColor("_OutlineColor", skin ? new Color(0.28f, 0.06f, 0.08f) : new Color(0.04f, 0.03f, 0.05f));
            mat.SetFloat("_OutlineWidth", part == "Glasses" ? 0.0006f : 0.0012f);
            mat.SetFloat("_OutlinePixels", part == "Glasses" ? 0.5f : 1f);
            mat.SetFloat("_OutlineDepthOffset", 0.003f);
            // 재킷: 양면 (긴 소매 입구 · 밑단 안쪽이 비어 보이지 않게)
            mat.SetFloat("_Cull", part == "Jacket" ? 0f : 2f);
            // 헤어 하이라이트: 헤어 머티리얼은 UV 전체가 머리카락
            mat.SetVector("_HairRectA", hair ? new Vector4(0f, 0f, 1f, 1f) : Vector4.zero);
            mat.SetVector("_HairRectB", Vector4.zero);
            mat.SetColor("_HairHighlightColor", new Color(1f, 0.8f, 0.8f));
            mat.SetFloat("_HairHighlightStrength", hair ? 0.35f : 0f);
            mat.SetFloat("_HairHighlightSize", 0.82f);
            EditorUtility.SetDirty(mat);
            result[part] = mat;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    // ---------------------------------------------------------------- Model

    static void ConfigureModel(System.Collections.Generic.Dictionary<string, Material> mats)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        if (mi == null) throw new System.Exception($"{Fbx} not found");
        mi.animationType = ModelImporterAnimationType.Generic;      // 전투 프로이와 같은 Generic 리그
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importBlendShapes = true;                                // Fist_L / Fist_R (손가락 구부림 · 주먹)
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.importVisibility = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        foreach (var kv in mats)
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_ProiG_" + kv.Key), kv.Value);

        var clips = mi.defaultClipAnimations
            .Select(c =>
            {
                c.name = c.takeName.Contains("IdleStand") ? "IdleStand" : c.takeName.Contains("Run") ? "Run" : c.takeName;
                c.loopTime = c.name == "IdleStand" || c.name == "Run";
                c.loopPose = false;
                return c;
            })
            .Where(c => c.name == "IdleStand" || c.name == "Run")
            .GroupBy(c => c.name).Select(g => g.First())
            .ToArray();
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        Debug.Log("[ProiGeneralSetup] Clips: " + string.Join(", ", clips.Select(c => $"{c.name}<-{c.takeName}")));
    }

    static AnimationClip LoadClip(string name)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
        if (clip == null) throw new System.Exception($"Clip '{name}' not found in {Fbx}");
        return clip;
    }

    // ---------------------------------------------------------------- Animator (Idle 서있기 ↔ Run)

    static AnimatorController BuildController(float moveSpeed = DefaultMoveSpeed)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        var locomotion = sm.AddState("Locomotion", new Vector3(300, 0));
        var tree = new BlendTree
        {
            name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed",
            useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(LoadClip("IdleStand"), 0f);
        tree.AddChild(LoadClip("Run"), 1f);
        ChildMotion[] children = tree.children;
        children[1].timeScale = moveSpeed / RunClipGroundSpeed;
        tree.children = children;
        locomotion.motion = tree;
        sm.defaultState = locomotion;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // ---------------------------------------------------------------- Scene

    static void PlaceInScene(AnimatorController controller)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var pc = Object.FindFirstObjectByType<ProiPlayerController>();
        if (pc == null) throw new System.Exception("ProiPlayerController (Player) not found in " + ScenePath);
        var pcSo = new SerializedObject(pc);
        var battle = pcSo.FindProperty("animator").objectReferenceValue as Animator;
        if (battle == null) battle = pc.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.name != ModelName);
        if (battle == null) throw new System.Exception("battle Proi Animator not found under Player");
        float moveSpeed = pcSo.FindProperty("moveSpeed").floatValue;
        if (Mathf.Abs(moveSpeed - DefaultMoveSpeed) > 0.01f) controller = BuildController(moveSpeed);

        // 다시 실행해도 하나만 있도록
        foreach (Transform t in pc.transform.Cast<Transform>().ToArray())
            if (t.name == ModelName) Object.DestroyImmediate(t.gameObject);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pc.transform);
        model.name = ModelName;
        model.transform.localPosition = battle.transform.localPosition;
        model.transform.localRotation = Quaternion.Euler(0f, DetectModelYaw(model), 0f);
        model.transform.localScale = battle.transform.localScale;

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
        }
        if (model.GetComponent<ProiGeneralHands>() == null) model.AddComponent<ProiGeneralHands>();
        model.SetActive(false);   // 시작은 전투 프로이, F 로 전환

        var sw = pc.GetComponent<ProiCharacterSwitcher>();
        if (sw == null) sw = pc.gameObject.AddComponent<ProiCharacterSwitcher>();
        var so = new SerializedObject(sw);
        so.FindProperty("battleModel").objectReferenceValue = battle;
        so.FindProperty("dailyModel").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ProiGeneralSetup] Placed '{ModelName}' under '{pc.name}' (yaw {model.transform.localEulerAngles.y}), battle='{battle.name}'");
    }

    /// <summary>어깨 위치로 캐릭터 정면을 구해, 정면이 +Z 가 되도록 90도 단위 회전각을 반환합니다. (ProiSetup 과 같은 방법)</summary>
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

    // ---------------------------------------------------------------- Preview capture

    static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var pc = Object.FindFirstObjectByType<ProiPlayerController>();
        var sw = pc.GetComponent<ProiCharacterSwitcher>();
        var so = new SerializedObject(sw);
        var daily = (Animator)so.FindProperty("dailyModel").objectReferenceValue;
        var battle = (Animator)so.FindProperty("battleModel").objectReferenceValue;
        string outDir = System.Environment.GetEnvironmentVariable("PROI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Temp/ProiGeneralPreviews");
        Directory.CreateDirectory(outDir);
        Camera cam = Camera.main;
        Vector3 p = pc.transform.position;

        // 배치 모드에는 플레이 루프가 없어 스키닝이 렌더마다 갱신되지 않으므로 강제로 다시 계산
        foreach (var smr in pc.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;

        void Shot(string file, Vector3 camPos, Vector3 look, float fov, int w, int h)
        {
            cam.transform.position = camPos; cam.transform.LookAt(look); cam.fieldOfView = fov;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes($"{outDir}/{file}", tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        Vector3 gameCam = cam.transform.position;
        Quaternion gameRot = cam.transform.rotation;

        foreach (var (anim, tag) in new[] { (battle, "battle"), (daily, "daily") })
        {
            battle.gameObject.SetActive(anim == battle);
            daily.gameObject.SetActive(anim == daily);
            anim.Rebind();
            anim.SetFloat("Speed", 0f);
            for (int i = 0; i < 45; i++) anim.Update(1f / 30f);
            var hands = anim.GetComponent<ProiGeneralHands>();
            if (hands != null) hands.Apply();
            Transform hand = FindDeep(anim.transform, "LeftHand");
            Vector3 hp = hand != null ? hand.position : p + Vector3.up;
            Shot($"{tag}_hand_idle.png", hp + new Vector3(-0.15f, 0.25f, 0.55f), hp, 30f, 720, 720);
            Shot($"{tag}_idle_game.png", gameCam, p + Vector3.up * 1.0f, 35f, 960, 720);
            Shot($"{tag}_idle_front.png", p + new Vector3(0.8f, 1.3f, 3.2f), p + Vector3.up * 1.0f, 40f, 720, 960);
            Shot($"{tag}_idle_left.png", p + new Vector3(-1.3f, 1.7f, 1.9f), p + Vector3.up * 1.25f, 35f, 720, 960);
            Shot($"{tag}_face.png", p + new Vector3(0.15f, 1.78f, 0.9f), p + new Vector3(0f, 1.72f, 0f), 30f, 720, 720);
            anim.SetFloat("Speed", 1f);
            for (int i = 0; i < 46; i++) anim.Update(1f / 30f);   // stride phase
            if (hands != null) hands.Apply();
            hp = hand != null ? hand.position : p + Vector3.up;
            Shot($"{tag}_hand_run.png", hp + new Vector3(0.45f, 0.2f, 0.45f), hp, 30f, 720, 720);
            Shot($"{tag}_run_side.png", p + new Vector3(3.4f, 1.1f, 0.4f), p + Vector3.up * 0.95f, 40f, 720, 960);
            Shot($"{tag}_run_game.png", gameCam, p + Vector3.up * 1.0f, 35f, 960, 720);
        }
        cam.transform.SetPositionAndRotation(gameCam, gameRot);
        Debug.Log("[ProiGeneralSetup] Captured previews to " + outDir);
    }
}
