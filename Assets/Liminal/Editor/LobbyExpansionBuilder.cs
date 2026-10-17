using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AcRoguelike.Liminal.EditorTools
{
    /// <summary>Repeatable import and asset checks for the four additional lobby character rigs.</summary>
    public static class LobbyExpansionBuilder
    {
        const string Root = "Assets/Liminal/Resources/LiminalLobby/";
        public static readonly Dictionary<string, string[]> Characters = new Dictionary<string, string[]>
        {
            { "field_medic", new[] { "idle", "walk", "talk", "greet", "inspect", "listen" } },
            { "gate_engineer", new[] { "idle", "walk", "talk", "greet", "phone", "inspect" } },
            { "rookie_hunter", new[] { "idle", "walk", "talk", "greet", "warmup", "breath" } },
            { "veteran_hunter", new[] { "idle", "walk", "talk", "greet", "drink", "look" } }
        };

        [Serializable] public sealed class CharacterReport
        {
            public string character;
            public int triangles, bones, animationCount, textureWidth, textureHeight;
            public bool validHumanoid;
            public List<string> clips = new List<string>();
        }
        [Serializable] public sealed class ImportReport
        {
            public string utc, unityVersion;
            public List<CharacterReport> characters = new List<CharacterReport>();
        }

        [MenuItem("AC Roguelike/Liminal/Import Additional Lobby Characters")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var report = new ImportReport { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            foreach (var item in Characters)
            {
                string stem = Root + item.Key + "/" + item.Key;
                ConfigureModel(stem + ".fbx");
                foreach (string label in item.Value) ConfigureModel(stem + "@" + label + ".fbx");
                string texturePath = stem + "_albedo.png";
                var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (textureImporter == null) throw new FileNotFoundException("Missing lobby albedo", texturePath);
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.sRGBTexture = true;
                textureImporter.maxTextureSize = 2048;
                textureImporter.mipmapEnabled = true;
                textureImporter.isReadable = false;
                textureImporter.anisoLevel = 4;
                textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                textureImporter.alphaSource = TextureImporterAlphaSource.None;
                textureImporter.SaveAndReimport();

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(stem + ".fbx");
                var animator = model ? model.GetComponent<Animator>() : null;
                if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException(item.Key + " has no valid humanoid avatar.");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                var row = new CharacterReport { character = item.Key, validHumanoid = true,
                    bones = model.GetComponentsInChildren<Transform>(true).Length, textureWidth = texture.width, textureHeight = texture.height };
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh) row.triangles += (int)Enumerable.Range(0, renderer.sharedMesh.subMeshCount)
                        .Sum(index => (long)renderer.sharedMesh.GetIndexCount(index) / 3);
                if (row.triangles < 1000 || row.triangles > 35000)
                    throw new InvalidOperationException(item.Key + " triangle budget outside 1k–35k: " + row.triangles);
                foreach (string label in item.Value)
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(stem + "@" + label + ".fbx")
                        .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                    if (!clip || !clip.humanMotion || clip.length < .25f)
                        throw new InvalidOperationException(item.Key + "@" + label + " has no usable humanoid animation.");
                    row.animationCount++;
                    row.clips.Add(label + ": " + clip.length.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " s");
                }
                report.characters.Add(row);
            }
            Directory.CreateDirectory("Library/LobbyExpansionValidation");
            File.WriteAllText("Library/LobbyExpansionValidation/import.json", JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets();
            Debug.Log("Lobby expansion import passed: 4 humanoid models and 24 animation clips.");
        }

        static void ConfigureModel(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Missing lobby character FBX", path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("No model importer: " + path);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
        }
    }
}
