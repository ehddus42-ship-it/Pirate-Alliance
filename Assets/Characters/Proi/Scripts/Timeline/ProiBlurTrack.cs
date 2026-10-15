using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

public class ProiBlurBehaviour : PlayableBehaviour
{
    public float aperture = 2.2f, focalLength = 70f, focusOffset, fistFocus;
}

/// <summary>
/// 배경 블러 트랙 (연결 대상 없음): 클립이 걸린 동안 URP 심도(Bokeh)로 캐릭터에 초점을 맞추고 주변을 흐립니다.
/// 초점 거리는 매 프레임 카메라에서 캐릭터 가슴 ~ 얼굴(ProiUltimate.FocusPoint) / 오른주먹 사이(클립의 fistFocus)까지. 플레이 중에만 보입니다.
/// </summary>
[TrackColor(0.3f, 0.55f, 0.35f)]
[TrackClipType(typeof(ProiBlurClip))]
public class ProiBlurTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mixer = ScriptPlayable<ProiBlurMixer>.Create(graph, inputCount);
        mixer.GetBehaviour().ult = go != null ? go.GetComponent<ProiUltimate>() : null;
        return mixer;
    }
}

public class ProiBlurMixer : PlayableBehaviour
{
    const float NoBlurAperture = 32f;     // the far end of the aperture range: practically sharp

    public ProiUltimate ult;
    Volume volume;
    VolumeProfile profile;
    DepthOfField dof;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!Application.isPlaying) return;
        float w = 0f, aperture = 0f, focal = 0f, offset = 0f, fist = 0f;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float k = playable.GetInputWeight(i);
            if (k <= 0f) continue;
            var b = ((ScriptPlayable<ProiBlurBehaviour>)playable.GetInput(i)).GetBehaviour();
            w += k; aperture += b.aperture * k; focal += b.focalLength * k; offset += b.focusOffset * k; fist += b.fistFocus * k;
        }
        Apply(w, w > 0f ? aperture / w : NoBlurAperture, w > 0f ? focal / w : 50f, w > 0f ? offset / w : 0f, w > 0f ? fist / w : 0f);
    }

    public override void OnBehaviourPause(Playable playable, FrameData info) => Apply(0f, NoBlurAperture, 50f, 0f, 0f);

    public override void OnPlayableDestroy(Playable playable)
    {
        if (volume != null) Object.Destroy(volume.gameObject);
        if (profile != null) Object.Destroy(profile);
    }

    void Apply(float w, float aperture, float focal, float offset, float fist)
    {
        if (!Application.isPlaying) return;
        if (w <= 0.001f) { if (volume != null) volume.weight = 0f; return; }
        if (volume == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(3f);
            dof.aperture.Override(NoBlurAperture);
            dof.focalLength.Override(50f);
            volume = new GameObject("ProiUltimate Blur Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 30f;
            volume.sharedProfile = profile;
        }
        Camera cam = Camera.main;
        Vector3 focus = ult != null ? Vector3.Lerp(ult.FocusPoint, ult.ChargeFistPoint, fist) : Vector3.zero;
        float dist = cam != null ? Vector3.Dot(focus - cam.transform.position, cam.transform.forward) : 3f;
        volume.weight = 1f;
        dof.focusDistance.value = Mathf.Max(dist + offset, 0.3f);
        dof.aperture.value = Mathf.Lerp(NoBlurAperture, aperture, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w)));   // the blur fills in with the clip's ease
        dof.focalLength.value = focal;
    }
}
