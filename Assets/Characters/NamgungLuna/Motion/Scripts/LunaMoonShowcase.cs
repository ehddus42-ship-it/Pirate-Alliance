using System;
using UnityEngine;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 자동 시연: 키보드를 몇 초 동안 만지지 않으면 대기 → 걷기 → 달리기(곡선) 중 대쉬 → 미끄러지듯 멈춤 →
    /// 연속 대쉬 → 제자리 대쉬 → 달리기 순서로 남궁루나를 움직입니다. 키를 누르면 바로 조작이 넘어옵니다.
    /// Scripted tour of every gait and the dash for the demo scene and the play-mode tests. Hands control back on any key press.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class LunaMoonShowcase : MonoBehaviour
    {
        public enum Mode { Idle, Walk, Run }

        [Serializable]
        public struct Beat
        {
            public float duration;
            public Mode mode;
            [Tooltip("이동 방향 (월드 yaw, 도)")]
            public float heading;
            [Tooltip("구간 동안 방향이 바뀌는 양 (도)")]
            public float turn;
            [Tooltip("이 구간 시작 후 대쉬할 시각 (s, 음수면 없음)")]
            public float dashAt;
        }

        public LunaMoonMotor motor;
        [Tooltip("키보드 입력이 없을 때 자동 시연을 시작할 때까지 (s). 0이면 처음부터")]
        public float idleDelay = 4f;
        public bool loop = true;
        public Beat[] beats =
        {
            new Beat { duration = 2.5f, mode = Mode.Idle, dashAt = -1f },
            new Beat { duration = 3.5f, mode = Mode.Walk, heading = 0f, turn = 25f, dashAt = -1f },
            new Beat { duration = 1.2f, mode = Mode.Idle, dashAt = -1f },
            new Beat { duration = 4.5f, mode = Mode.Run, heading = 30f, turn = 110f, dashAt = 2.4f },
            new Beat { duration = 1.6f, mode = Mode.Idle, dashAt = -1f },
            new Beat { duration = 2.2f, mode = Mode.Run, heading = 200f, turn = -40f, dashAt = 1.2f },
            new Beat { duration = 1.4f, mode = Mode.Run, heading = 160f, turn = 0f, dashAt = 0.3f },   // chained second dash
            new Beat { duration = 1.6f, mode = Mode.Idle, dashAt = -1f },
            new Beat { duration = 1.6f, mode = Mode.Idle, dashAt = 0.4f },                             // from a standstill
            new Beat { duration = 1.4f, mode = Mode.Idle, dashAt = -1f },
            new Beat { duration = 3.0f, mode = Mode.Run, heading = 300f, turn = 90f, dashAt = -1f },
            new Beat { duration = 2.5f, mode = Mode.Idle, dashAt = -1f },
        };

        public bool Playing { get; private set; }
        public float Clock { get; private set; }
        public int BeatIndex { get; private set; }
        public float TotalDuration { get { float t = 0f; foreach (var b in beats) t += b.duration; return t; } }

        float quiet, beatTime;
        bool dashed;

        void Awake()
        {
            if (motor == null) motor = GetComponent<LunaMoonMotor>();
        }

        /// <summary>Start the tour from the first beat now.</summary>
        public void Restart()
        {
            Playing = true;
            Clock = beatTime = 0f;
            BeatIndex = 0;
            dashed = false;
        }

        void Update()
        {
            if (motor == null || beats.Length == 0) return;
            if (motor.HasKeyboardInput)
            {
                quiet = 0f;
                if (Playing) Stop();
                return;
            }
            quiet += Time.deltaTime;
            if (!Playing && quiet >= idleDelay) Restart();
            if (!Playing) return;

            Beat beat = beats[BeatIndex];
            float t = Mathf.Clamp01(beatTime / Mathf.Max(0.01f, beat.duration));
            float heading = beat.heading + beat.turn * t;
            motor.scriptedMove = beat.mode == Mode.Idle ? Vector3.zero : Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            motor.scriptedWalk = beat.mode == Mode.Walk;
            if (!dashed && beat.dashAt >= 0f && beatTime >= beat.dashAt)
            {
                motor.Dash();
                dashed = true;
            }

            beatTime += Time.deltaTime;
            Clock += Time.deltaTime;
            if (beatTime >= beat.duration)
            {
                beatTime = 0f;
                dashed = false;
                BeatIndex++;
                if (BeatIndex >= beats.Length)
                {
                    if (loop) BeatIndex = 0;
                    else { Stop(); return; }
                }
            }
        }

        void Stop()
        {
            Playing = false;
            motor.scriptedMove = Vector3.zero;
            motor.scriptedWalk = false;
        }
    }
}
