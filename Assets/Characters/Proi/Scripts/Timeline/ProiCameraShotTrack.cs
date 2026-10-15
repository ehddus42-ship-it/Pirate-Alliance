using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>카메라 샷 종류 (ProiUltimate.ShotPose 가 구도를 계산).</summary>
public enum ProiShotType
{
    Front,          // 정면: 캐릭터에서 yaw 만큼 돌린 방향으로 distance, 높이 height, 바라보는 높이 lookHeight
    Fists,          // 두 주먹이 만나는 자리(가슴 앞, 높이 lookHeight)를 distance 거리에서 클로즈업
    ChargeAngle,    // 차징 클로즈업: yaw 만큼 돌아간 방향에서 허리의 주먹과 가슴 사이를 봄
    ArcWide,        // 쿼터뷰 각도로 점프 아치 전체 (출발점 ~ 착지점)
    Landing,        // 쿼터뷰 각도로 착지점을 당겨 봄
    PillarWide,     // 쿼터뷰 각도로 캐릭터와 불기둥 전체
    Gameplay,       // 기본 쿼터뷰로 복귀
    FistCloseUp,    // 오른주먹(실제 위치)을 yaw 방향 distance 거리, 주먹보다 height 높은 곳에서 봄 (망원 익스트림 클로즈업)
    DiveFollow,     // 내려꽂기 추적: 캐릭터 가슴 뒤(yaw 만큼 돌림) distance, 위 height 에서 따라가며 착지점(높이 lookHeight)을 봄
    UpperBody,      // 상반신: 가슴과 얼굴 사이(실제 자세, + lookHeight)를 yaw 방향 distance, 그보다 height 높은 곳에서 봄
    ChargeFist,     // 차징 주먹 메인: 오른주먹(불꽃이 모이는 곳)을 중심에 두고 얼굴이 함께 걸리게 봄 (+ lookHeight, 높이 height)
}

[Serializable]
public class ProiShot
{
    public ProiShotType type;
    [Tooltip("이 구도로 옮겨 가는 시간 (실제 초, 0 = 하드 컷). 히트스톱 / 슬로모션과 상관없이 흐름")]
    public float blend;
    [Tooltip("옮겨 가는 가감속: EaseOut = 빠르게 들어가 끝에서 멈추듯 (명조식), Smooth = 천천히 출발 / 도착")]
    public ProiEase ease = ProiEase.EaseOut;
    [Tooltip("카메라 거리 (m)")]
    public float distance = 4f;
    [Tooltip("카메라 높이 (m) — 정면 / 주먹 / 차징 / 추적 샷")]
    public float height = 1f;
    [Tooltip("바라보는 높이 (m) — 정면 / 주먹 / 차징 / 추적 샷")]
    public float lookHeight = 1f;
    [Tooltip("정면에서 캐릭터 오른쪽으로 돌린 각도 — 정면 / 주먹 / 차징 / 추적 샷")]
    public float yaw;
    [Tooltip("시야각 — 정면 / 주먹 / 차징 / 추적 샷 (작을수록 망원)")]
    public float fov = 34f;
    [Tooltip("기본 쿼터뷰 시야각에 더할 값 (- = 좁게) — 와이드 / 착지점 / 불기둥 샷")]
    public float fovOffset;
    [Tooltip("화면 기울기 (도, + = 시계 방향) — 더치 앵글")]
    public float roll;

    [Header("클립 동안 움직임 (클립 끝에서 아래 값이 되도록 이어서 이동: 오빗 / 달리 / 줌)")]
    public bool moving;
    [Tooltip("클립 동안 움직임의 가감속")]
    public ProiEase moveEase = ProiEase.EaseOut;
    public float endDistance = 4f;
    public float endHeight = 1f;
    public float endLookHeight = 1f;
    public float endYaw;
    public float endFov = 34f;
    public float endFovOffset;
    public float endRoll;

    /// <summary>클립 진행도 p (0..1) 에서의 구도 값.</summary>
    public ProiShot At(float p)
    {
        if (!moving) return this;
        float k = ProiQuarterViewCamera.Ease(moveEase, p);
        return new ProiShot
        {
            type = type, blend = blend, ease = ease,
            distance = Mathf.Lerp(distance, endDistance, k),
            height = Mathf.Lerp(height, endHeight, k),
            lookHeight = Mathf.Lerp(lookHeight, endLookHeight, k),
            yaw = Mathf.Lerp(yaw, endYaw, k),
            fov = Mathf.Lerp(fov, endFov, k),
            fovOffset = Mathf.Lerp(fovOffset, endFovOffset, k),
            roll = Mathf.Lerp(roll, endRoll, k),
        };
    }

