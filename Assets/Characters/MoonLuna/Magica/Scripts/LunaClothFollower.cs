using System.Collections.Generic;
using MagicaCloth2;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace MoonLuna.Magica
{
    /// <summary>
    /// 치마 금장식처럼 천 위에 붙은 작은 파츠가 Magica MeshCloth로 움직이는 천을 따라가게 합니다.
    /// Moves small attached parts (gold hem trims/charms) with a Magica MeshCloth surface.
    /// Anchors are placed every few centimetres over the attached parts; each frame every anchor takes the best-fit
    /// rotation + translation of the cloth vertices around it, and each attached vertex blends its nearby anchors
    /// (like skinning). The parts keep their shape even where cloth folds lie close together.
    /// Runs right after Magica writes the cloth mesh each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class LunaClothFollower : MonoBehaviour
    {
        [Tooltip("Magica Cloth가 움직이는 천 렌더러 (예: Skirt_Navy)")]
        public MeshRenderer target;
        [Tooltip("천을 따라갈 장식 렌더러")]
        public MeshRenderer[] followers = new MeshRenderer[0];
        [Tooltip("기준점 간격 (m)")]
        public float anchorSpacing = 0.03f;
        [Tooltip("Editor-generated attachment data. Rebuild after changing the cloth or ornament meshes.")]
        public LunaClothBindingData bindingData;
        public bool UsesBakedBinding { get; private set; }

        const int ClothPointsPerAnchor = 24;
        const int AnchorsPerVertex = 4;
        static readonly ProfilerMarker FollowMarker = new ProfilerMarker("LunaClothFollower.Follow");

        class Anchor
        {
            public int[] cloth;            // target vertex indices
            public Vector3[] clothRest;    // rest positions relative to restCenter (target space)
            public Vector3 restCenter;
            public Quaternion rotation = Quaternion.identity;
            public Matrix4x4 rotationMatrix = Matrix4x4.identity;
            public Vector3 translation;
        }

        class Binding
        {
            public MeshFilter filter;
            public Mesh original, deformed;
            public Vector3[] rest, restNormal, current, currentNormal;   // rest* in target space
            public int[] anchor;
            public float[] weight;
            public Matrix4x4 targetToFollower;
            public float[] anchorMatrix;   // per anchor, targetToFollower * [rotation | translation] (row-major 3x4)
            public NativeArray<float3> jobRest, jobNormals;
            public NativeArray<Vector3> jobVertices, jobOutputNormals;
            public NativeArray<int> jobAnchors;
            public NativeArray<float> jobWeights, jobMatrices;
        }

        readonly List<Anchor> anchors = new List<Anchor>();
        readonly List<Binding> bindings = new List<Binding>();
        readonly List<Vector3> targetNow = new List<Vector3>();
        MeshFilter targetFilter;
        Mesh targetOriginal;
        int targetVertexCount;
        bool subscribed;
        bool wasSimulating;
        MagicaCloth targetCloth;

        void Awake()
        {
            // Bind in the authored pose, before Magica swaps the cloth mesh.
            if (!InitializeTarget()) return;
            UsesBakedBinding = ReadBakedBinding();
            if (!UsesBakedBinding) BuildBindings();
            foreach (var b in bindings)
            {
                b.anchorMatrix = new float[anchors.Count * 12];
                b.deformed = Instantiate(b.original);
                b.deformed.name = b.original.name + "_Follow";
                b.deformed.MarkDynamic();
                b.filter.sharedMesh = b.deformed;
                b.jobRest = new NativeArray<float3>(b.rest.Length, Allocator.Persistent);
                b.jobNormals = new NativeArray<float3>(b.rest.Length, Allocator.Persistent);
                for (int i = 0; i < b.rest.Length; i++) { b.jobRest[i] = b.rest[i]; b.jobNormals[i] = b.restNormal[i]; }
                b.jobVertices = new NativeArray<Vector3>(b.current, Allocator.Persistent);
                b.jobOutputNormals = new NativeArray<Vector3>(b.currentNormal, Allocator.Persistent);
                b.jobAnchors = new NativeArray<int>(b.anchor, Allocator.Persistent);
                b.jobWeights = new NativeArray<float>(b.weight, Allocator.Persistent);
                b.jobMatrices = new NativeArray<float>(b.anchorMatrix.Length, Allocator.Persistent);
            }
            targetNow.Capacity = targetVertexCount;
        }

        bool InitializeTarget()
        {
            if (!target || !(targetFilter = target.GetComponent<MeshFilter>()) || !targetFilter.sharedMesh) return false;
            targetOriginal = targetFilter.sharedMesh;
            targetVertexCount = targetOriginal.vertexCount;
            targetCloth = GetComponent<MagicaCloth>();
            return true;
        }

        bool ReadBakedBinding()
        {
            if (!bindingData || bindingData.target != targetOriginal || bindingData.targetVertexCount != targetVertexCount
                || !Mathf.Approximately(bindingData.anchorSpacing, anchorSpacing) || bindingData.anchors == null
                || bindingData.parts == null || bindingData.parts.Length != followers.Length) return false;
            for (int i = 0; i < followers.Length; i++)
            {
                var part = bindingData.parts[i];
                if (!followers[i] || part == null || part.source != followers[i].GetComponent<MeshFilter>().sharedMesh
                    || part.rest.Length != part.source.vertexCount) return false;
                Matrix4x4 actual = followers[i].transform.worldToLocalMatrix * target.transform.localToWorldMatrix;
                for (int j = 0; j < 16; j++) if (Mathf.Abs(actual[j] - part.targetToFollower[j]) > .0001f) return false;
            }
            foreach (var a in bindingData.anchors) anchors.Add(new Anchor { cloth = a.cloth, clothRest = a.clothRest, restCenter = a.restCenter });
            for (int i = 0; i < followers.Length; i++)
            {
                var p = bindingData.parts[i];
                bindings.Add(new Binding { filter = followers[i].GetComponent<MeshFilter>(), original = p.source,
                    rest = p.rest, restNormal = p.restNormal, anchor = p.anchor, weight = p.weight,
                    targetToFollower = p.targetToFollower, current = p.source.vertices, currentNormal = p.source.normals });
            }
            return true;
        }

#if UNITY_EDITOR
        public LunaClothBindingData BakeBinding()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Bake attachments outside Play mode.");
            if (!InitializeTarget()) throw new System.InvalidOperationException("The follower needs a target mesh.");
            anchors.Clear(); bindings.Clear(); BuildBindings();
            var data = ScriptableObject.CreateInstance<LunaClothBindingData>();
            data.target = targetOriginal; data.targetVertexCount = targetVertexCount; data.anchorSpacing = anchorSpacing;
            data.anchors = anchors.ConvertAll(a => new LunaClothBindingData.Anchor { cloth = a.cloth, clothRest = a.clothRest, restCenter = a.restCenter }).ToArray();
            data.parts = bindings.ConvertAll(b => new LunaClothBindingData.Part { source = b.original, rest = b.rest, restNormal = b.restNormal,
                anchor = b.anchor, weight = b.weight, targetToFollower = b.targetToFollower }).ToArray();
            anchors.Clear(); bindings.Clear();
            return data;
        }
#endif

        void BuildBindings()
        {
            Vector3[] clothRest = targetOriginal.vertices;
            targetVertexCount = clothRest.Length;
            Matrix4x4 targetToWorld = target.transform.localToWorldMatrix;

            float cell = anchorSpacing;
            var clothGrid = new Dictionary<Vector3Int, List<int>>();
            var clothWorld = new Vector3[clothRest.Length];
            for (int i = 0; i < clothRest.Length; i++)
            {
                clothWorld[i] = targetToWorld.MultiplyPoint3x4(clothRest[i]);
                var key = Vector3Int.FloorToInt(clothWorld[i] / cell);
                if (!clothGrid.TryGetValue(key, out var list)) clothGrid[key] = list = new List<int>();
                list.Add(i);
            }

            // anchors: one per occupied cell of attached-part vertices
            var anchorCells = new Dictionary<Vector3Int, int>();
            var anchorWorld = new List<Vector3>();
            var anchorCount = new List<int>();
            var partWorld = new List<Vector3[]>();
            foreach (var renderer in followers)
            {
                if (renderer == null) { partWorld.Add(null); continue; }
                var verts = renderer.GetComponent<MeshFilter>().sharedMesh.vertices;
                var world = new Vector3[verts.Length];
                Matrix4x4 m = renderer.transform.localToWorldMatrix;
                for (int v = 0; v < verts.Length; v++)
                {
                    world[v] = m.MultiplyPoint3x4(verts[v]);
                    var key = Vector3Int.FloorToInt(world[v] / cell);
                    if (!anchorCells.TryGetValue(key, out int a))
                    {
                        anchorCells[key] = a = anchorWorld.Count;
                        anchorWorld.Add(Vector3.zero); anchorCount.Add(0);
                    }
                    anchorWorld[a] += world[v]; anchorCount[a]++;
                }
                partWorld.Add(world);
            }
            Matrix4x4 worldToTarget = target.transform.worldToLocalMatrix;
            var near = new List<(float d, int i)>();
            for (int a = 0; a < anchorWorld.Count; a++)
            {
                Vector3 c = anchorWorld[a] / anchorCount[a];
                anchorWorld[a] = c;
                near.Clear();
                Vector3Int k = Vector3Int.FloorToInt(c / cell);
                for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                    if (clothGrid.TryGetValue(k + new Vector3Int(x, y, z), out var list))
                        foreach (int i in list) near.Add(((clothWorld[i] - c).sqrMagnitude, i));
                near.Sort((p, q) => p.d.CompareTo(q.d));
                int n = Mathf.Min(ClothPointsPerAnchor, near.Count);
                var anchor = new Anchor { cloth = new int[n], clothRest = new Vector3[n] };
                for (int j = 0; j < n; j++) { anchor.cloth[j] = near[j].i; anchor.restCenter += clothRest[near[j].i]; }
                if (n > 0) anchor.restCenter /= n;
                for (int j = 0; j < n; j++) anchor.clothRest[j] = clothRest[anchor.cloth[j]] - anchor.restCenter;
                anchors.Add(anchor);
            }

            // attached vertices blend the anchors around them (Gaussian falloff)
            var anchorGrid = new Dictionary<Vector3Int, List<int>>();
            for (int a = 0; a < anchorWorld.Count; a++)
            {
                if (anchors[a].cloth.Length < 3) continue;
                var key = Vector3Int.FloorToInt(anchorWorld[a] / cell);
                if (!anchorGrid.TryGetValue(key, out var list)) anchorGrid[key] = list = new List<int>();
                list.Add(a);
            }
            for (int f = 0; f < followers.Length; f++)
            {
                var renderer = followers[f];
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var b = new Binding { filter = filter, original = filter.sharedMesh };
                Vector3[] verts = b.original.vertices, normals = b.original.normals;
                int count = verts.Length;
                b.targetToFollower = renderer.transform.worldToLocalMatrix * target.transform.localToWorldMatrix;
                Matrix4x4 followerToTarget = b.targetToFollower.inverse;
                b.rest = new Vector3[count]; b.restNormal = new Vector3[count];
                b.current = (Vector3[])verts.Clone(); b.currentNormal = (Vector3[])normals.Clone();
                b.anchor = new int[count * AnchorsPerVertex]; b.weight = new float[count * AnchorsPerVertex];
                for (int v = 0; v < count; v++)
                {
                    b.rest[v] = followerToTarget.MultiplyPoint3x4(verts[v]);
                    b.restNormal[v] = followerToTarget.MultiplyVector(normals[v]).normalized;
                    Vector3 p = partWorld[f][v];
                    near.Clear();
                    Vector3Int k = Vector3Int.FloorToInt(p / cell);
                    for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    for (int z = -2; z <= 2; z++)
                        if (anchorGrid.TryGetValue(k + new Vector3Int(x, y, z), out var list))
                            foreach (int a in list) near.Add(((anchorWorld[a] - p).sqrMagnitude, a));
                    near.Sort((x0, x1) => x0.d.CompareTo(x1.d));
                    float sum = 0f;
                    for (int j = 0; j < AnchorsPerVertex && j < near.Count; j++)
                    {
                        float w = Mathf.Exp(-near[j].d / (cell * cell)) + 1e-6f;
                        b.anchor[v * AnchorsPerVertex + j] = near[j].i; b.weight[v * AnchorsPerVertex + j] = w; sum += w;
                    }
                    for (int j = 0; j < AnchorsPerVertex; j++) b.weight[v * AnchorsPerVertex + j] = sum > 0f ? b.weight[v * AnchorsPerVertex + j] / sum : 0f;
                }
                bindings.Add(b);
            }
        }

        void OnEnable()
        {
            if (!subscribed) { MagicaManager.afterDelayedDelegate += Follow; subscribed = true; }
        }

        void OnDisable()
        {
            if (subscribed) { MagicaManager.afterDelayedDelegate -= Follow; subscribed = false; }
        }

        void OnDestroy()
        {
            foreach (var b in bindings)
            {
                if (b.filter != null) b.filter.sharedMesh = b.original;
                if (b.deformed != null) Destroy(b.deformed);
                if (b.jobRest.IsCreated) b.jobRest.Dispose();
                if (b.jobNormals.IsCreated) b.jobNormals.Dispose();
                if (b.jobVertices.IsCreated) b.jobVertices.Dispose();
                if (b.jobOutputNormals.IsCreated) b.jobOutputNormals.Dispose();
                if (b.jobAnchors.IsCreated) b.jobAnchors.Dispose();
                if (b.jobWeights.IsCreated) b.jobWeights.Dispose();
                if (b.jobMatrices.IsCreated) b.jobMatrices.Dispose();
            }
        }

        // Called by Magica's player loop after it has written the cloth meshes (PostLateUpdate).
        // Runs every frame for ~25k trim vertices, so the inner loops use plain float math instead of Vector3/Quaternion
        // operators: each anchor's rigid motion is folded together with targetToFollower into one 3x4 matrix per binding,
        // and each vertex blends those matrices once (sum w * (T * (R * rest + t)) = (sum w * T[R|t]) * rest, weights sum to 1).
        void Follow()
        {
            if (targetFilter == null) return;
            if (targetCloth && (!targetCloth.isActiveAndEnabled || !targetCloth.Process.IsRunning()
                || !MagicaManager.Team.GetTeamDataRef(targetCloth.Process.TeamId).IsProcess))
            {
                ResetAfterCulling();
                return;
            }
            Mesh now = targetFilter.sharedMesh;
            if (now == null || now == targetOriginal) { ResetAfterCulling(); return; }
            now.GetVertices(targetNow);
            if (targetNow.Count != targetVertexCount) return;
            wasSimulating = true;
            using var scope = FollowMarker.Auto();

            for (int ai = 0; ai < anchors.Count; ai++)
            {
                Anchor a = anchors[ai];
                int n = a.cloth.Length;
                if (n < 3) continue;
                float cx = 0f, cy = 0f, cz = 0f;
                for (int j = 0; j < n; j++) { Vector3 p = targetNow[a.cloth[j]]; cx += p.x; cy += p.y; cz += p.z; }
                float inv = 1f / n;
                cx *= inv; cy *= inv; cz *= inv;
                // covariance between current and rest offsets; its rotational part is the best-fit rotation
                float c0x = 0f, c0y = 0f, c0z = 0f, c1x = 0f, c1y = 0f, c1z = 0f, c2x = 0f, c2y = 0f, c2z = 0f;
                for (int j = 0; j < n; j++)
                {
                    Vector3 p = targetNow[a.cloth[j]], r = a.clothRest[j];
                    float ux = p.x - cx, uy = p.y - cy, uz = p.z - cz;
                    c0x += ux * r.x; c0y += uy * r.x; c0z += uz * r.x;
                    c1x += ux * r.y; c1y += uy * r.y; c1z += uz * r.y;
                    c2x += ux * r.z; c2y += uy * r.z; c2z += uz * r.z;
                }
                a.rotation = ExtractRotation(new Vector3(c0x, c0y, c0z), new Vector3(c1x, c1y, c1z), new Vector3(c2x, c2y, c2z), a.rotation);
                a.translation = new Vector3(cx, cy, cz) - a.rotation * a.restCenter;
                a.rotationMatrix = Matrix4x4.Rotate(a.rotation);
            }

            // Every part deforms in parallel; the frame waits once for all of them.
            JobHandle all = default;
            foreach (var b in bindings)
            {
                // per anchor: M = targetToFollower * [R | t] as a row-major 3x4
                Matrix4x4 T = b.targetToFollower;
                float[] m = b.anchorMatrix;
                for (int ai = 0; ai < anchors.Count; ai++)
                {
                    Anchor a = anchors[ai];
                    Matrix4x4 r = a.rotationMatrix;
                    Vector3 t = a.translation;
                    int o = ai * 12;
                    for (int row = 0; row < 3; row++)
                    {
                        float t0 = T[row, 0], t1 = T[row, 1], t2 = T[row, 2];
                        m[o + row * 4 + 0] = t0 * r.m00 + t1 * r.m10 + t2 * r.m20;
                        m[o + row * 4 + 1] = t0 * r.m01 + t1 * r.m11 + t2 * r.m21;
                        m[o + row * 4 + 2] = t0 * r.m02 + t1 * r.m12 + t2 * r.m22;
                        m[o + row * 4 + 3] = t0 * t.x + t1 * t.y + t2 * t.z + T[row, 3];
                    }
                }

                b.jobMatrices.CopyFrom(m);
                var handle = new DeformJob { rest = b.jobRest, normals = b.jobNormals, anchor = b.jobAnchors, weight = b.jobWeights,
                    matrices = b.jobMatrices, vertices = b.jobVertices, outputNormals = b.jobOutputNormals }.Schedule(b.rest.Length, 128);
                all = JobHandle.CombineDependencies(all, handle);
            }
            all.Complete();
            foreach (var b in bindings)
            {
                b.deformed.SetVertices(b.jobVertices);
                b.deformed.SetNormals(b.jobOutputNormals);
            }
        }

        void ResetAfterCulling()
        {
            if (!wasSimulating) return;
            wasSimulating = false;
            foreach (var a in anchors) { a.rotation = Quaternion.identity; a.rotationMatrix = Matrix4x4.identity; a.translation = Vector3.zero; }
            foreach (var b in bindings)
            {
                b.jobVertices.CopyFrom(b.current); b.jobOutputNormals.CopyFrom(b.currentNormal);
                b.deformed.SetVertices(b.jobVertices); b.deformed.SetNormals(b.jobOutputNormals);
            }
        }

        [BurstCompile]
        struct DeformJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> rest, normals;
            [ReadOnly] public NativeArray<int> anchor;
            [ReadOnly] public NativeArray<float> weight, matrices;
            public NativeArray<Vector3> vertices, outputNormals;
            public void Execute(int v)
            {
                int k = v * AnchorsPerVertex;
                if (weight[k] <= 0) return;
                float4 row0 = 0, row1 = 0, row2 = 0;
                for (int j = 0; j < AnchorsPerVertex; j++)
                {
                    float w = weight[k + j]; if (w <= 0) continue;
                    int o = anchor[k + j] * 12;
                    row0 += w * new float4(matrices[o], matrices[o + 1], matrices[o + 2], matrices[o + 3]);
                    row1 += w * new float4(matrices[o + 4], matrices[o + 5], matrices[o + 6], matrices[o + 7]);
                    row2 += w * new float4(matrices[o + 8], matrices[o + 9], matrices[o + 10], matrices[o + 11]);
                }
                float4 p = new float4(rest[v], 1);
                vertices[v] = new float3(math.dot(row0, p), math.dot(row1, p), math.dot(row2, p));
                float3 q = normals[v];
                float3 n = new float3(math.dot(row0.xyz, q), math.dot(row1.xyz, q), math.dot(row2.xyz, q));
                outputNormals[v] = math.lengthsq(n) > 1e-10f ? math.normalize(n) : float3.zero;
            }
        }

        /// <summary>
        /// Rotational part of the 3x3 matrix with columns c0..c2, warm-started from q
        /// (Müller et al. 2016, "A Robust Method to Extract the Rotational Part of Deformations").
        /// </summary>
        static Quaternion ExtractRotation(Vector3 c0, Vector3 c1, Vector3 c2, Quaternion q)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 r0 = q * Vector3.right, r1 = q * Vector3.up, r2 = q * Vector3.forward;
                Vector3 omega = (Vector3.Cross(r0, c0) + Vector3.Cross(r1, c1) + Vector3.Cross(r2, c2))
                                * (1f / (Mathf.Abs(Vector3.Dot(r0, c0) + Vector3.Dot(r1, c1) + Vector3.Dot(r2, c2)) + 1e-9f));
                float w = omega.magnitude;
                if (w < 1e-6f) break;
                q = Quaternion.AngleAxis(w * Mathf.Rad2Deg, omega / w) * q;
                q.Normalize();
            }
            return q;
        }
    }
}
