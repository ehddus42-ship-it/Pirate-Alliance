using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 쿼터뷰 카메라: 대상을 비스듬한 위쪽에서 고정 각도로 내려다보며 부드럽게 따라갑니다.
/// 마우스 휠 = 줌, E = 시점 회전 (Q 는 궁극기). Shake() = 타격 시 카메라 흔들림 (실제 시간 기준이라 히트스톱 중에도 흔들림).
/// SetCinematic / ClearCinematic = 연출용 고정 시점(궁극기 정면샷 등)으로 부드럽게 옮겼다가 기본 구도로 돌아옴.
/// </summary>
public class ProiQuarterViewCamera : MonoBehaviour, IProiCamera
{
    [SerializeField] Transform target;

    [Header("View")]
    [Tooltip("내려다보는 각도 (도). 클수록 위에서 수직으로 내려다봄")]
    [SerializeField] float pitch = 50f;
    [Tooltip("수평 방향 (도). 45 = 정대각선 쿼터뷰")]
    [SerializeField] float yaw = 35f;
    [SerializeField] float distance = 14f;
    [Tooltip("시야각. 낮을수록 원근감이 줄어 아이소메트릭에 가까워짐")]
    [SerializeField] float fieldOfView = 35f;
    [Tooltip("바라보는 지점의 높이 (캐릭터 발 기준)")]
    [SerializeField] float lookHeight = 0.9f;
    [SerializeField] float smoothTime = 0.12f;

    [Header("Zoom (마우스 휠)")]
    [SerializeField] float minDistance = 8f;
    [SerializeField] float maxDistance = 20f;
    [SerializeField] float zoomStep = 1.2f;

    [Header("Rotate (E)")]
    [SerializeField] bool allowRotate = true;
    [SerializeField] float rotateStep = 90f;
    [SerializeField] float rotateSpeed = 360f;

    Camera cam;
    Vector3 focus, velocity;
    float yawCurrent, distanceCurrent;
    float shakeAmp, shakeDur, shakeStart = -10f, shakeSeed;
    float zoomAmt, zoomDur = 1f, zoomStart = -10f;      // the zoom punch runs on its own: a later shake without one doesn't cut it off
    Vector3 shakeDir;
    Vector3 cinePos, cineFromPos;
    Quaternion cineRot = Quaternion.identity, cineFromRot = Quaternion.identity;
    float cineFov, cineFromFov, cineWeight, cineTarget, cineBlend = 0.3f, cineMove = 1f, cineMoveTime = 0.3f;

    /// <summary>연출 시점으로 blendTime(초, 실제 시간) 동안 부드럽게 이동. 이미 연출 시점이면 그 시점에서 새 시점으로 이어서 이동
    /// (예: 정면샷 -> 주먹으로 천천히 다가가는 샷). 흔들림은 그대로 적용됩니다.</summary>
    public void SetCinematic(Vector3 position, Quaternion rotation, float fov, float blendTime)
    {
        if (cineWeight > 0f)
        {   // already on a cinematic shot: move on from where the camera is now
            CinePose(out cineFromPos, out cineFromRot, out cineFromFov);
            cineMove = 0f;
            cineMoveTime = Mathf.Max(blendTime, 0.01f);
        }
        else cineMove = 1f;
        cinePos = position; cineRot = rotation; cineFov = fov;
        cineTarget = 1f; cineBlend = Mathf.Max(blendTime, 0.01f);
    }

    void CinePose(out Vector3 pos, out Quaternion rot, out float fov)
    {
        float m = Mathf.SmoothStep(0f, 1f, cineMove);
        pos = Vector3.Lerp(cineFromPos, cinePos, m);
        rot = Quaternion.Slerp(cineFromRot, cineRot, m);
        fov = Mathf.Lerp(cineFromFov, cineFov, m);
    }

    /// <summary>기본 쿼터뷰 구도의 회전 / 시야각 (연출 시점을 같은 각도로 잡을 때).</summary>
    public Quaternion BaseRotation => Quaternion.Euler(pitch, yawCurrent, 0f);
    public float BaseFieldOfView => fieldOfView;

    // the inspector defaults, for poses worked out where the component isn't on the camera yet (timeline preview in edit mode)
    public const float DefaultPitch = 50f, DefaultYaw = 35f, DefaultDistance = 14f, DefaultFieldOfView = 35f, DefaultLookHeight = 0.9f;
    public static Quaternion DefaultRotation => Quaternion.Euler(DefaultPitch, DefaultYaw, 0f);

    /// <summary>target(발밑)을 비추는 기본 쿼터뷰 구도. 컴포넌트가 없으면 기본값으로 계산합니다.</summary>
    public static void BasePose(ProiQuarterViewCamera qcam, Vector3 target, out Vector3 pos, out Quaternion rot, out float fov)
    {
        rot = qcam != null ? qcam.BaseRotation : DefaultRotation;
        fov = qcam != null ? qcam.fieldOfView : DefaultFieldOfView;
        float dist = qcam != null ? qcam.distance : DefaultDistance, look = qcam != null ? qcam.lookHeight : DefaultLookHeight;
        pos = target + Vector3.up * look - rot * Vector3.forward * dist;
    }

