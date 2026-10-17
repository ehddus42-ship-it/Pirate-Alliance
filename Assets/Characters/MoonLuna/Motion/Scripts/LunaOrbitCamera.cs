using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonLuna.Motion
{
    /// <summary>
    /// 데모용 3인칭 카메라. 마우스 오른쪽 드래그 / Q·E 로 회전, 휠로 거리 조절. 이동 중에는 천천히 등 뒤로 돌아갑니다.
    /// Third-person follow camera for the moon gait demo.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class LunaOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 1.0f, 0f);
        public float distance = 4.8f;
        public float minDistance = 2f, maxDistance = 10f;
        public float yaw, pitch = 14f;
        [Tooltip("따라가는 부드러움 (클수록 빠름)")]
        public float followSharpness = 9f;
        [Tooltip("이동 중 등 뒤로 돌아가는 빠르기 (0 = 끔)")]
        public float autoAlign = 0.5f;
        public bool readInput = true;

        Vector3 focus;
        bool ready;

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            Vector3 goal = target.position + targetOffset;
            if (!ready) { focus = goal; ready = true; }
            Vector3 moved = goal - focus;
            focus = Vector3.Lerp(focus, goal, 1f - Mathf.Exp(-followSharpness * dt));

            bool userRotated = false;
            if (readInput)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed)
                    {
                        Vector2 delta = mouse.delta.ReadValue();
                        yaw += delta.x * 0.15f;
                        pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -10f, 65f);
                        userRotated = true;
                    }
                    float wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f) distance = Mathf.Clamp(distance * (wheel > 0f ? 0.9f : 1.1f), minDistance, maxDistance);
                }
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.qKey.isPressed) { yaw -= 90f * dt; userRotated = true; }
                    if (kb.eKey.isPressed) { yaw += 90f * dt; userRotated = true; }
                }
            }

            // drift behind her back while she travels, never fighting the player's own rotation
            Vector3 planarMove = Vector3.ProjectOnPlane(moved, Vector3.up);
            if (!userRotated && autoAlign > 0f && planarMove.magnitude > 0.02f)
            {
                float behind = target.eulerAngles.y;
                yaw = Mathf.LerpAngle(yaw, behind, 1f - Mathf.Exp(-autoAlign * dt));
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        /// <summary>Jump straight to the framing (after a teleport or when a scene starts).</summary>
        public void Snap()
        {
            if (target == null) return;
            focus = target.position + targetOffset;
            ready = true;
        }
    }
}
