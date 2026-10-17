using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Forest
{
    /// <summary>Brief torn-earth crater, ballistic Meshy clods, fine debris and a soft expanding dust plume.</summary>
    public sealed class ForestWormSoilBurst : MonoBehaviour
    {
        struct Clod
        {
            public Transform transform;
            public Vector3 velocity, spin, scale;
            public bool landed;
        }
        readonly List<Object> owned = new List<Object>();
        Clod[] clods;
        Transform crater;
        float age, radius;

        public static void Spawn(Vector3 point, float radius, Transform owner)
        {
            var go = new GameObject("Moonworm — torn earth");
            go.transform.SetParent(owner, true);
            go.transform.position = point + Vector3.up * .02f;
            go.AddComponent<ForestWormSoilBurst>().Build(radius);
        }

        void Build(float size)
        {
            radius = Mathf.Max(.3f, size);
            var earth = LiminalMonsterKit.Lit(new Color(.2f, .12f, .065f), .16f);
            var moss = LiminalMonsterKit.Lit(new Color(.24f, .3f, .12f), .2f);
            var dark = LiminalMonsterKit.Lit(new Color(.045f, .029f, .018f), 0);
            owned.Add(earth); owned.Add(moss); owned.Add(dark);
            crater = new GameObject("Ripped turf and dark opening").transform;
            crater.SetParent(transform, false);
            const int segments = 32;
            var v = new Vector3[segments * 3 + 1];
            var uv = new Vector2[v.Length];
            var faces = new List<int>();
            var holeFaces = new List<int>();
            int center = v.Length - 1;
            v[center] = Vector3.up * .006f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float uneven = 1 + Mathf.Sin(i * 17.1f) * .1f;
                v[i * 3] = direction * (radius * .51f * uneven) + Vector3.up * .012f;
                v[i * 3 + 1] = direction * (radius * .82f * uneven) + Vector3.up * (.19f + .1f * Mathf.Sin(i * 8.2f));
                v[i * 3 + 2] = direction * (radius * 1.05f * uneven);
                for (int band = 0; band < 2; band++)
                {
                    int a = i * 3 + band, b = ((i + 1) % segments) * 3 + band;
                    faces.Add(a); faces.Add(b); faces.Add(a + 1);
                    faces.Add(a + 1); faces.Add(b); faces.Add(b + 1);
                }
                holeFaces.Add(center); holeFaces.Add(((i + 1) % segments) * 3); holeFaces.Add(i * 3);
            }
            var mesh = new Mesh { name = "Moonworm torn turf" };
            mesh.vertices = v; mesh.uv = uv; mesh.subMeshCount = 2;
            mesh.SetTriangles(faces, 0); mesh.SetTriangles(holeFaces, 1); mesh.RecalculateNormals();
            owned.Add(mesh);
            crater.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            crater.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { earth, dark };
            var source = Resources.Load<GameObject>("ForestMonsters/EarthClod");
            clods = new Clod[12];
            for (int i = 0; i < clods.Length; i++)
            {
                GameObject go;
                if (source && i < 9) go = Instantiate(source, transform);
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.SetParent(transform, false);
                    go.GetComponent<Renderer>().sharedMaterial = i % 3 == 0 ? moss : earth;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }
                go.name = "Ejected earth clod " + (i + 1);
                float angle = i * 2.399963f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                go.transform.localPosition = outward * (radius * .28f) + Vector3.up * .06f;
                go.transform.localRotation = Random.rotation;
                float scale = i < 9 ? Random.Range(.18f, .34f) : Random.Range(.055f, .11f);
                // Resource clods are authored at one metre; preserve their silhouette while varying the size.
                go.transform.localScale = new Vector3(scale, scale * Random.Range(.6f, 1), scale * Random.Range(.8f, 1.2f));
                clods[i] = new Clod
                {
                    transform = go.transform, velocity = outward * Random.Range(1.4f, 3.2f) + Vector3.up * Random.Range(3.8f, 6.5f),
                    spin = Random.onUnitSphere * Random.Range(120f, 400f), scale = go.transform.localScale
                };
            }
            BuildDust();
        }

        void BuildDust()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Soft brown soil dust", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float u = (x - 31.5f) / 31.5f, w = (y - 31.5f) / 31.5f;
                    float soft = Mathf.Pow(Mathf.Clamp01(1 - u * u - w * w), 2.3f);
                    float noise = Mathf.PerlinNoise(x * .16f, y * .16f);
                    pixels[y * 64 + x] = new Color(1, 1, 1, soft * (.5f + noise * .5f));
                }
            texture.SetPixels(pixels); texture.Apply(); owned.Add(texture);
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", Color.white);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.renderQueue = 3000;
            owned.Add(material);
            var go = new GameObject("Earth dust plume"); go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false; main.duration = 1; main.startLifetime = new ParticleSystem.MinMaxCurve(.65f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.7f, 2.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.24f, .55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.3f, .22f, .12f, .5f), new Color(.52f, .4f, .24f, .45f));
            main.gravityModifier = .08f; main.maxParticles = 42; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 32) });
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 62; shape.radius = radius * .58f;
            var color = system.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .08f), new GradientAlphaKey(.5f, .6f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .35f, 1, 2.2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            system.Play();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age > 3.5f) { Destroy(gameObject); return; }
            float fade = 1 - Mathf.SmoothStep(0, 1, (age - 2.4f) / 1.1f);
            if (crater) { crater.localScale = new Vector3(1, fade, 1); crater.localPosition = Vector3.down * ((1 - fade) * .15f); }
            if (clods == null) return;
            for (int i = 0; i < clods.Length; i++)
            {
                var c = clods[i];
                if (!c.transform) continue;
                if (!c.landed)
                {
                    c.velocity += Vector3.down * (13f * dt);
                    Vector3 before = c.transform.position;
                    Vector3 step = c.velocity * dt;
                    if (step.sqrMagnitude > .000001f && Physics.Raycast(before, step.normalized, out var hit,
                            step.magnitude + .04f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > .5f)
                    {
                        c.transform.position = hit.point + Vector3.up * .035f;
                        c.landed = true;
                    }
                    else c.transform.position += step;
                    if (c.transform.localPosition.y < .04f) { var p = c.transform.localPosition; p.y = .04f; c.transform.localPosition = p; c.landed = true; }
                    c.transform.Rotate(c.spin * dt, Space.World);
                }
                c.transform.localScale = c.scale * fade;
                clods[i] = c;
            }
        }

        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }
    }
}
