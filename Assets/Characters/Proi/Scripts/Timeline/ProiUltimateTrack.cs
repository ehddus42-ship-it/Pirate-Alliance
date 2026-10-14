using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Proi 궁극기 트랙 (ProiUltimate 에 연결): 비트 마커를 담고, 에디터에서 재생바를 움직일 때 아치 점프의 전진 이동을 미리 보여 줍니다.
/// 플레이 중에는 아무것도 하지 않습니다 (이동 / 이펙트 / 타격감은 ProiUltimate 가 마커 시간에 맞춰 처리).
/// </summary>
[TrackColor(0.86f, 0.36f, 0.12f)]
[TrackBindingType(typeof(ProiUltimate))]
[TrackClipType(typeof(ProiUltimateMoveClip))]
public class ProiUltimateTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mixer = ScriptPlayable<ProiUltimateMoveMixer>.Create(graph, inputCount);
        mixer.GetBehaviour().director = go != null ? go.GetComponent<PlayableDirector>() : null;
        return mixer;
    }

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {   // the preview moves her: let Timeline put her back afterwards
        var ult = director.GetGenericBinding(this) as ProiUltimate;
        if (ult == null) return;
        driver.AddFromName<Transform>(ult.gameObject, "m_LocalPosition.x");
        driver.AddFromName<Transform>(ult.gameObject, "m_LocalPosition.y");
        driver.AddFromName<Transform>(ult.gameObject, "m_LocalPosition.z");
        base.GatherProperties(director, driver);
    }
}

public class ProiUltimateMoveMixer : PlayableBehaviour
{
    public PlayableDirector director;
    bool started;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (Application.isPlaying) return;
        var ult = playerData as ProiUltimate;
        if (ult == null || director == null) return;
        if (!started)
        {   // where she stands when the preview starts (Timeline restores it afterwards)
            started = true;
            ult.BeginPreview(director.playableAsset as TimelineAsset);
        }
        ult.PreviewAt((float)director.time);
    }
}
