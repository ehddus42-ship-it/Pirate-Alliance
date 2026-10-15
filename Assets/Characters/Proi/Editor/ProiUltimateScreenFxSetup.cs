using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 궁극기 화면 연출 연결: Tools > Proi > Setup Ultimate Screen FX
///  - Assets/Characters/Proi/Materials/M_ImpactFrame.mat (Proi/ImpactFrame 셰이더)
///  - 지금 쓰는 URP 렌더러에 Full Screen Pass 기능 "ProiImpactFrame" (평소에는 꺼져 있고 임팩트 프레임 동안만 켜짐)
///  - Player 에 ProiUltimateScreenFx 를 붙이고 위 둘을 연결, 씬 저장
/// 여러 번 실행해도 같은 것을 다시 만들지 않습니다.
/// </summary>
public static class ProiUltimateScreenFxSetup
{
    const string MaterialPath = "Assets/Characters/Proi/Materials/M_ImpactFrame.mat";
    const string FeatureName = "ProiImpactFrame";

    [MenuItem("Tools/Proi/Setup Ultimate Screen FX")]
    static void FromMenu() => Debug.Log("[ProiUltimateScreenFxSetup] " + Setup());

    public static string Setup()
    {
        var ult = Object.FindFirstObjectByType<ProiUltimate>();
        if (ult == null) return "ProiUltimate 가 씬에 없습니다 (ProiPlayground 씬을 여세요)";
        var shader = Shader.Find("Proi/ImpactFrame");
        if (shader == null) return "Proi/ImpactFrame 셰이더를 찾지 못했습니다";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "M_ImpactFrame" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return "URP 파이프라인 에셋이 아닙니다";
        var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
        FullScreenPassRendererFeature first = null;
        for (int i = 0; i < list.arraySize; i++)
        {
            var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
            if (data == null) continue;
            var feature = FindOrAdd(data);
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.None;
            feature.passMaterial = mat;
            feature.passIndex = 0;
            feature.SetActive(false);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            if (first == null) first = feature;
        }
        AssetDatabase.SaveAssets();

        var fx = ult.GetComponent<ProiUltimateScreenFx>();
        if (fx == null) fx = Undo.AddComponent<ProiUltimateScreenFx>(ult.gameObject);
        var so = new SerializedObject(fx);
        so.FindProperty("impactMaterial").objectReferenceValue = mat;
        so.FindProperty("impactFeature").objectReferenceValue = first;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(ult.gameObject.scene);
        EditorSceneManager.SaveScene(ult.gameObject.scene);
        return "impact frame material + renderer feature ready, ProiUltimateScreenFx on " + ult.name;
    }

    static FullScreenPassRendererFeature FindOrAdd(ScriptableRendererData data)
    {
        foreach (var f in data.rendererFeatures)
            if (f is FullScreenPassRendererFeature fs && f.name == FeatureName) return fs;

        var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        feature.name = FeatureName;
        AssetDatabase.AddObjectToAsset(feature, data);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
        var so = new SerializedObject(data);
        var features = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedProperties();
        return feature;
    }
}
