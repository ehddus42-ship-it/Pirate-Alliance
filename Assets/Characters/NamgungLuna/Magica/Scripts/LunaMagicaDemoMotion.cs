using UnityEngine;

namespace NamgungLuna.Magica
{
    /// <summary>
    /// 리깅이 없는 정적 캐릭터라 몸 전체를 걷기·방향 전환·회전시켜 Magica Cloth의 흔들림을 보여 줍니다.
    /// Moves the whole character (walk, turns, a spin) to show the cloth/hair motion.
    /// Remove or disable this component once the character is driven by real animation.
    /// </summary>
    [DisallowMultipleComponent]
    public class LunaMagicaDemoMotion : MonoBehaviour
    {
        [Tooltip("움직일 캐릭터 루트. 비우면 이 오브젝트")]
        public Transform target;
        public bool play = true;
        [Tooltip("앞뒤 걷기 진폭 (m)")]
        public float walkDistance = 0.6f;
        [Tooltip("걷기 왕복 주기 (s)")]
        public float walkPeriod = 4.0f;
        [Tooltip("좌우 방향 전환 각도 (도)")]
        public float turnAngle = 45.0f;
        public float turnPeriod = 6.0f;
        [Tooltip("이 주기마다 한 바퀴 회전 (0이면 회전 없음)")]
        public float spinEvery = 8.0f;
        public float spinDuration = 1.2f;
        [Tooltip("시작 후 정지 상태로 기다리는 시간 (s)")]
        public float startDelay = 1.0f;

        public float ElapsedTime { get; private set; }

        Vector3 basePosition;
        Quaternion baseRotation;

        void Start()
        {
            if (target == null) target = transform;
            basePosition = target.position;
            baseRotation = target.rotation;
        }

        void Update()
        {
            if (!play || target == null) return;
            ElapsedTime += Time.deltaTime;
            float t = Mathf.Max(0.0f, ElapsedTime - startDelay);
            // Ease in over the first second so the cloth does not get a velocity spike.
            float ramp = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(t));
            float walk = Mathf.Sin(t * 2.0f * Mathf.PI / walkPeriod) * walkDistance * 0.5f * ramp;
            float yaw = Mathf.Sin(t * 2.0f * Mathf.PI / turnPeriod) * turnAngle * ramp;
            if (spinEvery > 0.0f)
            {
                float cycles = Mathf.Floor(t / spinEvery);
                float local = t - cycles * spinEvery - (spinEvery - spinDuration);
                yaw += 360.0f * (cycles + Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(local / spinDuration)));
            }
            Quaternion rotation = baseRotation * Quaternion.AngleAxis(yaw, Vector3.up);
            target.SetPositionAndRotation(basePosition + baseRotation * Vector3.forward * walk, rotation);
        }

        /// <summary>Return the character to its start pose and restart the motion.</summary>
        public void ResetMotion()
        {
            ElapsedTime = 0.0f;
            if (target != null) target.SetPositionAndRotation(basePosition, baseRotation);
        }
    }
}
