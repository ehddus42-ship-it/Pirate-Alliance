using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonLuna.Motion
{
    /// <summary>
    /// 문루나 조작: WASD 이동(카메라 기준, 기본이 콜롬비나 질주 모션) · Space / 왼쪽 Shift 대쉬(회피) · Ctrl 걷기 전환.
    /// 가볍게 미끄러지듯 움직입니다. 손을 떼면 조금 더 떠서 나아가다 멈추고, 방향은 곡선으로 바뀝니다.
    /// 대쉬는 게임의 회피(PlayerMotor)와 같은 규칙입니다: 스태미나 칸(기본 3)을 하나 쓰고, 대쉬 사이 최소 간격이 있고,
    /// 마지막 대쉬 뒤 잠깐 기다렸다가 칸이 하나씩 차오릅니다. 몸은 그동안 떠서 날듯이 도약합니다(LunaMoonGait).
    /// Floaty CharacterController mover for the moon gait: run at the game's move speed with the fast (sprint) motion,
    /// glide a little before stopping, turn on arcs, dash (dodge) with stamina pips like the game's PlayerMotor.
    /// There is no jump. Keyboard input can be replaced by <see cref="scriptedMove"/> / <see cref="Dash"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-10)]
    public class LunaMoonMotor : MonoBehaviour, ILunaMotionSource
    {
        public Transform cameraTransform;

        [Header("Speed (m/s, game PlayerMotor defaults)")]
        public float walkSpeed = 2.1f;
        public float runSpeed = 4.2f;

        [Header("Glide")]
        [Tooltip("가속 (m/s²)")]
        public float acceleration = 16f;
        [Tooltip("손을 뗐을 때 감속 (m/s²). 낮을수록 더 떠서 나아갑니다")]
        public float glideDeceleration = 6.5f;
        [Tooltip("이동 방향이 돌아가는 속도 (도/s)")]
        public float turnRate = 300f;
        [Tooltip("몸이 이동 방향을 바라보는 속도 (도/s)")]
        public float facingRate = 720f;
        public float gravity = 20f;

        [Header("Dash (dodge)")]
        [Tooltip("대쉬 거리 (m)")]
        public float dashDistance = 3.0f;
        [Tooltip("대쉬 시간 (s). 게임 기본(0.19)보다 길어 떠서 나는 도약이 보입니다")]
        public float dashDuration = 0.3f;
        [Tooltip("대쉬 속도 분포 (0~1 진행 → 속도 배율). 거리는 dashDistance로 고정, 모양만 정합니다")]
        public AnimationCurve dashSpeedCurve = new AnimationCurve(
            new Keyframe(0f, 1.6f, 0f, -1.2f), new Keyframe(0.55f, 1.0f, -1.6f, -1.6f), new Keyframe(1f, 0.35f, -0.8f, 0f));
        [Min(1)] public int maxStamina = 3;
        [Tooltip("스태미나 한 칸이 차는 시간 (s)")]
        public float dashCooldown = 0.85f;
        [Tooltip("마지막 대쉬 뒤 스태미나가 차기 시작할 때까지 (s)")]
        public float staminaRegenDelay = 0.3f;
        [Tooltip("두 대쉬 시작 사이 최소 간격 (s)")]
        public float dashInterval = 0.3f;

        [Header("Input")]
        public bool readKeyboard = true;
        [Tooltip("스크립트 입력: 월드 방향 (크기 0~1)")]
        public Vector3 scriptedMove;
        public bool scriptedWalk;

        public Vector3 Velocity => planar + Vector3.up * vertical;
        public bool IsGrounded => grounded;
        public bool IsSprinting => sprinting;
        public float DashProgress => dashTime >= 0f ? Mathf.Clamp01(dashTime / Mathf.Max(0.01f, dashDuration)) : -1f;
        public bool IsDashing => dashTime >= 0f;
        public float Stamina { get; private set; }
        public int DashCount { get; private set; }
        public bool HasKeyboardInput { get; private set; }

        CharacterController controller;
        Vector3 planar, dashDirection;
        float vertical, sinceGrounded, dashTime = -1f, lastDashStart = -10f, curveArea = 1f;
        bool grounded = true, sprinting, walkToggle, scriptedDash;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            Stamina = maxStamina;
            curveArea = CurveArea();
            var gait = GetComponent<LunaMoonGait>();
            if (gait != null)
            {
                gait.walkSpeed = walkSpeed;
                gait.runSpeed = runSpeed;
                gait.sprintSpeed = runSpeed * 1.6f;
                // the controller rests skinWidth above the floor
                gait.groundLevel = controller.center.y - controller.height * 0.5f - controller.skinWidth;
            }
        }

        /// <summary>Dash on the next update (scripted input), in the move direction or where she faces.</summary>
        public void Dash() => scriptedDash = true;

        /// <summary>Stop at once (e.g. before a cutscene); the gait settles from here.</summary>
        public void Halt()
        {
            planar = Vector3.zero;
            vertical = 0f;
            dashTime = -1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 move = scriptedMove;
            bool walkHeld = scriptedWalk, dashPressed = scriptedDash;
            scriptedDash = false;
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
                bool dashKey = kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame;
                if (input != Vector2.zero || dashKey)
                {
                    HasKeyboardInput = true;
                    move = ToWorld(Vector2.ClampMagnitude(input, 1f));
                    walkHeld = walkToggle;
                    dashPressed |= dashKey;
                }
            }
            move = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(move, Vector3.up), 1f);
            bool wantsMove = move.sqrMagnitude > 0.0001f;

            grounded = controller.isGrounded || (sinceGrounded < 0.08f && vertical <= 0f);
            sinceGrounded = controller.isGrounded ? 0f : sinceGrounded + dt;
            RecoverStamina(dt);

            if (dashPressed && dashTime < 0f && Stamina >= 1f && Time.time - lastDashStart >= dashInterval)
            {
                dashDirection = wantsMove ? move.normalized : transform.forward;
                dashTime = 0f;
                lastDashStart = Time.time;
                Stamina -= 1f;
                DashCount++;
            }

            float speed = planar.magnitude;
            Vector3 direction = speed > 0.01f ? planar / speed : transform.forward;
            float dashStep = -1f;   // this frame's share of the dash distance
            if (dashTime >= 0f)
            {
                // spread dashDistance over the dash by the speed curve, exactly whatever the frame times
                float from = Mathf.Clamp01(dashTime / dashDuration), to = Mathf.Clamp01((dashTime + dt) / dashDuration);
                dashStep = dashDistance * (CurveIntegral(to) - CurveIntegral(from)) / curveArea;
                speed = dashStep / dt;
                direction = dashDirection;
                dashTime += dt;
            }
            else if (wantsMove)
            {
                Vector3 desired = move.normalized;
                float target = (walkHeld ? walkSpeed : runSpeed) * move.magnitude;
                float angle = Vector3.Angle(direction, desired);
                if (speed < 0.6f) direction = desired;
                else if (angle > 110f) speed = Mathf.MoveTowards(speed, 0f, acceleration * dt);   // reversing: slow down first
                else direction = Vector3.RotateTowards(direction, desired, turnRate * Mathf.Deg2Rad * dt, 0f);
                speed = Mathf.MoveTowards(speed, target, (target > speed ? acceleration : glideDeceleration) * dt);
            }
            else
            {
                speed = Mathf.MoveTowards(speed, 0f, glideDeceleration * dt);
            }
            planar = direction * speed;
            sprinting = IsDashing || (wantsMove && !walkHeld && speed > walkSpeed);

            if (speed > 0.2f && direction.sqrMagnitude > 0.5f)
            {
                Quaternion face = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, (IsDashing ? 1800f : facingRate) * dt);
            }

            if (controller.isGrounded && vertical < 0f) vertical = -2f;
            vertical = Mathf.Max(vertical - gravity * dt, -12f);
            controller.Move((planar + Vector3.up * vertical) * dt);
            if (controller.isGrounded && vertical < 0f) grounded = true;
            if (dashTime >= dashDuration)
            {
                dashTime = -1f;
                // hand over to the run if a direction is held, otherwise glide out of it
                float exit = dashDistance / dashDuration * dashSpeedCurve.Evaluate(1f) / curveArea;
                planar = dashDirection * (wantsMove ? Mathf.Max(runSpeed, exit) : Mathf.Min(exit, runSpeed));
            }
        }

        void RecoverStamina(float dt)
        {
            if (Stamina >= maxStamina || Time.time - lastDashStart < staminaRegenDelay) return;
            Stamina = Mathf.Min(maxStamina, Stamina + dt / Mathf.Max(0.01f, dashCooldown));
        }

        float CurveArea() => Mathf.Max(1e-4f, CurveIntegral(1f));

        /// <summary>Area under the dash speed curve from 0 to <paramref name="t"/> (midpoint rule).</summary>
        float CurveIntegral(float t)
        {
            if (t <= 0f) return 0f;
            int samples = Mathf.Max(4, Mathf.CeilToInt(64 * t));
            float area = 0f;
            for (int i = 0; i < samples; i++) area += dashSpeedCurve.Evaluate((i + 0.5f) / samples * t);
            return area * t / samples;
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
