using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A row of pixel-font "0"/"1" quads in one mesh, using the binary glyph atlas. Each glyph has its own
    /// position, size, digit and color. It is used for the binary projectile and the monitor screens.
    /// </summary>
    public sealed class GlyphStrip
    {
        public readonly Mesh mesh;
        readonly Vector3[] vertices;
        readonly Vector2[] uvs;
        readonly Color[] colors;
        public readonly int count;

        public GlyphStrip(int glyphs, string name)
        {
            count = glyphs;
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            vertices = new Vector3[glyphs * 4];
            uvs = new Vector2[glyphs * 4];
            colors = new Color[glyphs * 4];
            var triangles = new int[glyphs * 6];
            for (int i = 0; i < glyphs; i++)
            {
                int v = i * 4, t = i * 6;
                // The glyph material draws both sides, so the strip reads whichever way it tumbles.
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
        }

        /// <summary>Places glyph `i` centered at `center` spanning `right` (width) and `up` (height) half-axes.</summary>
        public void Set(int i, Vector3 center, Vector3 right, Vector3 up, int digit, Color color)
        {
            int v = i * 4;
            vertices[v] = center - right - up;
            vertices[v + 1] = center + right - up;
            vertices[v + 2] = center + right + up;
            vertices[v + 3] = center - right + up;
            float u0 = digit == 0 ? 0 : .5f, u1 = u0 + .5f;
            uvs[v] = new Vector2(u0, 0); uvs[v + 1] = new Vector2(u1, 0); uvs[v + 2] = new Vector2(u1, 1); uvs[v + 3] = new Vector2(u0, 1);
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
        }

        public void Apply()
        {
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }
    }
}
