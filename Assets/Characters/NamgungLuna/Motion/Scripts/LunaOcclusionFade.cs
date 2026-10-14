using UnityEngine;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 등 뒤의 보름달처럼 큰 소품이 카메라와 캐릭터 사이를 가리면 점점 투명하게(디더) 만듭니다.
    /// Fades large props (the floating full moon) with the toon shader's screen-door _Fade while they sit between the
    /// camera and her body, e.g. when the camera follows from behind. Shadows are kept.
    /// </summary>
    [DefaultExecutionOrder(320)]
    public class LunaOcclusionFade : MonoBehaviour
    {
        public Renderer[] renderers = new Renderer[0];
        [Tooltip("가려지면 안 되는 몸 (Visual)")]
        public Transform body;
        [Tooltip("몸에서 확인할 점 (body local): 발 · 허리 · 머리와 양옆 (치마·소매)")]
        public Vector3[] bodyPoints =
        {
            new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.9f, 0f), new Vector3(0f, 1.5f, 0f),
            new Vector3(-0.3f, 0.45f, 0f), new Vector3(0.3f, 0.45f, 0f), new Vector3(-0.35f, 1.1f, 0f), new Vector3(0.35f, 1.1f, 0f),
        };
        [Range(0f, 1f)] public float fadedTo = 0.12f;
        public float fadeSpeed = 5f;
        public Camera viewCamera;

        static readonly int FadeId = Shader.PropertyToID("_Fade");
        MaterialPropertyBlock block;
        float fade = 1f;

        public float CurrentFade => fade;

        void LateUpdate()
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null || body == null) return;
            block ??= new MaterialPropertyBlock();
            Vector3 eye = cam.transform.position;
            bool blocking = false;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
                foreach (Vector3 local in bodyPoints)
                {
                    Vector3 target = body.TransformPoint(local);
                    Vector3 ray = target - eye;
                    float t = Mathf.Clamp01(Vector3.Dot(b.center - eye, ray) / ray.sqrMagnitude);
                    if (t < 0.98f && (eye + ray * t - b.center).sqrMagnitude < radius * radius) { blocking = true; break; }
                }
                if (blocking) break;
            }
            fade = Mathf.MoveTowards(fade, blocking ? fadedTo : 1f, fadeSpeed * Time.deltaTime);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(FadeId, fade);
                r.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            fade = 1f;
            if (block == null) return;
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(FadeId, 1f);
                r.SetPropertyBlock(block);
            }
        }
    }
}
