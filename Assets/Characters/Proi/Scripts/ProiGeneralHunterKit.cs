using AcRoguelike;
using UnityEngine;

/// <summary>
/// 게임(헌터, G 일상복) 안의 일상 프로이: 이동만 하므로 Run 클립 재생 속도를 PlayerMotor 의 이동 속도에 맞추는 일만 합니다
/// (ProiHunterKit 과 같은 방식: Run 은 3.2 m/s 에 맞춰져 있음). 손 모양은 ProiGeneralHands 가 Speed 로 정합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProiGeneralHunterKit : MonoBehaviour
{
    PlayerMotor motor;
    Animator animator;

    void Awake()
    {
        motor = GetComponentInParent<PlayerMotor>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (motor == null || animator == null) return;
        float run = Mathf.Clamp01(motor.PlanarVelocity.magnitude / Mathf.Max(.01f, motor.moveSpeed));
        animator.speed = Mathf.Lerp(1f, motor.moveSpeed / ProiHunterKit.RunTunedSpeed, run);
    }

    void OnDisable()
    {
        if (animator != null) animator.speed = 1f;
    }
}
