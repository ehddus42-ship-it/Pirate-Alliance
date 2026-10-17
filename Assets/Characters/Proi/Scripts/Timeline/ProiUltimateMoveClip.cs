using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 전진 이동 미리보기 구간 (에디터 전용 표시, 플레이 중에는 ProiUltimate 가 직접 옮김).
/// Timeline 에 저장되는 클래스라 파일 이름이 클래스 이름과 같아야 합니다.
/// </summary>
[Serializable]
public class ProiUltimateMoveClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.None;
    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
}
