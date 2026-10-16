using AcRoguelike;
using NamgungLuna.Motion;
using UnityEngine;

namespace NamgungLuna
{
    /// <summary>Feeds the existing moon gait from the stage's shared player motor.</summary>
    [DefaultExecutionOrder(-30)]
    public sealed class LunaStageMotionSource : MonoBehaviour, ILunaMotionSource
    {
        PlayerMotor motor;
        CharacterController body;
        public Vector3 Velocity => motor && motor.isActiveAndEnabled ? motor.PlanarVelocity : Vector3.zero;
        public bool IsGrounded => !body || body.isGrounded;
        public bool IsSprinting => motor && Velocity.magnitude > motor.walkSpeed + .25f;
        // The shared motor's dash (Space / Shift): the gait plays its floating leap meanwhile.
        public float DashProgress => motor && motor.isActiveAndEnabled ? motor.DashPhase : -1f;

        void Awake()
        {
            motor = GetComponentInParent<PlayerMotor>();
            body = GetComponentInParent<CharacterController>();
            var gait = GetComponent<LunaMoonGait>();
            if (gait && body) gait.groundLevel = -body.skinWidth;
        }
    }
}