    /// <summary>움직이는 샷의 끝 값을 시작 값과 같게 맞춥니다 (그다음 바꿀 값만 고치면 됨).</summary>
    public ProiShot WithEnd()
    {
        endDistance = distance; endHeight = height; endLookHeight = lookHeight; endYaw = yaw;
        endFov = fov; endFovOffset = fovOffset; endRoll = roll;
        return this;
    }
}

/// <summary>
/// 궁극기 카메라 트랙 (Main Camera 에 연결). 플레이 중에는 샷마다 ProiQuarterViewCamera.SetCinematic 을 불러 실제 시간으로
/// 옮겨 가고(흔들림 / 줌 펀치는 코드가 그 위에 얹음), 에디터에서는 재생바 위치의 구도로 카메라를 바로 놓아 미리 보여 줍니다.
/// </summary>
[TrackColor(0.18f, 0.44f, 0.6f)]
[TrackBindingType(typeof(Camera))]
[TrackClipType(typeof(ProiCameraShotClip))]
public class ProiCameraShotTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mixer = ScriptPlayable<ProiCameraShotMixer>.Create(graph, inputCount);
        var b = mixer.GetBehaviour();
        b.director = go != null ? go.GetComponent<PlayableDirector>() : null;
        b.clips = new System.Collections.Generic.List<TimelineClip>(GetClips()).ToArray();
        Array.Sort(b.clips, (x, y) => x.start.CompareTo(y.start));
        return mixer;
    }

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        var cam = director.GetGenericBinding(this) as Camera;
        if (cam == null) return;
        foreach (string p in new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                                     "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" })
            driver.AddFromName<Transform>(cam.gameObject, p);
        driver.AddFromName<Camera>(cam.gameObject, "field of view");
        base.GatherProperties(director, driver);
    }
}

public class ProiCameraShotMixer : PlayableBehaviour
{
    public PlayableDirector director;
    public TimelineClip[] clips;
    bool[] fired;
    double lastTime = -1.0;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var cam = playerData as Camera;
        if (cam == null || director == null || clips == null || clips.Length == 0) return;
        var ult = director.GetComponent<ProiUltimate>();
        if (ult == null) return;
        double t = director.time;

        if (Application.isPlaying)
        {   // each shot once, when the playhead reaches it (several in one frame: only the last one counts)
            if (fired == null || fired.Length != clips.Length || t + 1e-4 < lastTime) fired = new bool[clips.Length];
            lastTime = t;
            int latest = -1;
            for (int i = 0; i < clips.Length; i++)
                if (!fired[i] && clips[i].start <= t + 1e-6) { fired[i] = true; latest = i; }
            if (latest >= 0) ult.PlayShot(Shot(latest), clips[latest].start, clips[latest].duration);
            return;
        }

        // edit-mode preview: the shot under the playhead, blending in from the one before it
        int cur = -1;
        for (int i = 0; i < clips.Length; i++) if (clips[i].start <= t + 1e-6) cur = i;
        if (cur < 0) return;
        ProiShot s = Shot(cur);
        ult.ShotPose(s.At(Progress(cur, t)), out Vector3 pos, out Quaternion rot, out float fov);
        if (s.blend > 0f && cur > 0)
        {
            float k = ProiQuarterViewCamera.Ease(s.ease, (float)(t - clips[cur].start) / s.blend);
            ult.ShotPose(Shot(cur - 1).At(Progress(cur - 1, t)), out Vector3 p0, out Quaternion r0, out float f0);
            pos = Vector3.Lerp(p0, pos, k); rot = Quaternion.Slerp(r0, rot, k); fov = Mathf.Lerp(f0, fov, k);
        }
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = fov;
    }

    float Progress(int i, double t) => Mathf.Clamp01((float)((t - clips[i].start) / Math.Max(clips[i].duration, 1e-4)));

    ProiShot Shot(int i) => ((ProiCameraShotClip)clips[i].asset).shot;
}
