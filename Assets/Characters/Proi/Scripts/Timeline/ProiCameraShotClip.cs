using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 카메라 샷 클립: 클립이 시작하는 순간 그 구도로 컷 / 이동합니다 (클립 길이는 다음 샷까지 보기 좋게 둔 것).
/// Timeline 에 저장되는 클래스라 파일 이름이 클래스 이름과 같아야 합니다.
/// </summary>
[Serializable]
public class ProiCameraShotClip : PlayableAsset, ITimelineClipAsset
{
    public ProiShot shot = new ProiShot();
    public ClipCaps clipCaps => ClipCaps.None;
    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
}
