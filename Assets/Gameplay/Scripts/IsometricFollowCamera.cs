using UnityEngine;
using UnityEngine.InputSystem;
namespace AcRoguelike
{
    [DefaultExecutionOrder(100)]
    public sealed class IsometricFollowCamera : MonoBehaviour
    {
        public Transform target;
        public float pitch = 55f;
        public float yaw = 45f;
        public float distance = 15.5f;
        public float followSmoothTime = .08f;
        Vector3 focus, damping;
        bool initialized;
        void LateUpdate()
        {
            if (!target) return;
            if (Mouse.current != null && Application.isFocused)
                distance = Mathf.Clamp(distance - Mouse.current.scroll.ReadValue().y * .008f, 4.5f, 24f);
            if (!initialized) { focus = target.position + Vector3.up * .55f; initialized = true; }
            focus = Vector3.SmoothDamp(focus, target.position + Vector3.up * .55f, ref damping, followSmoothTime);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(focus + rotation * Vector3.back * distance, rotation);
        }
        public void Snap()
        {
            initialized = false; damping = Vector3.zero; LateUpdate();
        }
    }
}
