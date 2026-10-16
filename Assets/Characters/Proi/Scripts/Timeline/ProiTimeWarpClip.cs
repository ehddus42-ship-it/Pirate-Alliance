using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 연출 배속 클립: 클립 안의 시간(초)마다 게임 배속을 커브로 정합니다 (1 = 보통, 2 = 두 배 빠르게, 0.5 = 슬로).
/// 플레이 중에만 ProiHitFeedback.BaseScale 을 바꾸고, 히트스톱 / 슬로모션은 그 위에 따로 걸립니다. 클립이 끝나면 1 로 돌아옵니다.
/// Timeline 에 저장되는 클래스라 파일 이름이 클래스 이름과 같아야 합니다.
/// </summary>
[Serializable]
public class ProiTimeWarpClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("x = 클립 안의 시간 (초), y = 게임 배속")]
    public AnimationCurve speed = AnimationCurve.Constant(0f, 1f, 1f);
    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<ProiTimeWarpBehaviour>.Create(graph);
        p.GetBehaviour().speed = speed;
        return p;
    }
}
