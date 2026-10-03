using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Premium floor telegraph: a decal-like danger area instead of a bare outline.
    /// - a translucent crimson field that brightens toward its edge;
    /// - a progress fill that grows from the origin to the edge as the attack winds up, so the timing reads at a
    ///   glance;
    /// - a saturated rim with a dark outer stroke, so it still reads on the bright liminal floors (an additive glow
    ///   washes out to cream there), plus a bright line on the progress front;
    /// - chevrons that stream along line attacks in their direction, and tick marks on fans;
    /// - it flashes white just before the strike and leaves a short burst when the attack fires.
    /// Shapes: <see cref="Fan"/>, <see cref="Line"/>, <see cref="Circle"/>. Call one every frame while
    /// telegraphing, then <see cref="Release"/> (attack fired) or <see cref="Hide"/>.
    /// </summary>
    public sealed class Telegraph : MonoBehaviour
    {
        static Material fillMaterial, glowMaterial;
        static readonly Color Field = new Color(.62f, .02f, .06f, .2f), FieldEdge = new Color(.86f, .06f, .08f, .34f);
        static readonly Color Fill = new Color(.96f, .12f, .08f, .44f), Rim = new Color(1f, .3f, .16f, .95f), Front = new Color(1f, .86f, .5f, 1f);
        static readonly Color Edge = new Color(.22f, 0f, .02f, .55f);

        Mesh fillMesh, glowMesh;
        MeshRenderer fillRenderer, glowRenderer;
        readonly List<Vector3> fv = new List<Vector3>(), gv = new List<Vector3>();
        readonly List<Color> fc = new List<Color>(), gc = new List<Color>();
        readonly List<int> ft = new List<int>(), gt = new List<int>();
        float releasedAt = -1, lastProgress;
        public bool Visible { get; private set; }

        public static Telegraph Create(Transform parent, string name = "Telegraph")
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Telegraph>();
            t.Init();
            return t;
        }

        void Init()
        {
            fillMesh = new Mesh { name = "Telegraph Fill" }; fillMesh.MarkDynamic();
            glowMesh = new Mesh { name = "Telegraph Glow" }; glowMesh.MarkDynamic();
            fillRenderer = Layer("Fill", fillMesh, FillMaterial);
            glowRenderer = Layer("Glow", glowMesh, GlowMaterial);
            SetVisible(false);
        }

        MeshRenderer Layer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Material Make(bool overlay)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            m.SetFloat("_Surface", 1);
            // Both layers alpha-blend; the glow layer just draws on top of the fill.
            m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = overlay ? 3002 : 3001;
            return m;
        }

        static Material FillMaterial => fillMaterial ? fillMaterial : fillMaterial = Make(false);
        static Material GlowMaterial => glowMaterial ? glowMaterial : glowMaterial = Make(true);

        void SetVisible(bool on)
        {
            Visible = on;
            if (fillRenderer) fillRenderer.enabled = on;
            if (glowRenderer) glowRenderer.enabled = on;
        }

        // ---- shapes ----------------------------------------------------------------------------------------
        public void Fan(Vector3 origin, Vector3 forward, float radius, float halfAngle, float progress)
            => Sector(origin, forward, radius, halfAngle, progress, false);

        public void Circle(Vector3 center, float radius, float progress)
            => Sector(center, Vector3.forward, radius, 180, progress, true);

        void Sector(Vector3 origin, Vector3 forward, float radius, float halfAngle, float progress, bool full)
        {
            Begin(progress);
            forward = Flat(forward);
            float p = Mathf.Clamp01(progress);
            float y = origin.y;
            int segments = Mathf.Max(8, Mathf.CeilToInt(halfAngle / 4f));
            // Rings: centre, a few to the progress front, the front itself, then out to the edge.
            var rings = new List<float> { 0 };
            for (int i = 1; i <= 4; i++) rings.Add(p * i / 4f);
            rings.Add(Mathf.Min(1, p + .015f));
            for (int i = 1; i <= 3; i++) rings.Add(Mathf.Lerp(p, 1, i / 3f));
            rings.Sort();
            int columns = segments + 1;
            for (int r = 0; r < rings.Count; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float a = Mathf.Lerp(-halfAngle, halfAngle, s / (float)segments);
                    Vector3 d = Quaternion.Euler(0, a, 0) * forward;
                    float u = rings[r];
                    fv.Add(new Vector3(origin.x, y, origin.z) + d * (radius * u));
                    fc.Add(FillColor(u, p));
                }
            for (int r = 0; r < rings.Count - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * columns + s, b = a + 1, c = a + columns, d2 = c + 1;
                    ft.Add(a); ft.Add(c); ft.Add(b); ft.Add(b); ft.Add(c); ft.Add(d2);
                }
            // Glow: the outer arc, the two sides (fans), the progress front, and tick marks.
            Color rim = RimColor(p);
            Arc(origin, forward, radius * 1.012f, -halfAngle, halfAngle, segments, .11f, Edge);
            Arc(origin, forward, radius, -halfAngle, halfAngle, segments, .07f, rim);
            if (p > .02f) Arc(origin, forward, radius * p, -halfAngle, halfAngle, segments, .05f, FrontColor(p));
            if (!full)
            {
                Band(origin, origin + Quaternion.Euler(0, -halfAngle, 0) * forward * radius, .05f, rim * new Color(1, 1, 1, .8f));
                Band(origin, origin + Quaternion.Euler(0, halfAngle, 0) * forward * radius, .05f, rim * new Color(1, 1, 1, .8f));
                int ticks = Mathf.Max(2, Mathf.RoundToInt(halfAngle / 15f));
                for (int i = 1; i < ticks * 2; i++)
                {
                    Vector3 d = Quaternion.Euler(0, Mathf.Lerp(-halfAngle, halfAngle, i / (ticks * 2f)), 0) * forward;
                    Band(origin + d * radius * .9f, origin + d * radius * .98f, .035f, rim * new Color(1, 1, 1, .6f));
                }
            }
            else
            {
                // A circle gets an inner ring that contracts toward the centre as the strike nears.
                float inner = Mathf.Lerp(1, .15f, p);
                Arc(origin, forward, radius * inner, -180, 180, segments, .03f, new Color(1, .7f, .5f, .5f));
            }
            End();
        }

        public void Line(Vector3 origin, Vector3 forward, float length, float halfWidth, float progress)
        {
            Begin(progress);
            forward = Flat(forward);
            float p = Mathf.Clamp01(progress);
            Vector3 side = Vector3.Cross(Vector3.up, forward) * halfWidth;
            var rows = new List<float> { 0 };
            for (int i = 1; i <= 4; i++) rows.Add(p * i / 4f);
            rows.Add(Mathf.Min(1, p + .01f));
            for (int i = 1; i <= 3; i++) rows.Add(Mathf.Lerp(p, 1, i / 3f));
            rows.Sort();
            const int across = 4;
            for (int r = 0; r < rows.Count; r++)
                for (int k = 0; k <= across; k++)
                {
                    float w = k / (float)across * 2 - 1;
                    fv.Add(origin + forward * (length * rows[r]) + side * w);
                    // Brighter toward the long edges, like a lit strip.
                    var c = FillColor(rows[r], p);
                    c.a *= .75f + .45f * Mathf.Abs(w);
                    fc.Add(c);
                }
            for (int r = 0; r < rows.Count - 1; r++)
                for (int k = 0; k < across; k++)
                {
                    int a = r * (across + 1) + k, b = a + 1, c = a + across + 1, d = c + 1;
                    ft.Add(a); ft.Add(c); ft.Add(b); ft.Add(b); ft.Add(c); ft.Add(d);
                }
            Color rim = RimColor(p);
            Vector3 end = origin + forward * length;
            Vector3 stroke = side * (1 + .06f / Mathf.Max(.1f, halfWidth));
            Band(origin - stroke, end - stroke, .1f, Edge);
            Band(origin + stroke, end + stroke, .1f, Edge);
            Band(origin - side, end - side, .06f, rim);
            Band(origin + side, end + side, .06f, rim);
            Band(end - side, end + side, .07f, rim);
            if (p > .02f) Band(origin + forward * (length * p) - side, origin + forward * (length * p) + side, .06f, FrontColor(p));
            // Chevrons streaming forward along the charge line.
            float spacing = Mathf.Max(.9f, halfWidth * 1.6f);
            float scroll = (Time.unscaledTime * 3.5f) % spacing;
            for (float d = scroll; d < length - .2f; d += spacing)
            {
                float fade = Mathf.Clamp01(d / 1.2f) * Mathf.Clamp01((length - d) / 1.2f);
                var c = new Color(1f, .55f, .35f, .42f * fade * (d / length <= p ? 1.3f : .8f));
                Vector3 tip = origin + forward * (d + halfWidth * .45f);
                Band(origin + forward * d - side * .55f, tip, .045f, c);
                Band(origin + forward * d + side * .55f, tip, .045f, c);
            }
            End();
        }

        /// <summary>The attack fired: a brief white-hot burst of the last shape, then gone.</summary>
        public void Release()
        {
            if (!Visible) return;
            releasedAt = Time.unscaledTime;
        }

        public void Hide()
        {
            releasedAt = -1;
            SetVisible(false);
        }

        void LateUpdate()
        {
            if (releasedAt < 0) return;
            float t = (Time.unscaledTime - releasedAt) / .16f;
            if (t >= 1) { Hide(); return; }
            float k = 1 - t;
            fillRenderer.transform.localScale = glowRenderer.transform.localScale = Vector3.one;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(1.6f, 1.4f, 1.3f, k));
            glowRenderer.SetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(1.4f, 1.1f, 1f, k));
            fillRenderer.SetPropertyBlock(block);
        }

        // ---- mesh helpers ----------------------------------------------------------------------------------
        void Begin(float progress)
        {
            releasedAt = -1;
            lastProgress = progress;
            fv.Clear(); fc.Clear(); ft.Clear(); gv.Clear(); gc.Clear(); gt.Clear();
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            if (fillRenderer) { fillRenderer.SetPropertyBlock(null); glowRenderer.SetPropertyBlock(null); }
        }

        void End()
        {
            // Draw a hair above the floor, in world space (the object sits at the origin).
            var lift = Vector3.up * .035f;
            for (int i = 0; i < fv.Count; i++) fv[i] = transform.InverseTransformPoint(fv[i] + lift);
            for (int i = 0; i < gv.Count; i++) gv[i] = transform.InverseTransformPoint(gv[i] + lift + Vector3.up * .003f);
            Apply(fillMesh, fv, fc, ft);
            Apply(glowMesh, gv, gc, gt);
            SetVisible(true);
        }

        static void Apply(Mesh mesh, List<Vector3> v, List<Color> c, List<int> t)
        {
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }

        void Band(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 along = b - a; along.y = 0;
            if (along.sqrMagnitude < 1e-6f) return;
            Vector3 n = Vector3.Cross(Vector3.up, along.normalized) * (width * .5f);
            int i = gv.Count;
            gv.Add(a - n); gv.Add(a + n); gv.Add(b + n); gv.Add(b - n);
            var soft = new Color(color.r, color.g, color.b, 0);
            // Bright core along the middle would need more vertices; a soft edge on one side reads well enough.
            gc.Add(color); gc.Add(soft); gc.Add(soft); gc.Add(color);
            gt.Add(i); gt.Add(i + 1); gt.Add(i + 2); gt.Add(i); gt.Add(i + 2); gt.Add(i + 3);
            // Mirror so the band glows on both sides of its centre line.
            int j = gv.Count;
            gv.Add(a + n); gv.Add(a - n * 2.2f); gv.Add(b - n * 2.2f); gv.Add(b + n);
            gc.Add(color * new Color(1, 1, 1, .9f)); gc.Add(soft); gc.Add(soft); gc.Add(color * new Color(1, 1, 1, .9f));
            gt.Add(j); gt.Add(j + 1); gt.Add(j + 2); gt.Add(j); gt.Add(j + 2); gt.Add(j + 3);
        }

        void Arc(Vector3 origin, Vector3 forward, float radius, float from, float to, int segments, float width, Color color)
        {
            Vector3 previous = origin + Quaternion.Euler(0, from, 0) * forward * radius;
            for (int s = 1; s <= segments; s++)
            {
                Vector3 next = origin + Quaternion.Euler(0, Mathf.Lerp(from, to, s / (float)segments), 0) * forward * radius;
                Band(previous, next, width, color);
                previous = next;
            }
        }

        static Color FillColor(float u, float p)
        {
            // Field gets stronger toward the edge; the part already "charged" is a brighter, hotter red.
            Color c = Color.Lerp(Field, FieldEdge, u * u);
            if (u <= p + 1e-4f) c = Color.Lerp(c, Fill, .55f + .45f * (u / Mathf.Max(.001f, p)));
            if (p > .88f) c = Color.Lerp(c, new Color(1, .75f, .65f, c.a + .08f), (p - .88f) / .12f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 50)));
            return c;
        }

        static Color RimColor(float p)
        {
            float pulse = .75f + .25f * Mathf.Sin(Time.unscaledTime * (8 + 20 * p));
            Color c = Rim * pulse;
            if (p > .88f) c = Color.Lerp(c, Color.white, (p - .88f) / .12f);
            c.a = Mathf.Lerp(.55f, 1f, p);
            return c;
        }

        static Color FrontColor(float p) => new Color(Front.r, Front.g, Front.b, .85f * Mathf.Clamp01(p * 4));

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        void OnDestroy()
        {
            if (fillMesh) Destroy(fillMesh);
            if (glowMesh) Destroy(glowMesh);
        }
    }

    /// <summary>
    /// Keeps an existing LineRenderer telegraph working as before (enabled while telegraphing, same logic and
    /// tests) but draws it as a premium <see cref="Telegraph"/> instead: rectangles become charge lines, closed
    /// loops become circles. Progress runs over `duration` from the moment the line turns on.
    /// </summary>
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
            overlay.source = line;
            overlay.duration = duration;
            line.forceRenderingOff = true;
            return overlay;
        }

        void LateUpdate()
        {
            if (!source) return;
            if (!telegraph) telegraph = Telegraph.Create(transform, "Premium Telegraph");
            if (!source.enabled || !source.gameObject.activeInHierarchy)
            {
                if (shownAt >= 0) telegraph.Release();
                shownAt = -1;
                return;
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
                float half = Vector3.Distance(points[0], points[1]) * .5f;
                telegraph.Line(a, b - a, Vector3.Distance(a, b), half, progress);
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
    }
}
