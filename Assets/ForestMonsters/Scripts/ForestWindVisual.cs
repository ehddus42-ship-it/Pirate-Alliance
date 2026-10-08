using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Forest
{
    /// <summary>Three counter-rotating translucent ribbons, six fine wakes and orbiting leaf motes.</summary>
    public sealed class ForestWindVisual : MonoBehaviour
    {
        const int Segments = 56;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        readonly List<LineRenderer> wisps = new List<LineRenderer>();
        readonly List<Transform> leaves = new List<Transform>();
        readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        readonly Color[] colors = new Color[(Segments + 1) * 2];
        readonly Vector3[] linePoints = new Vector3[25];
        float age;
        public float Age => age;

        void Awake()
        {
            for (int layer = 0; layer < 3; layer++)
            {
                var mesh = new Mesh { name = "Forest wind spiral " + layer }; mesh.MarkDynamic();
                var uv = new Vector2[vertices.Length]; var triangles = new int[Segments * 6];
                for (int i = 0; i <= Segments; i++)
                {
                    uv[i * 2] = new Vector2(i / (float)Segments, 0); uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1);
                    colors[i * 2] = colors[i * 2 + 1] = Color.white;
                    if (i == Segments) continue;
                    int t = i * 6, v = i * 2;
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles;
                var go = new GameObject("Spiral ribbon " + layer); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                var tint = layer == 1 ? new Color(.72f, .94f, 1, .36f) : new Color(.29f, .92f, .63f, .47f);
                var material = ForestVfx.Material("Wind ribbon " + layer, tint); materials.Add(material);
                renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
                meshes.Add(mesh);
            }
            var lineMaterial = ForestVfx.Material("Pearl wind strands", new Color(.72f, 1, .94f, .75f)); materials.Add(lineMaterial);
            for (int i = 0; i < 6; i++)
            {
                var line = ForestVfx.Line(transform, "Fine spiral " + i, lineMaterial, .028f + (i % 2) * .017f, 25);
                line.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.35f, 1), new Keyframe(.7f, .65f), new Keyframe(1, 0));
                wisps.Add(line);
            }
            var leafMaterial = ForestVfx.Material("Orbiting leaf flecks", new Color(.68f, .9f, .24f, .95f)); materials.Add(leafMaterial);
            var leafMesh = ForestVfx.LeafMesh(); meshes.Add(leafMesh);
            for (int i = 0; i < 14; i++)
            {
                var leaf = new GameObject("Wind carried leaf " + i); leaf.transform.SetParent(transform, false);
                leaf.AddComponent<MeshFilter>().sharedMesh = leafMesh; leaf.AddComponent<MeshRenderer>().sharedMaterial = leafMaterial;
                leaf.transform.localScale = Vector3.one * (.12f + i % 3 * .055f); leaves.Add(leaf.transform);
            }
            ForestVfx.Motes(transform, Vector3.zero, new Color(.64f, 1, .8f, .45f), 12, 2f, true);
            Animate();
        }

        void Update() { if (Time.deltaTime <= 0) return; age += Time.deltaTime; Animate(); }

        void Animate()
        {
            for (int layer = 0; layer < 3; layer++)
            {
                for (int i = 0; i <= Segments; i++)
                {
                    float u = i / (float)Segments;
                    float angle = u * Mathf.PI * (4.5f + layer * .5f) + age * (layer == 1 ? -7.8f : 9.6f) + layer * 2.1f;
                    float radius = .17f + u * (.49f + layer * .07f) + .045f * Mathf.Sin(u * 18 - age * 7);
                    float y = u * 1.85f - .72f;
                    float width = (.09f + .16f * u) * Mathf.Sin(Mathf.PI * u);
                    Vector3 center = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    vertices[i * 2] = center - Vector3.up * width; vertices[i * 2 + 1] = center + Vector3.up * width;
                    float alpha = Mathf.Sin(Mathf.PI * u);
                    colors[i * 2] = colors[i * 2 + 1] = new Color(1, 1, 1, alpha);
                }
                meshes[layer].vertices = vertices; meshes[layer].colors = colors;
                meshes[layer].bounds = new Bounds(Vector3.zero, new Vector3(2, 3, 2));
            }
            for (int i = 0; i < wisps.Count; i++)
            {
                for (int j = 0; j < linePoints.Length; j++)
                {
                    float u = j / (float)(linePoints.Length - 1), angle = u * 5.4f + age * (7 + i * .4f) + i * 1.047f;
                    float r = .33f + u * .41f;
                    linePoints[j] = new Vector3(Mathf.Cos(angle) * r, -.65f + u * 1.7f, Mathf.Sin(angle) * r);
                }
                wisps[i].SetPositions(linePoints);
            }
            for (int i = 0; i < leaves.Count; i++)
            {
                float u = Mathf.Repeat(age * .43f + i / 14f, 1), angle = age * (5 + i % 3) + i * 2.4f;
                float r = .4f + u * .42f;
                leaves[i].localPosition = new Vector3(Mathf.Cos(angle) * r, u * 1.9f - .7f, Mathf.Sin(angle) * r);
                leaves[i].localRotation = Quaternion.Euler(age * 160 + i * 45, -angle * Mathf.Rad2Deg, age * 310);
            }
        }

        void OnDestroy() { foreach (var mesh in meshes) if (mesh) Destroy(mesh); foreach (var material in materials) if (material) Destroy(material); }
    }

    internal static class ForestVfx
    {
        internal static Material Material(string name, Color color)
        {
            var shader = Resources.Load<Shader>("ForestMonsters/ForestEthereal");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color); return material;
        }

        internal static LineRenderer Line(Transform parent, string name, Material material, float width, int points)
        {
            var go = new GameObject(name) { layer = 2 }; go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = points;
            line.widthMultiplier = width; line.sharedMaterial = material; line.numCapVertices = 2; line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = line.endColor = Color.white; return line;
        }

        internal static Mesh LeafMesh()
        {
            var mesh = new Mesh { name = "Four triangle pointed forest leaf" };
            mesh.vertices = new[] { new Vector3(0, 0, -.65f), new Vector3(-.34f, 0, 0), new Vector3(0, .12f, .6f), new Vector3(.34f, 0, 0), new Vector3(0, .12f, 0) };
            mesh.uv = new[] { new Vector2(0, .5f), new Vector2(.5f, 0), new Vector2(1, .5f), new Vector2(.5f, 1), new Vector2(.5f, .5f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 }; mesh.RecalculateNormals(); return mesh;
        }

        internal static void Motes(Transform parent, Vector3 local, Color color, int count, float radius, bool loop)
        {
            var go = new GameObject(loop ? "Swirling pollen motes" : "Pollen burst"); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            var particles = go.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.loop = loop; main.duration = .8f; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, radius * 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(.04f, .17f);
            main.startColor = color; main.maxParticles = loop ? 40 : count; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission; emission.enabled = loop; emission.rateOverTime = count;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = loop ? .4f : radius * .12f;
            var velocity = particles.velocityOverLifetime; velocity.enabled = loop; velocity.orbitalY = 4; velocity.y = .6f;
            var size = particles.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .2f), new Keyframe(.15f, 1), new Keyframe(1, 0)));
            var renderer = particles.GetComponent<ParticleSystemRenderer>(); var material = Material("Forest particle shimmer", Color.white);
            material.SetFloat("_Roundness", 1); renderer.sharedMaterial = material;
            var lifetime = go.AddComponent<ForestEffectLifetime>(); lifetime.ownedMaterial = material; lifetime.seconds = loop ? -1 : 1.6f;
            particles.Play(); if (!loop) particles.Emit(count);
        }

        internal static void Burst(Transform room, Vector3 point, Color color, float radius, int count = 45)
        {
            var go = new GameObject("Forest attack impact"); go.transform.SetParent(room, false); go.transform.position = point;
            var lifetime = go.AddComponent<ForestEffectLifetime>(); lifetime.seconds = 1.5f;
            var material = Material("Expanding forest impact ring", color); lifetime.ownedMaterial = material;
            var ring = Line(go.transform, "Ground pressure ring", material, .09f, 49); lifetime.ring = ring;
            lifetime.radius = radius;
            for (int i = 0; i < 49; i++) { float a = i / 48f * Mathf.PI * 2; ring.SetPosition(i, new Vector3(Mathf.Cos(a), .05f, Mathf.Sin(a))); }
            Motes(go.transform, Vector3.up * .35f, color, count, radius, false);
        }
    }
}
