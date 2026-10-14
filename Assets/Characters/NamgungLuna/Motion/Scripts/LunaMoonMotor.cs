using UnityEngine;
using UnityEngine.InputSystem;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 남궁루나 조작: WASD 이동(카메라 기준) · Shift 질주(누르는 동안) · Space 점프 · Ctrl 걷기 전환.
    /// 콜롬비나처럼 가볍게 미끄러지듯 움직입니다. 손을 떼면 조금 더 떠서 나아가다 멈추고, 방향은 곡선으로 바뀌며,
    /// 점프는 오래 떠 있다가 천천히 내려옵니다. 질주를 시작하면 앞으로 한 번 튀어 나갑니다.
    /// Floaty CharacterController mover for the moon gait: quick to start, glides a little before stopping, turns on
    /// arcs, sprint starts with a burst, low-gravity jump (fast rise, slow fall).
    /// Keyboard input can be replaced by <see cref="scriptedMove"/> etc. (showcase, tests, AI).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-10)]
    public class LunaMoonMotor : MonoBehaviour, ILunaMotionSource
    {
        public Transform cameraTransform;

        [Header("Speed (m/s)")]
        public float walkSpeed = 2.0f;
        public float runSpeed = 4.2f;
        public float sprintSpeed = 6.8f;

        [Header("Glide")]
        [Tooltip("가속 (m/s²)")]
        public float acceleration = 16f;
        [Tooltip("손을 뗐을 때 감속 (m/s²). 낮을수록 더 떠서 나아갑니다")]
        public float glideDeceleration = 6.5f;
        [Tooltip("이동 방향이 돌아가는 속도 (도/s)")]
        public float turnRate = 420f;
        public float sprintTurnRate = 260f;
        [Tooltip("몸이 이동 방향을 바라보는 속도 (도/s)")]
        public float facingRate = 720f;
        [Tooltip("질주 시작 순간 더해지는 속도 (m/s)")]
        public float sprintBurst = 2.5f;
        [Tooltip("공중에서의 조작 비율")]
        public float airControl = 0.35f;

        [Header("Low-gravity jump")]
        public float jumpHeight = 1.35f;
        [Tooltip("올라갈 때 중력 (m/s²)")]
        public float riseGravity = 15f;
        [Tooltip("내려올 때 중력 (m/s²). 작을수록 천천히 내려옵니다")]
        public float fallGravity = 8.5f;
        public float maxFallSpeed = 7f;
        public float coyoteTime = 0.12f;

        [Header("Input")]
        public bool readKeyboard = true;
        [Tooltip("스크립트 입력: 월드 방향 (크기 0~1)")]
        public Vector3 scriptedMove;
        public bool scriptedSprint, scriptedWalk;

        public Vector3 Velocity => planar + Vector3.up * vertical;
        public bool IsGrounded => grounded;
        public bool IsSprinting => sprinting;
        public bool HasKeyboardInput { get; private set; }

        CharacterController controller;
        LunaMoonGait gait;
        Vector3 planar;
        float vertical, sinceGrounded, jumpBuffer;
        bool grounded = true, sprinting, walkToggle, scriptedJump;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            gait = GetComponent<LunaMoonGait>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (gait != null)
            {
                gait.walkSpeed = walkSpeed;
                gait.runSpeed = runSpeed;
                gait.sprintSpeed = sprintSpeed;
                // the controller rests skinWidth above the floor
                gait.groundLevel = controller.center.y - controller.height * 0.5f - controller.skinWidth;
            }
        }

        /// <summary>Jump on the next update (scripted input).</summary>
        public void Jump() => scriptedJump = true;

        /// <summary>Stop at once (e.g. before a cutscene); the gait settles from here.</summary>
        public void Halt()
        {
            planar = Vector3.zero;
            vertical = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 move = scriptedMove;
            bool sprintHeld = scriptedSprint, walkHeld = scriptedWalk, jumpPressed = scriptedJump;
            scriptedJump = false;
            HasKeyboardInput = false;
            Keyboard kb = readKeyboard ? Keyboard.current : null;
            if (kb != null)
            {
                Vector2 input = Vector2.zero;
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                if (kb.leftCtrlKey.wasPressedThisFrame) walkToggle = !walkToggle;
                if (input != Vector2.zero || kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.isPressed)
                {
                    HasKeyboardInput = true;
                    move = ToWorld(Vector2.ClampMagnitude(input, 1f));
                    sprintHeld = kb.leftShiftKey.isPressed;
                    walkHeld = walkToggle;
                    jumpPressed |= kb.spaceKey.wasPressedThisFrame;
                }
            }
            move = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(move, Vector3.up), 1f);

            // ground
            grounded = controller.isGrounded || (sinceGrounded < 0.08f && vertical <= 0f);
            sinceGrounded = controller.isGrounded ? 0f : sinceGrounded + dt;
            jumpBuffer = jumpPressed ? 0.15f : Mathf.Max(0f, jumpBuffer - dt);

            // planar glide
            bool wantsMove = move.sqrMagnitude > 0.0001f;
            bool wasSprinting = sprinting;
            sprinting = sprintHeld && wantsMove && !walkHeld;
            float control = grounded ? 1f : airControl;
            float speed = planar.magnitude;
            Vector3 direction = speed > 0.01f ? planar / speed : transform.forward;
            if (wantsMove)
            {
                Vector3 desired = move.normalized;
                float target = (sprinting ? sprintSpeed : walkHeld ? walkSpeed : runSpeed) * move.magnitude;
                float angle = Vector3.Angle(direction, desired);
                if (speed < 0.6f)
                {
                    direction = desired;
                }
                else if (angle > 110f)
                {
                    // reversing: slow down first, then set off the other way
                    speed = Mathf.MoveTowards(speed, 0f, acceleration * control * dt);
                }
                else
                {
                    float rate = Mathf.Lerp(turnRate, sprintTurnRate, sprinting ? 1f : 0f) * control;
                    direction = Vector3.RotateTowards(direction, desired, rate * Mathf.Deg2Rad * dt, 0f);
                }
                float rateToTarget = target > speed ? acceleration : glideDeceleration;
                speed = Mathf.MoveTowards(speed, target, rateToTarget * control * dt);
                if (sprinting && !wasSprinting && grounded) speed += sprintBurst;
            }
            else
            {
                speed = Mathf.MoveTowards(speed, 0f, glideDeceleration * control * dt);
            }
            planar = direction * speed;

            if (speed > 0.2f && direction.sqrMagnitude > 0.5f)
            {
                Quaternion face = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, facingRate * dt);
            }

            // low-gravity vertical
            if (controller.isGrounded && vertical < 0f) vertical = -2f;
            if (jumpBuffer > 0f && (grounded || sinceGrounded < coyoteTime) && vertical <= 0.1f)
            {
                vertical = Mathf.Sqrt(2f * riseGravity * jumpHeight);
                jumpBuffer = 0f;
                grounded = false;
                sinceGrounded = coyoteTime;
            }
            vertical -= (vertical > 0f ? riseGravity : fallGravity) * dt;
            vertical = Mathf.Max(vertical, -maxFallSpeed);

            controller.Move((planar + Vector3.up * vertical) * dt);
            if (controller.isGrounded && vertical < 0f) grounded = true;
        }

        Vector3 ToWorld(Vector2 input)
        {
            if (input == Vector2.zero) return Vector3.zero;
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            }
            return forward * input.y + right * input.x;
        }
    }
}
