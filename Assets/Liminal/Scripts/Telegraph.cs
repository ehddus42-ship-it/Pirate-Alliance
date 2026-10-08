using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Thin white center guides for melee and attacks connected to their owner.</summary>
    public sealed class Telegraph : MonoBehaviour
    {
        public const float OutlineWidth = .035f;
        static Material outlineMaterial;
        Mesh mesh;
        MeshRenderer outline;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        public bool Visible { get; private set; }

        public static Telegraph Create(Transform parent, string name = "Telegraph")
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(parent, false);
            var warning = go.AddComponent<Telegraph>();
            warning.mesh = new Mesh { name = "White attack center guide" };
            warning.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = warning.mesh;
            warning.outline = go.AddComponent<MeshRenderer>();
            if (!outlineMaterial) outlineMaterial = CreateOverlayMaterial("White attack center guide");
            warning.outline.sharedMaterial = outlineMaterial;
            warning.outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            warning.outline.receiveShadows = false;
            warning.Hide();
            return warning;
        }

        /// <summary>Remains visible above raised water, planks and decorative bridge decks.</summary>
        public static Material CreateOverlayMaterial(string name)
        {
            var material = new Material(Resources.Load<Shader>("TelegraphOverlay")) { name = name, renderQueue = 3101 };
            material.SetColor("_BaseColor", Color.white);
            return material;
        }

        public void Fan(Vector3 origin, Vector3 forward, float radius, float halfAngle, float progress)
            => CenterLine(origin, forward, radius, progress);

        public void Circle(Vector3 center, float radius, float progress)
        {
            Begin();
            Color color = OutlineColor(progress);
            // Radial attacks have no forward axis. Mark only their center with a compact cross.
            float arm = Mathf.Min(.25f, Mathf.Max(0, radius) * .25f);
            Band(center - Vector3.right * arm, center + Vector3.right * arm, color);
            Band(center - Vector3.forward * arm, center + Vector3.forward * arm, color);
            End();
        }

        public void Line(Vector3 origin, Vector3 forward, float length, float halfWidth, float progress)
            => CenterLine(origin, forward, length, progress);

        void CenterLine(Vector3 origin, Vector3 forward, float length, float progress)
        {
            Begin();
            Band(origin, origin + Flat(forward) * Mathf.Max(0, length), OutlineColor(progress));
            End();
        }

        // Anticipation ends at the strike: do not leave an extra floor burst behind.
        public void Release() => Hide();
        public void Hide() { Visible = false; if (outline) outline.enabled = false; }

        void Begin()
        {
            vertices.Clear(); colors.Clear(); triangles.Clear();
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            if (outline) outline.SetPropertyBlock(null);
        }

        void Band(Vector3 from, Vector3 to, Color color)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return;
            Vector3 side = Vector3.Cross(Vector3.up, delta.normalized) * (OutlineWidth * .5f);
            int n = vertices.Count;
            vertices.Add(from - side); vertices.Add(from + side); vertices.Add(to + side); vertices.Add(to - side);
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
        }

        void End()
        {
            for (int i = 0; i < vertices.Count; i++)
                vertices[i] = transform.InverseTransformPoint(vertices[i] + Vector3.up * .038f);
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            Visible = true; outline.enabled = true;
        }

        static Color OutlineColor(float progress) => new Color(1, 1, 1, Mathf.Lerp(.58f, .95f, Mathf.Clamp01(progress)));
        static Vector3 Flat(Vector3 value)
        {
            value.y = 0;
            return value.sqrMagnitude > .000001f ? value.normalized : Vector3.forward;
        }

        void OnDisable() => Hide();
        void OnDestroy()
        {
            if (!mesh) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }

    /// <summary>Converts legacy charge footprints to center guides without rendering the source LineRenderer.</summary>
    [DisallowMultipleComponent]
    public sealed class TelegraphOverlay : MonoBehaviour
    {
        public LineRenderer source;
        public float duration = 1f;
        Telegraph telegraph;
        float shownAt = -1;
        Vector3[] points = new Vector3[128];

        public static TelegraphOverlay Attach(LineRenderer line, float duration)
        {
            if (!line) return null;
            var overlay = line.GetComponent<TelegraphOverlay>();
            if (!overlay) overlay = line.gameObject.AddComponent<TelegraphOverlay>();
            overlay.source = line; overlay.duration = duration;
            line.forceRenderingOff = true;
            return overlay;
        }

        void LateUpdate()
        {
            if (!source) return;
            if (!telegraph) telegraph = Telegraph.Create(transform, "White charge center guide");
            if (!source.enabled || !source.gameObject.activeInHierarchy)
            {
                telegraph.Hide(); shownAt = -1; return;
            }
            if (shownAt < 0) shownAt = Time.time;
            float progress = Mathf.Clamp01((Time.time - shownAt) / Mathf.Max(.05f, duration));
            int n = source.positionCount;
            if (n > points.Length) points = new Vector3[n];
            source.GetPositions(points);
            if (!source.useWorldSpace) for (int i = 0; i < n; i++) points[i] = source.transform.TransformPoint(points[i]);
            if (n == 5)
            {
                Vector3 a = (points[0] + points[1]) * .5f, b = (points[2] + points[3]) * .5f;
                telegraph.Line(a, b - a, Vector3.Distance(a, b), Vector3.Distance(points[0], points[1]) * .5f, progress);
            }
            else if (n >= 16)
            {
                Vector3 center = Vector3.zero;
                for (int i = 0; i < n; i++) center += points[i];
                center /= n;
                float radius = 0;
                for (int i = 0; i < n; i++) radius += Vector3.Distance(points[i], center);
                telegraph.Circle(center, radius / n, progress);
            }
        }

        void OnDisable() { if (telegraph) telegraph.Hide(); shownAt = -1; }
    }
}
