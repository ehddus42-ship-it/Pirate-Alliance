using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A blue fire gate: irregular upward flame tongues around a deep opening, two soft lights and rising sparks.
    /// Geometry is batched once; the shader receives a scaled clock, and all animation resumes from its paused state.
    /// </summary>
    public sealed class BlueFireGate : MonoBehaviour
    {
        const int ParticleLimit = 128;
        static readonly int GateTime = Shader.PropertyToID("_GateTime");
        static readonly Vector3 LocalCentre = new Vector3(0, 3.7f, 0);
        readonly List<Material> clockMaterials = new List<Material>(5);
        ParticleSystem sparks;
        Light upperLight, lowerLight;
        float elapsed, emissionFraction;
        uint randomState = 0x6C78B9D1u;

        public float Elapsed => elapsed;
        public int FlameRibbonCount { get; private set; }
        public int MeshCount { get; private set; }
        public int LightCount => (upperLight ? 1 : 0) + (lowerLight ? 1 : 0);
        public int ParticleCount => sparks ? sparks.particleCount : 0;
        public int MaxParticleCount => ParticleLimit;
        public bool IsActive => isActiveAndEnabled;
        public Vector3 Center => transform.TransformPoint(LocalCentre);

        public static BlueFireGate Build(Transform gate, List<Object> owned)
        {
            var root = new GameObject("BlueFireGate");
            root.transform.SetParent(gate, false);
            var fire = root.AddComponent<BlueFireGate>();
            fire.Construct(owned);
            return fire;
        }

        void Construct(List<Object> owned)
        {
            var shader = Shader.Find("PirateAlliance/BlueFireGate");
            if (!shader)
            {
                Debug.LogError("Blue fire gate shader is missing. Import Resources/LiminalLobby/BlueFireGate.shader.", this);
                return;
            }
            var outer = Material(shader, "Deep blue outer fire", 0, 1.55f, .47f, true, owned);
            var inner = Material(shader, "White hot flame roots", 0, 1.85f, .65f, true, owned);
            var core = Material(shader, "Deep gate opening", 1, .82f, 1, false, owned);
            var glow = Material(shader, "Blue fire ground bounce", 2, 1.10f, .42f, true, owned);
            var ember = Material(shader, "Rising blue sparks", 0, 2.1f, .92f, true, owned);
            core.SetFloat("_Seed", .173f); outer.SetFloat("_Seed", .617f); inner.SetFloat("_Seed", .293f);
            outer.SetFloat("_EdgeSoftness", 1.15f); inner.SetFloat("_EdgeSoftness", .8f);
            outer.SetFloat("_Sway", .14f); inner.SetFloat("_Sway", .07f); ember.SetFloat("_Sway", 0);

            // The opening lies just behind the fire. A second dark oval is unnecessary and would flatten its depth.
            var opening = new Geometry();
            opening.Quad(LocalCentre + new Vector3(-2.76f, -2.83f, .18f), LocalCentre + new Vector3(2.76f, -2.83f, .18f),
                LocalCentre + new Vector3(2.76f, 2.83f, .18f), LocalCentre + new Vector3(-2.76f, 2.83f, .18f), Color.white, .173f);
            AddMesh("Dark opening and internal heat", opening, core, owned);

            var outerFlames = new Geometry();
            var hotRoots = new Geometry();
            // Overlapping broad roots form one burning edge. Long tongues break out of it rather than reading as a row of candles.
            // Alternate shallow crossed planes and depth offsets so the rim keeps volume from the isometric gameplay view.
            for (int i = 0; i < 64; i++)
            {
                float seed = Hash(i * 3 + 73);
                float angle = (i + (Hash(i + 97) - .5f) * .64f) * Mathf.PI * 2 / 64;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                float bulge = Mathf.Sin(angle * 5.0f + .7f) * .07f + Mathf.Sin(angle * 9.0f) * .028f;
                var origin = LocalCentre + new Vector3(sin * (2.75f + bulge), cos * (2.74f + bulge) - .085f,
                    -.05f + (Hash(i + 41) - .5f) * .34f);
                float length = Mathf.Lerp(.58f, 1.68f, Hash(i + 17));
                length = Mathf.Min(length, 7.10f + Hash(i + 577) * .34f - origin.y);
                float width = Mathf.Lerp(.50f, .75f, Hash(i + 203));
                float lean = sin * (.11f + .27f * Hash(i + 13)) + (Hash(i + 521) - .5f) * .24f;
                float yaw = (i % 2 == 0 ? -1 : 1) * Mathf.Lerp(12, 27, Hash(i + 419));
                Ribbon(outerFlames, origin, length, width, lean, .23f, seed,
                    new Color(.12f, .38f, 1f, Mathf.Lerp(.58f, .88f, Hash(i + 181))), 12, yaw, .17f);
            }
            for (int i = 0; i < 80; i++)
            {
                float seed = Hash(i * 5 + 31);
                float angle = (i + (Hash(i + 251) - .5f) * .64f) * Mathf.PI * 2 / 80;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                var origin = LocalCentre + new Vector3(sin * (2.65f + Mathf.Sin(angle * 7 + 2) * .025f), cos * 2.68f - .045f,
                    -.21f + (Hash(i + 319) - .5f) * .25f);
                // Most of the pale heat lives close to the edge; a few longer strands tie it into the blue body.
                float length = Mathf.Lerp(.23f, .60f, Hash(i + 101));
                if (i % 7 == 0) length += .23f;
                length = Mathf.Min(length, 7.24f - origin.y);
                float yaw = (i % 2 == 0 ? 1 : -1) * Mathf.Lerp(9, 23, Hash(i + 443));
                Ribbon(hotRoots, origin, length, Mathf.Lerp(.24f, .35f, Hash(i + 67)), sin * .105f, .105f, seed,
                    new Color(.48f, .87f, 1f, Mathf.Lerp(.50f, .79f, Hash(i + 379))), 8, yaw, .09f);
            }
            AddMesh("Ascending cobalt flame tongues", outerFlames, outer, owned);
            AddMesh("Broken white blue fire rim", hotRoots, inner, owned);

            // Low transparent glow follows the pavement and does not add any collision to the gate approach.
            var ground = new Geometry();
            ground.Quad(new Vector3(-4.15f, .034f, -3.0f), new Vector3(4.15f, .034f, -3.0f),
                new Vector3(4.15f, .034f, 1.85f), new Vector3(-4.15f, .034f, 1.85f), new Color(.16f, .44f, 1f, .85f), .41f);
            AddMesh("Soft ground reflected fire", ground, glow, owned);
            upperLight = PointLight("Blue flame light", new Vector3(0, 3.65f, -.65f), new Color(.12f, .43f, 1f), 2.35f, 7.2f);
            lowerLight = PointLight("Pavement blue bounce", new Vector3(0, .85f, -1.0f), new Color(.20f, .59f, 1f), 1.25f, 4.9f);
            BuildSparks(ember);
            SetShaderTime();
        }

        Material Material(Shader shader, string label, float mode, float intensity, float opacity, bool additive, List<Object> owned)
        {
            var material = new Material(shader) { name = "Blue gate / " + label };
            material.SetFloat("_Mode", mode); material.SetFloat("_Intensity", intensity); material.SetFloat("_Opacity", opacity);
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_NoiseScale", 1); material.SetFloat("_EdgeSoftness", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.renderQueue = mode == 1 ? 3000 : mode == 2 ? 3001 : 3002;
            owned.Add(material); clockMaterials.Add(material); return material;
        }

        void Ribbon(Geometry geometry, Vector3 origin, float length, float width, float lean, float sway, float seed, Color color, int segments,
            float yaw, float depthCurl)
        {
            FlameRibbonCount++;
            var widthAxis = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1) / (float)segments;
                var p = FlamePoint(origin, length, lean, sway, seed, a, depthCurl);
                var q = FlamePoint(origin, length, lean, sway, seed, b, depthCurl);
                float aw = width * (.78f + .18f * Mathf.Sin(a * Mathf.PI)) * Mathf.Pow(1 - a, .54f);
                float bw = width * (.78f + .18f * Mathf.Sin(b * Mathf.PI)) * Mathf.Pow(1 - b, .54f);
                geometry.Strip(p - widthAxis * aw, p + widthAxis * aw, q + widthAxis * bw, q - widthAxis * bw,
                    a, b, color, seed);
            }
        }
        static Vector3 FlamePoint(Vector3 origin, float length, float lean, float sway, float seed, float t, float depthCurl)
        {
            float curl = Mathf.Sin(t * (3.8f + seed * 3.1f) + seed * 6.28f) * sway * t;
            return origin + new Vector3(lean * t * t + curl, length * t, Mathf.Sin(t * 3.14f + seed * 5) * t * depthCurl);
        }
        void AddMesh(string name, Geometry geometry, Material material, List<Object> owned)
        {
            var mesh = new Mesh { name = "Blue gate / " + name };
            mesh.SetVertices(geometry.vertices); mesh.SetColors(geometry.colors); mesh.SetUVs(0, geometry.uv); mesh.SetUVs(1, geometry.seed);
            mesh.SetTriangles(geometry.triangles, 0); mesh.RecalculateBounds();
            // Small room for shader heat distortion, without inflating culling bounds across the whole plaza.
            var bounds = mesh.bounds; bounds.Expand(.55f); mesh.bounds = bounds;
            owned.Add(mesh);
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            MeshCount++;
        }
        Light PointLight(string name, Vector3 localPosition, Color color, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.color = color; light.intensity = intensity;
            light.range = range; light.shadows = LightShadows.None; return light;
        }
        void BuildSparks(Material material)
        {
            var go = new GameObject("Rising blue fire sparks"); go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>(); sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = true; main.playOnAwake = false; main.useUnscaledTime = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = ParticleLimit; main.startLifetime = 1.15f; main.startSpeed = 0; main.startSize = .045f;
            main.gravityModifier = -.035f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = sparks.emission; emission.enabled = false;
            var shape = sparks.shape; shape.enabled = false;
            var size = sparks.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(.16f, 1), new Keyframe(1, 0)));
            var color = sparks.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.34f, .83f, 1), 0), new GradientColorKey(new Color(.06f, .28f, 1), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.9f, .12f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .12f; renderer.lengthScale = 2.8f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            sparks.Play(true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || clockMaterials.Count == 0) return;
            elapsed += dt; SetShaderTime();
            // Two incommensurate, low amplitude pulses read as reflected fire rather than a rotating neon ring.
            float flicker = Mathf.Sin(elapsed * 7.3f) * .12f + Mathf.Sin(elapsed * 11.7f + .8f) * .055f;
            if (upperLight) upperLight.intensity = 2.35f + flicker;
            if (lowerLight) lowerLight.intensity = 1.25f + Mathf.Sin(elapsed * 5.1f + 2) * .10f;
            if (!sparks) return;
            emissionFraction += dt * 34;
            int count = Mathf.Min(16, Mathf.FloorToInt(emissionFraction)); emissionFraction -= count;
            for (int i = 0; i < count; i++)
            {
                float angle = Next() * Mathf.PI * 2;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                var emit = new ParticleSystem.EmitParams
                {
                    position = LocalCentre + new Vector3(sin * 2.70f, cos * 2.67f, -.26f + Next() * .25f),
                    velocity = new Vector3(sin * (.08f + Next() * .15f), .62f + Next() * 1.08f, -.12f + Next() * .19f),
                    startLifetime = .65f + Next() * .85f,
                    startSize = .026f + Next() * .032f,
                    startColor = new Color(.35f, .78f, 1, .8f)
                };
                sparks.Emit(emit, 1);
            }
        }
        void SetShaderTime()
        {
            for (int i = 0; i < clockMaterials.Count; i++) clockMaterials[i].SetFloat(GateTime, elapsed);
        }
        float Next()
        {
            randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5;
            return (randomState & 0x00FFFFFF) / 16777216f;
        }
        static float Hash(int index)
        {
            uint value = (uint)index * 747796405u + 2891336453u;
            value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
            value = (value >> 22) ^ value;
            return (value & 0x00FFFFFF) / 16777216f;
        }
        void OnDisable() { if (sparks) sparks.Pause(true); }
        void OnEnable() { if (sparks) sparks.Play(true); }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<Vector2> uv = new List<Vector2>(), seed = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float value)
                => Strip(a, b, c, d, 0, 1, color, value);
            public void Strip(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float from, float to, Color color, float value)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                uv.Add(new Vector2(0, from)); uv.Add(new Vector2(1, from)); uv.Add(new Vector2(1, to)); uv.Add(new Vector2(0, to));
                for (int i = 0; i < 4; i++) seed.Add(new Vector2(value, 0));
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
                triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
            }
        }
    }
}
