using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.Ruins.Editor
{
    /// <summary>Builds independent Meshy creatures; office-dungeon examples are not an art specification.</summary>
    public static class RuinsMonsterBuilder
    {
        public const string Root = "Assets/RuinsMonsters";
        public const string Prefabs = Root + "/Resources/RuinsMonsters";
        const string Generated = Root + "/Art/Generated";
        static readonly string[] Keys = { "scrap_bulwark", "penitent_husk", "carrion_drone", "ossuary_medusa", "mourning_matron" };
        static readonly string[] Names = { "고철 방벽", "속죄의 잔해", "사체 수색기", "납골 해파리", "애곡의 모체" };

        [MenuItem("AC Roguelike/Ruins/Build Apocalypse Monsters")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring.");
            Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureMotionImports();
            for (int i = 0; i < Keys.Length; i++) Build((RuinsMonsterKind)i);
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            var mission = catalog ? catalog.Missions.FirstOrDefault(m => m != null && m.id == "ruins_clearance") : null;
            if (mission == null) throw new InvalidOperationException("G-04 Ruins mission is missing.");
            mission.gimmickName = "강화장갑 · 기계 군집";
            mission.gimmickDescription = "적이 받는 피해 25% 감소. 철편포 · 기계팔 돌진 · 드론 탄막 · 촉수 방전 · 모체의 긴 손톱. 예고를 피하고 회수 동작을 노려.";
            EditorUtility.SetDirty(catalog);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AcRoguelike.RuinsBoss.Editor.RuinsBossBuilder.ArenaPath))
                AcRoguelike.RuinsBoss.Editor.RuinsBossBuilder.ConnectStage();
            AssetDatabase.SaveAssets();
            Debug.Log("RUINS_MONSTERS_BUILD_OK: five Meshy creatures, skeletal motion and procedural machinery.");
        }

        [MenuItem("AC Roguelike/Ruins/Build Downloaded Models For Review")]
        public static void BuildAvailable()
        {
            Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < Keys.Length; i++)
            {
                string prefix = Root + "/Art/Meshy/" + Keys[i] + "/" + Keys[i];
                bool humanoid = i == 1 || i == 4;
                if (!File.Exists(prefix + ".glb") || (humanoid && !File.Exists(prefix + "_motions.fbx"))) continue;
                if (humanoid) ConfigureMotionImports(Keys[i]);
                Build((RuinsMonsterKind)i);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("RUINS_REVIEW_PREFABS_READY");
        }

        [MenuItem("AC Roguelike/Ruins/Rebuild Mechanical Medusa")]
        public static void RebuildMedusa()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring.");
            Build(RuinsMonsterKind.OssuaryMedusa);
            AssetDatabase.SaveAssets();
            Debug.Log("RUINS_MEDUSA_REBUILT");
        }

        static void ConfigureMotionImports()
        {
            foreach (var key in new[] { "penitent_husk", "mourning_matron" })
                ConfigureMotionImports(key);
        }
        static void ConfigureMotionImports(string key)
        {
            foreach (var suffix in new[] { "_rig", "_walking", "_running", "_motions" })
            {
                string path = Root + "/Art/Meshy/" + key + "/" + key + suffix + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!importer) throw new FileNotFoundException("Meshy skeleton/motion must be downloaded: " + path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.importCameras = false; importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
        }

        static void Build(RuinsMonsterKind kind)
        {
            int index = (int)kind;
            var root = new GameObject(kind.ToString());
            try
            {
                float[] heights = { 1.65f, 2.7f, .75f, 1.2f, 3.3f };
                float[] offsets = { 0, 0, 1.15f, 1.65f, 0 };
                float[] radii = { .85f, .48f, .62f, .67f, .6f };
                float[] bodies = { 1.65f, 2.65f, 1.9f, 2.85f, 3.25f };
                var body = root.AddComponent<CharacterController>();
                body.radius = radii[index]; body.height = bodies[index]; body.center = Vector3.up * (bodies[index] * .5f + .03f);
                body.skinWidth = .045f; body.minMoveDistance = 0; body.stepOffset = .25f; body.slopeLimit = 45;
                var visual = Child("Visual", root.transform);
                var pose = Child("Pose", visual);
                var model = Child("MeshyModel", pose);
                string source = Root + "/Art/Meshy/" + Keys[index] + "/" + Keys[index] + ".glb";
                bool humanoid = kind == RuinsMonsterKind.PenitentHusk || kind == RuinsMonsterKind.MourningMatron;
                string prefix = Root + "/Art/Meshy/" + Keys[index] + "/" + Keys[index];
                var instance = PlaceModel(humanoid ? prefix + "_rig.fbx" : source, model, heights[index]);
                model.localPosition = Vector3.up * offsets[index];
                if (kind == RuinsMonsterKind.ScrapBulwark || kind == RuinsMonsterKind.CarrionDrone) model.localRotation = Quaternion.Euler(0, -90, 0);
                if (kind == RuinsMonsterKind.OssuaryMedusa) model.localScale = new Vector3(1.35f, 1, 1.35f);
                var rig = pose.gameObject.AddComponent<RuinsMonsterRig>();
                rig.kind = kind; rig.model = model;
                if (kind == RuinsMonsterKind.MourningMatron)
                {
                    rig.attackAnticipationEnd = .17f;
                    rig.attackImpactNormalized = .247f;
                }
                rig.metalMaterial = Material("RustedSteel", new Color(.22f, .24f, .20f), .7f, .28f);
                rig.jointMaterial = Material("BlackHose", new Color(.065f, .074f, .063f), .35f, .24f);
                rig.glowMaterial = Material("AmberOptics", new Color(.9f, .31f, .045f), .4f, .4f, true);
                if (humanoid)
                {
                    // Rigging retains Meshy's atlas UVs. Use the source PBR material on the imported skeleton.
                    var sourceModel = RequireModel(prefix + "_rig.glb");
                    var materials = sourceModel.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).ToArray();
                    if (materials.Length == 0) throw new InvalidOperationException("Husk source PBR materials are missing.");
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterials = Enumerable.Repeat(materials[0], Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
                    rig.humanoidAnimator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                    rig.humanoidAnimator.applyRootMotion = false;
                    rig.humanoidAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    rig.idleClip = CopyClip(prefix, "_motions", "Idle", "Idle", true);
                    rig.attackClip = CopyClip(prefix, "_motions", kind == RuinsMonsterKind.PenitentHusk ? "Punch" : "Slash", "Attack", false);
                    rig.deathClip = CopyClip(prefix, "_motions", "Dead", "Death", false);
                    rig.walkClip = CopyClip(prefix, "_walking", null, "Walk", true);
                    rig.runClip = CopyClip(prefix, "_running", null, "Run", true);
                }
                rig.RefreshRestPose();
                int meshIndex = 0;
                foreach (var generated in rig.GeneratedMeshes)
                {
                    if (!generated) continue;
                    string path = Generated + "/" + kind + "_Tentacle_" + meshIndex++ + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh) EditorUtility.CopySerialized(generated, mesh);
                    else { mesh = Object.Instantiate(generated); AssetDatabase.CreateAsset(mesh, path); }
                    foreach (var filter in pose.GetComponentsInChildren<MeshFilter>())
                        if (filter.sharedMesh == generated) filter.sharedMesh = mesh;
                    EditorUtility.SetDirty(mesh);
                }
                var health = root.AddComponent<TrainingEnemy>();
                health.respawnOnDeath = false; health.deferDeathVisuals = true;
                health.aimAnchor = Child("Aim", pose);
                health.aimAnchor.localPosition = Vector3.up * (kind == RuinsMonsterKind.OssuaryMedusa ? 1.8f : kind == RuinsMonsterKind.CarrionDrone ? 1.4f : 1.1f);
                health.visibleRenderers = root.GetComponentsInChildren<Renderer>();
                var monster = root.AddComponent<RuinsMonster>();
                monster.kind = kind; monster.displayName = Names[index]; monster.visualRig = rig;
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + kind + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static AnimationClip CopyClip(string prefix, string suffix, string match, string role, bool loop)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(prefix + suffix + ".fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var source = match == null ? clips.FirstOrDefault() : clips.FirstOrDefault(c => c.name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!source) throw new InvalidOperationException("Missing Husk " + role + " clip; available: " + string.Join(", ", clips.Select(c => c.name)));
            string path = Generated + "/" + Path.GetFileName(prefix) + "_" + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip) EditorUtility.CopySerialized(source, clip);
            else { clip = Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
            clip.name = Path.GetFileName(prefix) + "_" + role;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip); return clip;
        }

        static GameObject RequireModel(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) throw new FileNotFoundException("Meshy model must finish importing: " + path);
            return model;
        }

        static GameObject PlaceModel(string path, Transform parent, float height)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(RequireModel(path), parent);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            if (path.EndsWith("_rig.fbx", StringComparison.OrdinalIgnoreCase))
            {
                string motions = path.Substring(0, path.Length - "_rig.fbx".Length) + "_motions.fbx";
                var idle = AssetDatabase.LoadAllAssetsAtPath(motions).OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 && !c.name.StartsWith("__preview__"));
                if (!idle) throw new InvalidOperationException("A humanoid needs its idle clip before sizing: " + motions);
                idle.SampleAnimation(go, .2f);
            }
            Bounds bounds = default; bool found = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.localBounds;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // Imported skin bounds may be relative to the root bone, not the renderer transform.
                    // The baked bind geometry gives a stable foot pivot and prevents a floating humanoid.
                    var baked = new Mesh(); skinned.BakeMesh(baked, true); b = baked.bounds; Object.DestroyImmediate(baked);
                }
                var matrix = parent.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            if (!found || bounds.size.y < .001f) throw new InvalidOperationException("Empty Meshy geometry: " + path);
            float scale = height / bounds.size.y;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return go;
        }

        static Transform Child(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static Material Material(string name, Color color, float metallic, float smoothness, bool glow = false)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness);
            if (glow) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2); }
            EditorUtility.SetDirty(material); return material;
        }
    }
}
