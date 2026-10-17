using AcRoguelike;
using UnityEngine;

/// <summary>
/// 궁극기 연출이 카메라에 시키는 일 (연출 시점 / 복귀 / 흔들림 / 기본 구도). 플레이그라운드는 ProiQuarterViewCamera,
/// 게임(헌터 교체) 안은 게임의 IsometricFollowCamera 를 ProiGameCamera 로 감싸서 같은 연출을 그대로 씁니다.
/// </summary>
public interface IProiCamera
{
    void SetCinematic(Vector3 position, Quaternion rotation, float fov, float blendTime);
    void ClearCinematic(float blendTime);
    /// <summary>흔들림: amplitude (m), duration (초), 방향, zoom = 순간 줌인 (시야각 도)</summary>
    void Shake(float amplitude, float duration, Vector3 direction, float zoom = 0f);
    Quaternion BaseRotation { get; }
    float BaseFieldOfView { get; }
    /// <summary>target(발밑)을 비추는 기본 구도.</summary>
    void BasePose(Vector3 target, out Vector3 pos, out Quaternion rot, out float fov);
}

public static class ProiCinematicCamera
{
    /// <summary>메인 카메라의 연출 카메라: 플레이그라운드의 쿼터뷰 카메라, 아니면 게임 카메라. 둘 다 없으면 null.</summary>
    public static IProiCamera Find()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var quarter = cam.GetComponent<ProiQuarterViewCamera>();
        if (quarter != null) return quarter;
        var follow = cam.GetComponent<IsometricFollowCamera>();
        return follow != null ? new ProiGameCamera(follow) : null;
    }
}

/// <summary>게임 카메라(IsometricFollowCamera)에 궁극기 연출 시점을 그대로 얹습니다.</summary>
public sealed class ProiGameCamera : IProiCamera
{
    readonly IsometricFollowCamera follow;

    public ProiGameCamera(IsometricFollowCamera follow) { this.follow = follow; }

    public void SetCinematic(Vector3 position, Quaternion rotation, float fov, float blendTime)
    {
        if (follow) follow.SetCinematic(position, rotation, fov, blendTime);
    }

    public void ClearCinematic(float blendTime)
    {
        if (follow) follow.ClearCinematic(blendTime);
    }

    public void Shake(float amplitude, float duration, Vector3 direction, float zoom = 0f)
    {
        if (!follow) return;
        follow.AddShake(amplitude, duration);
        // The quarter-view camera punches its field of view (degrees); the game camera pushes its distance instead.
        if (zoom != 0f) follow.AddZoom(zoom / Mathf.Max(1f, BaseFieldOfView), duration * .2f, 0f, duration * .8f);
    }

    public Quaternion BaseRotation => follow ? follow.BaseRotation : ProiQuarterViewCamera.DefaultRotation;
    public float BaseFieldOfView => follow ? follow.BaseFieldOfView : ProiQuarterViewCamera.DefaultFieldOfView;

    public void BasePose(Vector3 target, out Vector3 pos, out Quaternion rot, out float fov)
    {
        rot = BaseRotation;
        fov = BaseFieldOfView;
        float distance = follow ? follow.distance : ProiQuarterViewCamera.DefaultDistance;
        pos = target + Vector3.up * .55f + (follow ? follow.focusOffset : Vector3.zero) - rot * Vector3.forward * distance;
    }
}
