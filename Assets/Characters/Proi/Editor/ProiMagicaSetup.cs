#if MAGICACLOTH2
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AcRoguelike.Liminal;
using MagicaCloth2;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Proi 의 머리카락과 옷을 Magica Cloth 2 로 움직이는 프리팹 변형을 만들고 헌터 프로필이 그 프리팹을 쓰게 합니다.
/// 전투복 (Magica/Proi_Magica.prefab → Resources/Hunters/Proi.asset 의 model):
/// - Hair    : 긴 트윈테일. 두피와 묶은 곳은 고정, 뿌리부터 끝까지 같은 무게로 출렁이고 움직이는 반대쪽으로 날리며
///             머리 / 목 / 가슴 / 허리 / 팔 / 허벅지에 부딪힙니다.
/// - Tatters : 벨트에 매달린 찢어진 천 띠. 가볍게 펄럭이고 다리를 뚫지 않습니다.
/// - Pants   : 통 넓은 바지의 무릎 아래 풍선 부분 (무릎 스트랩 ~ 발목 커프). 넓은 천이 다리를 따라 크게 흔들리고 부풀며 날립니다.
/// 평시복 (Magica/Proi_General_Magica.prefab → Profiles/ProiEveryday.asset → Proi.asset 의 lobbyForm, 로비에서 입음):
/// - Hair    : 등 중간까지 오는 웨이브 생머리. 두피와 안경 앞 앞머리는 고정, 아래는 전투복 트윈테일과 같은 무게 · 규모로 출렁이고 날림.
/// - Hem     : 오버사이즈 후드 집업의 가슴 아래 옆 · 뒤 몸판 ~ 밑단 고무단. 조금 흔들리고 달리면 살짝 뒤로 날림 (지퍼 앞판 · 후드 · 소매 · 끈은 고정).
/// - Pants   : 통 넓은 트레이닝 바지의 허벅지 중간 ~ 신발 위 커프. 다리를 따라 흔들림.
/// 고정/이동 페인트와 콜라이더는 Magica/*MagicaData.json (Tools/Proi/proi_magica_data.py, proi_general_magica_data.py 로 생성)
/// 에서 읽고, 프리팹은 Magica PreBuild 로 미리 빌드해 스폰 순간의 자세와 상관없이 바로 동작합니다.
/// 메뉴: Tools > Proi > Setup Magica Cloth (Battle / Everyday)
/// 배치: Unity -batchmode -executeMethod ProiMagicaSetup.RunBatch (전투복) / ProiMagicaSetup.RunBatchEveryday (평시복)
/// </summary>
public static class ProiMagicaSetup
{
    const string Root = "Assets/Characters/Proi";
    const string ProfilePath = Root + "/Resources/Hunters/Proi.asset";
    const string PresetFolder = "Assets/MagicaCloth2/Res/Preset/";
    public const string PrefabPath = Root + "/Magica/Proi_Magica.prefab";
    public const string EverydayPrefabPath = Root + "/Magica/Proi_General_Magica.prefab";
    public const string EverydayProfilePath = Root + "/Profiles/ProiEveryday.asset";
    const string EverydayController = Root + "/Animations/Proi_General.controller";

    [Serializable] class Group { public string name; public float[] points; }
    [Serializable] class ColliderSpec { public string name, bone, type; public float[] start, end; public float r0, r1; }
    [Serializable] class Piece { public string group; public int count; public float[] centroid; }
    [Serializable] class MaterialSplit { public string group; public string[] materials; }
    [Serializable] class Data { public Group[] groups; public ColliderSpec[] colliders; public Piece[] split; public MaterialSplit[] split_materials; public float[] bounds_min, bounds_max; }

    /// <summary>One outfit: its model, data and outputs, the cloths, and what happens around the prefab save.</summary>
    sealed class Target
    {
        public string label, fbx, dataPath, preBuildFolder, meshBundlePath, prefabPath;
        public string prefix;                     // split renderers / meshes / PreBuild assets: {prefix}_Hair ...
        public string[] renderers;                // split renderers in hierarchy order; "Body" keeps the original renderer
        public Plan[] plans;
        public Action<GameObject, StringBuilder> beforeSave;   // on the model, before it becomes the prefab
        public Action<GameObject, StringBuilder> afterSave;    // with the saved prefab asset
    }

    /// <summary>Material-specific motion layered over a Magica preset.</summary>
    class Plan
    {
        public string group, preset, renderer;              // renderer: split piece the cloth simulates (Hair / Lower)
        public float simpleReduction, shapeReduction;   // fraction of the cloth's bounding box
        public float particleRadius, maxDistance, gravityFalloff, damping, angleRestoration, wind;
        public float restorationTip = 1f;               // restoration at the tips / at the root; 1 = the same all along
        public float angleLimit;                        // degrees, the same all along; 0 keeps the preset's
        public float movingWind;                        // m/s of head wind per m/s of movement (blows the cloth back)
        public float turbulence = 1f;
        public float particleSpeedLimit = 4f;           // m/s; the presets' 3 m/s drags long hair tips behind the root
        public float movementSpeedLimit = 5f;           // m/s of body movement the cloth still feels as inertia
        public string[] colliders;
    }

