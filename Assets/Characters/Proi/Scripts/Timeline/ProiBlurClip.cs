using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>배경 블러 클립: 이 클립 동안 캐릭터(가슴 ~ 얼굴)에 초점을 맞추고 주변을 흐립니다 (심도, 플레이 중에만).
/// 클립의 Ease In / Ease Out 길이가 블러가 차오르고 빠지는 시간입니다.</summary>
[System.Serializable]
public class ProiBlurClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("조리개 (f 값): 작을수록 주변이 많이 흐려짐")]
    public float aperture = 2.2f;
    [Tooltip("렌즈 초점 거리 (mm): 클수록 배경이 많이 흐려짐")]
    public float focalLength = 70f;
    [Tooltip("초점을 캐릭터보다 앞(-) / 뒤(+)로 옮김 (m)")]
    public float focusOffset;
    [Tooltip("초점 위치: 0 = 가슴 ~ 얼굴, 1 = 오른주먹 (차징 불꽃)")]
    [Range(0f, 1f)] public float fistFocus;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<ProiBlurBehaviour>.Create(graph);
        var b = p.GetBehaviour();
        b.aperture = aperture;
        b.focalLength = focalLength;
        b.focusOffset = focusOffset;
        b.fistFocus = fistFocus;
        return p;
    }
}
