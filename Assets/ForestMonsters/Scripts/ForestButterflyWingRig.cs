using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Forest
{
    /// <summary>Skinless Meshy assets retain their texture/materials; wing vertices hinge around the thorax.</summary>
    public sealed class ForestButterflyWingRig : MonoBehaviour
    {
        sealed class Surface
        {
            internal MeshFilter filter;
            internal Mesh original, animated;
            internal Vector3[] bind, normals, vertices, outputNormals;
            internal Vector4[] tangents, outputTangents;
            internal Matrix4x4 toRig, fromRig;
        }
        readonly List<Surface> surfaces = new List<Surface>();
        float phase, nextSample, halfSpan;
        bool stopped;
        public float FlapDegrees { get; private set; }
        public int AnimatedVertexCount { get; private set; }
        public float Effort { get; set; }

        public void Initialize(Transform model)
        {
            if (surfaces.Count > 0 || !model) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh;
                if (!source || !source.isReadable) { Debug.LogError("Forest butterfly requires a readable Meshy mesh: " + filter.name, filter); continue; }
                var s = new Surface { filter = filter, original = source, animated = Instantiate(source) };
                s.animated.name = source.name + " • animated butterfly wings"; s.animated.MarkDynamic();
                s.toRig = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix; s.fromRig = s.toRig.inverse;
                s.bind = source.vertices; s.normals = source.normals; s.tangents = source.tangents;
                s.vertices = new Vector3[s.bind.Length]; s.outputNormals = new Vector3[s.bind.Length];
                s.outputTangents = new Vector4[s.bind.Length];
                for (int i = 0; i < s.bind.Length; i++)
                {
                    s.bind[i] = s.toRig.MultiplyPoint3x4(s.bind[i]);
                    if (s.normals.Length == s.bind.Length) s.normals[i] = s.toRig.MultiplyVector(s.normals[i]).normalized;
                    if (s.tangents.Length == s.bind.Length)
                    {
                        Vector3 tangent = s.toRig.MultiplyVector((Vector3)s.tangents[i]).normalized;
                        s.tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, s.tangents[i].w);
                    }
                    halfSpan = Mathf.Max(halfSpan, Mathf.Abs(s.bind[i].x));
                }
                filter.sharedMesh = s.animated; AnimatedVertexCount += s.bind.Length; surfaces.Add(s);
            }
            phase = Random.value * Mathf.PI * 2;
        }

        void Update()
        {
            if (stopped || Time.deltaTime <= 0 || surfaces.Count == 0) return;
            phase += Time.deltaTime * Mathf.Lerp(15f, 22f, Effort);
            nextSample -= Time.deltaTime; if (nextSample > 0) return; nextSample = 1f / 30f;
            FlapDegrees = Mathf.Sin(phase) * Mathf.Lerp(43, 63, Effort) + 8;
            float hinge = Mathf.Max(.07f, halfSpan * .095f);
            foreach (var s in surfaces)
            {
                for (int i = 0; i < s.bind.Length; i++)
                {
                    Vector3 p = s.bind[i]; float side = Mathf.Sign(p.x);
                    float weight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hinge, halfSpan * .36f, Mathf.Abs(p.x)));
                    var turn = Quaternion.AngleAxis(FlapDegrees * side * weight, Vector3.forward);
                    Vector3 pivot = new Vector3(side * hinge, 0, p.z);
                    Vector3 deformed = pivot + turn * (p - pivot);
                    deformed.z += Mathf.Sin(phase - Mathf.Abs(p.x) * 1.8f) * .045f * weight;
                    s.vertices[i] = s.fromRig.MultiplyPoint3x4(deformed);
                    if (s.normals.Length == s.bind.Length) s.outputNormals[i] = s.fromRig.MultiplyVector(turn * s.normals[i]).normalized;
                    if (s.tangents.Length == s.bind.Length)
                    {
                        Vector3 tangent = s.fromRig.MultiplyVector(turn * (Vector3)s.tangents[i]).normalized;
                        s.outputTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, s.tangents[i].w);
                    }
                }
                s.animated.vertices = s.vertices;
                if (s.normals.Length == s.bind.Length) s.animated.normals = s.outputNormals;
                if (s.tangents.Length == s.bind.Length) s.animated.tangents = s.outputTangents;
                s.animated.RecalculateBounds();
            }
        }
        public void Stop() { stopped = true; }
        void OnDestroy()
        {
            foreach (var s in surfaces) { if (s.filter && s.filter.sharedMesh == s.animated) s.filter.sharedMesh = s.original; if (s.animated) Destroy(s.animated); }
        }
    }
}