    static readonly Plan[] BattlePlans =
    {
        // long, thick twin tails: one even weight from the tie to the tips (no stiff root / floppy tip curves, no speed cap that
        // holds the tips back), streaming back from the way she moves; three quarters of the gravity stays on at rest and the
        // air slows the swing a little, so the hair hangs and swings heavily instead of floating up and fluttering in the wind
        // (5 m/s particle speed: the tips keep up with the root, yet a spin kick does not fling the hair through her head)
        new Plan { group = "Hair", preset = "LongHair", renderer = "Hair", simpleReduction = 0.012f, shapeReduction = 0.02f, particleRadius = 0.014f, maxDistance = 0.3f,
                   gravityFalloff = 0.25f, damping = 0.08f, angleRestoration = 0.2f, restorationTip = 1f, angleLimit = 60f, wind = 1f, movingWind = 0.7f, turbulence = 0.4f,
                   particleSpeedLimit = 5f, movementSpeedLimit = 12f,
                   colliders = new[] { "Head", "Neck", "Chest", "Spine", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm", "LeftUpperLeg", "RightUpperLeg" } },
        // thin torn strips: light, little air resistance, floppy, catch the wind
        new Plan { group = "Tatters", preset = "Cape", renderer = "Lower", simpleReduction = 0.01f, shapeReduction = 0.018f, particleRadius = 0.006f, maxDistance = 0.2f,
                   gravityFalloff = 0.5f, damping = 0.04f, angleRestoration = 0.08f, restorationTip = 0.3f, wind = 1f, movingWind = 0.8f,
                   particleSpeedLimit = 6f, movementSpeedLimit = 12f,
                   colliders = new[] { "Spine", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg" } },
        // wide balloon trousers: lots of loose cloth below the knee straps that swings after the legs, bellies out and blows
        // back; held at the knee straps and where the cuff gathers on the boot
        new Plan { group = "Pants", preset = "Skirt", renderer = "Lower", simpleReduction = 0.015f, shapeReduction = 0.025f, particleRadius = 0.01f, maxDistance = 0.12f,
                   gravityFalloff = 0.7f, damping = 0.06f, angleRestoration = 0.15f, restorationTip = 1f, wind = 0.6f, movingWind = 0.6f,
                   particleSpeedLimit = 5f, movementSpeedLimit = 10f,
                   colliders = new[] { "LeftLowerLeg", "RightLowerLeg" } },
    };

    static readonly Plan[] EverydayPlans =
    {
        // long loose waves down to the middle of the back: the same weight and reach as the battle twin tails (it is the same
        // hair, worn open), lying on the hood folded behind the neck, the shoulders and the back of the hoodie; a firmer shape
        // than the tails keeps the full, wide waves from collapsing or flipping over the shoulders when she stops, and a
        // stronger moving wind (it stops with her, unlike inertia) streams them back as far as the tails
        new Plan { group = "Hair", preset = "LongHair", renderer = "Hair", simpleReduction = 0.012f, shapeReduction = 0.02f, particleRadius = 0.014f, maxDistance = 0.3f,
                   gravityFalloff = 0.25f, damping = 0.08f, angleRestoration = 0.25f, restorationTip = 1f, angleLimit = 60f, wind = 1f, movingWind = 1.1f, turbulence = 0.4f,
                   particleSpeedLimit = 5f, movementSpeedLimit = 12f,
                   colliders = new[] { "Head", "Neck", "Hood", "BackLeft", "BackRight", "LeftShoulder", "RightShoulder", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm" } },
        // the loose sides and back of the oversized hoodie (the zipped front stays): soft fleece that sways a little after the
        // hips and blows back a touch when she runs; the ribbed band rests on the pants' waist (no leg colliders: the thighs
        // would sweep through the band)
        new Plan { group = "Hem", preset = "Skirt", renderer = "Jacket", simpleReduction = 0.012f, shapeReduction = 0.02f, particleRadius = 0.01f, maxDistance = 0.04f,
                   gravityFalloff = 0.8f, damping = 0.08f, angleRestoration = 0.4f, restorationTip = 1f, wind = 0.5f, movingWind = 0.25f,
                   particleSpeedLimit = 4f, movementSpeedLimit = 10f,
                   colliders = new[] { "Spine", "Waist" } },
        // wide track pants: the legs from mid-thigh down swing after each step and flap a little; the cuffs gathered on the
        // shoes follow the feet
        new Plan { group = "Pants", preset = "Skirt", renderer = "Pants", simpleReduction = 0.015f, shapeReduction = 0.025f, particleRadius = 0.01f, maxDistance = 0.07f,
                   gravityFalloff = 0.7f, damping = 0.07f, angleRestoration = 0.2f, restorationTip = 1f, wind = 0.5f, movingWind = 0.4f,
                   particleSpeedLimit = 5f, movementSpeedLimit = 10f,
                   colliders = new[] { "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg" } },
    };

    static readonly Target Battle = new Target
    {
        label = "battle", fbx = Root + "/Models/Proi_Character.fbx", dataPath = Root + "/Magica/ProiMagicaData.json",
        preBuildFolder = Root + "/Magica/PreBuild", meshBundlePath = Root + "/Magica/Proi_MagicaMeshes.asset", prefabPath = PrefabPath,
        prefix = "Proi", renderers = new[] { "Hair", "Lower", "Body" }, plans = BattlePlans,
        afterSave = (prefab, log) =>
        {
            SetProfileReference(prefab, "model", 3, log);
            log.AppendLine("Hunter profile model -> " + PrefabPath);
        },
    };

    static readonly Target Everyday = new Target
    {
        label = "everyday", fbx = Root + "/Models/Proi_General.fbx", dataPath = Root + "/Magica/ProiGeneralMagicaData.json",
        preBuildFolder = Root + "/Magica/PreBuild", meshBundlePath = Root + "/Magica/Proi_General_MagicaMeshes.asset", prefabPath = EverydayPrefabPath,
        prefix = "ProiG", renderers = new[] { "Hair", "Jacket", "Pants", "Body" }, plans = EverydayPlans,
        beforeSave = (model, log) =>
        {
            // the everyday body only moves: hands curl / make fists with the speed, the Run clip keeps pace with PlayerMotor
            model.AddComponent<ProiGeneralHands>();
            model.AddComponent<ProiGeneralHunterKit>();
            var animator = model.GetComponent<Animator>();
            if (animator) animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EverydayController);
        },
        afterSave = (prefab, log) =>
        {
            var profile = EverydayProfile(prefab);
            SetProfileReference(profile, "lobbyForm", 2, log);
            log.AppendLine($"Everyday profile {EverydayProfilePath} -> {EverydayPrefabPath}; Proi.asset lobbyForm -> {EverydayProfilePath}");
        },
    };

    [MenuItem("Tools/Proi/Setup Magica Cloth (Battle)")]
    public static void SetupFromMenu() => Debug.Log(Setup(Battle));

    [MenuItem("Tools/Proi/Setup Magica Cloth (Everyday)")]
    public static void SetupEverydayFromMenu() => Debug.Log(Setup(Everyday));

    public static void RunBatch() => RunAndExit(Battle);
    public static void RunBatchEveryday() => RunAndExit(Everyday);
    public static string Setup() => Setup(Battle);
    public static string SetupEveryday() => Setup(Everyday);

    static void RunAndExit(Target target)
    {
        try { Debug.Log(Setup(target)); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static string Setup(Target target)
    {
        var log = new StringBuilder($"[ProiMagicaSetup] {target.label}\n");
        Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;   // PreBuild runs Magica's jobs right away
        var importer = (ModelImporter)AssetImporter.GetAtPath(target.fbx);
        if (!importer.isReadable)
        {
            importer.isReadable = true;   // MeshCloth reads the mesh on the CPU
            importer.SaveAndReimport();
            log.AppendLine(Path.GetFileName(target.fbx) + ": Read/Write enabled");
        }
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(target.dataPath));
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(target.fbx);

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, scene);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = skin.sharedMesh;
            // The FBX's default pose is not its bind pose: map the bind-pose data into mesh space first.
            Vector3[] bindVerts = mesh.vertices;
            var baked = new Mesh();
            skin.BakeMesh(baked);
            Vector3[] posedVerts = baked.vertices;
            Func<float[], Vector3> toMesh = MeshSpaceMapping(data, mesh.bounds, out float meshScale, log);
            var nearest = new NearestVertex(bindVerts, 0.02f * meshScale);   // 2 cm cells
            // a paint point moves with its nearest vertex from the bind pose into the prefab's default pose; the bake is in the
            // renderer's space without its scale (everyday Proi's rig carries a x100 scale that the mesh units undo)
            Func<float[], Vector3> toPosedWorld = p =>
            {
                Vector3 q = toMesh(p);
                int i = nearest.Find(q);
                return skin.transform.position + skin.transform.rotation * posedVerts[i] + skin.transform.TransformVector(q - bindVerts[i]);
            };
            // Magica needs at most 65,535 vertices per renderer: the cloth pieces get their own renderers.
            var renderers = SplitRenderer(skin, data, toMesh, meshScale, target, log);

            var boneIndex = new Dictionary<string, int>();
            for (int i = 0; i < skin.bones.Length; i++) boneIndex[skin.bones[i].name] = i;
            var colliderRoot = new Dictionary<string, ColliderComponent>();
            foreach (var c in data.colliders)
            {
                // bindposes map mesh space to the bone's local space in the bind pose, so the collider fits in any pose
                int bi = boneIndex[c.bone];
                Matrix4x4 meshToBone = mesh.bindposes[bi];
                Vector3 a = meshToBone.MultiplyPoint3x4(toMesh(c.start)), b = meshToBone.MultiplyPoint3x4(toMesh(c.end));
                float unit = meshScale * meshToBone.MultiplyVector(Vector3.right).magnitude;   // metres -> bone-local units
                float r0 = c.r0 * unit, r1 = c.r1 * unit;
                var go = new GameObject("Magica_" + c.name);
                go.transform.SetParent(skin.bones[bi], false);
                if (c.type == "sphere")
                {
                    go.transform.localPosition = a;
                    var sphere = go.AddComponent<MagicaSphereCollider>();
                    sphere.SetSize(r0);
                    colliderRoot[c.name] = sphere;
                }
                else
                {
                    // capsule along local +Y with sphere centres at the bone's head (r0) and tail (r1)
                    Vector3 dir = (a - b).normalized;
                    float length = Vector3.Distance(a, b) + r0 + r1;
                    go.transform.localPosition = a - dir * (length * 0.5f - r0);
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                    var capsule = go.AddComponent<MagicaCapsuleCollider>();
                    capsule.direction = MagicaCapsuleCollider.Direction.Y;
                    capsule.alignedOnCenter = true;
                    capsule.SetSize(r0, r1, length);
                    colliderRoot[c.name] = capsule;
                }
            }
            log.AppendLine("Colliders: " + string.Join(", ", colliderRoot.Keys));

            var clothRoot = new GameObject("Magica_Cloth").transform;
            clothRoot.SetParent(model.transform, false);
            Directory.CreateDirectory(target.preBuildFolder);
            foreach (var plan in target.plans)
            {
                var group = data.groups.First(g => g.name == plan.group);
                var go = new GameObject("Cloth_" + plan.group);
                go.transform.SetParent(clothRoot, false);
                var cloth = go.AddComponent<MagicaCloth>();
                var sdata = cloth.SerializeData;
                var preset = AssetDatabase.LoadAssetAtPath<TextAsset>(PresetFolder + "MC2_Preset_" + plan.preset + ".json");
                if (preset == null || !sdata.ImportJson(preset.text)) throw new InvalidOperationException("Magica preset: " + plan.preset);
                sdata.clothType = ClothProcess.ClothType.MeshCloth;
                sdata.sourceRenderers = new List<Renderer> { renderers[plan.renderer] };
                sdata.paintMode = ClothSerializeData.PaintMode.Manual;
                sdata.reductionSetting.simpleDistance = plan.simpleReduction;
                sdata.reductionSetting.shapeDistance = plan.shapeReduction;
                sdata.radius = new CurveSerializeData(plan.particleRadius);
                sdata.gravityFalloff = plan.gravityFalloff;
                sdata.damping = new CurveSerializeData(plan.damping);
                sdata.angleRestorationConstraint.stiffness = plan.restorationTip >= 1f
                    ? new CurveSerializeData(plan.angleRestoration)
                    : new CurveSerializeData(plan.angleRestoration, 1f, plan.restorationTip);
                if (plan.angleLimit > 0f)
                {
                    sdata.angleLimitConstraint.useAngleLimit = true;
                    sdata.angleLimitConstraint.limitAngle = new CurveSerializeData(plan.angleLimit);
                }
                sdata.wind.influence = plan.wind;
                sdata.wind.movingWind = plan.movingWind;
                sdata.wind.turbulence = plan.turbulence;
                sdata.wind.depthWeight = 0f;   // the same push at the root and the tip
                var inertia = sdata.inertiaConstraint;
                inertia.particleSpeedLimit = new CheckSliderSerializeData(true, plan.particleSpeedLimit);
                inertia.movementSpeedLimit = new CheckSliderSerializeData(true, plan.movementSpeedLimit);
                inertia.localMovementSpeedLimit = new CheckSliderSerializeData(true, plan.movementSpeedLimit);
                inertia.localRotationSpeedLimit = new CheckSliderSerializeData(true, 720f);
                inertia.depthInertia = 0f;
                sdata.motionConstraint.useMaxDistance = true;
                sdata.motionConstraint.maxDistance = new CurveSerializeData(plan.maxDistance);
                sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
                sdata.colliderCollisionConstraint.colliderList = plan.colliders.Select(n => colliderRoot[n]).ToList();
                sdata.DataValidate();

                int count = group.points.Length / 4, fixedCount = 0;
                var selection = new SelectionData(count) { maxConnectionDistance = 0.006f, userEdit = true };
                for (int i = 0; i < count; i++)
                {
                    Vector3 world = toPosedWorld(new[] { group.points[i * 4], group.points[i * 4 + 1], group.points[i * 4 + 2] });
                    selection.positions[i] = (float3)go.transform.InverseTransformPoint(world);
                    bool isFixed = Mathf.RoundToInt(group.points[i * 4 + 3]) == 1;
                    selection.attributes[i] = isFixed ? MagicaCloth2.VertexAttribute.Fixed : MagicaCloth2.VertexAttribute.Move;
                    if (isFixed) fixedCount++;
                }
                cloth.GetSerializeData2().selectionData = selection;

                // PreBuild: bake the proxy in the bind pose so spawning mid-animation cannot shift the paint
                string assetPath = $"{target.preBuildFolder}/MagicaPreBuild_{target.prefix}_{plan.group}.asset";
                // a fresh asset each run: Magica appends one entry per build id, so reusing it would keep stale builds
                AssetDatabase.DeleteAsset(assetPath);
                var asset = ScriptableObject.CreateInstance<PreBuildScriptableObject>();
                var preBuild = cloth.GetSerializeData2().preBuildData;
                preBuild.preBuildScriptableObject = asset;
                ResultCode result = PreBuildDataCreation.CreatePreBuildData(cloth, false);
                if (result.IsError()) throw new InvalidOperationException($"Magica PreBuild {plan.group}: {result.GetResultString()}");
                // the asset file is written only now, with the data in it: in an open editor the AssetDatabase.Refresh() inside
                // CreatePreBuildData reloads a freshly created, still empty asset file over the data
                AssetDatabase.CreateAsset(asset, assetPath);
                if (asset.sharePreBuildDataList.Count == 0 || new FileInfo(assetPath).Length < 4096)
                    throw new InvalidOperationException($"Magica PreBuild {plan.group}: {assetPath} was not written");
                preBuild.enabled = true;
                log.AppendLine($"Cloth_{plan.group}: preset {plan.preset}, paint points {count} (fixed {fixedCount}), colliders {string.Join("/", plan.colliders)}, prebuild {assetPath}");
                log.AppendLine($"  restoration {plan.angleRestoration} (tip x{plan.restorationTip}), damping {plan.damping}, max distance {plan.maxDistance} m, " +
                               $"moving wind {plan.movingWind}, particle speed limit {plan.particleSpeedLimit} m/s");
            }

            target.beforeSave?.Invoke(model, log);
            var prefab = PrefabUtility.SaveAsPrefabAsset(model, target.prefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Prefab save failed: " + target.prefabPath);
            log.AppendLine("Prefab variant: " + target.prefabPath);
            target.afterSave?.Invoke(prefab, log);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return log.ToString();
    }

    /// <summary>
    /// Splits the model's one renderer into Body (keeps the original renderer, its blend shapes and its place first in the
    /// hierarchy so ProiBlink / ProiFist / ProiGeneralHands still find it) and the target's cloth renderers. A cloth piece is
    /// either every triangle of the listed materials (data.split_materials) or a connected island matched with the Blender
    /// data (data.split). Same bones, bind poses, weights, UVs and materials.
    /// </summary>
    static Dictionary<string, SkinnedMeshRenderer> SplitRenderer(SkinnedMeshRenderer skin, Data data, Func<float[], Vector3> toMesh, float meshScale,
                                                                 Target target, StringBuilder log)
    {
        Mesh src = skin.sharedMesh;
        var triangles = Enumerable.Range(0, src.subMeshCount).Select(sm => src.GetTriangles(sm)).ToArray();
        var materials = skin.sharedMaterials;
        Func<int, int, string> groupOf = data.split_materials != null && data.split_materials.Length > 0
            ? MaterialGroups(materials, data.split_materials)
            : IslandGroups(src, triangles, data, toMesh, meshScale);

        var bundle = AssetDatabase.LoadAssetAtPath<ProiMeshBundle>(target.meshBundlePath);
        if (bundle == null)
        {
            bundle = ScriptableObject.CreateInstance<ProiMeshBundle>();
            AssetDatabase.CreateAsset(bundle, target.meshBundlePath);
        }
        var result = new Dictionary<string, SkinnedMeshRenderer>();
        foreach (string name in target.renderers)
        {
            var keep = triangles.Select((tris, sm) => Enumerable.Range(0, tris.Length / 3).Where(t => groupOf(sm, tris[t * 3]) == name)
                                                                .SelectMany(t => new[] { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] }).ToArray()).ToArray();
            int[] used = Enumerable.Range(0, keep.Length).Where(sm => keep[sm].Length > 0).ToArray();
            string rendererName = target.prefix + "_" + name;
            Mesh mesh = Subset(src, used.Select(sm => keep[sm]).ToArray(), rendererName + "_Magica", keepAllBlendShapes: name == "Body", blendShapeReach: 0.02f * meshScale);
            if (name != "Body" && mesh.vertexCount > 65535) throw new InvalidOperationException($"{rendererName}: {mesh.vertexCount} vertices (> 65,535)");
            mesh = SaveMesh(mesh, bundle, target.meshBundlePath);
            var mats = used.Select(sm => materials[Mathf.Min(sm, materials.Length - 1)]).ToArray();
            SkinnedMeshRenderer renderer = skin;
            if (name != "Body")
            {
                var go = new GameObject(rendererName);
                go.layer = skin.gameObject.layer;
                go.transform.SetParent(skin.transform.parent, false);
                go.transform.localPosition = skin.transform.localPosition;
                go.transform.localRotation = skin.transform.localRotation;
                go.transform.localScale = skin.transform.localScale;
                renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = skin.bones;
                renderer.rootBone = skin.rootBone;
                renderer.quality = skin.quality;
                renderer.updateWhenOffscreen = skin.updateWhenOffscreen;
                renderer.shadowCastingMode = skin.shadowCastingMode;
                renderer.receiveShadows = skin.receiveShadows;
                renderer.localBounds = skin.localBounds;
            }
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = mats;
            result[name] = renderer;
            log.AppendLine($"Split {rendererName}: {mesh.vertexCount} vertices, {mesh.blendShapeCount} blend shapes, materials {string.Join("/", mats.Select(m => m ? m.name : "none"))}");
        }
        return result;
    }

    /// <summary>(submesh, vertex) -> group: every triangle of a listed material belongs to its group, the rest to Body.</summary>
    static Func<int, int, string> MaterialGroups(Material[] materials, MaterialSplit[] split)
    {
        var bySubmesh = materials.Select(m => split.FirstOrDefault(s => m && s.materials.Contains(m.name))?.group ?? "Body").ToArray();
        foreach (var s in split)
            if (!bySubmesh.Contains(s.group)) throw new InvalidOperationException($"No material of {s.group} ({string.Join("/", s.materials)}) on the model.");
        return (sm, vertex) => bySubmesh[Mathf.Min(sm, bySubmesh.Length - 1)];
    }

    /// <summary>(submesh, vertex) -> group: the connected island matched with a Blender piece by size and centroid, else Body.</summary>
    static Func<int, int, string> IslandGroups(Mesh src, int[][] triangles, Data data, Func<float[], Vector3> toMesh, float meshScale)
    {
        Vector3[] v = src.vertices;
        int n = v.Length;
        // weld by position: Unity splits FBX control points at UV seams
        var weldIds = new Dictionary<Vector3Int, int>();
        int[] weld = new int[n];
        for (int i = 0; i < n; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] / (1e-5f * meshScale));
            if (!weldIds.TryGetValue(key, out int id)) weldIds[key] = id = weldIds.Count;
            weld[i] = id;
        }
        int[] parent = Enumerable.Range(0, weldIds.Count).ToArray();
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        foreach (var tris in triangles)
            for (int t = 0; t < tris.Length; t += 3)
                for (int k = 1; k < 3; k++)
                {
                    int a = Find(weld[tris[t]]), b = Find(weld[tris[t + k]]);
                    if (a != b) parent[b] = a;
                }
        // piece = connected component; compare with the Blender islands by size and centroid
        var pieceCount = new Dictionary<int, int>();
        var pieceSum = new Dictionary<int, Vector3>();
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            if (!seen.Add(weld[i])) continue;
            int r = Find(weld[i]);
            pieceCount[r] = pieceCount.TryGetValue(r, out int c) ? c + 1 : 1;
            pieceSum[r] = (pieceSum.TryGetValue(r, out Vector3 sum) ? sum : Vector3.zero) + v[i];
        }
        var group = new Dictionary<int, string>();
        foreach (var piece in data.split)
        {
            Vector3 centroid = toMesh(piece.centroid);
            int match = pieceCount.Keys.Where(r => Mathf.Abs(pieceCount[r] - piece.count) <= piece.count * 0.02f)
                                       .OrderBy(r => Vector3.Distance(pieceSum[r] / pieceCount[r], centroid)).DefaultIfEmpty(-1).First();
            if (match < 0 || Vector3.Distance(pieceSum[match] / pieceCount[match], centroid) > 0.01f * meshScale || group.ContainsKey(match))
                throw new InvalidOperationException($"Proi mesh piece not found: {piece.group} ({piece.count} vertices). Largest pieces: " +
                    string.Join(", ", pieceCount.Keys.OrderByDescending(r => pieceCount[r]).Take(8).Select(r => $"{pieceCount[r]} @ {Vector3.Distance(pieceSum[r] / pieceCount[r], centroid) / meshScale:F3} m")));
            group[match] = piece.group;
        }
        return (sm, vertex) => group.TryGetValue(Find(weld[vertex]), out string g) ? g : "Body";
    }

    /// <summary>A mesh holding only the given triangles (per submesh), with every vertex channel, bone weights and blend shapes.</summary>
    static Mesh Subset(Mesh src, int[][] submeshTriangles, string name, bool keepAllBlendShapes, float blendShapeReach)
    {
        int n = src.vertexCount;
        Vector3[] positions = src.vertices;
        int[] remap = Enumerable.Repeat(-1, n).ToArray();
        var order = new List<int>();
        foreach (var tris in submeshTriangles)
            foreach (int i in tris)
                if (remap[i] < 0) { remap[i] = order.Count; order.Add(i); }
        var mesh = new Mesh { name = name, indexFormat = order.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        T[] Pick<T>(T[] a) => order.Select(i => a[i]).ToArray();
        mesh.vertices = Pick(positions);
        if (src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) mesh.normals = Pick(src.normals);
        if (src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.tangents = Pick(src.tangents);
        if (src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color)) mesh.colors32 = Pick(src.colors32);
        for (int ch = 0; ch < 8; ch++)
        {
            var attr = UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch;
            if (!src.HasVertexAttribute(attr)) continue;
            var uv = new List<Vector4>();
            src.GetUVs(ch, uv);
            var picked = order.Select(i => uv[i]).ToList();
            int dim = src.GetVertexAttributeDimension(attr);
            if (dim == 2) mesh.SetUVs(ch, picked.Select(x => (Vector2)x).ToList());
            else if (dim == 3) mesh.SetUVs(ch, picked.Select(x => (Vector3)x).ToList());
            else mesh.SetUVs(ch, picked);
        }
        // bone weights (variable count per vertex)
        var perVertex = src.GetBonesPerVertex();
        var weights = src.GetAllBoneWeights();
        int[] start = new int[n];
        for (int i = 1; i < n; i++) start[i] = start[i - 1] + perVertex[i - 1];
        var newPerVertex = new Unity.Collections.NativeArray<byte>(order.Count, Unity.Collections.Allocator.Temp);
        var newWeights = new List<BoneWeight1>();
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            newPerVertex[k] = perVertex[i];
            for (int j = 0; j < perVertex[i]; j++) newWeights.Add(weights[start[i] + j]);
        }
        var weightArray = new Unity.Collections.NativeArray<BoneWeight1>(newWeights.ToArray(), Unity.Collections.Allocator.Temp);
        mesh.SetBoneWeights(newPerVertex, weightArray);
        newPerVertex.Dispose();
        weightArray.Dispose();
        mesh.bindposes = src.bindposes;
        mesh.subMeshCount = submeshTriangles.Length;
        for (int sm = 0; sm < submeshTriangles.Length; sm++) mesh.SetTriangles(submeshTriangles[sm].Select(i => remap[i]).ToArray(), sm);
        // blend shapes: all of them on the body (scripts address them by name); elsewhere only the ones that move these vertices
        var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
        for (int s = 0; s < src.blendShapeCount; s++)
        {
            var frames = new List<(float w, Vector3[] v, Vector3[] n, Vector3[] t)>();
            bool moves = false;
            for (int f = 0; f < src.GetBlendShapeFrameCount(s); f++)
            {
                src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                // The importer leaves normal / tangent deltas on every vertex of the body; keep them only where the shape
                // moves vertices (and 2 cm around), so the blend shapes stay sparse.
                var movedCells = new HashSet<Vector3Int>();
                for (int i = 0; i < n; i++) if (dv[i].sqrMagnitude > 1e-12f) movedCells.Add(Vector3Int.FloorToInt(positions[i] / blendShapeReach));
                for (int i = 0; i < n; i++)
                {
                    if (dv[i].sqrMagnitude > 1e-12f) continue;
                    var c = Vector3Int.FloorToInt(positions[i] / blendShapeReach);
                    bool near = false;
                    for (int x = -1; x <= 1 && !near; x++)
                        for (int y = -1; y <= 1 && !near; y++)
                            for (int z = -1; z <= 1 && !near; z++)
                                near = movedCells.Contains(c + new Vector3Int(x, y, z));
                    if (!near) { dv[i] = Vector3.zero; dn[i] = Vector3.zero; dt[i] = Vector3.zero; }
                }
                var pv = Pick(dv);
                moves |= pv.Any(x => x.sqrMagnitude > 1e-12f);
                frames.Add((src.GetBlendShapeFrameWeight(s, f), pv, Pick(dn), Pick(dt)));
            }
            if (!keepAllBlendShapes && !moves) continue;
            foreach (var f in frames) mesh.AddBlendShapeFrame(src.GetBlendShapeName(s), f.w, f.v, f.n, f.t);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Stores the mesh in the binary bundle, reusing the sub-asset of the same name on a re-run.</summary>
    static Mesh SaveMesh(Mesh mesh, ProiMeshBundle bundle, string bundlePath)
    {
        var existing = AssetDatabase.LoadAllAssetsAtPath(bundlePath).OfType<Mesh>().FirstOrDefault(m => m.name == mesh.name);
        if (existing == null) { AssetDatabase.AddObjectToAsset(mesh, bundle); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        return existing;
    }

    /// <summary>
    /// Points one reference field of Proi.asset at the asset. Edits that one YAML line (adding it when missing): saving
    /// through SerializedObject would write every field and freeze the constructor defaults ProiHunterProfile sets in code.
    /// </summary>
    static void SetProfileReference(UnityEngine.Object asset, string field, int type, StringBuilder log)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId);
        string line = $"  {field}: {{fileID: {fileId}, guid: {guid}, type: {type}}}";
        string yaml = File.ReadAllText(ProfilePath);
        var pattern = new System.Text.RegularExpressions.Regex($@"(?m)^  {field}: \{{[^}}]*\}}");
        string updated = pattern.IsMatch(yaml) ? pattern.Replace(yaml, line) : yaml.TrimEnd('\n') + "\n" + line + "\n";
        if (updated != yaml)
        {
            File.WriteAllText(ProfilePath, updated);
            AssetDatabase.ImportAsset(ProfilePath);
        }
    }

    /// <summary>
    /// The everyday outfit as its own movement-only profile (kept out of Resources/Hunters, so it is not another pick in
    /// the roster): Proi wears it in the lobby (HunterProfile.lobbyForm), G switches it in and out for testing.
    /// </summary>
    static HunterProfile EverydayProfile(GameObject prefab)
    {
        var profile = AssetDatabase.LoadAssetAtPath<HunterProfile>(EverydayProfilePath);
        if (profile == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EverydayProfilePath));
            profile = ScriptableObject.CreateInstance<HunterProfile>();
            AssetDatabase.CreateAsset(profile, EverydayProfilePath);
        }
        profile.characterId = "proi_everyday";
        profile.displayName = "프로이 · 평시";
        profile.order = 10;
        profile.summary = "후드 집업 평시 차림\n이동만 · 소심한 달리기";
        profile.portraitResource = "Loadout/hunter_proi";
        profile.model = prefab;
        profile.controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EverydayController);
        profile.lobbyForm = null;
        profile.movementOnly = true;
        profile.keepDash = false;
        profile.headBone = "Head";
        profile.attackDurations = Array.Empty<float>();
        profile.hitTimes = Array.Empty<float>();
        profile.swings = Array.Empty<AcRoguelike.MeleeSlash.Swing>();
        profile.blade = false;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    /// <summary>Blender bind-pose coordinates -> mesh space: the axis permutation, scale and offset that make the bounds agree.</summary>
    static Func<float[], Vector3> MeshSpaceMapping(Data data, Bounds meshBounds, out float scale, StringBuilder log)
    {
        // Blender is right-handed and Unity left-handed, so the mapping is a reflection; the left/right-symmetric body
        // would otherwise leave the sign of x undecided by the bounds alone.
        var candidates = new (string name, Func<Vector3, Vector3> perm)[]
        {
            ("(-x, z, -y)", p => new Vector3(-p.x, p.z, -p.y)),
            ("(x, z, y)", p => new Vector3(p.x, p.z, p.y)),
            ("(-x, y, z)", p => new Vector3(-p.x, p.y, p.z)),
            ("(x, -y, z)", p => new Vector3(p.x, -p.y, p.z)),
        };
        Vector3 bmin = new Vector3(data.bounds_min[0], data.bounds_min[1], data.bounds_min[2]);
        Vector3 bmax = new Vector3(data.bounds_max[0], data.bounds_max[1], data.bounds_max[2]);
        (string name, Func<Vector3, Vector3> perm, float s, Vector3 offset, float error) best = default;
        best.error = float.MaxValue;
        foreach (var c in candidates)
        {
            Vector3 lo = Vector3.Min(c.perm(bmin), c.perm(bmax)), hi = Vector3.Max(c.perm(bmin), c.perm(bmax));
            Vector3 size = hi - lo;
            float s = (meshBounds.size.x / size.x + meshBounds.size.y / size.y + meshBounds.size.z / size.z) / 3f;
            float error = (meshBounds.size - size * s).magnitude / meshBounds.size.magnitude;
            if (error < best.error) best = (c.name, c.perm, s, meshBounds.center - (lo + hi) * 0.5f * s, error);
        }
        log.AppendLine($"Mesh-space mapping {best.name} x{best.s:F4}, bounds error {best.error * 100f:F2}%");
        if (best.error > 0.01f) throw new InvalidOperationException("ProiMagicaData.json does not match Proi_Character.fbx (bounds error " + best.error + ").");
        scale = best.s;
        var b = best;
        return p => b.perm(new Vector3(p[0], p[1], p[2])) * b.s + b.offset;
    }

    /// <summary>Uniform-grid nearest-vertex lookup.</summary>
    sealed class NearestVertex
    {
        readonly Vector3[] points;
        readonly float cell;
        readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();

        public NearestVertex(Vector3[] points, float cell)
        {
            this.points = points; this.cell = cell;
            for (int i = 0; i < points.Length; i++)
            {
                var key = Vector3Int.FloorToInt(points[i] / cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }
        }

        public int Find(Vector3 q)
        {
            var key = Vector3Int.FloorToInt(q / cell);
            int best = -1; float bestD = float.MaxValue;
            for (int r = 1; best < 0 && r <= 8; r++)
                for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                    if (grid.TryGetValue(key + new Vector3Int(x, y, z), out var list))
                        foreach (int i in list)
                        {
                            float d = (points[i] - q).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
            if (best < 0) throw new InvalidOperationException("No mesh vertex near a paint point: " + q);
            return best;
        }
    }
}
#endif
