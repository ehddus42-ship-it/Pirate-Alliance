using UnityEditor;
using UnityEngine;

/// <summary>
/// Blender 화염 콤보(ProiBlazeCombo) 에셋 임포트 설정. 에디터가 스크립트를 다시 불러올 때 확인하고, 다르면 고칩니다.
///  - Resources/Proi_BlazeFX.fbx : Blender(D:/Blender_Test/VFX_Scripts/export_blaze_unity.py)에서 내보낸 화염 메시.
///    머티리얼 / 애니메이션 없음, 블렌드셰이프(Stretch) 유지, 노멀은 파일 그대로.
///  - Resources/ProiBlazeRamps.png : 화염 색 램프 4줄 (Clamp, 밉맵 없음, 압축 없음).
/// </summary>
[InitializeOnLoad]
public static class ProiBlazeSetup
{
    const string Fbx = "Assets/Characters/Proi/Resources/Proi_BlazeFX.fbx";
    const string Ramps = "Assets/Characters/Proi/Resources/ProiBlazeRamps.png";

    static ProiBlazeSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("Tools/Proi/Update Blaze FX Import")]
    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetImporter.GetAtPath(Fbx) is ModelImporter mi &&
            (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || !mi.importBlendShapes ||
             mi.importNormals != ModelImporterNormals.Import || mi.globalScale != 1f))
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.importBlendShapes = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.globalScale = 1f;
            mi.SaveAndReimport();
            Debug.Log("[ProiBlazeSetup] FBX import settings updated: " + Fbx);
        }
        if (AssetImporter.GetAtPath(Ramps) is TextureImporter ti &&
            (ti.wrapMode != TextureWrapMode.Clamp || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed ||
             !ti.sRGBTexture))
        {
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.sRGBTexture = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
            Debug.Log("[ProiBlazeSetup] Ramp texture import settings updated: " + Ramps);
        }
    }
}
