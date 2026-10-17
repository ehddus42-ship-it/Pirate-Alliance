using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Forest
{
    /// <summary>Roots alternate their lift and the crown lags behind the walking trunk.</summary>
    public sealed class ForestCanopyRig : MonoBehaviour
    {
        sealed class Surface
        {
            internal MeshFilter filter; internal Mesh original, animated;
            internal Vector3[] bind, output; internal Matrix4x4 toRig, fromRig;
        }
        readonly List<Surface> surfaces = new List<Surface>();
        float phase, sample, height = 1;
        public float Movement { get; set; }
        public float Casting { get; set; }
        bool stopped;
        public void Initialize(Transform model)
        {
            if (!model || surfaces.Count > 0) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh; if (!source || !source.isReadable) continue;
                var s = new Surface { filter = filter, original = source, animated = Instantiate(source) };
                s.animated.name = source.name + " • rooted walking motion"; s.animated.MarkDynamic();
                s.toRig = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix; s.fromRig = s.toRig.inverse;
                s.bind = source.vertices; s.output = new Vector3[s.bind.Length];
                for (int i = 0; i < s.bind.Length; i++) { s.bind[i] = s.toRig.MultiplyPoint3x4(s.bind[i]); height = Mathf.Max(height, s.bind[i].y); }
                filter.sharedMesh = s.animated; surfaces.Add(s);
            }
        }
        void Update()
        {
            if (stopped || Time.deltaTime <= 0) return;
            phase += Time.deltaTime * Mathf.Lerp(1.4f, 5.8f, Movement);
            sample -= Time.deltaTime; if (sample > 0) return; sample = 1f / 24f;
            foreach (var s in surfaces)
            {
                for (int i = 0; i < s.bind.Length; i++)
                {
                    Vector3 p = s.bind[i]; float top = Mathf.SmoothStep(0, 1, p.y / height);
                    float root = 1 - Mathf.SmoothStep(0, .4f, p.y / height);
                    p.x += Mathf.Sin(phase - p.y * 1.3f) * (.025f + .045f * Movement + .055f * Casting) * top;
                    p.z += Mathf.Cos(phase + p.x * 1.8f) * .035f * top;
                    p.y += Mathf.Max(0, Mathf.Sin(phase + (p.x < 0 ? Mathf.PI : 0))) * .13f * root * Movement;
                    p.z += Mathf.Cos(phase + (p.x < 0 ? Mathf.PI : 0)) * .10f * root * Movement;
                    s.output[i] = s.fromRig.MultiplyPoint3x4(p);
                }
                s.animated.vertices = s.output; s.animated.RecalculateBounds();
            }
        }
        public void Stop() { stopped = true; }
        void OnDestroy() { foreach (var s in surfaces) { if (s.filter && s.filter.sharedMesh == s.animated) s.filter.sharedMesh = s.original; if (s.animated) Destroy(s.animated); } }
    }
}
