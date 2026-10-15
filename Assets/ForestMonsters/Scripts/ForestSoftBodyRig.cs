using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Forest
{
    /// <summary>Deforms copies of Meshy geometry in the monster's pose space. Materials and source meshes stay intact.</summary>
    internal sealed class ForestSoftBodyRig
    {
        sealed class Part
        {
            public Mesh mesh, source;
            public MeshFilter filter;
            public SkinnedMeshRenderer skin;
            public Matrix4x4 toLocal;
            public Vector3[] rest, normals, output, outputNormals;
            public Vector4[] tangents, outputTangents;
        }

        readonly List<Part> parts = new List<Part>();
        readonly Bounds bounds;
        public bool IsReady => parts.Count > 0;

        public ForestSoftBodyRig(Transform model, Transform space, Bounds localBounds)
        {
            bounds = localBounds;
            if (!model || !space) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                Add(filter.sharedMesh, filter.transform, space, filter, null);
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Add(skin.sharedMesh, skin.transform, space, null, skin);
        }

        void Add(Mesh source, Transform transform, Transform space, MeshFilter filter, SkinnedMeshRenderer skin)
        {
            if (!source) return;
            if (!source.isReadable)
            {
                Debug.LogWarning($"Forest creature mesh '{source.name}' needs Read/Write enabled for its body animation.", transform);
                return;
            }
            var toPose = space.worldToLocalMatrix * transform.localToWorldMatrix;
            var p = new Part
            {
                source = source, mesh = Object.Instantiate(source), filter = filter, skin = skin,
                toLocal = toPose.inverse, rest = source.vertices, normals = source.normals, tangents = source.tangents,
                output = new Vector3[source.vertexCount], outputNormals = new Vector3[source.vertexCount],
                outputTangents = new Vector4[source.vertexCount]
            };
            p.mesh.name = source.name + " — Forest Soft Body";
            p.mesh.MarkDynamic();
            for (int i = 0; i < p.rest.Length; i++)
            {
                p.rest[i] = toPose.MultiplyPoint3x4(p.rest[i]);
                if (p.normals.Length == p.rest.Length) p.normals[i] = toPose.MultiplyVector(p.normals[i]).normalized;
                if (p.tangents.Length == p.rest.Length)
                {
                    Vector4 original = p.tangents[i];
                    Vector3 tangent = toPose.MultiplyVector(new Vector3(original.x, original.y, original.z)).normalized;
                    p.tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                }
            }
            if (filter) filter.sharedMesh = p.mesh;
            else skin.sharedMesh = p.mesh;
            parts.Add(p);
        }

        public void Worm(float clock, float strength, float recoil = 0)
        {
            float length = Mathf.Max(.1f, bounds.size.z);
            float width = Mathf.Max(.1f, bounds.size.x);
            foreach (var p in parts)
            {
                for (int i = 0; i < p.rest.Length; i++)
                {
                    Vector3 v = p.rest[i];
                    float z = (v.z - bounds.min.z) / length;
                    float wave = z * Mathf.PI * 2.6f - clock;
                    float taper = Mathf.Lerp(.85f, .24f, z * z);
                    float lateral = Mathf.Sin(wave) * width * .26f * strength * taper;
                    float pulse = 1 + Mathf.Sin(wave + .8f) * .13f * strength;
                    v.x = bounds.center.x + (v.x - bounds.center.x) * pulse + lateral;
                    v.y = bounds.min.y + (v.y - bounds.min.y) * (1 + Mathf.Cos(wave) * .12f * strength);
                    v.y += Mathf.Max(0, Mathf.Sin(wave - .6f)) * .08f * strength;
                    v.z += Mathf.Sin(wave + 1.1f) * .065f * strength - recoil * z * .22f;
                    p.output[i] = p.toLocal.MultiplyPoint3x4(v);
                    if (p.normals.Length == p.rest.Length)
                    {
                        float slope = Mathf.Cos(wave) * width * .26f * strength * taper * Mathf.PI * 2.6f / length;
                        var turn = Quaternion.Euler(0, Mathf.Atan(slope) * Mathf.Rad2Deg, 0);
                        p.outputNormals[i] = p.toLocal.MultiplyVector(turn * p.normals[i]).normalized;
                        if (p.tangents.Length == p.rest.Length)
                        {
                            Vector4 original = p.tangents[i];
                            Vector3 tangent = p.toLocal.MultiplyVector(turn * new Vector3(original.x, original.y, original.z));
                            tangent = Vector3.ProjectOnPlane(tangent, p.outputNormals[i]).normalized;
                            p.outputTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                        }
                    }
                }
                Upload(p);
            }
        }

        /// <param name="crouch">Compresses torso and folds the feet before takeoff / at landing.</param>
        /// <param name="extension">Extends the weighted hind legs backward and front feet forward in flight.</param>
        public void Frog(float clock, float crouch, float extension, float throat)
        {
            float halfWidth = Mathf.Max(.1f, bounds.extents.x), length = Mathf.Max(.1f, bounds.size.z);
            float height = Mathf.Max(.1f, bounds.size.y);
            foreach (var p in parts)
            {
                for (int i = 0; i < p.rest.Length; i++)
                {
                    Vector3 v = p.rest[i];
                    float nx = (v.x - bounds.center.x) / halfWidth;
                    float nz = (v.z - bounds.min.z) / length;
                    float ny = (v.y - bounds.min.y) / height;
                    float side = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .72f, Mathf.Abs(nx)));
                    float low = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .65f, ny));
                    float hind = side * low * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.32f, .68f, nz)));
                    float fore = side * low * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.4f, .74f, nz));
                    float chest = (1 - side * .55f) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.42f, .8f, nz));
                    float breathe = Mathf.Sin(clock * 2.8f) * .017f;
                    v.y = bounds.min.y + (v.y - bounds.min.y) * (1 - crouch * .26f + breathe);
                    v.x += Mathf.Sign(nx) * halfWidth * (crouch * .08f + extension * .12f) * (hind + fore * .6f);
                    v.z += length * (-.32f * extension * hind + .12f * extension * fore + .07f * crouch * hind);
                    v.y += height * extension * (.06f * fore - .06f * hind);
                    v.x += nx * throat * .08f * chest;
                    v.y += throat * .045f * chest;
                    p.output[i] = p.toLocal.MultiplyPoint3x4(v);
                    if (p.normals.Length == p.rest.Length)
                    {
                        Vector3 n = p.normals[i];
                        n.y /= Mathf.Max(.6f, 1 - crouch * .26f);
                        p.outputNormals[i] = p.toLocal.MultiplyVector(n.normalized).normalized;
                        if (p.tangents.Length == p.rest.Length)
                        {
                            Vector4 original = p.tangents[i];
                            Vector3 tangent = new Vector3(original.x, original.y * (1 - crouch * .26f), original.z);
                            tangent = Vector3.ProjectOnPlane(p.toLocal.MultiplyVector(tangent), p.outputNormals[i]).normalized;
                            p.outputTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                        }
                    }
                }
                Upload(p);
            }
        }

        static void Upload(Part p)
        {
            p.mesh.vertices = p.output;
            if (p.normals.Length == p.output.Length) p.mesh.normals = p.outputNormals;
            if (p.tangents.Length == p.output.Length && p.normals.Length == p.output.Length) p.mesh.tangents = p.outputTangents;
            p.mesh.RecalculateBounds();
            if (p.skin) p.skin.localBounds = p.mesh.bounds;
        }

        public void Dispose()
        {
            foreach (var p in parts)
            {
                if (p.filter) p.filter.sharedMesh = p.source;
                if (p.skin) p.skin.sharedMesh = p.source;
                if (p.mesh) Object.Destroy(p.mesh);
            }
            parts.Clear();
        }
    }
}
