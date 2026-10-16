// Unity 6 Editor utility. Run explicitly from Tools > Namgung Luna.
// No model importer settings, user materials, rigs, or open scenes are modified.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NamgungLuna.Editor
{
    public static class LunaAssetSetup
    {
        /// <summary>The character folder (e.g. Assets/NamgungLuna or Assets/Characters/NamgungLuna): parent of this script's Editor folder.</summary>
        internal static string Root
        {
            get
            {
                string script = AssetDatabase.FindAssets("LunaAssetSetup t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/LunaAssetSetup.cs", StringComparison.Ordinal));
                return Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
            }
        }
        private const string ShaderName = "NamgungLuna/Toon URP";

        [MenuItem("Tools/Namgung Luna/Create Toon Prefabs from FBX")]
        public static void CreateToonPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.LogError("[Namgung Luna] 캐릭터 폴더를 찾을 수 없습니다: " + Root);
                return;
            }
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || !pipeline.GetType().Name.Contains("UniversalRenderPipelineAsset"))
            {
                Debug.LogError("[Namgung Luna] 현재 품질 설정에 URP Render Pipeline Asset을 먼저 지정하세요.");
                return;
            }
            Shader shader = Shader.Find(ShaderName);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                Debug.LogError("[Namgung Luna] Toon URP 셰이더가 없거나 컴파일 오류가 있습니다. Console을 확인하세요.");
                return;
            }

            string[] fbxPaths = AssetDatabase.FindAssets("t:Model", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (fbxPaths.Length == 0)
            {
                Debug.LogWarning("[Namgung Luna] 이 폴더 아래에 FBX가 없습니다. 모델과 함께 텍스처를 먼저 가져오세요.");
                return;
            }

            string generated = EnsureFolder(Root, "Generated");
            string runFolder = NewFolder(generated, "Setup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            string materialFolder = NewFolder(runFolder, "Materials");
            string originalFolder = NewFolder(runFolder, "OriginalMaterials");
            string prefabFolder = NewFolder(runFolder, "Prefabs");
            // Thin cloth/hair may share a source material with closed skin meshes.
            // Cache the two-sided policy separately so those meshes keep their own culling.
            var converted = new Dictionary<(Material Source, bool DoubleSided), Material>();
            var report = new StringBuilder();
            report.AppendLine("남궁루나 URP 변환 보고서 / " + DateTime.UtcNow.ToString("u"));
            report.AppendLine("생성 위치: " + runFolder);
            report.AppendLine("원본 FBX / 원본 재질 / 열린 씬 / 리그 설정을 변경하지 않았습니다.");
            report.AppendLine("Toon: 기본색, UV 변환, 노멀, 발광, 컬링, 알파 클리핑/일반 알파 투명 지원.");
            report.AppendLine("이름으로 식별한 헤어·치마·칼라·소매·밑단·프릴·리본 등 얇은 파츠는 양면 렌더합니다. 얼굴·몸은 기존 컬링을 유지합니다.");
            report.AppendLine("금속/러프니스/AO 등 PBR 전용 효과는 Toon에서 재현하지 않습니다. OriginalMaterials에 원본 복사본을 보존합니다.");
            report.AppendLine("이 도구는 리깅, 웨이트, Humanoid Avatar 또는 애니메이션을 새로 만들지 않습니다.");
            int successes = 0;
            UnityEngine.Object lastPrefab = null;

            try
            {
                for (int i = 0; i < fbxPaths.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("남궁루나 프리팹 생성", fbxPaths[i], (float)i / fbxPaths.Length))
                    {
                        report.AppendLine("사용자가 중단했습니다. 이미 만든 에셋은 해당 실행 폴더에 보존합니다.");
                        break;
                    }
                    try
                    {
                        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPaths[i]);
                        if (model == null) throw new InvalidOperationException("FBX 모델을 불러오지 못했습니다.");
                        var previewScene = EditorSceneManager.NewPreviewScene();
                        try
                        {
                            GameObject instance = PrefabUtility.InstantiatePrefab(model, previewScene) as GameObject;
                            if (instance == null) throw new InvalidOperationException("미리보기 인스턴스 생성 실패.");
                            // An ordinary prefab preserves the hierarchy without a model-prefab variant connection.
                            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                            {
                                Material[] slots = renderer.sharedMaterials;
                                for (int slot = 0; slot < slots.Length; slot++)
                                {
                                    Material source = slots[slot];
                                    if (source == null)
                                    {
                                        report.AppendLine("경고: 재질 누락 / " + fbxPaths[i] + " / " + renderer.name + " / 슬롯 " + slot);
                                        continue;
                                    }
                                    bool doubleSided = NeedsDoubleSided(renderer, source, instance.transform);
                                    var key = (source, doubleSided);
                                    if (!converted.TryGetValue(key, out Material toon))
                                    {
                                        toon = ConvertMaterial(source, shader, materialFolder, originalFolder, report, doubleSided);
                                        converted.Add(key, toon);
                                    }
                                    slots[slot] = toon;
                                }
                                renderer.sharedMaterials = slots;
                            }

                            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabFolder + "/" + SafeName(model.name) + "_Toon.prefab");
                            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool ok);
                            if (!ok || saved == null) throw new InvalidOperationException("프리팹 저장 실패: " + prefabPath);
                            successes++;
                            lastPrefab = saved;
                            int skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                            Animator[] animators = instance.GetComponentsInChildren<Animator>(true);
                            bool humanoid = animators.Any(a => a.avatar != null && a.avatar.isValid && a.avatar.isHuman);
                            report.AppendLine("완료: " + fbxPaths[i] + " -> " + prefabPath);
                            report.AppendLine("  기존 SkinnedMeshRenderer: " + skins + ", 기존 유효 Humanoid Avatar: " + humanoid);
                            if (skins == 0) report.AppendLine("  정적 메시입니다. 캐릭터 애니메이션에 사용할 리깅과 웨이트 작업은 별도입니다.");
                        }
                        finally
                        {
                            // Destroys temporary objects without adding/removing objects in the user's open scenes.
                            EditorSceneManager.ClosePreviewScene(previewScene);
                        }
                    }
                    catch (Exception exception)
                    {
                        report.AppendLine("실패: " + fbxPaths[i] + " / " + exception.Message);
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                report.AppendLine("프리팹 성공: " + successes + " / 발견한 FBX: " + fbxPaths.Length);
                string reportPath = AssetDatabase.GenerateUniqueAssetPath(runFolder + "/SetupReport.txt");
                // CreateNew also prevents overwriting an on-disk file not yet known to AssetDatabase.
                using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(report.ToString());
                AssetDatabase.ImportAsset(reportPath);
            }
            if (lastPrefab != null) EditorGUIUtility.PingObject(lastPrefab);
            Debug.Log("[Namgung Luna] 프리팹 " + successes + "개 생성. 보고서: " + runFolder + "/SetupReport.txt");
        }

        [MenuItem("Tools/Namgung Luna/Create Toon Prefabs from FBX", true)]
        private static bool CanCreate() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        private static Material ConvertMaterial(Material source, Shader shader, string materials, string originals, StringBuilder report, bool doubleSided)
        {
            // Capture before changing shaders; property names vary between Standard, URP, and FBX importers.
            string baseProperty = FindBaseTextureProperty(source);
            Texture baseTexture = baseProperty != null ? source.GetTexture(baseProperty) : source.mainTexture;
            Vector2 baseScale = baseProperty != null ? source.GetTextureScale(baseProperty) : Vector2.one;
            Vector2 baseOffset = baseProperty != null ? source.GetTextureOffset(baseProperty) : Vector2.zero;
            Color baseColor = ReadBaseColor(source);
            bool alreadyToon = source.shader == shader;
            Material snapshot = new Material(source) { name = source.name + "_Original" };
            SaveNewMaterial(snapshot, originals);
            Material target = new Material(source) { name = source.name + (doubleSided ? "_DoubleSided_Toon" : "_Toon"), shader = shader };
            try
            {
                if (!alreadyToon)
                {
                    target.shaderKeywords = Array.Empty<string>();
                    target.SetColor("_ShadowColor", new Color(0.65f, 0.68f, 0.83f, 1f));
                    target.SetFloat("_ShadowThreshold", 0.1f);
                    target.SetFloat("_ShadowSoftness", 0.08f);
                    target.SetFloat("_AmbientStrength", 0.6f);
                    ApplyCharacterShadingPreset(target);
                }
                target.SetTexture("_MainTex", baseTexture);
                target.SetTextureScale("_MainTex", baseScale);
                target.SetTextureOffset("_MainTex", baseOffset);
                target.SetColor("_BaseColor", baseColor);

                bool hasNormal = CopyTexture(source, target, "_BumpMap", "_BumpMap", "_NormalMap", "_NormalTexture");
                target.SetFloat("_UseNormalMap", alreadyToon ? ReadFloat(source, 0f, "_UseNormalMap") : (hasNormal ? 1f : 0f));
                target.SetFloat("_BumpScale", ReadFloat(source, 1f, "_BumpScale", "_NormalScale"));
                bool hasEmission = CopyTexture(source, target, "_EmissionMap", "_EmissionMap", "_EmissiveColorMap", "_EmissiveTexture");
                Color emission = ReadColor(source, hasEmission ? Color.white : Color.black, "_EmissionColor", "_EmissiveColor", "_EmissiveFactor");
                target.SetColor("_EmissionColor", emission);
                target.SetFloat("_UseEmission", alreadyToon ? ReadFloat(source, 0f, "_UseEmission") : (emission.maxColorComponent > 0f ? 1f : 0f));
                // A missing map uses the shader's white texture for constant-color emission.
                if (!hasEmission) target.SetTexture("_EmissionMap", null);

                bool alphaClip = ReadFloat(source, 0f, "_AlphaClip", "_AlphaClipEnable") > 0.5f
                    || source.IsKeywordEnabled("_ALPHATEST_ON") || Mathf.Approximately(ReadFloat(source, 0f, "_Mode"), 1f);
                bool transparent = ReadFloat(source, 0f, "_Surface", "_SurfaceType") > 0.5f
                    || ReadFloat(source, 0f, "_Mode") >= 2f
                    || source.GetTag("RenderType", false, "") == "Transparent"
                    || source.renderQueue >= (int)RenderQueue.Transparent;
                target.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
                target.SetFloat("_Cutoff", ReadFloat(source, 0.5f, "_Cutoff", "_AlphaCutoff"));
                target.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : ReadFloat(source, (float)CullMode.Back, "_Cull", "_CullMode"));
                target.SetFloat("_Surface", transparent ? 1f : 0f);
                if (transparent) target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else target.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                target.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
                target.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
                target.SetFloat("_ZWrite", transparent ? 0f : 1f);
                target.SetOverrideTag("RenderType", transparent ? "Transparent" : (alphaClip ? "TransparentCutout" : "Opaque"));
                target.renderQueue = transparent ? Math.Max((int)RenderQueue.Transparent, source.renderQueue)
                    : (alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry);
                target.SetShaderPassEnabled("ShadowCaster", !transparent);
                target.SetShaderPassEnabled("DepthOnly", !transparent);
                target.SetShaderPassEnabled("DepthNormals", !transparent);
                target.SetShaderPassEnabled("DepthNormalsOnly", !transparent);
                target.enableInstancing = source.enableInstancing;

                SaveNewMaterial(target, materials);
                report.AppendLine("재질: " + AssetDatabase.GetAssetPath(source) + " :: " + source.name + " -> " + AssetDatabase.GetAssetPath(target));
                report.AppendLine("  기본 텍스처: " + (baseTexture == null ? "없음 (색상만 유지)" : AssetDatabase.GetAssetPath(baseTexture)));
                if (doubleSided) report.AppendLine("  얇은 파츠: 양면 렌더(Cull Off), 공유 재질의 얼굴·몸 컬링과 분리.");
                if (transparent) report.AppendLine("  투명 재질은 일반 알파 블렌딩으로 변환했습니다. 유리의 굴절/특수 블렌딩은 수동 검수가 필요합니다.");
                return target;
            }
            catch
            {
                if (!AssetDatabase.Contains(target)) UnityEngine.Object.DestroyImmediate(target);
                throw;
            }
        }

        // A gentle conventional toon preset; this does not implement a facial SDF.
        public static void ApplyCharacterShadingPreset(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != ShaderName) return;
            string name = NormalizePartName(material.name);
            if (name.Contains("hair"))
            {
                material.SetColor("_ShadowColor", new Color(0.76f, 0.78f, 0.92f, 1f));
                material.SetFloat("_ShadowSoftness", 0.24f);
            }
            else if (name.Contains("face") || name.Contains("skin") || name.Contains("body"))
            {
                material.SetColor("_ShadowColor", new Color(0.92f, 0.88f, 0.93f, 1f));
                material.SetFloat("_AmbientStrength", 0.85f);
                material.SetFloat("_ShadowSoftness", 0.20f);
            }
        }

        private static bool NeedsDoubleSided(Renderer renderer, Material source, Transform modelRoot)
        {
            // A specific child/group name takes precedence over an ambiguous material name.
            for (Transform item = renderer.transform; item != null; item = item.parent)
            {
                string name = NormalizePartName(item.name);
                if (IsThinPartName(name)) return true;
                if (IsClosedPartName(name)) return false;
                if (item == modelRoot) break;
            }
            string materialName = NormalizePartName(source.name);
            return !IsClosedPartName(materialName) && IsThinPartName(materialName);
        }

        private static string NormalizePartName(string name) => name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");

        private static bool IsThinPartName(string name) => new[]
        {
            "hair", "skirt", "collar", "sleeve", "goldhem", "frill", "ribbon", "lace", "lining"
        }.Any(name.Contains);

        private static bool IsClosedPartName(string name) => new[] { "face", "head", "body", "skin" }.Any(name.Contains);

        private static string FindBaseTextureProperty(Material source)
        {
            if (source.shader != null)
                for (int i = 0; i < source.shader.GetPropertyCount(); i++)
                    if (source.shader.GetPropertyType(i) == ShaderPropertyType.Texture
                        && (source.shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    {
                        string property = source.shader.GetPropertyName(i);
                        if (source.GetTexture(property) != null) return property;
                    }
            foreach (string property in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_BaseColorTexture", "_AlbedoMap", "_DiffuseMap" })
                if (source.HasProperty(property) && source.GetTexture(property) != null) return property;
            // Keep custom [MainTexture] shaders' tiling/offset if they use an unfamiliar name.
            Texture mainTexture = source.mainTexture;
            if (mainTexture != null)
                foreach (string property in source.GetTexturePropertyNames())
                    if (source.GetTexture(property) == mainTexture) return property;
            return null;
        }

        private static bool CopyTexture(Material source, Material target, string destination, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (!source.HasProperty(property) || source.GetTexture(property) == null) continue;
                target.SetTexture(destination, source.GetTexture(property));
                target.SetTextureScale(destination, source.GetTextureScale(property));
                target.SetTextureOffset(destination, source.GetTextureOffset(property));
                return true;
            }
            target.SetTexture(destination, null);
            return false;
        }

        private static Color ReadColor(Material material, Color fallback, params string[] properties)
        {
            foreach (string property in properties) if (material.HasProperty(property)) return material.GetColor(property);
            return fallback;
        }

        private static Color ReadBaseColor(Material source)
        {
            if (source.shader != null)
                for (int i = 0; i < source.shader.GetPropertyCount(); i++)
                    if (source.shader.GetPropertyType(i) == ShaderPropertyType.Color
                        && (source.shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainColor) != 0)
                        return source.GetColor(source.shader.GetPropertyName(i));
            return ReadColor(source, Color.white, "_BaseColor", "_Color", "_BaseColorFactor");
        }

        private static float ReadFloat(Material material, float fallback, params string[] properties)
        {
            foreach (string property in properties) if (material.HasProperty(property)) return material.GetFloat(property);
            return fallback;
        }

        private static void SaveNewMaterial(Material material, string folder)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(material.name) + ".mat");
            if (File.Exists(path)) throw new IOException("기존 파일을 덮어쓰지 않습니다: " + path);
            AssetDatabase.CreateAsset(material, path);
        }

        private static string SafeName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string(value.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(safe) ? "Unnamed" : safe;
        }

        private static string EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            return AssetDatabase.IsValidFolder(path) ? path : NewFolder(parent, name);
        }

        private static string NewFolder(string parent, string name)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + name);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            string created = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(created)) throw new IOException("폴더 생성 실패: " + path);
            return created;
        }
    }
}
#endif
