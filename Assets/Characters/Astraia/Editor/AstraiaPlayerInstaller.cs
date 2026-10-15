using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.EditorTools
{
    public static class AstraiaPlayerInstaller
    {
        public const string Root = "Assets/Characters/Astraia";
        public const string PlayerPath = Root + "/AstraiaPlayer.prefab";
        const string ExplorerPath = "Assets/Liminal/Prefabs/Explorer/LiminalExplorer.prefab";

        [MenuItem("Tools/Pirate Alliance/Astraia/Install Playable Character")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var visual = PirateAlliance.Characters.Editor.AstraiaRigBuilder.Build();
            try
            {
                ApplyMaterials(visual);
                var animator = visual.GetComponent<Animator>();
                animator.runtimeAnimatorController = AstraiaAnimationBuilder.Build();
                animator.applyRootMotion = false;
                PrefabUtility.SaveAsPrefabAsset(visual, PirateAlliance.Characters.Editor.AstraiaRigBuilder.PrefabPath);
            }
            finally { Object.DestroyImmediate(visual); }

            var player = PrefabUtility.LoadPrefabContents(ExplorerPath);
            try
            {
                var motor = player.GetComponent<PlayerMotor>();
                if (!motor) throw new InvalidOperationException("Explorer has no PlayerMotor.");
                if (motor.visual) Object.DestroyImmediate(motor.visual.gameObject);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PirateAlliance.Characters.Editor.AstraiaRigBuilder.PrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                motor.visual = instance.transform;
                motor.animator = instance.GetComponent<Animator>();
                motor.faceMovementDirection = true;
                motor.moveSpeed = 4.2f;
                motor.walkSpeed = 2.1f;
                motor.acceleration = 24f;
                motor.turnSpeed = 720f;
                motor.dashDuration = .24f;
                motor.dashSpeed = 12.5f;
                motor.dashCooldown = .72f;
                var body = player.GetComponent<CharacterController>();
                body.height = 1.65f; body.center = new Vector3(0, .84f, 0);
                body.radius = .24f; body.stepOffset = .24f; body.skinWidth = .025f;
                var caster = player.GetComponent<TalismanCaster>();
                if (!caster) caster = player.AddComponent<TalismanCaster>();
                caster.externalInput = true;
                caster.requireLineOfSight = true;
                caster.holdToCast = false;
                var hand = motor.animator.GetBoneTransform(HumanBodyBones.RightHand);
                var origin = new GameObject("Astraia Cast Origin").transform;
                origin.SetParent(hand ? hand : motor.visual, false);
                origin.localPosition = hand ? new Vector3(0, 0, .05f) : new Vector3(0, 1.2f, .3f);
                caster.castOrigin = origin;
                if (!caster.talismanPrefab) caster.talismanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Skill/VioletTalismanProjectile.prefab");
                if (!caster.spiritFlamePrefab) caster.spiritFlamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Skill/DokkaebiFlame.prefab");
                if (!caster.impactPrefab) caster.impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Skill/TalismanImpact.prefab");
                var combat = player.GetComponent<PlayerCombat>();
                if (!combat) combat = player.AddComponent<PlayerCombat>();
                var leftOrigin = new GameObject("Astraia Left Cast Origin").transform;
                leftOrigin.SetParent(motor.animator.GetBoneTransform(HumanBodyBones.LeftHand), false);
                leftOrigin.localPosition = new Vector3(0, 0, .05f);
                combat.attackOrigins = new[] { origin, leftOrigin, origin };
                // Preserve the existing prefab GUID so all three map scenes receive the character.
                PrefabUtility.SaveAsPrefabAsset(player, ExplorerPath);
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log("Astraia playable character installed in map prefabs: " + PlayerPath);
        }

        public static void ApplyMaterials(GameObject visual)
        {
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            ConfigureTexture(Root + "/Textures/AstraiaFace_BaseColor.png", false, 2048);
            ConfigureTexture(Root + "/Textures/AstraiaFace_Normal.png", true, 1024);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    string name = source ? source.name : renderer.name;
                    string path = Root + "/Materials/" + name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(mat, path); }
                    mat.SetFloat("_Smoothness", .30f);
                    mat.SetFloat("_Metallic", 0);
                    Color color = new Color(.82f, .88f, .98f);
                    if (name.Contains("Skin")) color = new Color(1f, .79f, .72f);
                    if (name.Contains("Dress")) color = new Color(.10f, .16f, .32f);
                    if (name.Contains("Hair") && !name.Contains("Ornament")) color = new Color(.78f, .85f, .97f);
                    if (name.Contains("Stockings")) color = new Color(.10f, .095f, .16f);
                    if (name.Contains("Boots")) color = new Color(.22f, .28f, .43f);
                    if (name.Contains("Gold") || name.Contains("Ornament")) { color = new Color(.78f, .64f, .36f); mat.SetFloat("_Metallic", .58f); }
                    if (name.Contains("Crystal")) { color = new Color(.24f, .61f, .96f); mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * .26f); }
                    if (name.Contains("Face"))
                    {
                        color = Color.white;
                        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/AstraiaFace_BaseColor.png"));
                        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/AstraiaFace_Normal.png"));
                        mat.SetFloat("_BumpScale", .25f); mat.EnableKeyword("_NORMALMAP");
                    }
                    mat.SetColor("_BaseColor", color);
                    EditorUtility.SetDirty(mat);
                    materials[i] = mat;
                }
                renderer.sharedMaterials = materials;
            }
        }

        static void ConfigureTexture(string path, bool normal, int size)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) return;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.maxTextureSize = size;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}
