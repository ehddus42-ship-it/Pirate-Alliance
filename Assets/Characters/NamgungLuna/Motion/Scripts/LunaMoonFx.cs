using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 이동 연출: 발끝이 닿을 때 바닥에 퍼지는 달빛 물결, 발밑의 은은한 빛, 질주 시작의 빛 폭발과 반짝임,
    /// 질주 중 뒤로 남는 빛의 꼬리.
    /// Moonlight effects driven by <see cref="LunaMoonGait"/> events: a ripple ring on the ground at every toe touch,
    /// a soft glow under the hovering feet, a sparkle burst and light flash when a sprint starts, and a light trail
    /// plus sparkles while sprinting. Objects are built at runtime from the assigned materials.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public class LunaMoonFx : MonoBehaviour
    {
        public LunaMoonGait gait;
        public Material rippleMaterial, glowMaterial, trailMaterial, sparkleMaterial;
        [ColorUsage(false, true)] public Color moonlight = new Color(0.55f, 0.8f, 1.6f, 1f);

        [Header("Ripple")]
        public float rippleRadius = 0.55f;
        public float rippleDuration = 0.75f;
        [Tooltip("물결이 놓일 바닥을 찾는 레이어")]
        public LayerMask groundMask = ~0;

        [Header("Trail / sparkles")]
        [Tooltip("빛 꼬리 위치 (Visual local, 치맛단 뒤)")]
        public Vector3 trailOffset = new Vector3(0f, 0.34f, -0.1f);
        public float trailWidth = 0.42f;
        public float trailTime = 0.3f;
        [Tooltip("질주 중 1m 당 반짝임 수")]
        public float sparklesPerMetre = 7f;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Mesh quad;

        class Ripple
        {
            public Transform transform;
            public MeshRenderer renderer;
            public float age, duration, strength;
            public bool active;
        }

        readonly List<Ripple> ripples = new List<Ripple>();
        MaterialPropertyBlock block;
        Transform fxRoot, glow;
        MeshRenderer glowRenderer;
        TrailRenderer trail;
        ParticleSystem sparkles;
        Light flash;
        float flashLevel, glowLevel;

        public int RipplesSpawned { get; private set; }
        public int ActiveRipples { get { int n = 0; foreach (var r in ripples) if (r.active) n++; return n; } }

        void Awake()
        {
            if (gait == null) gait = GetComponent<LunaMoonGait>();
            block = new MaterialPropertyBlock();
            // ripples stay where they were made, so they live outside the moving character
            fxRoot = new GameObject(name + "_MoonFx").transform;
            BuildGlow();
            BuildTrail();
            BuildSparkles();
            BuildFlash();
        }

        void OnEnable()
        {
            if (gait != null) gait.GaitEvent += OnGaitEvent;
            if (fxRoot != null) fxRoot.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (gait != null) gait.GaitEvent -= OnGaitEvent;
            if (fxRoot != null) fxRoot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (fxRoot != null) Destroy(fxRoot.gameObject);
        }

        void OnGaitEvent(LunaGaitEvent e)
        {
            switch (e.kind)
            {
                case LunaGaitEventKind.Step:
                    SpawnRipple(e.position, rippleRadius * Mathf.Lerp(0.6f, 1.1f, e.strength), e.strength);
                    if (e.sprint > 0.3f) Burst(e.position + Vector3.up * 0.1f, Mathf.RoundToInt(6 * e.sprint), 0.12f, 0.9f);
                    break;
                case LunaGaitEventKind.Settle:
                    SpawnRipple(e.position, rippleRadius * 0.8f, e.strength);
                    break;
                case LunaGaitEventKind.Jump:
                    SpawnRipple(e.position, rippleRadius * 1.1f, e.strength);
                    Burst(e.position + Vector3.up * 0.15f, 10, 0.2f, 1.2f);
                    break;
                case LunaGaitEventKind.Land:
                    SpawnRipple(e.position, rippleRadius * 1.5f, e.strength);
                    Burst(e.position + Vector3.up * 0.1f, 14, 0.3f, 1.6f);
                    flashLevel = Mathf.Max(flashLevel, 0.6f);
                    break;
                case LunaGaitEventKind.SprintStart:
                    SpawnRipple(e.position, rippleRadius * 2.0f, 1.3f);
                    Burst(transform.position + Vector3.up * 0.8f, 30, 0.45f, 3.0f);
                    flashLevel = 1f;
                    break;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < ripples.Count; i++)
            {
                Ripple r = ripples[i];
                if (!r.active) continue;
                r.age += dt;
                float t = r.age / r.duration;
                if (t >= 1f)
                {
                    r.active = false;
                    r.renderer.enabled = false;
                    continue;
                }
                block.SetFloat(ProgressId, t);
                block.SetFloat(StrengthId, r.strength);
                block.SetColor(ColorId, moonlight);
                r.renderer.SetPropertyBlock(block);
            }

            if (gait == null) return;
            // soft glow under the hovering feet: strongest at rest and while sprinting, gone in the air
            float targetGlow = Mathf.Lerp(0.55f, 0.3f, gait.MoveWeight) + 0.5f * gait.SprintWeight;
            targetGlow *= 1f - gait.AirWeight;
            glowLevel = Mathf.Lerp(glowLevel, targetGlow, 1f - Mathf.Exp(-6f * dt));
            if (glow != null)
            {
                Vector3 ground = transform.TransformPoint(new Vector3(0f, gait.groundLevel + 0.012f, 0f));
                glow.SetPositionAndRotation(ground, Quaternion.Euler(90f, transform.eulerAngles.y, 0f));
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.4f);
                block.Clear();
                block.SetColor(ColorId, moonlight * (glowLevel * pulse * 0.6f));
                glowRenderer.SetPropertyBlock(block);
                glowRenderer.enabled = glowLevel > 0.01f;
            }

            if (trail != null) trail.emitting = gait.SprintWeight > 0.35f && gait.Speed > 3f;
            if (sparkles != null)
            {
                var emission = sparkles.emission;
                emission.rateOverDistanceMultiplier = sparklesPerMetre * gait.SprintWeight + 1.2f * gait.RunWeight * gait.MoveWeight;
            }
            if (flash != null)
            {
                flashLevel = Mathf.MoveTowards(flashLevel, 0f, dt * 3f);
                flash.intensity = 2.5f * flashLevel * flashLevel;
                flash.enabled = flashLevel > 0.01f;
            }
        }

        void SpawnRipple(Vector3 position, float radius, float strength)
        {
            if (rippleMaterial == null) return;
            Ripple r = null;
            foreach (var candidate in ripples) if (!candidate.active) { r = candidate; break; }
            if (r == null)
            {
                var go = new GameObject("MoonRipple");
                go.transform.SetParent(fxRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = rippleMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                r = new Ripple { transform = go.transform, renderer = renderer };
                ripples.Add(r);
            }
            // lay it on whatever floor is under the toe
            if (Physics.Raycast(position + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.2f, groundMask, QueryTriggerInteraction.Ignore))
                position = hit.point;
            r.transform.SetPositionAndRotation(position + Vector3.up * 0.015f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            r.transform.localScale = Vector3.one * (radius * 2f);
            r.age = 0f;
            r.duration = rippleDuration * Mathf.Lerp(0.8f, 1.25f, Mathf.Clamp01(strength - 0.3f));
            r.strength = strength;
            r.active = true;
            r.renderer.enabled = true;
            RipplesSpawned++;
        }

        void Burst(Vector3 position, int count, float radius, float speed)
        {
            if (sparkles == null || count <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.1f;
                p.position = position + dir * (radius * Random.value);
                p.velocity = dir.normalized * (speed * Random.Range(0.4f, 1f));
                p.startSize = Random.Range(0.03f, 0.08f);
                p.startLifetime = Random.Range(0.45f, 0.95f);
                sparkles.Emit(p, 1);
            }
        }

        void BuildGlow()
        {
            if (glowMaterial == null) return;
            var go = new GameObject("MoonGlow");
            go.transform.SetParent(fxRoot, false);
            go.transform.localScale = Vector3.one * 0.95f;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            glowRenderer = go.AddComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = glowMaterial;
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            glow = go.transform;
        }

        void BuildTrail()
        {
            if (trailMaterial == null || gait == null || gait.visual == null) return;
            var go = new GameObject("MoonTrail");
            go.transform.SetParent(gait.visual, false);
            go.transform.localPosition = trailOffset;
            trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = trailTime;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.75f), new Keyframe(1f, 0f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.8f, 1f), 0.4f), new GradientColorKey(new Color(0.35f, 0.5f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.45f, 0.4f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
        }

        void BuildSparkles()
        {
            if (sparkleMaterial == null || gait == null || gait.visual == null) return;
            var go = new GameObject("MoonSparkles");
            go.transform.SetParent(gait.visual, false);
            go.transform.localPosition = new Vector3(0f, 0.45f, -0.05f);
            sparkles = go.AddComponent<ParticleSystem>();
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparkles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(0.65f, 0.82f, 1f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 400;
            var emission = sparkles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            var shape = sparkles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var color = sparkles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(fade);
            var size = sparkles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.2f)));
            var velocity = sparkles.limitVelocityOverLifetime;
            velocity.enabled = true;
            velocity.limit = 0.5f;
            velocity.dampen = 0.12f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sparkles.Play();
        }

        void BuildFlash()
        {
            if (gait == null || gait.visual == null) return;
            var go = new GameObject("MoonFlash");
            go.transform.SetParent(gait.visual, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(0.62f, 0.8f, 1f);
            flash.range = 3.5f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "LunaMoonQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            return quad;
        }
    }
}
