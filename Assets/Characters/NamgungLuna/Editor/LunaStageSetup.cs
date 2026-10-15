#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using NamgungLuna.Motion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NamgungLuna.Editor
{
    public static class LunaStageSetup
    {
        public const string Root = "Assets/Characters/NamgungLuna";
        public const string BattlePrefab = Root + "/Stage/NamgungLuna_Battle.prefab";
        public const string PeacePrefab = Root + "/Stage/NamgungLuna_Peaceful.prefab";
        const string HunterFolder = "Assets/Liminal/Resources/Hunters";
        const string FormFolder = Root + "/Stage/Profiles";
        const float PeaceMoveSpeed = .65f;
        const float PeaceSlowSpeed = .4f;
        const float PeaceWalkPlayback = 1.5f;

        /// <summary>
        /// Battle-form renderers that skip the shadow passes. They sit inside the silhouette of a larger part (hair fill under
        /// the back hair, boot underlaps, chest ornaments, inset, facings, sleeve joins, lantern core) or are thin hem charms,
        /// so the cast shadow is unchanged in practice while about 111k of her 359k triangles leave every shadow cascade.
        /// They still render and receive shadows. Docs: Documentation/NamgungLuna/Performance/battle_form_lag.md
        /// </summary>
        public static readonly string[] BattleShadowlessParts =
        {
            "Hair_BackFill_Lock_01", "Hair_BackFill_Lock_02", "Hair_BackFill_Lock_03", "Hair_BackFill_Lock_04",
            "Hair_BackFill_Lock_05", "Hair_BackFill_Lock_06", "Hair_BackFill_Lock_07",
            "Gold_Hem_Charms_L", "Gold_Hem_Charms_R", "Gold_Hem_Trim",
            "Chest_Crescent", "Chest_Star", "Waist_Navy_Inset",
            "Boot_Shaft_Underlap_L", "Boot_Shaft_Underlap_R",
            "Neckline_Navy_Facing_L", "Neckline_Navy_Facing_R",
            "Puff_Sleeve_Join_L", "Puff_Sleeve_Join_R",
            "Lantern_Emissive_Core",
        };

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [MenuItem("Tools/Namgung Luna/Stage Capture/Install Battle Movement")]
        public static void InstallBattle()
        {
            Folder(Root + "/Stage");
            string source = Root + "/Magica/NamgungLuna_Magica.prefab";
            var instance = PrefabUtility.LoadPrefabContents(source);
            try
            {
                instance.name = "NamgungLuna_Battle";
                foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is LunaMoonMotor || component is LunaMoonShowcase ||
                        (component && component.GetType().Name == "LunaMagicaDemoMotion"))
                        UnityEngine.Object.DestroyImmediate(component);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                if (!instance.GetComponent<LunaStageMotionSource>()) instance.AddComponent<LunaStageMotionSource>();
                var gait = instance.GetComponent<LunaMoonGait>();
                if (!gait) throw new InvalidOperationException("The battle source prefab has no moon gait.");
                gait.enabled = true;
                gait.lanternSmoothTime = 0.24f;
                gait.lanternMaxAngle = 14f;
                var fx = instance.GetComponent<LunaMoonFx>();
                if (fx) fx.enabled = true;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    if (Array.IndexOf(BattleShadowlessParts, renderer.name) >= 0)
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                PrefabUtility.SaveAsPrefabAsset(instance, BattlePrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            InstallUnifiedHunter();
        }

        [MenuItem("Tools/Namgung Luna/Stage Capture/Install Peaceful Movement")]
        public static void InstallPeaceful()
        {
            Folder(Root + "/Stage");
            string modelPath = Root + "/Peaceful/Models/LunaPeaceful_Game.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.preserveHierarchy = true;
            importer.importAnimation = false;
            importer.importCameras = importer.importLights = false;
            importer.SaveAndReimport();
            AnimationClip idle = ImportClip("LunaPeacefulIdle_Game", "LunaPeaceful_Idle");
            AnimationClip walk = ImportClip("LunaPeacefulWalk_Game", "LunaPeaceful_Walk");
            string controllerPath = Root + "/Stage/LunaPeaceful.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            foreach (var state in controller.layers[0].stateMachine.states) controller.layers[0].stateMachine.RemoveState(state.state);
            foreach (var previousTree in AssetDatabase.LoadAllAssetsAtPath(controllerPath).OfType<BlendTree>())
                UnityEngine.Object.DestroyImmediate(previousTree, true);
            controller.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float } };
            var blend = new BlendTree { name = "Peaceful Locomotion", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(blend, controller);
            blend.AddChild(idle, 0);
            float slowRatio = PeaceSlowSpeed / PeaceMoveSpeed;
            blend.AddChild(walk, slowRatio);
            blend.AddChild(walk, 1);
            var children = blend.children;
            children[1].timeScale = PeaceWalkPlayback * slowRatio;
            children[2].timeScale = PeaceWalkPlayback;
            blend.children = children;
            var locomotion = controller.layers[0].stateMachine.AddState("Locomotion");
            locomotion.motion = blend;
            controller.layers[0].stateMachine.defaultState = locomotion;
            EditorUtility.SetDirty(controller);

            string materialPath = Root + "/Stage/LunaPeaceful_Toon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("NamgungLuna/Toon URP"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Peaceful/Models/LunaPeaceful_Albedo_0.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cull", 0);
            material.SetFloat("_AmbientStrength", .85f);
            material.SetFloat("_ShadowSoftness", .18f);
            EditorUtility.SetDirty(material);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "NamgungLuna_Peaceful";
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.sharedMaterials = Enumerable.Repeat(material, skin.sharedMaterials.Length).ToArray();
                    skin.updateWhenOffscreen = true;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, PeacePrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            InstallUnifiedHunter();
        }

        [MenuItem("Tools/Namgung Luna/Stage Capture/Install Unified Hunter")]
        public static void InstallUnifiedHunter()
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefab) ||
                !AssetDatabase.LoadAssetAtPath<GameObject>(PeacePrefab))
                throw new InvalidOperationException("Install the battle and peaceful movement prefabs before unifying their hunter profile.");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Stage/LunaPeaceful.controller");
            if (!controller) throw new InvalidOperationException("The peaceful locomotion controller is missing. Install Peaceful Movement first.");

            // Moving preserves references while only the canonical hunter remains in Resources/Hunters.
            MoveLegacyProfile(HunterFolder + "/NamgungLunaBattle.asset", HunterFolder + "/NamgungLuna.asset");
            MoveLegacyProfile(HunterFolder + "/NamgungLunaPeaceful.asset", FormFolder + "/NamgungLunaPeaceful.asset");
            var peaceful = MakeProfile("NamgungLunaPeaceful", "namgung_luna_peace", "남궁루나 · 평시", 30,
                "베일을 쓴 평시 상태\n차분한 걸음 · 이동만 구현", PeacePrefab, controller, PeaceMoveSpeed, PeaceSlowSpeed,
                "Loadout/namgung_luna_peace", folder: FormFolder);
            peaceful.lobbyForm = null;
            peaceful.previousCharacterIds = Array.Empty<string>();
            var hunter = MakeProfile("NamgungLuna", "namgung_luna", "남궁루나", 30,
                "로비 평시 · 던전 전시\n이동 · 도약 대시 · 공격 없음", BattlePrefab, null, 4.2f, 2.1f,
                "Loadout/namgung_luna_peace", keepDash: true);
            hunter.lobbyForm = peaceful;
            hunter.previousCharacterIds = new[] { "namgung_luna_peace", "namgung_luna_battle" };
            EditorUtility.SetDirty(peaceful);
            EditorUtility.SetDirty(hunter);
            AssetDatabase.SaveAssets();
        }

        static void MoveLegacyProfile(string source, string destination)
        {
            if (!AssetDatabase.LoadAssetAtPath<HunterProfile>(source)) return;
            if (AssetDatabase.LoadMainAssetAtPath(destination))
                throw new InvalidOperationException($"Both legacy and current profiles exist: {source} / {destination}. Resolve the duplicate before installing.");
            Folder(Path.GetDirectoryName(destination).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }

        static AnimationClip ImportClip(string file, string clipName)
        {
            string path = Root + "/Peaceful/Animations/" + file + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            string output = Root + "/Stage/" + clipName + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
            if (!clip)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, output);
            }
            EditorUtility.CopySerialized(source, clip);
            clip.name = clipName;
            clip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        public static HunterProfile MakeProfile(string fileName, string id, string display, int order,
            string summary, string prefab, RuntimeAnimatorController controller, float speed, float walk, string portrait,
            bool keepDash = false, string folder = HunterFolder)
        {
            Folder(folder);
            string path = folder + "/" + fileName + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<HunterProfile>(path);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<HunterProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.characterId = id;
            profile.displayName = display;
            profile.order = order;
            profile.summary = summary;
            profile.model = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            profile.controller = controller;
            profile.headBone = "";
            profile.portraitResource = portrait;
            profile.attackDurations = Array.Empty<float>();
            profile.hitTimes = Array.Empty<float>();
            profile.swings = Array.Empty<AcRoguelike.MeleeSlash.Swing>();
            profile.blade = false;
            // Serialized names keep this installer independent of the integration's compile order.
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("movementOnly").boolValue = true;
            serialized.FindProperty("keepDash").boolValue = keepDash;
            serialized.FindProperty("moveSpeedOverride").floatValue = speed;
            serialized.FindProperty("walkSpeedOverride").floatValue = walk;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
#endif
