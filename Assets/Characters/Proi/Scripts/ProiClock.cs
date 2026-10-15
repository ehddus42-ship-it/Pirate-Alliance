using UnityEngine;

/// <summary>
/// 연출용 실제 시간 시계: 게임 배속(Time.timeScale)과 상관없이 흐르는 시간 (히트스톱 / 슬로모션 / 카메라 전환 / 흔들림이 씀).
/// 평소에는 Time.unscaledTime 과 같고, 고정 프레임 녹화 중(Time.captureDeltaTime > 0, Unity Recorder 등)에는 프레임마다
/// captureDeltaTime 만큼 흘러 녹화 영상에서도 연출 타이밍이 게임과 맞습니다.
/// </summary>
public static class ProiClock
{
    static int frame = -1;
    static float now, delta, lastUnscaled;

    /// <summary>실제 시간 (초).</summary>
    public static float Now { get { Tick(); return now; } }

    /// <summary>이번 프레임의 실제 시간 간격 (초).</summary>
    public static float Delta { get { Tick(); return delta; } }

    static void Tick()
    {
        int f = Time.frameCount;
        if (f == frame) return;
        float u = Time.unscaledTime;
        if (frame < 0) { now = u; delta = Time.unscaledDeltaTime; }
        else
        {   // recording: a fixed step per frame; otherwise the wall clock (even if a frame went unasked)
            delta = Time.captureDeltaTime > 0f ? Time.captureDeltaTime * (f - frame) : u - lastUnscaled;
            now += delta;
        }
        frame = f;
        lastUnscaled = u;
    }
}
