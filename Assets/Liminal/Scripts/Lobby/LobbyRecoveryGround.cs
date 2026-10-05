using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A repaired Korean city plaza: fine modular pavers, coherent replacement contracts and flush utility work.
    /// This is a visual skin only. The existing plaza owns collision, and its .012 m emblem remains above it.
    /// All marks are abstract roadwork shapes; no signs, letters or numbers are generated.
    /// </summary>
    public sealed class LobbyRecoveryGround : MonoBehaviour
    {
        public const float SurfaceHeight = .008f;
        public const float DetailHeight = .0098f;

        // Deliberately large, surveyed repair areas rather than randomly scattered rubble or individual new tiles.
        // Their combined 322.33 square metres cover 20.3% of the 44 x 36 metre plaza.
        static readonly Rect[] ReplacementAreas =
        {
            Rect.MinMaxRect(-21f, -17.4f, -5.5f, -11.5f),
            Rect.MinMaxRect(6f, -16.6f, 20.8f, -10.5f),
            Rect.MinMaxRect(-21f, -10.8f, -16f, 7.4f),
            Rect.MinMaxRect(14.8f, 4.8f, 21f, 12.8f)
        };

        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }

        readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
        readonly List<Object> localOwned = new List<Object>();
        List<Object> owner;
        Material oldPavers, newPavers, concrete, seal, steel, tactileLines, tactileDots, oldPaint, freshPaint, dust;

        public int SurfaceMeshCount { get; private set; }
        public int SurfaceTriangleCount { get; private set; }
        public int TextureCount { get; private set; }
        public float ReplacementArea => 322.33f;

        public static LobbyRecoveryGround Build(Transform parent, List<Object> owned)
        {
            var go = new GameObject("LobbyRecoveryGround");
            go.transform.SetParent(parent, false);
            var result = go.AddComponent<LobbyRecoveryGround>();
            result.owner = owned;
            result.Construct();
            return result;
        }

        void Own(Object value)
        {
            if (owner != null) owner.Add(value);
            else localOwned.Add(value);
        }

        void Construct()
        {
            var pavingTexture = PavingTexture();
            var concreteTexture = AggregateTexture("Repaired cement aggregate", 512, false);
            var metalTexture = CoverTexture();
            var wornTexture = PaintTexture();
            oldPavers = Surface("Retained granite pavers", pavingTexture, new Color(.77f, .77f, .73f), .13f);
            newPavers = Surface("Recent replacement pavers", pavingTexture, new Color(.95f, .955f, .935f), .17f);
            concrete = Surface("Saw cut cement infill", concreteTexture, new Color(.71f, .69f, .63f), .1f);
            seal = Surface("Weathered elastic joint seal", null, new Color(.16f, .17f, .16f), .21f);
            steel = Surface("Replaced utility cover steel", metalTexture, new Color(.41f, .445f, .45f), .33f, .35f);
            tactileLines = Surface("Faded ochre directional tiles", TactileTexture(false), new Color(.73f, .58f, .29f), .16f);
            tactileDots = Surface("Faded ochre warning tiles", TactileTexture(true), new Color(.73f, .58f, .29f), .16f);
            oldPaint = Surface("Worn former white boundary", wornTexture, new Color(.69f, .69f, .62f), .05f);
            freshPaint = Surface("Repainted white boundary", wornTexture, new Color(.91f, .91f, .85f), .09f);
            foreach (var paint in new[] { oldPaint, freshPaint })
            {
                paint.SetFloat("_AlphaClip", 1);
                paint.SetFloat("_Cutoff", .5f);
                paint.EnableKeyword("_ALPHATEST_ON");
                paint.SetOverrideTag("RenderType", "TransparentCutout");
                paint.renderQueue = (int)RenderQueue.AlphaTest;
            }
            dust = Surface("Settled dust beside repair work", DustTexture(), new Color(.62f, .57f, .45f, .24f), .02f);
            dust.SetFloat("_Surface", 1);
            dust.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            dust.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            dust.SetFloat("_ZWrite", 0);
            dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dust.SetOverrideTag("RenderType", "Transparent");
            dust.renderQueue = (int)RenderQueue.Transparent;

            // UV coordinates are in metres / 2.4. A repeat contains twelve 200 x 100 mm pavers across.
            RetainedPaving();
            foreach (var area in ReplacementAreas)
            {
                RectSurface(newPavers, area, .00865f, 2.4f, true);
                RepairEdge(area);
            }

            // Two narrow service excavations, deliberately aligned to the repaired edge districts.
            RectSurface(concrete, Rect.MinMaxRect(-18.45f, -15.5f, -17.95f, 6.2f), .00905f, 1.8f);
            RectSurface(concrete, Rect.MinMaxRect(16.85f, -7.9f, 17.36f, 11.9f), .00905f, 1.8f);
            Line(seal, new Vector2(-18.45f, -15.5f), new Vector2(-18.45f, 6.2f), .018f, .00935f);
            Line(seal, new Vector2(17.36f, -7.9f), new Vector2(17.36f, 11.9f), .018f, .00935f);

            // Few repaired cracks. Broad cement stitching and a narrow seal make their repair more legible than damage.
            SealedJoint(new[] { new Vector2(-20.8f, 2.6f), new Vector2(-18.6f, 2.4f), new Vector2(-16.3f, 1.7f),
                new Vector2(-14.2f, 2.2f), new Vector2(-12.4f, 2.0f) });
            SealedJoint(new[] { new Vector2(11.8f, -7.7f), new Vector2(13.5f, -7.35f), new Vector2(15.4f, -7.5f),
                new Vector2(17.1f, -6.95f), new Vector2(19.9f, -7.2f) });
            SealedJoint(new[] { new Vector2(-4.7f, -13.6f), new Vector2(-2.4f, -13.4f), new Vector2(-.2f, -13.75f),
                new Vector2(1.6f, -13.25f), new Vector2(3.4f, -13.4f) });
            SealedJoint(new[] { new Vector2(-11.6f, 11.2f), new Vector2(-9.7f, 11.35f), new Vector2(-8.0f, 10.9f),
                new Vector2(-6.3f, 11.1f) });

            Drains(-19.65f, -14.8f, 12.8f);
            Drains(19.65f, -14.8f, 12.8f);
            Manhole(new Vector2(-14.9f, -2.4f), .61f);
            Manhole(new Vector2(12.8f, -13.6f), .63f);
            Manhole(new Vector2(16.0f, 8.5f), .56f);
            Manhole(new Vector2(-8.0f, -14.7f), .55f);

            // Small surviving/replaced guidance runs near the approaches. Symbols are only ribs and raised-dot patterns.
            Guidance(new Vector2(-5.0f, -14.1f), 4.4f);
            Guidance(new Vector2(14.4f, -12.5f), 3.7f);
            PaintedBoundaries();

            // Wide, feathered deposits stay at work edges. No randomly scattered chunks in the traversable centre.
            RectSurface(dust, Rect.MinMaxRect(-21.5f, -11.8f, -15.7f, -10.6f), DetailHeight, 1, false, true);
            RectSurface(dust, Rect.MinMaxRect(5.5f, -11.0f, 20.9f, -10.1f), DetailHeight, 1, false, true);
            RectSurface(dust, Rect.MinMaxRect(14.2f, 4.3f, 21.5f, 5.5f), DetailHeight, 1, false, true);
            RectSurface(dust, Rect.MinMaxRect(-21.7f, -7.5f, -20.5f, 8.0f), DetailHeight, 1, false, true);
            Finish();
        }

        void RetainedPaving()
        {
            // Subtract every replacement footprint from the retained plane. Millimetre-high overlays alone
            // can quantize to the same depth in the overview camera, so the two pavements never share area.
            var retained = new List<Rect> { Rect.MinMaxRect(-22, -18, 22, 18) };
            foreach (var cut in ReplacementAreas)
            {
                var next = new List<Rect>();
                foreach (var rect in retained)
                {
                    float left = Mathf.Max(rect.xMin, cut.xMin), right = Mathf.Min(rect.xMax, cut.xMax);
                    float bottom = Mathf.Max(rect.yMin, cut.yMin), top = Mathf.Min(rect.yMax, cut.yMax);
                    if (left >= right || bottom >= top) { next.Add(rect); continue; }
                    // Full-width lower/upper pieces, then the two sides of the intersection's middle band.
                    AddRemainder(next, rect.xMin, rect.yMin, rect.xMax, bottom);
                    AddRemainder(next, rect.xMin, top, rect.xMax, rect.yMax);
                    AddRemainder(next, rect.xMin, bottom, left, top);
                    AddRemainder(next, right, bottom, rect.xMax, top);
                }
                retained = next;
            }
            foreach (var rect in retained) RectSurface(oldPavers, rect, SurfaceHeight, 2.4f);
        }

        static void AddRemainder(List<Rect> list, float left, float bottom, float right, float top)
        {
            if (right - left > .00001f && top - bottom > .00001f)
                list.Add(Rect.MinMaxRect(left, bottom, right, top));
        }

        void RepairEdge(Rect area)
        {
            const float width = .085f;
            RectSurface(concrete, Rect.MinMaxRect(area.xMin - width, area.yMin - width, area.xMax + width, area.yMin), .00905f, 1.8f);
            RectSurface(concrete, Rect.MinMaxRect(area.xMin - width, area.yMax, area.xMax + width, area.yMax + width), .00905f, 1.8f);
            RectSurface(concrete, Rect.MinMaxRect(area.xMin - width, area.yMin, area.xMin, area.yMax), .00905f, 1.8f);
            RectSurface(concrete, Rect.MinMaxRect(area.xMax, area.yMin, area.xMax + width, area.yMax), .00905f, 1.8f);
            // A small offset shows where the replacement contractor cut against the old boundary.
            Line(seal, new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin), .016f, .00935f);
        }

        void SealedJoint(Vector2[] controls)
        {
            var points = new List<Vector2>();
            for (int i = 0; i < controls.Length - 1; i++)
            {
                Vector2 a = controls[Mathf.Max(0, i - 1)], b = controls[i], c = controls[i + 1], d = controls[Mathf.Min(controls.Length - 1, i + 2)];
                for (int s = 0; s < 6; s++)
                {
                    float t = s / 6f;
                    points.Add(.5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t));
                }
            }
            points.Add(controls[controls.Length - 1]);
            Ribbon(concrete, points, .145f, .00915f);
            Ribbon(seal, points, .027f, .00955f);
        }

        void Drains(float x, float from, float to)
        {
            // One continuous cast-iron drain line per side, set into a newer narrow concrete surround.
            RectSurface(concrete, Rect.MinMaxRect(x - .23f, from - .10f, x + .23f, to + .10f), .0091f, 1.8f);
            RectSurface(seal, Rect.MinMaxRect(x - .16f, from, x + .16f, to), .00935f, 1);
            for (float z = from + .025f; z < to - .05f; z += .70f)
            {
                float end = Mathf.Min(z + .655f, to - .025f);
                RectSurface(steel, Rect.MinMaxRect(x - .142f, z, x + .142f, end), .00965f, .55f);
                // Real openings remain flush painted recesses, never holes in the collision plane.
                for (float s = z + .07f; s < end - .04f; s += .095f)
                    RectSurface(seal, Rect.MinMaxRect(x - .11f, s, x + .11f, s + .042f), DetailHeight, 1);
            }
        }

        void Manhole(Vector2 centre, float radius)
        {
            Disk(concrete, centre, radius + .16f, .0091f, 48);
            Disk(seal, centre, radius + .035f, .0094f, 48);
            Disk(steel, centre, radius, .00965f, 48);
            // Flush lifting slots and four restrained geometric bolt heads; no municipal lettering.
            for (int side = -1; side <= 1; side += 2)
                RectSurface(seal, new Rect(centre.x + side * radius * .55f - .025f, centre.y - .10f, .05f, .20f), DetailHeight, 1);
        }

        void Guidance(Vector2 centre, float length)
        {
            RectSurface(tactileLines, new Rect(centre.x - .21f, centre.y - length * .5f, .42f, length), .00955f, .30f);
            RectSurface(tactileDots, new Rect(centre.x - .31f, centre.y + length * .5f, .62f, .62f), .00955f, .30f);
            // Thin replacement grout bounds the guide without making a raised obstacle.
            Line(concrete, new Vector2(centre.x - .23f, centre.y - length * .5f), new Vector2(centre.x - .23f, centre.y + length * .5f), .025f, DetailHeight);
        }

        void PaintedBoundaries()
        {
            // A formerly continuous service boundary has been re-marked only where repaving removed it.
            for (int i = 0; i < 5; i++)
            {
                float z = -16.8f + i * 1.28f;
                RectSurface(i < 3 ? freshPaint : oldPaint, new Rect(-6.15f, z, .095f, .74f), DetailHeight, .68f);
                RectSurface(i < 4 ? freshPaint : oldPaint, new Rect(6.15f, z + .13f, .095f, .74f), DetailHeight, .68f);
            }
            // Old loading-bay corner traces remain beside the utility route, away from the central emblem.
            RectSurface(oldPaint, new Rect(-16.0f, -8.8f, 1.7f, .095f), DetailHeight, .68f);
            RectSurface(oldPaint, new Rect(-16.0f, -8.8f, .095f, .8f), DetailHeight, .68f);
            RectSurface(freshPaint, new Rect(13.9f, 3.1f, 1.5f, .085f), DetailHeight, .68f);
            RectSurface(freshPaint, new Rect(15.32f, 2.4f, .085f, .78f), DetailHeight, .68f);
        }

        Material Surface(string name, Texture2D texture, Color colour, float smoothness, float metallic = 0)
        {
            var result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            result.SetColor("_BaseColor", colour);
            result.SetFloat("_Smoothness", smoothness);
            result.SetFloat("_Metallic", metallic);
            if (texture) result.SetTexture("_BaseMap", texture);
            Own(result);
            return result;
        }

        Texture2D Texture(string name, int size, System.Func<int, int, Color> pixel, bool clamp = false)
        {
            var result = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name, filterMode = FilterMode.Trilinear, anisoLevel = 8,
                wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat
            };
            var colours = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) colours[y * size + x] = pixel(x, y);
            result.SetPixels32(colours);
            result.Apply(true, true);
            TextureCount++;
            Own(result);
            return result;
        }

        Texture2D PavingTexture()
        {
            const int size = 1024;
            return Texture("200 by 100 mm retained city pavers", size, (x, y) =>
            {
                float py = y / (float)size * 24;
                int row = Mathf.FloorToInt(py);
                float px = x / (float)size * 12 + (row % 2) * .5f;
                int col = Mathf.FloorToInt(px);
                float fx = px - col, fy = py - row;
                float edge = Mathf.Min(Mathf.Min(fx, 1 - fx) * .20f, Mathf.Min(fy, 1 - fy) * .10f);
                float face = Mathf.SmoothStep(0, 1, Mathf.Clamp01((edge - .0006f) / .0023f));
                float tile = Hash(col % 12, row % 24, 17);
                Color grey = new Color(.61f, .625f, .61f);
                Color warm = new Color(.65f, .60f, .515f);
                Color color = Color.Lerp(grey, warm, tile > .76f ? .58f : .08f);
                float grain = (Hash(x, y, 41) - .5f) * .048f;
                float mottling = Mathf.PerlinNoise(x * .041f, y * .041f) * .019f;
                color *= .91f + tile * .15f + grain + mottling;
                color *= Mathf.Lerp(.61f, 1, face);
                // A thin worn bevel reads as stone rather than a black grid.
                if (edge > .003f && edge < .0048f) color *= 1.045f;
                color.a = 1;
                return color;
            });
        }

        Texture2D AggregateTexture(string name, int size, bool asphalt)
        {
            return Texture(name, size, (x, y) =>
            {
                float grain = Hash(x, y, asphalt ? 83u : 52u);
                float fine = (grain - .5f) * (asphalt ? .12f : .055f);
                float broad = (Mathf.PerlinNoise(x * .025f, y * .025f) - .5f) * .055f;
                Color color = asphalt ? new Color(.32f, .335f, .35f) : new Color(.75f, .745f, .70f);
                color *= 1 + fine + broad;
                if (grain > .988f) color *= asphalt ? 1.18f : .89f;
                return color;
            });
        }

        Texture2D CoverTexture()
        {
            const int size = 256;
            return Texture("Utility cover diagonal non letter hatch", size, (x, y) =>
            {
                float a = Mathf.Repeat(x + y, 32), b = Mathf.Repeat(x - y, 32);
                float line = Mathf.Min(Mathf.Min(a, 32 - a), Mathf.Min(b, 32 - b));
                float value = line < 2 ? .48f : line < 4 ? .82f : .67f;
                value += (Hash(x, y, 69) - .5f) * .06f;
                return new Color(value, value * 1.01f, value * 1.025f, 1);
            });
        }

        Texture2D TactileTexture(bool dots)
        {
            const int size = 256;
            return Texture(dots ? "Geometric tactile dots" : "Geometric tactile ribs", size, (x, y) =>
            {
                float gx = x / (float)size * (dots ? 5 : 4), gy = y / (float)size * 5;
                float dx = Mathf.Abs(gx - Mathf.Floor(gx) - .5f), dy = Mathf.Abs(gy - Mathf.Floor(gy) - .5f);
                float distance = dots ? Mathf.Sqrt(dx * dx + dy * dy) : dx;
                float value = distance < .22f ? 1.04f : distance < .29f ? .72f : .90f;
                if (x < 3 || y < 3 || x > size - 4 || y > size - 4) value *= .78f;
                value += (Hash(x, y, 97) - .5f) * .035f;
                return new Color(value, value, value, 1);
            });
        }

        Texture2D PaintTexture()
        {
            const int size = 512;
            return Texture("Worn lane paint flakes", size, (x, y) =>
            {
                float flake = Mathf.PerlinNoise(x * .088f, y * .088f);
                float micro = Hash(x, y, 101);
                float alpha = flake < .32f || micro < .035f ? 0 : 1;
                float value = .88f + micro * .10f;
                return new Color(value, value, value, alpha);
            });
        }

        Texture2D DustTexture()
        {
            const int size = 256;
            return Texture("Soft edge deposited construction dust", size, (x, y) =>
            {
                float u = x / (size - 1f), v = y / (size - 1f);
                float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1-u), Mathf.Min(v, 1-v)) * 7);
                float noise = Mathf.PerlinNoise(u * 7.5f + 1.3f, v * 9.3f + 2.7f);
                float alpha = Mathf.SmoothStep(0, 1, edge) * Mathf.SmoothStep(.25f, .80f, noise);
                return new Color(.91f, .88f, .80f, alpha);
            }, true);
        }

        static float Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }

        Batch For(Material material)
        {
            if (!batches.TryGetValue(material, out var batch)) batches.Add(material, batch = new Batch());
            return batch;
        }

        void RectSurface(Material material, Rect rect, float height, float repeat, bool rotate = false, bool fit = false)
        {
            var points = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin) };
            var batch = For(material);
            int start = batch.vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                Vector2 point = points[i];
                batch.vertices.Add(new Vector3(point.x, height, point.y));
                Vector2 uv = fit ? new Vector2((point.x - rect.xMin) / rect.width, (point.y - rect.yMin) / rect.height) : point / repeat;
                batch.uv.Add(rotate ? new Vector2(uv.y + .217f, -uv.x + .391f) : uv);
            }
            QuadIndices(batch, start);
        }

        void Line(Material material, Vector2 a, Vector2 b, float width, float height)
            => Ribbon(material, new[] { a, b }, width, height);

        void Ribbon(Material material, IList<Vector2> points, float width, float height)
        {
            var batch = For(material);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 before = points[i] - points[Mathf.Max(0, i - 1)];
                Vector2 segment = points[i + 1] - points[i];
                Vector2 after = points[Mathf.Min(points.Count - 1, i + 2)] - points[i + 1];
                Vector2 startDirection = (before.normalized + segment.normalized).normalized;
                Vector2 endDirection = (after.normalized + segment.normalized).normalized;
                Vector2 normalA = new Vector2(-startDirection.y, startDirection.x) * width * .5f;
                Vector2 normalB = new Vector2(-endDirection.y, endDirection.x) * width * .5f;
                Vector2[] p = { points[i] + normalA, points[i + 1] + normalB, points[i + 1] - normalB, points[i] - normalA };
                int start = batch.vertices.Count;
                foreach (var point in p)
                {
                    batch.vertices.Add(new Vector3(point.x, height, point.y));
                    batch.uv.Add(point / 1.8f);
                }
                QuadIndices(batch, start);
            }
        }

        void Disk(Material material, Vector2 centre, float radius, float height, int sides)
        {
            var batch = For(material);
            int start = batch.vertices.Count;
            batch.vertices.Add(new Vector3(centre.x, height, centre.y));
            batch.uv.Add(centre / .55f);
            for (int i = 0; i <= sides; i++)
            {
                float angle = -i * Mathf.PI * 2 / sides;
                Vector2 p = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                batch.vertices.Add(new Vector3(p.x, height, p.y));
                batch.uv.Add(p / .55f);
                if (i < sides) { batch.triangles.Add(start); batch.triangles.Add(start + i + 1); batch.triangles.Add(start + i + 2); }
            }
        }

        static void QuadIndices(Batch batch, int start)
        {
            batch.triangles.Add(start); batch.triangles.Add(start + 1); batch.triangles.Add(start + 2);
            batch.triangles.Add(start); batch.triangles.Add(start + 2); batch.triangles.Add(start + 3);
        }

        void Finish()
        {
            foreach (var pair in batches)
            {
                var data = pair.Value;
                var mesh = new Mesh { name = pair.Key.name + " combined surface" };
                if (data.vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(data.vertices);
                mesh.SetUVs(0, data.uv);
                mesh.SetTriangles(data.triangles, 0);
                var normals = new Vector3[data.vertices.Count];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
                mesh.normals = normals;
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                Own(mesh);
                var go = new GameObject(pair.Key.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                SurfaceMeshCount++;
                SurfaceTriangleCount += data.triangles.Count / 3;
            }
            batches.Clear();
        }

        /// <summary>Optional companion asphalt for the outside road. Callers own the returned material and texture.</summary>
        public static Material CreateRoadMaterial(List<Object> owned)
        {
            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { name = "Quiet reclaimed city asphalt", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float grain = Hash(x, y, 119);
                    float v = .23f + (grain - .5f) * .055f + (Mathf.PerlinNoise(x * .022f, y * .022f) - .5f) * .035f;
                    if (grain > .991f) v += .065f;
                    pixels[y * size + x] = new Color(v * .96f, v, v * 1.045f, 1);
                }
            texture.SetPixels32(pixels); texture.Apply(true, true);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Repaired city outer asphalt" };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .12f);
            material.SetTextureScale("_BaseMap", new Vector2(30, 27.5f));
            owned?.Add(texture); owned?.Add(material);
            return material;
        }

        void OnDestroy()
        {
            // The lobby normally owns these resources; standalone preview construction can pass null safely.
            foreach (var item in localOwned) if (item) Destroy(item);
            localOwned.Clear();
        }
    }
}
