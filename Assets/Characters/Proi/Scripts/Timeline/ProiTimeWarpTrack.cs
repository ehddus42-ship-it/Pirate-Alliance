using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class ProiTimeWarpBehaviour : PlayableBehaviour
{
    public AnimationCurve speed;
    bool applied;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!Application.isPlaying || speed == null) return;
        ProiHitFeedback.BaseScale = Mathf.Max(speed.Evaluate((float)playable.GetTime()), 0.01f);
        applied = true;
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (!applied || !Application.isPlaying) return;
        applied = false;
        ProiHitFeedback.BaseScale = 1f;
    }
}

/// <summary>궁극기 배속 트랙 (연결 대상 없음).</summary>
[TrackColor(0.42f, 0.31f, 0.63f)]
[TrackClipType(typeof(ProiTimeWarpClip))]
public class ProiTimeWarpTrack : TrackAsset { }
