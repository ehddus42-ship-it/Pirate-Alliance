using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.GameTheme
{
    public enum TetrominoShape { I, O, T, L, S, Z }

    /// <summary>Four separated, bevelled cells: the silhouettes remain legible while spinning.</summary>
    public static class TetrominoVisual
    {
        static Mesh cellMesh;
        static readonly Material[] Materials = new Material[6];
        static readonly Vector2[][] Cells =
        {
            new[] { new Vector2(-1.5f, 0), new Vector2(-.5f, 0), new Vector2(.5f, 0), new Vector2(1.5f, 0) },
            new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(-.5f, .5f), new Vector2(.5f, .5f) },
            new[] { new Vector2(-1, -.25f), new Vector2(0, -.25f), new Vector2(1, -.25f), new Vector2(0, .75f) },
            new[] { new Vector2(-1, -.25f), new Vector2(0, -.25f), new Vector2(1, -.25f), new Vector2(1, .75f) },
            new[] { new Vector2(-1, -.5f), new Vector2(0, -.5f), new Vector2(0, .5f), new Vector2(1, .5f) },
            new[] { new Vector2(-1, .5f), new Vector2(0, .5f), new Vector2(0, -.5f), new Vector2(1, -.5f) }
        };
        static readonly Color[] Colors =
        {
            new Color(.04f, .9f, 1), new Color(1, .8f, .04f), new Color(.73f, .14f, 1),
            new Color(1, .37f, .04f), new Color(.2f, .94f, .16f), new Color(1, .08f, .13f)
        };

        /// <summary>Creates a centred shape in the local XY plane. Geometry/materials are shared.</summary>
        public static Transform Create(Transform parent, TetrominoShape shape, float cellSize)
        {
            int index = Mathf.Clamp((int)shape, 0, Cells.Length - 1);
            var root = new GameObject(shape + " Block").transform;
            root.SetParent(parent, false);
            if (!cellMesh) cellMesh = BuildCell();
            var material = GetMaterial(index);
            for (int i = 0; i < 4; i++)
            {
                var cell = new GameObject("Cell " + (i + 1));
                cell.transform.SetParent(root, false);
                cell.transform.localPosition = (Vector3)Cells[index][i] * cellSize;
                cell.transform.localScale = Vector3.one * (cellSize * .94f);
                cell.AddComponent<MeshFilter>().sharedMesh = cellMesh;
                var renderer = cell.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            return root;
        }

        static Material GetMaterial(int index)
        {
            if (Materials[index]) return Materials[index];
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = "Tetromino " + (TetrominoShape)index, enableInstancing = true };
            material.SetColor("_BaseColor", Colors[index]);
            material.SetColor("_Color", Colors[index]);
            material.SetFloat("_Metallic", .12f);
            material.SetFloat("_Smoothness", .55f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Colors[index] * .22f);
            Materials[index] = material;
            return material;
        }

        static Mesh BuildCell()
        {
            const float outer = .5f, inner = .415f, bevel = outer - inner;
            var vertices = new List<Vector3>(96);
            var normals = new List<Vector3>(96);
            var triangles = new List<int>(132);
            for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int a = (axis + 1) % 3, b = (axis + 2) % 3;
                    var face = new Vector3[4];
                    for (int n = 0; n < 4; n++)
                    {
                        face[n][axis] = outer * sign;
                        face[n][a] = inner * (n == 0 || n == 3 ? -1 : 1);
                        face[n][b] = inner * (n < 2 ? -1 : 1);
                    }
                    var normal = Vector3.zero; normal[axis] = sign;
                    Face(face, normal, vertices, normals, triangles);
                }
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    for (int sa = -1; sa <= 1; sa += 2)
                        for (int sb = -1; sb <= 1; sb += 2)
                        {
                            int d = 3 - a - b;
                            var face = new Vector3[4];
                            for (int n = 0; n < 4; n++)
                            {
                                bool high = n == 0 || n == 3;
                                face[n][a] = (high ? outer : inner) * sa;
                                face[n][b] = (high ? inner : outer) * sb;
                                face[n][d] = inner * (n < 2 ? -1 : 1);
                            }
                            var normal = Vector3.zero; normal[a] = sa; normal[b] = sb;
                            Face(face, normal.normalized, vertices, normals, triangles);
                        }
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var sign = new Vector3(x, y, z);
                        var point = sign * inner;
                        Face(new[] { point + Vector3.right * (x * bevel), point + Vector3.up * (y * bevel),
                            point + Vector3.forward * (z * bevel) }, sign.normalized, vertices, normals, triangles);
                    }
            var mesh = new Mesh { name = "Shared Bevelled Tetromino Cell" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Face(Vector3[] points, Vector3 normal, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0)
                System.Array.Reverse(points);
            int start = vertices.Count;
            foreach (var point in points) { vertices.Add(point); normals.Add(normal); }
            for (int i = 1; i < points.Length - 1; i++)
            { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }
    }
}
