// Unity 6 Editor utility. Run from Tools > Namgung Luna > Setup Magica Cloth (or batch: LunaMagicaSetup.BatchSetup).
// Builds NamgungLuna_Magica.prefab and a demo scene from the Toon prefab: Magica colliders fitted to the body,
// MeshCloth for hair / skirt / sleeves / collar ribbons with Fixed/Move paint data from <character folder>/Magica/*.json.
// Compiled only when Magica Cloth 2 is imported (it defines MAGICACLOTH2).
// The paint data is stored as user-edited SelectionData, so it can be refined with Magica's own paint tool.
#if UNITY_EDITOR && MAGICACLOTH2
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using NamgungLuna.Magica;
using NamgungLuna.Motion;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NamgungLuna.Editor
{
    public static class LunaMagicaSetup
    {
        public static string DataFolder => LunaAssetSetup.Root + "/Magica";
        public static string PrefabPath => DataFolder + "/NamgungLuna_Magica.prefab";
        public static string ScenePath => DataFolder + "/NamgungLuna_MagicaDemo.unity";
        const string PresetFolder = "Assets/MagicaCloth2/Res/Preset/";

        [Serializable] class Reference { public string name; public float[] center; }
        [Serializable] class ColliderSpec { public string name; public string type; public float[] center; public float[] start; public float[] end; public float radius; public float r0; public float r1; }
        [Serializable] class Specs { public Reference[] references; public ColliderSpec[] colliders; }
        [Serializable] class Selection { public string group; public string[] renderers; public float[] points; }

        /// <summary>Per-part simulation settings layered on top of a Magica preset.</summary>
        class ClothPlan
        {
            public string name, selection, preset;            // selection: selection_<name>.json with the Fixed/Move paint
            public string[] renderers;
            public float simpleReduction, shapeReduction;     // fraction of the cloth's bounding box
            public float particleRadius, maxDistance, gravityFalloff;
            public string mutualCollisionWith;                // Magica sync partner (outer skirt -> underskirt)
            public string[] colliders;
            public string[] followers;                        // small attached parts driven by LunaClothFollower
            public float movingWind;                          // wind from her own movement: hair and hems stream behind
        }

        static readonly string[] HairColliders = { "Head", "Neck", "Torso_L", "Torso_R", "Shoulders" };
        // The Leg_L / Leg_R specs become thigh, shin and foot capsules on the leg bones, sized to the lower body that
        // LunaLocomotionSetup builds, plus a hip capsule: the skirt rests on her hips and is kicked from inside by the legs.
        static readonly string[] LegColliders = { "Hips", "Leg_L", "Leg_R", "Shin_L", "Shin_R", "Foot_L", "Foot_R" };

        // Magica stalls when the renderers of one cloth add up to more than 65,535 vertices, so the hair and the two
        // skirt layers are separate cloths. The hair cloths share parameters and fixed roots so they move together.
        static readonly ClothPlan[] Plans =
        {
            new ClothPlan { name = "Hair_Front", selection = "Hair", preset = "FrontHair", renderers = new[] { "Hair_Front" },
                            simpleReduction = 0.035f, shapeReduction = 0.05f, particleRadius = 0.005f, maxDistance = 0.06f, gravityFalloff = 1.0f, colliders = HairColliders, movingWind = 1.5f },
            new ClothPlan { name = "Hair_Back_L", selection = "Hair", preset = "LongHair", renderers = new[] { "Hair_Back_L" },
                            simpleReduction = 0.025f, shapeReduction = 0.04f, particleRadius = 0.012f, maxDistance = 0.12f, gravityFalloff = 1.0f, colliders = HairColliders, movingWind = 2.0f },
            new ClothPlan { name = "Hair_Back_R", selection = "Hair", preset = "LongHair", renderers = new[] { "Hair_Back_R" },
                            simpleReduction = 0.025f, shapeReduction = 0.04f, particleRadius = 0.012f, maxDistance = 0.12f, gravityFalloff = 1.0f, colliders = HairColliders, movingWind = 2.0f },
            new ClothPlan { name = "Hair_Locks", selection = "Hair", preset = "LongHair",
                            renderers = Enumerable.Range(1, 7).Select(i => $"Hair_BackFill_Lock_{i:00}").ToArray(),
                            // locks collapse to a centre line in the proxy, so a wider particle keeps their edges off the body
                            simpleReduction = 0.035f, shapeReduction = 0.05f, particleRadius = 0.012f, maxDistance = 0.12f, gravityFalloff = 1.0f, colliders = HairColliders, movingWind = 1.2f },
            new ClothPlan { name = "Skirt_Ivory", selection = "Skirt", preset = "Skirt", renderers = new[] { "Skirt_Ivory" },
                            simpleReduction = 0.03f, shapeReduction = 0.045f, particleRadius = 0.01f, maxDistance = 0.3f, gravityFalloff = 1.0f, colliders = LegColliders, movingWind = 1.2f },
            // The gold hem pieces sit up to 1.5 cm off the skirt, too far for Magica's reduction to merge them reliably,
            // so they follow the simulated skirt surface instead of being cloth themselves.
            new ClothPlan { name = "Skirt_Navy", selection = "Skirt", preset = "Skirt", renderers = new[] { "Skirt_Navy" },
                            simpleReduction = 0.03f, shapeReduction = 0.045f, particleRadius = 0.01f, maxDistance = 0.3f, gravityFalloff = 1.0f, movingWind = 1.2f,
                            mutualCollisionWith = "Skirt_Ivory", colliders = LegColliders, followers = new[] { "Gold_Hem_Trim", "Gold_Hem_Charms_L", "Gold_Hem_Charms_R" } },
            new ClothPlan { name = "Sleeve_L", selection = "Sleeve_L", preset = "Skirt", renderers = new[] { "BellSleeve_L", "Sleeve_Band_L" },
                            simpleReduction = 0.04f, shapeReduction = 0.055f, particleRadius = 0.008f, maxDistance = 0.12f, gravityFalloff = 1.0f, colliders = new[] { "Forearm_L" }, movingWind = 1.5f },
            new ClothPlan { name = "Sleeve_R", selection = "Sleeve_R", preset = "Skirt", renderers = new[] { "BellSleeve_R", "Sleeve_Band_R" },
                            simpleReduction = 0.04f, shapeReduction = 0.055f, particleRadius = 0.008f, maxDistance = 0.12f, gravityFalloff = 1.0f, colliders = new[] { "Forearm_R" }, movingWind = 1.5f },
            new ClothPlan { name = "CollarRibbon", selection = "CollarRibbon", preset = "Accessory", renderers = new[] { "Collar_Ribbons_Ivory" },
                            simpleReduction = 0.03f, shapeReduction = 0.04f, particleRadius = 0.006f, maxDistance = 0.08f, gravityFalloff = 1.0f, colliders = new[] { "Neck", "Torso_L", "Torso_R" }, movingWind = 1.5f },
        };

        [MenuItem("Tools/Namgung Luna/Setup Magica Cloth")]
        public static void SetupMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report = Setup();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Namgung Luna", "Magica Cloth 설정 완료\n" + PrefabPath + "\n" + ScenePath, "OK");
        }

        [MenuItem("Tools/Namgung Luna/Setup Magica Cloth", true)]
        static bool CanSetup() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        /// <summary>Entry point for -batchmode -executeMethod.</summary>
        public static void BatchSetup()
        {
            try
            {
                Debug.Log(Setup());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static string Setup()
        {
            var report = new StringBuilder();
            report.AppendLine("[Namgung Luna] Magica Cloth setup " + DateTime.Now.ToString("u"));
            var specs = JsonUtility.FromJson<Specs>(ReadText(DataFolder + "/collider_specs.json"));

            GameObject toon = FindToonPrefab();
            if (toon == null)
            {
                LunaAssetSetup.CreateToonPrefabs();
                toon = FindToonPrefab();
            }
            if (toon == null) throw new InvalidOperationException("NamgungLuna_Toon 프리팹을 만들지 못했습니다. Console을 확인하세요.");
            report.AppendLine("Toon prefab: " + AssetDatabase.GetAssetPath(toon));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The FBX root carries the axis conversion (rotation) and unit scale (100); keep it as is under an
            // unscaled root so Magica components and colliders work in plain metres.
            var character = new GameObject("NamgungLuna_Magica");
            SceneManager.MoveGameObjectToScene(character, scene);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(toon, scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(character.transform, false);
            // Moon gait rig: Visual pivot (hover / lean), hidden leg chains carrying the boots, lantern and moon pivots.
            LunaLocomotionSetup.Rig rig = LunaLocomotionSetup.BuildRig(character, model);
            var renderers = character.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());

            Func<float[], Vector3> toUnity = FindAxisMapping(specs, renderers, report);

            // Colliders
            var colliderRoot = new GameObject("Magica_Colliders").transform;
            colliderRoot.SetParent(rig.visual, false);
            var colliders = new Dictionary<string, ColliderComponent>();
            foreach (var c in specs.colliders)
            {
                if (c.type != "sphere" && (c.name == "Leg_L" || c.name == "Leg_R"))
                {
                    AddLegColliders(c, c.name.Substring(4), rig, toUnity, colliders);
                    continue;
                }
                var go = new GameObject("Collider_" + c.name);
                go.transform.SetParent(colliderRoot, false);
                if (c.type == "sphere")
                {
                    go.transform.position = toUnity(c.center);
                    var sphere = go.AddComponent<MagicaSphereCollider>();
                    sphere.SetSize(c.radius);
                    colliders[c.name] = sphere;
                }
                else
                {
                    // Capsule along local +Y: sphere centres at start (r0) and end (r1).
                    Vector3 a = toUnity(c.start), b = toUnity(c.end);
                    Vector3 dir = (a - b).normalized;
                    float length = Vector3.Distance(a, b) + c.r0 + c.r1;
                    go.transform.SetPositionAndRotation(a - dir * (length * 0.5f - c.r0), Quaternion.FromToRotation(Vector3.up, dir));
                    var capsule = go.AddComponent<MagicaCapsuleCollider>();
                    capsule.direction = MagicaCapsuleCollider.Direction.Y;
                    capsule.alignedOnCenter = true;
                    capsule.SetSize(c.r0, c.r1, length);
                    colliders[c.name] = capsule;
                }
            }
            colliders["Hips"] = AddHipsCollider(rig, colliderRoot);
            report.AppendLine("Colliders: " + string.Join(", ", colliders.Keys));
            report.AppendLine("Rest clearance to the skirts (mm, + = gap): " + string.Join(", ", RestClearance(renderers, "Hips", "Leg_L", "Leg_R", "Shin_L", "Shin_R")));

            // Cloth
            var clothRoot = new GameObject("Magica_Cloth").transform;
            clothRoot.SetParent(rig.visual, false);
            var selections = new Dictionary<string, Selection>();
            var cloths = new Dictionary<string, MagicaCloth>();
            foreach (var plan in Plans)
            {
                if (!selections.TryGetValue(plan.selection, out Selection sel))
                    selections[plan.selection] = sel = JsonUtility.FromJson<Selection>(ReadText(DataFolder + "/selection_" + plan.selection + ".json"));
                var go = new GameObject("Cloth_" + plan.name);
                go.transform.SetParent(clothRoot, false);
                var cloth = go.AddComponent<MagicaCloth>();
                cloths[plan.name] = cloth;
                var sdata = cloth.SerializeData;
                var preset = AssetDatabase.LoadAssetAtPath<TextAsset>(PresetFolder + "MC2_Preset_" + plan.preset + ".json");
                if (preset == null || !sdata.ImportJson(preset.text)) throw new InvalidOperationException("Magica preset load failed: " + plan.preset);
                sdata.clothType = ClothProcess.ClothType.MeshCloth;
                sdata.sourceRenderers.Clear();
                int vertexTotal = 0;
                Bounds bounds = default;
                foreach (string n in plan.renderers)
                {
                    if (!renderers.TryGetValue(n, out Renderer r)) throw new InvalidOperationException("Renderer not found: " + n);
                    sdata.sourceRenderers.Add(r);
                    vertexTotal += r.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                    if (bounds.size == Vector3.zero) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                }
                if (vertexTotal > 65535) throw new InvalidOperationException($"Cloth_{plan.name}: {vertexTotal} vertices; Magica needs 65,535 or fewer per cloth.");
                bounds.Expand(0.04f);
                sdata.paintMode = ClothSerializeData.PaintMode.Manual;
                sdata.reductionSetting.simpleDistance = plan.simpleReduction;
                sdata.reductionSetting.shapeDistance = plan.shapeReduction;
                sdata.gravityFalloff = plan.gravityFalloff;
                // FrontHair/Accessory presets react to wind 5x more than the others; the split hair cloths must sway together.
                sdata.wind.influence = 0.2f;
                sdata.wind.movingWind = plan.movingWind;
                sdata.radius = new CurveSerializeData(plan.particleRadius);
                sdata.motionConstraint.useMaxDistance = true;
                sdata.motionConstraint.maxDistance = new CurveSerializeData(plan.maxDistance);
                // Edge collision also stops the coarse proxy's edges, not only its particles, from crossing a collider.
                sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
                sdata.colliderCollisionConstraint.colliderList = plan.colliders.Select(n => colliders[n]).ToList();
                if (plan.mutualCollisionWith != null)
                {
                    // Mutual collision keeps the two layers from passing through each other.
                    sdata.selfCollisionConstraint.syncMode = SelfCollisionConstraint.SelfCollisionMode.FullMesh;
                    sdata.selfCollisionConstraint.syncPartner = cloths[plan.mutualCollisionWith];
                    sdata.selfCollisionConstraint.surfaceThickness = new CurveSerializeData(0.006f, 0.5f, 1.0f, false);
                }
                sdata.DataValidate();

                // Paint points around this cloth's renderers, in the cloth component's local space.
                var positions = new List<float3>();
                var attributes = new List<MagicaCloth2.VertexAttribute>();
                int fixedCount = 0;
                for (int i = 0; i < sel.points.Length / 4; i++)
                {
                    Vector3 world = toUnity(new[] { sel.points[i * 4], sel.points[i * 4 + 1], sel.points[i * 4 + 2] });
                    if (!bounds.Contains(world)) continue;
                    bool isFixed = Mathf.RoundToInt(sel.points[i * 4 + 3]) == 1;
                    positions.Add(go.transform.InverseTransformPoint(world));
                    attributes.Add(isFixed ? MagicaCloth2.VertexAttribute.Fixed : MagicaCloth2.VertexAttribute.Move);
                    if (isFixed) fixedCount++;
                }
                if (plan.followers != null)
                {
                    var follower = go.AddComponent<LunaClothFollower>();
                    follower.target = (MeshRenderer)renderers[plan.renderers[0]];
                    follower.followers = plan.followers.Select(n => (MeshRenderer)renderers[n]).ToArray();
                }
                cloth.GetSerializeData2().selectionData = new SelectionData
                {
                    positions = positions.ToArray(), attributes = attributes.ToArray(), maxConnectionDistance = 0.006f, userEdit = true,
                };
                EditorUtility.SetDirty(cloth);
                report.AppendLine($"Cloth_{plan.name}: preset {plan.preset}, renderers {plan.renderers.Length} ({vertexTotal} vertices), paint points {positions.Count} (fixed {fixedCount}), " +
                                  $"colliders {string.Join("/", plan.colliders)}" + (plan.mutualCollisionWith != null ? $", mutual collision with Cloth_{plan.mutualCollisionWith}" : "") +
                                  (plan.followers != null ? $", followers {string.Join("/", plan.followers)}" : ""));
            }

            LunaLocomotionSetup.AttachGait(character, rig);
            report.AppendLine("Lower body: " + rig.lowerBodyReport);
            report.AppendLine($"Moon gait: Visual pivot, legs Hip/Knee/Ankle_L/R, sole outline {rig.left.solePoints.Length} points, " +
                              $"lantern {(rig.lantern != null ? "yes" : "no")}, moon {(rig.moon != null ? "yes" : "no")}");

            EnsureFolder(DataFolder);
            BakePerformance(character, DataFolder + "/PreBuild", report);
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(character, PrefabPath, InteractionMode.AutomatedAction, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("Prefab save failed: " + PrefabPath);

            // Cloth demo scene: camera, light, wind and whole-body motion (the prefab itself stays clean). The moon gait is
            // off here so the cloth validation keeps measuring the authored pose; NamgungLuna_MoonGaitDemo shows the gait.
            character.GetComponent<LunaMoonGait>().enabled = false;
            character.GetComponent<LunaMoonFx>().enabled = false;
            var motion = character.AddComponent<LunaMagicaDemoMotion>();
            motion.target = character.transform;
            var wind = new GameObject("Magica_Wind").AddComponent<MagicaWindZone>();
            wind.mode = MagicaWindZone.Mode.GlobalDirection;
            wind.main = 1.5f;
            wind.turbulence = 1.0f;
            wind.directionAngleY = 160.0f;
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.17f, 0.18f, 0.22f);
            cam.fieldOfView = 30f;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 4.2f), Quaternion.LookRotation(new Vector3(0f, -0.05f, -1f)));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.62f);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed: " + ScenePath);

            // Playable moon gait demo with a light breeze.
            LunaLocomotionSetup.BuildDemoScene(prefab, (gaitScene, luna) =>
            {
                var breeze = new GameObject("Magica_Wind").AddComponent<MagicaWindZone>();
                breeze.mode = MagicaWindZone.Mode.GlobalDirection;
                breeze.main = 0.8f;
                breeze.turbulence = 1.0f;
                breeze.directionAngleY = 160.0f;
            });

            report.AppendLine("Prefab: " + PrefabPath);
            report.AppendLine("Scene: " + ScenePath + " (LunaMagicaDemoMotion + MagicaWindZone; 재생하면 캐릭터 전체가 걷고 회전합니다)");
            report.AppendLine("Scene: " + LunaLocomotionSetup.ScenePath + " (콜롬비나식 이동: WASD / Shift 질주 / Space 점프 / Ctrl 걷기, 손을 떼면 자동 시연)");
            File.WriteAllText(DataFolder + "/MagicaSetupReport.txt", report.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(DataFolder + "/MagicaSetupReport.txt");
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        /// <summary>Rebuild only performance data; preserve the existing battle kit and authored rig.</summary>
        [MenuItem("Tools/Namgung Luna/Optimize Battle Cloth")]
        public static void OptimizeBattleCloth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            var report = new StringBuilder("Luna battle cloth performance setup\n");
            foreach (string path in new[] { PrefabPath, LunaStageSetup.BattlePrefab })
            {
                var character = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    report.AppendLine(path);
                    string folder = Path.GetDirectoryName(path).Replace('\\', '/') + "/PreBuild";
                    BakePerformance(character, folder, report);
                    PrefabUtility.SaveAsPrefabAsset(character, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(character); }
            }
            Directory.CreateDirectory("Documentation/NamgungLuna/Performance/Runtime");
            File.WriteAllText("Documentation/NamgungLuna/Performance/Runtime/setup.txt", report.ToString());
            AssetDatabase.SaveAssets(); Debug.Log(report.ToString());
        }

        public static void BatchOptimizeBattleCloth()
        {
            try { OptimizeBattleCloth(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BakePerformance(GameObject character, string folder, StringBuilder report = null)
        {
            EnsureFolder(folder);
            bool sync = Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            try
            {
                int total = 0;
                foreach (var cloth in character.GetComponentsInChildren<MagicaCloth>(true))
                {
                    var plan = Plans.Single(p => "Cloth_" + p.name == cloth.name);
                    var s = cloth.SerializeData;
                    s.reductionSetting.simpleDistance = plan.simpleReduction;
                    s.reductionSetting.shapeDistance = plan.shapeReduction;
                    s.cullingSettings.cameraCullingMode = CullingSettings.CameraCullingMode.Reset;
                    s.cullingSettings.cameraCullingMethod = CullingSettings.CameraCullingMethod.ManualRenderer;
                    s.cullingSettings.cameraCullingRenderers = s.sourceRenderers.ToList();
                    // The gameplay camera sits 15.5m away and can zoom out to 24m.
                    s.cullingSettings.distanceCullingLength = new CheckSliderSerializeData(true, 30);
                    s.cullingSettings.distanceCullingFadeRatio = .25f;
                    s.inertiaConstraint.teleportMode = InertiaConstraint.TeleportMode.Reset;
                    s.inertiaConstraint.teleportDistance = 2;
                    s.inertiaConstraint.teleportRotation = 100;
                    s.DataValidate();
                    var pre = cloth.GetSerializeData2().preBuildData;
                    pre.enabled = false;
                    var fresh = ScriptableObject.CreateInstance<PreBuildScriptableObject>();
                    pre.preBuildScriptableObject = fresh;
                    var result = PreBuildDataCreation.CreatePreBuildData(cloth, false);
                    if (result.IsError()) throw new InvalidOperationException(cloth.name + ": " + result.GetResultString());
                    string path = folder + "/" + plan.name + ".asset";
                    var saved = AssetDatabase.LoadAssetAtPath<PreBuildScriptableObject>(path);
                    if (saved) { EditorUtility.CopySerialized(fresh, saved); UnityEngine.Object.DestroyImmediate(fresh); }
                    else { saved = fresh; AssetDatabase.CreateAsset(saved, path); }
                    pre.preBuildScriptableObject = saved; pre.enabled = true;
                    EditorUtility.SetDirty(saved); EditorUtility.SetDirty(cloth);
                    if (pre.DataValidate().IsError()) throw new InvalidOperationException("Invalid prebuild " + cloth.name);
                    int particles = pre.GetSharePreBuildData().proxyMesh.attributes.count; total += particles;
                    report?.AppendLine($"{cloth.name}: {particles} particles, prebuild valid");
                }
                foreach (var follower in character.GetComponentsInChildren<LunaClothFollower>(true))
                {
                    var fresh = follower.BakeBinding();
                    string path = folder + "/" + follower.target.name + "_Attachments.asset";
                    var saved = AssetDatabase.LoadAssetAtPath<LunaClothBindingData>(path);
                    if (saved) { EditorUtility.CopySerialized(fresh, saved); UnityEngine.Object.DestroyImmediate(fresh); }
                    else { saved = fresh; AssetDatabase.CreateAsset(saved, path); }
                    follower.bindingData = saved;
                    EditorUtility.SetDirty(saved); EditorUtility.SetDirty(follower);
                    report?.AppendLine($"Attachments: {saved.anchors.Length} anchors, {saved.parts.Sum(p => p.rest.Length)} vertices baked");
                }
                report?.AppendLine($"Total: {total} particles; distance/offscreen culling enabled.");
            }
            finally { Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = sync; }
        }

        /// <summary>Thigh, shin and foot capsules on the leg bones in place of the single authored leg capsule, as thick as the
        /// built legs (the kneecap included) so the cloth particles stay outside their surface.</summary>
        static void AddLegColliders(ColliderSpec spec, string side, LunaLocomotionSetup.Rig rig, Func<float[], Vector3> toUnity, Dictionary<string, ColliderComponent> colliders)
        {
            LunaLeg leg = rig.Leg(side);
            LunaLocomotionSetup.LegFit fit = rig.Fit(side);
            Vector3 hip = leg.hip.position, knee = leg.knee.position, ankle = leg.ankle.position;
            colliders["Leg_" + side] = AddCapsule("Leg_" + side, leg.hip, hip, knee, fit.thighTop, Mathf.Max(fit.aboveKnee, fit.kneeMax));
            colliders["Shin_" + side] = AddCapsule("Shin_" + side, leg.knee, knee, ankle, Mathf.Max(fit.kneeMax, fit.calf), fit.ankle);
            colliders["Foot_" + side] = AddCapsule("Foot_" + side, leg.ankle, ankle, rig.Toe(side) + Vector3.up * 0.02f, spec.r1 * 0.9f, spec.r1 * 0.7f);
        }

        /// <summary>Side-to-side capsule through the widest part of her hips, inside the built pelvis.</summary>
        static MagicaCapsuleCollider AddHipsCollider(LunaLocomotionSetup.Rig rig, Transform parent)
        {
            float radius = Mathf.Min(rig.hipsDepth, rig.hipsHalfWidth) * 0.9f;
            float half = Mathf.Max(0.005f, rig.hipsHalfWidth * 0.95f - radius);
            return AddCapsule("Hips", parent, rig.hipsCenter + Vector3.left * half, rig.hipsCenter + Vector3.right * half, radius, radius);
        }

        /// <summary>Capsule along local +Y from <paramref name="a"/> (radius r0) to <paramref name="b"/> (radius r1), world positions.</summary>
        static readonly Dictionary<string, (Vector3 a, Vector3 b, float r0, float r1)> capsuleShapes = new Dictionary<string, (Vector3, Vector3, float, float)>();

        /// <summary>Smallest gap between the authored skirts and each capsule, before any simulation.</summary>
        static IEnumerable<string> RestClearance(Dictionary<string, Renderer> renderers, params string[] names)
        {
            var skirt = new[] { "Skirt_Ivory", "Skirt_Navy" }.Select(n => renderers[n]).SelectMany(r =>
            {
                Matrix4x4 m = r.transform.localToWorldMatrix;
                return r.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v));
            }).ToArray();
            foreach (string name in names)
            {
                if (!capsuleShapes.TryGetValue(name, out var c)) continue;
                float best = float.MaxValue;
                foreach (Vector3 p in skirt)
                {
                    Vector3 ab = c.b - c.a;
                    float t = Mathf.Clamp01(Vector3.Dot(p - c.a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector3.Distance(p, c.a + ab * t) - Mathf.Lerp(c.r0, c.r1, t));
                }
                yield return $"{name} {best * 1000f:F0}";
            }
        }

        static MagicaCapsuleCollider AddCapsule(string name, Transform parent, Vector3 a, Vector3 b, float r0, float r1)
        {
            capsuleShapes[name] = (a, b, r0, r1);
            var go = new GameObject("Collider_" + name);
            go.transform.SetParent(parent, false);
            Vector3 dir = (a - b).normalized;
            float length = Vector3.Distance(a, b) + r0 + r1;
            go.transform.SetPositionAndRotation(a - dir * (length * 0.5f - r0), Quaternion.FromToRotation(Vector3.up, dir));
            var capsule = go.AddComponent<MagicaCapsuleCollider>();
            capsule.direction = MagicaCapsuleCollider.Direction.Y;
            capsule.alignedOnCenter = true;
            capsule.SetSize(r0, r1, length);
            return capsule;
        }

        static GameObject FindToonPrefab()
        {
            string path = AssetDatabase.FindAssets("NamgungLuna_Toon t:Prefab", new[] { LunaAssetSetup.Root + "/Generated" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p).StartsWith("NamgungLuna_Toon", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal).LastOrDefault();
            return path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Pick the Blender -> Unity axis mapping that puts reference parts where their renderers are.</summary>
        static Func<float[], Vector3> FindAxisMapping(Specs specs, Dictionary<string, Renderer> renderers, StringBuilder report)
        {
            var candidates = new (string name, Func<float[], Vector3> map)[]
            {
                ("(-x, z, -y)", p => new Vector3(-p[0], p[2], -p[1])),
                ("(x, z, y)", p => new Vector3(p[0], p[2], p[1])),
                ("(x, z, -y)", p => new Vector3(p[0], p[2], -p[1])),
                ("(-x, z, y)", p => new Vector3(-p[0], p[2], p[1])),
            };
            float bestError = float.MaxValue;
            (string name, Func<float[], Vector3> map) best = candidates[0];
            foreach (var c in candidates)
            {
                float error = specs.references.Max(r => Vector3.Distance(c.map(r.center), renderers[r.name].bounds.center));
                if (error < bestError) { bestError = error; best = c; }
            }
            report.AppendLine($"Axis mapping Blender->Unity {best.name}, max reference error {bestError * 1000f:F1} mm");
            if (bestError > 0.01f)
            {
                var detail = new StringBuilder();
                foreach (var r in specs.references)
                {
                    var ren = renderers[r.name];
                    detail.AppendLine($"{r.name}: unity bounds {ren.bounds.center:F4} size {ren.bounds.size:F4}, blender ({r.center[0]}, {r.center[1]}, {r.center[2]}), lossyScale {ren.transform.lossyScale:F4}");
                }
                for (var t = renderers[specs.references[0].name].transform; t != null; t = t.parent)
                    detail.AppendLine($"  {t.name}: localPos {t.localPosition:F4} localRot {t.localEulerAngles:F2} localScale {t.localScale:F4}");
                throw new InvalidOperationException("Blender/Unity 좌표 매핑 오차가 큽니다: " + bestError + "\n" + detail);
            }
            return best.map;
        }

        static string ReadText(string assetPath)
        {
            if (!File.Exists(assetPath)) throw new FileNotFoundException(assetPath);
            return File.ReadAllText(assetPath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
