using UnityEngine;

/// <summary>
/// 타격감 연출: 히트스톱 (맞는 순간 게임 시간을 아주 짧게 거의 멈춤) + 그 뒤의 슬로모션, 연출용 기본 배속.
///  - HitStop(초) : Time.timeScale 을 잠깐 낮췄다가 실제 시간 기준으로 되돌립니다. 겹쳐서 호출되면 더 긴 쪽으로 이어집니다.
///  - HitStop(초, 배율, 슬로 초, 슬로 배율) : 멈춘 뒤 슬로모션으로 이어지고, 슬로 구간 동안 기본 배속으로 서서히 돌아옵니다.
///  - BaseScale : 히트스톱이 없을 때의 게임 배속 (궁극기가 구간마다 빠르게 / 느리게 바꿈, 평소 1).
/// (카메라 흔들림은 ProiQuarterViewCamera.Shake)
/// </summary>
public class ProiHitFeedback : MonoBehaviour
{
    static ProiHitFeedback inst;
    static float baseScale = 1f;
    float until = -1f, slowUntil = -1f;
    float stopScale = 0.03f, slowScale = 1f;

    /// <summary>히트스톱이 없을 때의 게임 배속.</summary>
    public static float BaseScale
    {
        get => baseScale;
        set
        {
            baseScale = Mathf.Max(value, 0.01f);
            if (inst != null) inst.Apply();
            else if (!Mathf.Approximately(baseScale, 1f)) Ensure().Apply();
            else Time.timeScale = 1f;
        }
    }

    /// <summary>seconds 동안 (실제 시간) 게임 시간을 scale 배로 늦추고, 이어서 slowSeconds 동안 slowScale 배에서 기본 배속으로 돌아옵니다.</summary>
    public static void HitStop(float seconds, float scale = 0.03f, float slowSeconds = 0f, float slowScale = 1f)
    {
        if (seconds <= 0f && slowSeconds <= 0f) return;
        Ensure().Begin(seconds, scale, slowSeconds, slowScale);
    }

    static ProiHitFeedback Ensure()
    {
        if (inst == null)
        {
            var go = new GameObject("ProiHitFeedback");
            go.hideFlags = HideFlags.HideInHierarchy;
            inst = go.AddComponent<ProiHitFeedback>();
        }
        return inst;
    }

    void Begin(float seconds, float scale, float slowSeconds, float slow)
    {
        float now = ProiClock.Now;
        until = Mathf.Max(until, now + seconds);
        stopScale = scale;
        if (slowSeconds > 0f)
        {
            slowUntil = Mathf.Max(slowUntil, until + slowSeconds);
            slowScale = slow;
        }
        Apply();
    }

    float Current()
    {
        float now = ProiClock.Now;
        if (now < until) return stopScale;
        if (now < slowUntil)
        {   // eases back from the slow-mo to the base speed
            float k = Mathf.Clamp01((now - until) / Mathf.Max(slowUntil - until, 1e-3f));
            return Mathf.Lerp(slowScale, baseScale, k * k);
        }
        return baseScale;
    }

    void Apply() => Time.timeScale = Current();

    void Update() => Apply();

    void OnDisable()
    {
        until = slowUntil = -1f;
        baseScale = 1f;
        Time.timeScale = 1f;
    }
}
