using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace NamgungLuna.Magica
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

        const int ClothPointsPerAnchor = 24;
        const int AnchorsPerVertex = 4;

        class Anchor
        {
            public int[] cloth;            // target vertex indices
            public Vector3[] clothRest;    // rest positions relative to restCenter (target space)
            public Vector3 restCenter;
            public Quaternion rotation = Quaternion.identity;
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
        }

        readonly List<Anchor> anchors = new List<Anchor>();
        readonly List<Binding> bindings = new List<Binding>();
        readonly List<Vector3> targetNow = new List<Vector3>();
        MeshFilter targetFilter;
        Mesh targetOriginal;
        int targetVertexCount;
        bool subscribed;

        void Awake()
        {
            // Bind in the authored pose, before Magica swaps the cloth mesh.
            if (target == null) return;
            targetFilter = target.GetComponent<MeshFilter>();
            targetOriginal = targetFilter.sharedMesh;
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
                b.deformed = Instantiate(b.original);
                b.deformed.name = b.original.name + "_Follow";
                b.deformed.MarkDynamic();
                filter.sharedMesh = b.deformed;
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
            }
        }

        // Called by Magica's player loop after it has written the cloth meshes (PostLateUpdate).
        void Follow()
        {
            if (targetFilter == null) return;
            Mesh now = targetFilter.sharedMesh;
            if (now == null || now == targetOriginal) return;   // cloth not built yet
            now.GetVertices(targetNow);
            if (targetNow.Count != targetVertexCount) return;

            foreach (var a in anchors)
            {
                int n = a.cloth.Length;
                if (n < 3) continue;
                Vector3 center = Vector3.zero;
                for (int j = 0; j < n; j++) center += targetNow[a.cloth[j]];
                center /= n;
                // covariance between current and rest offsets; its rotational part is the best-fit rotation
                Vector3 c0 = Vector3.zero, c1 = Vector3.zero, c2 = Vector3.zero;
                for (int j = 0; j < n; j++)
                {
                    Vector3 cur = targetNow[a.cloth[j]] - center, r = a.clothRest[j];
                    c0 += cur * r.x; c1 += cur * r.y; c2 += cur * r.z;
                }
                a.rotation = ExtractRotation(c0, c1, c2, a.rotation);
                a.translation = center - a.rotation * a.restCenter;
            }

            foreach (var b in bindings)
            {
                for (int v = 0; v < b.rest.Length; v++)
                {
                    if (b.weight[v * AnchorsPerVertex] <= 0f) continue;   // no cloth nearby: stays in place
                    Vector3 p = Vector3.zero, nrm = Vector3.zero;
                    for (int j = 0; j < AnchorsPerVertex; j++)
                    {
                        float w = b.weight[v * AnchorsPerVertex + j];
                        if (w <= 0f) continue;
                        var a = anchors[b.anchor[v * AnchorsPerVertex + j]];
                        p += (a.rotation * b.rest[v] + a.translation) * w;
                        nrm += a.rotation * b.restNormal[v] * w;
                    }
                    b.current[v] = b.targetToFollower.MultiplyPoint3x4(p);
                    b.currentNormal[v] = b.targetToFollower.MultiplyVector(nrm).normalized;
                }
                b.deformed.SetVertices(b.current);
                b.deformed.SetNormals(b.currentNormal);
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
