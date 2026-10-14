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
        float zoomAmount, zoomStart, zoomAttack, zoomHold, zoomRelease, zoomFrom;

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

        /// <summary>
        /// Skill framing: moves the camera `amount` of its distance closer (negative: farther), eased in over
        /// `attack`, held, then eased back over `release`. A new call takes over from the current push smoothly.
        /// </summary>
        public void AddZoom(float amount, float attack, float hold, float release)
        {
            zoomFrom = ZoomNow();
            zoomAmount = amount;
            zoomStart = Time.unscaledTime;
            zoomAttack = Mathf.Max(.001f, attack);
            zoomHold = Mathf.Max(0, hold);
            zoomRelease = Mathf.Max(.001f, release);
        }

        float ZoomNow()
        {
            float t = Time.unscaledTime - zoomStart;
            if (t < zoomAttack) return Mathf.Lerp(zoomFrom, zoomAmount, Mathf.SmoothStep(0, 1, t / zoomAttack));
            t -= zoomAttack + zoomHold;
            if (t < 0) return zoomAmount;
            return t < zoomRelease ? Mathf.Lerp(zoomAmount, 0, Mathf.SmoothStep(0, 1, t / zoomRelease)) : 0;
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
            float framed = distance * (1 - Mathf.Clamp(ZoomNow(), -.5f, .5f));
            Vector3 position = focus + rotation * Vector3.back * framed;
            if (!view) { view = GetComponent<Camera>(); baseFieldOfView = view ? view.fieldOfView : 60f; }
            if (cineWeight > 0 || cineTarget > 0)
            {
                // A skill's cut (Proi's ultimate): blend from the follow view to a fixed shot and back, in real time.
                float dt = Time.unscaledDeltaTime;
                cineWeight = Mathf.MoveTowards(cineWeight, cineTarget, dt / cineBlend);
                cineMove = Mathf.MoveTowards(cineMove, 1, dt / cineMoveTime);
                CinePose(out Vector3 shotPosition, out Quaternion shotRotation, out float shotFov);
                float w = Mathf.SmoothStep(0, 1, cineWeight);
                position = Vector3.Lerp(position, shotPosition, w);
                rotation = Quaternion.Slerp(rotation, shotRotation, w);
                if (view) view.fieldOfView = Mathf.Lerp(baseFieldOfView, shotFov, w);
            }
            transform.SetPositionAndRotation(position + shake, rotation);
        }

        // ---- cinematic shots ------------------------------------------------------------------------------
        Camera view;
        float baseFieldOfView = 60f;
        Vector3 cinePosition, cineFromPosition;
        Quaternion cineRotation = Quaternion.identity, cineFromRotation = Quaternion.identity;
        float cineFov, cineFromFov, cineWeight, cineTarget, cineBlend = .3f, cineMove = 1, cineMoveTime = .3f;

        /// <summary>The follow view's rotation and field of view (shots framed at the same angle use these).</summary>
        public Quaternion BaseRotation => Quaternion.Euler(pitch, yaw, 0);
        public float BaseFieldOfView => view ? baseFieldOfView : (TryGetComponent(out Camera c) ? c.fieldOfView : 60f);
        /// <summary>True while a cinematic shot holds the camera (or is blending back).</summary>
        public bool InCinematic => cineWeight > 0 || cineTarget > 0;

        /// <summary>
        /// Moves to a fixed shot over `blendTime` (real seconds). From another shot it travels on from where the camera
        /// is now, so one cut can lead into the next. Shakes still apply on top.
        /// </summary>
        public void SetCinematic(Vector3 position, Quaternion rotation, float fieldOfView, float blendTime)
        {
            if (cineWeight > 0)
            {
                CinePose(out cineFromPosition, out cineFromRotation, out cineFromFov);
                cineMove = 0;
                cineMoveTime = Mathf.Max(blendTime, .01f);
            }
            else cineMove = 1;
            cinePosition = position; cineRotation = rotation; cineFov = fieldOfView;
            cineTarget = 1; cineBlend = Mathf.Max(blendTime, .01f);
        }

        /// <summary>Back to the follow view over `blendTime` (real seconds).</summary>
        public void ClearCinematic(float blendTime)
        {
            cineTarget = 0; cineBlend = Mathf.Max(blendTime, .01f);
        }

        void CinePose(out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            float m = Mathf.SmoothStep(0, 1, cineMove);
            position = Vector3.Lerp(cineFromPosition, cinePosition, m);
            rotation = Quaternion.Slerp(cineFromRotation, cineRotation, m);
            fieldOfView = Mathf.Lerp(cineFromFov, cineFov, m);
        }
        public void Snap()
        {
            initialized = false; damping = Vector3.zero; LateUpdate();
        }
    }
}
