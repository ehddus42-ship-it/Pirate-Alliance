#if MAGICACLOTH2
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// 궁극기 동안 머리카락(과 옷)을 휘날리게 하는 연출 바람 (Magica Cloth 2, Proi_Magica 프리팹 전용).
/// Proi 를 감싸는 작은 방향풍(MagicaWindZone, 다른 바람에 더해짐)을 궁극기 장면마다 바꿉니다:
///  1) 주먹 마주치기: 충격파가 머리를 뒤로 한 번 털어 줌
///  2) 화염 차징: 발밑에서 솟는 열기 — 점점 세지며 소용돌이처럼 돌아 머리가 떠올라 너울거림
///  3) 도약: 솟구치는 반대(아래 뒤)로 / 정점: 잠깐 떠오름 / 내려꽂기: 떨어지는 반대(위 뒤)로 길게 날림
///  4) 땅 찍기: 충격으로 위 뒤로 튀었다가 가라앉음
/// 게임 안에서는 궁극기 연출 배속이 Proi 의 동작에만 걸리므로(ProiUltimate.OwnTimeScale) 물리도 같은 배속으로 돌립니다.
/// (걷기 / 달리기 때 뒤로 날리는 것은 각 클로스의 Moving Wind — Tools > Proi > Setup Magica Cloth 참고)
/// </summary>
public class ProiHairFlutter : MonoBehaviour
{
    [Tooltip("바람이 닿는 범위 (m, 엉덩이 중심)")]
    [SerializeField] float radius = 2.5f;

    [Header("장면별 바람 세기 (m/s)")]
    [SerializeField] float clashGust = 8f;
    [Tooltip("차징 시작 → 완충까지 솟는 열기")]
    [SerializeField] Vector2 chargeUpdraft = new Vector2(3f, 8f);
    [Tooltip("소용돌이 회전 속도 (회/초)")]
    [SerializeField] float swirlRate = 0.7f;
    [SerializeField] float leapWind = 7f;
    [SerializeField] float apexWind = 3f;
    [SerializeField] float diveWind = 10f;
    [SerializeField] float slamBurst = 8f;
    [Tooltip("땅 찍은 뒤 바람이 잦아드는 시간 (초)")]
    [SerializeField] float slamFade = 0.8f;

    [Header("순간 충격 (m/s)")]
    [SerializeField] float clashKick = 1.2f;
    [SerializeField] float slamKick = 1.2f;

    ProiUltimate ultimate;
    MagicaCloth[] cloths;
    MagicaWindZone zone;
    Transform hips;
    float appliedScale = 1f;
    bool wasActive;

    /// <summary>지금 부는 연출 바람 (테스트 / 디버그용).</summary>
    public float WindSpeed => zone != null && zone.enabled ? zone.main : 0f;

    void Start()
    {
        cloths = GetComponentsInChildren<MagicaCloth>(true);
        ultimate = GetComponent<ProiUltimate>();
        if (ultimate == null) ultimate = GetComponentInParent<ProiUltimate>();
        if (ultimate == null) ultimate = transform.root.GetComponentInChildren<ProiUltimate>();
        hips = FindDeep(transform, "Hips");
        if (cloths.Length == 0 || ultimate == null) { enabled = false; return; }

        var go = new GameObject("Magica_UltimateWind");
        go.transform.SetParent(transform, false);
        zone = go.AddComponent<MagicaWindZone>();
        zone.mode = MagicaWindZone.Mode.SphereDirection;
        zone.isAddition = true;          // on top of any scene wind and the cloths' own moving wind
        zone.radius = radius;
        zone.turbulence = 1f;
        zone.main = 0f;
        zone.enabled = false;
        ultimate.OnClash += OnClash;
        ultimate.OnSlam += OnSlam;
    }

    void OnDestroy()
    {
        if (ultimate == null) return;
        ultimate.OnClash -= OnClash;
        ultimate.OnSlam -= OnSlam;
    }

    void OnClash(Vector3 at) => Kick(-ultimate.Forward + Vector3.up * 0.5f, clashKick);
    void OnSlam(Vector3 at) => Kick(Vector3.up - ultimate.Forward * 0.5f, slamKick);

    void LateUpdate()
    {
        bool active = ultimate.Active;
        if (active != wasActive)
        {
            wasActive = active;
            zone.enabled = active;
            if (!active) zone.main = 0f;
        }
        SetTimeScale(active ? Mathf.Min(1f, ultimate.OwnTimeScale) : 1f);
        if (!active) return;

        zone.transform.position = hips != null ? hips.position : transform.position;
        Vector3 fwd = ultimate.Forward.sqrMagnitude > 0.01f ? ultimate.Forward : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Wind(ultimate.SkillTime, fwd, right, out Vector3 dir, out float speed);
        zone.main = Mathf.Clamp(speed, 0f, 30f);
        if (dir.sqrMagnitude > 1e-4f) zone.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    /// <summary>궁극기 t 초의 바람 방향과 세기.</summary>
    void Wind(float t, Vector3 fwd, Vector3 right, out Vector3 dir, out float speed)
    {
        float clash = ultimate.ClashTime, charge0 = ultimate.ChargeStart, charge1 = ultimate.ChargeFull;
        float leap = ultimate.DashStart, apex = ultimate.ApexTime, dive = ultimate.DiveTime, slam = ultimate.SlamTime;
        dir = -fwd; speed = 0f;
        if (t < clash) return;
        if (t < charge0)
        {   // the shockwave of the fists meeting, dying out
            float k = Mathf.Clamp01((t - clash) / 0.45f);
            dir = -fwd + Vector3.up * 0.3f;
            speed = clashGust * (1f - k) * (1f - k);
            return;
        }
        if (t < leap)
        {   // heat rising from the ground around her, swirling, stronger as the fist charges
            float k = Mathf.Clamp01(Mathf.InverseLerp(charge0, charge1, t));
            float a = t * swirlRate * Mathf.PI * 2f;
            dir = Vector3.up + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * 0.45f;
            speed = Mathf.Lerp(chargeUpdraft.x, chargeUpdraft.y, k * k);
            return;
        }
        if (t < apex)
        {   // shooting up and forward: the air streams down and back past her
            dir = -Vector3.up * 0.8f - fwd * 0.6f;
            speed = leapWind;
            return;
        }
        if (t < dive)
        {   // hanging at the top: the hair floats up
            dir = Vector3.up - fwd * 0.2f;
            speed = apexWind;
            return;
        }
        if (t < slam)
        {   // diving down and forward: the hair streams up and back
            dir = Vector3.up * 0.7f - fwd * 0.7f;
            speed = diveWind;
            return;
        }
        {   // the impact throws air up and back off the ground (the hair would fall over her face), then it settles
            float k = Mathf.Clamp01((t - slam) / Mathf.Max(slamFade, 0.05f));
            dir = Vector3.up - fwd * 0.5f;
            speed = slamBurst * (1f - k) * (1f - k);
        }
    }

    void Kick(Vector3 dir, float velocity)
    {
        foreach (var c in cloths)
            if (c != null && c.isActiveAndEnabled) c.AddForce(dir, velocity, ClothForceMode.VelocityAdd);
    }

    void SetTimeScale(float scale)
    {
        if (Mathf.Abs(scale - appliedScale) < 0.001f) return;
        appliedScale = scale;
        foreach (var c in cloths)
            if (c != null && c.isActiveAndEnabled) c.SetTimeScale(scale);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
#endif
