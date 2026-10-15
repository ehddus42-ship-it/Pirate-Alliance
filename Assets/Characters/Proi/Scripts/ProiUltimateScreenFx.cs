using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 궁극기(ProiUltimate) 화면 연출. 궁극기의 상태 / 이벤트만 보고 그립니다.
///  - 차징: 해(Directional Light)와 주변광을 낮추고 비네트를 짙게 해 주먹 화염만 빛나는 무대 조명처럼 만들고, 도약 순간 확 밝아짐.
///  - 주먹 마주침: 2프레임짜리 짧은 임팩트 프레임.
///  - 꽂힘: 임팩트 프레임 (명조식: 2색 반전 -> 2색 -> 원래 화면, 만화 집중선, 실제 시간 몇 프레임) 뒤 잠깐 색이 빠졌다가 돌아옴.
/// 임팩트 프레임은 URP 렌더러의 Full Screen Pass 기능(Proi/ImpactFrame 머티리얼)을 쓸 때만 켜집니다
/// (Tools > Proi > Setup Ultimate Screen FX 가 연결). 비어 있으면 그 부분만 건너뜁니다.
/// </summary>
[DefaultExecutionOrder(110)]
public class ProiUltimateScreenFx : MonoBehaviour
{
    [Header("차징: 어둡게")]
    [Tooltip("차징 중 해(Directional Light) / 주변광 밝기 배율")]
    [SerializeField] Vector2 chargeDim = new Vector2(0.25f, 0.35f);
    [Tooltip("차징 중 비네트 세기 / 노출 보정 (EV)")]
    [SerializeField] Vector2 chargeVignette = new Vector2(0.45f, -0.3f);
    [Tooltip("차징 중 하늘(스카이박스) 밝기 배율")]
    [SerializeField] float chargeSky = 0.3f;
    [Tooltip("어두워지는 시간 / 도약 때 밝아지는 시간 (초, 실제 시간)")]
    [SerializeField] Vector2 dimTimes = new Vector2(0.45f, 0.1f);

    [Header("임팩트 프레임")]
    [SerializeField] Material impactMaterial;
    [SerializeField] ScriptableRendererFeature impactFeature;
    [Tooltip("꽂힘: 반전 2색 / 정방향 2색 / 원래 화면으로 돌아오기 (초, 실제 시간)")]
    [SerializeField] Vector3 slamFrames = new Vector3(0.05f, 0.05f, 0.06f);
    [Tooltip("주먹 마주침: 반전 2색 (초, 실제 시간, 0 = 끔)")]
    [SerializeField] float clashFrame = 0.035f;
    [Tooltip("2색으로 나눌 밝기 경계 (0..1)")]
    [SerializeField] float threshold = 0.35f;
    [SerializeField] Color ink = new Color(0.07f, 0.015f, 0.01f);
    [SerializeField] Color paper = new Color(1f, 0.96f, 0.9f);
    [Tooltip("꽂힌 뒤 채도 (-100 ~ 0) / 돌아오는 시간 (초, 실제 시간)")]
    [SerializeField] Vector2 afterDesat = new Vector2(-70f, 0.7f);

    static readonly int IdAmount = Shader.PropertyToID("_Amount"), IdInvert = Shader.PropertyToID("_Invert"),
        IdThreshold = Shader.PropertyToID("_Threshold"), IdLines = Shader.PropertyToID("_Lines"),
        IdSeed = Shader.PropertyToID("_LineSeed"), IdCenter = Shader.PropertyToID("_Center"),
        IdFlash = Shader.PropertyToID("_Flash"), IdInk = Shader.PropertyToID("_Ink"), IdPaper = Shader.PropertyToID("_Paper");

    ProiUltimate ult;
    Light sun;
    float sunBase = 1f, ambientBase = 1f;
    Color ambientColorBase;
    Material skyBase, sky;
    float skyExposure = 1f;
    static readonly int IdExposure = Shader.PropertyToID("_Exposure");
    float dim;
    Volume dimVolume, desatVolume;
    VolumeProfile dimProfile, desatProfile;
    Vignette vignette;
    ColorAdjustments dimColor, desatColor;
    float impactAt = -99f, clashAt = -99f;
    Vector2 impactCenter, clashCenter;

    void Awake()
    {
        ult = GetComponent<ProiUltimate>();
        if (ult == null) { enabled = false; return; }
        ult.OnSlam += HandleSlam;
        ult.OnClash += HandleClash;
    }

    void OnDestroy()
    {
        if (ult == null) return;
        ult.OnSlam -= HandleSlam;
        ult.OnClash -= HandleClash;
    }

    void OnEnable()
    {
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) sunBase = sun.intensity;
        ambientBase = RenderSettings.ambientIntensity;
        ambientColorBase = RenderSettings.ambientLight;
        skyBase = RenderSettings.skybox;
        if (skyBase != null && skyBase.HasProperty(IdExposure))
        {   // a copy: the skybox asset itself is never touched
            sky = new Material(skyBase) { name = skyBase.name + " (ultimate)" };
            skyExposure = skyBase.GetFloat(IdExposure);
            RenderSettings.skybox = sky;
        }

        dimProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        vignette = dimProfile.Add<Vignette>(true);
        vignette.intensity.Override(chargeVignette.x);
        vignette.smoothness.Override(0.55f);
        vignette.color.Override(Color.black);
        dimColor = dimProfile.Add<ColorAdjustments>(true);
        dimColor.postExposure.Override(chargeVignette.y);
        dimColor.contrast.Override(14f);
        dimVolume = MakeVolume("ProiUltimate Charge Volume", dimProfile, 20f);

        desatProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        desatColor = desatProfile.Add<ColorAdjustments>(true);
        desatColor.saturation.Override(afterDesat.x);
        desatColor.contrast.Override(18f);
        desatVolume = MakeVolume("ProiUltimate Impact Volume", desatProfile, 21f);

        if (impactFeature != null) impactFeature.SetActive(false);
    }

    void OnDisable()
    {
        ApplyDim(0f);
        if (sky != null) { RenderSettings.skybox = skyBase; Destroy(sky); sky = null; }
        if (impactFeature != null) impactFeature.SetActive(false);
        if (dimVolume != null) Destroy(dimVolume.gameObject);
        if (desatVolume != null) Destroy(desatVolume.gameObject);
        if (dimProfile != null) Destroy(dimProfile);
        if (desatProfile != null) Destroy(desatProfile);
    }

    static Volume MakeVolume(string name, VolumeProfile profile, float priority)
    {
        var v = new GameObject(name).AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = priority;
        v.sharedProfile = profile;
        v.weight = 0f;
        return v;
    }

    void HandleSlam(Vector3 at)
    {
        impactAt = ProiClock.Now;
        impactCenter = Viewport(at + Vector3.up * 0.4f);
    }

    void HandleClash(Vector3 at)
    {
        if (clashFrame <= 0f) return;
        clashAt = ProiClock.Now;
        clashCenter = Viewport(at);
    }

    static Vector2 Viewport(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector2(0.5f, 0.5f);
        Vector3 v = cam.WorldToViewportPoint(world);
        return v.z > 0f ? new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y)) : new Vector2(0.5f, 0.5f);
    }

    void LateUpdate()
    {
        float now = ProiClock.Now, dt = ProiClock.Delta;

        // ---- charge: the world goes dark round her burning fist, and lights up again as she takes off
        float st = ult.SkillTime;
        bool charging = ult.Active && st >= ult.ChargeStart && st < ult.DashStart;
        dim = Mathf.MoveTowards(dim, charging ? 1f : 0f, dt / Mathf.Max(charging ? dimTimes.x : dimTimes.y, 0.01f));
        ApplyDim(dim);

        // ---- impact frames: inverted two-tone, then upright two-tone, then fading back (real time: through the hit-stop)
        float amount = 0f, invert = 0f, lines = 1f, seed = 0f;
        Vector2 center = impactCenter;
        float e = now - impactAt, end = slamFrames.x + slamFrames.y;
        if (e >= 0f && e < end + slamFrames.z)
        {
            amount = e < end ? 1f : 1f - (e - end) / Mathf.Max(slamFrames.z, 1e-3f);
            invert = e < slamFrames.x ? 1f : 0f;
            seed = e < slamFrames.x ? 1f : 2f;
            lines = e < end ? 1f : 0f;
        }
        float c = now - clashAt;
        if (amount <= 0f && c >= 0f && c < clashFrame)
        {
            amount = 1f; invert = 1f; seed = 3f; center = clashCenter;
        }
        bool on = amount > 0f && impactMaterial != null && impactFeature != null;
        if (impactFeature != null && impactFeature.isActive != on) impactFeature.SetActive(on);
        if (on)
        {
            impactMaterial.SetFloat(IdAmount, amount);
            impactMaterial.SetFloat(IdInvert, invert);
            impactMaterial.SetFloat(IdLines, lines);
            impactMaterial.SetFloat(IdSeed, seed);
            impactMaterial.SetFloat(IdThreshold, threshold);
            impactMaterial.SetFloat(IdFlash, 0f);
            impactMaterial.SetVector(IdCenter, center);
            impactMaterial.SetColor(IdInk, ink);
            impactMaterial.SetColor(IdPaper, paper);
        }

        // ---- after the slam: the colour drains out for a moment and comes back
        float d = now - (impactAt + end);
        desatVolume.weight = d >= 0f && d < afterDesat.y ? 1f - Mathf.SmoothStep(0f, 1f, d / afterDesat.y) : 0f;
        desatColor.saturation.value = afterDesat.x;
    }

    void ApplyDim(float k)
    {
        float s = Mathf.SmoothStep(0f, 1f, k);
        if (sun != null) sun.intensity = sunBase * Mathf.Lerp(1f, chargeDim.x, s);
        RenderSettings.ambientIntensity = ambientBase * Mathf.Lerp(1f, chargeDim.y, s);
        RenderSettings.ambientLight = ambientColorBase * Mathf.Lerp(1f, chargeDim.y, s);
        if (sky != null) sky.SetFloat(IdExposure, skyExposure * Mathf.Lerp(1f, chargeSky, s));
        if (dimVolume != null) dimVolume.weight = s;
        if (vignette != null) vignette.intensity.value = chargeVignette.x;
        if (dimColor != null) dimColor.postExposure.value = chargeVignette.y;
    }
}
