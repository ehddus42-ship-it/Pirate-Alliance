using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 화면 연출: 카메라 포스트 프로세싱을 켜고, 블룸 · 색보정 · 약한 비네트를 가진 전역 볼륨을 만듭니다.
/// 카메라에 붙입니다 (ProiPlayerController 가 없으면 자동으로 붙임).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ProiPostFX : MonoBehaviour
{
    [Header("Bloom")]
    [SerializeField] float bloomThreshold = 1.1f;
    [SerializeField] float bloomIntensity = 0.3f;
    [Range(0f, 1f)] [SerializeField] float bloomScatter = 0.65f;

    [Header("Color")]
    [Tooltip("노출 보정 (EV)")]
    [SerializeField] float postExposure = 0.15f;
    [Range(-100f, 100f)] [SerializeField] float contrast = 4f;
    [Range(-100f, 100f)] [SerializeField] float saturation = 0f;
    [SerializeField] Color colorFilter = new Color(1f, 0.98f, 0.97f);

    [Header("Vignette")]
    [Range(0f, 1f)] [SerializeField] float vignette = 0.08f;

    VolumeProfile profile;
    GameObject volumeObject;

    void OnEnable()
    {
        var data = GetComponent<Camera>().GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "ProiPostFX (runtime)";

        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(bloomThreshold);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(bloomScatter);

        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(postExposure);
        color.contrast.Override(contrast);
        color.saturation.Override(saturation);
        color.colorFilter.Override(colorFilter);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.None);

        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(vignette);
        vig.smoothness.Override(0.5f);

        volumeObject = new GameObject("ProiPostFX Volume");
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }

    void OnDisable()
    {
        if (volumeObject != null) Destroy(volumeObject);
        if (profile != null) Destroy(profile);
    }
}