    void IProiCamera.BasePose(Vector3 target, out Vector3 pos, out Quaternion rot, out float fov) => BasePose(this, target, out pos, out rot, out fov);

    /// <summary>기본 쿼터뷰 구도로 blendTime(초, 실제 시간) 동안 부드럽게 복귀.</summary>
    public void ClearCinematic(float blendTime)
    {
        cineTarget = 0f; cineBlend = Mathf.Max(blendTime, 0.01f);
    }

    /// <summary>타격 흔들림: amplitude (m), duration (초, 실제 시간), 공격 방향으로 살짝 밀림, zoom = 순간 줌인 (시야각 도)</summary>
    public void Shake(float amplitude, float duration, Vector3 direction, float zoom = 0f)
    {
        if (zoom != 0f && Mathf.Abs(zoom) >= Mathf.Abs(CurrentZoom()))
        {
            zoomAmt = zoom; zoomDur = Mathf.Max(duration, 0.01f); zoomStart = ProiClock.Now;
        }
        if (CurrentShake() > amplitude) return;                 // a stronger shake is still running
        shakeAmp = amplitude; shakeDur = Mathf.Max(duration, 0.01f); shakeStart = ProiClock.Now;
        shakeDir = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        shakeSeed = Random.Range(0f, 100f);
    }

    float CurrentZoom()
    {   // punches in fast, eases back out over the rest of its time
        float zt = (ProiClock.Now - zoomStart) / zoomDur;
        return zt < 1f ? zoomAmt * Mathf.Sin(Mathf.Clamp01(zt * 2.5f) * Mathf.PI * 0.5f) * (1f - zt) : 0f;
    }

    float ShakeT() => (ProiClock.Now - shakeStart) / shakeDur;

    float CurrentShake()
    {
        float t = ShakeT();
        return t >= 1f ? 0f : shakeAmp * (1f - t) * (1f - t);
    }

    Vector3 ShakeOffset()
    {
        float t = ShakeT();
        if (t >= 1f) return Vector3.zero;
        float k = (1f - t) * (1f - t);
        float tt = (ProiClock.Now - shakeStart) * 24f;              // slower jitter: less dizzying
        var n = new Vector3(Mathf.PerlinNoise(shakeSeed, tt) - 0.5f, (Mathf.PerlinNoise(shakeSeed + 7.1f, tt) - 0.5f) * 0.5f,
                            Mathf.PerlinNoise(shakeSeed + 13.7f, tt) - 0.5f) * 2f;     // mostly sideways, little vertical
        Vector3 kick = shakeDir * Mathf.Sin(Mathf.Clamp01(t * 3f) * Mathf.PI) * 0.5f;   // a quick push with the hit
        return (n + kick) * shakeAmp * k;
    }

    public void Configure(Transform newTarget)
    {
        target = newTarget;
        Snap();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        Snap();
    }

    void Snap()
    {
        yawCurrent = yaw;
        distanceCurrent = distance;
        if (target != null) focus = target.position + Vector3.up * lookHeight;
        velocity = Vector3.zero;
        Apply();
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;
        ReadInput();

        cineWeight = Mathf.MoveTowards(cineWeight, cineTarget, ProiClock.Delta / cineBlend);
        cineMove = Mathf.MoveTowards(cineMove, 1f, ProiClock.Delta / cineMoveTime);
        CinePose(out Vector3 cPos, out Quaternion cRot, out float cFov);
        if (cam != null)
        {
            cam.orthographic = false;
            cam.fieldOfView = Mathf.Lerp(fieldOfView, cFov, Mathf.SmoothStep(0f, 1f, cineWeight)) - CurrentZoom();
        }
        yawCurrent = Mathf.MoveTowardsAngle(yawCurrent, yaw, rotateSpeed * dt);
        distanceCurrent = Mathf.Lerp(distanceCurrent, distance, 1f - Mathf.Exp(-10f * dt));
        focus = Vector3.SmoothDamp(focus, target.position + Vector3.up * lookHeight, ref velocity, smoothTime);
        Apply(cPos, cRot);
    }

    void ReadInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }

        Keyboard kb = Keyboard.current;
        if (allowRotate && kb != null)
        {
            if (kb.eKey.wasPressedThisFrame) yaw -= rotateStep;
        }
    }

    void Apply() => Apply(cinePos, cineRot);

    void Apply(Vector3 cPos, Quaternion cRot)
    {
        Quaternion rot = Quaternion.Euler(pitch, yawCurrent, 0f);
        Vector3 pos = focus - rot * Vector3.forward * distanceCurrent;
        if (cineWeight > 0f)
        {
            float w = Mathf.SmoothStep(0f, 1f, cineWeight);
            pos = Vector3.Lerp(pos, cPos, w);
            rot = Quaternion.Slerp(rot, cRot, w);
        }
        transform.SetPositionAndRotation(pos + ShakeOffset(), rot);
    }
}
