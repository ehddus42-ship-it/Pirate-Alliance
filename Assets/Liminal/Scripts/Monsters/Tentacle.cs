using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A wet, tapering tentacle drawn as a tube mesh rebuilt every frame. Its path is a curved reach from the
    /// root toward the target with a travelling wave along it. <see cref="Shape"/> sets how far it is extended
    /// (0 = inside the locker, 1 = at the target).
    /// </summary>
    public sealed class Tentacle : MonoBehaviour
    {
        const int Rings = 18, Sides = 8;
        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly Vector3[] vertices = new Vector3[Rings * Sides];
        readonly Vector3[] normals = new Vector3[Rings * Sides];
        readonly Vector3[] path = new Vector3[Rings];
        public float rootRadius = .11f, tipRadius = .022f;
        public Vector3 Tip { get; private set; }

        public static Tentacle Create(Transform parent, Material material, float phase)
        {
            var go = new GameObject("Tentacle") { layer = 2 };
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Tentacle>();
            t.Init(material);
            t.phase = phase;
            return t;
        }

        float phase;

        void Init(Material material)
        {
            mesh = new Mesh { name = "Tentacle" };
            mesh.MarkDynamic();
            var triangles = new int[(Rings - 1) * Sides * 6];
            int k = 0;
            for (int r = 0; r < Rings - 1; r++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = r * Sides + s, b = r * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                    triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                    triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.enabled = false;
        }

        /// <summary>World-space shape: from `root` (leaving along `outward`) to `target`, extended by `extension`.</summary>
        public void Shape(Vector3 root, Vector3 outward, Vector3 target, float extension, float wave)
        {
            extension = Mathf.Clamp01(extension);
            meshRenderer.enabled = extension > .01f;
            if (!meshRenderer.enabled) return;
            float reach = Vector3.Distance(root, target);
            Vector3 c1 = root + outward * Mathf.Max(.4f, reach * .35f);
            Vector3 c2 = target + Vector3.up * Mathf.Min(.9f, reach * .25f);
            Vector3 along = (target - root).normalized;
            Vector3 sideways = Vector3.Cross(Vector3.up, along).normalized;
            if (sideways.sqrMagnitude < .01f) sideways = Vector3.right;
            Vector3 lift = Vector3.Cross(along, sideways);
            for (int i = 0; i < Rings; i++)
            {
                float u = extension * i / (Rings - 1f);
                Vector3 p = Bezier(root, c1, c2, target, u);
                // Travelling wave that dies out at the root and at the tip, so it reads as muscle, not noise.
                float envelope = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * .28f * Mathf.Min(1, reach / 2f);
                p += sideways * (Mathf.Sin(u * 9f - wave * 7f + phase) * envelope)
                     + lift * (Mathf.Cos(u * 7f - wave * 5f + phase * 1.3f) * envelope * .6f);
                path[i] = p;
            }
            Tip = path[Rings - 1];
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Vector3 previousNormal = Vector3.zero;
            for (int i = 0; i < Rings; i++)
            {
                Vector3 tangent = (i < Rings - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = along;
                Vector3 normal = previousNormal.sqrMagnitude > 0 ? Vector3.ProjectOnPlane(previousNormal, tangent).normalized : Vector3.Cross(tangent, Vector3.up).normalized;
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right).normalized;
                previousNormal = normal;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                float u = i / (Rings - 1f);
                // Taper, with a slight bulge a third of the way out and a pulse running along it.
                float radius = Mathf.Lerp(rootRadius, tipRadius, Mathf.Pow(u, .8f)) * (1 + .18f * Mathf.Sin(u * Mathf.PI * 1.5f))
                               * (1 + .08f * Mathf.Sin(u * 12f - wave * 10f));
                for (int s = 0; s < Sides; s++)
                {
                    float a = s * Mathf.PI * 2 / Sides;
                    Vector3 n = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    vertices[i * Sides + s] = toLocal.MultiplyPoint3x4(path[i] + n * radius);
                    normals[i * Sides + s] = toLocal.MultiplyVector(n);
                }
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        public void Hide() { if (meshRenderer) meshRenderer.enabled = false; }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        void OnDestroy() { if (mesh) Destroy(mesh); }
    }
}
