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
        /// <summary>Extra framing offset, e.g. a boss encounter raising the view to keep a tall enemy on screen.</summary>
        public Vector3 focusOffset;
        Vector3 focus, damping;
        bool initialized;
        float shakeAmplitude, shakeUntil, shakeDuration;

        /// <summary>Short camera shake for hit feedback; stronger calls replace weaker ones.</summary>
        public void AddShake(float amplitude, float duration)
        {
            float remaining = Mathf.Max(0, shakeUntil - Time.unscaledTime);
            float current = shakeDuration > 0 ? shakeAmplitude * remaining / shakeDuration : 0;
            if (amplitude < current) return;
            shakeAmplitude = amplitude;
            shakeDuration = Mathf.Max(.01f, duration);
            shakeUntil = Time.unscaledTime + shakeDuration;
        }
        void LateUpdate()
        {
            if (!target) return;
            if (Mouse.current != null && Application.isFocused)
                distance = Mathf.Clamp(distance - Mouse.current.scroll.ReadValue().y * .008f, 4.5f, 24f);
            if (!initialized) { focus = target.position + Vector3.up * .55f + focusOffset; initialized = true; }
            focus = Vector3.SmoothDamp(focus, target.position + Vector3.up * .55f + focusOffset, ref damping, followSmoothTime);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 shake = Vector3.zero;
            float left = shakeUntil - Time.unscaledTime;
            if (left > 0)
            {
                float k = shakeAmplitude * left / shakeDuration;
                float t = Time.unscaledTime * 63f;
                shake = rotation * new Vector3(Mathf.Sin(t) * k, Mathf.Sin(t * 1.37f + 1.1f) * k, 0);
            }
            transform.SetPositionAndRotation(focus + rotation * Vector3.back * distance + shake, rotation);
        }
        public void Snap()
        {
            initialized = false; damping = Vector3.zero; LateUpdate();
        }
    }
}
